using System.Globalization;
using System.Net;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Reader.Models;
using CoffeeMovie.Reader.Services;
using Microsoft.Maui.Controls.Shapes;

namespace CoffeeMovie.Reader.Pages;

public sealed class MoviePlayerPage : ContentPage
{
    private readonly ReaderLibraryService _libraryService;
    private readonly string _movieId;
    private readonly Label _titleLabel = new()
    {
        TextColor = Colors.White,
        FontAttributes = FontAttributes.Bold,
        FontSize = 20,
        LineBreakMode = LineBreakMode.TailTruncation
    };
    private readonly Label _statusLabel = new()
    {
        TextColor = Color.FromArgb("#A5B3C6"),
        FontSize = 12
    };
    private readonly WebView _webView = new()
    {
        HeightRequest = 260
    };
    private readonly CollectionView _scenesView = new()
    {
        SelectionMode = SelectionMode.Single
    };
    private Movie? _movie;
    private bool _loaded;

    public MoviePlayerPage(ReaderLibraryService libraryService, string movieId)
    {
        _libraryService = libraryService;
        _movieId = movieId;
        Title = "Player";
        BackgroundColor = Color.FromArgb("#05070B");
        NavigationPage.SetHasNavigationBar(this, true);

        var subtitleButton = CreateActionButton("字幕を追加");
        subtitleButton.Clicked += async (_, _) => await ImportSubtitleAsync();

        _scenesView.ItemTemplate = new DataTemplate(CreateSceneRow);
        _scenesView.SelectionChanged += OnSceneSelected;

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            Children =
            {
                new VerticalStackLayout
                {
                    Padding = new Thickness(16, 14, 16, 8),
                    Spacing = 8,
                    Children =
                    {
                        _titleLabel,
                        _statusLabel,
                        subtitleButton
                    }
                },
                CreatePlayerFrame(),
                CreateSceneFrame()
            }
        };

        Grid.SetRow(_webView, 1);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await ReloadAsync();
    }

    private Border CreatePlayerFrame()
    {
        var frame = new Border
        {
            Margin = new Thickness(16, 0, 16, 12),
            Stroke = Color.FromArgb("#1E2A3A"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Colors.Black,
            Content = _webView
        };

        Grid.SetRow(frame, 1);
        return frame;
    }

    private Border CreateSceneFrame()
    {
        var header = new Label
        {
            Text = "字幕シーン",
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var layout = new VerticalStackLayout
        {
            Padding = new Thickness(16, 12),
            Spacing = 0,
            Children = { header, _scenesView }
        };

        var frame = new Border
        {
            Margin = new Thickness(16, 0, 16, 16),
            Stroke = Color.FromArgb("#1E2A3A"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Color.FromArgb("#0B111A"),
            Content = layout
        };

        Grid.SetRow(frame, 2);
        return frame;
    }

    private async Task ReloadAsync()
    {
        _movie = await _libraryService.GetMovieAsync(_movieId);
        if (_movie is null)
        {
            _titleLabel.Text = "動画が見つかりません";
            _statusLabel.Text = "ライブラリから削除された可能性があります。";
            return;
        }

        _titleLabel.Text = _movie.Title;
        _statusLabel.Text = _movie.SubtitleTracks.Count == 0
            ? "字幕なし"
            : $"{_movie.SubtitleTracks.Count} subtitle / {_movie.SceneMarkers.Count} scene";

        _webView.Source = new HtmlWebViewSource
        {
            Html = BuildPlayerHtml(_movie)
        };

        _scenesView.ItemsSource = _movie.SceneMarkers
            .Select(marker => new SceneJumpItem
            {
                Label = marker.Label,
                Timestamp = FormatTimestamp(marker.Start),
                StartSeconds = marker.Start.TotalSeconds
            })
            .ToList();
    }

    private async Task ImportSubtitleAsync()
    {
        if (_movie is null)
        {
            return;
        }

        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "字幕ファイルを選択",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.Android] = ["text/*", "application/x-subrip", "application/octet-stream"],
                [DevicePlatform.WinUI] = [".srt", ".vtt"]
            })
        });

        if (result is null)
        {
            return;
        }

        try
        {
            await _libraryService.ImportSubtitleAsync(_movie, result);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("字幕の取り込みに失敗しました", ex.Message, "閉じる");
        }
    }

    private async void OnSceneSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SceneJumpItem item)
        {
            return;
        }

        _scenesView.SelectedItem = null;
        var seconds = item.StartSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        await _webView.EvaluateJavaScriptAsync($"window.coffeeMovieJumpTo({seconds});");
    }

    private View CreateSceneRow()
    {
        var time = new Label
        {
            TextColor = Color.FromArgb("#5DE0D0"),
            FontSize = 12,
            WidthRequest = 62,
            VerticalTextAlignment = TextAlignment.Start
        };
        time.SetBinding(Label.TextProperty, nameof(SceneJumpItem.Timestamp));

        var label = new Label
        {
            TextColor = Colors.White,
            FontSize = 13,
            LineBreakMode = LineBreakMode.WordWrap
        };
        label.SetBinding(Label.TextProperty, nameof(SceneJumpItem.Label));

        return new Grid
        {
            Padding = new Thickness(0, 8),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            Children = { time, label }
        }.Tap(view =>
        {
            Grid.SetColumn(label, 1);
            return view;
        });
    }

    private static Button CreateActionButton(string text)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb("#5DE0D0"),
            TextColor = Color.FromArgb("#04100F"),
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 8,
            HeightRequest = 44
        };
    }

    private static string BuildPlayerHtml(Movie movie)
    {
        var videoUri = ToFileUri(movie.Video.CachePath);
        var track = movie.SubtitleTracks.LastOrDefault(subtitle =>
            !string.IsNullOrWhiteSpace(subtitle.VttCachePath)
            && File.Exists(subtitle.VttCachePath));
        var trackHtml = track is null
            ? string.Empty
            : $"""<track kind="subtitles" src="{Html(ToFileUri(track.VttCachePath))}" label="{Html(track.Label)}" srclang="{Html(track.Language ?? "ja")}" default>""";

        return $$"""
<!doctype html>
<html>
<head>
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
html, body {
  width: 100%;
  height: 100%;
  margin: 0;
  background: #000;
  overflow: hidden;
}
video {
  width: 100%;
  height: 100%;
  background: #000;
}
</style>
</head>
<body>
<video id="player" controls playsinline preload="metadata">
  <source src="{{Html(videoUri)}}" type="{{Html(movie.Video.ContentType ?? "video/mp4")}}">
  {{trackHtml}}
</video>
<script>
window.coffeeMovieJumpTo = function(seconds) {
  const player = document.getElementById('player');
  player.currentTime = seconds;
  player.play();
};
</script>
</body>
</html>
""";
    }

    private static string ToFileUri(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return new Uri(path).AbsoluteUri;
    }

    private static string Html(string value)
    {
        return WebUtility.HtmlEncode(value);
    }

    private static string FormatTimestamp(TimeSpan value)
    {
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
    }
}

internal static class ViewBuilderExtensions
{
    public static T Tap<T>(this T view, Func<T, T> configure)
    {
        return configure(view);
    }
}

