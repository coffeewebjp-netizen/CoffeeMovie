namespace CoffeeMovie.Core.Models;

public sealed class Movie
{
    public string Id { get; set; } = MovieId.New();

    public string Title { get; set; } = string.Empty;

    public string? SeriesTitle { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public string? Description { get; set; }

    public VideoAsset Video { get; set; } = new();

    public List<string> Tags { get; set; } = [];

    public List<SubtitleTrack> SubtitleTracks { get; set; } = [];

    public List<SceneMarker> SceneMarkers { get; set; } = [];

    public PlaybackState Playback { get; set; } = new();

    public string? SourcePackageUri { get; set; }

    public string? SourcePackageName { get; set; }

    public long? SourcePackageLastModified { get; set; }

    public long? SourcePackageSize { get; set; }

    public DateTimeOffset? SourceMovieUpdatedAt { get; set; }

    public string? SourceContentFingerprint { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

