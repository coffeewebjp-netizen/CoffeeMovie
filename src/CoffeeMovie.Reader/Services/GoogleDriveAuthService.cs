using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoffeeMovie.Reader.Models;

namespace CoffeeMovie.Reader.Services;

public sealed class GoogleDriveAuthService
{
    private const string DriveScope = "https://www.googleapis.com/auth/drive.readonly https://www.googleapis.com/auth/drive.file";
    private const string BrowserRedirectUri = "net.coffeewebjp.coffeemovie.reader:/oauth2redirect";
    private const string AuthorizationUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    private const string RefreshTokenKey = "coffee-movie-google-drive-refresh-token";

    private readonly ReaderSyncSettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public GoogleDriveAuthService(ReaderSyncSettingsService settingsService, HttpClient httpClient)
    {
        _settingsService = settingsService;
        _httpClient = httpClient;
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        return !string.IsNullOrWhiteSpace(settings.GoogleDriveClientId)
            && !string.IsNullOrWhiteSpace(settings.GoogleDriveFolderId)
            && !string.IsNullOrWhiteSpace(await GetRefreshTokenAsync());
    }

    public async Task<ReaderSyncSettings> SaveConfigurationAsync(
        string clientId,
        string? clientSecret,
        string folderInput,
        CancellationToken cancellationToken = default)
    {
        var folderId = ExtractFolderId(folderInput);
        var normalizedClientId = clientId.Trim();
        var normalizedClientSecret = string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret.Trim();

        if (string.IsNullOrWhiteSpace(normalizedClientId))
        {
            throw new InvalidOperationException("Google OAuth Client ID を入力してください。");
        }

        if (normalizedClientId.StartsWith("GOCSPX-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Client ID欄にClient Secretが入力されています。Client IDは通常 .apps.googleusercontent.com で終わる値です。");
        }

        if (!normalizedClientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Client IDの形式が違うようです。Google CloudのクライアントIDを入力してください。");
        }

        if (!string.IsNullOrWhiteSpace(normalizedClientSecret)
            && normalizedClientSecret.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Client Secret欄にClient IDが入力されています。Client IDとClient Secretを入れ替えてください。");
        }

        if (string.IsNullOrWhiteSpace(folderId))
        {
            throw new InvalidOperationException("Google Drive フォルダURLまたはフォルダIDを入力してください。");
        }

        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        settings.GoogleDriveClientId = normalizedClientId;
        settings.GoogleDriveClientSecret = normalizedClientSecret;
        settings.GoogleDriveFolderId = folderId;
        settings.GoogleDriveFolderName = "Google Drive";
        await _settingsService.SaveSettingsAsync(settings, cancellationToken);
        return settings;
    }

