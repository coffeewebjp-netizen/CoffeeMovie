using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Models;
using CoffeeMovie.Storage.Services;
using Microsoft.Win32;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
    private const string FlagTagName = "flag";

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".m4v",
        ".mov",
        ".webm",
        ".mkv",
        ".avi"
    };

    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt",
        ".vtt"
    };

    private readonly CoffeeMoviePaths _paths;
    private readonly MovieLibraryStore _libraryStore;
    private readonly ObservableCollection<MovieListItem> _movies = [];
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Movie? _selectedMovie;
    private SubtitleTrack? _previewSubtitleTrack;
    private SubtitleCue? _currentPreviewCue;
    private string _subtitleTagHighlightColor = "#F6C945";
    private bool _showDualSubtitles;
    private TimeSpan _previewDuration = TimeSpan.Zero;
    private TimeSpan? _pendingPreviewSeek;
    private bool _isPreviewMediaOpened;
    private bool _playPreviewWhenMediaOpened;
    private bool _isPreviewSeeking;
    private TimeSpan _fullPreviewDuration = TimeSpan.Zero;
    private TimeSpan? _pendingFullPreviewSeek;
    private bool _isFullPreviewMediaOpened;
    private bool _playFullPreviewWhenMediaOpened;
    private bool _isFullPreviewSeeking;
    private bool _isUpdatingFullPreviewSlider;
    private bool _isSubtitleGenerationRunning;
    private bool _isUpdatingSelection;
    private bool _isUpdatingPreferences;
    private bool _isUpdatingPreviewSlider;

    public MainWindow()
    {
        _paths = new CoffeeMoviePaths();
        _paths.EnsureCreated();
        _libraryStore = new MovieLibraryStore(_paths);

        InitializeComponent();
        MoviesListBox.ItemsSource = _movies;
        _previewTimer.Tick += (_, _) =>
        {
            UpdatePreviewSeekFromPlayer();
            UpdateFullPreviewSeekFromPlayer();
        };
        PreviewSeekSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnPreviewSeekDragStarted));
        PreviewSeekSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnPreviewSeekDragCompleted));
        PreviewSeekSlider.LostMouseCapture += OnPreviewSeekLostMouseCapture;
        FullPreviewSeekSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnFullPreviewSeekDragStarted));
        FullPreviewSeekSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnFullPreviewSeekDragCompleted));
        FullPreviewSeekSlider.LostMouseCapture += OnFullPreviewSeekLostMouseCapture;
        Loaded += async (_, _) => await RefreshMoviesAsync();
        ResetPreviewSeek();
        ResetFullPreviewSeek();
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

    private async void OnRemoveSubtitleClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie is null || SubtitlesDataGrid.SelectedItem is not SubtitleRow row)
        {
            SetStatus("削除する字幕を選択してください。");
            return;
        }

        var track = _selectedMovie.SubtitleTracks
            .FirstOrDefault(candidate => string.Equals(candidate.Id, row.TrackId, StringComparison.Ordinal));
        if (track is null)
        {
            SetStatus("削除する字幕が見つかりませんでした。");
            return;
        }

        var result = MessageBox.Show(
            this,
            $"「{track.Label}」をこの動画から削除しますか？",
            "字幕を削除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _selectedMovie.SubtitleTracks.Remove(track);
            DeleteCachedSubtitleFiles(_selectedMovie.Id, track);
            RefreshMovieSceneMarkers(_selectedMovie);
            await _libraryStore.UpsertMovieAsync(_selectedMovie);
            await RefreshMoviesAsync(_selectedMovie.Id);
            SetStatus($"字幕を削除しました: {track.Label}");
        }
        catch (Exception ex)
        {
            ShowError("字幕の削除に失敗しました", ex);
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

    private async void OnGenerateEnglishSubtitleClicked(object sender, RoutedEventArgs e)
    {
        if (_isSubtitleGenerationRunning)
        {
            return;
        }

        if (_selectedMovie is null)
        {
            SetStatus("字幕を生成する動画を選択してください。");
            return;
        }

        try
        {
            _isSubtitleGenerationRunning = true;
            SetSubtitleGenerationEnabled(false);
            SubtitleGenerationLogTextBox.Clear();
            AppendSubtitleGenerationLog("WhisperX subtitle generation started.");

            var generatedPath = await GenerateEnglishSubtitleAsync(_selectedMovie);
            AppendSubtitleGenerationLog($"Importing generated subtitle: {generatedPath}");
            var track = await ImportSubtitleAsync(_selectedMovie, generatedPath);
            await RefreshMoviesAsync(_selectedMovie.Id);
            SetStatus($"英語字幕を生成して取り込みました: {track.CueCount} cues");
            AppendSubtitleGenerationLog($"Done: {track.CueCount} cues imported.");
        }
        catch (Exception ex)
        {
            ShowError("英語字幕の生成に失敗しました", ex);
            AppendSubtitleGenerationLog("ERROR: " + ex.Message);
        }
        finally
        {
            _isSubtitleGenerationRunning = false;
            SetSubtitleGenerationEnabled(_selectedMovie is not null);
        }
    }

    private void OnBrowseWhisperOutputDirectoryClicked(object sender, RoutedEventArgs e)
    {
        var initialDirectory = Directory.Exists(WhisperOutputDirectoryTextBox.Text)
            ? WhisperOutputDirectoryTextBox.Text
            : GetDefaultSubtitleGenerationDirectory(_selectedMovie);
        var dialog = new OpenFolderDialog
        {
            Title = "WhisperX字幕の出力先フォルダを選択",
            InitialDirectory = initialDirectory
        };

        if (dialog.ShowDialog(this) == true)
        {
            WhisperOutputDirectoryTextBox.Text = dialog.FolderName;
        }
    }

    private async void OnSaveWhisperDefaultsClicked(object sender, RoutedEventArgs e)
    {
        await SaveStudioPreferencesAsync();
        SetStatus("字幕生成の既定設定を保存しました。");
    }

    private async void OnToggleDualSubtitleClicked(object sender, RoutedEventArgs e)
    {
        _showDualSubtitles = !_showDualSubtitles;
        UpdateDualSubtitleButton();
        UpdatePreviewSubtitleAtCurrentPosition();
        await SaveStudioPreferencesAsync();
    }

    private async void OnHighlightColorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingPreferences || HighlightColorComboBox.SelectedValue is not string color)
        {
            return;
        }

        _subtitleTagHighlightColor = color;
        RenderSceneRows(_previewSubtitleTrack);
        UpdatePreviewSubtitleAtCurrentPosition();
        await SaveStudioPreferencesAsync();
    }

    private async void OnManageTagsClicked(object sender, RoutedEventArgs e)
    {
        var library = await _libraryStore.LoadAsync();
        MergeTagDefinitionsFromLibrary(library);

        var movieTags = new ObservableCollection<TagDefinitionRow>(
            library.TagDefinitions
                .Where(tag => tag.Scope == TagScope.Movie)
                .OrderBy(tag => tag.SortOrder)
                .ThenBy(tag => tag.Name)
                .Select(tag => new TagDefinitionRow(tag)));
        var subtitleTags = new ObservableCollection<TagDefinitionRow>(
            library.TagDefinitions
                .Where(tag => tag.Scope == TagScope.Subtitle)
                .OrderBy(tag => tag.SortOrder)
                .ThenBy(tag => tag.Name)
                .Select(tag => new TagDefinitionRow(tag)));

        var window = CreateTagManagerWindow(movieTags, subtitleTags);
        if (window.ShowDialog() != true)
        {
            return;
        }

        library.TagDefinitions = movieTags
            .Select((row, index) => row.ToDefinition(TagScope.Movie, index))
            .Concat(subtitleTags.Select((row, index) => row.ToDefinition(TagScope.Subtitle, index)))
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Name))
            .GroupBy(tag => (tag.Scope, NormalizedTagKey(tag.Name)))
            .Select(group => group.First())
            .ToList();

        await _libraryStore.SaveAsync(library);
        SetStatus("タグ定義を保存しました。");
    }

    private void OnWindowDragOver(object sender, System.Windows.DragEventArgs e)
    {
        var paths = GetDroppedFilePaths(e);
        var hasVideo = paths.Any(IsVideoFile);
        var hasSubtitle = paths.Any(IsSubtitleFile);

        e.Effects = hasVideo || (hasSubtitle && _selectedMovie is not null)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnWindowDrop(object sender, System.Windows.DragEventArgs e)
    {
        var paths = GetDroppedFilePaths(e).ToList();
        if (paths.Count == 0)
        {
            return;
        }

        var videoPaths = paths.Where(IsVideoFile).ToList();
        var subtitlePaths = paths.Where(IsSubtitleFile).ToList();
        var importedMovies = new List<Movie>();
        var importedSubtitleCount = 0;

        try
        {
            foreach (var videoPath in videoPaths)
            {
                importedMovies.Add(await ImportVideoAsync(videoPath));
            }

            if (subtitlePaths.Count > 0)
            {
                var subtitleTarget = importedMovies.Count == 1
                    ? importedMovies[0]
                    : _selectedMovie;
                if (subtitleTarget is null)
                {
                    SetStatus("字幕を追加する動画を選択してからドロップしてください。");
                }
                else
                {
                    foreach (var subtitlePath in subtitlePaths)
                    {
                        await ImportSubtitleAsync(subtitleTarget, subtitlePath);
                        importedSubtitleCount++;
                    }
                }
            }

            var selectedMovieId = importedMovies.LastOrDefault()?.Id ?? _selectedMovie?.Id;
            await RefreshMoviesAsync(selectedMovieId);

            if (importedMovies.Count > 0 || importedSubtitleCount > 0)
            {
                SetStatus($"ドロップから追加しました: {importedMovies.Count} video / {importedSubtitleCount} subtitle");
            }
            else if (videoPaths.Count == 0 && subtitlePaths.Count == 0)
            {
                SetStatus("対応している動画または字幕ファイルをドロップしてください。");
            }
        }
        catch (Exception ex)
        {
            ShowError("ドロップしたファイルの取り込みに失敗しました", ex);
        }
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

    private void OnSubtitleSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelection || _selectedMovie is null)
        {
            return;
        }

        if (SubtitlesDataGrid.SelectedItem is not SubtitleRow row)
        {
            _previewSubtitleTrack = null;
            RemoveSubtitleButton.IsEnabled = false;
            HidePreviewSubtitle();
            RenderSceneRows(null);
            return;
        }

        RemoveSubtitleButton.IsEnabled = true;
        _previewSubtitleTrack = _selectedMovie.SubtitleTracks
            .FirstOrDefault(track => string.Equals(track.Id, row.TrackId, StringComparison.Ordinal));
        RenderSceneRows(_previewSubtitleTrack);
        UpdatePreviewSubtitleAtCurrentPosition();
    }

    private void OnPlayPreviewClicked(object sender, RoutedEventArgs e)
    {
        StartPreview();
    }

    private void OnStopPreviewClicked(object sender, RoutedEventArgs e)
    {
        _previewTimer.Stop();
        _playPreviewWhenMediaOpened = false;
        PreviewPlayer.Stop();
        SetPreviewSeek(TimeSpan.Zero);
        HidePreviewSubtitle();
        SetStatus("プレビューを停止しました。");
    }

    private void OnPreviewSubtitleClicked(object sender, MouseButtonEventArgs e)
    {
        if (_currentPreviewCue is not null)
        {
            JumpPreviewTo(_currentPreviewCue.Start);
        }
    }

    private void OnSceneMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ScenesDataGrid.SelectedItem is SceneRow row)
        {
            JumpPreviewTo(row.Start);
        }
    }

    private async void OnSceneCurrentCellChanged(object sender, EventArgs e)
    {
        if (_isUpdatingSelection
            || _selectedMovie is null
            || _previewSubtitleTrack is null
            || ScenesDataGrid.CurrentItem is not SceneRow row)
        {
            return;
        }

        await SaveSceneRowLearningStateAsync(row);
    }

    private async void OnSceneCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_isUpdatingSelection || e.Row.Item is not SceneRow row)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        var header = e.Column.Header?.ToString();
        if (string.Equals(header, "Start", StringComparison.OrdinalIgnoreCase)
            || string.Equals(header, "End", StringComparison.OrdinalIgnoreCase))
        {
            await SaveSceneRowTimingAsync(row);
            return;
        }

        if (string.Equals(e.Column.Header?.ToString(), "Tags", StringComparison.OrdinalIgnoreCase))
        {
            row.IsFlagged = ParseTags(row.Tags).Any(IsFlagTag);
        }

        await SaveSceneRowLearningStateAsync(row);
    }

    private void OnFlaggedOnlyChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSelection)
        {
            return;
        }

        RenderSceneRows(_previewSubtitleTrack);
    }

    private void OnPlayNextFlaggedCueClicked(object sender, RoutedEventArgs e)
    {
        if (_previewSubtitleTrack is null)
        {
            SetStatus("字幕を選択してください。");
            return;
        }

        var flaggedCues = _previewSubtitleTrack.Cues
            .Where(cue => IsFlaggedLearningState(FindCueLearningState(_previewSubtitleTrack, cue)))
            .OrderBy(cue => cue.Start)
            .ToList();
        if (flaggedCues.Count == 0)
        {
            SetStatus("flagタグ付き字幕がありません。");
            return;
        }

        var currentPosition = PreviewPlayer.Source is null ? TimeSpan.Zero : PreviewPlayer.Position;
        var nextCue = flaggedCues.FirstOrDefault(cue => cue.Start > currentPosition.Add(TimeSpan.FromMilliseconds(250)))
            ?? flaggedCues[0];

        SelectSceneRow(nextCue.Id);
        JumpPreviewTo(nextCue.Start);
    }

    private async void OnShiftSelectedCueEarlierClicked(object sender, RoutedEventArgs e)
    {
        await ShiftSelectedCueTimingAsync(-1);
    }

    private async void OnShiftSelectedCueLaterClicked(object sender, RoutedEventArgs e)
    {
        await ShiftSelectedCueTimingAsync(1);
    }

    private async void OnSetSelectedCueStartFromPreviewClicked(object sender, RoutedEventArgs e)
    {
        await SetSelectedCueBoundaryFromPreviewAsync(setStart: true);
    }

    private async void OnSetSelectedCueEndFromPreviewClicked(object sender, RoutedEventArgs e)
    {
        await SetSelectedCueBoundaryFromPreviewAsync(setStart: false);
    }

    private void OnPreviewMediaOpened(object sender, RoutedEventArgs e)
    {
        _isPreviewMediaOpened = true;
        if (PreviewPlayer.NaturalDuration.HasTimeSpan)
        {
            _previewDuration = PreviewPlayer.NaturalDuration.TimeSpan;
            PreviewSeekSlider.Maximum = Math.Max(1.0, _previewDuration.TotalSeconds);
            PreviewSeekSlider.IsEnabled = _previewDuration > TimeSpan.Zero;
        }
        else
        {
            ResetPreviewSeek();
        }

        if (_pendingPreviewSeek is { } pendingPosition)
        {
            _pendingPreviewSeek = null;
            SeekPreviewTo(pendingPosition);
        }
        else
        {
            SetPreviewSeek(PreviewPlayer.Position);
        }

        if (_playPreviewWhenMediaOpened)
        {
            _playPreviewWhenMediaOpened = false;
            PreviewPlayer.Play();
            _previewTimer.Start();
            SetStatus("プレビュー再生中です。");
            return;
        }

        SetStatus("プレビューの準備ができました。");
    }

    private void OnPreviewMediaEnded(object sender, RoutedEventArgs e)
    {
        _previewTimer.Stop();
        _playPreviewWhenMediaOpened = false;
        PreviewPlayer.Stop();
        SetPreviewSeek(TimeSpan.Zero);
        SetStatus("プレビューが終了しました。");
    }

    private void OnPreviewSeekStarted(object sender, MouseButtonEventArgs e)
    {
        BeginPreviewSeek();
    }

    private void OnPreviewSeekCompleted(object sender, MouseButtonEventArgs e)
    {
        CompletePreviewSeek();
    }

    private void OnPreviewSeekDragStarted(object sender, DragStartedEventArgs e)
    {
        BeginPreviewSeek();
    }

    private void OnPreviewSeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        CompletePreviewSeek();
    }

    private void OnPreviewSeekLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isPreviewSeeking && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            CompletePreviewSeek();
        }
    }

    private void OnPreviewSeekKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (PreviewSeekSlider.IsEnabled)
        {
            SeekPreviewToSliderValue();
        }
    }

    private void OnPreviewSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingPreviewSlider)
        {
            return;
        }

        var position = TimeSpan.FromSeconds(Math.Clamp(e.NewValue, 0.0, PreviewSeekSlider.Maximum));
        PreviewPositionTextBlock.Text = FormatPlaybackPosition(position, _previewDuration);
        UpdatePreviewSubtitle(position);
        if (_isPreviewSeeking && PreviewPlayer.Source is not null && _previewDuration > TimeSpan.Zero)
        {
            PreviewPlayer.Position = ClampPreviewPosition(position);
        }
    }

    private void OnFullPreviewPlayClicked(object sender, RoutedEventArgs e)
    {
        StartFullPreview();
    }

    private void OnFullPreviewStopClicked(object sender, RoutedEventArgs e)
    {
        _playFullPreviewWhenMediaOpened = false;
        _isFullPreviewSeeking = false;
        FullPreviewPlayer.Stop();
        SetFullPreviewSeek(TimeSpan.Zero);
        HideFullPreviewSubtitle();
        SetStatus("フルプレビューを停止しました。");
    }

    private void OnFullPreviewMediaOpened(object sender, RoutedEventArgs e)
    {
        _isFullPreviewMediaOpened = true;
        if (FullPreviewPlayer.NaturalDuration.HasTimeSpan)
        {
            _fullPreviewDuration = FullPreviewPlayer.NaturalDuration.TimeSpan;
            FullPreviewSeekSlider.Maximum = Math.Max(1.0, _fullPreviewDuration.TotalSeconds);
            FullPreviewSeekSlider.IsEnabled = _fullPreviewDuration > TimeSpan.Zero;
        }
        else
        {
            ResetFullPreviewSeek();
        }

        if (_pendingFullPreviewSeek is { } pendingPosition)
        {
            _pendingFullPreviewSeek = null;
            SeekFullPreviewTo(pendingPosition);
        }
        else
        {
            SetFullPreviewSeek(FullPreviewPlayer.Position);
        }

        if (_playFullPreviewWhenMediaOpened)
        {
            _playFullPreviewWhenMediaOpened = false;
            FullPreviewPlayer.Play();
            _previewTimer.Start();
            SetStatus("フルプレビュー再生中です。");
            return;
        }

        SetStatus("フルプレビューの準備ができました。");
    }

    private void OnFullPreviewMediaEnded(object sender, RoutedEventArgs e)
    {
        _playFullPreviewWhenMediaOpened = false;
        FullPreviewPlayer.Stop();
        SetFullPreviewSeek(TimeSpan.Zero);
        SetStatus("フルプレビューが終了しました。");
    }

    private void OnFullPreviewSeekStarted(object sender, MouseButtonEventArgs e)
    {
        BeginFullPreviewSeek();
    }

    private void OnFullPreviewSeekCompleted(object sender, MouseButtonEventArgs e)
    {
        CompleteFullPreviewSeek();
    }

    private void OnFullPreviewSeekDragStarted(object sender, DragStartedEventArgs e)
    {
        BeginFullPreviewSeek();
    }

    private void OnFullPreviewSeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        CompleteFullPreviewSeek();
    }

    private void OnFullPreviewSeekLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isFullPreviewSeeking && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            CompleteFullPreviewSeek();
        }
    }

    private void OnFullPreviewSeekKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (FullPreviewSeekSlider.IsEnabled)
        {
            SeekFullPreviewToSliderValue();
        }
    }

    private void OnFullPreviewSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingFullPreviewSlider)
        {
            return;
        }

        var position = TimeSpan.FromSeconds(Math.Clamp(e.NewValue, 0.0, FullPreviewSeekSlider.Maximum));
        FullPreviewPositionTextBlock.Text = FormatPlaybackPosition(position, _fullPreviewDuration);
        UpdateFullPreviewSubtitle(position);
        if (_isFullPreviewSeeking && FullPreviewPlayer.Source is not null && _fullPreviewDuration > TimeSpan.Zero)
        {
            FullPreviewPlayer.Position = ClampFullPreviewPosition(position);
        }
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

    private async Task<string> GenerateEnglishSubtitleAsync(Movie movie)
    {
        var videoPath = ResolveGenerationVideoPath(movie);
        var outputDirectory = NormalizeOptionalText(WhisperOutputDirectoryTextBox.Text)
            ?? GetDefaultSubtitleGenerationDirectory(movie);
        Directory.CreateDirectory(outputDirectory);

        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        var generatedSrtPath = Path.Combine(outputDirectory, baseName + ".srt");
        var englishSrtPath = Path.Combine(outputDirectory, baseName + ".en.srt");
        var overwrite = OverwriteGeneratedSubtitleCheckBox.IsChecked == true;

        if (File.Exists(englishSrtPath) && !overwrite)
        {
            AppendSubtitleGenerationLog($"Existing English subtitle found: {englishSrtPath}");
            return englishSrtPath;
        }

        if (overwrite)
        {
            BackupExistingFile(englishSrtPath);
            BackupExistingFile(generatedSrtPath);
        }

        var pythonCommand = NormalizeOptionalText(WhisperPythonCommandTextBox.Text) ?? "py";
        var launcherArguments = SplitCommandLine(WhisperPythonArgumentsTextBox.Text);
        var model = NormalizeOptionalText(WhisperModelTextBox.Text) ?? "medium";
        var language = NormalizeOptionalText(WhisperLanguageTextBox.Text) ?? "en";
        var device = SelectedComboText(WhisperDeviceComboBox, "cuda");
        var computeType = SelectedComboText(WhisperComputeTypeComboBox, "float16");

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonCommand,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        foreach (var argument in launcherArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add(videoPath);
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(model);
        startInfo.ArgumentList.Add("--language");
        startInfo.ArgumentList.Add(language);
        startInfo.ArgumentList.Add("--output_format");
        startInfo.ArgumentList.Add("srt");
        startInfo.ArgumentList.Add("--output_dir");
        startInfo.ArgumentList.Add(outputDirectory);
        startInfo.ArgumentList.Add("--device");
        startInfo.ArgumentList.Add(device);
        startInfo.ArgumentList.Add("--compute_type");
        startInfo.ArgumentList.Add(computeType);
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

        AppendSubtitleGenerationLog("Command:");
        AppendSubtitleGenerationLog(FormatProcessCommand(startInfo));

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("WhisperX process could not be started.");
        }

        var outputTask = PumpProcessOutputAsync(process.StandardOutput);
        var errorTask = PumpProcessOutputAsync(process.StandardError);
        await process.WaitForExitAsync();
        await Task.WhenAll(outputTask, errorTask);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"WhisperX exited with code {process.ExitCode}.");
        }

        if (File.Exists(englishSrtPath))
        {
            return englishSrtPath;
        }

        if (File.Exists(generatedSrtPath))
        {
            File.Move(generatedSrtPath, englishSrtPath, overwrite: true);
            AppendSubtitleGenerationLog($"Renamed generated SRT: {englishSrtPath}");
            return englishSrtPath;
        }

        throw new FileNotFoundException("WhisperX completed but no SRT file was found.", generatedSrtPath);
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

        var metadata = SubtitleFileMetadataService.Infer(sourceFileName);
        var track = new SubtitleTrack
        {
            Label = metadata.Label,
            Language = metadata.Language,
            Role = metadata.Role,
            GroupKey = metadata.GroupKey,
            Format = document.Format,
            SourceUri = sourcePath,
            SourceFileName = sourceFileName,
            LocalPath = originalPath,
            VttCachePath = vttPath,
            CueCount = document.Cues.Count,
            Cues = document.Cues
        };

        movie.SubtitleTracks.RemoveAll(existing =>
            string.Equals(existing.SourceFileName, track.SourceFileName, StringComparison.OrdinalIgnoreCase));
        movie.SubtitleTracks.Add(track);
        RefreshMovieSceneMarkers(movie);
        await _libraryStore.UpsertMovieAsync(movie);
        return track;
    }

    private void DeleteCachedSubtitleFiles(string movieId, SubtitleTrack track)
    {
        DeleteCachedSubtitleFile(movieId, track.LocalPath);
        DeleteCachedSubtitleFile(movieId, track.VttCachePath);
    }

    private void DeleteCachedSubtitleFile(string movieId, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var subtitleDirectory = Path.GetFullPath(_paths.GetMovieSubtitleDirectory(movieId));
        if (!subtitleDirectory.EndsWith(Path.DirectorySeparatorChar))
        {
            subtitleDirectory += Path.DirectorySeparatorChar;
        }

        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(subtitleDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    private async Task<bool> RewriteSubtitleTrackFilesAsync(SubtitleTrack track, bool writeBackOriginal)
    {
        await WriteSubtitleFileAsync(track.LocalPath, track, FormatForPath(track.LocalPath, track.Format));

        if (!string.IsNullOrWhiteSpace(track.VttCachePath))
        {
            await WriteSubtitleFileAsync(track.VttCachePath, track, SubtitleFormat.WebVtt);
        }

        if (!writeBackOriginal
            || string.IsNullOrWhiteSpace(track.SourceUri)
            || !File.Exists(track.SourceUri)
            || (!string.IsNullOrWhiteSpace(track.LocalPath) && IsSamePath(track.SourceUri, track.LocalPath)))
        {
            return false;
        }

        await WriteSubtitleFileAsync(track.SourceUri, track, FormatForPath(track.SourceUri, track.Format));
        return true;
    }

    private static async Task WriteSubtitleFileAsync(string? path, SubtitleTrack track, SubtitleFormat format)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var content = SubtitleParser.ToSubtitleText(track.Cues, format);
        await File.WriteAllTextAsync(path, content, Encoding.UTF8);
    }

    private static SubtitleFormat FormatForPath(string? path, SubtitleFormat fallback)
    {
        var extension = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetExtension(path);
        if (extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase))
        {
            return SubtitleFormat.WebVtt;
        }

        if (extension.Equals(".srt", StringComparison.OrdinalIgnoreCase))
        {
            return SubtitleFormat.Srt;
        }

        return fallback == SubtitleFormat.Unknown ? SubtitleFormat.Srt : fallback;
    }

    private static bool IsSamePath(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static void RefreshMovieSceneMarkers(Movie movie)
    {
        var track = SelectDefaultSubtitleTrack(movie);
        movie.SceneMarkers = track is null
            ? []
            : SubtitleSceneFactory.CreateSceneMarkers(track, maxMarkers: 1000);
    }

    private static SubtitleTrack? SelectDefaultSubtitleTrack(Movie movie)
    {
        return movie.SubtitleTracks.FirstOrDefault(track =>
                track.Role == SubtitleTrackRole.LearningTarget && track.Cues.Count > 0)
            ?? movie.SubtitleTracks.LastOrDefault(track => track.Cues.Count > 0)
            ?? movie.SubtitleTracks.LastOrDefault();
    }

    private async Task RefreshMoviesAsync(string? selectedMovieId = null)
    {
        var library = await _libraryStore.LoadAsync();
        ApplyStudioPreferences(library);
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
            ResetPreviewIfMovieChanged(movie);
            SetDetailsEnabled(movie is not null);

            if (movie is null)
            {
                TitleTextBox.Text = string.Empty;
                FileNameTextBlock.Text = "動画を追加してください";
                CachePathTextBlock.Text = string.Empty;
                SizeTextBlock.Text = string.Empty;
                UpdateSubtitleGenerationPanel(null);
                _previewSubtitleTrack = null;
                SubtitlesDataGrid.ItemsSource = null;
                ScenesDataGrid.ItemsSource = null;
                HidePreviewSubtitle();
                return;
            }

            TitleTextBox.Text = movie.Title;
            FileNameTextBlock.Text = movie.Video.FileName;
            CachePathTextBlock.Text = movie.Video.CachePath ?? movie.Video.SourceUri;
            SizeTextBlock.Text = $"{FormatBytes(movie.Video.SizeBytes)} / {movie.SubtitleTracks.Count} subtitle / {movie.SceneMarkers.Count} scene";
            UpdateSubtitleGenerationPanel(movie);
            var subtitleRows = movie.SubtitleTracks
                .Select(track => new SubtitleRow(track))
                .ToList();
            _previewSubtitleTrack = SelectDefaultSubtitleTrack(movie);
            SubtitlesDataGrid.ItemsSource = subtitleRows;
            SubtitlesDataGrid.SelectedItem = subtitleRows
                .FirstOrDefault(row => string.Equals(row.TrackId, _previewSubtitleTrack?.Id, StringComparison.Ordinal));
            RemoveSubtitleButton.IsEnabled = subtitleRows.Count > 0;
            RenderSceneRows(_previewSubtitleTrack);
            UpdatePreviewSubtitleAtCurrentPosition();
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
        RemoveSubtitleButton.IsEnabled = enabled && SubtitlesDataGrid.SelectedItem is not null;
        WriteSidecarButton.IsEnabled = enabled;
        DualSubtitleButton.IsEnabled = enabled;
        PlayButton.IsEnabled = enabled;
        StopButton.IsEnabled = enabled;
        FullPreviewPlayButton.IsEnabled = enabled;
        FullPreviewStopButton.IsEnabled = enabled;
        if (!enabled)
        {
            PreviewSeekSlider.IsEnabled = false;
            FullPreviewSeekSlider.IsEnabled = false;
        }
        HighlightColorComboBox.IsEnabled = enabled;
        TimingShiftTextBox.IsEnabled = enabled;
        SyncPairedSubtitleCheckBox.IsEnabled = enabled;
        OriginalSubtitleWriteBackCheckBox.IsEnabled = enabled;
        FlaggedOnlyCheckBox.IsEnabled = enabled;
        PlayFlaggedButton.IsEnabled = enabled;
        SubtitlesDataGrid.IsEnabled = enabled;
        ScenesDataGrid.IsEnabled = enabled;
        SetSubtitleGenerationEnabled(enabled && !_isSubtitleGenerationRunning);
    }

    private void SetSubtitleGenerationEnabled(bool enabled)
    {
        WhisperPythonCommandTextBox.IsEnabled = enabled;
        WhisperPythonArgumentsTextBox.IsEnabled = enabled;
        WhisperOutputDirectoryTextBox.IsEnabled = enabled;
        WhisperModelTextBox.IsEnabled = enabled;
        WhisperLanguageTextBox.IsEnabled = enabled;
        WhisperDeviceComboBox.IsEnabled = enabled;
        WhisperComputeTypeComboBox.IsEnabled = enabled;
        OverwriteGeneratedSubtitleCheckBox.IsEnabled = enabled;
        BrowseWhisperOutputDirectoryButton.IsEnabled = enabled;
        SaveWhisperDefaultsButton.IsEnabled = enabled;
        GenerateEnglishSubtitleButton.IsEnabled = enabled;
    }

    private void UpdateSubtitleGenerationPanel(Movie? movie)
    {
        if (movie is null)
        {
            GenerationMovieTextBlock.Text = "動画を選択してください";
            return;
        }

        GenerationMovieTextBlock.Text = movie.Title;
        if (string.IsNullOrWhiteSpace(WhisperOutputDirectoryTextBox.Text))
        {
            WhisperOutputDirectoryTextBox.Text = GetDefaultSubtitleGenerationDirectory(movie);
        }
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }

    private void ApplyStudioPreferences(MovieLibrary library)
    {
        _isUpdatingPreferences = true;
        try
        {
            _subtitleTagHighlightColor = string.IsNullOrWhiteSpace(library.Studio.SubtitleTagHighlightColor)
                ? "#F6C945"
                : library.Studio.SubtitleTagHighlightColor;
            _showDualSubtitles = library.Studio.ShowDualSubtitles;
            HighlightColorComboBox.SelectedValue = _subtitleTagHighlightColor;
            WhisperOutputDirectoryTextBox.Text = library.Studio.WhisperOutputDirectory ?? string.Empty;
            WhisperPythonCommandTextBox.Text = string.IsNullOrWhiteSpace(library.Studio.WhisperPythonCommand)
                ? "py"
                : library.Studio.WhisperPythonCommand;
            WhisperPythonArgumentsTextBox.Text = string.IsNullOrWhiteSpace(library.Studio.WhisperPythonArguments)
                ? "-3.10 -m whisperx"
                : library.Studio.WhisperPythonArguments;
            WhisperModelTextBox.Text = string.IsNullOrWhiteSpace(library.Studio.WhisperModel)
                ? "medium"
                : library.Studio.WhisperModel;
            WhisperLanguageTextBox.Text = string.IsNullOrWhiteSpace(library.Studio.WhisperLanguage)
                ? "en"
                : library.Studio.WhisperLanguage;
            SelectComboBoxItem(WhisperDeviceComboBox, library.Studio.WhisperDevice, "cuda");
            SelectComboBoxItem(WhisperComputeTypeComboBox, library.Studio.WhisperComputeType, "float16");
            UpdateDualSubtitleButton();
        }
        finally
        {
            _isUpdatingPreferences = false;
        }
    }

    private async Task SaveStudioPreferencesAsync()
    {
        var library = await _libraryStore.LoadAsync();
        library.Studio.SubtitleTagHighlightColor = _subtitleTagHighlightColor;
        library.Studio.ShowDualSubtitles = _showDualSubtitles;
        library.Studio.WhisperOutputDirectory = NormalizeOptionalText(WhisperOutputDirectoryTextBox.Text);
        library.Studio.WhisperPythonCommand = NormalizeOptionalText(WhisperPythonCommandTextBox.Text) ?? "py";
        library.Studio.WhisperPythonArguments = NormalizeOptionalText(WhisperPythonArgumentsTextBox.Text) ?? "-3.10 -m whisperx";
        library.Studio.WhisperModel = NormalizeOptionalText(WhisperModelTextBox.Text) ?? "medium";
        library.Studio.WhisperLanguage = NormalizeOptionalText(WhisperLanguageTextBox.Text) ?? "en";
        library.Studio.WhisperDevice = SelectedComboText(WhisperDeviceComboBox, "cuda");
        library.Studio.WhisperComputeType = SelectedComboText(WhisperComputeTypeComboBox, "float16");
        await _libraryStore.SaveAsync(library);
    }

    private void UpdateDualSubtitleButton()
    {
        DualSubtitleButton.Content = _showDualSubtitles ? "両方表示: ON" : "両方表示: OFF";
        DualSubtitleButton.Background = _showDualSubtitles
            ? FindResource("AccentBrush") as System.Windows.Media.Brush
            : new SolidColorBrush(Color.FromRgb(0x12, 0x1A, 0x26));
        DualSubtitleButton.Foreground = _showDualSubtitles
            ? new SolidColorBrush(Color.FromRgb(0x04, 0x10, 0x0F))
            : Brushes.White;
    }

    private void ShowError(string title, Exception exception)
    {
        SetStatus(exception.Message);
        MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void RenderSceneRows(SubtitleTrack? subtitleTrack)
    {
        if (subtitleTrack is null)
        {
            ScenesDataGrid.ItemsSource = null;
            return;
        }

        var rows = subtitleTrack.Cues
            .Where(cue => !string.IsNullOrWhiteSpace(cue.Text))
            .Take(1000)
            .Select(cue =>
            {
                var learningState = FindCueLearningState(subtitleTrack, cue);
                return new SceneRow(cue, learningState, CreateSceneRowBackground(learningState, _subtitleTagHighlightColor));
            })
            .ToList();

        if (FlaggedOnlyCheckBox.IsChecked == true)
        {
            rows = rows.Where(row => row.IsFlagged).ToList();
        }

        ScenesDataGrid.ItemsSource = rows;
    }

    private async Task SaveSceneRowLearningStateAsync(SceneRow row)
    {
        if (_selectedMovie is null || _previewSubtitleTrack is null)
        {
            return;
        }

        var tags = ParseTags(row.Tags);
        if (row.IsFlagged)
        {
            AddTag(tags, FlagTagName);
        }
        else
        {
            tags.RemoveAll(tag => IsFlagTag(tag));
        }

        var note = NormalizeOptionalText(row.Note);
        var state = FindCueLearningState(_previewSubtitleTrack, row.CueId, row.CueIndex);
        if (state is null && !row.IsFlagged && tags.Count == 0 && note is null)
        {
            return;
        }

        state ??= EnsureCueLearningState(_previewSubtitleTrack, row.CueId, row.CueIndex);
        var isDirty = state.IsFlagged != row.IsFlagged
            || !state.Tags.SequenceEqual(tags, StringComparer.OrdinalIgnoreCase)
            || !string.Equals(state.Note, note, StringComparison.Ordinal);
        if (!isDirty)
        {
            return;
        }

        state.IsFlagged = tags.Any(IsFlagTag);
        state.Tags = tags;
        state.Note = note;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        row.IsFlagged = state.IsFlagged;
        row.Tags = string.Join(", ", tags);
        row.Note = note ?? string.Empty;

        await _libraryStore.UpsertMovieAsync(_selectedMovie);
        if (FlaggedOnlyCheckBox.IsChecked == true && !row.IsFlagged)
        {
            RenderSceneRows(_previewSubtitleTrack);
        }

        SetStatus("字幕の学習メタデータを保存しました。");
    }

    private async Task ShiftSelectedCueTimingAsync(int direction)
    {
        if (_selectedMovie is null || _previewSubtitleTrack is null || ScenesDataGrid.SelectedItem is not SceneRow row)
        {
            SetStatus("タイミングを調整する字幕行を選択してください。");
            return;
        }

        if (!TryGetTimingShiftMilliseconds(out var milliseconds))
        {
            SetStatus("タイミング補正値は 1 以上のミリ秒で入力してください。");
            return;
        }

        var offset = TimeSpan.FromMilliseconds(direction * milliseconds);
        var targetTracks = GetTimingShiftTargetTracks(_selectedMovie, _previewSubtitleTrack).ToList();
        var changedTracks = new List<SubtitleTrack>();
        var originalWriteCount = 0;
        TimeSpan? selectedStart = null;
        TimeSpan? selectedEnd = null;

        foreach (var track in targetTracks)
        {
            var cue = string.Equals(track.Id, _previewSubtitleTrack.Id, StringComparison.Ordinal)
                ? track.Cues.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, row.CueId, StringComparison.Ordinal)
                    || candidate.Index == row.CueIndex)
                : track.Cues.FirstOrDefault(candidate => candidate.Index == row.CueIndex);
            if (cue is null)
            {
                continue;
            }

            ShiftCue(cue, offset);
            if (string.Equals(track.Id, _previewSubtitleTrack.Id, StringComparison.Ordinal))
            {
                selectedStart = cue.Start;
                selectedEnd = cue.End;
            }

            changedTracks.Add(track);
            if (await RewriteSubtitleTrackFilesAsync(track, OriginalSubtitleWriteBackCheckBox.IsChecked == true))
            {
                originalWriteCount++;
            }
        }

        if (changedTracks.Count == 0)
        {
            SetStatus("調整対象の字幕が見つかりませんでした。");
            return;
        }

        RefreshMovieSceneMarkers(_selectedMovie);
        await _libraryStore.UpsertMovieAsync(_selectedMovie);
        RenderSceneRows(_previewSubtitleTrack);
        if (selectedStart is not null && selectedEnd is not null)
        {
            row.Timestamp = FormatCueEditTimestamp(selectedStart.Value);
            row.EndTimestamp = FormatCueEditTimestamp(selectedEnd.Value);
        }

        SelectSceneRow(row.CueId);
        UpdatePreviewSubtitleAtCurrentPosition();

        var directionText = direction > 0 ? "遅らせました" : "早めました";
        var syncText = changedTracks.Count > 1 ? $" / {changedTracks.Count} tracks synced" : string.Empty;
        var originalText = OriginalSubtitleWriteBackCheckBox.IsChecked == true
            ? $" / 原本更新 {originalWriteCount}"
            : string.Empty;
        SetStatus($"字幕タイミングを {milliseconds}ms {directionText}{syncText}{originalText}");
    }

    private async Task SetSelectedCueBoundaryFromPreviewAsync(bool setStart)
    {
        if (PreviewPlayer.Source is null || ScenesDataGrid.SelectedItem is not SceneRow row)
        {
            SetStatus("プレビュー再生中に字幕行を選択してください。");
            return;
        }

        var value = FormatCueEditTimestamp(PreviewPlayer.Position);
        if (setStart)
        {
            row.Timestamp = value;
        }
        else
        {
            row.EndTimestamp = value;
        }

        await SaveSceneRowTimingAsync(row);
    }

    private async Task SaveSceneRowTimingAsync(SceneRow row)
    {
        if (_selectedMovie is null || _previewSubtitleTrack is null)
        {
            return;
        }

        if (!TryParseCueTimestamp(row.Timestamp, out var start)
            || !TryParseCueTimestamp(row.EndTimestamp, out var end))
        {
            SetStatus("Start / End は 01:23.456 または 00:01:23.456 の形式で入力してください。");
            return;
        }

        if (end <= start)
        {
            SetStatus("End は Start より後にしてください。");
            return;
        }

        var targetTracks = GetTimingShiftTargetTracks(_selectedMovie, _previewSubtitleTrack).ToList();
        var changedTracks = new List<SubtitleTrack>();
        var originalWriteCount = 0;

        foreach (var track in targetTracks)
        {
            var cue = string.Equals(track.Id, _previewSubtitleTrack.Id, StringComparison.Ordinal)
                ? track.Cues.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, row.CueId, StringComparison.Ordinal)
                    || candidate.Index == row.CueIndex)
                : track.Cues.FirstOrDefault(candidate => candidate.Index == row.CueIndex);
            if (cue is null)
            {
                continue;
            }

            cue.Start = start;
            cue.End = end;
            changedTracks.Add(track);
            if (await RewriteSubtitleTrackFilesAsync(track, OriginalSubtitleWriteBackCheckBox.IsChecked == true))
            {
                originalWriteCount++;
            }
        }

        if (changedTracks.Count == 0)
        {
            SetStatus("調整対象の字幕が見つかりませんでした。");
            return;
        }

        RefreshMovieSceneMarkers(_selectedMovie);
        await _libraryStore.UpsertMovieAsync(_selectedMovie);
        row.Timestamp = FormatCueEditTimestamp(start);
        row.EndTimestamp = FormatCueEditTimestamp(end);
        RenderSceneRows(_previewSubtitleTrack);
        SelectSceneRow(row.CueId);
        UpdatePreviewSubtitleAtCurrentPosition();

        var syncText = changedTracks.Count > 1 ? $" / {changedTracks.Count} tracks synced" : string.Empty;
        var originalText = OriginalSubtitleWriteBackCheckBox.IsChecked == true
            ? $" / 原本更新 {originalWriteCount}"
            : string.Empty;
        SetStatus($"字幕タイミングを保存しました: {row.Timestamp} - {row.EndTimestamp}{syncText}{originalText}");
    }

    private bool TryGetTimingShiftMilliseconds(out int milliseconds)
    {
        return int.TryParse(TimingShiftTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out milliseconds)
            && milliseconds > 0
            && milliseconds <= 60_000;
    }

    private IEnumerable<SubtitleTrack> GetTimingShiftTargetTracks(Movie movie, SubtitleTrack selectedTrack)
    {
        yield return selectedTrack;

        if (SyncPairedSubtitleCheckBox.IsChecked != true)
        {
            yield break;
        }

        foreach (var track in movie.SubtitleTracks)
        {
            if (!string.Equals(track.Id, selectedTrack.Id, StringComparison.Ordinal)
                && HasSameSubtitleGroup(selectedTrack, track))
            {
                yield return track;
            }
        }
    }

    private static void ShiftCue(SubtitleCue cue, TimeSpan offset)
    {
        var duration = cue.End > cue.Start
            ? cue.End - cue.Start
            : TimeSpan.FromMilliseconds(1);
        var start = cue.Start + offset;
        if (start < TimeSpan.Zero)
        {
            start = TimeSpan.Zero;
        }

        cue.Start = start;
        cue.End = start + duration;
    }

    private void UpdatePreviewSubtitleAtCurrentPosition()
    {
        if (PreviewPlayer.Source is null)
        {
            HidePreviewSubtitle();
            return;
        }

        UpdatePreviewSubtitle(PreviewPlayer.Position);
    }

    private void UpdatePreviewSubtitle(TimeSpan position)
    {
        var lines = CreatePreviewSubtitleLines(position);
        if (lines.Count == 0)
        {
            HidePreviewSubtitle();
            return;
        }

        _currentPreviewCue = lines[0].Cue;
        PreviewSubtitlePrimaryTextBlock.Text = NormalizePreviewSubtitleText(lines[0].Cue.Text);
        if (lines.Count > 1)
        {
            PreviewSubtitleSecondaryTextBlock.Text = NormalizePreviewSubtitleText(lines[1].Cue.Text);
            PreviewSubtitleSecondaryTextBlock.Visibility = Visibility.Visible;
        }
        else
        {
            PreviewSubtitleSecondaryTextBlock.Text = string.Empty;
            PreviewSubtitleSecondaryTextBlock.Visibility = Visibility.Collapsed;
        }

        var hasTaggedCue = lines.Any(line => HasSubtitleTags(FindCueLearningState(line.Track, line.Cue)));
        PreviewSubtitleOverlay.BorderBrush = hasTaggedCue ? CreateBrush(_subtitleTagHighlightColor) : Brushes.Transparent;
        PreviewSubtitleOverlay.BorderThickness = hasTaggedCue ? new Thickness(2) : new Thickness(0);
        PreviewSubtitleOverlay.Visibility = Visibility.Visible;
    }

    private void HidePreviewSubtitle()
    {
        _currentPreviewCue = null;
        PreviewSubtitlePrimaryTextBlock.Text = string.Empty;
        PreviewSubtitleSecondaryTextBlock.Text = string.Empty;
        PreviewSubtitleSecondaryTextBlock.Visibility = Visibility.Collapsed;
        PreviewSubtitleOverlay.BorderThickness = new Thickness(0);
        PreviewSubtitleOverlay.Visibility = Visibility.Collapsed;
    }

    private void UpdateFullPreviewSubtitle(TimeSpan position)
    {
        var lines = CreatePreviewSubtitleLines(position);
        if (lines.Count == 0)
        {
            HideFullPreviewSubtitle();
            return;
        }

        FullPreviewSubtitlePrimaryTextBlock.Text = NormalizePreviewSubtitleText(lines[0].Cue.Text);
        if (lines.Count > 1)
        {
            FullPreviewSubtitleSecondaryTextBlock.Text = NormalizePreviewSubtitleText(lines[1].Cue.Text);
            FullPreviewSubtitleSecondaryTextBlock.Visibility = Visibility.Visible;
        }
        else
        {
            FullPreviewSubtitleSecondaryTextBlock.Text = string.Empty;
            FullPreviewSubtitleSecondaryTextBlock.Visibility = Visibility.Collapsed;
        }

        var hasTaggedCue = lines.Any(line => HasSubtitleTags(FindCueLearningState(line.Track, line.Cue)));
        FullPreviewSubtitleOverlay.BorderBrush = hasTaggedCue ? CreateBrush(_subtitleTagHighlightColor) : Brushes.Transparent;
        FullPreviewSubtitleOverlay.BorderThickness = hasTaggedCue ? new Thickness(2) : new Thickness(0);
        FullPreviewSubtitleOverlay.Visibility = Visibility.Visible;
    }

    private void HideFullPreviewSubtitle()
    {
        FullPreviewSubtitlePrimaryTextBlock.Text = string.Empty;
        FullPreviewSubtitleSecondaryTextBlock.Text = string.Empty;
        FullPreviewSubtitleSecondaryTextBlock.Visibility = Visibility.Collapsed;
        FullPreviewSubtitleOverlay.BorderThickness = new Thickness(0);
        FullPreviewSubtitleOverlay.Visibility = Visibility.Collapsed;
    }

    private List<PreviewSubtitleLine> CreatePreviewSubtitleLines(TimeSpan position)
    {
        if (_selectedMovie is null || _previewSubtitleTrack is null)
        {
            return [];
        }

        if (!_showDualSubtitles)
        {
            var cue = FindActiveCue(_previewSubtitleTrack, position);
            return cue is null ? [] : [new PreviewSubtitleLine(_previewSubtitleTrack, cue)];
        }

        var topTrack = FindSubtitleTrackByRole(_selectedMovie, _previewSubtitleTrack, SubtitleTrackRole.LearningTarget)
            ?? _previewSubtitleTrack;
        var bottomTrack = FindSubtitleTrackByRole(_selectedMovie, _previewSubtitleTrack, SubtitleTrackRole.Translation);

        if (bottomTrack is not null && string.Equals(bottomTrack.Id, topTrack.Id, StringComparison.Ordinal))
        {
            bottomTrack = null;
        }

        var lines = new List<PreviewSubtitleLine>();
        var topCue = FindActiveCue(topTrack, position);
        if (topCue is not null)
        {
            lines.Add(new PreviewSubtitleLine(topTrack, topCue));
        }

        if (bottomTrack is not null)
        {
            var bottomCue = FindActiveCue(bottomTrack, position);
            if (bottomCue is not null)
            {
                lines.Add(new PreviewSubtitleLine(bottomTrack, bottomCue));
            }
        }

        if (lines.Count == 0)
        {
            var cue = FindActiveCue(_previewSubtitleTrack, position);
            if (cue is not null)
            {
                lines.Add(new PreviewSubtitleLine(_previewSubtitleTrack, cue));
            }
        }

        return lines;
    }

    private static SubtitleCue? FindActiveCue(SubtitleTrack track, TimeSpan position)
    {
        return track.Cues.FirstOrDefault(candidate =>
            candidate.Start <= position
            && position < candidate.End
            && !string.IsNullOrWhiteSpace(candidate.Text));
    }

    private static SubtitleTrack? FindSubtitleTrackByRole(Movie movie, SubtitleTrack anchor, SubtitleTrackRole role)
    {
        if (anchor.Role == role)
        {
            return anchor;
        }

        var groupedTrack = movie.SubtitleTracks.FirstOrDefault(track =>
            track.Role == role
            && HasSameSubtitleGroup(anchor, track));
        if (groupedTrack is not null)
        {
            return groupedTrack;
        }

        return movie.SubtitleTracks.FirstOrDefault(track => track.Role == role);
    }

    private static bool HasSameSubtitleGroup(SubtitleTrack left, SubtitleTrack right)
    {
        return !string.IsNullOrWhiteSpace(left.GroupKey)
            && string.Equals(left.GroupKey, right.GroupKey, StringComparison.OrdinalIgnoreCase);
    }

    private void StartPreview(TimeSpan? startPosition = null)
    {
        if (_selectedMovie?.Video.CachePath is null || !File.Exists(_selectedMovie.Video.CachePath))
        {
            SetStatus("プレビューできる動画ファイルがありません。");
            return;
        }

        if (!EnsurePreviewSource(_selectedMovie, playWhenReady: true, startPosition))
        {
            SetStatus("プレビューを準備中です。");
            return;
        }

        if (startPosition is { } position)
        {
            SeekPreviewTo(position);
        }

        PreviewPlayer.Play();
        _previewTimer.Start();
        SetStatus("プレビュー再生中です。");
    }

    private void JumpPreviewTo(TimeSpan position)
    {
        if (_selectedMovie?.Video.CachePath is null || !File.Exists(_selectedMovie.Video.CachePath))
        {
            SetStatus("プレビューできる動画ファイルがありません。");
            return;
        }

        if (!EnsurePreviewSource(_selectedMovie, playWhenReady: true, position))
        {
            SetStatus("プレビューを準備中です。");
            return;
        }

        SeekPreviewTo(position);
        PreviewPlayer.Play();
        _previewTimer.Start();
    }

    private void StartFullPreview(TimeSpan? startPosition = null)
    {
        if (_selectedMovie?.Video.CachePath is null || !File.Exists(_selectedMovie.Video.CachePath))
        {
            SetStatus("フルプレビューできる動画ファイルがありません。");
            return;
        }

        if (!EnsureFullPreviewSource(_selectedMovie, playWhenReady: true, startPosition))
        {
            SetStatus("フルプレビューを準備中です。");
            return;
        }

        if (startPosition is { } position)
        {
            SeekFullPreviewTo(position);
        }

        FullPreviewPlayer.Play();
        _previewTimer.Start();
        SetStatus("フルプレビュー再生中です。");
    }

    private void SelectSceneRow(string cueId)
    {
        if (ScenesDataGrid.ItemsSource is not IEnumerable<SceneRow> rows)
        {
            return;
        }

        var row = rows.FirstOrDefault(candidate => string.Equals(candidate.CueId, cueId, StringComparison.Ordinal));
        if (row is null)
        {
            return;
        }

        ScenesDataGrid.SelectedItem = row;
        ScenesDataGrid.ScrollIntoView(row);
    }

    private static SubtitleCueLearningState? FindCueLearningState(SubtitleTrack track, SubtitleCue cue)
    {
        return FindCueLearningState(track, cue.Id, cue.Index);
    }

    private static SubtitleCueLearningState? FindCueLearningState(SubtitleTrack track, string cueId, int cueIndex)
    {
        return track.CueLearningStates.FirstOrDefault(state =>
            string.Equals(state.CueId, cueId, StringComparison.Ordinal)
            || state.CueIndex == cueIndex);
    }

    private static SubtitleCueLearningState EnsureCueLearningState(SubtitleTrack track, string cueId, int cueIndex)
    {
        var state = FindCueLearningState(track, cueId, cueIndex);
        if (state is not null)
        {
            if (string.IsNullOrWhiteSpace(state.CueId))
            {
                state.CueId = cueId;
            }

            return state;
        }

        state = new SubtitleCueLearningState
        {
            CueId = cueId,
            CueIndex = cueIndex
        };
        track.CueLearningStates.Add(state);
        return state;
    }

    private static bool IsFlaggedLearningState(SubtitleCueLearningState? state)
    {
        return state?.IsFlagged == true || state?.Tags.Any(IsFlagTag) == true;
    }

    private static bool HasSubtitleTags(SubtitleCueLearningState? state)
    {
        return state?.IsFlagged == true || state?.Tags.Count > 0;
    }

    private static System.Windows.Media.Brush CreateSceneRowBackground(SubtitleCueLearningState? state, string highlightColor)
    {
        return HasSubtitleTags(state)
            ? CreateBrush(highlightColor, 0x4D)
            : new SolidColorBrush(Color.FromRgb(0x0B, 0x11, 0x1A));
    }

    private static System.Windows.Media.Brush CreateBrush(string colorText, byte alpha = 0xFF)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(colorText);
            color.A = alpha;
            return new SolidColorBrush(color);
        }
        catch (FormatException)
        {
            return new SolidColorBrush(Color.FromArgb(alpha, 0xF6, 0xC9, 0x45));
        }
    }

    private static bool IsFlagTag(string tag)
    {
        return string.Equals(tag, FlagTagName, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddTag(List<string> tags, string tag)
    {
        var normalized = tag.Trim();
        if (normalized.Length == 0 || tags.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        tags.Add(normalized);
    }

    private static string NormalizedTagKey(string tag)
    {
        return tag.Trim().ToLowerInvariant();
    }

    private static void MergeTagDefinitionsFromLibrary(MovieLibrary library)
    {
        foreach (var movie in library.Movies)
        {
            foreach (var tag in movie.Tags)
            {
                AddTagDefinition(library, TagScope.Movie, tag);
            }

            foreach (var state in movie.SubtitleTracks.SelectMany(track => track.CueLearningStates))
            {
                if (state.IsFlagged)
                {
                    AddTag(state.Tags, FlagTagName);
                }

                foreach (var tag in state.Tags)
                {
                    AddTagDefinition(library, TagScope.Subtitle, tag);
                }
            }
        }

        AddTagDefinition(library, TagScope.Subtitle, FlagTagName);
    }

    private static void AddTagDefinition(MovieLibrary library, TagScope scope, string tag)
    {
        var normalized = tag.Trim();
        if (normalized.Length == 0)
        {
            return;
        }

        if (library.TagDefinitions.Any(existing =>
            existing.Scope == scope
            && string.Equals(existing.Name, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        library.TagDefinitions.Add(new TagDefinition
        {
            Name = normalized,
            Scope = scope,
            SortOrder = library.TagDefinitions.Count(existing => existing.Scope == scope)
        });
    }

    private Window CreateTagManagerWindow(
        ObservableCollection<TagDefinitionRow> movieTags,
        ObservableCollection<TagDefinitionRow> subtitleTags)
    {
        var window = new Window
        {
            Title = "タグ管理",
            Owner = this,
            Width = 720,
            Height = 460,
            MinWidth = 620,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = FindResource("PanelBrush") as System.Windows.Media.Brush,
            Foreground = System.Windows.Media.Brushes.White
        };

        var movieList = new System.Windows.Controls.ListBox { ItemsSource = movieTags, DisplayMemberPath = nameof(TagDefinitionRow.Name), Margin = new Thickness(0, 8, 0, 8) };
        var subtitleList = new System.Windows.Controls.ListBox { ItemsSource = subtitleTags, DisplayMemberPath = nameof(TagDefinitionRow.Name), Margin = new Thickness(0, 8, 0, 8) };
        var moviePanel = CreateTagScopePanel("動画タグ", movieTags, movieList);
        var subtitlePanel = CreateTagScopePanel("字幕タグ", subtitleTags, subtitleList);

        var content = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            },
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(14) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };
        content.Children.Add(moviePanel);
        Grid.SetColumn(subtitlePanel, 2);
        content.Children.Add(subtitlePanel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 3);
        var saveButton = new Button { Content = "保存", MinWidth = 86 };
        var cancelButton = new Button
        {
            Content = "キャンセル",
            MinWidth = 86,
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = FindResource("BorderBrush") as System.Windows.Media.Brush
        };
        saveButton.Click += (_, _) => window.DialogResult = true;
        cancelButton.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);
        content.Children.Add(buttons);

        window.Content = content;
        return window;
    }

    private FrameworkElement CreateTagScopePanel(
        string title,
        ObservableCollection<TagDefinitionRow> tags,
        System.Windows.Controls.ListBox listBox)
    {
        var textBox = new TextBox { Margin = new Thickness(0, 0, 8, 0) };
        var addButton = new Button { Content = "追加", MinWidth = 70 };
        var deleteButton = new Button
        {
            Content = "削除",
            MinWidth = 70,
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = FindResource("BorderBrush") as System.Windows.Media.Brush
        };

        addButton.Click += (_, _) =>
        {
            var tag = NormalizeOptionalText(textBox.Text);
            if (tag is null || tags.Any(existing => string.Equals(existing.Name, tag, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            tags.Add(new TagDefinitionRow(new TagDefinition { Name = tag, SortOrder = tags.Count }));
            textBox.Text = string.Empty;
        };
        deleteButton.Click += (_, _) =>
        {
            if (listBox.SelectedItem is TagDefinitionRow row)
            {
                tags.Remove(row);
            }
        };

        var panel = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            }
        };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold });

        var addRow = new Grid
        {
            Margin = new Thickness(0, 10, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        addRow.Children.Add(textBox);
        Grid.SetColumn(addButton, 1);
        addRow.Children.Add(addButton);
        Grid.SetRow(addRow, 1);
        panel.Children.Add(addRow);

        Grid.SetRow(listBox, 2);
        panel.Children.Add(listBox);
        Grid.SetRow(deleteButton, 3);
        deleteButton.HorizontalAlignment = HorizontalAlignment.Right;
        panel.Children.Add(deleteButton);
        return panel;
    }

    private static List<string> ParseTags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Split([',', '、', '，', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizeOptionalText(string? text)
    {
        var normalized = text?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private string ResolveGenerationVideoPath(Movie movie)
    {
        if (!string.IsNullOrWhiteSpace(movie.Video.SourceUri)
            && File.Exists(movie.Video.SourceUri))
        {
            return movie.Video.SourceUri;
        }

        if (!string.IsNullOrWhiteSpace(movie.Video.CachePath)
            && File.Exists(movie.Video.CachePath))
        {
            return movie.Video.CachePath;
        }

        throw new FileNotFoundException("字幕生成に使える動画ファイルが見つかりません。", movie.Video.FileName);
    }

    private string GetDefaultSubtitleGenerationDirectory(Movie? movie = null)
    {
        if (!string.IsNullOrWhiteSpace(WhisperOutputDirectoryTextBox.Text))
        {
            return WhisperOutputDirectoryTextBox.Text;
        }

        const string knownWorkspace = @"D:\英語\subtitile";
        if (Directory.Exists(knownWorkspace))
        {
            return knownWorkspace;
        }

        if (!string.IsNullOrWhiteSpace(movie?.Video.SourceUri)
            && File.Exists(movie.Video.SourceUri)
            && Path.GetDirectoryName(movie.Video.SourceUri) is { } sourceDirectory)
        {
            return sourceDirectory;
        }

        if (!string.IsNullOrWhiteSpace(movie?.Video.CachePath)
            && Path.GetDirectoryName(movie.Video.CachePath) is { } cacheDirectory)
        {
            return cacheDirectory;
        }

        return _paths.SubtitlePath;
    }

    private static string SelectedComboText(System.Windows.Controls.ComboBox comboBox, string fallback)
    {
        return comboBox.SelectedItem is ComboBoxItem item
            ? item.Content?.ToString() ?? fallback
            : fallback;
    }

    private static void SelectComboBoxItem(System.Windows.Controls.ComboBox comboBox, string? value, string fallback)
    {
        var target = string.IsNullOrWhiteSpace(value) ? fallback : value;
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), target, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), fallback, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private static void BackupExistingFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileName(path);
        var backupPath = Path.Combine(directory, $"{name}.{DateTime.Now:yyyyMMddHHmmss}.bak");
        File.Move(path, backupPath);
    }

    private async Task PumpProcessOutputAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            AppendSubtitleGenerationLog(line);
        }
    }

    private void AppendSubtitleGenerationLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendSubtitleGenerationLog(message));
            return;
        }

        SubtitleGenerationLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        SubtitleGenerationLogTextBox.ScrollToEnd();
    }

    private static string FormatProcessCommand(ProcessStartInfo startInfo)
    {
        return string.Join(
            ' ',
            new[] { QuoteCommandPart(startInfo.FileName) }.Concat(startInfo.ArgumentList.Select(QuoteCommandPart)));
    }

    private static string QuoteCommandPart(string value)
    {
        return value.Any(char.IsWhiteSpace) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;
    }

    private static List<string> SplitCommandLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var arguments = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        foreach (var character in text)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (builder.Length > 0)
                {
                    arguments.Add(builder.ToString());
                    builder.Clear();
                }

                continue;
            }

            builder.Append(character);
        }

        if (builder.Length > 0)
        {
            arguments.Add(builder.ToString());
        }

        return arguments;
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

    private static IEnumerable<string> GetDroppedFilePaths(System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return [];
        }

        return e.Data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths.Where(File.Exists)
            : [];
    }

    private static bool IsVideoFile(string path)
    {
        return VideoExtensions.Contains(Path.GetExtension(path));
    }

    private static bool IsSubtitleFile(string path)
    {
        return SubtitleExtensions.Contains(Path.GetExtension(path));
    }

    private void ResetPreviewIfMovieChanged(Movie? movie)
    {
        var currentPath = PreviewPlayer.Source?.LocalPath;
        var nextPath = movie?.Video.CachePath;
        if (string.Equals(currentPath, nextPath, StringComparison.OrdinalIgnoreCase)
            && (FullPreviewPlayer.Source is null
                || string.Equals(FullPreviewPlayer.Source.LocalPath, nextPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _previewTimer.Stop();
        PreviewPlayer.Stop();
        _playPreviewWhenMediaOpened = false;
        _isPreviewMediaOpened = false;
        PreviewPlayer.Source = null;
        ResetPreviewSeek();
        FullPreviewPlayer.Stop();
        _playFullPreviewWhenMediaOpened = false;
        _isFullPreviewMediaOpened = false;
        FullPreviewPlayer.Source = null;
        ResetFullPreviewSeek();
        if (!string.IsNullOrWhiteSpace(nextPath) && File.Exists(nextPath))
        {
            PreviewPlayer.Source = new Uri(nextPath);
        }
    }

    private bool EnsurePreviewSource(Movie movie, bool playWhenReady, TimeSpan? startPosition)
    {
        if (movie.Video.CachePath is null || !File.Exists(movie.Video.CachePath))
        {
            return false;
        }

        var source = new Uri(movie.Video.CachePath);
        var isSameSource = PreviewPlayer.Source is not null
            && string.Equals(PreviewPlayer.Source.LocalPath, source.LocalPath, StringComparison.OrdinalIgnoreCase);
        if (!isSameSource)
        {
            _previewTimer.Stop();
            PreviewPlayer.Stop();
            _isPreviewMediaOpened = false;
            _playPreviewWhenMediaOpened = playWhenReady;
            ResetPreviewSeek();
            _pendingPreviewSeek = startPosition;
            PreviewPlayer.Source = source;
            return false;
        }

        if (startPosition is not null)
        {
            _pendingPreviewSeek = startPosition;
        }

        if (!_isPreviewMediaOpened || _previewDuration <= TimeSpan.Zero)
        {
            _playPreviewWhenMediaOpened = _playPreviewWhenMediaOpened || playWhenReady;
            return false;
        }

        _playPreviewWhenMediaOpened = false;
        return true;
    }

    private bool EnsureFullPreviewSource(Movie movie, bool playWhenReady, TimeSpan? startPosition)
    {
        if (movie.Video.CachePath is null || !File.Exists(movie.Video.CachePath))
        {
            return false;
        }

        var source = new Uri(movie.Video.CachePath);
        var isSameSource = FullPreviewPlayer.Source is not null
            && string.Equals(FullPreviewPlayer.Source.LocalPath, source.LocalPath, StringComparison.OrdinalIgnoreCase);
        if (!isSameSource)
        {
            FullPreviewPlayer.Stop();
            _isFullPreviewMediaOpened = false;
            _playFullPreviewWhenMediaOpened = playWhenReady;
            ResetFullPreviewSeek();
            _pendingFullPreviewSeek = startPosition;
            FullPreviewPlayer.Source = source;
            return false;
        }

        if (startPosition is not null)
        {
            _pendingFullPreviewSeek = startPosition;
        }

        if (!_isFullPreviewMediaOpened || _fullPreviewDuration <= TimeSpan.Zero)
        {
            _playFullPreviewWhenMediaOpened = _playFullPreviewWhenMediaOpened || playWhenReady;
            return false;
        }

        _playFullPreviewWhenMediaOpened = false;
        return true;
    }

    private void ResetPreviewSeek()
    {
        _previewDuration = TimeSpan.Zero;
        _pendingPreviewSeek = null;
        _isPreviewMediaOpened = false;
        _isPreviewSeeking = false;

        _isUpdatingPreviewSlider = true;
        try
        {
            PreviewSeekSlider.Minimum = 0;
            PreviewSeekSlider.Maximum = 1;
            PreviewSeekSlider.Value = 0;
            PreviewSeekSlider.IsEnabled = false;
            PreviewPositionTextBlock.Text = FormatPlaybackPosition(TimeSpan.Zero, TimeSpan.Zero);
            HidePreviewSubtitle();
        }
        finally
        {
            _isUpdatingPreviewSlider = false;
        }
    }

    private void UpdatePreviewSeekFromPlayer()
    {
        if (_isPreviewSeeking || PreviewPlayer.Source is null)
        {
            return;
        }

        SetPreviewSeek(PreviewPlayer.Position);
    }

    private void BeginPreviewSeek()
    {
        if (PreviewSeekSlider.IsEnabled)
        {
            _isPreviewSeeking = true;
        }
    }

    private void CompletePreviewSeek()
    {
        if (!PreviewSeekSlider.IsEnabled)
        {
            _isPreviewSeeking = false;
            return;
        }

        SeekPreviewToSliderValue();
        _isPreviewSeeking = false;
    }

    private void SetPreviewSeek(TimeSpan position)
    {
        var maxSeconds = Math.Max(0.0, PreviewSeekSlider.Maximum);
        var seconds = Math.Clamp(position.TotalSeconds, 0.0, maxSeconds);
        var displayPosition = TimeSpan.FromSeconds(seconds);

        _isUpdatingPreviewSlider = true;
        try
        {
            PreviewSeekSlider.Value = seconds;
            PreviewPositionTextBlock.Text = FormatPlaybackPosition(displayPosition, _previewDuration);
            UpdatePreviewSubtitle(displayPosition);
        }
        finally
        {
            _isUpdatingPreviewSlider = false;
        }
    }

    private void SeekPreviewToSliderValue()
    {
        SeekPreviewTo(TimeSpan.FromSeconds(Math.Clamp(PreviewSeekSlider.Value, 0.0, PreviewSeekSlider.Maximum)));
    }

    private void SeekPreviewTo(TimeSpan position)
    {
        if (PreviewPlayer.Source is null || _previewDuration <= TimeSpan.Zero)
        {
            return;
        }

        position = ClampPreviewPosition(position);
        PreviewPlayer.Position = position;
        SetPreviewSeek(position);
    }

    private TimeSpan ClampPreviewPosition(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return position > _previewDuration ? _previewDuration : position;
    }

    private void ResetFullPreviewSeek()
    {
        _fullPreviewDuration = TimeSpan.Zero;
        _pendingFullPreviewSeek = null;
        _isFullPreviewMediaOpened = false;
        _isFullPreviewSeeking = false;

        _isUpdatingFullPreviewSlider = true;
        try
        {
            FullPreviewSeekSlider.Minimum = 0;
            FullPreviewSeekSlider.Maximum = 1;
            FullPreviewSeekSlider.Value = 0;
            FullPreviewSeekSlider.IsEnabled = false;
            FullPreviewPositionTextBlock.Text = FormatPlaybackPosition(TimeSpan.Zero, TimeSpan.Zero);
            HideFullPreviewSubtitle();
        }
        finally
        {
            _isUpdatingFullPreviewSlider = false;
        }
    }

    private void UpdateFullPreviewSeekFromPlayer()
    {
        if (_isFullPreviewSeeking || FullPreviewPlayer.Source is null)
        {
            return;
        }

        SetFullPreviewSeek(FullPreviewPlayer.Position);
    }

    private void BeginFullPreviewSeek()
    {
        if (FullPreviewSeekSlider.IsEnabled)
        {
            _isFullPreviewSeeking = true;
        }
    }

    private void CompleteFullPreviewSeek()
    {
        if (!FullPreviewSeekSlider.IsEnabled)
        {
            _isFullPreviewSeeking = false;
            return;
        }

        SeekFullPreviewToSliderValue();
        _isFullPreviewSeeking = false;
    }

    private void SetFullPreviewSeek(TimeSpan position)
    {
        var maxSeconds = Math.Max(0.0, FullPreviewSeekSlider.Maximum);
        var seconds = Math.Clamp(position.TotalSeconds, 0.0, maxSeconds);
        var displayPosition = TimeSpan.FromSeconds(seconds);

        _isUpdatingFullPreviewSlider = true;
        try
        {
            FullPreviewSeekSlider.Value = seconds;
            FullPreviewPositionTextBlock.Text = FormatPlaybackPosition(displayPosition, _fullPreviewDuration);
            UpdateFullPreviewSubtitle(displayPosition);
        }
        finally
        {
            _isUpdatingFullPreviewSlider = false;
        }
    }

    private void SeekFullPreviewToSliderValue()
    {
        SeekFullPreviewTo(TimeSpan.FromSeconds(Math.Clamp(FullPreviewSeekSlider.Value, 0.0, FullPreviewSeekSlider.Maximum)));
    }

    private void SeekFullPreviewTo(TimeSpan position)
    {
        if (FullPreviewPlayer.Source is null || _fullPreviewDuration <= TimeSpan.Zero)
        {
            return;
        }

        position = ClampFullPreviewPosition(position);
        FullPreviewPlayer.Position = position;
        SetFullPreviewSeek(position);
    }

    private TimeSpan ClampFullPreviewPosition(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return position > _fullPreviewDuration ? _fullPreviewDuration : position;
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

    private static string FormatCueEditTimestamp(TimeSpan value)
    {
        return value.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}")
            : string.Create(CultureInfo.InvariantCulture, $"{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}");
    }

    private static bool TryParseCueTimestamp(string? value, out TimeSpan timestamp)
    {
        timestamp = TimeSpan.Zero;
        var normalized = value?.Trim().Replace(',', '.');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var parts = normalized.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        var secondsText = parts[^1];
        if (!double.TryParse(secondsText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        var minutes = 0;
        var hours = 0;
        if (parts.Length >= 2 && !int.TryParse(parts[^2], NumberStyles.None, CultureInfo.InvariantCulture, out minutes))
        {
            return false;
        }

        if (parts.Length == 3 && !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hours))
        {
            return false;
        }

        if (hours < 0 || minutes < 0 || seconds < 0)
        {
            return false;
        }

        timestamp = TimeSpan.FromHours(hours)
            + TimeSpan.FromMinutes(minutes)
            + TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static string FormatPlaybackPosition(TimeSpan position, TimeSpan duration)
    {
        return $"{FormatTimestamp(position)} / {FormatTimestamp(duration)}";
    }

    private static string NormalizePreviewSubtitleText(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
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
            TrackId = track.Id;
            Label = track.Label;
            Language = track.Language ?? string.Empty;
            Role = track.Role.ToString();
            Format = track.Format.ToString();
            CueCount = track.CueCount;
        }

        public string TrackId { get; }

        public string Label { get; }

        public string Language { get; }

        public string Role { get; }

        public string Format { get; }

        public int CueCount { get; }
    }

    private sealed record PreviewSubtitleLine(SubtitleTrack Track, SubtitleCue Cue);

    private sealed class TagDefinitionRow
    {
        public TagDefinitionRow(TagDefinition tag)
        {
            Name = tag.Name;
            SortOrder = tag.SortOrder;
            CreatedAt = tag.CreatedAt;
        }

        public string Name { get; }

        public int SortOrder { get; }

        public DateTimeOffset CreatedAt { get; }

        public TagDefinition ToDefinition(TagScope scope, int index)
        {
            return new TagDefinition
            {
                Name = Name.Trim(),
                Scope = scope,
                SortOrder = index,
                CreatedAt = CreatedAt
            };
        }
    }

    private sealed class SceneRow
    {
        public SceneRow(SubtitleCue cue, SubtitleCueLearningState? learningState, System.Windows.Media.Brush rowBackgroundBrush)
        {
            CueId = cue.Id;
            CueIndex = cue.Index;
            Start = cue.Start;
            End = cue.End;
            Timestamp = FormatCueEditTimestamp(cue.Start);
            EndTimestamp = FormatCueEditTimestamp(cue.End);
            Label = CompactCueText(cue.Text);
            IsFlagged = IsFlaggedLearningState(learningState);
            ListeningAccuracy = FormatAccuracy(learningState?.Listening.LastAccuracy);
            ShadowingAccuracy = FormatAccuracy(learningState?.Shadowing.LastAccuracy);
            Tags = learningState is null ? string.Empty : string.Join(", ", learningState.Tags);
            Note = learningState?.Note ?? string.Empty;
            RowBackgroundBrush = rowBackgroundBrush;
        }

        public string CueId { get; }

        public int CueIndex { get; }

        public TimeSpan Start { get; }

        public TimeSpan End { get; }

        public string Timestamp { get; set; }

        public string EndTimestamp { get; set; }

        public string Label { get; }

        public bool IsFlagged { get; set; }

        public string ListeningAccuracy { get; }

        public string ShadowingAccuracy { get; }

        public string Tags { get; set; }

        public string Note { get; set; }

        public System.Windows.Media.Brush RowBackgroundBrush { get; }

        private static string CompactCueText(string text)
        {
            var normalized = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            return normalized.Length <= 120 ? normalized : normalized[..117] + "...";
        }

        private static string FormatAccuracy(double? value)
        {
            return value is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{value.Value:P0}");
        }
    }
}

