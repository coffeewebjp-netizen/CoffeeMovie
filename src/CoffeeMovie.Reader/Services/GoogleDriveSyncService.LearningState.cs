using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CoffeeMovie.Reader.Models;
using CoffeeMovie.Storage.Services;
using Microsoft.Maui.Storage;

namespace CoffeeMovie.Reader.Services;

public sealed partial class GoogleDriveSyncService
{
    private const string DriveFilesUrl = "https://www.googleapis.com/drive/v3/files";
    private const string DriveUploadFilesUrl = "https://www.googleapis.com/upload/drive/v3/files";
    private const string LearningStateDeviceIdPreferenceKey = "coffee-movie-learning-state-device-id";

    public async Task<IReadOnlyList<DriveLearningStateFile>> FindCoffeeLearningRegistrationFilesAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.GoogleDriveFolderId))
        {
            return [];
        }

        var accessToken = await _authService.GetValidAccessTokenAsync(settings, cancellationToken);
        var result = new List<DriveLearningStateFile>();
        string? pageToken = null;
        do
        {
            var query = $"'{settings.GoogleDriveFolderId}' in parents and trashed = false and mimeType != 'application/vnd.google-apps.folder'";
            var url = $"{DriveFilesUrl}?pageSize=1000&supportsAllDrives=true&includeItemsFromAllDrives=true&orderBy=modifiedTime desc&fields=nextPageToken,files(id,name,size,modifiedTime)&q={Uri.EscapeDataString(query)}";
            if (!string.IsNullOrWhiteSpace(pageToken))
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"CoffeeLearning登録状態の一覧取得に失敗しました: {GoogleDriveAuthService.GetErrorMessage(body)}");
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            pageToken = root.TryGetProperty("nextPageToken", out var tokenElement)
                ? tokenElement.GetString()
                : null;
            if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var file in files.EnumerateArray())
            {
                var name = GetDriveString(file, "name");
                if (!CoffeeLearningRegistrationSyncService.IsSyncFileName(name))
                {
                    continue;
                }

                var id = GetDriveString(file, "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                result.Add(new DriveLearningStateFile(
                    id,
                    name,
                    GetDriveModifiedTime(file),
                    GetDriveInt64(file, "size")));
            }
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        return result;
    }

    public async Task<Stream> DownloadCoffeeLearningRegistrationFileAsync(
        DriveLearningStateFile file,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        var accessToken = await _authService.GetValidAccessTokenAsync(settings, cancellationToken);
        var url = $"{DriveFilesUrl}/{Uri.EscapeDataString(file.Id)}?alt=media&supportsAllDrives=true";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"CoffeeLearning登録状態の取得に失敗しました: {GoogleDriveAuthService.GetErrorMessage(body)}");
        }

        return new MemoryStream(bytes, writable: false);
    }

    public async Task UploadCoffeeLearningRegistrationStateAsync(
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.GoogleDriveFolderId))
        {
            return;
        }

        var accessToken = await _authService.GetValidAccessTokenAsync(settings, cancellationToken);
        var fileName = CoffeeLearningRegistrationSyncService.CreateFileName(GetOrCreateLearningStateDeviceId());
        var existing = (await FindCoffeeLearningRegistrationFilesAsync(cancellationToken))
            .FirstOrDefault(file => string.Equals(file.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        using var request = existing is null
            ? CreateLearningStateFileRequest(settings.GoogleDriveFolderId, fileName, content)
            : CreateLearningStateUpdateRequest(existing.Id, content);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            throw new GoogleDriveReconnectRequiredException(
                "CoffeeLearning登録状態をDriveへ保存する権限がありません。Google Drive設定から再ログインしてください。");
        }

        throw new InvalidOperationException($"CoffeeLearning登録状態の保存に失敗しました: {GoogleDriveAuthService.GetErrorMessage(body)}");
    }

    private static HttpRequestMessage CreateLearningStateFileRequest(
        string folderId,
        string fileName,
        byte[] content)
    {
        var metadata = JsonSerializer.Serialize(new
        {
            name = fileName,
            parents = new[] { folderId },
            mimeType = "application/json"
        });
        var multipart = new MultipartContent("related");
        multipart.Add(new StringContent(metadata, Encoding.UTF8, "application/json"));
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        multipart.Add(fileContent);
        return new HttpRequestMessage(
            HttpMethod.Post,
            $"{DriveUploadFilesUrl}?uploadType=multipart&supportsAllDrives=true")
        {
            Content = multipart
        };
    }

    private static HttpRequestMessage CreateLearningStateUpdateRequest(string fileId, byte[] content)
    {
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return new HttpRequestMessage(
            HttpMethod.Patch,
            $"{DriveUploadFilesUrl}/{Uri.EscapeDataString(fileId)}?uploadType=media&supportsAllDrives=true")
        {
            Content = fileContent
        };
    }

    private static string GetOrCreateLearningStateDeviceId()
    {
        var deviceId = Preferences.Default.Get(LearningStateDeviceIdPreferenceKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            return "reader-" + deviceId;
        }

        deviceId = Guid.NewGuid().ToString("N")[..12];
        Preferences.Default.Set(LearningStateDeviceIdPreferenceKey, deviceId);
        return "reader-" + deviceId;
    }

    private static string GetDriveString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static long? GetDriveModifiedTime(JsonElement element)
    {
        var value = GetDriveString(element, "modifiedTime");
        return DateTimeOffset.TryParse(value, out var modified)
            ? modified.ToUnixTimeMilliseconds()
            : null;
    }

    private static long? GetDriveInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return long.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }
}
