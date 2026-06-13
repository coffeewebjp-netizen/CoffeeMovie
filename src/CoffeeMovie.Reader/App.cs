using CoffeeMovie.Reader.Pages;
using CoffeeMovie.Reader.Services;

namespace CoffeeMovie.Reader;

public sealed class App : Application
{
    private readonly ReaderLibraryService _libraryService;

    public App(ReaderLibraryService libraryService)
    {
        _libraryService = libraryService;
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        try
        {
            var rootPage = new NavigationPage(new MovieShelfPage(_libraryService))
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

