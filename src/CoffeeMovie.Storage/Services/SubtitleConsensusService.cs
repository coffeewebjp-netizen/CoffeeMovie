using System.Text;
using CoffeeMovie.Core.Models;

namespace CoffeeMovie.Storage.Services;

public static class SubtitleConsensusService
{
    private const double ClusterThreshold = 0.58;

    public static IReadOnlyList<SubtitleCue> Merge(IReadOnlyList<IReadOnlyList<SubtitleCue>> cueRuns)
    {
        var candidates = cueRuns
            .SelectMany((cues, runIndex) => cues.Select(cue => new CueCandidate(runIndex, cue)))
            .OrderBy(candidate => candidate.Cue.Start)
            .ThenBy(candidate => candidate.Cue.End)
            .ToList();
        var clusters = new List<CueCluster>();

        foreach (var candidate in candidates)
        {
            var bestCluster = clusters
                .Where(cluster => !cluster.ContainsRun(candidate.RunIndex))
                .Select(cluster => new
                {
                    Cluster = cluster,
                    Score = ScoreCandidate(cluster, candidate)
                })
                .OrderByDescending(item => item.Score)
                .FirstOrDefault();

            if (bestCluster is not null && bestCluster.Score >= ClusterThreshold)
            {
                bestCluster.Cluster.Items.Add(candidate);
            }
            else
            {
                clusters.Add(new CueCluster(candidate));
            }
        }

        var merged = clusters
            .Select(MergeCluster)
            .OrderBy(cue => cue.Start)
            .ThenBy(cue => cue.End)
            .ToList();
        NormalizeTimeline(merged);

        for (var index = 0; index < merged.Count; index++)
        {
            merged[index].Index = index + 1;
        }

        return merged;
    }

    private static double ScoreCandidate(CueCluster cluster, CueCandidate candidate)
    {
        var bestScore = 0.0;
        foreach (var item in cluster.Items)
        {
            var textScore = CalculateTextSimilarity(candidate.Cue.Text, item.Cue.Text);
            var exactText = string.Equals(
                NormalizeComparisonText(candidate.Cue.Text),
                NormalizeComparisonText(item.Cue.Text),
                StringComparison.Ordinal);
            var startDelta = Math.Abs((candidate.Cue.Start - item.Cue.Start).TotalSeconds);
            var centerDelta = Math.Abs((GetCenter(candidate.Cue) - GetCenter(item.Cue)).TotalSeconds);
            var overlapScore = CalculateOverlapScore(candidate.Cue, item.Cue);
            var timeScore = Math.Max(
                overlapScore,
                1.0 - Math.Min(Math.Min(startDelta, centerDelta) / 3.0, 1.0));

            var score = (textScore * 0.7) + (timeScore * 0.3);
            if (exactText && startDelta <= 5.0)
            {
                score = Math.Max(score, 0.94 - (startDelta * 0.04));
            }
            else if (textScore >= 0.82 && startDelta <= 4.0)
            {
                score = Math.Max(score, 0.82 - (startDelta * 0.03));
            }
            else if (overlapScore >= 0.35 && textScore >= 0.45)
            {
                score = Math.Max(score, 0.62 + (overlapScore * 0.2) + (textScore * 0.1));
            }

            bestScore = Math.Max(bestScore, Math.Clamp(score, 0.0, 1.0));
        }

        return bestScore;
    }

    private static SubtitleCue MergeCluster(CueCluster cluster)
    {
        var cues = cluster.Items.Select(item => item.Cue).ToList();
        var start = MedianTime(cues.Select(cue => cue.Start));
        var end = MedianTime(cues.Select(cue => cue.End));
        if (end <= start)
        {
            var duration = MedianTime(cues.Select(cue => cue.End - cue.Start));
            if (duration < TimeSpan.FromMilliseconds(250))
            {
                duration = TimeSpan.FromSeconds(1);
            }

            end = start + duration;
        }

        return new SubtitleCue
        {
            Id = Guid.NewGuid().ToString("N"),
            Start = start,
            End = end,
            Text = SelectConsensusText(cues)
        };
    }

    private static string SelectConsensusText(IReadOnlyList<SubtitleCue> cues)
    {
        var grouped = cues
            .GroupBy(cue => NormalizeComparisonText(cue.Text))
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Max(cue => NormalizeDisplayText(cue.Text).Length))
            .FirstOrDefault();
        if (grouped is not null && grouped.Count() >= 2)
        {
            return grouped
                .Select(cue => NormalizeDisplayText(cue.Text))
                .OrderByDescending(text => text.Length)
                .First();
        }

        var bestText = string.Empty;
        var bestScore = double.NegativeInfinity;
        foreach (var cue in cues)
        {
            var text = NormalizeDisplayText(cue.Text);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var similaritySum = cues
                .Where(other => !ReferenceEquals(other, cue))
                .Sum(other => CalculateTextSimilarity(text, other.Text));
            var score = similaritySum + Math.Min(text.Length / 120.0, 0.25);
            if (score > bestScore)
            {
                bestScore = score;
                bestText = text;
            }
        }

