namespace CoffeeMovie.Core.Models;

public sealed class PlaybackState
{
    public double PositionSeconds { get; set; }

    public double DurationSeconds { get; set; }

    public DateTimeOffset? LastWatchedAt { get; set; }
}

