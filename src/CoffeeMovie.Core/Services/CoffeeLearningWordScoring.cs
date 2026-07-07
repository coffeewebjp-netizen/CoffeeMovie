using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoffeeMovie.Core.Services;

public static class CoffeeLearningScoringDefaults
{
    public const string ModeAiAgent = "ai-agent";
    public const string ModeAiProvider = "ai-provider";
    public const string ModeCoffeeLearning = "coffee-learning";
    public const string ModeSimple = "simple";

    public const string ProviderOpenAi = "openai";
    public const string ProviderGemini = "gemini";
    public const string ProviderDeepSeek = "deepseek";
    public const string ProviderLocal = "local";

    public const string DefaultAiAgentCommand = "codex-spark";
    public const string DefaultAiAgentModel = "gpt-5.3-codex-spark";
    public const string DefaultAiAgentArguments = "exec --full-auto -C \"{workingDir}\" --skip-git-repo-check \"Read {promptFile} and write strict JSON to {outputFile}.\"";

    public const string DefaultOpenAiBaseUrl = "https://api.openai.com/v1";
    public const string DefaultGeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    public const string DefaultDeepSeekBaseUrl = "https://api.deepseek.com/v1";
    public const string DefaultLocalBaseUrl = "http://127.0.0.1:11434/v1";
}

public sealed record CoffeeLearningWordScoreInput(
    string Word,
    string Meaning,
    string? Memo,
    IReadOnlyList<string>? Labels);

public sealed record CoffeeLearningWordScore(
    string Judgement,
    string Cefr,
    int Point,
    string? BetterMeaning,
    string? Diagnosis,
    bool IsAiGenerated);

public sealed record CoffeeLearningAiProviderScoringSettings(
    string? Provider,
    string? BaseUrl,
    string? Model,
    string? ApiKey);

public static class CoffeeLearningWordScoreEstimator
{
    public static CoffeeLearningWordScore Estimate(string? cefr, string word)
    {
        var normalizedCefr = NormalizeCefr(cefr) ?? EstimateCefr(word);
        return new CoffeeLearningWordScore(
            "simple",
            normalizedCefr,
            EstimatePoint(normalizedCefr, word),
            null,
            null,
            false);
    }

    public static string? NormalizeCefr(string? cefr)
    {
        var value = cefr?.Trim().ToUpperInvariant();
        return value is "A1" or "A2" or "B1" or "B2" or "C1" or "C2"
            ? value
            : null;
    }

    private static string EstimateCefr(string word)
    {
        var tokenCount = word.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var letterCount = word.Count(char.IsLetter);
        if (tokenCount >= 8)
        {
            return "B2";
        }

        if (tokenCount >= 4 || letterCount >= 14)
        {
            return "B1";
        }

        return letterCount >= 9 ? "A2" : "A1";
    }

    private static int EstimatePoint(string cefr, string word)
    {
        var (min, max, basis) = cefr switch
        {
            "A1" => (1, 100, 70),
            "A2" => (101, 300, 210),
            "B1" => (301, 600, 450),
            "B2" => (601, 999, 780),
            "C1" => (1000, 1999, 1350),
            "C2" => (2000, 3000, 2300),
            _ => (101, 300, 210)
        };
        var tokenCount = word.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var letterCount = word.Count(char.IsLetter);
        var bonus = Math.Min(180, Math.Max(0, tokenCount - 1) * 25 + Math.Max(0, letterCount - 8) * 4);
        return Math.Clamp(basis + bonus, min, max);
    }
}

public static class CoffeeLearningWordScorePrompt
{
    public static string Build(CoffeeLearningWordScoreInput input)
    {
        var labels = input.Labels is { Count: > 0 }
            ? string.Join(", ", input.Labels.Select(label => label.Trim()).Where(label => !string.IsNullOrWhiteSpace(label)))
            : "(none)";

        return $$"""
You are a strict and capable English teacher.
The learner is registering the following English word or phrase from a movie subtitle.

Input:
- Word or phrase: "{{input.Word}}"
- Meaning/Japanese translation: "{{input.Meaning}}"
- Existing memo/context: "{{input.Memo ?? string.Empty}}"
- Labels: "{{labels}}"

Judge the item objectively and return JSON only.

Level rules:
- A1: 1-100 points, basic CEFR A1.
- A2: 101-300 points, elementary CEFR A2.
- B1: 301-600 points, intermediate CEFR B1.
- B2: 601-999 points, upper-intermediate CEFR B2.
- C1: 1000-1999 points. Practical business English or dry scientific/medical/IT terms.
- C2: 2000-3000 points. Literary, poetic, philosophical, archaic, highly educated native vocabulary, or rhetorically complex phrasing. If a technical term is literary, ornate, or archaic in context, choose C2.

Point rules:
- Score by difficulty, abstractness, and frequency. Do not choose randomly.
- Keep point inside the selected CEFR range.

Output schema:
{
  "judgement": "妥当 | 惜しい | 修正推奨 | 素晴らしい",
  "cefr": "A1 | A2 | B1 | B2 | C1 | C2",
  "point": 1,
  "better_meaning": "a more natural or accurate Japanese meaning, especially when judgement is 惜しい or 修正推奨",
  "diagnosis": "Japanese explanation of the reason for the judgement"
}

Return valid UTF-8 JSON only. Do not use Markdown or code fences.
""";
    }
}

