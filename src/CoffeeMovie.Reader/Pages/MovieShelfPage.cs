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
    private readonly Button _importButton = CreateHeaderButton("動画");
    private readonly Button _driveSettingsButton = CreateHeaderButton("Drive設定");
    private readonly Button _syncButton = CreateHeaderButton("同期");
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

        _moviesView.ItemTemplate = new DataTemplate(CreateMovieCard);
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

        _moviesView.ItemsSource = items;
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

    private MovieListItem CreateMovieListItem(Movie movie)
    {
        var item = new MovieListItem
        {
            MovieId = movie.Id,
            Title = movie.Title,
            Detail = $"{movie.SubtitleTracks.Count} subtitle / {movie.SceneMarkers.Count} scene",
            CacheState = "not cached"
        };

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
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            Children = { title, detail, cache, actionButton }
        };
        Grid.SetRow(detail, 1);
        Grid.SetColumn(cache, 1);
        Grid.SetRow(cache, 1);
        Grid.SetColumn(actionButton, 1);

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
}
