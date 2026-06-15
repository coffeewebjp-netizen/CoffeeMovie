using System.Text;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Reader.Services;
using Microsoft.Maui.Storage;

namespace CoffeeMovie.Reader.Pages;

public sealed partial class MoviePlayerPage
{
    private async Task LoadPlayerHtmlAsync(Movie movie)
    {
        if (string.IsNullOrWhiteSpace(movie.Video.CachePath) || !File.Exists(movie.Video.CachePath))
        {
            _statusLabel.Text = "動画キャッシュが見つかりません。動画棚から再取得してください。";
            _webView.Source = new HtmlWebViewSource
            {
                Html = "<html><body style=\"background:#000;color:#fff;font-family:sans-serif;\">動画キャッシュがありません。</body></html>"
            };
            return;
        }

        var playerDirectory = System.IO.Path.Combine(FileSystem.AppDataDirectory, "player");
        Directory.CreateDirectory(playerDirectory);
        var htmlPath = System.IO.Path.Combine(playerDirectory, $"{CreateSafeFileStem(movie.Id)}.html");
        await File.WriteAllTextAsync(
            htmlPath,
            ReaderPlayerHtmlBuilder.Build(
                movie,
                _englishSubtitleSwitch.IsToggled,
                _japaneseSubtitleSwitch.IsToggled,
                _memoSwitch.IsToggled,
                _subtitlePosition,
                _subtitleAlignment),
            Encoding.UTF8);
        _webView.Source = new UrlWebViewSource
        {
            Url = ToFileUri(htmlPath)
        };
    }

    private static string ToFileUri(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return new Uri(path).AbsoluteUri;
    }

    private static string CreateSafeFileStem(string value)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var safe = new string(value
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray())
            .Trim();
        return string.IsNullOrWhiteSpace(safe) ? "movie" : safe;
    }


}
