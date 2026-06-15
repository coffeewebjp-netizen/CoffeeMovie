using System.Windows;
using CoffeeMovie.Studio.Services;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
    private async void OnCreateThumbnailClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie is null)
        {
            SetStatus("動画を選択してください。");
            return;
        }

        try
        {
            var videoPath = ResolveGenerationVideoPath(_selectedMovie);
            var capturePosition = PreviewPlayer.Source is not null
                ? PreviewPlayer.Position
                : TimeSpan.Zero;
            var thumbnailPath = ThumbnailCaptureService.GetThumbnailPath(_paths.ThumbnailCachePath, _selectedMovie.Id);
            SetStatus("サムネイルを作成中です...", hideProgress: false);
            await ThumbnailCaptureService.CaptureAsync(videoPath, thumbnailPath, capturePosition);

            _selectedMovie.Video.ThumbnailPath = thumbnailPath;
            _selectedMovie.Video.ThumbnailTimestampSeconds = Math.Max(0, capturePosition.TotalSeconds);
            _selectedMovie.UpdatedAt = DateTimeOffset.UtcNow;
            await _libraryStore.UpsertMovieAsync(_selectedMovie);
            await RefreshMoviesAsync(_selectedMovie.Id);
            SetStatus($"サムネイルを作成しました: {FormatTimestamp(capturePosition)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "サムネイル作成に失敗しました",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            SetStatus("サムネイル作成に失敗しました。");
        }
    }

    private void OnPlayThumbnailClipClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie?.Video.ThumbnailTimestampSeconds is not { } seconds)
        {
            SetStatus("サムネイル位置がまだありません。先にサムネイルを作成してください。");
            return;
        }

        var start = TimeSpan.FromSeconds(Math.Max(0, seconds));
        _previewStopAt = start.Add(TimeSpan.FromSeconds(5));
        StartPreview(start);
        SetStatus("サムネイル位置を5秒だけ再生します。");
    }

}
