namespace CoffeeMovie.Core.Models;

public sealed class Movie
{
    public string Id { get; set; } = MovieId.New();

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public VideoAsset Video { get; set; } = new();

    public List<string> Tags { get; set; } = [];

    public List<SubtitleTrack> SubtitleTracks { get; set; } = [];

    public List<SceneMarker> SceneMarkers { get; set; } = [];

    public PlaybackState Playback { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

