using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Services;
using Microsoft.Win32;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
    private readonly CoffeeMoviePaths _paths;
    private readonly MovieLibraryStore _libraryStore;
    private readonly ObservableCollection<MovieListItem> _movies = [];
    private Movie? _selectedMovie;
    private bool _isUpdatingSelection;

    public MainWindow()
    {
        _paths = new CoffeeMoviePaths();
        _paths.EnsureCreated();
        _libraryStore = new MovieLibraryStore(_paths);

        InitializeComponent();
        MoviesListBox.ItemsSource = _movies;
        Loaded += async (_, _) => await RefreshMoviesAsync();
        SetDetailsEnabled(false);
    }

    private async void OnImportVideoClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "動画ファイルを選択",
            Filter = "Video files|*.mp4;*.m4v;*.mov;*.webm;*.mkv;*.avi|All files|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var movie = await ImportVideoAsync(dialog.FileName);
            await RefreshMoviesAsync(movie.Id);
            SetStatus($"動画を追加しました: {movie.Title}");
        }
        catch (Exception ex)
        {
            ShowError("動画の取り込みに失敗しました", ex);
        }
    }

    private async void OnImportSubtitleClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "字幕ファイルを選択",
            Filter = "Subtitle files|*.srt;*.vtt|All files|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var track = await ImportSubtitleAsync(_selectedMovie, dialog.FileName);
            await RefreshMoviesAsync(_selectedMovie.Id);
            SetStatus($"字幕を追加しました: {track.Label} ({track.CueCount} cues)");
        }
        catch (Exception ex)
        {
            ShowError("字幕の取り込みに失敗しました", ex);
        }
    }

    private async void OnWriteSidecarClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie is null)
        {
            return;
        }

        var defaultFileName = Path.GetFileNameWithoutExtension(_selectedMovie.Video.FileName) + ".coffeemovie.json";
        var dialog = new SaveFileDialog
        {
            Title = "サイドカーを書き出し",
            FileName = defaultFileName,
            Filter = "CoffeeMovie sidecar|*.coffeemovie.json|JSON|*.json|All files|*.*"
        };

        if (!string.IsNullOrWhiteSpace(_selectedMovie.Video.CachePath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_selectedMovie.Video.CachePath);
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await CoffeeMovieSidecarService.WriteAsync(_selectedMovie, dialog.FileName);
            SetStatus($"サイドカーを書き出しました: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            ShowError("サイドカーの書き出しに失敗しました", ex);
        }
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await RefreshMoviesAsync(_selectedMovie?.Id);
    }

    private async void OnTitleLostFocus(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie is null || _isUpdatingSelection)
        {
            return;
        }

        var title = TitleTextBox.Text.Trim();
        if (title.Length == 0 || string.Equals(title, _selectedMovie.Title, StringComparison.Ordinal))
        {
            return;
        }

        _selectedMovie.Title = title;
        await _libraryStore.UpsertMovieAsync(_selectedMovie);
        await RefreshMoviesAsync(_selectedMovie.Id);
        SetStatus("タイトルを保存しました。");
    }

    private async void OnMovieSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (MoviesListBox.SelectedItem is not MovieListItem item)
        {
            _selectedMovie = null;
            RenderMovieDetails(null);
            return;
        }

        var library = await _libraryStore.LoadAsync();
        _selectedMovie = library.Movies.FirstOrDefault(movie => string.Equals(movie.Id, item.MovieId, StringComparison.Ordinal));
        RenderMovieDetails(_selectedMovie);
    }

    private void OnPlayPreviewClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie?.Video.CachePath is null || !File.Exists(_selectedMovie.Video.CachePath))
        {
            SetStatus("プレビューできる動画ファイルがありません。");
            return;
        }

        PreviewPlayer.Source = new Uri(_selectedMovie.Video.CachePath);
        PreviewPlayer.Play();
        SetStatus("プレビュー再生中です。");
    }

    private void OnStopPreviewClicked(object sender, RoutedEventArgs e)
    {
        PreviewPlayer.Stop();
        SetStatus("プレビューを停止しました。");
    }

    private async Task<Movie> ImportVideoAsync(string sourcePath)
    {
        var movieId = MovieId.New();
        var sourceFileName = Path.GetFileName(sourcePath);
        var safeFileName = SanitizeFileName(sourceFileName);
        var movieDirectory = _paths.GetMovieVideoDirectory(movieId);
        Directory.CreateDirectory(movieDirectory);

        var targetPath = EnsureUniquePath(Path.Combine(movieDirectory, safeFileName));
        await using (var input = File.OpenRead(sourcePath))
        await using (var output = File.Create(targetPath))
        {
            await input.CopyToAsync(output);
        }

        var fileInfo = new FileInfo(targetPath);
        var movie = new Movie
        {
            Id = movieId,
            Title = Path.GetFileNameWithoutExtension(sourceFileName),
            Video = new VideoAsset
            {
                SourceKind = VideoSourceKind.LocalFile,
                SourceUri = sourcePath,
                SourceKey = $"local:{movieId}",
                FileName = safeFileName,
                ContentType = GuessVideoContentType(safeFileName),
                SizeBytes = fileInfo.Length,
                ModifiedAt = fileInfo.LastWriteTimeUtc,
                CachePath = targetPath
            }
        };

        await _libraryStore.UpsertMovieAsync(movie);
        return movie;
    }

    private async Task<SubtitleTrack> ImportSubtitleAsync(Movie movie, string sourcePath)
    {
        var sourceFileName = Path.GetFileName(sourcePath);
        var safeFileName = SanitizeFileName(sourceFileName);
        var subtitleDirectory = _paths.GetMovieSubtitleDirectory(movie.Id);
        Directory.CreateDirectory(subtitleDirectory);

        var content = await File.ReadAllTextAsync(sourcePath, Encoding.UTF8);
        var document = SubtitleParser.Parse(content, sourceFileName);
        if (document.Cues.Count == 0)
        {
            throw new InvalidOperationException("字幕キューが見つかりませんでした。SRT または WebVTT の形式を確認してください。");
        }

        var originalPath = EnsureUniquePath(Path.Combine(subtitleDirectory, safeFileName));
        await File.WriteAllTextAsync(originalPath, content, Encoding.UTF8);

        var vttPath = Path.Combine(subtitleDirectory, Path.GetFileNameWithoutExtension(safeFileName) + ".vtt");
        await File.WriteAllTextAsync(vttPath, SubtitleParser.ToWebVtt(document.Cues), Encoding.UTF8);

        var track = new SubtitleTrack
        {
            Label = Path.GetFileNameWithoutExtension(sourceFileName),
            Language = "ja",
            Format = document.Format,
            SourceFileName = sourceFileName,
            LocalPath = originalPath,
            VttCachePath = vttPath,
            CueCount = document.Cues.Count,
            Cues = document.Cues
        };

        movie.SubtitleTracks.RemoveAll(existing =>
            string.Equals(existing.SourceFileName, track.SourceFileName, StringComparison.OrdinalIgnoreCase));
        movie.SubtitleTracks.Add(track);
        movie.SceneMarkers = SubtitleSceneFactory.CreateSceneMarkers(track, maxMarkers: 1000);
        await _libraryStore.UpsertMovieAsync(movie);
        return track;
    }

    private async Task RefreshMoviesAsync(string? selectedMovieId = null)
    {
        var library = await _libraryStore.LoadAsync();
        var movies = library.Movies
            .OrderByDescending(movie => movie.UpdatedAt)
            .ToList();

        _movies.Clear();
        foreach (var movie in movies)
        {
            _movies.Add(new MovieListItem(movie));
        }

        SummaryTextBlock.Text = $"{_movies.Count} movies";

        var selectedItem = !string.IsNullOrWhiteSpace(selectedMovieId)
            ? _movies.FirstOrDefault(item => string.Equals(item.MovieId, selectedMovieId, StringComparison.Ordinal))
            : _movies.FirstOrDefault();

        MoviesListBox.SelectedItem = selectedItem;
        if (selectedItem is null)
        {
            _selectedMovie = null;
            RenderMovieDetails(null);
        }
    }

    private void RenderMovieDetails(Movie? movie)
    {
        _isUpdatingSelection = true;
        try
        {
            SetDetailsEnabled(movie is not null);

            if (movie is null)
            {
                TitleTextBox.Text = string.Empty;
                FileNameTextBlock.Text = "動画を追加してください";
                CachePathTextBlock.Text = string.Empty;
                SizeTextBlock.Text = string.Empty;
                SubtitlesDataGrid.ItemsSource = null;
                ScenesDataGrid.ItemsSource = null;
                return;
            }

            TitleTextBox.Text = movie.Title;
            FileNameTextBlock.Text = movie.Video.FileName;
            CachePathTextBlock.Text = movie.Video.CachePath ?? movie.Video.SourceUri;
            SizeTextBlock.Text = $"{FormatBytes(movie.Video.SizeBytes)} / {movie.SubtitleTracks.Count} subtitle / {movie.SceneMarkers.Count} scene";
            SubtitlesDataGrid.ItemsSource = movie.SubtitleTracks
                .Select(track => new SubtitleRow(track))
                .ToList();
            ScenesDataGrid.ItemsSource = movie.SceneMarkers
                .Select(marker => new SceneRow(marker))
                .ToList();
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void SetDetailsEnabled(bool enabled)
    {
        TitleTextBox.IsEnabled = enabled;
        AddSubtitleButton.IsEnabled = enabled;
        WriteSidecarButton.IsEnabled = enabled;
        PlayButton.IsEnabled = enabled;
        StopButton.IsEnabled = enabled;
        SubtitlesDataGrid.IsEnabled = enabled;
        ScenesDataGrid.IsEnabled = enabled;
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }

    private void ShowError(string title, Exception exception)
    {
        SetStatus(exception.Message);
        MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string EnsureUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, $"{name}-{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"保存先ファイル名を決定できませんでした: {path}");
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(fileName.Length);
        foreach (var character in fileName)
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        var sanitized = builder.ToString().Trim();
        return sanitized.Length == 0 ? "movie" : sanitized;
    }

    private static string GuessVideoContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".m4v" => "video/x-m4v",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            _ => "video/mp4"
        };
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private static string FormatTimestamp(TimeSpan value)
    {
        return value.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{value.Minutes:00}:{value.Seconds:00}");
    }

    private sealed class MovieListItem
    {
        public MovieListItem(Movie movie)
        {
            MovieId = movie.Id;
            Title = movie.Title;
            Detail = $"{movie.SubtitleTracks.Count} subtitle / {movie.SceneMarkers.Count} scene";
            CacheState = movie.Video.HasLocalCache ? "cached" : "not cached";
        }

        public string MovieId { get; }

        public string Title { get; }

        public string Detail { get; }

        public string CacheState { get; }
    }

    private sealed class SubtitleRow
    {
        public SubtitleRow(SubtitleTrack track)
        {
            Label = track.Label;
            Format = track.Format.ToString();
            CueCount = track.CueCount;
        }

        public string Label { get; }

        public string Format { get; }

        public int CueCount { get; }
    }

    private sealed class SceneRow
    {
        public SceneRow(SceneMarker marker)
        {
            Timestamp = FormatTimestamp(marker.Start);
            Label = marker.Label;
        }

        public string Timestamp { get; }

        public string Label { get; }
    }
}

