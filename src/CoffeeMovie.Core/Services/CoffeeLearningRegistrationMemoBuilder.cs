namespace CoffeeMovie.Core.Services;

public static class CoffeeLearningRegistrationMemoBuilder
{
    public static string Build(string? memo, CoffeeLearningWordScore score)
    {
        if (!score.IsAiGenerated)
        {
            return memo ?? string.Empty;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(memo))
        {
            parts.Add(memo.Trim());
        }

        parts.Add($"AI scoring: {score.Judgement} / CEFR {score.Cefr} / {score.Point}pt");
        if (!string.IsNullOrWhiteSpace(score.BetterMeaning))
        {
            parts.Add($"Better meaning: {score.BetterMeaning.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(score.Diagnosis))
        {
            parts.Add($"Diagnosis: {score.Diagnosis.Trim()}");
        }

        return string.Join("\n", parts);
    }
}
