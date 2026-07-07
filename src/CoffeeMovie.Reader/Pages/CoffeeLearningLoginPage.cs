using CoffeeMovie.Reader.Services;

namespace CoffeeMovie.Reader.Pages;

public sealed class CoffeeLearningLoginPage : ContentPage
{
    private readonly CoffeeLearningWordRegistrationService _coffeeLearningService;
    private readonly Label _statusLabel = new()
    {
        TextColor = Color.FromArgb("#A5B3C6"),
        FontSize = 12,
        LineBreakMode = LineBreakMode.WordWrap
    };
    private readonly WebView _webView = new();
    private string _baseUrl = CoffeeLearningWordRegistrationService.DefaultBaseUrl;
    private bool _loaded;
    private bool _captureInProgress;

    public CoffeeLearningLoginPage(CoffeeLearningWordRegistrationService coffeeLearningService)
    {
        _coffeeLearningService = coffeeLearningService;
        Title = "CoffeeLearningログイン";
        BackgroundColor = Color.FromArgb("#05070B");

        var titleLabel = new Label
        {
            Text = "CoffeeLearning",
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 20
        };

        var hintLabel = new Label
        {
            Text = "この画面でCoffeeLearningにログインすると、登録用の認証Cookieと現在デッキを保存します。",
            TextColor = Color.FromArgb("#A5B3C6"),
            FontSize = 12,
            LineBreakMode = LineBreakMode.WordWrap
        };

        var captureButton = CreateActionButton("ログインを取得");
        captureButton.Clicked += async (_, _) => await TryCaptureAsync(showMissingMessage: true);

        var reloadButton = CreateSecondaryButton("再読み込み");
        reloadButton.Clicked += (_, _) => _webView.Source = new UrlWebViewSource { Url = _baseUrl };

        var closeButton = CreateSecondaryButton("閉じる");
        closeButton.Clicked += async (_, _) => await Navigation.PopAsync();

        _webView.Navigated += async (_, _) => await TryCaptureAsync(showMissingMessage: false);

        var header = new VerticalStackLayout
        {
            Padding = new Thickness(16, 14, 16, 10),
            Spacing = 8,
            Children = { titleLabel, hintLabel, _statusLabel }
        };

        var footer = new Grid
        {
            Padding = new Thickness(16, 10),
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            Children = { captureButton, reloadButton, closeButton }
        };
        Grid.SetColumn(reloadButton, 1);
        Grid.SetColumn(closeButton, 2);

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            Children = { header, _webView, footer }
        };
        Grid.SetRow(_webView, 1);
        Grid.SetRow(footer, 2);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        PrepareCookieCapture();
        var settings = await _coffeeLearningService.LoadSettingsAsync();
        _baseUrl = string.IsNullOrWhiteSpace(settings.CoffeeLearningBaseUrl)
            ? CoffeeLearningWordRegistrationService.DefaultBaseUrl
            : settings.CoffeeLearningBaseUrl;
        _statusLabel.Text = "CoffeeLearningにログインしてください。ログイン済みなら自動取得されます。";
        _webView.Source = new UrlWebViewSource { Url = _baseUrl };
    }

    private async Task TryCaptureAsync(bool showMissingMessage)
    {
        if (_captureInProgress)
        {
            return;
        }

        _captureInProgress = true;
        try
        {
            var result = await _coffeeLearningService.CaptureAuthCookieFromWebViewAsync(_baseUrl);
            if (result.Success)
            {
                _statusLabel.Text = result.Message;
                _statusLabel.TextColor = Color.FromArgb("#5DE0D0");
                return;
            }

            if (showMissingMessage)
            {
                _statusLabel.Text = result.Message;
                _statusLabel.TextColor = Color.FromArgb("#F6D365");
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = ex.Message;
            _statusLabel.TextColor = Color.FromArgb("#FF9AA5");
        }
        finally
        {
            _captureInProgress = false;
        }
    }

    private static void PrepareCookieCapture()
    {
#if ANDROID
        Android.Webkit.CookieManager.Instance?.SetAcceptCookie(true);
#endif
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
            HeightRequest = 42,
            Padding = new Thickness(10, 0)
        };
    }

    private static Button CreateSecondaryButton(string text)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb("#142033"),
            TextColor = Colors.White,
            BorderColor = Color.FromArgb("#2A3A50"),
            BorderWidth = 1,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 8,
            HeightRequest = 42,
            Padding = new Thickness(10, 0)
        };
    }
}