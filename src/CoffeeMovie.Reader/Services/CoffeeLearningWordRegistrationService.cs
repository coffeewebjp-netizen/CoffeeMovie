using CoffeeMovie.Core.Services;
using CoffeeMovie.Reader.Models;
using Microsoft.Maui.Storage;

namespace CoffeeMovie.Reader.Services;

public sealed class CoffeeLearningWordRegistrationService
{
    public const string DefaultBaseUrl = CoffeeLearningRegistrationDefaults.DefaultBaseUrl;
    public const string DefaultDeckId = CoffeeLearningRegistrationDefaults.DefaultDeckId;

    private const string AuthHeaderKey = "coffee-movie-coffee-learning-auth-header";
    private const string ScoringProviderApiKeyKey = "coffee-movie-coffee-learning-scoring-provider-api-key";

    private readonly ReaderSyncSettingsService _settingsService;
    private readonly CoffeeLearningAiProviderScoringService _scoringService = new();
    private readonly CoffeeLearningRegistrationClient _registrationClient = new();

    public CoffeeLearningWordRegistrationService(ReaderSyncSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public Task<ReaderSyncSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        return _settingsService.LoadSettingsAsync(cancellationToken);
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        var authHeader = await GetAuthHeaderAsync();
        return !string.IsNullOrWhiteSpace(settings.CoffeeLearningBaseUrl)
            && !string.IsNullOrWhiteSpace(settings.CoffeeLearningDeckId)
            && !string.IsNullOrWhiteSpace(authHeader);
    }

    public async Task<ReaderSyncSettings> SaveConfigurationAsync(
        string baseUrl,
        string deckId,
        string? authHeader,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        settings.CoffeeLearningBaseUrl = NormalizeBaseUrl(baseUrl);
        settings.CoffeeLearningDeckId = string.IsNullOrWhiteSpace(deckId)
            ? DefaultDeckId
            : deckId.Trim();

        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            await SaveAuthHeaderAsync(authHeader.Trim());
        }

