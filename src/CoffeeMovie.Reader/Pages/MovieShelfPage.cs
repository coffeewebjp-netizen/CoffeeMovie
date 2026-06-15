using CoffeeMovie.Core.Models;
using CoffeeMovie.Reader.Models;
using CoffeeMovie.Reader.Services;
using Microsoft.Maui.Controls.Shapes;

namespace CoffeeMovie.Reader.Pages;

public sealed class MovieShelfPage : ContentPage
{
    private const string DefaultGoogleDriveClientId = "327808944898-6q8gs3t06ts12tsaqagg5268tahug7ru.apps.googleusercontent.com";

    private readonly ReaderLibraryService _libraryService;
    private readonly GoogleDriveSyncService _googleDriveSyncService;
    private readonly ReaderSyncSettingsService _syncSettingsService;
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
    private readonly Button _importButton = CreateHeaderButton("動画");
    private readonly Button _driveSettingsButton = CreateHeaderButton("Drive設定");
    private readonly Button _syncButton = CreateHeaderButton("同期");
    private readonly HashSet<string> _collapsedSeries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _collapsedSeasons = new(StringComparer.OrdinalIgnoreCase);
    private bool _isSyncing;
    private bool _isOpeningMovie;

    public MovieShelfPage(
        ReaderLibraryService libraryService,
        GoogleDriveSyncService googleDriveSyncService,
        ReaderSyncSettingsService syncSettingsService,
        ISpeechRecognitionService speechRecognitionService)
    {
        _libraryService = libraryService;
        _googleDriveSyncService = googleDriveSyncService;
        _syncSettingsService = syncSettingsService;
        _speechRecognitionService = speechRecognitionService;
        Title = "CoffeeMovie";
        BackgroundColor = Color.FromArgb("#05070B");
        NavigationPage.SetHasNavigationBar(this, false);

        _importButton.Clicked += async (_, _) => await ImportVideoAsync();
        _driveSettingsButton.Clicked += async (_, _) => await ConfigureGoogleDriveAsync();
        _syncButton.Clicked += async (_, _) => await SyncGoogleDriveAsync();

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
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8,
            Children = { _importButton, _driveSettingsButton, _syncButton }
        };
        Grid.SetColumn(_driveSettingsButton, 1);
        Grid.SetColumn(_syncButton, 2);

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
                            Text = "動画棚",
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
        var items = movies.Select(CreateMovieListItem).ToList();

        _moviesView.ItemsSource = BuildShelfRows(items);
        _emptyLabel.IsVisible = items.Count == 0;
        if (!_isSyncing)
        {
            _summaryLabel.Text = $"{items.Count} movies";
        }
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
            await Navigation.PushAsync(new MoviePlayerPage(_libraryService, _speechRecognitionService, movie.Id));
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

