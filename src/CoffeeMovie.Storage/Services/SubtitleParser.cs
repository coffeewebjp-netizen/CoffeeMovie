using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CoffeeMovie.Core.Models;

namespace CoffeeMovie.Storage.Services;

public static class SubtitleParser
{
    private static readonly Regex BlankLinePattern = new(@"\n\s*\n", RegexOptions.Compiled);

    public static SubtitleDocument Parse(string content, string? sourceFileName = null)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        var normalized = NormalizeLineEndings(content).Trim('\uFEFF');
        var cues = new List<SubtitleCue>();

        foreach (var block in BlankLinePattern.Split(normalized))
        {
            var lines = block
                .Split('\n')
                .Select(line => line.TrimEnd())
                .Where(line => line.Length > 0)
                .ToList();

            if (lines.Count == 0)
            {
                continue;
            }

            var timeLineIndex = lines.FindIndex(line => line.Contains("-->", StringComparison.Ordinal));
            if (timeLineIndex < 0)
            {
                continue;
            }

            if (!TryParseTimingLine(lines[timeLineIndex], out var start, out var end))
            {
                continue;
            }

            var text = NormalizeCueText(string.Join('\n', lines.Skip(timeLineIndex + 1)));
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            cues.Add(new SubtitleCue
            {
                Index = cues.Count + 1,
                Start = start,
                End = end,
                Text = text
            });
        }

        return new SubtitleDocument
        {
            Format = InferFormat(normalized, sourceFileName),
            SourceFileName = sourceFileName,
            Cues = cues
        };
    }

    public static string ToWebVtt(IEnumerable<SubtitleCue> cues)
    {
        var builder = new StringBuilder();
        builder.AppendLine("WEBVTT");
        builder.AppendLine();

        foreach (var cue in cues)
        {
            builder.AppendLine(cue.Index.ToString(CultureInfo.InvariantCulture));
            builder
                .Append(FormatWebVttTimestamp(cue.Start))
                .Append(" --> ")
                .AppendLine(FormatWebVttTimestamp(cue.End));

            builder.AppendLine(NormalizeCueText(cue.Text));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static SubtitleFormat InferFormat(string content, string? sourceFileName = null)
    {
        if (!string.IsNullOrWhiteSpace(sourceFileName))
        {
            var extension = Path.GetExtension(sourceFileName);
            if (extension.Equals(".srt", StringComparison.OrdinalIgnoreCase))
            {
                return SubtitleFormat.Srt;
            }

            if (extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase))
            {
                return SubtitleFormat.WebVtt;
            }
        }

        var trimmed = content.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return trimmed.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase)
            ? SubtitleFormat.WebVtt
            : SubtitleFormat.Srt;
    }

    public static string FormatWebVttTimestamp(TimeSpan value)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}");
    }

    private static bool TryParseTimingLine(string line, out TimeSpan start, out TimeSpan end)
    {
        start = TimeSpan.Zero;
        end = TimeSpan.Zero;

        var arrowIndex = line.IndexOf("-->", StringComparison.Ordinal);
        if (arrowIndex < 0)
        {
            return false;
        }

        var left = line[..arrowIndex].Trim();
        var right = line[(arrowIndex + 3)..].Trim();
        var rightTimestamp = FirstToken(right);

        return TryParseTimestamp(left, out start)
            && TryParseTimestamp(rightTimestamp, out end)
            && end >= start;
    }

    private static bool TryParseTimestamp(string value, out TimeSpan timestamp)
    {
        timestamp = TimeSpan.Zero;
        var normalized = value.Trim().Replace(',', '.');
        if (normalized.Length == 0)
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

        timestamp = TimeSpan.FromHours(hours)
            + TimeSpan.FromMinutes(minutes)
            + TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static string FirstToken(string value)
    {
        var parts = value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? string.Empty : parts[0];
    }

    private static string NormalizeCueText(string text)
    {
        return NormalizeLineEndings(text).Trim();
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }
}

