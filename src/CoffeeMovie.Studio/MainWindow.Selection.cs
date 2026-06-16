using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Models;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
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

    private async void OnMovieMetadataLostFocus(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie is null || _isUpdatingSelection)
        {
            return;
        }

        if (!TryParseOptionalPositiveInt(SeasonNumberTextBox.Text, out var seasonNumber)
            || !TryParseOptionalPositiveInt(EpisodeNumberTextBox.Text, out var episodeNumber))
        {
            SetStatus("Season / Episode は空欄または1以上の数値で入力してください。");
            RenderMovieDetails(_selectedMovie);
            return;
        }

        var seriesTitle = NormalizeOptionalText(SeriesTitleTextBox.Text);
        var tags = ParseTags(MovieTagsTextBox.Text);
        var isDirty = !string.Equals(_selectedMovie.SeriesTitle, seriesTitle, StringComparison.Ordinal)
            || _selectedMovie.SeasonNumber != seasonNumber
            || _selectedMovie.EpisodeNumber != episodeNumber
            || !_selectedMovie.Tags.SequenceEqual(tags, StringComparer.OrdinalIgnoreCase);
        if (!isDirty)
        {
            return;
        }

        _selectedMovie.SeriesTitle = seriesTitle;
        _selectedMovie.SeasonNumber = seasonNumber;
        _selectedMovie.EpisodeNumber = episodeNumber;
        _selectedMovie.Tags = tags;
        _selectedMovie.UpdatedAt = DateTimeOffset.UtcNow;

        var library = await _libraryStore.LoadAsync();
        var target = library.Movies.FirstOrDefault(movie => string.Equals(movie.Id, _selectedMovie.Id, StringComparison.Ordinal));
        if (target is not null)
        {
            target.SeriesTitle = _selectedMovie.SeriesTitle;
            target.SeasonNumber = _selectedMovie.SeasonNumber;
            target.EpisodeNumber = _selectedMovie.EpisodeNumber;
            target.Tags = _selectedMovie.Tags.ToList();
            target.UpdatedAt = _selectedMovie.UpdatedAt;
            foreach (var tag in target.Tags)
            {
                AddTagDefinition(library, TagScope.Movie, tag);
            }

            await _libraryStore.SaveAsync(library);
        }
        else
        {
            await _libraryStore.UpsertMovieAsync(_selectedMovie);
        }

        await RefreshMoviesAsync(_selectedMovie.Id);
        SetStatus("動画の管理情報を保存しました。");
    }

    private async void OnMovieSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isOpeningGlobalSceneRow)
        {
            return;
        }

        if (MoviesListBox.SelectedItem is not MovieListItem item)
        {
            _selectedMovie = null;
            RenderMovieDetails(null);
            return;
        }

        var library = await _libraryStore.LoadAsync();
        _selectedMovie = library.Movies.FirstOrDefault(movie => string.Equals(movie.Id, item.MovieId, StringComparison.Ordinal));
        RenderMovieDetails(_selectedMovie);
        if (HasGlobalSubtitleTagFilter())
        {
            RenderGlobalSubtitleTagResults(library);
        }
    }

    private async void OnSubtitleSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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
            if (HasGlobalSubtitleTagFilter())
            {
                await RenderGlobalSubtitleTagResultsAsync();
            }
            else
            {
                RenderSceneRows(null);
            }
            return;
        }

        RemoveSubtitleButton.IsEnabled = true;
        _previewSubtitleTrack = _selectedMovie.SubtitleTracks
            .FirstOrDefault(track => string.Equals(track.Id, row.TrackId, StringComparison.Ordinal));
        if (HasGlobalSubtitleTagFilter())
        {
            await RenderGlobalSubtitleTagResultsAsync();
        }
        else
        {
            RenderSceneRows(_previewSubtitleTrack);
        }

        UpdatePreviewSubtitleAtCurrentPosition();
    }

    private async Task OpenGlobalSceneRowAsync(SceneRow row)
    {
        if (string.IsNullOrWhiteSpace(row.MovieId) || string.IsNullOrWhiteSpace(row.TrackId))
        {
            return;
        }

        var library = await _libraryStore.LoadAsync();
        var movie = library.Movies.FirstOrDefault(candidate => string.Equals(candidate.Id, row.MovieId, StringComparison.Ordinal));
        var track = movie?.SubtitleTracks.FirstOrDefault(candidate => string.Equals(candidate.Id, row.TrackId, StringComparison.Ordinal));
        if (movie is null || track is null)
        {
            SetStatus("字幕タグ検索結果の動画または字幕が見つかりませんでした。");
            return;
        }

        if (_movies.FirstOrDefault(item => string.Equals(item.MovieId, row.MovieId, StringComparison.Ordinal)) is { } listItem)
        {
            _isOpeningGlobalSceneRow = true;
            try
            {
                MoviesListBox.SelectedItem = listItem;
                MoviesListBox.ScrollIntoView(listItem);
            }
            finally
            {
                _isOpeningGlobalSceneRow = false;
            }
        }

        _selectedMovie = movie;
        RenderMovieDetails(movie);
        _previewSubtitleTrack = track;
        if (SubtitlesDataGrid.ItemsSource is IEnumerable<SubtitleRow> subtitleRows)
        {
            SubtitlesDataGrid.SelectedItem = subtitleRows
                .FirstOrDefault(candidate => string.Equals(candidate.TrackId, track.Id, StringComparison.Ordinal));
        }

        RenderGlobalSubtitleTagResults(library);
        JumpPreviewTo(row.Start);
        SetStatus($"字幕タグ検索から開きました: {movie.Title} / {row.Timestamp}");
    }

}