            await Navigation.PushAsync(new MoviePlayerPage(_libraryService, _speechRecognitionService, movie.Id));
        }
        finally
        {
            _isOpeningMovie = false;
        }
    }

    private async Task ConfigureGoogleDriveAsync()
    {
        try
        {
            var settings = await _syncSettingsService.LoadSettingsAsync();
            var clientId = await DisplayPromptAsync(
                "Google Drive設定",
                "OAuth Client ID",
                "次へ",
                "キャンセル",
                initialValue: string.IsNullOrWhiteSpace(settings.GoogleDriveClientId)
                    ? DefaultGoogleDriveClientId
                    : settings.GoogleDriveClientId);
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return;
            }

            var folder = await DisplayPromptAsync(
                "Google Drive設定",
                "同期フォルダURLまたはフォルダID",
                "保存",
                "キャンセル",
                initialValue: string.IsNullOrWhiteSpace(settings.GoogleDriveFolderId)
                    ? string.Empty
                    : settings.GoogleDriveFolderId);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            settings = await _googleDriveSyncService.SaveConfigurationAsync(clientId, null, folder);
            var shouldLogin = await DisplayAlertAsync(
                "Google Drive設定",
                "設定を保存しました。Googleログインを開きますか？",
                "ログイン",
                "後で");
            if (!shouldLogin)
            {
                _summaryLabel.Text = "Google Drive設定を保存しました";
                return;
            }

            await AuthorizeGoogleDriveAsync(settings);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Google Drive設定に失敗しました", ex.Message, "閉じる");
        }
    }

    private async Task SyncGoogleDriveAsync()
    {
        if (_isSyncing)
        {
            return;
        }

        SetSyncBusy(true, "Google Drive同期を開始しています...");
        try
        {
            if (!await EnsureGoogleDriveReadyAsync())
            {
                return;
            }

            SetSyncBusy(true, "Google Driveを確認しています...");
            var packages = await _googleDriveSyncService.FindPackagesAsync();
            if (packages.Count == 0)
            {
                _summaryLabel.Text = "同期対象の .coffeemovie がありません";
                return;
            }

            var imported = 0;
            var unchanged = 0;
            var skipped = 0;
            var failed = 0;
            for (var index = 0; index < packages.Count; index++)
            {
                var package = packages[index];
                if (!package.HasSidecar)
                {
                    skipped++;
                    continue;
                }

                string? tempPath = null;
                try
                {
                    _summaryLabel.Text = $"同期中 ({index + 1}/{packages.Count}): {package.DisplayName}";
                    tempPath = await _googleDriveSyncService.DownloadSidecarToCacheAsync(
                        package,
                        CreateTransferProgress("メタ取得中"));
                    if (await _libraryService.ImportDriveSidecarAsync(package, tempPath))
                    {
                        imported++;
                    }
                    else
                    {
                        unchanged++;
                    }
                }
                catch
                {
                    failed++;
                }
                finally
                {
                    DeleteFileQuietly(tempPath);
                }
            }

            await ReloadAsync();
            _summaryLabel.Text = $"Drive同期完了: 追加/更新 {imported} / 変更なし {unchanged} / sidecarなし {skipped} / 失敗 {failed}";
        }
        catch (GoogleDriveReconnectRequiredException ex)
        {
            await DisplayAlertAsync("Google Driveの再接続が必要です", ex.Message, "閉じる");
            await ConfigureGoogleDriveAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Google Drive同期に失敗しました", ex.Message, "閉じる");
        }
        finally
        {
            SetSyncBusy(false);
        }
    }

    private async Task<bool> EnsureGoogleDriveReadyAsync()
    {
        var settings = await _syncSettingsService.LoadSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.GoogleDriveClientId)
            || string.IsNullOrWhiteSpace(settings.GoogleDriveFolderId))
        {
            await ConfigureGoogleDriveAsync();
            return await _googleDriveSyncService.IsConfiguredAsync();
        }

        if (await _googleDriveSyncService.IsConfiguredAsync())
        {
            return true;
        }

        var shouldLogin = await DisplayAlertAsync(
            "Google Drive",
            "Google Driveに接続します。",
            "ログイン",
            "キャンセル");
        if (!shouldLogin)
        {
            return false;
        }

        await AuthorizeGoogleDriveAsync(settings);
        return await _googleDriveSyncService.IsConfiguredAsync();
    }

    private async Task<bool?> ChooseDownloadModeAsync(Movie movie, SyncMovieCandidate package)
    {
        var state = _googleDriveSyncService.GetPackageDownloadState(package);
        if (state.CompletedAvailable)
        {
            return false;
        }

        if (state.CanResume)
        {
            var resumeLabel = state.Percent is null
                ? $"続きから取得 ({FormatBytes(state.PartialBytes)} 取得済み)"
                : $"続きから取得 ({state.Percent.Value:0}% / {FormatBytes(state.PartialBytes)} 取得済み)";
            var choice = await DisplayActionSheetAsync(
                $"「{movie.Title}」をGoogle Driveから取得します",
                "キャンセル",
                "最初から取得",
                resumeLabel);
            if (choice == resumeLabel)
            {
                return false;
            }

            return choice == "最初から取得" ? true : null;
        }

        var shouldDownload = await DisplayAlertAsync(
            "動画キャッシュを取得",
            $"「{movie.Title}」をGoogle Driveから取得して開きますか？{FormatDownloadEstimate(movie.SourcePackageSize)}",
            "取得",
            "キャンセル");
        return shouldDownload ? false : null;
    }

    private async Task AuthorizeGoogleDriveAsync(ReaderSyncSettings settings)
    {
        await _googleDriveSyncService.AuthorizeWithBrowserAsync(
            settings,
            new Progress<string>(message => _summaryLabel.Text = message));
        _summaryLabel.Text = "Google Driveに接続しました";
    }

    private async Task<Movie> DownloadMovieCacheAsync(Movie movie, bool restartDownload)
    {
        var package = CreatePackageCandidate(movie);

        string? tempPath = null;
        try
        {
            SetSyncBusy(true, restartDownload
                ? $"動画を最初から取得中: {movie.Title}"
                : $"動画を取得中: {movie.Title}");
            tempPath = await _googleDriveSyncService.DownloadPackageToCacheAsync(
                package,
                CreateTransferProgress(restartDownload ? "動画再取得中" : "動画取得中"),
                restartDownload);
            return await _libraryService.ImportCoffeeMoviePackageFileAsync(tempPath, package);
        }
        finally
        {
            DeleteFileQuietly(tempPath);
            SetSyncBusy(false);
        }
    }

    private static SyncMovieCandidate CreatePackageCandidate(Movie movie)
    {
        return new SyncMovieCandidate
        {
            ContentUri = movie.SourcePackageUri ?? string.Empty,
            FileName = string.IsNullOrWhiteSpace(movie.SourcePackageName)
                ? $"{movie.Title}.coffeemovie"
                : movie.SourcePackageName,
            LastModified = movie.SourcePackageLastModified,
            Size = movie.SourcePackageSize
        };
    }

    private IProgress<SyncTransferProgress> CreateTransferProgress(string label)
    {
        return new Progress<SyncTransferProgress>(progress =>
        {
            var percent = progress.Percent is null ? string.Empty : $" {progress.Percent.Value:0}%";
            var total = progress.TotalBytes is > 0 ? $" / {FormatBytes(progress.TotalBytes.Value)}" : string.Empty;
            _summaryLabel.Text = $"{label}: {progress.FileName}{percent} ({FormatBytes(progress.BytesTransferred)}{total})";
        });
    }

    private IReadOnlyList<object> BuildShelfRows(IReadOnlyList<MovieListItem> items)
    {
        var rows = new List<object>();
        foreach (var seriesGroup in items.GroupBy(item => item.SeriesKey))
        {
            var seriesItems = seriesGroup.ToList();
            var seriesTitle = seriesItems[0].SeriesTitle;
            var seriesExpanded = !_collapsedSeries.Contains(seriesGroup.Key);
            rows.Add(new ShelfHeaderRow(
                Key: seriesGroup.Key,
                ParentKey: null,
                Title: seriesTitle,
                Detail: $"{seriesItems.Count} episode",
                Level: 0,
                IsExpanded: seriesExpanded));
            if (!seriesExpanded)
            {
                continue;
            }

            foreach (var seasonGroup in seriesItems.GroupBy(item => item.SeasonKey))
            {
                var seasonItems = seasonGroup.ToList();
                var seasonKey = $"{seriesGroup.Key}|{seasonGroup.Key}";
                var seasonExpanded = !_collapsedSeasons.Contains(seasonKey);
                rows.Add(new ShelfHeaderRow(
                    Key: seasonKey,
                    ParentKey: seriesGroup.Key,
                    Title: seasonItems[0].SeasonTitle,
                    Detail: $"{seasonItems.Count} episode",
                    Level: 1,
                    IsExpanded: seasonExpanded));
                if (seasonExpanded)
                {
                    rows.AddRange(seasonItems);
                }
            }
        }

        return rows;
    }

    private View CreateSeriesHeaderRow()
    {
        return CreateShelfHeaderRow(Color.FromArgb("#F6D365"), new Thickness(18, 10, 18, 4));
    }

    private View CreateSeasonHeaderRow()
    {
        return CreateShelfHeaderRow(Color.FromArgb("#5DE0D0"), new Thickness(34, 6, 18, 4));
    }

    private View CreateShelfHeaderRow(Color accentColor, Thickness margin)
    {
        var marker = new Label
        {
            TextColor = accentColor,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center,
            WidthRequest = 24
        };
        marker.SetBinding(Label.TextProperty, nameof(ShelfHeaderRow.Marker));

        var title = new Label
        {
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 15,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        title.SetBinding(Label.TextProperty, nameof(ShelfHeaderRow.Title));

        var detail = new Label
        {
            TextColor = Color.FromArgb("#A5B3C6"),
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center
        };
        detail.SetBinding(Label.TextProperty, nameof(ShelfHeaderRow.Detail));

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children = { marker, title, detail }
        };
        Grid.SetColumn(title, 1);
        Grid.SetColumn(detail, 2);

        var border = new Border
        {
            Margin = margin,
            Padding = new Thickness(10, 8),
            Stroke = Color.FromArgb("#1E2A3A"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Color.FromArgb("#111A27"),
            Content = grid
        };
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command<ShelfHeaderRow?>(ToggleShelfHeader),
            CommandParameter = border.BindingContext
        });
        border.BindingContextChanged += (_, _) =>
        {
            if (border.GestureRecognizers.OfType<TapGestureRecognizer>().FirstOrDefault() is { } tap)
            {
                tap.CommandParameter = border.BindingContext;
            }
        };
        return border;
    }

    private void ToggleShelfHeader(ShelfHeaderRow? row)
    {
        if (row is null)
        {
            return;
        }

        var target = row.Level == 0 ? _collapsedSeries : _collapsedSeasons;
        if (!target.Add(row.Key))
        {
            target.Remove(row.Key);
        }

        _ = ReloadAsync();
    }

    private MovieListItem CreateMovieListItem(Movie movie)
    {
        var item = new MovieListItem
        {
            MovieId = movie.Id,
            Title = movie.Title,
            Detail = $"{movie.SubtitleTracks.Count} subtitle / {movie.SceneMarkers.Count} scene",
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

    private static string FormatMovieSeriesDetail(Movie movie)
    {
        var series = string.IsNullOrWhiteSpace(movie.SeriesTitle) ? string.Empty : movie.SeriesTitle;
        var seasonEpisode = FormatSeasonEpisode(movie);
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

    private static string FormatSeasonEpisode(Movie movie)
    {
        var season = movie.SeasonNumber is null ? string.Empty : $"S{movie.SeasonNumber.Value:00}";
        var episode = movie.EpisodeNumber is null ? string.Empty : $"E{movie.EpisodeNumber.Value:00}";
        return string.Join(' ', new[] { season, episode }.Where(part => part.Length > 0));
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

    private void SetSyncBusy(bool busy, string? message = null)
    {
        _isSyncing = busy;
        _syncButton.IsEnabled = !busy;
        _driveSettingsButton.IsEnabled = !busy;
        _importButton.IsEnabled = !busy;
        _moviesView.IsEnabled = !busy;
        _syncButton.Opacity = busy ? 0.55 : 1;
        _driveSettingsButton.Opacity = busy ? 0.55 : 1;
        _importButton.Opacity = busy ? 0.55 : 1;
        _moviesView.Opacity = busy ? 0.65 : 1;
        if (!string.IsNullOrWhiteSpace(message))
        {
            _summaryLabel.Text = message;
        }
    }

    private static void DeleteFileQuietly(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary Drive downloads are best-effort cleanup.
        }
    }

    private sealed record ShelfHeaderRow(
        string Key,
        string? ParentKey,
        string Title,
        string Detail,
        int Level,
        bool IsExpanded)
    {
        public string Marker => IsExpanded ? "v" : ">";
    }

    private sealed class ShelfRowTemplateSelector : DataTemplateSelector
    {
        public DataTemplate SeriesTemplate { get; set; } = null!;

        public DataTemplate SeasonTemplate { get; set; } = null!;

        public DataTemplate MovieTemplate { get; set; } = null!;

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        {
            return item switch
            {
                ShelfHeaderRow { Level: 0 } => SeriesTemplate,
                ShelfHeaderRow => SeasonTemplate,
                _ => MovieTemplate
            };
        }
    }
}
