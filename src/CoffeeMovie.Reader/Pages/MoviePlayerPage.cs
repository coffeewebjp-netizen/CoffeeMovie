using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Core.Services;
using CoffeeMovie.Reader.Models;
using CoffeeMovie.Reader.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Media;

namespace CoffeeMovie.Reader.Pages;

public sealed partial class MoviePlayerPage : ContentPage
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
    private readonly GoogleDriveSyncService _googleDriveSyncService;
    private readonly CoffeeLearningWordRegistrationService _coffeeLearningService;
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
    private readonly Image _playerThumbnailImage = new()
    {
        Aspect = Aspect.AspectFit,
        IsVisible = false,
        InputTransparent = true
    };
    private readonly Border _playerThumbnailLayer = new()
    {
        BackgroundColor = Colors.Black,
        InputTransparent = true,
        IsVisible = false
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
    private readonly Label _coffeeLearningMemoStatusLabel = new()
    {
        Text = "✓ CoffeeLearning登録済",
        TextColor = Color.FromArgb("#5DE0D0"),
        FontSize = 12,
        FontAttributes = FontAttributes.Bold,
        IsVisible = false
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
    private readonly Button _registerCoffeeLearningButton = CreateSecondaryButton("単語登録");
    private readonly Button _shadowingButton = CreateShadowButton("音声入力", "#5DE0D0", "#04100F");
    private readonly Button _shadowOkButton = CreateShadowButton("手動OK", "#142033", "#FFFFFF");
    private readonly Button _shadowNgButton = CreateShadowButton("手動NG", "#142033", "#FFFFFF");
    private readonly Button _fullscreenButton = CreateSecondaryButton("全画面");
    private readonly Button _headerSubtitlePositionButton = CreateSecondaryButton("字幕位置:下");
    private readonly Button _headerSubtitleAlignmentButton = CreateSecondaryButton("字幕寄せ:中央");
    private readonly Button _exitFullscreenButton = CreateOverlayButton("戻る");
    private readonly Button _fullscreenShadowingButton = CreateOverlayButton("Shadow");
    private readonly Button _fullscreenRegisterCoffeeLearningButton = CreateOverlayButton("単語登録");
    private readonly Button _speakOriginalButton = CreateOverlayButton("原文音声");
    private readonly Button _playPauseButton = CreateOverlayButton("一時停止");
    private readonly Button _rewindOneButton = CreateCompactOverlayButton("-1秒");
    private readonly Button _rewindFiveButton = CreateCompactOverlayButton("-5秒");
    private readonly Button _rewindCustomButton = CreateCompactOverlayButton("-3秒");
    private readonly Button _forwardOneButton = CreateCompactOverlayButton("+1秒");
    private readonly Button _forwardFiveButton = CreateCompactOverlayButton("+5秒");
    private readonly Button _forwardCustomButton = CreateCompactOverlayButton("+3秒");
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
    private bool _isCoffeeLearningRegisterBusy;
    private DateTimeOffset _lastPlaybackPositionSavedAt;
    private double _lastPlaybackPositionSavedSeconds = -1d;
    private string _subtitlePosition = "bottom";
    private string _subtitleAlignment = "center";
    private int _customRewindSeconds = 3;

    public MoviePlayerPage(
        ReaderLibraryService libraryService,
        GoogleDriveSyncService googleDriveSyncService,
        CoffeeLearningWordRegistrationService coffeeLearningService,
        ISpeechRecognitionService speechRecognitionService,
        string movieId)
    {
        _libraryService = libraryService;
        _googleDriveSyncService = googleDriveSyncService;
        _coffeeLearningService = coffeeLearningService;
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
        _fullscreenRegisterCoffeeLearningButton.Clicked += async (_, _) => await RegisterCurrentCueInCoffeeLearningAsync();
        _fullscreenRegisterCoffeeLearningButton.IsVisible = false;
        _speakOriginalButton.Clicked += async (_, _) => await SpeakCurrentSubtitleAsync();
        _speakOriginalButton.IsVisible = false;
        _playPauseButton.Clicked += async (_, _) => await TogglePlayPauseAsync();
        _playPauseButton.IsVisible = false;
        _rewindOneButton.Clicked += async (_, _) => await SeekRelativeAsync(-1);
        _rewindOneButton.IsVisible = false;
        _rewindFiveButton.Clicked += async (_, _) => await SeekRelativeAsync(-5);
        _rewindFiveButton.IsVisible = false;
        _rewindCustomButton.Clicked += async (_, _) => await SeekRelativeAsync(-_customRewindSeconds);
        _rewindCustomButton.IsVisible = false;
        _forwardOneButton.Clicked += async (_, _) => await SeekRelativeAsync(1);
        _forwardOneButton.IsVisible = false;
        _forwardFiveButton.Clicked += async (_, _) => await SeekRelativeAsync(5);
        _forwardFiveButton.IsVisible = false;
        _forwardCustomButton.Clicked += async (_, _) => await SeekRelativeAsync(_customRewindSeconds);
        _forwardCustomButton.IsVisible = false;
        _rewindSettingsButton.Clicked += async (_, _) => await EditCustomRewindSecondsAsync();
        _rewindSettingsButton.IsVisible = false;
        _fullscreenSubtitlePositionButton.IsVisible = false;
        _fullscreenSubtitleAlignmentButton.IsVisible = false;
        _saveLearningButton.Clicked += async (_, _) => await SaveLearningAsync();
        _registerCoffeeLearningButton.Clicked += async (_, _) => await RegisterCurrentCueInCoffeeLearningAsync();
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
        _customRewindSeconds = Math.Clamp(Preferences.Default.Get(CustomRewindSecondsPreferenceKey, 3), 1, 9999);
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

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await NotifyPlayerPositionAsync();
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
            Children = { _speakOriginalButton, _fullscreenRegisterCoffeeLearningButton, _fullscreenShadowingButton, _exitFullscreenButton }
        };

        var rewindRow = new HorizontalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.End,
            Children = { _rewindOneButton, _rewindFiveButton, _rewindCustomButton }
        };

        var forwardRow = new HorizontalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.End,
            Children = { _forwardOneButton, _forwardFiveButton, _forwardCustomButton }
        };

        _rewindSettingsButton.HorizontalOptions = LayoutOptions.End;
        var rewindActions = new VerticalStackLayout
        {
            Spacing = 4,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Children = { rewindRow, forwardRow, _rewindSettingsButton }
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

        _playerThumbnailLayer.Content = _playerThumbnailImage;

        var playerLayer = new Grid
        {
            Children = { _webView, _playerThumbnailLayer, _playerMessageLabel, overlayActions }
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
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8,
            Children = { _learningMessageLabel, _registerCoffeeLearningButton, _saveLearningButton }
        };
        Grid.SetColumn(_registerCoffeeLearningButton, 1);
        Grid.SetColumn(_saveLearningButton, 2);

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
                _coffeeLearningMemoStatusLabel,
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

        _titleLabel.Text = FormatPlayerTitle(_movie);
        _statusLabel.Text = _movie.SubtitleTracks.Count == 0
            ? "字幕なし"
            : $"{_movie.SubtitleTracks.Count} subtitle / {_movie.SceneMarkers.Count} scene";

        UpdatePlayerThumbnail(_movie, show: true);
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

    private static string CollapseWhitespace(string value)
    {
        return string.Join(' ', value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string FormatTimestamp(TimeSpan value)
    {
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
    }

    private static string FormatPlayerTitle(Movie movie)
    {
        var series = string.IsNullOrWhiteSpace(movie.SeriesTitle) ? string.Empty : movie.SeriesTitle;
        var seasonEpisode = MovieMetadataInferenceService.FormatSeasonEpisode(movie);
        var prefix = string.Join(' ', new[] { series, seasonEpisode }.Where(part => part.Length > 0));
        return string.IsNullOrWhiteSpace(prefix) ? movie.Title : $"{prefix} - {movie.Title}";
    }
}
