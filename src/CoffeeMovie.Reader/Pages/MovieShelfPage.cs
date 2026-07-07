using CoffeeMovie.Core.Models;
using CoffeeMovie.Core.Services;
using CoffeeMovie.Reader.Models;
using CoffeeMovie.Reader.Services;
using Microsoft.Maui.Controls.Shapes;

namespace CoffeeMovie.Reader.Pages;

public sealed partial class MovieShelfPage : ContentPage
{
    private const string DefaultGoogleDriveClientId = "327808944898-6q8gs3t06ts12tsaqagg5268tahug7ru.apps.googleusercontent.com";
    private const string ReaderVersionText = "v0.1.2";

    private readonly ReaderLibraryService _libraryService;
    private readonly GoogleDriveSyncService _googleDriveSyncService;
    private readonly ReaderSyncSettingsService _syncSettingsService;
    private readonly CoffeeLearningWordRegistrationService _coffeeLearningService;
    private readonly ISpeechRecognitionService _speechRecognitionService;
    private readonly CollectionView _moviesView = new()
    {
        SelectionMode = SelectionMode.Single,
        ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems
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
    private readonly Grid _startupLayer = new()
    {
        BackgroundColor = Color.FromArgb("#05070B"),
        InputTransparent = false,
        Opacity = 1
    };
    private readonly Image _startupIcon = new()
    {
        Source = ImageSource.FromFile("startup_icon.png"),
        Aspect = Aspect.AspectFit,
        WidthRequest = 300,
        HeightRequest = 300,
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center,
        Scale = 0.9,
        Opacity = 0
    };
    private readonly Label _startupTitle = new()
    {
        Text = "CoffeeMovie",
        FontSize = 36,
        FontAttributes = FontAttributes.Bold,
        TextColor = Colors.White,
        HorizontalTextAlignment = TextAlignment.Center,
        Opacity = 0
    };
    private readonly Label _startupSubtitle = new()
    {
        Text = "Reader",
        FontSize = 22,
        TextColor = Color.FromArgb("#5DE0D0"),
        HorizontalTextAlignment = TextAlignment.Center,
        Opacity = 0
    };
    private readonly Button _importButton = CreateHeaderButton("動画");
    private readonly Button _driveSettingsButton = CreateHeaderButton("Drive設定");
    private readonly Button _syncButton = CreateHeaderButton("同期");
    private readonly Button _moreButton = CreateHeaderButton("その他");
    private readonly HashSet<string> _collapsedSeries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _collapsedSeasons = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<MovieListItem> _movieItems = [];
    private bool _isSyncing;
    private bool _isOpeningMovie;
    private bool _playedStartupAnimation;
    private bool _hasLoadedLibrary;
    private int _renderedLibraryRevision = -1;

    public MovieShelfPage(
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
        Title = "CoffeeMovie";
        BackgroundColor = Color.FromArgb("#05070B");
        NavigationPage.SetHasNavigationBar(this, false);

        _importButton.Clicked += async (_, _) => await ImportVideoAsync();
        _driveSettingsButton.Clicked += async (_, _) => await ConfigureGoogleDriveAsync();
        _syncButton.Clicked += async (_, _) => await SyncGoogleDriveAsync();
        _moreButton.Clicked += async (_, _) => await ManageOtherActionsAsync();

        _moviesView.ItemTemplate = new ShelfRowTemplateSelector
        {
            SeriesTemplate = new DataTemplate(CreateSeriesHeaderRow),
            SeasonTemplate = new DataTemplate(CreateSeasonHeaderRow),
            MovieTemplate = new DataTemplate(CreateMovieCard)
        };
        _moviesView.SelectionChanged += OnMovieSelected;

        var actionGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8,
            Children = { _importButton, _driveSettingsButton, _syncButton, _moreButton }
        };
        Grid.SetColumn(_driveSettingsButton, 1);
        Grid.SetColumn(_syncButton, 2);
        Grid.SetColumn(_moreButton, 3);

        var header = new VerticalStackLayout
        {
            Padding = new Thickness(18, 16, 18, 10),
            Spacing = 10,
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
                            Text = $"動画棚 {ReaderVersionText}",
                            TextColor = Color.FromArgb("#A5B3C6"),
                            FontSize = 13
                        }
                    }
                },
                actionGrid
            }
        };

        var moviesLayer = new Grid
        {
            Children = { _moviesView, _emptyLabel }
        };

        var summary = new VerticalStackLayout
        {
            Padding = new Thickness(18, 0, 18, 10),
            Children = { _summaryLabel }
        };

        var startupLogo = new VerticalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                _startupIcon,
                _startupTitle,
                _startupSubtitle
            }
        };
        _startupLayer.Children.Add(startupLogo);

        var root = new Grid
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
                moviesLayer,
                _startupLayer
            }
        };
        Grid.SetRow(summary, 1);
        Grid.SetRow(moviesLayer, 2);
        Grid.SetRowSpan(_startupLayer, 3);
        Content = root;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _ = PlayStartupAnimationAsync();
        if (!_hasLoadedLibrary || _renderedLibraryRevision != _libraryService.Revision)
        {
            await ReloadAsync();
        }
    }

    private async Task PlayStartupAnimationAsync()
    {
        if (_playedStartupAnimation)
        {
            return;
        }

        _playedStartupAnimation = true;
        _startupLayer.IsVisible = true;
        _startupLayer.InputTransparent = false;
        _startupLayer.Opacity = 1;
        _startupIcon.Opacity = 0;
        _startupIcon.Scale = 0.9;
        _startupTitle.Opacity = 0;
        _startupSubtitle.Opacity = 0;

        await Task.Delay(220);
        await Task.WhenAll(
            _startupIcon.FadeToAsync(1, 520, Easing.CubicOut),
            _startupTitle.FadeToAsync(1, 520, Easing.CubicOut),
            _startupSubtitle.FadeToAsync(1, 520, Easing.CubicOut));
        await Task.Delay(120);
        await RunStartupExitAnimationAsync();
        _startupLayer.IsVisible = false;
        _startupLayer.InputTransparent = true;
    }

    private Task RunStartupExitAnimationAsync()
    {
        var completed = new TaskCompletionSource();
        var animation = new Animation();
        animation.Add(0, 1, new Animation(
            value => _startupIcon.Scale = value,
            0.9,
            5.6,
            Easing.CubicIn));
        animation.Add(0, 1, new Animation(
            value => _startupIcon.Opacity = value,
            1,
            0,
            Easing.CubicIn));
        animation.Add(0, 0.35, new Animation(
            value => _startupTitle.Opacity = value,
            1,
            0,
            Easing.CubicIn));
        animation.Add(0, 0.35, new Animation(
            value => _startupSubtitle.Opacity = value,
            1,
            0,
            Easing.CubicIn));
        animation.Add(0, 1, new Animation(
            value => _startupLayer.Opacity = value,
            1,
            0,
            Easing.CubicIn));
        animation.Commit(
            this,
            "StartupExit",
            rate: 16,
            length: 1280,
            finished: (_, _) => completed.TrySetResult());
        return completed.Task;
    }

    private async Task ReloadAsync()
    {
        var movies = await _libraryService.LoadMoviesAsync();
        var items = movies.Select(CreateMovieListItem).ToList();

        _movieItems = items;
        RenderShelfRows();
        _emptyLabel.IsVisible = items.Count == 0;
        if (!_isSyncing)
        {
            _summaryLabel.Text = $"{items.Count} movies";
        }

        _hasLoadedLibrary = true;
        _renderedLibraryRevision = _libraryService.Revision;
    }

    private void RenderShelfRows()
    {
        _moviesView.ItemsSource = BuildShelfRows(_movieItems);
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
            await Navigation.PushAsync(new MoviePlayerPage(_libraryService, _coffeeLearningService, _speechRecognitionService, movie.Id));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("動画の取り込みに失敗しました", ex.Message, "閉じる");
        }
    }

    private async void OnMovieSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not MovieListItem item)
        {
            _moviesView.SelectedItem = null;
            return;
        }

        _moviesView.SelectedItem = null;
        await OpenMovieAsync(item.MovieId);
    }

    private async void OnMovieActionButtonClicked(object? sender, EventArgs e)
    {
        if (sender is not BindableObject { BindingContext: MovieListItem item })
        {
            return;
        }

        await OpenMovieAsync(item.MovieId);
    }

    private async Task OpenMovieAsync(string movieId)
    {
        if (_isSyncing || _isOpeningMovie)
        {
            return;
        }

        _isOpeningMovie = true;
        try
        {
            var movie = await _libraryService.GetMovieAsync(movieId);
            if (movie is null)
            {
                await DisplayAlertAsync("動画が見つかりません", "本棚を更新してください。", "閉じる");
                return;
            }

            if (!HasVideoCache(movie))
            {
                if (string.IsNullOrWhiteSpace(movie.SourcePackageUri))
                {
                    await DisplayAlertAsync("キャッシュがありません", "この動画は再取得元のDriveパッケージが記録されていません。", "閉じる");
                    return;
                }

                var package = CreatePackageCandidate(movie);
                var restartDownload = await ChooseDownloadModeAsync(movie, package);
                if (restartDownload is null)
                {
                    return;
                }

                try
                {
                    movie = await DownloadMovieCacheAsync(movie, restartDownload.Value);
                    await ReloadAsync();
                }
                catch (GoogleDriveReconnectRequiredException ex)
                {
                    await DisplayAlertAsync("Google Driveの再接続が必要です", ex.Message, "閉じる");
                    await ConfigureGoogleDriveAsync();
                    return;
                }
                catch (Exception ex)
                {
                    await DisplayAlertAsync("動画キャッシュの取得に失敗しました", ex.Message, "閉じる");
                    return;
                }
            }

            await Navigation.PushAsync(new MoviePlayerPage(_libraryService, _coffeeLearningService, _speechRecognitionService, movie.Id));
        }
        finally
        {
            _isOpeningMovie = false;
        }
    }

    private MovieListItem CreateMovieListItem(Movie movie)
    {
        var item = new MovieListItem
        {
            MovieId = movie.Id,
            Title = movie.Title,
            Detail = FormatMovieDetail(movie),
            SeriesKey = GetSeriesKey(movie),
            SeriesTitle = GetSeriesTitle(movie),
            SeasonKey = GetSeasonKey(movie),
            SeasonTitle = GetSeasonTitle(movie),
            SeriesDetail = FormatMovieSeriesDetail(movie),
            TagsDetail = movie.Tags.Count == 0 ? string.Empty : $"tags: {string.Join(", ", movie.Tags)}",
            ThumbnailPath = GetExistingThumbnailPath(movie),
            CacheState = "not cached"
        };
        item.HasThumbnail = !string.IsNullOrWhiteSpace(item.ThumbnailPath);
        item.HasSeriesDetail = !string.IsNullOrWhiteSpace(item.SeriesDetail);
        item.HasTagsDetail = !string.IsNullOrWhiteSpace(item.TagsDetail);

        if (HasVideoCache(movie))
        {
            item.CacheState = "cached";
            return item;
        }

        if (string.IsNullOrWhiteSpace(movie.SourcePackageUri))
        {
            return item;
        }

        item.HasAction = true;
        item.ActionText = "取得";
        item.CacheState = $"Drive shell{FormatDownloadEstimate(movie.SourcePackageSize)}";
        try
        {
            var state = _googleDriveSyncService.GetPackageDownloadState(CreatePackageCandidate(movie));
            if (state.CompletedAvailable)
            {
                item.ActionText = "取り込み";
                item.CacheState = "package ready";
            }
            else if (state.CanResume)
            {
                item.ActionText = "続きから";
                item.CacheState = state.Percent is null
                    ? $"resume {FormatBytes(state.PartialBytes)}{FormatDownloadEstimate(state.TotalBytes)}"
                    : $"resume {state.Percent.Value:0}% ({FormatBytes(state.PartialBytes)}{FormatDownloadEstimate(state.TotalBytes)})";
            }
        }
        catch
        {
            // If local download-state inspection fails, the normal Drive fetch path will show the real error.
        }

        return item;
    }

    private View CreateMovieCard()
    {
        const double ThumbnailWidth = 118;
        const double ThumbnailHeight = 82;

        var thumbnail = new Image
        {
            Aspect = Aspect.AspectFit,
            WidthRequest = ThumbnailWidth,
            HeightRequest = ThumbnailHeight,
            BackgroundColor = Color.FromArgb("#101A27")
        };
        thumbnail.SetBinding(Image.SourceProperty, nameof(MovieListItem.ThumbnailPath));
        thumbnail.SetBinding(IsVisibleProperty, nameof(MovieListItem.HasThumbnail));

        var placeholder = new Grid
        {
            WidthRequest = ThumbnailWidth,
            HeightRequest = ThumbnailHeight,
            BackgroundColor = Color.FromArgb("#101A27"),
            Children =
            {
                new Label
                {
                    Text = "MOVIE",
                    TextColor = Color.FromArgb("#44546A"),
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            }
        };
        placeholder.SetBinding(IsVisibleProperty, nameof(MovieListItem.HasNoThumbnail));

        var thumbnailFrame = new Border
        {
            WidthRequest = ThumbnailWidth,
            HeightRequest = ThumbnailHeight,
            Stroke = Color.FromArgb("#26364A"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 7 },
            BackgroundColor = Color.FromArgb("#101A27"),
            Content = new Grid
            {
                Children = { placeholder, thumbnail }
            }
        };

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

        var series = new Label
        {
            TextColor = Color.FromArgb("#F6D365"),
            FontSize = 12,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        series.SetBinding(Label.TextProperty, nameof(MovieListItem.SeriesDetail));
        series.SetBinding(IsVisibleProperty, nameof(MovieListItem.HasSeriesDetail));

        var tags = new Label
        {
            TextColor = Color.FromArgb("#8CE7B2"),
            FontSize = 11,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        tags.SetBinding(Label.TextProperty, nameof(MovieListItem.TagsDetail));
        tags.SetBinding(IsVisibleProperty, nameof(MovieListItem.HasTagsDetail));

        var textStack = new VerticalStackLayout
        {
            Spacing = 3,
            Children = { title, series, detail, tags }
        };

        var cache = new Label
        {
            TextColor = Color.FromArgb("#5DE0D0"),
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.End
        };
        cache.SetBinding(Label.TextProperty, nameof(MovieListItem.CacheState));

        var actionButton = new Button
        {
            BackgroundColor = Color.FromArgb("#5DE0D0"),
            TextColor = Color.FromArgb("#04100F"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 12,
            CornerRadius = 8,
            HeightRequest = 34,
            MinimumWidthRequest = 76,
            Padding = new Thickness(10, 0)
        };
        actionButton.SetBinding(Button.TextProperty, nameof(MovieListItem.ActionText));
        actionButton.SetBinding(IsVisibleProperty, nameof(MovieListItem.HasAction));
        actionButton.Clicked += OnMovieActionButtonClicked;

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(ThumbnailWidth)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            Children = { thumbnailFrame, textStack, cache, actionButton }
        };
        Grid.SetRowSpan(thumbnailFrame, 2);
        Grid.SetColumn(textStack, 1);
        Grid.SetRowSpan(textStack, 2);
        Grid.SetColumn(cache, 2);
        Grid.SetRow(cache, 1);
        Grid.SetColumn(actionButton, 2);

        var card = new Border
        {
            Margin = new Thickness(18, 6),
            Padding = new Thickness(14),
            Stroke = Color.FromArgb("#1E2A3A"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Color.FromArgb("#0B111A"),
            Content = grid
        };
        card.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command<MovieListItem?>(item =>
            {
                if (item is not null)
                {
                    _ = OpenMovieAsync(item.MovieId);
                }
            }),
            CommandParameter = card.BindingContext
        });
        card.BindingContextChanged += (_, _) =>
        {
            if (card.GestureRecognizers.OfType<TapGestureRecognizer>().FirstOrDefault() is { } tap)
            {
                tap.CommandParameter = card.BindingContext;
            }
        };
        return card;
    }

    private static string? GetExistingThumbnailPath(Movie movie)
    {
        return !string.IsNullOrWhiteSpace(movie.Video.ThumbnailPath)
            && File.Exists(movie.Video.ThumbnailPath)
            ? movie.Video.ThumbnailPath
            : null;
    }

    private static string FormatMovieDetail(Movie movie)
    {
        var detail = $"{movie.SubtitleTracks.Count} subtitle / {movie.SceneMarkers.Count} scene";
        var playback = movie.Playback;
        if (playback.PositionSeconds <= 1d
            || (playback.DurationSeconds > 0 && playback.PositionSeconds >= Math.Max(0d, playback.DurationSeconds - 5d)))
        {
            return detail;
        }

        return $"{detail} / resume {FormatPlaybackTimestamp(playback.PositionSeconds)}";
    }

    private static string FormatPlaybackTimestamp(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
    }

    private static string FormatMovieSeriesDetail(Movie movie)
    {
        var series = string.IsNullOrWhiteSpace(movie.SeriesTitle) ? string.Empty : movie.SeriesTitle;
        var seasonEpisode = MovieMetadataInferenceService.FormatSeasonEpisode(movie);
        return string.Join(' ', new[] { series, seasonEpisode }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string GetSeriesTitle(Movie movie)
    {
        return string.IsNullOrWhiteSpace(movie.SeriesTitle) ? "未分類" : movie.SeriesTitle.Trim();
    }

    private static string GetSeriesKey(Movie movie)
    {
        return GetSeriesTitle(movie).ToLowerInvariant();
    }

    private static string GetSeasonTitle(Movie movie)
    {
        return movie.SeasonNumber is null ? "Season 未設定" : $"Season {movie.SeasonNumber.Value}";
    }

    private static string GetSeasonKey(Movie movie)
    {
        return movie.SeasonNumber?.ToString("000", System.Globalization.CultureInfo.InvariantCulture) ?? "none";
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
            HeightRequest = 38,
            FontSize = 13,
            Padding = new Thickness(8, 0)
        };
    }

    private static bool HasVideoCache(Movie movie)
    {
        return !string.IsNullOrWhiteSpace(movie.Video.CachePath)
            && File.Exists(movie.Video.CachePath);
    }

    private static string GetCacheStateLabel(Movie movie)
    {
        if (HasVideoCache(movie))
        {
            return "cached";
        }

        return string.IsNullOrWhiteSpace(movie.SourcePackageUri)
            ? "not cached"
            : $"Drive shell{FormatDownloadEstimate(movie.SourcePackageSize)}";
    }

    private static string FormatDownloadEstimate(long? bytes)
    {
        return bytes is > 0 ? $" / {FormatBytes(bytes.Value)}" : string.Empty;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)Math.Max(0, bytes);
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{bytes} {units[unitIndex]}" : $"{size:0.0} {units[unitIndex]}";
    }


}
