using System.Globalization;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Core.Services;
using CoffeeMovie.Storage.Models;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
    private sealed class MovieListItem
    {
        public MovieListItem(Movie movie)
        {
            MovieId = movie.Id;
            Title = movie.Title;
            SeriesGroup = string.IsNullOrWhiteSpace(movie.SeriesTitle) ? "未分類" : movie.SeriesTitle;
            SeasonGroup = movie.SeasonNumber is null ? "Season 未設定" : $"Season {movie.SeasonNumber.Value}";
            var series = string.IsNullOrWhiteSpace(movie.SeriesTitle) ? string.Empty : movie.SeriesTitle;
            var episode = MovieMetadataInferenceService.FormatSeasonEpisode(movie);
            Detail = string.Join(" / ", new[]
                {
                    string.Join(' ', new[] { series, episode }.Where(part => !string.IsNullOrWhiteSpace(part))),
                    $"{movie.SubtitleTracks.Count} subtitle",
                    $"{movie.SceneMarkers.Count} scene",
                    movie.Tags.Count == 0 ? string.Empty : string.Join(", ", movie.Tags)
                }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
            CacheState = movie.Video.HasLocalCache ? "cached" : "not cached";
            ThumbnailPath = string.IsNullOrWhiteSpace(movie.Video.ThumbnailPath)
                ? null
                : movie.Video.ThumbnailPath;
        }

        public string MovieId { get; }

        public string Title { get; }

        public string SeriesGroup { get; }

        public string SeasonGroup { get; }

        public string Detail { get; }

        public string CacheState { get; }

        public string? ThumbnailPath { get; }
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

    private enum PreviewOverlayKind
    {
        EnglishSubtitle,
        JapaneseSubtitle,
        AiNote,
        UserNote
    }

    private enum OverlaySide
    {
        Above,
        Below
    }

    private sealed record OverlayPlacement(OverlaySide Side, int Order);

    private sealed record PositionedOverlayItem(PreviewOverlayItem Item, OverlayPlacement Placement);

    private sealed record PreviewOverlayItem(
        PreviewOverlayKind Kind,
        string Text,
        string Position,
        bool HasHighlight)
    {
        public int SortPriority => Kind switch
        {
            PreviewOverlayKind.EnglishSubtitle => 0,
            PreviewOverlayKind.JapaneseSubtitle => 1,
            PreviewOverlayKind.AiNote => 2,
            PreviewOverlayKind.UserNote => 3,
            _ => 4
        };
    }

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
        public SceneRow(
            SubtitleCue cue,
            SubtitleCueLearningState? learningState,
            System.Windows.Media.Brush rowBackgroundBrush,
            Movie? sourceMovie = null,
            SubtitleTrack? sourceTrack = null,
            bool isGlobalResult = false)
        {
            MovieId = sourceMovie?.Id ?? string.Empty;
            TrackId = sourceTrack?.Id ?? string.Empty;
            SourceMovie = sourceMovie is null ? string.Empty : FormatSceneSourceMovie(sourceMovie);
            SourceTrack = sourceTrack?.Label ?? string.Empty;
            IsGlobalResult = isGlobalResult;
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
            AiNote = learningState?.AiNote ?? string.Empty;
            Note = learningState?.Note ?? string.Empty;
            RowBackgroundBrush = rowBackgroundBrush;
        }

        public string MovieId { get; }

        public string TrackId { get; }

        public string SourceMovie { get; }

        public string SourceTrack { get; }

        public bool IsGlobalResult { get; }

        public bool CanEditTags => !IsGlobalResult;

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

        public string AiNote { get; }

        public string Note { get; set; }

        public System.Windows.Media.Brush RowBackgroundBrush { get; }

        private static string CompactCueText(string text)
        {
            var normalized = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            return normalized.Length <= 120 ? normalized : normalized[..117] + "...";
        }

        private static string FormatSceneSourceMovie(Movie movie)
        {
            var episode = MovieMetadataInferenceService.FormatSeasonEpisode(movie);
            return string.IsNullOrWhiteSpace(episode)
                ? movie.Title
                : $"{movie.Title} ({episode})";
        }

        private static string FormatAccuracy(double? value)
        {
            return value is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{value.Value:P0}");
        }
    }
}
