using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Core.Services;
using CoffeeMovie.Storage.Models;
using CoffeeMovie.Storage.Services;
using CoffeeMovie.Studio.Services;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
    private readonly CoffeeMoviePaths _paths;
    private readonly MovieLibraryStore _libraryStore;
    private readonly CoffeeMoviePackageService _packageService = new();
    private readonly SubtitleGenerationJobService _subtitleGenerationJobService = new();
    private readonly CoffeeLearningWordRegistrationService _coffeeLearningService = new();
    private readonly CoffeeLearningAiAgentScoringService _coffeeLearningAiAgentScoringService = new();
    private readonly CoffeeLearningAiProviderScoringService _coffeeLearningAiProviderScoringService = new();
    private readonly ObservableCollection<MovieListItem> _movies = [];
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private MovieLibrary _currentLibrary = new();
    private Movie? _selectedMovie;
    private SubtitleTrack? _previewSubtitleTrack;
    private SubtitleCue? _currentPreviewCue;
    private string _subtitleTagHighlightColor = "#F6C945";
    private bool _showDualSubtitles;
    private bool _showLearningNotes;
    private string _englishSubtitleOverlayPosition = DefaultEnglishSubtitleOverlayPosition;
    private string _japaneseSubtitleOverlayPosition = DefaultJapaneseSubtitleOverlayPosition;
    private string _aiNoteOverlayPosition = DefaultAiNoteOverlayPosition;
    private string _userNoteOverlayPosition = DefaultUserNoteOverlayPosition;
    private TimeSpan _previewDuration = TimeSpan.Zero;
    private TimeSpan? _pendingPreviewSeek;
    private bool _isPreviewMediaOpened;
    private bool _playPreviewWhenMediaOpened;
    private bool _isPreviewPlaying;
    private bool _isPreviewSeeking;
    private TimeSpan? _previewStopAt;
    private TimeSpan _fullPreviewDuration = TimeSpan.Zero;
    private TimeSpan? _pendingFullPreviewSeek;
    private bool _isFullPreviewMediaOpened;
    private bool _playFullPreviewWhenMediaOpened;
    private bool _isFullPreviewPlaying;
    private bool _isFullPreviewSeeking;
    private bool _isUpdatingFullPreviewSlider;
    private bool _isSubtitleGenerationRunning;
    private bool _isUpdatingSelection;
    private bool _isUpdatingPreferences;
    private bool _isUpdatingPreviewSlider;
    private bool _isOpeningGlobalSceneRow;
    private FullPreviewPopupWindow? _previewPopupWindow;
    private string? _previewPopupVideoPath;
    private bool _previewPopupVideoAvailable;

    public MainWindow()
    {
        _paths = new CoffeeMoviePaths();
        _paths.EnsureCreated();
        _libraryStore = new MovieLibraryStore(_paths);

        InitializeComponent();
        InstallTagSelectorButtons();
        MoviesListBox.ItemsSource = _movies;
        ConfigureMovieShelfGrouping();
        _previewTimer.Tick += (_, _) =>
        {
            UpdatePreviewSeekFromPlayer();
            UpdateFullPreviewSeekFromPlayer();
            SyncPreviewPopupFromActiveSurface();
        };
        PreviewSeekSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnPreviewSeekDragStarted));
        PreviewSeekSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnPreviewSeekDragCompleted));
        PreviewSeekSlider.LostMouseCapture += OnPreviewSeekLostMouseCapture;
        FullPreviewSeekSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnFullPreviewSeekDragStarted));
        FullPreviewSeekSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnFullPreviewSeekDragCompleted));
        FullPreviewSeekSlider.LostMouseCapture += OnFullPreviewSeekLostMouseCapture;
        Loaded += async (_, _) => await RefreshMoviesAsync();
        Closed += OnMainWindowClosed;
        ResetPreviewSeek();
        ResetFullPreviewSeek();
        SetDetailsEnabled(false);
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        PreviewPlayer.Stop();
        PreviewPlayer.Source = null;
        FullPreviewPlayer.Stop();
        FullPreviewPlayer.Source = null;
        _previewPopupWindow?.Close();
        _previewPopupWindow = null;
        _previewPopupVideoPath = null;
        _previewPopupVideoAvailable = false;
    }

    private async Task<Movie> ImportVideoAsync(string sourcePath)
    {
        var movieId = MovieId.New();
        var sourceFileName = Path.GetFileName(sourcePath);
        var safeFileName = SanitizeFileName(sourceFileName);
        var movieDirectory = _paths.GetMovieVideoDirectory(movieId);
        Directory.CreateDirectory(movieDirectory);

        var targetPath = EnsureUniquePath(Path.Combine(movieDirectory, safeFileName));
        await using (var input = File.OpenRead(sourcePath))
        await using (var output = File.Create(targetPath))
        {
            await input.CopyToAsync(output);
        }

        var fileInfo = new FileInfo(targetPath);
        var metadata = MovieMetadataInferenceService.InferFromFileName(sourceFileName);
        var movie = new Movie
        {
            Id = movieId,
            Title = Path.GetFileNameWithoutExtension(sourceFileName),
            SeriesTitle = metadata.SeriesTitle,
            SeasonNumber = metadata.SeasonNumber,
            EpisodeNumber = metadata.EpisodeNumber,
            Video = new VideoAsset
            {
                SourceKind = VideoSourceKind.LocalFile,
                SourceUri = sourcePath,
                SourceKey = $"local:{movieId}",
                FileName = safeFileName,
                ContentType = GuessVideoContentType(safeFileName),
                SizeBytes = fileInfo.Length,
                ModifiedAt = fileInfo.LastWriteTimeUtc,
                CachePath = targetPath
            }
        };

        await _libraryStore.UpsertMovieAsync(movie);
        return movie;
    }

    private void DeleteCachedSubtitleFiles(string movieId, SubtitleTrack track)
    {
        DeleteCachedSubtitleFile(movieId, track.LocalPath);
        DeleteCachedSubtitleFile(movieId, track.VttCachePath);
    }

    private void DeleteCachedSubtitleFile(string movieId, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var subtitleDirectory = Path.GetFullPath(_paths.GetMovieSubtitleDirectory(movieId));
        if (!subtitleDirectory.EndsWith(Path.DirectorySeparatorChar))
        {
            subtitleDirectory += Path.DirectorySeparatorChar;
        }

        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(subtitleDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    private async Task<bool> RewriteSubtitleTrackFilesAsync(SubtitleTrack track, bool writeBackOriginal)
    {
        await WriteSubtitleFileAsync(track.LocalPath, track, FormatForPath(track.LocalPath, track.Format));

        if (!string.IsNullOrWhiteSpace(track.VttCachePath))
        {
            await WriteSubtitleFileAsync(track.VttCachePath, track, SubtitleFormat.WebVtt);
        }

        if (!writeBackOriginal
            || string.IsNullOrWhiteSpace(track.SourceUri)
            || !File.Exists(track.SourceUri)
            || (!string.IsNullOrWhiteSpace(track.LocalPath) && IsSamePath(track.SourceUri, track.LocalPath)))
        {
            return false;
        }

        await WriteSubtitleFileAsync(track.SourceUri, track, FormatForPath(track.SourceUri, track.Format));
        return true;
    }

    private static async Task WriteSubtitleFileAsync(string? path, SubtitleTrack track, SubtitleFormat format)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var content = SubtitleParser.ToSubtitleText(track.Cues, format);
        await File.WriteAllTextAsync(path, content, Encoding.UTF8);
    }

    private static SubtitleFormat FormatForPath(string? path, SubtitleFormat fallback)
    {
        var extension = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetExtension(path);
        if (extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase))
        {
            return SubtitleFormat.WebVtt;
        }

        if (extension.Equals(".srt", StringComparison.OrdinalIgnoreCase))
        {
            return SubtitleFormat.Srt;
        }

        return fallback == SubtitleFormat.Unknown ? SubtitleFormat.Srt : fallback;
    }

    private static bool IsSamePath(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static void RefreshMovieSceneMarkers(Movie movie)
    {
        var track = SelectDefaultSubtitleTrack(movie);
        movie.SceneMarkers = track is null
            ? []
            : SubtitleSceneFactory.CreateSceneMarkers(track, maxMarkers: 1000);
    }

    private static SubtitleTrack? SelectDefaultSubtitleTrack(Movie movie)
    {
        return movie.SubtitleTracks.FirstOrDefault(track =>
                track.Role == SubtitleTrackRole.LearningTarget && track.Cues.Count > 0)
            ?? movie.SubtitleTracks.LastOrDefault(track => track.Cues.Count > 0)
            ?? movie.SubtitleTracks.LastOrDefault();
    }

    private async Task RefreshMoviesAsync(string? selectedMovieId = null, bool forceReload = false)
    {
        var library = forceReload
            ? await _libraryStore.ReloadAsync()
            : await _libraryStore.LoadAsync();
        _currentLibrary = library;
        ApplyStudioPreferences(library);
        var movies = library.Movies
            .Where(MatchesMovieFilters)
            .OrderBy(movie => string.IsNullOrWhiteSpace(movie.SeriesTitle) ? movie.Title : movie.SeriesTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(movie => movie.SeasonNumber ?? int.MaxValue)
            .ThenBy(movie => movie.EpisodeNumber ?? int.MaxValue)
            .ThenByDescending(movie => movie.UpdatedAt)
            .ToList();

        _movies.Clear();
        foreach (var movie in movies)
        {
            _movies.Add(new MovieListItem(movie));
        }

        SummaryTextBlock.Text = $"{_movies.Count} / {library.Movies.Count} movies";

        var selectedItem = !string.IsNullOrWhiteSpace(selectedMovieId)
            ? _movies.FirstOrDefault(item => string.Equals(item.MovieId, selectedMovieId, StringComparison.Ordinal))
            : _movies.FirstOrDefault();

        MoviesListBox.SelectedItem = selectedItem;
        if (selectedItem is null)
        {
            _selectedMovie = null;
            RenderMovieDetails(null);
        }
        else
        {
            _selectedMovie = library.Movies.FirstOrDefault(movie => string.Equals(movie.Id, selectedItem.MovieId, StringComparison.Ordinal));
            RenderMovieDetails(_selectedMovie);
        }

        if (HasGlobalSubtitleTagFilter())
        {
            RenderGlobalSubtitleTagResults(library);
        }
    }

    private void RenderMovieDetails(Movie? movie)
    {
        _isUpdatingSelection = true;
        try
        {
            ResetPreviewIfMovieChanged(movie);
            SetDetailsEnabled(movie is not null);

            if (movie is null)
            {
                TitleTextBox.Text = string.Empty;
                SeriesTitleTextBox.Text = string.Empty;
                SeasonNumberTextBox.Text = string.Empty;
                EpisodeNumberTextBox.Text = string.Empty;
                MovieTagsButton.ToolTip = null;
                FileNameTextBlock.Text = "動画を追加してください";
                CachePathTextBlock.Text = string.Empty;
                SizeTextBlock.Text = string.Empty;
                UpdateSubtitleGenerationPanel(null);
                _previewSubtitleTrack = null;
                SubtitlesDataGrid.ItemsSource = null;
                RenderSceneRows(null);
                HidePreviewSubtitle();
                HideFullPreviewSubtitle();
                return;
            }

            TitleTextBox.Text = movie.Title;
            SeriesTitleTextBox.Text = movie.SeriesTitle ?? string.Empty;
            SeasonNumberTextBox.Text = movie.SeasonNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            EpisodeNumberTextBox.Text = movie.EpisodeNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            MovieTagsButton.ToolTip = movie.Tags.Count == 0 ? "\u30BF\u30B0\u672A\u9078\u629E" : string.Join(", ", movie.Tags);
            FileNameTextBlock.Text = movie.Video.FileName;
            CachePathTextBlock.Text = movie.Video.CachePath ?? movie.Video.SourceUri;
            SizeTextBlock.Text = $"{FormatBytes(movie.Video.SizeBytes)} / {movie.SubtitleTracks.Count} subtitle / {movie.SceneMarkers.Count} scene";
            UpdateSubtitleGenerationPanel(movie);
            var subtitleRows = movie.SubtitleTracks
                .Select(track => new SubtitleRow(track))
                .ToList();
            _previewSubtitleTrack = SelectDefaultSubtitleTrack(movie);
            SubtitlesDataGrid.ItemsSource = subtitleRows;
            SubtitlesDataGrid.SelectedItem = subtitleRows
                .FirstOrDefault(row => string.Equals(row.TrackId, _previewSubtitleTrack?.Id, StringComparison.Ordinal));
            RemoveSubtitleButton.IsEnabled = subtitleRows.Count > 0;
            RenderSceneRows(_previewSubtitleTrack);
            UpdatePreviewSubtitleAtCurrentPosition();
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void SetDetailsEnabled(bool enabled)
    {
        TitleTextBox.IsEnabled = enabled;
        SeriesTitleTextBox.IsEnabled = enabled;
        SeasonNumberTextBox.IsEnabled = enabled;
        EpisodeNumberTextBox.IsEnabled = enabled;
        MovieTagsButton.IsEnabled = enabled;
        AddSubtitleButton.IsEnabled = enabled;
        RemoveSubtitleButton.IsEnabled = enabled && SubtitlesDataGrid.SelectedItem is not null;
        WriteSidecarButton.IsEnabled = enabled;
        ExportDrivePackageButton.IsEnabled = enabled;
        DualSubtitleButton.IsEnabled = enabled;
        LearningNotesButton.IsEnabled = enabled;
        CoffeeLearningRegisterButton.IsEnabled = enabled;
        PlayButton.IsEnabled = enabled;
        PauseButton.IsEnabled = enabled;
        StopButton.IsEnabled = enabled;
        PreviewPopupButton.IsEnabled = enabled;
        CreateThumbnailButton.IsEnabled = enabled;
        PlayThumbnailClipButton.IsEnabled = enabled && _selectedMovie?.Video.ThumbnailTimestampSeconds is not null;
        FullPreviewPlayButton.IsEnabled = enabled;
        FullPreviewPauseButton.IsEnabled = enabled;
        FullPreviewStopButton.IsEnabled = enabled;
        FullPreviewLearningNotesButton.IsEnabled = enabled;
        FullPreviewCoffeeLearningRegisterButton.IsEnabled = enabled;
        if (!enabled)
        {
            PreviewSeekSlider.IsEnabled = false;
            FullPreviewSeekSlider.IsEnabled = false;
        }
        HighlightColorComboBox.IsEnabled = enabled;
        TimingShiftTextBox.IsEnabled = enabled;
        SyncPairedSubtitleCheckBox.IsEnabled = enabled;
        OriginalSubtitleWriteBackCheckBox.IsEnabled = enabled;
        FlaggedOnlyCheckBox.IsEnabled = enabled;
        SelectSelectedSceneTagsButton.IsEnabled = enabled;
        ClearSelectedSceneTagsButton.IsEnabled = enabled;
        PlayFlaggedButton.IsEnabled = enabled;
        EnglishSubtitlePositionComboBox.IsEnabled = enabled;
        JapaneseSubtitlePositionComboBox.IsEnabled = enabled;
        AiNotePositionComboBox.IsEnabled = enabled;
        UserNotePositionComboBox.IsEnabled = enabled;
        ResetOverlayLayoutButton.IsEnabled = enabled;
        SubtitlesDataGrid.IsEnabled = enabled;
        ScenesDataGrid.IsEnabled = enabled;
        SetSubtitleGenerationEnabled(enabled && !_isSubtitleGenerationRunning);
    }

    private void SetSubtitleGenerationEnabled(bool enabled)
    {
        WhisperPythonCommandTextBox.IsEnabled = enabled;
        WhisperPythonArgumentsTextBox.IsEnabled = enabled;
        WhisperOutputDirectoryTextBox.IsEnabled = enabled;
        WhisperModelTextBox.IsEnabled = enabled;
        WhisperLanguageTextBox.IsEnabled = enabled;
        WhisperDeviceComboBox.IsEnabled = enabled;
        WhisperComputeTypeComboBox.IsEnabled = enabled;
        EnglishSubtitleGenerationModeComboBox.IsEnabled = enabled;
        TranslationCommandTextBox.IsEnabled = enabled;
        TranslationArgumentsTextBox.IsEnabled = enabled;
        TranslationSourceLanguageTextBox.IsEnabled = enabled;
        TranslationTargetLanguageTextBox.IsEnabled = enabled;
        TranslationModelTextBox.IsEnabled = enabled;
        TranslationPromptTextBox.IsEnabled = enabled;
        ResetTranslationPromptButton.IsEnabled = enabled;
        LearningNotesAudienceLevelComboBox.IsEnabled = enabled;
        LearningNotesPromptTextBox.IsEnabled = enabled;
        ResetLearningNotesPromptButton.IsEnabled = enabled;
        OverwriteGeneratedSubtitleCheckBox.IsEnabled = enabled;
        OverwriteJapaneseSubtitleCheckBox.IsEnabled = enabled;
        BrowseWhisperOutputDirectoryButton.IsEnabled = enabled;
        SaveWhisperDefaultsButton.IsEnabled = enabled;
        GenerateEnglishSubtitleButton.IsEnabled = enabled;
        GenerateJapaneseSubtitleButton.IsEnabled = enabled;
        GenerateEnglishAndJapaneseSubtitleButton.IsEnabled = enabled;
        GenerateAiNotesButton.IsEnabled = enabled;
    }

    private void UpdateSubtitleGenerationPanel(Movie? movie)
    {
        if (movie is null)
        {
            GenerationMovieTextBlock.Text = "動画を選択してください";
            SetSubtitleGenerationState("待機中");
            return;
        }

        GenerationMovieTextBlock.Text = movie.Title;
        if (!_isSubtitleGenerationRunning)
        {
            SetSubtitleGenerationState("待機中");
        }

        if (string.IsNullOrWhiteSpace(WhisperOutputDirectoryTextBox.Text))
        {
            WhisperOutputDirectoryTextBox.Text = GetDefaultSubtitleGenerationDirectory(movie);
        }
    }

    private void SetSubtitleGenerationState(string message)
    {
        SubtitleGenerationStateTextBlock.Text = message;
    }

    private void SetStatus(string message, bool hideProgress = true)
    {
        StatusTextBlock.Text = message;
        if (hideProgress)
        {
            HideStatusProgress();
        }
    }

    private void HideStatusProgress()
    {
        StatusProgressBar.Value = 0;
        StatusProgressTextBlock.Text = "0%";
        StatusProgressBar.Visibility = Visibility.Collapsed;
        StatusProgressTextBlock.Visibility = Visibility.Collapsed;
    }

    private void ApplyStudioPreferences(MovieLibrary library)
    {
        _isUpdatingPreferences = true;
        try
        {
            _subtitleTagHighlightColor = string.IsNullOrWhiteSpace(library.Studio.SubtitleTagHighlightColor)
                ? "#F6C945"
                : library.Studio.SubtitleTagHighlightColor;
            _showDualSubtitles = library.Studio.ShowDualSubtitles;
            _showLearningNotes = library.Studio.ShowLearningNotes;
            _englishSubtitleOverlayPosition = NormalizeOverlayPosition(
                library.Studio.EnglishSubtitleOverlayPosition,
                DefaultEnglishSubtitleOverlayPosition);
            _japaneseSubtitleOverlayPosition = NormalizeOverlayPosition(
                library.Studio.JapaneseSubtitleOverlayPosition,
                DefaultJapaneseSubtitleOverlayPosition);
            _aiNoteOverlayPosition = NormalizeOverlayPosition(
                library.Studio.AiNoteOverlayPosition,
                DefaultAiNoteOverlayPosition);
            _userNoteOverlayPosition = NormalizeOverlayPosition(
                library.Studio.UserNoteOverlayPosition,
                DefaultUserNoteOverlayPosition);
            HighlightColorComboBox.SelectedValue = _subtitleTagHighlightColor;
            ApplyOverlayPositionComboBoxes();
            WhisperOutputDirectoryTextBox.Text = library.Studio.WhisperOutputDirectory ?? string.Empty;
            WhisperPythonCommandTextBox.Text = string.IsNullOrWhiteSpace(library.Studio.WhisperPythonCommand)
                ? "py"
                : library.Studio.WhisperPythonCommand;
            WhisperPythonArgumentsTextBox.Text = string.IsNullOrWhiteSpace(library.Studio.WhisperPythonArguments)
                ? "-3.10 -m whisperx"
                : library.Studio.WhisperPythonArguments;
            WhisperModelTextBox.Text = string.IsNullOrWhiteSpace(library.Studio.WhisperModel)
                ? "medium"
                : library.Studio.WhisperModel;
            WhisperLanguageTextBox.Text = string.IsNullOrWhiteSpace(library.Studio.WhisperLanguage)
                ? "en"
                : library.Studio.WhisperLanguage;
            SelectComboBoxItem(WhisperDeviceComboBox, library.Studio.WhisperDevice, "cuda");
            SelectComboBoxItem(WhisperComputeTypeComboBox, library.Studio.WhisperComputeType, "float16");
            SelectComboBoxValue(
                EnglishSubtitleGenerationModeComboBox,
                library.Studio.EnglishSubtitleGenerationMode,
                "normal");
            TranslationCommandTextBox.Text = string.IsNullOrWhiteSpace(library.Studio.TranslationCommand)
                ? DefaultTranslationCommand
                : library.Studio.TranslationCommand;
            TranslationModelTextBox.Text = string.IsNullOrWhiteSpace(library.Studio.TranslationModel)
                ? DefaultCodexSparkModel
                : library.Studio.TranslationModel;
            TranslationArgumentsTextBox.Text = string.IsNullOrWhiteSpace(library.Studio.TranslationArguments)
                ? DefaultTranslationArguments
                : library.Studio.TranslationArguments;
            if (string.Equals(TranslationCommandTextBox.Text, DefaultTranslationCommand, StringComparison.OrdinalIgnoreCase)
                && (TranslationArgumentsTextBox.Text.TrimStart().StartsWith("--input", StringComparison.OrdinalIgnoreCase)
                    || TranslationArgumentsTextBox.Text.Contains("{notesOutput}", StringComparison.OrdinalIgnoreCase)))
            {
                TranslationArgumentsTextBox.Text = DefaultTranslationArguments;
            }

            TranslationSourceLanguageTextBox.Text = string.IsNullOrWhiteSpace(library.Studio.TranslationSourceLanguage)
                ? "en"
                : library.Studio.TranslationSourceLanguage;
            TranslationTargetLanguageTextBox.Text = string.IsNullOrWhiteSpace(library.Studio.TranslationTargetLanguage)
                ? "ja"
                : library.Studio.TranslationTargetLanguage;
            TranslationPromptTextBox.Text = string.IsNullOrWhiteSpace(library.Studio.TranslationPrompt)
                ? DefaultTranslationPrompt
                : library.Studio.TranslationPrompt;
            var learningNotesPrompt = NormalizeOptionalText(library.Studio.LearningNotesPrompt);
            LearningNotesPromptTextBox.Text = learningNotesPrompt is null || IsLegacyLearningNotesPrompt(learningNotesPrompt)
                ? DefaultLearningNotesPrompt
                : learningNotesPrompt;
            SelectComboBoxItem(
                LearningNotesAudienceLevelComboBox,
                library.Studio.LearningNotesAudienceLevel,
                DefaultLearningNotesAudienceLevel);
            UpdateDualSubtitleButton();
            UpdateLearningNotesButton();
        }
        finally
        {
            _isUpdatingPreferences = false;
        }
    }

    private async Task SaveStudioPreferencesAsync()
    {
        var library = await _libraryStore.LoadAsync();
        library.Studio.SubtitleTagHighlightColor = _subtitleTagHighlightColor;
        library.Studio.ShowDualSubtitles = _showDualSubtitles;
        library.Studio.ShowLearningNotes = _showLearningNotes;
        library.Studio.EnglishSubtitleOverlayPosition = _englishSubtitleOverlayPosition;
        library.Studio.JapaneseSubtitleOverlayPosition = _japaneseSubtitleOverlayPosition;
        library.Studio.AiNoteOverlayPosition = _aiNoteOverlayPosition;
        library.Studio.UserNoteOverlayPosition = _userNoteOverlayPosition;
        library.Studio.WhisperOutputDirectory = NormalizeOptionalText(WhisperOutputDirectoryTextBox.Text);
        library.Studio.WhisperPythonCommand = NormalizeOptionalText(WhisperPythonCommandTextBox.Text) ?? "py";
        library.Studio.WhisperPythonArguments = NormalizeOptionalText(WhisperPythonArgumentsTextBox.Text) ?? "-3.10 -m whisperx";
        library.Studio.WhisperModel = NormalizeOptionalText(WhisperModelTextBox.Text) ?? "medium";
        library.Studio.WhisperLanguage = NormalizeOptionalText(WhisperLanguageTextBox.Text) ?? "en";
        library.Studio.WhisperDevice = SelectedComboText(WhisperDeviceComboBox, "cuda");
        library.Studio.WhisperComputeType = SelectedComboText(WhisperComputeTypeComboBox, "float16");
        library.Studio.EnglishSubtitleGenerationMode = SelectedComboValue(
            EnglishSubtitleGenerationModeComboBox,
            "normal");
        library.Studio.TranslationCommand = NormalizeOptionalText(TranslationCommandTextBox.Text) ?? DefaultTranslationCommand;
        library.Studio.TranslationModel = NormalizeOptionalText(TranslationModelTextBox.Text) ?? DefaultCodexSparkModel;
        library.Studio.TranslationArguments = NormalizeOptionalText(TranslationArgumentsTextBox.Text)
            ?? DefaultTranslationArguments;
        library.Studio.TranslationSourceLanguage = NormalizeOptionalText(TranslationSourceLanguageTextBox.Text) ?? "en";
        library.Studio.TranslationTargetLanguage = NormalizeOptionalText(TranslationTargetLanguageTextBox.Text) ?? "ja";
        var translationPrompt = NormalizeOptionalText(TranslationPromptTextBox.Text);
        library.Studio.TranslationPrompt = string.Equals(translationPrompt, DefaultTranslationPrompt, StringComparison.Ordinal)
            ? null
            : translationPrompt;
        var learningNotesPrompt = NormalizeOptionalText(LearningNotesPromptTextBox.Text);
        library.Studio.LearningNotesPrompt = string.Equals(learningNotesPrompt, DefaultLearningNotesPrompt, StringComparison.Ordinal)
            ? null
            : learningNotesPrompt;
        library.Studio.LearningNotesAudienceLevel = SelectedComboText(
            LearningNotesAudienceLevelComboBox,
            DefaultLearningNotesAudienceLevel);
        await _libraryStore.SaveAsync(library);
    }

    private void UpdateDualSubtitleButton()
    {
        DualSubtitleButton.Content = _showDualSubtitles ? "両方表示: ON" : "両方表示: OFF";
        DualSubtitleButton.Background = _showDualSubtitles
            ? FindResource("AccentBrush") as System.Windows.Media.Brush
            : new SolidColorBrush(Color.FromRgb(0x12, 0x1A, 0x26));
        DualSubtitleButton.Foreground = _showDualSubtitles
            ? new SolidColorBrush(Color.FromRgb(0x04, 0x10, 0x0F))
            : Brushes.White;
    }

    private void UpdateLearningNotesButton()
    {
        var content = _showLearningNotes ? "メモ表示: ON" : "メモ表示: OFF";
        var background = _showLearningNotes
            ? FindResource("AccentBrush") as System.Windows.Media.Brush
            : new SolidColorBrush(Color.FromRgb(0x12, 0x1A, 0x26));
        var foreground = _showLearningNotes
            ? new SolidColorBrush(Color.FromRgb(0x04, 0x10, 0x0F))
            : Brushes.White;

        LearningNotesButton.Content = content;
        LearningNotesButton.Background = background;
        LearningNotesButton.Foreground = foreground;
        FullPreviewLearningNotesButton.Content = content;
        FullPreviewLearningNotesButton.Background = background;
        FullPreviewLearningNotesButton.Foreground = foreground;
    }

    private void ShowError(string title, Exception exception)
    {
        SetStatus(exception.Message);
        MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static System.Windows.Media.Brush CreateBrush(string colorText, byte alpha = 0xFF)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(colorText);
            color.A = alpha;
            return new SolidColorBrush(color);
        }
        catch (FormatException)
        {
            return new SolidColorBrush(Color.FromArgb(alpha, 0xF6, 0xC9, 0x45));
        }
    }

    private static string? NormalizeOptionalText(string? text)
    {
        var normalized = text?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool TryParseOptionalPositiveInt(string? text, out int? value)
    {
        value = null;
        var normalized = NormalizeOptionalText(text);
        if (normalized is null)
        {
            return true;
        }

        if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 1)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool ContainsText(string? value, string search)
    {
        return value?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private string ResolveGenerationVideoPath(Movie movie)
    {
        if (IsUsableVideoInputPath(movie.Video.CachePath))
        {
            return movie.Video.CachePath!;
        }

        if (IsUsableVideoInputPath(movie.Video.SourceUri))
        {
            return movie.Video.SourceUri!;
        }

        var packagePath = IsReaderPackagePath(movie.Video.SourceUri)
            ? movie.Video.SourceUri
            : null;
        if (!string.IsNullOrWhiteSpace(packagePath) && File.Exists(packagePath))
        {
            throw new FileNotFoundException(
                "Drive package is available, but the extracted video cache is missing. Import the package again before creating thumbnails or generating subtitles.",
                movie.Video.FileName);
        }

        throw new FileNotFoundException("Video file for thumbnail/subtitle generation was not found.", movie.Video.FileName);
    }

    private static bool IsUsableVideoInputPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && File.Exists(path)
            && !IsReaderPackagePath(path);
    }

    private static bool IsReaderPackagePath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && CoffeeMoviePackageService.IsReaderPackageFileName(Path.GetFileName(path));
    }
    private static string EnsureUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, $"{name}-{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"保存先ファイル名を決定できませんでした: {path}");
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(fileName.Length);
        foreach (var character in fileName)
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        var sanitized = builder.ToString().Trim();
        return sanitized.Length == 0 ? "movie" : sanitized;
    }

    private static string GuessVideoContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".m4v" => "video/x-m4v",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            _ => "video/mp4"
        };
    }

    private static IEnumerable<string> GetDroppedFilePaths(System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return [];
        }

        return e.Data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths.Where(File.Exists)
            : [];
    }

    private static bool IsVideoFile(string path)
    {
        return VideoExtensions.Contains(Path.GetExtension(path));
    }

    private static bool IsSubtitleFile(string path)
    {
        return SubtitleExtensions.Contains(Path.GetExtension(path));
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private static string FormatTimestamp(TimeSpan value)
    {
        return value.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{value.Minutes:00}:{value.Seconds:00}");
    }

    private static string FormatCueEditTimestamp(TimeSpan value)
    {
        return value.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}")
            : string.Create(CultureInfo.InvariantCulture, $"{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}");
    }

    private static bool TryParseCueTimestamp(string? value, out TimeSpan timestamp)
    {
        timestamp = TimeSpan.Zero;
        var normalized = value?.Trim().Replace(',', '.');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var parts = normalized.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        var secondsText = parts[^1];
        if (!double.TryParse(secondsText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        var minutes = 0;
        var hours = 0;
        if (parts.Length >= 2 && !int.TryParse(parts[^2], NumberStyles.None, CultureInfo.InvariantCulture, out minutes))
        {
            return false;
        }

        if (parts.Length == 3 && !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hours))
        {
            return false;
        }

        if (hours < 0 || minutes < 0 || seconds < 0)
        {
            return false;
        }

        timestamp = TimeSpan.FromHours(hours)
            + TimeSpan.FromMinutes(minutes)
            + TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static string FormatPlaybackPosition(TimeSpan position, TimeSpan duration)
    {
        return $"{FormatTimestamp(position)} / {FormatTimestamp(duration)}";
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{elapsed.Minutes:00}:{elapsed.Seconds:00}");
    }

    private static string CollapseWhitespace(string value)
    {
        return string.Join(' ', value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizePreviewSubtitleText(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }
}
