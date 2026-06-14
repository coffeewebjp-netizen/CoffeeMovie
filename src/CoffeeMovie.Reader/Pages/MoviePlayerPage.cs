using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Reader.Models;
using CoffeeMovie.Reader.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Media;

namespace CoffeeMovie.Reader.Pages;

public sealed class MoviePlayerPage : ContentPage
{
    private const string BridgeScheme = "coffeemovie";
    private const string ShowEnglishSubtitlesPreferenceKey = "coffee-movie-show-english-subtitles";
    private const string ShowJapaneseSubtitlesPreferenceKey = "coffee-movie-show-japanese-subtitles";
    private const string ShowMemoPreferenceKey = "coffee-movie-show-memo";
    private const string SubtitlePositionPreferenceKey = "coffee-movie-subtitle-position";
    private const string SubtitleAlignmentPreferenceKey = "coffee-movie-subtitle-alignment";
    private const string CustomRewindSecondsPreferenceKey = "coffee-movie-custom-rewind-seconds";
    private const double ShadowingPassThreshold = 0.82d;
    private static readonly string[] SubtitlePositions = ["bottom", "middle", "top"];
    private static readonly string[] SubtitleAlignments = ["center", "left", "right"];

    private readonly ReaderLibraryService _libraryService;
    private readonly ISpeechRecognitionService _speechRecognitionService;
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
        HeightRequest = 240
    };
    private readonly Switch _englishSubtitleSwitch = new()
    {
        IsToggled = true,
        OnColor = Color.FromArgb("#5DE0D0"),
        ThumbColor = Colors.White
    };
    private readonly Switch _japaneseSubtitleSwitch = new()
    {
        IsToggled = true,
        OnColor = Color.FromArgb("#F6D365"),
        ThumbColor = Colors.White
    };
    private readonly Switch _memoSwitch = new()
    {
        IsToggled = true,
        OnColor = Color.FromArgb("#8CE7B2"),
        ThumbColor = Colors.White
    };
    private readonly Label _currentCueMetaLabel = new()
    {
        TextColor = Color.FromArgb("#5DE0D0"),
        FontSize = 12,
        HorizontalTextAlignment = TextAlignment.End
    };
    private readonly Label _currentCueLabel = new()
    {
        Text = "再生を一時停止すると英語字幕を編集できます。",
        TextColor = Colors.White,
        FontSize = 15,
        FontAttributes = FontAttributes.Bold,
        LineBreakMode = LineBreakMode.WordWrap
    };
    private readonly Label _aiNoteLabel = new()
    {
        TextColor = Color.FromArgb("#F6D365"),
        FontSize = 12,
        LineBreakMode = LineBreakMode.WordWrap,
        IsVisible = false
    };
    private readonly Entry _tagsEntry = new()
    {
        Placeholder = "タグ（カンマ区切り）",
        TextColor = Colors.White,
        PlaceholderColor = Color.FromArgb("#607086"),
        BackgroundColor = Color.FromArgb("#111A27"),
        ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
        HeightRequest = 42
    };
    private readonly Editor _noteEditor = new()
    {
        Placeholder = "自分メモ",
        TextColor = Colors.White,
        PlaceholderColor = Color.FromArgb("#607086"),
        BackgroundColor = Color.FromArgb("#111A27"),
        AutoSize = EditorAutoSizeOption.Disabled,
        HeightRequest = 74
    };
    private readonly Label _learningMessageLabel = new()
    {
        TextColor = Color.FromArgb("#A5B3C6"),
        FontSize = 12
    };
    private readonly Label _shadowingStatusLabel = new()
    {
        TextColor = Color.FromArgb("#A5B3C6"),
        FontSize = 12,
        VerticalTextAlignment = TextAlignment.Center
    };
    private readonly Button _saveLearningButton = CreateSecondaryButton("保存");
    private readonly Button _shadowingButton = CreateShadowButton("音声入力", "#5DE0D0", "#04100F");
    private readonly Button _shadowOkButton = CreateShadowButton("手動OK", "#142033", "#FFFFFF");
    private readonly Button _shadowNgButton = CreateShadowButton("手動NG", "#142033", "#FFFFFF");
    private readonly Button _fullscreenButton = CreateSecondaryButton("全画面");
    private readonly Button _headerSubtitlePositionButton = CreateSecondaryButton("字幕位置:下");
    private readonly Button _headerSubtitleAlignmentButton = CreateSecondaryButton("字幕寄せ:中央");
    private readonly Button _exitFullscreenButton = CreateOverlayButton("戻る");
    private readonly Button _fullscreenShadowingButton = CreateOverlayButton("Shadow");
    private readonly Button _speakOriginalButton = CreateOverlayButton("原文音声");
    private readonly Button _playPauseButton = CreateOverlayButton("一時停止");
    private readonly Button _rewindOneButton = CreateCompactOverlayButton("-1秒");
    private readonly Button _rewindFiveButton = CreateCompactOverlayButton("-5秒");
    private readonly Button _rewindCustomButton = CreateCompactOverlayButton("-3秒");
    private readonly Button _rewindSettingsButton = CreateCompactOverlayButton("秒設定");
    private readonly Button _fullscreenSubtitlePositionButton = CreateOverlayButton("字幕位置:下");
    private readonly Button _fullscreenSubtitleAlignmentButton = CreateOverlayButton("字幕寄せ:中央");
    private readonly Label _playerMessageLabel = new()
    {
        TextColor = Colors.White,
        FontSize = 13,
        FontAttributes = FontAttributes.Bold,
        BackgroundColor = Color.FromArgb("#CC0B111A"),
        Padding = new Thickness(10, 7),
        LineBreakMode = LineBreakMode.WordWrap,
        HorizontalTextAlignment = TextAlignment.Center,
        IsVisible = false
    };
    private readonly CollectionView _scenesView = new()
    {
        SelectionMode = SelectionMode.Single
    };
    private Grid? _rootGrid;
    private RowDefinition? _headerRow;
    private RowDefinition? _playerRow;
    private RowDefinition? _learningRow;
    private RowDefinition? _sceneRow;
    private View? _headerView;
    private Border? _playerFrame;
    private View? _learningView;
    private View? _sceneView;
    private Movie? _movie;
    private SubtitleTrack? _activeEnglishTrack;
    private SubtitleCue? _activeEnglishCue;
    private SubtitleCueLearningState? _activeLearningState;
    private bool _loaded;
    private bool _updatingLearningFields;
    private bool _isFullscreen;
    private bool _showSpeakOriginalButton;
    private bool _isPlayerPaused = true;
    private string _subtitlePosition = "bottom";
    private string _subtitleAlignment = "center";
    private int _customRewindSeconds = 3;

    public MoviePlayerPage(
        ReaderLibraryService libraryService,
        ISpeechRecognitionService speechRecognitionService,
        string movieId)
    {
        _libraryService = libraryService;
        _speechRecognitionService = speechRecognitionService;
        _movieId = movieId;
        Title = "Player";
        BackgroundColor = Color.FromArgb("#05070B");
        NavigationPage.SetHasNavigationBar(this, true);

        var subtitleButton = CreateActionButton("字幕を追加");
        subtitleButton.Clicked += async (_, _) => await ImportSubtitleAsync();
        _fullscreenButton.Clicked += (_, _) => SetFullscreen(true);
        _headerSubtitlePositionButton.Clicked += async (_, _) => await CycleSubtitlePositionAsync();
        _fullscreenSubtitlePositionButton.Clicked += async (_, _) => await CycleSubtitlePositionAsync();
        _headerSubtitleAlignmentButton.Clicked += async (_, _) => await CycleSubtitleAlignmentAsync();
        _fullscreenSubtitleAlignmentButton.Clicked += async (_, _) => await CycleSubtitleAlignmentAsync();
        _exitFullscreenButton.Clicked += (_, _) => SetFullscreen(false);
        _exitFullscreenButton.IsVisible = false;
        _fullscreenShadowingButton.Clicked += async (_, _) => await RunShadowingRecognitionAsync();
        _fullscreenShadowingButton.IsVisible = false;
        _speakOriginalButton.Clicked += async (_, _) => await SpeakCurrentSubtitleAsync();
        _speakOriginalButton.IsVisible = false;
        _playPauseButton.Clicked += async (_, _) => await TogglePlayPauseAsync();
        _playPauseButton.IsVisible = false;
        _rewindOneButton.Clicked += async (_, _) => await RewindAsync(1);
        _rewindOneButton.IsVisible = false;
        _rewindFiveButton.Clicked += async (_, _) => await RewindAsync(5);
        _rewindFiveButton.IsVisible = false;
        _rewindCustomButton.Clicked += async (_, _) => await RewindAsync(_customRewindSeconds);
        _rewindCustomButton.IsVisible = false;
        _rewindSettingsButton.Clicked += async (_, _) => await EditCustomRewindSecondsAsync();
        _rewindSettingsButton.IsVisible = false;
        _fullscreenSubtitlePositionButton.IsVisible = false;
        _fullscreenSubtitleAlignmentButton.IsVisible = false;
        _saveLearningButton.Clicked += async (_, _) => await SaveLearningAsync();
        _shadowingButton.Clicked += async (_, _) => await RunShadowingRecognitionAsync();
        _shadowOkButton.Clicked += async (_, _) => await RecordShadowingAsync(true, null, 1d);
        _shadowNgButton.Clicked += async (_, _) => await RecordShadowingAsync(false, null, 0d);
        _webView.Navigating += OnWebViewNavigating;
        _webView.Navigated += async (_, _) =>
        {
            await ApplySubtitleSwitchesAsync(savePreferences: false);
            await ApplySubtitlePositionAsync();
            await ApplySubtitleAlignmentAsync();
            await ApplyFullscreenModeToPlayerAsync(_isFullscreen);
        };
        SizeChanged += (_, _) => UpdatePlayerHeight();
        _englishSubtitleSwitch.IsToggled = Preferences.Default.Get(ShowEnglishSubtitlesPreferenceKey, true);
        _japaneseSubtitleSwitch.IsToggled = Preferences.Default.Get(ShowJapaneseSubtitlesPreferenceKey, true);
        _memoSwitch.IsToggled = Preferences.Default.Get(ShowMemoPreferenceKey, true);
        _subtitlePosition = NormalizeSubtitlePosition(Preferences.Default.Get(SubtitlePositionPreferenceKey, "bottom"));
        _subtitleAlignment = NormalizeSubtitleAlignment(Preferences.Default.Get(SubtitleAlignmentPreferenceKey, "center"));
        _customRewindSeconds = Math.Clamp(Preferences.Default.Get(CustomRewindSecondsPreferenceKey, 3), 1, 30);
        UpdateSubtitlePositionButtons();
        UpdateSubtitleAlignmentButtons();
        UpdateRewindButtonLabels();
        _englishSubtitleSwitch.Toggled += async (_, _) => await ApplySubtitleSwitchesAsync();
        _japaneseSubtitleSwitch.Toggled += async (_, _) => await ApplySubtitleSwitchesAsync();
        _memoSwitch.Toggled += async (_, _) => await ApplySubtitleSwitchesAsync();

        _scenesView.ItemTemplate = new DataTemplate(CreateSceneRow);
        _scenesView.SelectionChanged += OnSceneSelected;

        var headerActions = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8,
            RowSpacing = 8,
            Children =
            {
                subtitleButton,
                _fullscreenButton,
                _headerSubtitlePositionButton,
                _headerSubtitleAlignmentButton
            }
        };
        Grid.SetColumn(_fullscreenButton, 1);
        Grid.SetRow(_headerSubtitlePositionButton, 1);
        Grid.SetRow(_headerSubtitleAlignmentButton, 1);
        Grid.SetColumn(_headerSubtitleAlignmentButton, 1);

        var header = new VerticalStackLayout
        {
            Padding = new Thickness(16, 14, 16, 8),
            Spacing = 8,
            Children =
            {
                _titleLabel,
                _statusLabel,
                CreateSubtitleSwitchRow(),
                headerActions
            }
        };
        _headerView = header;
        Grid.SetRow(header, 0);

        _headerRow = new RowDefinition(GridLength.Auto);
        _playerRow = new RowDefinition(GridLength.Auto);
        _learningRow = new RowDefinition(GridLength.Auto);
        _sceneRow = new RowDefinition(GridLength.Star);
        _playerFrame = CreatePlayerFrame();
        _learningView = CreateLearningFrame();
        _sceneView = CreateSceneFrame();
        _rootGrid = new Grid
        {
            RowDefinitions =
            {
                _headerRow,
                _playerRow,
                _learningRow,
                _sceneRow
            },
            Children =
            {
                header,
                _playerFrame,
                _learningView,
                _sceneView
            }
        };
        Content = _rootGrid;

        ClearLearningTarget("再生を一時停止すると英語字幕を編集できます。");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        UpdatePlayerHeight();
        await ReloadAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        if (_isFullscreen)
        {
            SetFullscreen(false);
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private void UpdatePlayerHeight()
    {
        if (_isFullscreen)
        {
            _webView.HeightRequest = Math.Max(220, Height);
            return;
        }

        var usableWidth = Math.Max(0, Width - 32);
        if (usableWidth <= 0)
        {
            return;
        }

        var isLandscape = Width > Height && Height > 0;
        var targetHeight = isLandscape
            ? Math.Clamp(Height - 96, 220, 520)
            : Math.Clamp(usableWidth * 9d / 16d, 220, 320);
        _webView.HeightRequest = targetHeight;
    }

    private Border CreatePlayerFrame()
    {
        var topOverlayActions = new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Children = { _speakOriginalButton, _fullscreenShadowingButton, _exitFullscreenButton }
        };

        var customRewindActions = new VerticalStackLayout
        {
            Spacing = 4,
            Children = { _rewindCustomButton, _rewindSettingsButton }
        };

        var rewindActions = new HorizontalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Children = { _rewindOneButton, _rewindFiveButton, customRewindActions }
        };

        var overlayActions = new VerticalStackLayout
        {
            Spacing = 7,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 10, 10, 0),
            Children =
            {
                topOverlayActions,
                _playPauseButton,
                rewindActions,
                _fullscreenSubtitlePositionButton,
                _fullscreenSubtitleAlignmentButton
            }
        };

        _playPauseButton.HorizontalOptions = LayoutOptions.End;
        _fullscreenSubtitlePositionButton.HorizontalOptions = LayoutOptions.End;
        _fullscreenSubtitleAlignmentButton.HorizontalOptions = LayoutOptions.End;

        _playerMessageLabel.HorizontalOptions = LayoutOptions.Fill;
        _playerMessageLabel.VerticalOptions = LayoutOptions.End;
        _playerMessageLabel.Margin = new Thickness(16, 0, 16, 16);

        var playerLayer = new Grid
        {
            Children = { _webView, _playerMessageLabel, overlayActions }
        };

        var frame = new Border
        {
            Margin = new Thickness(16, 0, 16, 10),
            Stroke = Color.FromArgb("#1E2A3A"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Colors.Black,
            Content = playerLayer
        };

        Grid.SetRow(frame, 1);
        return frame;
    }

    private void SetFullscreen(bool fullscreen)
    {
        if (_isFullscreen == fullscreen)
        {
            return;
        }

        _isFullscreen = fullscreen;
        NavigationPage.SetHasNavigationBar(this, !fullscreen);
        if (_headerRow is not null)
        {
            _headerRow.Height = fullscreen ? new GridLength(0) : GridLength.Auto;
        }

        if (_playerRow is not null)
        {
            _playerRow.Height = fullscreen ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        }

        if (_learningRow is not null)
        {
            _learningRow.Height = fullscreen ? new GridLength(0) : GridLength.Auto;
        }

        if (_sceneRow is not null)
        {
            _sceneRow.Height = fullscreen ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        }

        if (_headerView is not null)
        {
            _headerView.IsVisible = !fullscreen;
        }

        if (_learningView is not null)
        {
            _learningView.IsVisible = !fullscreen;
        }

        if (_sceneView is not null)
        {
            _sceneView.IsVisible = !fullscreen;
        }

        if (_playerFrame is not null)
        {
            _playerFrame.Margin = fullscreen ? new Thickness(0) : new Thickness(16, 0, 16, 10);
            _playerFrame.StrokeThickness = fullscreen ? 0 : 1;
            _playerFrame.StrokeShape = fullscreen ? null : new RoundRectangle { CornerRadius = 8 };
        }

        _exitFullscreenButton.IsVisible = fullscreen;
        UpdateFullscreenOverlayControls();
        SetSystemFullscreen(fullscreen);
        UpdatePlayerHeight();
        _ = ApplyFullscreenModeToPlayerAsync(fullscreen);
    }

    private View CreateSubtitleSwitchRow()
    {
        var row = new FlexLayout
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlignItems.Center,
            JustifyContent = FlexJustify.Start,
            Children =
            {
                CreateSwitchGroup("英語", _englishSubtitleSwitch),
                CreateSwitchGroup("日本語", _japaneseSubtitleSwitch),
                CreateSwitchGroup("メモ", _memoSwitch)
            }
        };

        return row;
    }

    private static View CreateSwitchGroup(string labelText, Switch toggle)
    {
        var label = new Label
        {
            Text = labelText,
            TextColor = Color.FromArgb("#A5B3C6"),
            FontSize = 12,
            VerticalTextAlignment = TextAlignment.Center
        };

        return new HorizontalStackLayout
        {
            Spacing = 6,
            Margin = new Thickness(0, 0, 12, 6),
            Children = { label, toggle }
        };
    }

    private Border CreateLearningFrame()
    {
        var headerTitle = new Label
        {
            Text = "字幕メモ / シャドーイング",
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 15
        };
        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children = { headerTitle, _currentCueMetaLabel }
        };
        Grid.SetColumn(_currentCueMetaLabel, 1);

        var tagsLabel = new Label
        {
            Text = "タグ",
            TextColor = Color.FromArgb("#A5B3C6"),
            FontSize = 12,
            WidthRequest = 48,
            VerticalTextAlignment = TextAlignment.Center
        };
        var tagRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            Children = { tagsLabel, _tagsEntry }
        };
        Grid.SetColumn(_tagsEntry, 1);

        var noteLabel = new Label
        {
            Text = "自分メモ",
            TextColor = Color.FromArgb("#A5B3C6"),
            FontSize = 12
        };

        var shadowRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8,
            Children = { _shadowingButton, _shadowOkButton, _shadowNgButton, _shadowingStatusLabel }
        };
        Grid.SetColumn(_shadowOkButton, 1);
        Grid.SetColumn(_shadowNgButton, 2);
        Grid.SetColumn(_shadowingStatusLabel, 3);

        var saveRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children = { _learningMessageLabel, _saveLearningButton }
        };
        Grid.SetColumn(_saveLearningButton, 1);

        var layout = new VerticalStackLayout
        {
            Padding = new Thickness(14, 12),
            Spacing = 8,
            Children =
            {
                header,
                _currentCueLabel,
                _aiNoteLabel,
                tagRow,
                noteLabel,
                _noteEditor,
                shadowRow,
                saveRow
            }
        };

        var frame = new Border
        {
            Margin = new Thickness(16, 0, 16, 10),
            Stroke = Color.FromArgb("#1E2A3A"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Color.FromArgb("#0B111A"),
            Content = layout
        };

        Grid.SetRow(frame, 2);
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

        Grid.SetRow(frame, 3);
        return frame;
    }

    private async Task ReloadAsync()
    {
        _movie = await _libraryService.GetMovieAsync(_movieId);
        if (_movie is null)
        {
            _titleLabel.Text = "動画が見つかりません";
            _statusLabel.Text = "ライブラリから削除された可能性があります。";
            ClearLearningTarget("動画が見つかりません。");
            return;
        }

        _activeEnglishTrack = FindEnglishTrack(_movie)
            ?? _movie.SubtitleTracks.LastOrDefault(track => track.Cues.Count > 0);

        _titleLabel.Text = _movie.Title;
        _statusLabel.Text = _movie.SubtitleTracks.Count == 0
            ? "字幕なし"
            : $"{_movie.SubtitleTracks.Count} subtitle / {_movie.SceneMarkers.Count} scene";

        await LoadPlayerHtmlAsync(_movie);

        _scenesView.ItemsSource = _movie.SceneMarkers
            .Select(marker => new SceneJumpItem
            {
                Label = marker.Label,
                Timestamp = FormatTimestamp(marker.Start),
                StartSeconds = marker.Start.TotalSeconds
            })
            .ToList();

        ClearLearningTarget(_activeEnglishTrack is null
            ? "英語字幕がありません。"
            : "再生を一時停止すると英語字幕を編集できます。");
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
            await DisplayAlertAsync("字幕の取り込みに失敗しました", ex.Message, "閉じる");
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

    private void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, BridgeScheme, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        if (string.Equals(uri.Host, "playstate", StringComparison.OrdinalIgnoreCase))
        {
            var stateQuery = ParseQuery(uri.Query);
            stateQuery.TryGetValue("state", out var state);
            MainThread.BeginInvokeOnMainThread(() => UpdatePlayerState(state));
            return;
        }

        if (!string.Equals(uri.Host, "cue", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var query = ParseQuery(uri.Query);
        query.TryGetValue("cueId", out var cueId);
        MainThread.BeginInvokeOnMainThread(() => SelectActiveCue(cueId ?? string.Empty));
    }

    private void UpdatePlayerState(string? state)
    {
        if (string.Equals(state, "paused", StringComparison.OrdinalIgnoreCase))
        {
            _isPlayerPaused = true;
        }
        else if (string.Equals(state, "playing", StringComparison.OrdinalIgnoreCase))
        {
            _isPlayerPaused = false;
        }

        UpdatePlayPauseButton();
    }

    private void SelectActiveCue(string cueId)
    {
        if (_movie is null)
        {
            ClearLearningTarget("動画が見つかりません。");
            return;
        }

        _activeEnglishTrack ??= FindEnglishTrack(_movie)
            ?? _movie.SubtitleTracks.LastOrDefault(track => track.Cues.Count > 0);
        if (_activeEnglishTrack is null)
        {
            ClearLearningTarget("英語字幕がありません。");
            return;
        }

        if (string.IsNullOrWhiteSpace(cueId))
        {
            ClearLearningTarget("英語字幕の外です。");
            return;
        }

        var cue = _activeEnglishTrack.Cues.FirstOrDefault(item =>
            string.Equals(item.Id, cueId, StringComparison.Ordinal));
        if (cue is null)
        {
            ClearLearningTarget("字幕キューが見つかりません。");
            return;
        }

        _activeEnglishCue = cue;
        _activeLearningState = GetOrCreateLearningState(_activeEnglishTrack, cue);
        BindLearningState();
    }

    private void BindLearningState()
    {
        if (_activeEnglishCue is null || _activeLearningState is null)
        {
            return;
        }

        _updatingLearningFields = true;
        _currentCueLabel.Text = CollapseWhitespace(_activeEnglishCue.Text);
        _currentCueMetaLabel.Text = $"{_activeEnglishCue.Index}  {FormatTimestamp(_activeEnglishCue.Start)}";
        _tagsEntry.Text = string.Join(", ", _activeLearningState.Tags);
        _noteEditor.Text = _activeLearningState.Note ?? string.Empty;
        _aiNoteLabel.Text = _activeLearningState.AiNote ?? string.Empty;
        _aiNoteLabel.IsVisible = !string.IsNullOrWhiteSpace(_activeLearningState.AiNote);
        _learningMessageLabel.Text = "現在の英語字幕";
        _learningMessageLabel.TextColor = Color.FromArgb("#A5B3C6");
        UpdateShadowingStatus();
        SetLearningControlsEnabled(true);
        _updatingLearningFields = false;
    }

    private async Task SaveLearningAsync()
    {
        if (_updatingLearningFields || _movie is null || _activeLearningState is null)
        {
            return;
        }

        _activeLearningState.Tags = ParseTags(_tagsEntry.Text);
        _activeLearningState.Note = string.IsNullOrWhiteSpace(_noteEditor.Text)
            ? null
            : _noteEditor.Text.Trim();
        _activeLearningState.UpdatedAt = DateTimeOffset.UtcNow;

        await _libraryService.SaveMovieAsync(_movie);
        _learningMessageLabel.Text = "保存しました";
        _learningMessageLabel.TextColor = Color.FromArgb("#5DE0D0");
    }

    private async Task RunShadowingRecognitionAsync()
    {
        if (!_englishSubtitleSwitch.IsToggled)
        {
            await DisplayAlertAsync("シャドーイング", "英語字幕をONにするとシャドーイングできます。", "閉じる");
            return;
        }

        if (_movie is null || _activeEnglishCue is null || _activeLearningState is null)
        {
            await DisplayAlertAsync("シャドーイング", "対象の英語字幕がありません。動画を一時停止して字幕を選んでください。", "閉じる");
            return;
        }

        var targetText = CollapseWhitespace(_activeEnglishCue.Text);
        if (string.IsNullOrWhiteSpace(targetText))
        {
            return;
        }

        _shadowingButton.IsEnabled = false;
        _fullscreenShadowingButton.IsEnabled = false;
        _shadowingButton.Text = "聞き取り中";
        _learningMessageLabel.Text = $"発音してください: {targetText}";
        _learningMessageLabel.TextColor = Color.FromArgb("#F6D365");
        SetPlayerMessage($"音声入力待ち\n{targetText}", Color.FromArgb("#F6D365"));
        _showSpeakOriginalButton = false;
        UpdateFullscreenOverlayControls();

        try
        {
            await SetShadowingHighlightAsync(true);
            var transcript = await _speechRecognitionService.RecognizeEnglishAsync();
            var accuracy = CalculateShadowingAccuracy(targetText, transcript);
            var accepted = accuracy >= ShadowingPassThreshold;
            await RecordShadowingAsync(accepted, transcript, accuracy);
            _learningMessageLabel.Text =
                $"{(accepted ? "OK" : "NG")} {accuracy * 100d:0}%: {transcript}";
            _learningMessageLabel.TextColor = accepted
                ? Color.FromArgb("#5DE0D0")
                : Color.FromArgb("#FF9AA5");
            SetPlayerMessage(
                $"{(accepted ? "OK" : "NG")} {accuracy * 100d:0}%\n入力: {transcript}",
                accepted ? Color.FromArgb("#5DE0D0") : Color.FromArgb("#FF9AA5"),
                showSpeakOriginalButton: !accepted);
        }
        catch (Exception ex)
        {
            _learningMessageLabel.Text = "音声入力に失敗しました";
            _learningMessageLabel.TextColor = Color.FromArgb("#FF9AA5");
            SetPlayerMessage("音声入力に失敗しました", Color.FromArgb("#FF9AA5"));
            await DisplayAlertAsync("音声入力に失敗しました", ex.Message, "閉じる");
        }
        finally
        {
            await SetShadowingHighlightAsync(false);
            _shadowingButton.Text = "音声入力";
            SetLearningControlsEnabled(_activeLearningState is not null);
            UpdateFullscreenOverlayControls();
        }
    }

    private async Task RecordShadowingAsync(bool accepted, string? transcript, double? accuracy)
    {
        if (_movie is null || _activeLearningState is null)
        {
            return;
        }

        _activeLearningState.Tags = ParseTags(_tagsEntry.Text);
        _activeLearningState.Note = string.IsNullOrWhiteSpace(_noteEditor.Text)
            ? null
            : _noteEditor.Text.Trim();

        _activeLearningState.Shadowing ??= new CuePracticeMetric();
        if (accepted)
        {
            _activeLearningState.Shadowing.OkCount++;
        }
        else
        {
            _activeLearningState.Shadowing.NgCount++;
        }

        var total = _activeLearningState.Shadowing.OkCount + _activeLearningState.Shadowing.NgCount;
        _activeLearningState.Shadowing.AttemptCount = total;
        _activeLearningState.Shadowing.LastAccuracy = accuracy ?? (accepted ? 1d : 0d);
        _activeLearningState.Shadowing.BestAccuracy = Math.Max(
            _activeLearningState.Shadowing.BestAccuracy ?? 0d,
            _activeLearningState.Shadowing.LastAccuracy ?? 0d);
        _activeLearningState.Shadowing.LastTranscript = string.IsNullOrWhiteSpace(transcript)
            ? _activeLearningState.Shadowing.LastTranscript
            : transcript.Trim();
        _activeLearningState.Shadowing.LastPracticedAt = DateTimeOffset.UtcNow;
        _activeLearningState.UpdatedAt = DateTimeOffset.UtcNow;

        await _libraryService.SaveMovieAsync(_movie);
        UpdateShadowingStatus();
        if (string.IsNullOrWhiteSpace(transcript))
        {
            _learningMessageLabel.Text = accepted ? "Shadow OKを記録しました" : "Shadow NGを記録しました";
            _learningMessageLabel.TextColor = accepted ? Color.FromArgb("#5DE0D0") : Color.FromArgb("#FF9AA5");
        }
    }

    private async Task SpeakCurrentSubtitleAsync()
    {
        var targetText = CollapseWhitespace(_activeEnglishCue?.Text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(targetText))
        {
            return;
        }

        try
        {
            var locales = await TextToSpeech.Default.GetLocalesAsync();
            var englishLocale = locales.FirstOrDefault(locale =>
                locale.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            await TextToSpeech.Default.SpeakAsync(targetText, new SpeechOptions
            {
                Locale = englishLocale
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("原文音声", ex.Message, "閉じる");
        }
    }

    private void SetPlayerMessage(string? message, Color? color = null, bool showSpeakOriginalButton = false)
    {
        _playerMessageLabel.Text = message ?? string.Empty;
        _playerMessageLabel.TextColor = color ?? Colors.White;
        _showSpeakOriginalButton = showSpeakOriginalButton;
        UpdateFullscreenOverlayControls();
    }

    private async Task SetShadowingHighlightAsync(bool active)
    {
        try
        {
            await _webView.EvaluateJavaScriptAsync(
                $"window.coffeeMovieSetShadowingActive && window.coffeeMovieSetShadowingActive({(active ? "true" : "false")});");
        }
        catch
        {
            // The WebView may be between navigations.
        }
    }

    private async Task ApplyFullscreenModeToPlayerAsync(bool fullscreen)
    {
        try
        {
            await _webView.EvaluateJavaScriptAsync(
                $"window.coffeeMovieSetAppFullscreen && window.coffeeMovieSetAppFullscreen({(fullscreen ? "true" : "false")});");
        }
        catch
        {
            // The WebView may not have loaded the player script yet.
        }
    }

    private async Task TogglePlayPauseAsync()
    {
        try
        {
            var state = await _webView.EvaluateJavaScriptAsync(
                "window.coffeeMovieTogglePlayPause && window.coffeeMovieTogglePlayPause();");
            UpdatePlayerState(CleanJavaScriptString(state));
        }
        catch
        {
            // The WebView may not have loaded the player script yet.
        }
    }

    private async Task RewindAsync(int seconds)
    {
        var safeSeconds = Math.Clamp(seconds, 1, 30);
        try
        {
            await _webView.EvaluateJavaScriptAsync(
                $"window.coffeeMovieRewind && window.coffeeMovieRewind({safeSeconds.ToString(CultureInfo.InvariantCulture)});");
        }
        catch
        {
            // The WebView may not have loaded the player script yet.
        }
    }

    private async Task EditCustomRewindSecondsAsync()
    {
        var result = await DisplayPromptAsync(
            "戻る秒数",
            "カスタム戻し秒数を 1-30 秒で指定します。",
            "保存",
            "キャンセル",
            "3",
            maxLength: 2,
            keyboard: Keyboard.Numeric,
            initialValue: _customRewindSeconds.ToString(CultureInfo.InvariantCulture));
        if (string.IsNullOrWhiteSpace(result))
        {
            return;
        }

        if (!int.TryParse(result.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            await DisplayAlertAsync("戻る秒数", "数字で入力してください。", "閉じる");
            return;
        }

        _customRewindSeconds = Math.Clamp(seconds, 1, 30);
        Preferences.Default.Set(CustomRewindSecondsPreferenceKey, _customRewindSeconds);
        UpdateRewindButtonLabels();
    }

    private async Task CycleSubtitlePositionAsync()
    {
        var currentIndex = Array.IndexOf(SubtitlePositions, NormalizeSubtitlePosition(_subtitlePosition));
        var nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % SubtitlePositions.Length;
        _subtitlePosition = SubtitlePositions[nextIndex];
        Preferences.Default.Set(SubtitlePositionPreferenceKey, _subtitlePosition);
        UpdateSubtitlePositionButtons();
        await ApplySubtitlePositionAsync();
    }

    private async Task CycleSubtitleAlignmentAsync()
    {
        var currentIndex = Array.IndexOf(SubtitleAlignments, NormalizeSubtitleAlignment(_subtitleAlignment));
        var nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % SubtitleAlignments.Length;
        _subtitleAlignment = SubtitleAlignments[nextIndex];
        Preferences.Default.Set(SubtitleAlignmentPreferenceKey, _subtitleAlignment);
        UpdateSubtitleAlignmentButtons();
        await ApplySubtitleAlignmentAsync();
    }

    private async Task ApplySubtitlePositionAsync()
    {
        try
        {
            var position = JsonSerializer.Serialize(NormalizeSubtitlePosition(_subtitlePosition));
            await _webView.EvaluateJavaScriptAsync(
                $"window.coffeeMovieSetSubtitlePosition && window.coffeeMovieSetSubtitlePosition({position});");
        }
        catch
        {
            // The WebView may not have loaded the player script yet.
        }
    }

    private async Task ApplySubtitleAlignmentAsync()
    {
        try
        {
            var alignment = JsonSerializer.Serialize(NormalizeSubtitleAlignment(_subtitleAlignment));
            await _webView.EvaluateJavaScriptAsync(
                $"window.coffeeMovieSetSubtitleAlignment && window.coffeeMovieSetSubtitleAlignment({alignment});");
        }
        catch
        {
            // The WebView may not have loaded the player script yet.
        }
    }

    private void UpdatePlayPauseButton()
    {
        _playPauseButton.Text = _isPlayerPaused ? "再開" : "一時停止";
    }

    private void UpdateRewindButtonLabels()
    {
        _rewindCustomButton.Text = $"-{_customRewindSeconds}秒";
    }

    private void UpdateSubtitlePositionButtons()
    {
        var label = NormalizeSubtitlePosition(_subtitlePosition) switch
        {
            "top" => "上",
            "middle" => "中央",
            _ => "下"
        };
        _headerSubtitlePositionButton.Text = $"字幕位置:{label}";
        _fullscreenSubtitlePositionButton.Text = $"字幕位置:{label}";
    }

    private void UpdateSubtitleAlignmentButtons()
    {
        var label = NormalizeSubtitleAlignment(_subtitleAlignment) switch
        {
            "left" => "左",
            "right" => "右",
            _ => "中央"
        };
        _headerSubtitleAlignmentButton.Text = $"字幕寄せ:{label}";
        _fullscreenSubtitleAlignmentButton.Text = $"字幕寄せ:{label}";
    }

    private void ClearLearningTarget(string message)
    {
        _updatingLearningFields = true;
        _activeEnglishCue = null;
        _activeLearningState = null;
        _currentCueLabel.Text = message;
        _currentCueMetaLabel.Text = string.Empty;
        _tagsEntry.Text = string.Empty;
        _noteEditor.Text = string.Empty;
        _aiNoteLabel.Text = string.Empty;
        _aiNoteLabel.IsVisible = false;
        _learningMessageLabel.Text = "対象字幕なし";
        _learningMessageLabel.TextColor = Color.FromArgb("#A5B3C6");
        _shadowingStatusLabel.Text = "OK 0 / NG 0";
        SetPlayerMessage(null);
        SetLearningControlsEnabled(false);
        _updatingLearningFields = false;
    }

    private void SetLearningControlsEnabled(bool enabled)
    {
        var shadowingEnabled = enabled && _englishSubtitleSwitch.IsToggled;
        _tagsEntry.IsEnabled = enabled;
        _noteEditor.IsEnabled = enabled;
        _saveLearningButton.IsEnabled = enabled;
        _shadowingButton.IsEnabled = shadowingEnabled;
        _shadowOkButton.IsEnabled = shadowingEnabled;
        _shadowNgButton.IsEnabled = shadowingEnabled;
        _fullscreenShadowingButton.IsEnabled = shadowingEnabled;

        var opacity = enabled ? 1d : 0.45d;
        var shadowOpacity = shadowingEnabled ? 1d : 0.45d;
        _tagsEntry.Opacity = opacity;
        _noteEditor.Opacity = opacity;
        _saveLearningButton.Opacity = opacity;
        _shadowingButton.Opacity = shadowOpacity;
        _shadowOkButton.Opacity = shadowOpacity;
        _shadowNgButton.Opacity = shadowOpacity;
        _fullscreenShadowingButton.Opacity = shadowOpacity;
        UpdateFullscreenOverlayControls();
    }

    private void UpdateFullscreenOverlayControls()
    {
        var hasMessage = !string.IsNullOrWhiteSpace(_playerMessageLabel.Text);
        _playerMessageLabel.IsVisible = _isFullscreen && hasMessage;
        _playPauseButton.IsVisible = _isFullscreen;
        _rewindOneButton.IsVisible = _isFullscreen;
        _rewindFiveButton.IsVisible = _isFullscreen;
        _rewindCustomButton.IsVisible = _isFullscreen;
        _rewindSettingsButton.IsVisible = _isFullscreen;
        _fullscreenSubtitlePositionButton.IsVisible = _isFullscreen;
        _fullscreenSubtitleAlignmentButton.IsVisible = _isFullscreen;
        _fullscreenShadowingButton.IsVisible = _isFullscreen
            && _englishSubtitleSwitch.IsToggled
            && _activeEnglishCue is not null;
        _speakOriginalButton.IsVisible = _isFullscreen
            && _showSpeakOriginalButton
            && _activeEnglishCue is not null;
    }

    private void UpdateShadowingStatus()
    {
        var metric = _activeLearningState?.Shadowing;
        var ok = metric?.OkCount ?? 0;
        var ng = metric?.NgCount ?? 0;
        var total = ok + ng;
        _shadowingStatusLabel.Text = total == 0
            ? "OK 0 / NG 0"
            : $"OK {ok} / NG {ng} / {(ok * 100d / total):0}% / 前回 {(metric?.LastAccuracy ?? 0d) * 100d:0}%";
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

        var grid = new Grid
        {
            Padding = new Thickness(0, 8),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            Children = { time, label }
        };
        Grid.SetColumn(label, 1);
        return grid;
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
            HeightRequest = 40,
            Padding = new Thickness(16, 0)
        };
    }

    private static Button CreateOverlayButton(string text)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb("#CC0B111A"),
            TextColor = Colors.White,
            BorderColor = Color.FromArgb("#5DE0D0"),
            BorderWidth = 1,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 8,
            HeightRequest = 38,
            MinimumWidthRequest = 72,
            Padding = new Thickness(12, 0)
        };
    }

    private static Button CreateCompactOverlayButton(string text)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb("#CC0B111A"),
            TextColor = Colors.White,
            BorderColor = Color.FromArgb("#2A3A50"),
            BorderWidth = 1,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 8,
            HeightRequest = 34,
            MinimumWidthRequest = 52,
            FontSize = 12,
            Padding = new Thickness(7, 0)
        };
    }

    private static Button CreateShadowButton(string text, string background, string foreground)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb(background),
            TextColor = Color.FromArgb(foreground),
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 8,
            HeightRequest = 40,
            Padding = new Thickness(6, 0)
        };
    }

    private static void SetSystemFullscreen(bool fullscreen)
    {
#if ANDROID
        var window = Platform.CurrentActivity?.Window;
        if (window?.DecorView is null)
        {
            return;
        }

#pragma warning disable CA1422
        window.DecorView.SystemUiFlags = fullscreen
            ? Android.Views.SystemUiFlags.Fullscreen
                | Android.Views.SystemUiFlags.HideNavigation
                | Android.Views.SystemUiFlags.ImmersiveSticky
                | Android.Views.SystemUiFlags.LayoutFullscreen
                | Android.Views.SystemUiFlags.LayoutHideNavigation
                | Android.Views.SystemUiFlags.LayoutStable
            : Android.Views.SystemUiFlags.Visible;
#pragma warning restore CA1422
#endif
    }

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
            BuildPlayerHtml(
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

    private async Task ApplySubtitleSwitchesAsync(bool savePreferences = true)
    {
        if (savePreferences)
        {
            Preferences.Default.Set(ShowEnglishSubtitlesPreferenceKey, _englishSubtitleSwitch.IsToggled);
            Preferences.Default.Set(ShowJapaneseSubtitlesPreferenceKey, _japaneseSubtitleSwitch.IsToggled);
            Preferences.Default.Set(ShowMemoPreferenceKey, _memoSwitch.IsToggled);
        }

        try
        {
            var showEnglish = _englishSubtitleSwitch.IsToggled ? "true" : "false";
            var showJapanese = _japaneseSubtitleSwitch.IsToggled ? "true" : "false";
            var showMemo = _memoSwitch.IsToggled ? "true" : "false";
            await _webView.EvaluateJavaScriptAsync($"window.coffeeMovieSetSubtitleVisibility && window.coffeeMovieSetSubtitleVisibility({showEnglish}, {showJapanese}, {showMemo});");
        }
        catch
        {
            // The WebView may not have loaded the player script yet.
        }

        SetLearningControlsEnabled(_activeLearningState is not null);
    }

    private static string BuildPlayerHtml(
        Movie movie,
        bool showEnglishSubtitles,
        bool showJapaneseSubtitles,
        bool showMemo,
        string subtitlePosition,
        string subtitleAlignment)
    {
        var videoUri = ToFileUri(movie.Video.CachePath);
        var safeSubtitlePosition = NormalizeSubtitlePosition(subtitlePosition);
        var safeSubtitleAlignment = NormalizeSubtitleAlignment(subtitleAlignment);
        var cueTrack = FindEnglishTrack(movie)
            ?? movie.SubtitleTracks.LastOrDefault(subtitle => subtitle.Cues.Count > 0);
        var japaneseTrack = FindJapaneseTrack(movie);
        var bridgeCuesJson = JsonSerializer.Serialize((cueTrack?.Cues ?? [])
            .Select(cue =>
            {
                var learningState = cueTrack is null ? null : FindLearningState(cueTrack, cue);
                return new
                {
                    cueId = cue.Id,
                    index = cue.Index,
                    start = cue.Start.TotalSeconds,
                    end = cue.End.TotalSeconds,
                    text = cue.Text,
                    memo = BuildDisplayMemo(learningState)
                };
            }));
        var japaneseCuesJson = JsonSerializer.Serialize((japaneseTrack?.Cues ?? [])
            .Select(cue => new
            {
                index = cue.Index,
                start = cue.Start.TotalSeconds,
                end = cue.End.TotalSeconds,
                text = cue.Text
            }));

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
.stage {
  position: relative;
  width: 100%;
  height: 100%;
  background: #000;
}
video {
  width: 100%;
  height: 100%;
  background: #000;
}
.subtitleOverlay {
  position: absolute;
  left: 50%;
  width: min(94vw, 1080px);
  transform: translateX(-50%);
  display: flex;
  flex-direction: column;
  gap: 5px;
  align-items: center;
  pointer-events: none;
  z-index: 10;
}
.subtitleOverlay.position-bottom {
  top: auto;
  bottom: calc(48px + env(safe-area-inset-bottom));
  transform: translateX(-50%);
}
.subtitleOverlay.position-middle {
  top: 50%;
  bottom: auto;
  transform: translate(-50%, -50%);
}
.subtitleOverlay.position-top {
  top: calc(48px + env(safe-area-inset-top));
  bottom: auto;
  transform: translateX(-50%);
}
.subtitleOverlay.align-left {
  align-items: flex-start;
}
.subtitleOverlay.align-center {
  align-items: center;
}
.subtitleOverlay.align-right {
  align-items: flex-end;
}
.subtitleLine {
  display: none;
  width: fit-content;
  max-width: 100%;
  box-sizing: border-box;
  padding: 3px 9px;
  border-radius: 6px;
  background: rgba(0, 0, 0, 0.58);
  color: #fff;
  font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  font-weight: 700;
  line-height: 1.32;
  text-align: center;
  text-shadow: 0 1px 2px #000, 0 0 3px #000;
  white-space: pre-wrap;
  overflow-wrap: normal;
  word-break: normal;
  text-wrap: wrap;
}
.subtitleLine.active {
  display: block;
}
#subtitleEn {
  font-size: clamp(15px, 3.8vw, 26px);
  overflow-wrap: break-word;
}
#subtitleEn.shadowing {
  outline: 2px solid #5DE0D0;
  background: rgba(9, 45, 42, 0.82);
  box-shadow: 0 0 18px rgba(93, 224, 208, 0.45);
}
#subtitleJa {
  font-size: clamp(14px, 3.4vw, 24px);
  color: #F6D365;
  line-break: strict;
  overflow-wrap: normal;
  word-break: normal;
}
#subtitleMemo {
  font-size: clamp(12px, 3vw, 20px);
  color: #BDEFCF;
  font-weight: 650;
  background: rgba(3, 22, 17, 0.72);
}
@media (orientation: landscape) {
  .subtitleOverlay {
    width: min(92vw, 1120px);
    gap: 4px;
  }
  .subtitleOverlay.position-bottom {
    bottom: calc(38px + env(safe-area-inset-bottom));
  }
  .subtitleOverlay.position-top {
    top: calc(38px + env(safe-area-inset-top));
  }
  #subtitleEn {
    font-size: clamp(13px, 2.35vw, 22px);
  }
  #subtitleJa {
    font-size: clamp(12px, 1.9vw, 19px);
  }
  #subtitleMemo {
    font-size: clamp(11px, 1.85vw, 18px);
  }
}
video::-webkit-media-controls-fullscreen-button {
  display: none;
}
</style>
</head>
<body>
<div class="stage">
  <video id="player" controls playsinline webkit-playsinline preload="metadata" controlsList="nofullscreen" disablepictureinpicture>
    <source src="{{Html(videoUri)}}" type="{{Html(movie.Video.ContentType ?? "video/mp4")}}">
  </video>
  <div class="subtitleOverlay position-{{Html(safeSubtitlePosition)}} align-{{Html(safeSubtitleAlignment)}}" aria-live="off">
    <div id="subtitleMemo" class="subtitleLine"></div>
    <div id="subtitleEn" class="subtitleLine"></div>
    <div id="subtitleJa" class="subtitleLine"></div>
  </div>