        await _settingsService.SaveSettingsAsync(settings, cancellationToken);
        return settings;
    }

    public async Task<ReaderSyncSettings> SaveScoringConfigurationAsync(
        string mode,
        string provider,
        string? providerBaseUrl,
        string? providerModel,
        string? providerApiKey,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        settings.CoffeeLearningScoringMode = NormalizeScoringMode(mode);
        settings.CoffeeLearningScoringProvider = NormalizeScoringProvider(provider);
        settings.CoffeeLearningScoringProviderBaseUrl = NormalizeOptionalText(providerBaseUrl);
        settings.CoffeeLearningScoringProviderModel = NormalizeOptionalText(providerModel);
        if (!string.IsNullOrWhiteSpace(providerApiKey))
        {
            await SecureStorage.SetAsync(ScoringProviderApiKeyKey, providerApiKey.Trim());
        }

        await _settingsService.SaveSettingsAsync(settings, cancellationToken);
        return settings;
    }

    public async Task<CoffeeLearningRegistrationScore> ScoreForRegistrationAsync(
        CoffeeLearningWordScoreInput input,
        CoffeeLearningWordScore fallbackScore,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        var mode = NormalizeScoringMode(settings.CoffeeLearningScoringMode);
        if (mode == CoffeeLearningScoringDefaults.ModeCoffeeLearning)
        {
            return new CoffeeLearningRegistrationScore(fallbackScore, AutoAnalyze: true, Warning: null);
        }

        if (mode == CoffeeLearningScoringDefaults.ModeSimple)
        {
            return new CoffeeLearningRegistrationScore(fallbackScore, AutoAnalyze: false, Warning: null);
        }

        try
        {
            var score = await _scoringService.ScoreAsync(
                new CoffeeLearningAiProviderScoringSettings(
                    settings.CoffeeLearningScoringProvider,
                    settings.CoffeeLearningScoringProviderBaseUrl,
                    settings.CoffeeLearningScoringProviderModel,
                    await GetScoringProviderApiKeyAsync()),
                input,
                cancellationToken);
            return new CoffeeLearningRegistrationScore(score, AutoAnalyze: false, Warning: null);
        }
        catch (Exception ex)
        {
            return new CoffeeLearningRegistrationScore(fallbackScore, AutoAnalyze: false, Warning: ex.Message);
        }
    }

    public async Task<CoffeeLearningAuthCaptureResult> CaptureAuthCookieFromWebViewAsync(
        string? baseUrl = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        var normalizedBaseUrl = NormalizeBaseUrl(string.IsNullOrWhiteSpace(baseUrl)
            ? settings.CoffeeLearningBaseUrl ?? DefaultBaseUrl
            : baseUrl);

#if ANDROID
        Android.Webkit.CookieManager.Instance?.Flush();
        var cookie = ReadAndroidCookie(normalizedBaseUrl);
        if (string.IsNullOrWhiteSpace(cookie) || !LooksLikeCoffeeLearningSessionCookie(cookie))
        {
            settings.CoffeeLearningBaseUrl = normalizedBaseUrl;
            if (string.IsNullOrWhiteSpace(settings.CoffeeLearningDeckId))
            {
                settings.CoffeeLearningDeckId = DefaultDeckId;
            }

            await _settingsService.SaveSettingsAsync(settings, cancellationToken);
            return new CoffeeLearningAuthCaptureResult(
                false,
                "CoffeeLearningのログインCookieがまだ見つかりません。ログイン完了後にもう一度取得してください。",
                null,
                settings.CoffeeLearningDeckId,
                null);
        }

        var authHeader = "Cookie: " + cookie.Trim();
        settings.CoffeeLearningBaseUrl = normalizedBaseUrl;
        if (string.IsNullOrWhiteSpace(settings.CoffeeLearningDeckId))
        {
            settings.CoffeeLearningDeckId = DefaultDeckId;
        }

        await SaveAuthHeaderAsync(authHeader);
        await _settingsService.SaveSettingsAsync(settings, cancellationToken);

        try
        {
            var deck = await RefreshCurrentDeckAsync(cancellationToken);
            return new CoffeeLearningAuthCaptureResult(
                true,
                string.IsNullOrWhiteSpace(deck.DeckName)
                    ? $"CoffeeLearning認証を保存しました。登録先: {deck.DeckId ?? settings.CoffeeLearningDeckId}"
                    : $"CoffeeLearning認証を保存しました。登録先: {deck.DeckName}",
                authHeader,
                deck.DeckId ?? settings.CoffeeLearningDeckId,
                deck.DeckName);
        }
        catch
        {
            return new CoffeeLearningAuthCaptureResult(
                true,
                $"CoffeeLearning認証を保存しました。登録先: {settings.CoffeeLearningDeckId}",
                authHeader,
                settings.CoffeeLearningDeckId,
                null);
        }
#else
        return new CoffeeLearningAuthCaptureResult(
            false,
            "この端末ではWebView Cookie取得に未対応です。認証ヘッダーを手入力してください。",
            null,
            settings.CoffeeLearningDeckId,
            null);
#endif
    }

    public async Task<CoffeeLearningDeckDiscoveryResult> RefreshCurrentDeckAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        var baseUrl = GetConfiguredBaseUrl(settings);
        var authHeader = await GetRequiredAuthHeaderAsync();
        var deck = await _registrationClient.DiscoverCurrentDeckAsync(baseUrl, authHeader, cancellationToken);
        if (!string.IsNullOrWhiteSpace(deck.DeckId))
        {
            settings.CoffeeLearningBaseUrl = baseUrl;
            settings.CoffeeLearningDeckId = deck.DeckId;
            await _settingsService.SaveSettingsAsync(settings, cancellationToken);
        }

        return deck;
    }
    public async Task<CoffeeLearningWordRegistrationResult> RegisterWordAsync(
        CoffeeLearningWordRegistrationRequest registration,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        var baseUrl = GetConfiguredBaseUrl(settings);
        var deckId = string.IsNullOrWhiteSpace(settings.CoffeeLearningDeckId)
            ? DefaultDeckId
            : settings.CoffeeLearningDeckId.Trim();
        var authHeader = await GetRequiredAuthHeaderAsync();
        return await _registrationClient.RegisterWordAsync(
            baseUrl,
            deckId,
            authHeader,
            registration,
            cancellationToken);
    }
    private static string NormalizeBaseUrl(string baseUrl)
    {
        return CoffeeLearningRegistrationClient.NormalizeBaseUrl(baseUrl);
    }

    private static string GetConfiguredBaseUrl(ReaderSyncSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.CoffeeLearningBaseUrl)
            ? DefaultBaseUrl
            : NormalizeBaseUrl(settings.CoffeeLearningBaseUrl);
    }

    private static string EnsureTrailingSlash(string value)
    {
        return CoffeeLearningRegistrationClient.EnsureTrailingSlash(value);
    }

    private static async Task<string?> GetAuthHeaderAsync()
    {
        try
        {
            var value = await SecureStorage.Default.GetAsync(AuthHeaderKey);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> GetRequiredAuthHeaderAsync()
    {
        var authHeader = await GetAuthHeaderAsync();
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            throw new InvalidOperationException("CoffeeLearningの認証ヘッダーが未設定です。");
        }

        return authHeader;
    }

    private static Task SaveAuthHeaderAsync(string authHeader)
    {
        return SecureStorage.Default.SetAsync(AuthHeaderKey, authHeader.Trim());
    }

    private static async Task<string?> GetScoringProviderApiKeyAsync()
    {
        try
        {
            var value = await SecureStorage.Default.GetAsync(ScoringProviderApiKeyKey);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeScoringMode(string? mode)
    {
        var value = mode?.Trim().ToLowerInvariant();
        return value switch
        {
            CoffeeLearningScoringDefaults.ModeCoffeeLearning => CoffeeLearningScoringDefaults.ModeCoffeeLearning,
            CoffeeLearningScoringDefaults.ModeSimple => CoffeeLearningScoringDefaults.ModeSimple,
            _ => CoffeeLearningScoringDefaults.ModeAiProvider
        };
    }

    private static string NormalizeScoringProvider(string? provider)
    {
        var value = provider?.Trim().ToLowerInvariant();
        return value switch
        {
            "gpt" or "openai" => CoffeeLearningScoringDefaults.ProviderOpenAi,
            CoffeeLearningScoringDefaults.ProviderGemini => CoffeeLearningScoringDefaults.ProviderGemini,
            "deepseek" or "deepseek-chat" => CoffeeLearningScoringDefaults.ProviderDeepSeek,
            "local" or "local-llm" or "ollama" => CoffeeLearningScoringDefaults.ProviderLocal,
            _ => CoffeeLearningScoringDefaults.ProviderOpenAi
        };
    }

    private static string? NormalizeOptionalText(string? text)
    {
        var normalized = text?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

#if ANDROID
    private static string? ReadAndroidCookie(string baseUrl)
    {
        var manager = Android.Webkit.CookieManager.Instance;
        if (manager is null)
        {
            return null;
        }

        manager.SetAcceptCookie(true);
        var cookie = manager.GetCookie(baseUrl);
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            return cookie;
        }

        return manager.GetCookie(EnsureTrailingSlash(baseUrl));
    }
#endif

    private static bool LooksLikeCoffeeLearningSessionCookie(string value)
    {
        return value.Contains("connect.sid", StringComparison.OrdinalIgnoreCase);
    }
}
public sealed record CoffeeLearningRegistrationScore(
    CoffeeLearningWordScore Score,
    bool AutoAnalyze,
    string? Warning);


public sealed record CoffeeLearningAuthCaptureResult(
    bool Success,
    string Message,
    string? AuthHeader,
    string? DeckId,
    string? DeckName);
