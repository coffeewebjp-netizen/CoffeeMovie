using System.Text;

namespace CoffeeMovie.Reader.Services;

public static class ReaderShadowingScorer
{
    public static double CalculateAccuracy(string targetText, string transcript)
    {
        var expected = TokenizeForShadowing(targetText);
        var actual = TokenizeForShadowing(transcript);
        if (expected.Count == 0 || actual.Count == 0)
        {
            return 0d;
        }

        var distance = CalculateEditDistance(expected, actual);
        var denominator = Math.Max(expected.Count, actual.Count);
        return Math.Clamp(1d - (double)distance / denominator, 0d, 1d);
    }

    private static List<string> TokenizeForShadowing(string text)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        foreach (var character in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                continue;
            }

            if (character is '\'' or '’')
            {
                continue;
            }

            AddToken();
        }

        AddToken();
        return tokens;

        void AddToken()
        {
            if (builder.Length == 0)
            {
                return;
            }

            tokens.Add(builder.ToString());
            builder.Clear();
        }
    }

    private static int CalculateEditDistance(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var previous = new int[actual.Count + 1];
        var current = new int[actual.Count + 1];
        for (var column = 0; column <= actual.Count; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= expected.Count; row++)
        {
            current[0] = row;
            for (var column = 1; column <= actual.Count; column++)
            {
                var cost = string.Equals(expected[row - 1], actual[column - 1], StringComparison.Ordinal)
                    ? 0
                    : 1;
                current[column] = Math.Min(
                    Math.Min(previous[column] + 1, current[column - 1] + 1),
                    previous[column - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[actual.Count];
    }
}
