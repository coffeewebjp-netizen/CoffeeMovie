using CoffeeMovie.Reader.Models;
using CoffeeMovie.Reader.Services;
using Microsoft.Maui.Controls.Shapes;

namespace CoffeeMovie.Reader.Pages;

public sealed class MovieShelfPage : ContentPage
{
    private readonly ReaderLibraryService _libraryService;
    private readonly CollectionView _moviesView = new()
    {
        SelectionMode = SelectionMode.Single,
        ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem
    };
    private readonly Label _summaryLabel = new()
    {
        TextColor = Color.FromArgb("#A5B3C6"),
        FontSize = 12
    };
    private readonly Label _emptyLabel = new()
    {
        Text = "まだ動画がありません",
        TextColor = Color.FromArgb("#A5B3C6"),
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center
    };

    public MovieShelfPage(ReaderLibraryService libraryService)
    {
        _libraryService = libraryService;
        Title = "CoffeeMovie";
        BackgroundColor = Color.FromArgb("#05070B");
        NavigationPage.SetHasNavigationBar(this, false);

        var importButton = CreateHeaderButton("動画を追加");
        importButton.Clicked += async (_, _) => await ImportVideoAsync();

        _moviesView.ItemTemplate = new DataTemplate(CreateMovieCard);
        _moviesView.SelectionChanged += OnMovieSelected;

        var header = new Grid
        {
            Padding = new Thickness(18, 16, 18, 10),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label
                        {
                            Text = "CoffeeMovie",
                            TextColor = Colors.White,
                            FontSize = 25,
                            FontAttributes = FontAttributes.Bold
                        },
                        new Label
                        {
                            Text = "動画棚",
                            TextColor = Color.FromArgb("#A5B3C6"),
                            FontSize = 13
                        }
                    }
                },
                importButton
            }
        };
        Grid.SetColumn(importButton, 1);

        var moviesLayer = new Grid
        {
            Children = { _moviesView, _emptyLabel }
        };

        var summary = new VerticalStackLayout
        {
            Padding = new Thickness(18, 0, 18, 10),
            Children = { _summaryLabel }
        };

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
                header,
                summary,
                moviesLayer
            }
        };
        Grid.SetRow(summary, 1);
        Grid.SetRow(moviesLayer, 2);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var movies = await _libraryService.LoadMoviesAsync();
        var items = movies.Select(movie => new MovieListItem
        {
            MovieId = movie.Id,
            Title = movie.Title,
            Detail = $"{movie.SubtitleTracks.Count} subtitle / {movie.SceneMarkers.Count} scene",
            CacheState = movie.Video.HasLocalCache ? "cached" : "not cached"
        }).ToList();

        _moviesView.ItemsSource = items;
        _emptyLabel.IsVisible = items.Count == 0;
        _summaryLabel.Text = $"{items.Count} movies";
    }

    private async Task ImportVideoAsync()
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "動画ファイルを選択",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.Android] = ["video/*"],
                [DevicePlatform.WinUI] = [".mp4", ".mkv", ".webm", ".mov", ".m4v"]
            })
        });

        if (result is null)
        {
            return;
        }

        try
        {
            var movie = await _libraryService.ImportVideoAsync(result);
            await ReloadAsync();
            await Navigation.PushAsync(new MoviePlayerPage(_libraryService, movie.Id));
        }
        catch (Exception ex)
        {
            await DisplayAlert("動画の取り込みに失敗しました", ex.Message, "閉じる");
        }
    }

    private async void OnMovieSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not MovieListItem item)
        {
            return;
        }

        _moviesView.SelectedItem = null;
        await Navigation.PushAsync(new MoviePlayerPage(_libraryService, item.MovieId));
    }

    private View CreateMovieCard()
    {
        var title = new Label
        {
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 16,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        title.SetBinding(Label.TextProperty, nameof(MovieListItem.Title));

        var detail = new Label
        {
            TextColor = Color.FromArgb("#A5B3C6"),
            FontSize = 12
        };
        detail.SetBinding(Label.TextProperty, nameof(MovieListItem.Detail));

        var cache = new Label
        {
            TextColor = Color.FromArgb("#5DE0D0"),
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.End
        };
        cache.SetBinding(Label.TextProperty, nameof(MovieListItem.CacheState));

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            Children = { title, detail, cache }
        };
        Grid.SetRow(detail, 1);
        Grid.SetColumn(cache, 1);
        Grid.SetRowSpan(cache, 2);

        return new Border
        {
            Margin = new Thickness(18, 6),
            Padding = new Thickness(14),
            Stroke = Color.FromArgb("#1E2A3A"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Color.FromArgb("#0B111A"),
            Content = grid
        };
    }

    private static Button CreateHeaderButton(string text)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb("#5DE0D0"),
            TextColor = Color.FromArgb("#04100F"),
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 8,
            HeightRequest = 42,
            Padding = new Thickness(14, 0)
        };
    }
}