</div>
<script>
const coffeeMovieCues = {{bridgeCuesJson}};
const coffeeMovieJapaneseCues = {{japaneseCuesJson}};
const player = document.getElementById('player');
const subtitleOverlay = document.querySelector('.subtitleOverlay');
const subtitleMemo = document.getElementById('subtitleMemo');
const subtitleEn = document.getElementById('subtitleEn');
const subtitleJa = document.getElementById('subtitleJa');
let showEnglishSubtitles = {{showEnglishSubtitles.ToString().ToLowerInvariant()}};
let showJapaneseSubtitles = {{showJapaneseSubtitles.ToString().ToLowerInvariant()}};
let showMemo = {{showMemo.ToString().ToLowerInvariant()}};
let lastCoffeeMovieCueId = null;
let shadowingActive = false;
let coffeeMovieAppFullscreen = false;
let coffeeMovieSubtitlePosition = '{{Html(safeSubtitlePosition)}}';
let coffeeMovieSubtitleAlignment = '{{Html(safeSubtitleAlignment)}}';

function coffeeMovieFindCue(cues) {
  const time = player.currentTime || 0;
  return cues.find(cue => time >= cue.start && time <= cue.end) || null;
}

function coffeeMovieCurrentCue() {
  return coffeeMovieFindCue(coffeeMovieCues);
}

