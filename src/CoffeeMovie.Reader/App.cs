using CoffeeMovie.Reader.Pages;
using CoffeeMovie.Reader.Services;

namespace CoffeeMovie.Reader;

public sealed class App : Application
{
    private readonly ReaderLibraryService _libraryService;
    private readonly GoogleDriveSyncService _googleDriveSyncService;
    private readonly ReaderSyncSettingsService _syncSettingsService;
    private readonly CoffeeLearningWordRegistrationService _coffeeLearningService;
    private readonly ISpeechRecognitionService _speechRecognitionService;

    public App(
        ReaderLibraryService libraryService,
        GoogleDriveSyncService googleDriveSyncService,
        ReaderSyncSettingsService syncSettingsService,
        CoffeeLearningWordRegistrationService coffeeLearningService,
        ISpeechRecognitionService speechRecognitionService)
    {
        _libraryService = libraryService;
        _googleDriveSyncService = googleDriveSyncService;
        _syncSettingsService = syncSettingsService;
        _coffeeLearningService = coffeeLearningService;
        _speechRecognitionService = speechRecognitionService;
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        try
        {
            var rootPage = new NavigationPage(new MovieShelfPage(
                _libraryService,
                _googleDriveSyncService,
                _syncSettingsService,
                _coffeeLearningService,
                _speechRecognitionService))
            {
                BarBackgroundColor = Color.FromArgb("#05070B"),
                BarTextColor = Colors.White
            };

            return new Window(rootPage);
        }
        catch (Exception ex)
        {
            return new Window(CreateStartupErrorPage(ex));
        }
    }

    private static ContentPage CreateStartupErrorPage(Exception exception)
    {
        return new ContentPage
        {
            Title = "CoffeeMovie Reader",
            BackgroundColor = Color.FromArgb("#05070B"),
            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Padding = 24,
                    Spacing = 12,
                    Children =
                    {
                        new Label
                        {
                            Text = "起動に失敗しました",
                            TextColor = Colors.White,
                            FontSize = 24,
                            FontAttributes = FontAttributes.Bold
                        },
                        new Label
                        {
                            Text = exception.ToString(),
                            TextColor = Color.FromArgb("#FFB4A9"),
                            FontSize = 12,
                            LineBreakMode = LineBreakMode.WordWrap
                        }
                    }
                }
            }
        };
    }
}

