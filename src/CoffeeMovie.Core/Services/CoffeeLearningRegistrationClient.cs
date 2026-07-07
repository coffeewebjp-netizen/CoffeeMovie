using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CoffeeMovie.Core.Services;

public static class CoffeeLearningRegistrationDefaults
{
    public const string DefaultBaseUrl = "https://www.coffeewebjp.com";
    public const string DefaultDeckId = "deck-english-main";
}

public sealed record CoffeeLearningConnectionSettings(
    string? BaseUrl,
    string? DeckId,
    string? AuthHeader,
    string? ScoringMode = null,
    string? ScoringAiAgentCommand = null,
    string? ScoringAiAgentModel = null,
    string? ScoringAiAgentArguments = null,
    string? ScoringProvider = null,
    string? ScoringProviderBaseUrl = null,
    string? ScoringProviderModel = null,
    string? ScoringProviderApiKey = null);

public sealed record CoffeeLearningWordRegistrationRequest(
    string Word,
    string Meaning,
    string? Memo,
    string? Cefr,
    IReadOnlyList<string>? Labels = null,
    int? Point = null,
    bool AutoAnalyze = true);

public sealed record CoffeeLearningWordRegistrationResult(
    string? WordId,
    string? DeckId);

public sealed record CoffeeLearningDeckDiscoveryResult(
    string? DeckId,
    string? DeckName);

public sealed class CoffeeLearningRegistrationClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CoffeeLearningRegistrationClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(25) })
    {
    }

    public CoffeeLearningRegistrationClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public static bool IsConfigured(CoffeeLearningConnectionSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.BaseUrl)
            && !string.IsNullOrWhiteSpace(settings.DeckId)
            && !string.IsNullOrWhiteSpace(settings.AuthHeader);
    }

    public Task<CoffeeLearningWordRegistrationResult> RegisterWordAsync(
        CoffeeLearningConnectionSettings settings,
        CoffeeLearningWordRegistrationRequest registration,
        CancellationToken cancellationToken = default)
    {
        return RegisterWordAsync(
            settings.BaseUrl,
            settings.DeckId,
            settings.AuthHeader,
            registration,
            cancellationToken);
    }

    public async Task<CoffeeLearningWordRegistrationResult> RegisterWordAsync(
        string? baseUrl,
        string? deckId,
        string? authHeader,
        CoffeeLearningWordRegistrationRequest registration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(registration.Word))
        {
            throw new InvalidOperationException("CoffeeLearning word is empty.");
        }

        if (string.IsNullOrWhiteSpace(registration.Meaning))
        {
            throw new InvalidOperationException("CoffeeLearning meaning is empty.");
        }

        var normalizedBaseUrl = NormalizeBaseUrl(baseUrl);
        var normalizedDeckId = string.IsNullOrWhiteSpace(deckId)
            ? CoffeeLearningRegistrationDefaults.DefaultDeckId
            : deckId.Trim();
        var normalizedAuthHeader = authHeader?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedAuthHeader))
        {
            throw new InvalidOperationException("CoffeeLearning authorization header is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(normalizedBaseUrl, "words"));
        ApplyAuthHeader(request, normalizedAuthHeader);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(
            JsonSerializer.Serialize(BuildWordPayload(normalizedDeckId, registration), _jsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(CreateErrorMessage(response, responseBody));
        }

        var result = ParseRegistrationResult(responseBody);
        return string.IsNullOrWhiteSpace(result.DeckId)
            ? result with { DeckId = normalizedDeckId }
            : result;
    }

    public async Task<CoffeeLearningDeckDiscoveryResult> DiscoverCurrentDeckAsync(
        string? baseUrl,
        string? authHeader,
        CancellationToken cancellationToken = default)
    {
        var normalizedBaseUrl = NormalizeBaseUrl(baseUrl);
        var normalizedAuthHeader = authHeader?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedAuthHeader))
        {
            throw new InvalidOperationException("CoffeeLearning authorization header is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildEndpoint(normalizedBaseUrl, "decks"));
        ApplyAuthHeader(request, normalizedAuthHeader);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(CreateErrorMessage(response, responseBody));
        }

        return ParseCurrentDeck(responseBody);
    }

    public static string NormalizeBaseUrl(string? baseUrl)
    {
        var value = string.IsNullOrWhiteSpace(baseUrl)
            ? CoffeeLearningRegistrationDefaults.DefaultBaseUrl
            : baseUrl.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("CoffeeLearning API URL must be an http(s) URL.");
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    public static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
    }

    public static void ApplyAuthHeader(HttpRequestMessage request, string authHeader)
    {
        var value = authHeader.Trim();
        var colonIndex = value.IndexOf(':');
        if (colonIndex > 0)
        {
            var name = value[..colonIndex].Trim();
            var headerValue = value[(colonIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(headerValue))
            {
                request.Headers.TryAddWithoutValidation(name, headerValue);
                return;
            }
        }

        if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value[7..].Trim());
            return;
        }

        if (LooksLikeCookie(value))
        {
            request.Headers.TryAddWithoutValidation("Cookie", value);
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);
    }

    private static Uri BuildEndpoint(string baseUrl, string relativePath)
    {
        return new Uri(new Uri(EnsureTrailingSlash(baseUrl)), relativePath);
    }

    private static Dictionary<string, object?> BuildWordPayload(
        string deckId,
        CoffeeLearningWordRegistrationRequest registration)
    {
        var payload = new Dictionary<string, object?>
        {
            ["deckId"] = deckId,
            ["word"] = registration.Word.Trim(),
            ["meaning"] = registration.Meaning.Trim()
        };

        if (!string.IsNullOrWhiteSpace(registration.Memo))
        {
            payload["memo"] = registration.Memo.Trim();
        }

        if (!string.IsNullOrWhiteSpace(registration.Cefr))
        {
            payload["cefr"] = registration.Cefr.Trim().ToUpperInvariant();
        }

        if (registration.Point is int point)
        {
            payload["point"] = Math.Clamp(point, 1, 3000);
        }

        if (registration.AutoAnalyze)
        {
            payload["autoAnalyze"] = true;
        }

        var labelNames = registration.Labels?
            .Select(label => label.Trim())
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (labelNames is { Length: > 0 })
        {
            payload["labelNames"] = labelNames;
        }

        return payload;
    }

    private static bool LooksLikeCookie(string value)
    {
        return value.Contains('=')
            && (value.Contains("connect.sid", StringComparison.OrdinalIgnoreCase)
                || value.Contains(';'));
    }

    private static CoffeeLearningDeckDiscoveryResult ParseCurrentDeck(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new CoffeeLearningDeckDiscoveryResult(null, null);
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var deckId = GetString(root, "currentDeckId") ?? CoffeeLearningRegistrationDefaults.DefaultDeckId;
        var deckName = default(string);
        if (root.TryGetProperty("decks", out var decks) && decks.ValueKind == JsonValueKind.Array)
        {
            foreach (var deck in decks.EnumerateArray())
            {
                if (string.Equals(GetString(deck, "id"), deckId, StringComparison.Ordinal))
                {
                    deckName = GetString(deck, "name");
                    break;
                }
            }
        }

        return new CoffeeLearningDeckDiscoveryResult(deckId, deckName);
    }

    private static CoffeeLearningWordRegistrationResult ParseRegistrationResult(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new CoffeeLearningWordRegistrationResult(null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return new CoffeeLearningWordRegistrationResult(
                GetString(root, "id"),
                GetString(root, "deckId"));
        }
        catch
        {
            return new CoffeeLearningWordRegistrationResult(null, null);
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (TryGetDirectString(element, propertyName, out var value))
        {
            return value;
        }

        foreach (var containerName in new[] { "word", "data", "item" })
        {
            if (element.TryGetProperty(containerName, out var container)
                && container.ValueKind == JsonValueKind.Object
                && TryGetDirectString(container, propertyName, out value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryGetDirectString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string CreateErrorMessage(HttpResponseMessage response, string responseBody)
    {
        var body = string.IsNullOrWhiteSpace(responseBody)
            ? string.Empty
            : responseBody.Trim();
        if (body.Length > 400)
        {
            body = body[..400];
        }

        var prefix = response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
            ? "CoffeeLearning authorization failed. Refresh the CoffeeLearning token or cookie."
            : $"CoffeeLearning API request failed ({(int)response.StatusCode}).";
        return string.IsNullOrWhiteSpace(body) ? prefix : $"{prefix}\n{body}";
    }
}
