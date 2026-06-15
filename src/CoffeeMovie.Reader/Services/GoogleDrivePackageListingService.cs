using System.Net.Http.Headers;
using System.Text.Json;
using CoffeeMovie.Reader.Models;
using CoffeeMovie.Storage.Services;

namespace CoffeeMovie.Reader.Services;

public sealed class GoogleDrivePackageListingService
{
    private const string DriveFilesUrl = "https://www.googleapis.com/drive/v3/files";

    public async Task<IReadOnlyList<SyncMovieCandidate>> FindPackagesAsync(
        HttpClient httpClient,
        string folderId,
        string accessToken,
        Func<string, string> createListErrorMessage,
        CancellationToken cancellationToken = default)
    {
        var driveFiles = new List<DriveFileInfo>();
        string? pageToken = null;

        do
        {
            var query = $"'{folderId}' in parents and trashed = false and mimeType != 'application/vnd.google-apps.folder'";
            var url = $"{DriveFilesUrl}?pageSize=1000&supportsAllDrives=true&includeItemsFromAllDrives=true&orderBy=name_natural&fields=nextPageToken,files(id,name,size,modifiedTime,mimeType)&q={Uri.EscapeDataString(query)}";
            if (!string.IsNullOrWhiteSpace(pageToken))
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(createListErrorMessage(body));
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
                var name = GetString(file, "name");
                if (!CoffeeMoviePackageService.IsReaderPackageFileName(name)
                    && !CoffeeMoviePackageService.IsReaderPackageSidecarFileName(name))
                {
                    continue;
                }

                var id = GetString(file, "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                driveFiles.Add(new DriveFileInfo(
                    $"gdrive://files/{id}",
                    name,
                    GetModifiedTimeMilliseconds(file),
                    GetInt64(file, "size")));
            }
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        var sidecars = driveFiles
            .Where(file => CoffeeMoviePackageService.IsReaderPackageSidecarFileName(file.FileName))
            .GroupBy(file => CoffeeMoviePackageService.GetPackageFileNameForSidecarName(file.FileName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var packages = new List<SyncMovieCandidate>();
        foreach (var file in driveFiles.Where(file => CoffeeMoviePackageService.IsReaderPackageFileName(file.FileName)))
        {
            sidecars.TryGetValue(file.FileName, out var sidecar);
            packages.Add(new SyncMovieCandidate
            {
                ContentUri = file.ContentUri,
                FileName = file.FileName,
                LastModified = file.LastModified,
                Size = file.Size,
                SidecarContentUri = sidecar?.ContentUri,
                SidecarFileName = sidecar?.FileName,
                SidecarLastModified = sidecar?.LastModified,
                SidecarSize = sidecar?.Size
            });
        }

        return packages
            .OrderBy(package => package.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static long? GetModifiedTimeMilliseconds(JsonElement element)
    {
        var value = GetString(element, "modifiedTime");
        return DateTimeOffset.TryParse(value, out var modified)
            ? modified.ToUnixTimeMilliseconds()
            : null;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static long? GetInt64(JsonElement element, string propertyName)
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

    private sealed record DriveFileInfo(
        string ContentUri,
        string FileName,
        long? LastModified,
        long? Size);
}
