namespace CoffeeMovie.Core.Models;

public sealed class CuePracticeMetric
{
    public int AttemptCount { get; set; }

    public int OkCount { get; set; }

    public int NgCount { get; set; }

    public double? LastAccuracy { get; set; }

    public double? BestAccuracy { get; set; }

    public string? LastTranscript { get; set; }

    public TimeSpan? LastDuration { get; set; }

    public DateTimeOffset? LastPracticedAt { get; set; }
}