    public async Task AuthorizeWithBrowserAsync(
        ReaderSyncSettings settings,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.GoogleDriveClientId))
        {
            throw new InvalidOperationException("Google OAuth Client ID が未設定です。");
        }

        var codeVerifier = CreateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var authUri = CreateAuthorizationUri(settings.GoogleDriveClientId, codeChallenge);

        progress?.Report("Googleログインを開いています...");
        var result = await WebAuthenticator.Default.AuthenticateAsync(authUri, new Uri(BrowserRedirectUri));
        if (!result.Properties.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            var error = result.Properties.TryGetValue("error", out var errorValue) ? errorValue : "authorization_failed";
            throw new InvalidOperationException($"Google認証に失敗しました: {error}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("Google認証トークンを取得しています...");

        var form = new Dictionary<string, string>
        {
            ["client_id"] = settings.GoogleDriveClientId,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = BrowserRedirectUri
        };
        if (!string.IsNullOrWhiteSpace(settings.GoogleDriveClientSecret))
        {
            form["client_secret"] = settings.GoogleDriveClientSecret;
        }

        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(TokenUrl, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google認証に失敗しました: {GetErrorMessage(body)}");
        }

        await SaveTokenResponseAsync(settings, body, cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        await ClearStoredGoogleDriveTokenAsync(settings, cancellationToken);
    }

    public async Task<string> GetValidAccessTokenAsync(
        ReaderSyncSettings settings,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken)
            && DateTimeOffset.UtcNow < _accessTokenExpiresAt.AddSeconds(-60))
        {
            return _accessToken;
        }

        if (string.IsNullOrWhiteSpace(settings.GoogleDriveClientId))
        {
            throw new InvalidOperationException("Google OAuth Client ID が未設定です。");
        }

        var refreshToken = await GetRefreshTokenAsync();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new InvalidOperationException("Google Driveに接続してください。");
        }

        var form = new Dictionary<string, string>
        {
            ["client_id"] = settings.GoogleDriveClientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        };
        if (!string.IsNullOrWhiteSpace(settings.GoogleDriveClientSecret))
        {
            form["client_secret"] = settings.GoogleDriveClientSecret;
        }

        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(TokenUrl, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (string.Equals(GetErrorCode(body), "invalid_grant", StringComparison.OrdinalIgnoreCase))
            {
                await ClearStoredGoogleDriveTokenAsync(settings, cancellationToken);
                throw new GoogleDriveReconnectRequiredException("Google Driveの認証期限が切れたか、Google側で取り消されています。もう一度Google Driveに接続してください。");
            }

            throw new InvalidOperationException($"Google Driveの再接続に失敗しました: {GetErrorMessage(body)}");
        }

        await SaveTokenResponseAsync(settings, body, cancellationToken, keepExistingRefreshToken: true);
        return _accessToken ?? throw new InvalidOperationException("Google Driveのアクセストークンを取得できませんでした。");
    }

    public void ClearCachedAccessToken()
    {
        _accessToken = null;
        _accessTokenExpiresAt = default;
    }

    public static string GetErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var description = GetString(root, "error_description");
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description;
            }

            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString() ?? body;
                }

                if (error.ValueKind == JsonValueKind.Object)
                {
                    var message = GetString(error, "message");
                    return string.IsNullOrWhiteSpace(message) ? body : message;
                }
            }
        }
        catch
        {
            // The response may be plain text or HTML.
        }

        return body.Length > 300 ? body[..300] : body;
    }

    private async Task ClearStoredGoogleDriveTokenAsync(
        ReaderSyncSettings settings,
        CancellationToken cancellationToken)
    {
        ClearCachedAccessToken();
        await SecureStorage.Default.SetAsync(RefreshTokenKey, string.Empty);
        settings.GoogleDriveConnectedAt = null;
        await _settingsService.SaveSettingsAsync(settings, cancellationToken);
    }

    private async Task SaveTokenResponseAsync(
        ReaderSyncSettings settings,
        string body,
        CancellationToken cancellationToken,
        bool keepExistingRefreshToken = false)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        _accessToken = GetString(root, "access_token");
        var expiresIn = GetInt32(root, "expires_in", 3600);
        _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

        var refreshToken = GetString(root, "refresh_token");
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await SecureStorage.Default.SetAsync(RefreshTokenKey, refreshToken);
        }
        else if (!keepExistingRefreshToken)
        {
            throw new InvalidOperationException("Google Driveの更新トークンを取得できませんでした。");
        }

        settings.GoogleDriveConnectedAt = DateTimeOffset.UtcNow;
        await _settingsService.SaveSettingsAsync(settings, cancellationToken);
    }

    private static async Task<string?> GetRefreshTokenAsync()
    {
        try
        {
            var token = await SecureStorage.Default.GetAsync(RefreshTokenKey);
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractFolderId(string input)
    {
        var value = input.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        const string marker = "/folders/";
        var markerIndex = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            value = value[(markerIndex + marker.Length)..];
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            value = ExtractQueryValue(uri.Query, "id");
        }

        var cutIndex = value.IndexOfAny(['?', '/', '&', '#']);
        if (cutIndex >= 0)
        {
            value = value[..cutIndex];
        }

        return value.Trim();
    }

    private static string ExtractQueryValue(string query, string key)
    {
        var normalized = query.TrimStart('?');
        foreach (var part in normalized.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            if (pieces.Length == 2 && string.Equals(Uri.UnescapeDataString(pieces[0]), key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pieces[1]);
            }
        }

        return string.Empty;
    }

    private static Uri CreateAuthorizationUri(string clientId, string codeChallenge)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = BrowserRedirectUri,
            ["response_type"] = "code",
            ["scope"] = DriveScope,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };

        var query = string.Join("&", parameters.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri($"{AuthorizationUrl}?{query}");
    }

    private static string CreateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Base64UrlEncode(bytes);
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int GetInt32(JsonElement element, string propertyName, int fallback)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(value.GetString(), out var parsed) ? parsed : fallback;
    }

    private static string GetErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return GetString(document.RootElement, "error");
        }
        catch
        {
            return string.Empty;
        }
    }
}