function coffeeMovieSetLine(element, text) {
  if (!text) {
    element.textContent = '';
    element.classList.remove('active');
    return;
  }

  element.textContent = text;
  element.classList.add('active');
}

function coffeeMovieJoinSubtitleLines(text, compact) {
  if (!text) {
    return '';
  }

  const lines = String(text)
    .replace(/\r\n/g, '\n')
    .split('\n')
    .map(line => line.trim())
    .filter(line => line.length > 0);
  return lines.join(compact ? '' : ' ');
}

function coffeeMovieRenderSubtitles() {
  const enCue = coffeeMovieCurrentCue();
  const jaCue = coffeeMovieFindCue(coffeeMovieJapaneseCues);
  coffeeMovieSetLine(subtitleMemo, showMemo && enCue ? enCue.memo : '');
  coffeeMovieSetLine(subtitleEn, showEnglishSubtitles && enCue ? coffeeMovieJoinSubtitleLines(enCue.text, false) : '');
  coffeeMovieSetLine(subtitleJa, showJapaneseSubtitles && jaCue ? coffeeMovieJoinSubtitleLines(jaCue.text, true) : '');
  subtitleEn.classList.toggle('shadowing', shadowingActive && showEnglishSubtitles && !!enCue);
}

window.coffeeMovieSetSubtitleVisibility = function(showEnglish, showJapanese, showMemoLine) {
  showEnglishSubtitles = !!showEnglish;
  showJapaneseSubtitles = !!showJapanese;
  showMemo = !!showMemoLine;
  coffeeMovieRenderSubtitles();
};

