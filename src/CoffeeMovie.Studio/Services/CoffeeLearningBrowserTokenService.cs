using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace CoffeeMovie.Studio.Services;

public sealed class CoffeeLearningBrowserTokenService
{
    private const string CallbackPath = "/coffee-learning-callback/";
    private const int MaxCallbackRequests = 8;

    public async Task<CoffeeLearningBrowserTokenResult> AcquireAsync(
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        var normalizedBaseUrl = CoffeeLearningWordRegistrationService.NormalizeBaseUrl(baseUrl);
        var state = CreateState();

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var redirectUri = $"http://127.0.0.1:{port}{CallbackPath}";
        var connectUrl = BuildConnectUrl(normalizedBaseUrl, redirectUri, state);

        OpenBrowser(connectUrl);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));

        try
        {
            for (var requestCount = 0; requestCount < MaxCallbackRequests; requestCount++)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                using var stream = client.GetStream();
                var request = await ReadHttpRequestAsync(stream, timeout.Token);

                if (!IsCallbackRequest(request.Target))
                {
                    await WriteHtmlResponseAsync(stream, "CoffeeMovie Studio", "Not found", timeout.Token, statusCode: 404);
                    continue;
                }

                var values = request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                    ? ParseUrlEncoded(request.Body)
                    : ParseQuery(request.Target);

                if (values.TryGetValue("bearer", out var postedBearer) && !string.IsNullOrWhiteSpace(postedBearer))
                {
                    return await CompleteAsync(stream, values, state, timeout.Token);
                }

                await WriteFragmentRelayResponseAsync(stream, timeout.Token);
            }

            throw new InvalidOperationException("CoffeeLearning\u30C8\u30FC\u30AF\u30F3\u53D6\u5F97\u30EA\u30AF\u30A8\u30B9\u30C8\u304C\u5B8C\u4E86\u3057\u307E\u305B\u3093\u3067\u3057\u305F\u3002\u30D6\u30E9\u30A6\u30B6\u3067\u8868\u793A\u3055\u308C\u305F\u753B\u9762\u306E\u30EA\u30F3\u30AF\u3092\u62BC\u3057\u3066\u304F\u3060\u3055\u3044\u3002");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("CoffeeLearning\u30C8\u30FC\u30AF\u30F3\u53D6\u5F97\u304C\u30BF\u30A4\u30E0\u30A2\u30A6\u30C8\u3057\u307E\u3057\u305F\u3002\u30D6\u30E9\u30A6\u30B6\u3067\u30ED\u30B0\u30A4\u30F3\u6E08\u307F\u304B\u78BA\u8A8D\u3057\u3066\u3001\u3082\u3046\u4E00\u5EA6\u8A66\u3057\u3066\u304F\u3060\u3055\u3044\u3002");
        }
    }

    private static async Task<CoffeeLearningBrowserTokenResult> CompleteAsync(
        NetworkStream stream,
        Dictionary<string, string> values,
        string expectedState,
        CancellationToken cancellationToken)
    {
        if (!values.TryGetValue("state", out var receivedState)
            || !string.Equals(receivedState, expectedState, StringComparison.Ordinal))
        {
            await WriteHtmlResponseAsync(
                stream,
                "CoffeeMovie Studio",
                "\u8A8D\u8A3C\u72B6\u614B\u306E\u78BA\u8A8D\u306B\u5931\u6557\u3057\u307E\u3057\u305F\u3002Studio\u3067\u3082\u3046\u4E00\u5EA6\u5B9F\u884C\u3057\u3066\u304F\u3060\u3055\u3044\u3002",
                cancellationToken);
            throw new InvalidOperationException("CoffeeLearning\u8A8D\u8A3C\u306Estate\u304C\u4E00\u81F4\u3057\u307E\u305B\u3093\u3067\u3057\u305F\u3002");
        }

        if (!values.TryGetValue("bearer", out var bearer) || string.IsNullOrWhiteSpace(bearer))
        {
            await WriteHtmlResponseAsync(
                stream,
                "CoffeeMovie Studio",
                "CoffeeLearning\u30C8\u30FC\u30AF\u30F3\u3092\u53D6\u5F97\u3067\u304D\u307E\u305B\u3093\u3067\u3057\u305F\u3002",
                cancellationToken);
            throw new InvalidOperationException("CoffeeLearning\u30C8\u30FC\u30AF\u30F3\u3092\u53D6\u5F97\u3067\u304D\u307E\u305B\u3093\u3067\u3057\u305F\u3002");
        }

        values.TryGetValue("deckId", out var deckId);
        await WriteHtmlResponseAsync(
            stream,
            "CoffeeMovie Studio",
            "CoffeeLearning\u9023\u643A\u30C8\u30FC\u30AF\u30F3\u3092Studio\u3078\u4FDD\u5B58\u3057\u307E\u3057\u305F\u3002\u3053\u306E\u30BF\u30D6\u306F\u9589\u3058\u3066\u5927\u4E08\u592B\u3067\u3059\u3002",
            cancellationToken);
        return new CoffeeLearningBrowserTokenResult(bearer.Trim(), string.IsNullOrWhiteSpace(deckId) ? null : deckId.Trim());
    }

    private static bool IsCallbackRequest(string target)
    {
        var queryIndex = target.IndexOf('?');
        var path = queryIndex >= 0 ? target[..queryIndex] : target;
        return path.Equals(CallbackPath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(CallbackPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildConnectUrl(string baseUrl, string redirectUri, string state)
    {
        return baseUrl.TrimEnd('/')
            + "/api/coffee-movie/studio-connect?redirect_uri="
            + Uri.EscapeDataString(redirectUri)
            + "&state="
            + Uri.EscapeDataString(state)
            + "&name="
            + Uri.EscapeDataString("CoffeeMovie Studio");
    }

    private static void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static string CreateState()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static async Task<HttpRequestData> ReadHttpRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var requestBytes = new MemoryStream();
        var headerEnd = -1;
        var contentLength = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            requestBytes.Write(buffer, 0, read);
            var data = requestBytes.ToArray();
            if (headerEnd < 0)
            {
                headerEnd = FindHeaderEnd(data);
                if (headerEnd >= 0)
                {
                    var headerText = Encoding.ASCII.GetString(data, 0, headerEnd);
                    contentLength = ParseContentLength(headerText);
                }
            }

            if (headerEnd >= 0 && data.Length >= headerEnd + 4 + contentLength)
            {
                return ParseRequest(data, headerEnd, contentLength);
            }

            if (data.Length > 128 * 1024)
            {
                throw new InvalidOperationException("CoffeeLearning\u8A8D\u8A3C\u30EC\u30B9\u30DD\u30F3\u30B9\u304C\u5927\u304D\u3059\u304E\u307E\u3059\u3002");
            }
        }

        throw new InvalidOperationException("CoffeeLearning\u8A8D\u8A3C\u30EC\u30B9\u30DD\u30F3\u30B9\u3092\u8AAD\u307F\u53D6\u308C\u307E\u305B\u3093\u3067\u3057\u305F\u3002");
    }

    private static int FindHeaderEnd(byte[] data)
    {
        for (var i = 0; i <= data.Length - 4; i++)
        {
            if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n')
            {
                return i;
            }
        }

        return -1;
    }

    private static int ParseContentLength(string headerText)
    {
        foreach (var line in headerText.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2
                && parts[0].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(parts[1].Trim(), out var value)
                && value > 0)
            {
                return value;
            }
        }

        return 0;
    }

    private static HttpRequestData ParseRequest(byte[] data, int headerEnd, int contentLength)
    {
        var headerText = Encoding.ASCII.GetString(data, 0, headerEnd);
        var requestLine = headerText.Split(["\r\n", "\n"], StringSplitOptions.None).FirstOrDefault() ?? string.Empty;
        var requestParts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestParts.Length < 2)
        {
            throw new InvalidOperationException("CoffeeLearning\u8A8D\u8A3C\u30EA\u30AF\u30A8\u30B9\u30C8\u306E\u5F62\u5F0F\u304C\u4E0D\u6B63\u3067\u3059\u3002");
        }

        var body = contentLength > 0
            ? Encoding.UTF8.GetString(data, headerEnd + 4, contentLength)
            : string.Empty;
        return new HttpRequestData(requestParts[0], requestParts[1], body);
    }

    private static Dictionary<string, string> ParseQuery(string target)
    {
        var questionIndex = target.IndexOf('?');
        return questionIndex < 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : ParseUrlEncoded(target[(questionIndex + 1)..]);
    }

    private static Dictionary<string, string> ParseUrlEncoded(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in value.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = DecodeUrlPart(parts[0]);
            var itemValue = parts.Length > 1 ? DecodeUrlPart(parts[1]) : string.Empty;
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = itemValue;
            }
        }

        return result;
    }

    private static string DecodeUrlPart(string value)
    {
        return Uri.UnescapeDataString(value.Replace('+', ' '));
    }

    private static async Task WriteFragmentRelayResponseAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        const string html = "<!doctype html><meta charset=\"utf-8\"><title>CoffeeMovie Studio</title>"
            + "<body style=\"font-family: sans-serif; padding: 24px; line-height: 1.7;\">"
            + "<h2>CoffeeMovie Studio</h2>"
            + "<p id=\"status\">CoffeeLearning token is being sent to Studio...</p>"
            + "<script>"
            + "(async function(){"
            + "const status=document.getElementById('status');"
            + "const body=location.hash&&location.hash.length>1?location.hash.substring(1):'';"
            + "if(!body){status.textContent='Token data was not found. Return to Studio and try again.';return;}"
            + "try{await fetch('/coffee-learning-callback/receive',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:body});"
            + "history.replaceState(null,'','/coffee-learning-callback/done');"
            + "status.textContent='CoffeeLearning token was sent to Studio. You can close this tab.';"
            + "setTimeout(function(){window.close();},500);"
            + "}catch(e){status.textContent='Could not send the token to Studio. Return to Studio and try again.';}"
            + "})();"
            + "</script></body>";
        await WriteRawHtmlResponseAsync(stream, html, cancellationToken);
    }

    private static Task WriteHtmlResponseAsync(
        NetworkStream stream,
        string title,
        string message,
        CancellationToken cancellationToken,
        int statusCode = 200)
    {
        var html = "<!doctype html><meta charset=\"utf-8\"><title>"
            + EscapeHtml(title)
            + "</title><body style=\"font-family: sans-serif; padding: 24px; line-height: 1.7;\"><h2>"
            + EscapeHtml(title)
            + "</h2><p>"
            + EscapeHtml(message)
            + "</p></body>";
        return WriteRawHtmlResponseAsync(stream, html, cancellationToken, statusCode);
    }

    private static async Task WriteRawHtmlResponseAsync(
        NetworkStream stream,
        string html,
        CancellationToken cancellationToken,
        int statusCode = 200)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var statusText = statusCode == 404 ? "Not Found" : "OK";
        var header = Encoding.ASCII.GetBytes(
            "HTTP/1.1 "
            + statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " "
            + statusText
            + "\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Length: "
            + body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
    }

    private static string EscapeHtml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private sealed record HttpRequestData(string Method, string Target, string Body);
}

public sealed record CoffeeLearningBrowserTokenResult(string Bearer, string? DeckId);