public static class CoffeeLearningWordScoreParser
{
    public static CoffeeLearningWordScore Parse(string text)
    {
        var json = ExtractJsonObject(text);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var cefr = CoffeeLearningWordScoreEstimator.NormalizeCefr(GetString(root, "cefr"))
            ?? throw new InvalidOperationException("AI scoring JSON did not contain a valid cefr.");
        var point = GetInt(root, "point")
            ?? throw new InvalidOperationException("AI scoring JSON did not contain a valid point.");

        return new CoffeeLearningWordScore(
            GetString(root, "judgement") ?? "妥当",
            cefr,
            Math.Clamp(point, 1, 3000),
            GetString(root, "better_meaning") ?? GetString(root, "betterMeaning"),
            GetString(root, "diagnosis"),
            true);
    }

    private static string ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("AI scoring output was empty.");
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return trimmed;
        }

        var match = Regex.Match(trimmed, @"\{[\s\S]*\}");
        if (!match.Success)
        {
            throw new InvalidOperationException("AI scoring output did not contain JSON.");
        }

        return match.Value;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(property.ToString(), out number) ? number : null;
    }
}

public sealed class CoffeeLearningAiProviderScoringService
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public CoffeeLearningAiProviderScoringService()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(45) })
    {
    }

    public CoffeeLearningAiProviderScoringService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CoffeeLearningWordScore> ScoreAsync(
        CoffeeLearningAiProviderScoringSettings settings,
        CoffeeLearningWordScoreInput input,
        CancellationToken cancellationToken = default)
    {
        var provider = Normalize(settings.Provider, CoffeeLearningScoringDefaults.ProviderOpenAi);
        var prompt = CoffeeLearningWordScorePrompt.Build(input);
        var responseText = provider == CoffeeLearningScoringDefaults.ProviderGemini
            ? await ScoreWithGeminiAsync(settings, prompt, cancellationToken)
            : await ScoreWithOpenAiCompatibleAsync(provider, settings, prompt, cancellationToken);
        return CoffeeLearningWordScoreParser.Parse(responseText);
    }

    private async Task<string> ScoreWithOpenAiCompatibleAsync(
        string provider,
        CoffeeLearningAiProviderScoringSettings settings,
        string prompt,
        CancellationToken cancellationToken)
    {
        var baseUrl = NormalizeBaseUrl(settings.BaseUrl, provider switch
        {
            CoffeeLearningScoringDefaults.ProviderDeepSeek => CoffeeLearningScoringDefaults.DefaultDeepSeekBaseUrl,
            CoffeeLearningScoringDefaults.ProviderLocal => CoffeeLearningScoringDefaults.DefaultLocalBaseUrl,
            _ => CoffeeLearningScoringDefaults.DefaultOpenAiBaseUrl
        });
        var model = Normalize(settings.Model, provider switch
        {
            CoffeeLearningScoringDefaults.ProviderDeepSeek => "deepseek-chat",
            CoffeeLearningScoringDefaults.ProviderLocal => "llama3.1",
            _ => "gpt-4.1-mini"
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(EnsureTrailingSlash(baseUrl)), "chat/completions"));
        var apiKey = settings.ApiKey?.Trim();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                model,
                temperature = 0.1,
                messages = new[]
                {
                    new { role = "system", content = "Return strict JSON only." },
                    new { role = "user", content = prompt }
                }
            }, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"AI provider scoring failed ({(int)response.StatusCode}): {TrimForError(body)}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content))
        {
            return content.GetString() ?? string.Empty;
        }

        return body;
    }

    private async Task<string> ScoreWithGeminiAsync(
        CoffeeLearningAiProviderScoringSettings settings,
        string prompt,
        CancellationToken cancellationToken)
    {
        var apiKey = settings.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Gemini scoring requires an API key.");
        }

        var baseUrl = NormalizeBaseUrl(settings.BaseUrl, CoffeeLearningScoringDefaults.DefaultGeminiBaseUrl);
        var model = Normalize(settings.Model, "gemini-2.5-flash");
        var endpoint = $"{EnsureTrailingSlash(baseUrl)}models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = prompt } }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.1,
                    responseMimeType = "application/json"
                }
            }, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Gemini scoring failed ({(int)response.StatusCode}): {TrimForError(body)}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.TryGetProperty("candidates", out var candidates)
            && candidates.ValueKind == JsonValueKind.Array
            && candidates.GetArrayLength() > 0
            && candidates[0].TryGetProperty("content", out var content)
            && content.TryGetProperty("parts", out var parts)
            && parts.ValueKind == JsonValueKind.Array
            && parts.GetArrayLength() > 0
            && parts[0].TryGetProperty("text", out var text))
        {
            return text.GetString() ?? string.Empty;
        }

        return body;
    }

    private static string Normalize(string? value, string fallback)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string NormalizeBaseUrl(string? value, string fallback)
    {
        var normalized = Normalize(value, fallback).TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("AI provider base URL must be http(s).");
        }

        return uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath.TrimEnd('/');
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
    }

    private static string TrimForError(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400];
    }
}