window.coffeeMovieSetShadowingActive = function(active) {
  shadowingActive = !!active;
  coffeeMovieRenderSubtitles();
};

window.coffeeMovieSetAppFullscreen = function(fullscreen) {
  coffeeMovieAppFullscreen = !!fullscreen;
};

window.coffeeMovieSetSubtitlePosition = function(position) {
  const normalized = ['top', 'middle', 'bottom'].includes(position) ? position : 'bottom';
  coffeeMovieSubtitlePosition = normalized;
  subtitleOverlay.classList.remove('position-top', 'position-middle', 'position-bottom');
  subtitleOverlay.classList.add('position-' + normalized);
};

window.coffeeMovieSetSubtitleAlignment = function(alignment) {
  const normalized = ['left', 'center', 'right'].includes(alignment) ? alignment : 'center';
  coffeeMovieSubtitleAlignment = normalized;
  subtitleOverlay.classList.remove('align-left', 'align-center', 'align-right');
  subtitleOverlay.classList.add('align-' + normalized);
};

function coffeeMovieNotifyCue(force) {
  const cue = coffeeMovieCurrentCue();
  const cueId = cue ? cue.cueId : '';
  coffeeMovieRenderSubtitles();
  if (!force && cueId === lastCoffeeMovieCueId) {
    return;
  }
  lastCoffeeMovieCueId = cueId;
  location.href = 'coffeemovie://cue?cueId=' + encodeURIComponent(cueId);
}