        return bestText;
    }

    private static void NormalizeTimeline(List<SubtitleCue> cues)
    {
        for (var index = 0; index < cues.Count; index++)
        {
            if (cues[index].End <= cues[index].Start)
            {
                cues[index].End = cues[index].Start + TimeSpan.FromSeconds(1);
            }

            if (index >= cues.Count - 1)
            {
                continue;
            }

            var next = cues[index + 1];
            if (cues[index].End <= next.Start)
            {
                continue;
            }

            var adjustedEnd = next.Start - TimeSpan.FromMilliseconds(40);
            if (adjustedEnd > cues[index].Start + TimeSpan.FromMilliseconds(250))
            {
                cues[index].End = adjustedEnd;
            }
        }
    }

    private static TimeSpan MedianTime(IEnumerable<TimeSpan> values)
    {
        var ticks = values.Select(value => value.Ticks).Order().ToList();
        if (ticks.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var middle = ticks.Count / 2;
        if (ticks.Count % 2 == 1)
        {
            return TimeSpan.FromTicks(ticks[middle]);
        }

        return TimeSpan.FromTicks((ticks[middle - 1] + ticks[middle]) / 2);
    }

    private static TimeSpan GetCenter(SubtitleCue cue)
    {
        return cue.Start + TimeSpan.FromTicks(Math.Max(0, (cue.End - cue.Start).Ticks / 2));
    }

    private static double CalculateOverlapScore(SubtitleCue left, SubtitleCue right)
    {
        var overlapStart = left.Start > right.Start ? left.Start : right.Start;
        var overlapEnd = left.End < right.End ? left.End : right.End;
        if (overlapEnd <= overlapStart)
        {
            return 0.0;
        }

        var unionStart = left.Start < right.Start ? left.Start : right.Start;
        var unionEnd = left.End > right.End ? left.End : right.End;
        var union = Math.Max(0.001, (unionEnd - unionStart).TotalSeconds);
        return Math.Clamp((overlapEnd - overlapStart).TotalSeconds / union, 0.0, 1.0);
    }

    private static double CalculateTextSimilarity(string left, string right)
    {
        var leftNormalized = NormalizeComparisonText(left);
        var rightNormalized = NormalizeComparisonText(right);
        if (leftNormalized.Length == 0 || rightNormalized.Length == 0)
        {
            return 0.0;
        }

        if (string.Equals(leftNormalized, rightNormalized, StringComparison.Ordinal))
        {
            return 1.0;
        }

        return Math.Max(
            CalculateTokenJaccard(leftNormalized, rightNormalized),
            CalculateBigramDice(leftNormalized, rightNormalized));
    }

    private static double CalculateTokenJaccard(string left, string right)
    {
        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0.0;
        }

        var intersection = leftTokens.Count(token => rightTokens.Contains(token));
        var union = leftTokens.Count + rightTokens.Count - intersection;
        return union == 0 ? 0.0 : intersection / (double)union;
    }

    private static double CalculateBigramDice(string left, string right)
    {
        var leftBigrams = CreateBigrams(left);
        var rightBigrams = CreateBigrams(right);
        if (leftBigrams.Count == 0 || rightBigrams.Count == 0)
        {
            return 0.0;
        }

        var remaining = new Dictionary<string, int>(rightBigrams, StringComparer.Ordinal);
        var matches = 0;
        foreach (var bigram in leftBigrams.Keys)
        {
            if (!remaining.TryGetValue(bigram, out var rightCount))
            {
                continue;
            }

            var matchCount = Math.Min(leftBigrams[bigram], rightCount);
            matches += matchCount;
            remaining[bigram] = rightCount - matchCount;
        }

        return (2.0 * matches) / (leftBigrams.Values.Sum() + rightBigrams.Values.Sum());
    }

    private static Dictionary<string, int> CreateBigrams(string value)
    {
        var normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (normalized.Length == 1)
        {
            counts[normalized] = 1;
            return counts;
        }

        for (var index = 0; index < normalized.Length - 1; index++)
        {
            var bigram = normalized.Substring(index, 2);
            counts.TryGetValue(bigram, out var count);
            counts[bigram] = count + 1;
        }

        return counts;
    }

    private static string NormalizeComparisonText(string text)
    {
        var builder = new StringBuilder(text.Length);
        var lastWasSpace = true;
        foreach (var character in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasSpace = false;
            }
            else if (!lastWasSpace && char.IsWhiteSpace(character))
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static string NormalizeDisplayText(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private sealed record CueCandidate(int RunIndex, SubtitleCue Cue);

    private sealed class CueCluster
    {
        public CueCluster(CueCandidate candidate)
        {
            Items.Add(candidate);
        }

        public List<CueCandidate> Items { get; } = [];

        public bool ContainsRun(int runIndex)
        {
            return Items.Any(item => item.RunIndex == runIndex);
        }
    }
}
