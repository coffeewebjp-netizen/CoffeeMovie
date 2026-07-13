using System.IO;
using System.Windows;
using CoffeeMovie.Studio.Services;
using Microsoft.Win32;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
    private readonly RoundtripMovieExchangeService _roundtripMovieExchangeService = new();

    private async void OnExportRoundtripClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie is null)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Roundtrip Export folder",
            Multiselect = false
        };
        if (!string.IsNullOrWhiteSpace(_selectedMovie.Video.CachePath)
            && Path.GetDirectoryName(_selectedMovie.Video.CachePath) is { } videoDirectory
            && Directory.Exists(videoDirectory))
        {
            dialog.InitialDirectory = videoDirectory;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ExportRoundtripButton.IsEnabled = false;
            var result = await _roundtripMovieExchangeService.ExportAsync(_selectedMovie, dialog.FolderName);
            SetStatus($"Roundtrip Export completed: subtitles {result.SubtitleFileCount} / CSV {result.NoteRowCount} rows / {result.ExportDirectory}");
        }
        catch (Exception ex)
        {
            ShowError("Roundtrip Export failed", ex);
        }
        finally
        {
            ExportRoundtripButton.IsEnabled = _selectedMovie is not null;
        }
    }

    private async void OnImportRoundtripClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Roundtrip Import folder",
            Multiselect = false
        };
        if (!string.IsNullOrWhiteSpace(_selectedMovie?.Video.CachePath)
            && Path.GetDirectoryName(_selectedMovie.Video.CachePath) is { } videoDirectory
            && Directory.Exists(videoDirectory))
        {
            dialog.InitialDirectory = videoDirectory;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ImportRoundtripButton.IsEnabled = false;
            var library = await _libraryStore.LoadAsync();
            var result = await _roundtripMovieExchangeService.ImportAsync(
                library,
                _selectedMovie,
                dialog.FolderName,
                FlagTagName);
            var movie = library.Movies.First(candidate => string.Equals(candidate.Id, result.MovieId, StringComparison.Ordinal));

            foreach (var trackId in result.ChangedTrackIds)
            {
                var track = movie.SubtitleTracks.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, trackId, StringComparison.Ordinal));
                if (track is not null)
                {
                    await RewriteSubtitleTrackFilesAsync(track, writeBackOriginal: false);
                }
            }

            RefreshMovieSceneMarkers(movie);
            MergeTagDefinitionsFromLibrary(library);
            await _libraryStore.SaveAsync(library);
            await RefreshMoviesAsync(movie.Id);

            var warningText = result.Warnings.Count == 0
                ? string.Empty
                : $" / warnings {result.Warnings.Count}";
            SetStatus($"Roundtrip Import completed: subtitles {result.SubtitleTracksUpdated} tracks/{result.SubtitleCuesUpdated} cues, notes {result.LearningStatesUpdated} rows, skip {result.RowsSkipped}{warningText}");
            if (result.Warnings.Count > 0)
            {
                MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, result.Warnings.Take(12)),
                    "Roundtrip Import warnings",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            ShowError("Roundtrip Import failed", ex);
        }
        finally
        {
            ImportRoundtripButton.IsEnabled = true;
        }
    }
}
