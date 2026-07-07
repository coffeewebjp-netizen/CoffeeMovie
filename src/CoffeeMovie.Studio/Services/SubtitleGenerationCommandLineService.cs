using System.Diagnostics;
using System.Text;

namespace CoffeeMovie.Studio.Services;

public static class SubtitleGenerationCommandLineService
{
    public static string ApplyArgumentTemplate(string template, IReadOnlyDictionary<string, string> replacements)
    {
        var result = template;
        foreach (var (key, value) in replacements)
        {
            result = result.Replace("{" + key + "}", value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    public static string FormatProcessCommand(ProcessStartInfo startInfo)
    {
        return string.Join(
            ' ',
            new[] { QuoteCommandPart(startInfo.FileName) }.Concat(startInfo.ArgumentList.Select(QuoteCommandPart)));
    }

    public static List<string> SplitCommandLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var arguments = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        foreach (var character in text)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (builder.Length > 0)
                {
                    arguments.Add(builder.ToString());
                    builder.Clear();
                }

                continue;
            }

            builder.Append(character);
        }

        if (builder.Length > 0)
        {
            arguments.Add(builder.ToString());
        }

        return arguments;
    }

    private static string QuoteCommandPart(string value)
    {
        return value.Any(char.IsWhiteSpace)
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;
    }
}