function coffeeMovieNotifyPlayState() {
  location.href = 'coffeemovie://playstate?state=' + (player.paused ? 'paused' : 'playing');
}

window.coffeeMovieJumpTo = function(seconds) {
  player.currentTime = seconds;
  player.play();
  setTimeout(() => coffeeMovieNotifyCue(true), 80);
};

window.coffeeMovieTogglePlayPause = function() {
  if (player.paused) {
    player.play();
    coffeeMovieNotifyPlayState();
    return 'playing';
  }

  player.pause();
  coffeeMovieNotifyPlayState();
  return 'paused';
};

window.coffeeMovieRewind = function(seconds) {
  const amount = Math.max(0, Number(seconds) || 0);
  player.currentTime = Math.max(0, (player.currentTime || 0) - amount);
  setTimeout(() => coffeeMovieNotifyCue(true), 50);
  return player.currentTime;
};

player.addEventListener('loadedmetadata', () => {
  coffeeMovieNotifyCue(true);
  setTimeout(coffeeMovieNotifyPlayState, 20);
});
player.addEventListener('timeupdate', () => coffeeMovieNotifyCue(false));
player.addEventListener('seeked', () => coffeeMovieNotifyCue(true));
player.addEventListener('pause', () => {
  coffeeMovieNotifyCue(true);
  setTimeout(coffeeMovieNotifyPlayState, 20);
});
player.addEventListener('play', () => {
  coffeeMovieNotifyCue(true);
  setTimeout(coffeeMovieNotifyPlayState, 20);
});
player.addEventListener('click', event => {
  if (!coffeeMovieAppFullscreen || player.paused) {
    return;
  }

  event.preventDefault();
  event.stopPropagation();
  player.pause();
}, true);
</script>
</body>
</html>
""";
    }

    private static SubtitleCueLearningState GetOrCreateLearningState(SubtitleTrack track, SubtitleCue cue)
    {
        var state = FindLearningState(track, cue);
        if (state is not null)
        {
            state.CueId = cue.Id;
            state.CueIndex = cue.Index;
            state.Tags ??= [];
            state.Listening ??= new CuePracticeMetric();
            state.Shadowing ??= new CuePracticeMetric();
            return state;
        }

        state = new SubtitleCueLearningState
        {
            CueId = cue.Id,
            CueIndex = cue.Index
        };
        track.CueLearningStates.Add(state);
        return state;
    }

    private static SubtitleCueLearningState? FindLearningState(SubtitleTrack track, SubtitleCue cue)
    {
        return track.CueLearningStates.FirstOrDefault(item =>
            string.Equals(item.CueId, cue.Id, StringComparison.Ordinal)
            || (item.CueIndex > 0 && item.CueIndex == cue.Index));
    }

    private static string BuildDisplayMemo(SubtitleCueLearningState? state)
    {
        if (state is null)
        {
            return string.Empty;
        }

        var parts = new[] { state.AiNote, state.Note }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => CollapseWhitespace(part!))
            .ToArray();
        return string.Join("\n", parts);
    }

    private static SubtitleTrack? FindEnglishTrack(Movie movie)
    {
        return movie.SubtitleTracks.FirstOrDefault(IsEnglishTrack);
    }

    private static SubtitleTrack? FindJapaneseTrack(Movie movie)
    {
        return movie.SubtitleTracks.FirstOrDefault(track => track.Role == SubtitleTrackRole.Translation && track.Cues.Count > 0)
            ?? movie.SubtitleTracks.FirstOrDefault(IsJapaneseTrack);
    }

    private static bool IsEnglishTrack(SubtitleTrack track)
    {
        var language = track.Language?.Trim().ToLowerInvariant();
        if (language is "en" or "eng" or "en-us" or "en-gb")
        {
            return true;
        }

        var fileName = track.SourceFileName.ToLowerInvariant();
        return fileName.EndsWith(".en.srt", StringComparison.Ordinal)
            || fileName.EndsWith(".en.vtt", StringComparison.Ordinal)
            || fileName.Contains(".en.", StringComparison.Ordinal);
    }

    private static bool IsJapaneseTrack(SubtitleTrack track)
    {
        var language = track.Language?.Trim().ToLowerInvariant();
        if (language is "ja" or "jpn" or "jp")
        {
            return true;
        }

        var fileName = track.SourceFileName.ToLowerInvariant();
        return fileName.EndsWith(".ja.srt", StringComparison.Ordinal)
            || fileName.EndsWith(".ja.vtt", StringComparison.Ordinal)
            || fileName.Contains(".ja.", StringComparison.Ordinal);
    }

    private static List<string> ParseTags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Split([',', '、', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = query.TrimStart('?');
        if (trimmed.Length == 0)
        {
            return result;
        }

        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace("+", " "));
            var value = parts.Length > 1
                ? Uri.UnescapeDataString(parts[1].Replace("+", " "))
                : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static string NormalizeSubtitlePosition(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return SubtitlePositions.Contains(normalized) ? normalized! : "bottom";
    }

    private static string NormalizeSubtitleAlignment(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return SubtitleAlignments.Contains(normalized) ? normalized! : "center";
    }

    private static string CleanJavaScriptString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().Trim('"', '\'');
    }

    private static string CollapseWhitespace(string value)
    {
        return string.Join(' ', value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static double CalculateShadowingAccuracy(string targetText, string transcript)
    {
        var expected = TokenizeForShadowing(targetText);
        var actual = TokenizeForShadowing(transcript);
        if (expected.Count == 0 || actual.Count == 0)
        {
            return 0d;
        }

        var distance = CalculateEditDistance(expected, actual);
        var denominator = Math.Max(expected.Count, actual.Count);
        return Math.Clamp(1d - (double)distance / denominator, 0d, 1d);
    }

    private static List<string> TokenizeForShadowing(string text)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        foreach (var character in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                continue;
            }

            if (character is '\'' or '’')
            {
                continue;
            }

            AddToken();
        }

        AddToken();
        return tokens;

        void AddToken()
        {
            if (builder.Length == 0)
            {
                return;
            }

            tokens.Add(builder.ToString());
            builder.Clear();
        }
    }

    private static int CalculateEditDistance(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var previous = new int[actual.Count + 1];
        var current = new int[actual.Count + 1];
        for (var column = 0; column <= actual.Count; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= expected.Count; row++)
        {
            current[0] = row;
            for (var column = 1; column <= actual.Count; column++)
            {
                var cost = string.Equals(expected[row - 1], actual[column - 1], StringComparison.Ordinal)
                    ? 0
                    : 1;
                current[column] = Math.Min(
                    Math.Min(previous[column] + 1, current[column - 1] + 1),
                    previous[column - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[actual.Count];
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
