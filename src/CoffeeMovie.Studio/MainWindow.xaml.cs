using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Models;
using CoffeeMovie.Storage.Services;
using Microsoft.Win32;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
    private const string FlagTagName = "flag";
    private const string DefaultTranslationCommand = "codex-spark";
    private const string DefaultCodexSparkModel = "gpt-5.3-codex-spark";
    private const string DefaultEnglishSubtitleOverlayPosition = "below2";
    private const string DefaultJapaneseSubtitleOverlayPosition = "below1";
    private const string DefaultAiNoteOverlayPosition = "above1";
    private const string DefaultUserNoteOverlayPosition = "above2";
    private const string DefaultLearningNotesAudienceLevel = "B1";
    private const string DefaultTranslationArguments = "exec --full-auto -C \"{outputDir}\" --add-dir \"{inputDir}\" --skip-git-repo-check \"You are codex-spark for CoffeeMovie. Read the prompt file at {promptFile}, translate {input}, and write the Japanese SRT to {output}.\"";
    private const string DefaultLearningNotesArguments = "exec --full-auto -C \"{outputDir}\" --add-dir \"{inputDir}\" --skip-git-repo-check \"You are codex-spark for CoffeeMovie. Read the prompt file at {promptFile}, analyze {input}, and write learning notes JSON to {notesOutput}.\"";
    private const string DefaultTranslationPrompt = """
あなたはアニメ英語字幕を日本語字幕へ翻訳する専門エージェントです。

目的:
- 英語SRT `{input}` を読み、自然で作品世界に合う日本語SRTを `{output}` に書き出してください。
- source language: `{source}`
- target language: `{target}`
- movie title: `{title}`

絶対条件:
- SRTの番号、順番、開始時刻、終了時刻を変更しないでください。
- キュー数を増減しないでください。
- 出力は有効なSRT本文だけにしてください。Markdown、説明、注釈、コードブロックは禁止です。
- タイムスタンプは入力SRTのものをそのまま使ってください。
- 英語本文だけを日本語へ置き換えてください。
- `{input}` と `{output}` が相対パスの場合は現在の作業フォルダ基準で扱い、絶対パスへ変換しないでください。

翻訳方針:
- 機械的な直訳ではなく、日本の視聴者が字幕として自然に読める表現へ意訳してください。
- 作品の世界観を推定してください。ファンタジー、現代劇、SFなどに合う語彙を選んでください。
- キャラクターの口調を文脈から判断し、セリフへ反映してください。
- 英語のイディオム、スラング、皮肉、冗談は直訳せず、意味と感情が伝わる日本語へ変換してください。
- 固有名詞、魔法名、地名、組織名は一貫させてください。判断できない場合は自然なカタカナまたは原語維持を選んでください。
- 字幕として読みやすい長さを意識し、冗長な説明を避けてください。

品質基準:
- 会話として自然であること。
- キャラクターの距離感、年齢感、敬語/ため口が破綻しないこと。
- 原文の意味を削りすぎず、字幕としてテンポよく読めること。
- 各キューの日本語が、そのタイムスタンプ内で読める長さであること。
""";
    private const string DefaultLearningNotesPrompt = """
あなたは英語学習者向けにアニメ英語字幕を分析する専門エージェントです。

目的:
- 英語SRT `{input}` を読み、対象レベル `{audienceLevel}` の学習者に本当に役立つ学習メモを `{notesOutput}` にJSONで書き出してください。
- movie title: `{title}`
- source language: `{source}`
- audience level: `{audienceLevel}`

出力形式:
- `{notesOutput}` には有効なUTF-8 JSONだけを書いてください。Markdown、説明、コードブロックは禁止です。
- ルートは配列にしてください。
- 入力SRTの各キューにつき1オブジェクトを出力してください。
- `index` はSRT番号と同じ整数にしてください。
- `cefr` は `A1`, `A2`, `B1`, `B2`, `C1`, `C2` のいずれかにしてください。
- `note` は必ず文字列で埋めてください。重要でないキューは `CEFR {level}: コメント不要（対象者レベル以下の通常表現）` のように短く書いてください。
- 重要なキューの `note` は日本語で100文字以内にしてください。

JSONスキーマ:
[
  {
    "index": 1,
    "cefr": "B1",
    "note": "CEFR B1: 語彙 'dissipate'=魔力が散る。魔法説明で再登場しやすい世界観語。"
  },
  {
    "index": 2,
    "cefr": "A1",
    "note": "CEFR A1: コメント不要（対象者レベル以下の通常表現）"
  }
]

分析方針:
- 学習者の対象レベルは `{audienceLevel}` です。対象レベル未満の普通の挨拶、短い相づち、固有名詞だけの行はコメント不要マーカーにしてください。
- 対象レベル以上の語彙・構文・慣用句・口語、または対象レベル未満でも作品理解や今後の読解に効く特殊表現があるキューだけ実質的なnoteを書いてください。
- 世界観ならではの語彙、魔法/戦闘/宗教/旅/師弟関係などのジャンル語、キャラクターの口調が出る言い回しを優先してください。
- `Hello`, `Yes`, `Okay`, `No`, `Apple` のような基礎語や一語返答は、特殊なニュアンスがない限り解説しないでください。
- noteの先頭に `CEFR {level}:` を含め、続けて `語彙:`, `構文:`, `慣用句:`, `口語:`, `世界観:` など要点が分かるラベルを入れてください。
- 各noteは必ずそのキュー固有の内容にしてください。汎用テンプレートの反復は禁止です。
- `$k`, `{word}`, `{level}`, `など抽象語`, `基本表現` のような未展開テンプレートや曖昧な定型句をnoteに書かないでください。
- コメント不要ではないnoteには、そのキュー本文に実在する英単語または英語フレーズを1つ引用してください。別キューの語句を書かないでください。
- B1以上に上げている語彙、文脈、構文、慣用句がある場合は、該当する英単語や英語フレーズを必ず明記してください。
- CEFRとは別に、よく使うスラング、口語表現、慣用句があれば `スラング:`、`口語:`、`慣用句:` のように示してください。
- ローマ字歌詞、固有名詞だけ、英語学習対象外に近いキューは原則コメント不要マーカーにしてください。
- 実質的なnoteは全体の15%-35%を目安にしてください。密度が高い場面でも半分以上のキューに実質noteを書かないでください。
- 同じnote文を大量に使い回さないでください。似たキューでも、該当語句と理由を変えてください。
- 推測用のスクリプトや簡易分類で作らず、SRT本文を読んで各キューを個別に判断してください。
- セリフ本文、番号、時刻は変更しないでください。字幕ファイルを書き換えないでください。
- `{input}` と `{notesOutput}` が相対パスの場合は現在の作業フォルダ基準で扱い、絶対パスへ変換しないでください。
""";

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".m4v",
        ".mov",
        ".webm",
        ".mkv",
        ".avi"
    };

    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt",
        ".vtt"
    };

    private readonly CoffeeMoviePaths _paths;
    private readonly MovieLibraryStore _libraryStore;
    private readonly CoffeeMoviePackageService _packageService = new();
    private readonly ObservableCollection<MovieListItem> _movies = [];
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
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

    public MainWindow()
    {
        _paths = new CoffeeMoviePaths();
        _paths.EnsureCreated();
        _libraryStore = new MovieLibraryStore(_paths);

        InitializeComponent();
        MoviesListBox.ItemsSource = _movies;
        _previewTimer.Tick += (_, _) =>
        {
            UpdatePreviewSeekFromPlayer();
            UpdateFullPreviewSeekFromPlayer();
        };
        PreviewSeekSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnPreviewSeekDragStarted));
        PreviewSeekSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnPreviewSeekDragCompleted));
        PreviewSeekSlider.LostMouseCapture += OnPreviewSeekLostMouseCapture;
        FullPreviewSeekSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnFullPreviewSeekDragStarted));
        FullPreviewSeekSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnFullPreviewSeekDragCompleted));
        FullPreviewSeekSlider.LostMouseCapture += OnFullPreviewSeekLostMouseCapture;
        Loaded += async (_, _) => await RefreshMoviesAsync();
        ResetPreviewSeek();
        ResetFullPreviewSeek();
        SetDetailsEnabled(false);
    }

    private async void OnImportVideoClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "動画ファイルを選択",
            Filter = "Video files|*.mp4;*.m4v;*.mov;*.webm;*.mkv;*.avi|All files|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var movie = await ImportVideoAsync(dialog.FileName);
            await RefreshMoviesAsync(movie.Id);
            SetStatus($"動画を追加しました: {movie.Title}");
        }
        catch (Exception ex)
        {
            ShowError("動画の取り込みに失敗しました", ex);
        }
    }

    private async void OnImportSubtitleClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "字幕ファイルを選択",
            Filter = "Subtitle files|*.srt;*.vtt|All files|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var track = await ImportSubtitleAsync(_selectedMovie, dialog.FileName);
            await RefreshMoviesAsync(_selectedMovie.Id);
            SetStatus($"字幕を追加しました: {track.Label} ({track.CueCount} cues)");
        }
        catch (Exception ex)
        {
            ShowError("字幕の取り込みに失敗しました", ex);
        }
    }

    private async void OnRemoveSubtitleClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie is null || SubtitlesDataGrid.SelectedItem is not SubtitleRow row)
        {
            SetStatus("削除する字幕を選択してください。");
            return;
        }

        var track = _selectedMovie.SubtitleTracks
            .FirstOrDefault(candidate => string.Equals(candidate.Id, row.TrackId, StringComparison.Ordinal));
        if (track is null)
        {
            SetStatus("削除する字幕が見つかりませんでした。");
            return;
        }

        var result = MessageBox.Show(
            this,
            $"「{track.Label}」をこの動画から削除しますか？",
            "字幕を削除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _selectedMovie.SubtitleTracks.Remove(track);
            DeleteCachedSubtitleFiles(_selectedMovie.Id, track);
            RefreshMovieSceneMarkers(_selectedMovie);
            await _libraryStore.UpsertMovieAsync(_selectedMovie);
            await RefreshMoviesAsync(_selectedMovie.Id);
            SetStatus($"字幕を削除しました: {track.Label}");
        }
        catch (Exception ex)
        {
            ShowError("字幕の削除に失敗しました", ex);
        }
    }

    private async void OnWriteSidecarClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie is null)
        {
            return;
        }

        var defaultFileName = Path.GetFileNameWithoutExtension(_selectedMovie.Video.FileName) + ".coffeemovie.json";
        var dialog = new SaveFileDialog
        {
            Title = "サイドカーを書き出し",
            FileName = defaultFileName,
            Filter = "CoffeeMovie sidecar|*.coffeemovie.json|JSON|*.json|All files|*.*"
        };

        if (!string.IsNullOrWhiteSpace(_selectedMovie.Video.CachePath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_selectedMovie.Video.CachePath);
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await CoffeeMovieSidecarService.WriteAsync(_selectedMovie, dialog.FileName);
            SetStatus($"サイドカーを書き出しました: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            ShowError("サイドカーの書き出しに失敗しました", ex);
        }
    }

    private async void OnExportDrivePackageClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie is null)
        {
            return;
        }

        try
        {
            var driveRootPath = await GetOrChooseDriveRootPathAsync();
            if (string.IsNullOrWhiteSpace(driveRootPath))
            {
                return;
            }

            SetStatus("スマホ用パッケージを書き出しています...");
            ExportDrivePackageButton.IsEnabled = false;
            var progress = new Progress<CoffeeMoviePackageExportProgress>(SetPackageExportProgress);
            var result = await _packageService.ExportReaderPackageAsync(_selectedMovie, driveRootPath, progress);
            if (result.Skipped)
            {
                SetStatus(
                    $"差分なしのため書き出しをスキップしました: {Path.GetFileName(result.PackagePath)}",
                    hideProgress: false);
                return;
            }

            SetStatus(
                $"スマホ用パッケージを書き出しました: {Path.GetFileName(result.PackagePath)} / {Path.GetFileName(result.SidecarPath)}",
                hideProgress: false);
        }
        catch (Exception ex)
        {
            ShowError("スマホ用パッケージの書き出しに失敗しました", ex);
        }
        finally
        {
            ExportDrivePackageButton.IsEnabled = _selectedMovie is not null;
        }
    }

    private async void OnConfigureDriveSyncFolderClicked(object sender, RoutedEventArgs e)
    {
        var path = await GetOrChooseDriveRootPathAsync(forceChoose: true);
        if (!string.IsNullOrWhiteSpace(path))
        {
            SetStatus($"Drive同期フォルダを設定しました: {path}");
        }
    }

    private async Task<string?> GetOrChooseDriveRootPathAsync(bool forceChoose = false)
    {
        var library = await _libraryStore.LoadAsync();
        var driveRootPath = library.Studio.GoogleDriveRootPath;
        if (!forceChoose && !string.IsNullOrWhiteSpace(driveRootPath) && Directory.Exists(driveRootPath))
        {
            return driveRootPath;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Google Drive 同期先フォルダを選択",
            Multiselect = false
        };
        if (!string.IsNullOrWhiteSpace(driveRootPath) && Directory.Exists(driveRootPath))
        {
            dialog.InitialDirectory = driveRootPath;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return null;
        }

        driveRootPath = dialog.FolderName;
        Directory.CreateDirectory(driveRootPath);
        library.Studio.GoogleDriveRootPath = driveRootPath;
        await _libraryStore.SaveAsync(library);
        return driveRootPath;
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await RefreshMoviesAsync(_selectedMovie?.Id);
    }

    private async void OnGenerateEnglishSubtitleClicked(object sender, RoutedEventArgs e)
    {
        await RunSubtitleGenerationJobAsync(
            "WhisperX subtitle generation started.",
            async movie => [await GenerateEnglishSubtitleAsync(movie)],
            "英語字幕を生成して取り込みました",
            "英語字幕の生成に失敗しました");
    }

    private async void OnGenerateJapaneseSubtitleClicked(object sender, RoutedEventArgs e)
    {
        await RunSubtitleGenerationJobAsync(
            "Japanese subtitle translation started.",
            async movie => [await GenerateJapaneseSubtitleAsync(movie)],
            "日本語訳字幕を生成して取り込みました",
            "日本語訳字幕の生成に失敗しました");
    }

    private async void OnGenerateEnglishAndJapaneseSubtitleClicked(object sender, RoutedEventArgs e)
    {
        await RunSubtitleGenerationJobAsync(
            "English subtitle generation and Japanese translation started.",
            async movie =>
            {
                var englishPath = await GenerateEnglishSubtitleAsync(movie);
                var japanesePath = await GenerateJapaneseSubtitleAsync(movie, englishPath);
                return [englishPath, japanesePath];
            },
            "英語字幕と日本語訳字幕を生成して取り込みました",
            "英日字幕の生成に失敗しました");
    }

    private async void OnGenerateAiNotesClicked(object sender, RoutedEventArgs e)
    {
        await RunLearningNotesGenerationJobAsync();
    }

    private async void OnToggleLearningNotesClicked(object sender, RoutedEventArgs e)
    {
        _showLearningNotes = !_showLearningNotes;
        UpdateLearningNotesButton();
        UpdatePreviewSubtitleAtCurrentPosition();
        UpdateFullPreviewSubtitle(FullPreviewPlayer.Position);
        await SaveStudioPreferencesAsync();
    }

    private async void OnOverlayPositionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingPreferences)
        {
            return;
        }

        ReadOverlayPositionComboBoxes();
        UpdatePreviewSubtitleAtCurrentPosition();
        UpdateFullPreviewSubtitle(FullPreviewPlayer.Position);
        await SaveStudioPreferencesAsync();
    }

    private async void OnResetOverlayLayoutClicked(object sender, RoutedEventArgs e)
    {
        SetDefaultOverlayPositions();
        _isUpdatingPreferences = true;
        try
        {
            ApplyOverlayPositionComboBoxes();
        }
        finally
        {
            _isUpdatingPreferences = false;
        }

        UpdatePreviewSubtitleAtCurrentPosition();
        UpdateFullPreviewSubtitle(FullPreviewPlayer.Position);
        await SaveStudioPreferencesAsync();
        SetStatus("表示位置を既定に戻しました。");
    }

    private async Task RunSubtitleGenerationJobAsync(
        string startMessage,
        Func<Movie, Task<IReadOnlyList<string>>> generateSubtitlePathsAsync,
        string successMessage,
        string errorTitle)
    {
        if (_isSubtitleGenerationRunning)
        {
            return;
        }

        if (_selectedMovie is null)
        {
            SetStatus("字幕を生成する動画を選択してください。");
            return;
        }

        try
        {
            _isSubtitleGenerationRunning = true;
            SetSubtitleGenerationEnabled(false);
            SubtitleGenerationLogTextBox.Clear();
            var startedAt = DateTimeOffset.Now;
            SetSubtitleGenerationState("実行中");
            SetStatus("字幕生成を実行中です。");
            AppendSubtitleGenerationLog(startMessage);
            AppendSubtitleGenerationLog("RUNNING: external subtitle job is active.");

            var generatedPaths = await generateSubtitlePathsAsync(_selectedMovie);
            var importedCueCount = 0;
            foreach (var generatedPath in generatedPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                AppendSubtitleGenerationLog($"Importing generated subtitle: {generatedPath}");
                var track = await ImportSubtitleAsync(_selectedMovie, generatedPath);
                importedCueCount += track.CueCount;
                AppendSubtitleGenerationLog($"Imported {track.Label}: {track.CueCount} cues.");
            }

            await RefreshMoviesAsync(_selectedMovie.Id);
            var elapsed = DateTimeOffset.Now - startedAt;
            SetSubtitleGenerationState($"完了 ({FormatElapsed(elapsed)})");
            SetStatus($"{successMessage}: {importedCueCount} cues");
            AppendSubtitleGenerationLog($"COMPLETED: {importedCueCount} cues imported in {FormatElapsed(elapsed)}.");
        }
        catch (Exception ex)
        {
            SetSubtitleGenerationState("失敗");
            ShowError(errorTitle, ex);
            AppendSubtitleGenerationLog("ERROR: " + ex.Message);
        }
        finally
        {
            _isSubtitleGenerationRunning = false;
            SetSubtitleGenerationEnabled(_selectedMovie is not null);
        }
    }

    private async Task RunLearningNotesGenerationJobAsync()
    {
        if (_isSubtitleGenerationRunning)
        {
            return;
        }

        if (_selectedMovie is null)
        {
            SetStatus("AIメモを追加する動画を選択してください。");
            return;
        }

        try
        {
            _isSubtitleGenerationRunning = true;
            SetSubtitleGenerationEnabled(false);
            SubtitleGenerationLogTextBox.Clear();
            var startedAt = DateTimeOffset.Now;
            SetSubtitleGenerationState("AIメモ実行中");
            SetStatus("AIメモを生成中です。");
            AppendSubtitleGenerationLog("AI learning note generation started.");
            AppendSubtitleGenerationLog("RUNNING: external AI note job is active.");

            var importedNoteCount = await GenerateLearningNotesAsync(_selectedMovie);
            await RefreshMoviesAsync(_selectedMovie.Id);
            var elapsed = DateTimeOffset.Now - startedAt;
            SetSubtitleGenerationState($"完了 ({FormatElapsed(elapsed)})");
            SetStatus($"AIメモを追加しました: {importedNoteCount} cues");
            AppendSubtitleGenerationLog($"COMPLETED: {importedNoteCount} AI notes imported in {FormatElapsed(elapsed)}.");
        }
        catch (Exception ex)
        {
            SetSubtitleGenerationState("失敗");
            ShowError("AIメモの追加に失敗しました", ex);
            AppendSubtitleGenerationLog("ERROR: " + ex.Message);
        }
        finally
        {
            _isSubtitleGenerationRunning = false;
            SetSubtitleGenerationEnabled(_selectedMovie is not null);
        }
    }

    private void OnBrowseWhisperOutputDirectoryClicked(object sender, RoutedEventArgs e)
    {
        var initialDirectory = Directory.Exists(WhisperOutputDirectoryTextBox.Text)
            ? WhisperOutputDirectoryTextBox.Text
            : GetDefaultSubtitleGenerationDirectory(_selectedMovie);
        var dialog = new OpenFolderDialog
        {
            Title = "WhisperX字幕の出力先フォルダを選択",
            InitialDirectory = initialDirectory
        };

        if (dialog.ShowDialog(this) == true)
        {
            WhisperOutputDirectoryTextBox.Text = dialog.FolderName;
        }
    }

    private async void OnSaveWhisperDefaultsClicked(object sender, RoutedEventArgs e)
    {
        await SaveStudioPreferencesAsync();
        SetStatus("字幕生成の既定設定を保存しました。");
    }

    private async void OnResetTranslationPromptClicked(object sender, RoutedEventArgs e)
    {
        TranslationPromptTextBox.Text = DefaultTranslationPrompt;
        await SaveStudioPreferencesAsync();
        SetStatus("翻訳プロンプトをベースに戻しました。");
    }

    private async void OnResetLearningNotesPromptClicked(object sender, RoutedEventArgs e)
    {
        LearningNotesPromptTextBox.Text = DefaultLearningNotesPrompt;
        await SaveStudioPreferencesAsync();
        SetStatus("AIメモプロンプトをベースに戻しました。");
    }

    private async void OnToggleDualSubtitleClicked(object sender, RoutedEventArgs e)
    {
        _showDualSubtitles = !_showDualSubtitles;
        UpdateDualSubtitleButton();
        UpdatePreviewSubtitleAtCurrentPosition();
        UpdateFullPreviewSubtitle(FullPreviewPlayer.Position);
        await SaveStudioPreferencesAsync();
    }

    private async void OnHighlightColorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingPreferences || HighlightColorComboBox.SelectedValue is not string color)
        {
            return;
        }

        _subtitleTagHighlightColor = color;
        RenderSceneRows(_previewSubtitleTrack);
        UpdatePreviewSubtitleAtCurrentPosition();
        await SaveStudioPreferencesAsync();
    }

    private async void OnManageTagsClicked(object sender, RoutedEventArgs e)
    {
        var library = await _libraryStore.LoadAsync();
        MergeTagDefinitionsFromLibrary(library);

        var movieTags = new ObservableCollection<TagDefinitionRow>(
            library.TagDefinitions
                .Where(tag => tag.Scope == TagScope.Movie)
                .OrderBy(tag => tag.SortOrder)
                .ThenBy(tag => tag.Name)
                .Select(tag => new TagDefinitionRow(tag)));
        var subtitleTags = new ObservableCollection<TagDefinitionRow>(
            library.TagDefinitions
                .Where(tag => tag.Scope == TagScope.Subtitle)
                .OrderBy(tag => tag.SortOrder)
                .ThenBy(tag => tag.Name)
                .Select(tag => new TagDefinitionRow(tag)));

        var window = CreateTagManagerWindow(movieTags, subtitleTags);
        if (window.ShowDialog() != true)
        {
            return;
        }

        library.TagDefinitions = movieTags
            .Select((row, index) => row.ToDefinition(TagScope.Movie, index))
            .Concat(subtitleTags.Select((row, index) => row.ToDefinition(TagScope.Subtitle, index)))
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Name))
            .GroupBy(tag => (tag.Scope, NormalizedTagKey(tag.Name)))
            .Select(group => group.First())
            .ToList();

        await _libraryStore.SaveAsync(library);
        SetStatus("タグ定義を保存しました。");
    }

    private void OnWindowDragOver(object sender, System.Windows.DragEventArgs e)
    {
        var paths = GetDroppedFilePaths(e);
        var hasVideo = paths.Any(IsVideoFile);
        var hasSubtitle = paths.Any(IsSubtitleFile);

        e.Effects = hasVideo || (hasSubtitle && _selectedMovie is not null)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnWindowDrop(object sender, System.Windows.DragEventArgs e)
    {
        var paths = GetDroppedFilePaths(e).ToList();
        if (paths.Count == 0)
        {
            return;
        }

        var videoPaths = paths.Where(IsVideoFile).ToList();
        var subtitlePaths = paths.Where(IsSubtitleFile).ToList();
        var importedMovies = new List<Movie>();
        var importedSubtitleCount = 0;

        try
        {
            foreach (var videoPath in videoPaths)
            {
                importedMovies.Add(await ImportVideoAsync(videoPath));
            }

            if (subtitlePaths.Count > 0)
            {
                var subtitleTarget = importedMovies.Count == 1
                    ? importedMovies[0]
                    : _selectedMovie;
                if (subtitleTarget is null)
                {
                    SetStatus("字幕を追加する動画を選択してからドロップしてください。");
                }
                else
                {
                    foreach (var subtitlePath in subtitlePaths)
                    {
                        await ImportSubtitleAsync(subtitleTarget, subtitlePath);
                        importedSubtitleCount++;
                    }
                }
            }

            var selectedMovieId = importedMovies.LastOrDefault()?.Id ?? _selectedMovie?.Id;
            await RefreshMoviesAsync(selectedMovieId);

            if (importedMovies.Count > 0 || importedSubtitleCount > 0)
            {
                SetStatus($"ドロップから追加しました: {importedMovies.Count} video / {importedSubtitleCount} subtitle");
            }
            else if (videoPaths.Count == 0 && subtitlePaths.Count == 0)
            {
                SetStatus("対応している動画または字幕ファイルをドロップしてください。");
            }
        }
        catch (Exception ex)
        {
            ShowError("ドロップしたファイルの取り込みに失敗しました", ex);
        }
    }

    private async void OnTitleLostFocus(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie is null || _isUpdatingSelection)
        {
            return;
        }

        var title = TitleTextBox.Text.Trim();
        if (title.Length == 0 || string.Equals(title, _selectedMovie.Title, StringComparison.Ordinal))
        {
            return;
        }

        _selectedMovie.Title = title;
        await _libraryStore.UpsertMovieAsync(_selectedMovie);
        await RefreshMoviesAsync(_selectedMovie.Id);
        SetStatus("タイトルを保存しました。");
    }

    private async void OnMovieSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (MoviesListBox.SelectedItem is not MovieListItem item)
        {
            _selectedMovie = null;
            RenderMovieDetails(null);
            return;
        }

        var library = await _libraryStore.LoadAsync();
        _selectedMovie = library.Movies.FirstOrDefault(movie => string.Equals(movie.Id, item.MovieId, StringComparison.Ordinal));
        RenderMovieDetails(_selectedMovie);
    }

    private void OnSubtitleSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelection || _selectedMovie is null)
        {
            return;
        }

        if (SubtitlesDataGrid.SelectedItem is not SubtitleRow row)
        {
            _previewSubtitleTrack = null;
            RemoveSubtitleButton.IsEnabled = false;
            HidePreviewSubtitle();
            RenderSceneRows(null);
            return;
        }

        RemoveSubtitleButton.IsEnabled = true;
        _previewSubtitleTrack = _selectedMovie.SubtitleTracks
            .FirstOrDefault(track => string.Equals(track.Id, row.TrackId, StringComparison.Ordinal));
        RenderSceneRows(_previewSubtitleTrack);
        UpdatePreviewSubtitleAtCurrentPosition();
    }

    private void OnPlayPreviewClicked(object sender, RoutedEventArgs e)
    {
        _previewStopAt = null;
        StartPreview();
    }

    private void OnPausePreviewClicked(object sender, RoutedEventArgs e)
    {
        TogglePreviewPlayback();
    }

    private void OnStopPreviewClicked(object sender, RoutedEventArgs e)
    {
        _previewTimer.Stop();
        _playPreviewWhenMediaOpened = false;
        _isPreviewPlaying = false;
        _previewStopAt = null;
        PreviewPlayer.Stop();
        SetPreviewSeek(TimeSpan.Zero);
        HidePreviewSubtitle();
        UpdatePlaybackButtonContent();
        SetStatus("プレビューを停止しました。");
    }

    private async void OnCreateThumbnailClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie is null)
        {
            SetStatus("動画を選択してください。");
            return;
        }

        try
        {
            var videoPath = ResolveGenerationVideoPath(_selectedMovie);
            var capturePosition = PreviewPlayer.Source is not null
                ? PreviewPlayer.Position
                : TimeSpan.Zero;
            var thumbnailPath = GetMovieThumbnailPath(_selectedMovie.Id);
            SetStatus("サムネイルを作成中です...", hideProgress: false);
            await CreateThumbnailAsync(videoPath, thumbnailPath, capturePosition);

            _selectedMovie.Video.ThumbnailPath = thumbnailPath;
            _selectedMovie.Video.ThumbnailTimestampSeconds = Math.Max(0, capturePosition.TotalSeconds);
            _selectedMovie.UpdatedAt = DateTimeOffset.UtcNow;
            await _libraryStore.UpsertMovieAsync(_selectedMovie);
            await RefreshMoviesAsync(_selectedMovie.Id);
            SetStatus($"サムネイルを作成しました: {FormatTimestamp(capturePosition)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "サムネイル作成に失敗しました",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            SetStatus("サムネイル作成に失敗しました。");
        }
    }

    private void OnPlayThumbnailClipClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie?.Video.ThumbnailTimestampSeconds is not { } seconds)
        {
            SetStatus("サムネイル位置がまだありません。先にサムネイルを作成してください。");
            return;
        }

        var start = TimeSpan.FromSeconds(Math.Max(0, seconds));
        _previewStopAt = start.Add(TimeSpan.FromSeconds(5));
        StartPreview(start);
        SetStatus("サムネイル位置を5秒だけ再生します。");
    }

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.Handled || IsInteractiveInputFocused(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (FullPreviewTabItem.IsSelected)
        {
            ToggleFullPreviewPlayback();
        }
        else if (EditTabItem.IsSelected)
        {
            TogglePreviewPlayback();
        }

        e.Handled = true;
    }

    private void OnPreviewSubtitleClicked(object sender, MouseButtonEventArgs e)
    {
        if (_currentPreviewCue is not null)
        {
            JumpPreviewTo(_currentPreviewCue.Start);
        }
    }

    private void OnSceneMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ScenesDataGrid.SelectedItem is SceneRow row)
        {
            JumpPreviewTo(row.Start);
        }
    }

    private async void OnSceneCurrentCellChanged(object sender, EventArgs e)
    {
        if (_isUpdatingSelection
            || _selectedMovie is null
            || _previewSubtitleTrack is null
            || ScenesDataGrid.CurrentItem is not SceneRow row)
        {
            return;
        }

        await SaveSceneRowLearningStateAsync(row);
    }

    private async void OnSceneCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_isUpdatingSelection || e.Row.Item is not SceneRow row)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        var header = e.Column.Header?.ToString();
        if (string.Equals(header, "Start", StringComparison.OrdinalIgnoreCase)
            || string.Equals(header, "End", StringComparison.OrdinalIgnoreCase))
        {
            await SaveSceneRowTimingAsync(row);
            return;
        }

        if (string.Equals(e.Column.Header?.ToString(), "Tags", StringComparison.OrdinalIgnoreCase))
        {
            row.IsFlagged = ParseTags(row.Tags).Any(IsFlagTag);
        }

        await SaveSceneRowLearningStateAsync(row);
    }

    private void OnFlaggedOnlyChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSelection)
        {
            return;
        }

        RenderSceneRows(_previewSubtitleTrack);
    }

    private void OnPlayNextFlaggedCueClicked(object sender, RoutedEventArgs e)
    {
        if (_previewSubtitleTrack is null)
        {
            SetStatus("字幕を選択してください。");
            return;
        }

        var flaggedCues = _previewSubtitleTrack.Cues
            .Where(cue => IsFlaggedLearningState(FindCueLearningState(_previewSubtitleTrack, cue)))
            .OrderBy(cue => cue.Start)
            .ToList();
        if (flaggedCues.Count == 0)
        {
            SetStatus("flagタグ付き字幕がありません。");
            return;
        }

        var currentPosition = PreviewPlayer.Source is null ? TimeSpan.Zero : PreviewPlayer.Position;
        var nextCue = flaggedCues.FirstOrDefault(cue => cue.Start > currentPosition.Add(TimeSpan.FromMilliseconds(250)))
            ?? flaggedCues[0];

        SelectSceneRow(nextCue.Id);
        JumpPreviewTo(nextCue.Start);
    }

    private async void OnShiftSelectedCueEarlierClicked(object sender, RoutedEventArgs e)
    {
        await ShiftSelectedCueTimingAsync(-1);
    }

    private async void OnShiftSelectedCueLaterClicked(object sender, RoutedEventArgs e)
    {
        await ShiftSelectedCueTimingAsync(1);
    }

    private async void OnSetSelectedCueStartFromPreviewClicked(object sender, RoutedEventArgs e)
    {
        await SetSelectedCueBoundaryFromPreviewAsync(setStart: true);
    }

    private async void OnSetSelectedCueEndFromPreviewClicked(object sender, RoutedEventArgs e)
    {
        await SetSelectedCueBoundaryFromPreviewAsync(setStart: false);
    }

    private void OnPreviewMediaOpened(object sender, RoutedEventArgs e)
    {
        _isPreviewMediaOpened = true;
        if (PreviewPlayer.NaturalDuration.HasTimeSpan)
        {
            _previewDuration = PreviewPlayer.NaturalDuration.TimeSpan;
            PreviewSeekSlider.Maximum = Math.Max(1.0, _previewDuration.TotalSeconds);
            PreviewSeekSlider.IsEnabled = _previewDuration > TimeSpan.Zero;
        }
        else
        {
            ResetPreviewSeek();
        }

        if (_pendingPreviewSeek is { } pendingPosition)
        {
            _pendingPreviewSeek = null;
            SeekPreviewTo(pendingPosition);
        }
        else
        {
            SetPreviewSeek(PreviewPlayer.Position);
        }

        if (_playPreviewWhenMediaOpened)
        {
            _playPreviewWhenMediaOpened = false;
            PreviewPlayer.Play();
            _isPreviewPlaying = true;
            _previewTimer.Start();
            UpdatePlaybackButtonContent();
            SetStatus("プレビュー再生中です。");
            return;
        }

        SetStatus("プレビューの準備ができました。");
    }

    private void OnPreviewMediaEnded(object sender, RoutedEventArgs e)
    {
        _previewTimer.Stop();
        _playPreviewWhenMediaOpened = false;
        _isPreviewPlaying = false;
        PreviewPlayer.Stop();
        SetPreviewSeek(TimeSpan.Zero);
        UpdatePlaybackButtonContent();
        SetStatus("プレビューが終了しました。");
    }

    private void OnPreviewSeekStarted(object sender, MouseButtonEventArgs e)
    {
        BeginPreviewSeek();
    }

    private void OnPreviewSeekCompleted(object sender, MouseButtonEventArgs e)
    {
        CompletePreviewSeek();
    }

    private void OnPreviewSeekDragStarted(object sender, DragStartedEventArgs e)
    {
        BeginPreviewSeek();
    }

    private void OnPreviewSeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        CompletePreviewSeek();
    }

    private void OnPreviewSeekLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isPreviewSeeking && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            CompletePreviewSeek();
        }
    }

    private void OnPreviewSeekKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (PreviewSeekSlider.IsEnabled)
        {
            SeekPreviewToSliderValue();
        }
    }

    private void OnPreviewSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingPreviewSlider)
        {
            return;
        }

        var position = TimeSpan.FromSeconds(Math.Clamp(e.NewValue, 0.0, PreviewSeekSlider.Maximum));
        PreviewPositionTextBlock.Text = FormatPlaybackPosition(position, _previewDuration);
        UpdatePreviewSubtitle(position);
        if (_isPreviewSeeking && PreviewPlayer.Source is not null && _previewDuration > TimeSpan.Zero)
        {
            PreviewPlayer.Position = ClampPreviewPosition(position);
        }
    }

    private void OnFullPreviewPlayClicked(object sender, RoutedEventArgs e)
    {
        StartFullPreview();
    }

    private void OnPauseFullPreviewClicked(object sender, RoutedEventArgs e)
    {
        ToggleFullPreviewPlayback();
    }

    private void OnFullPreviewStopClicked(object sender, RoutedEventArgs e)
    {
        _playFullPreviewWhenMediaOpened = false;
        _isFullPreviewSeeking = false;
        _isFullPreviewPlaying = false;
        FullPreviewPlayer.Stop();
        SetFullPreviewSeek(TimeSpan.Zero);
        HideFullPreviewSubtitle();
        UpdatePlaybackButtonContent();
        SetStatus("フルプレビューを停止しました。");
    }

    private void OnFullPreviewMediaOpened(object sender, RoutedEventArgs e)
    {
        _isFullPreviewMediaOpened = true;
        if (FullPreviewPlayer.NaturalDuration.HasTimeSpan)
        {
            _fullPreviewDuration = FullPreviewPlayer.NaturalDuration.TimeSpan;
            FullPreviewSeekSlider.Maximum = Math.Max(1.0, _fullPreviewDuration.TotalSeconds);
            FullPreviewSeekSlider.IsEnabled = _fullPreviewDuration > TimeSpan.Zero;
        }
        else
        {
            ResetFullPreviewSeek();
        }

        if (_pendingFullPreviewSeek is { } pendingPosition)
        {
            _pendingFullPreviewSeek = null;
            SeekFullPreviewTo(pendingPosition);
        }
        else
        {
            SetFullPreviewSeek(FullPreviewPlayer.Position);
        }

        if (_playFullPreviewWhenMediaOpened)
        {
            _playFullPreviewWhenMediaOpened = false;
            FullPreviewPlayer.Play();
            _isFullPreviewPlaying = true;
            _previewTimer.Start();
            UpdatePlaybackButtonContent();
            SetStatus("フルプレビュー再生中です。");
            return;
        }

        SetStatus("フルプレビューの準備ができました。");
    }

    private void OnFullPreviewMediaEnded(object sender, RoutedEventArgs e)
    {
        _playFullPreviewWhenMediaOpened = false;
        _isFullPreviewPlaying = false;
        FullPreviewPlayer.Stop();
        SetFullPreviewSeek(TimeSpan.Zero);
        UpdatePlaybackButtonContent();
        SetStatus("フルプレビューが終了しました。");
    }

    private void OnFullPreviewSeekStarted(object sender, MouseButtonEventArgs e)
    {
        BeginFullPreviewSeek();
    }

    private void OnFullPreviewSeekCompleted(object sender, MouseButtonEventArgs e)
    {
        CompleteFullPreviewSeek();
    }

    private void OnFullPreviewSeekDragStarted(object sender, DragStartedEventArgs e)
    {
        BeginFullPreviewSeek();
    }

    private void OnFullPreviewSeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        CompleteFullPreviewSeek();
    }

    private void OnFullPreviewSeekLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isFullPreviewSeeking && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            CompleteFullPreviewSeek();
        }
    }

    private void OnFullPreviewSeekKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (FullPreviewSeekSlider.IsEnabled)
        {
            SeekFullPreviewToSliderValue();
        }
    }

    private void OnFullPreviewSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingFullPreviewSlider)
        {
            return;
        }

        var position = TimeSpan.FromSeconds(Math.Clamp(e.NewValue, 0.0, FullPreviewSeekSlider.Maximum));
        FullPreviewPositionTextBlock.Text = FormatPlaybackPosition(position, _fullPreviewDuration);
        UpdateFullPreviewSubtitle(position);
        if (_isFullPreviewSeeking && FullPreviewPlayer.Source is not null && _fullPreviewDuration > TimeSpan.Zero)
        {
            FullPreviewPlayer.Position = ClampFullPreviewPosition(position);
        }
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
        var movie = new Movie
        {
            Id = movieId,
            Title = Path.GetFileNameWithoutExtension(sourceFileName),
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

    private async Task<string> GenerateEnglishSubtitleAsync(Movie movie)
    {
        var videoPath = ResolveGenerationVideoPath(movie);
        var outputDirectory = NormalizeOptionalText(WhisperOutputDirectoryTextBox.Text)
            ?? GetDefaultSubtitleGenerationDirectory(movie);
        Directory.CreateDirectory(outputDirectory);

        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        var generatedSrtPath = Path.Combine(outputDirectory, baseName + ".srt");
        var englishSrtPath = Path.Combine(outputDirectory, baseName + ".en.srt");
        var overwrite = OverwriteGeneratedSubtitleCheckBox.IsChecked == true;

        if (File.Exists(englishSrtPath) && !overwrite)
        {
            AppendSubtitleGenerationLog($"Existing English subtitle found: {englishSrtPath}");
            return englishSrtPath;
        }

        if (overwrite)
        {
            BackupExistingFile(englishSrtPath);
            BackupExistingFile(generatedSrtPath);
        }

        var pythonCommand = NormalizeOptionalText(WhisperPythonCommandTextBox.Text) ?? "py";
        var launcherArguments = SplitCommandLine(WhisperPythonArgumentsTextBox.Text);
        var model = NormalizeOptionalText(WhisperModelTextBox.Text) ?? "medium";
        var language = NormalizeOptionalText(WhisperLanguageTextBox.Text) ?? "en";
        var device = SelectedComboText(WhisperDeviceComboBox, "cuda");
        var computeType = SelectedComboText(WhisperComputeTypeComboBox, "float16");

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonCommand,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        foreach (var argument in launcherArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add(videoPath);
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(model);
        startInfo.ArgumentList.Add("--language");
        startInfo.ArgumentList.Add(language);
        startInfo.ArgumentList.Add("--output_format");
        startInfo.ArgumentList.Add("srt");
        startInfo.ArgumentList.Add("--output_dir");
        startInfo.ArgumentList.Add(outputDirectory);
        startInfo.ArgumentList.Add("--device");
        startInfo.ArgumentList.Add(device);
        startInfo.ArgumentList.Add("--compute_type");
        startInfo.ArgumentList.Add(computeType);
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

        AppendSubtitleGenerationLog("Command:");
        AppendSubtitleGenerationLog(FormatProcessCommand(startInfo));
        AppendSubtitleGenerationLog("RUNNING: waiting for WhisperX process to finish...");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("WhisperX process could not be started.");
        }

        AppendSubtitleGenerationLog($"RUNNING: WhisperX process started. PID={process.Id}");
        var outputTask = PumpProcessOutputAsync(process.StandardOutput);
        var errorTask = PumpProcessOutputAsync(process.StandardError);
        await process.WaitForExitAsync();
        await Task.WhenAll(outputTask, errorTask);
        AppendSubtitleGenerationLog($"WhisperX process exited with code {process.ExitCode}.");
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"WhisperX exited with code {process.ExitCode}.");
        }

        if (File.Exists(englishSrtPath))
        {
            return englishSrtPath;
        }

        if (File.Exists(generatedSrtPath))
        {
            File.Move(generatedSrtPath, englishSrtPath, overwrite: true);
            AppendSubtitleGenerationLog($"Renamed generated SRT: {englishSrtPath}");
            return englishSrtPath;
        }

        throw new FileNotFoundException("WhisperX completed but no SRT file was found.", generatedSrtPath);
    }

    private async Task<string> GenerateJapaneseSubtitleAsync(Movie movie, string? englishSrtPath = null)
    {
        var videoPath = ResolveGenerationVideoPath(movie);
        var outputDirectory = NormalizeOptionalText(WhisperOutputDirectoryTextBox.Text)
            ?? GetDefaultSubtitleGenerationDirectory(movie);
        Directory.CreateDirectory(outputDirectory);

        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        englishSrtPath ??= ResolveEnglishSubtitlePath(movie, outputDirectory, baseName);
        var japaneseSrtPath = Path.Combine(outputDirectory, baseName + ".ja.srt");
        var overwrite = OverwriteJapaneseSubtitleCheckBox.IsChecked == true;

        if (File.Exists(japaneseSrtPath) && !overwrite)
        {
            AppendSubtitleGenerationLog($"Existing Japanese subtitle found: {japaneseSrtPath}");
            return japaneseSrtPath;
        }

        if (overwrite)
        {
            BackupExistingFile(japaneseSrtPath);
        }

        var translationCommand = NormalizeOptionalText(TranslationCommandTextBox.Text);
        if (translationCommand is null)
        {
            translationCommand = DefaultTranslationCommand;
        }

        var argumentTemplate = NormalizeOptionalText(TranslationArgumentsTextBox.Text)
            ?? DefaultTranslationArguments;
        if (IsCodexSparkCommand(translationCommand)
            && (argumentTemplate.TrimStart().StartsWith("--input", StringComparison.OrdinalIgnoreCase)
                || argumentTemplate.Contains("{notesOutput}", StringComparison.OrdinalIgnoreCase)))
        {
            argumentTemplate = DefaultTranslationArguments;
        }

        var useCodexRelativePaths = IsCodexSparkCommand(translationCommand);
        var processEnglishSrtPath = useCodexRelativePaths
            ? EnsureFileAvailableInWorkingDirectory(englishSrtPath, outputDirectory)
            : englishSrtPath;
        var sourceLanguage = NormalizeOptionalText(TranslationSourceLanguageTextBox.Text) ?? "en";
        var targetLanguage = NormalizeOptionalText(TranslationTargetLanguageTextBox.Text) ?? "ja";
        var aiModel = NormalizeOptionalText(TranslationModelTextBox.Text) ?? DefaultCodexSparkModel;
        var inputDirectory = Path.GetDirectoryName(processEnglishSrtPath) ?? outputDirectory;

        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["input"] = FormatExternalProcessPath(processEnglishSrtPath, outputDirectory, useCodexRelativePaths),
            ["output"] = FormatExternalProcessPath(japaneseSrtPath, outputDirectory, useCodexRelativePaths),
            ["inputDir"] = FormatExternalProcessDirectory(inputDirectory, outputDirectory, useCodexRelativePaths),
            ["outputDir"] = FormatExternalProcessDirectory(outputDirectory, outputDirectory, useCodexRelativePaths),
            ["source"] = sourceLanguage,
            ["target"] = targetLanguage,
            ["model"] = aiModel,
            ["movie"] = videoPath,
            ["title"] = movie.Title
        };
        var promptTemplate = NormalizeOptionalText(TranslationPromptTextBox.Text) ?? DefaultTranslationPrompt;
        var promptText = ApplyArgumentTemplate(promptTemplate, replacements);
        var promptFilePath = Path.Combine(outputDirectory, baseName + ".translation.prompt.md");
        await File.WriteAllTextAsync(promptFilePath, promptText, Encoding.UTF8);
        replacements["promptFile"] = FormatExternalProcessPath(promptFilePath, outputDirectory, useCodexRelativePaths);
        replacements["prompt"] = promptText;

        var translationArguments = SplitCommandLine(ApplyArgumentTemplate(argumentTemplate, replacements));
        var startInfo = CreateTranslationProcessStartInfo(translationCommand, translationArguments, outputDirectory, aiModel);

        AppendSubtitleGenerationLog("Translation command:");
        AppendSubtitleGenerationLog(FormatProcessCommand(startInfo));
        AppendSubtitleGenerationLog($"Translation input: {englishSrtPath}");
        AppendSubtitleGenerationLog($"Translation output: {japaneseSrtPath}");
        AppendSubtitleGenerationLog($"Translation prompt: {promptFilePath}");
        AppendSubtitleGenerationLog("RUNNING: waiting for AI translation process to finish...");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Translation process could not be started.");
        }

        AppendSubtitleGenerationLog($"RUNNING: translation process started. PID={process.Id}");
        var outputTask = PumpProcessOutputAsync(process.StandardOutput);
        var errorTask = PumpProcessOutputAsync(process.StandardError);
        await process.WaitForExitAsync();
        await Task.WhenAll(outputTask, errorTask);
        AppendSubtitleGenerationLog($"Translation process exited with code {process.ExitCode}.");
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Translation process exited with code {process.ExitCode}.");
        }

        if (!File.Exists(japaneseSrtPath))
        {
            throw new FileNotFoundException("Translation process completed but no Japanese SRT file was found.", japaneseSrtPath);
        }

        AppendSubtitleGenerationLog("Verifying generated Japanese SRT...");
        var translatedContent = await File.ReadAllTextAsync(japaneseSrtPath, Encoding.UTF8);
        var translatedDocument = SubtitleParser.Parse(translatedContent, japaneseSrtPath);
        if (translatedDocument.Cues.Count == 0)
        {
            throw new InvalidOperationException("生成された日本語字幕に字幕キューが見つかりませんでした。");
        }

        AppendSubtitleGenerationLog($"Japanese SRT verified: {translatedDocument.Cues.Count} cues.");
        return japaneseSrtPath;
    }

    private async Task<int> GenerateLearningNotesAsync(Movie movie)
    {
        var videoPath = ResolveGenerationVideoPath(movie);
        var outputDirectory = NormalizeOptionalText(WhisperOutputDirectoryTextBox.Text)
            ?? GetDefaultSubtitleGenerationDirectory(movie);
        Directory.CreateDirectory(outputDirectory);

        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        var englishSrtPath = ResolveEnglishSubtitlePath(movie, outputDirectory, baseName);
        var notesOutputPath = Path.Combine(outputDirectory, baseName + ".learning-notes.json");
        var noteGenerationStartedAtUtc = PrepareGeneratedOutputPath(notesOutputPath);

        var command = NormalizeOptionalText(TranslationCommandTextBox.Text) ?? DefaultTranslationCommand;
        var useCodexRelativePaths = IsCodexSparkCommand(command);
        var processEnglishSrtPath = useCodexRelativePaths
            ? EnsureFileAvailableInWorkingDirectory(englishSrtPath, outputDirectory)
            : englishSrtPath;
        var promptFilePath = Path.Combine(outputDirectory, baseName + ".learning-notes.prompt.md");
        var sourceLanguage = NormalizeOptionalText(TranslationSourceLanguageTextBox.Text) ?? "en";
        var aiModel = NormalizeOptionalText(TranslationModelTextBox.Text) ?? DefaultCodexSparkModel;
        var audienceLevel = SelectedComboText(LearningNotesAudienceLevelComboBox, DefaultLearningNotesAudienceLevel);
        var inputDirectory = Path.GetDirectoryName(processEnglishSrtPath) ?? outputDirectory;
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["input"] = FormatExternalProcessPath(processEnglishSrtPath, outputDirectory, useCodexRelativePaths),
            ["notesOutput"] = FormatExternalProcessPath(notesOutputPath, outputDirectory, useCodexRelativePaths),
            ["inputDir"] = FormatExternalProcessDirectory(inputDirectory, outputDirectory, useCodexRelativePaths),
            ["outputDir"] = FormatExternalProcessDirectory(outputDirectory, outputDirectory, useCodexRelativePaths),
            ["source"] = sourceLanguage,
            ["model"] = aiModel,
            ["audienceLevel"] = audienceLevel,
            ["movie"] = videoPath,
            ["title"] = movie.Title,
            ["promptFile"] = FormatExternalProcessPath(promptFilePath, outputDirectory, useCodexRelativePaths)
        };
        var promptTemplate = NormalizeOptionalText(LearningNotesPromptTextBox.Text) ?? DefaultLearningNotesPrompt;
        var promptText = ApplyArgumentTemplate(promptTemplate, replacements);
        await File.WriteAllTextAsync(promptFilePath, promptText, Encoding.UTF8);

        var noteArguments = SplitCommandLine(ApplyArgumentTemplate(DefaultLearningNotesArguments, replacements));
        var startInfo = CreateTranslationProcessStartInfo(command, noteArguments, outputDirectory, aiModel);

        AppendSubtitleGenerationLog("AI note command:");
        AppendSubtitleGenerationLog(FormatProcessCommand(startInfo));
        AppendSubtitleGenerationLog($"AI note input: {englishSrtPath}");
        AppendSubtitleGenerationLog($"AI note output: {notesOutputPath}");
        AppendSubtitleGenerationLog($"AI note prompt: {promptFilePath}");
        AppendSubtitleGenerationLog("RUNNING: waiting for AI note process to finish...");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("AI note process could not be started.");
        }

        AppendSubtitleGenerationLog($"RUNNING: AI note process started. PID={process.Id}");
        var outputTask = PumpProcessOutputAsync(process.StandardOutput);
        var errorTask = PumpProcessOutputAsync(process.StandardError);
        await process.WaitForExitAsync();
        await Task.WhenAll(outputTask, errorTask);
        AppendSubtitleGenerationLog($"AI note process exited with code {process.ExitCode}.");
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"AI note process exited with code {process.ExitCode}.");
        }

        EnsureGeneratedOutputIsFresh(
            notesOutputPath,
            noteGenerationStartedAtUtc,
            "AI note process completed but no fresh learning notes JSON was found.");

        var targetTrack = FindLearningNotesTargetTrack(movie, englishSrtPath);
        if (targetTrack is null)
        {
            AppendSubtitleGenerationLog("English subtitle track was not imported yet. Importing it before applying AI notes.");
            targetTrack = await ImportSubtitleAsync(movie, englishSrtPath);
        }

        var importedCount = await ImportLearningNotesAsync(movie, targetTrack, notesOutputPath);
        AppendSubtitleGenerationLog($"AI notes imported into {targetTrack.Label}: {importedCount} cues.");
        return importedCount;
    }

    private async Task<SubtitleTrack> ImportSubtitleAsync(Movie movie, string sourcePath)
    {
        var sourceFileName = Path.GetFileName(sourcePath);
        var safeFileName = SanitizeFileName(sourceFileName);
        var subtitleDirectory = _paths.GetMovieSubtitleDirectory(movie.Id);
        Directory.CreateDirectory(subtitleDirectory);

        var content = await File.ReadAllTextAsync(sourcePath, Encoding.UTF8);
        var document = SubtitleParser.Parse(content, sourceFileName);
        if (document.Cues.Count == 0)
        {
            throw new InvalidOperationException("字幕キューが見つかりませんでした。SRT または WebVTT の形式を確認してください。");
        }

        var originalPath = EnsureUniquePath(Path.Combine(subtitleDirectory, safeFileName));
        await File.WriteAllTextAsync(originalPath, content, Encoding.UTF8);

        var vttPath = Path.Combine(subtitleDirectory, Path.GetFileNameWithoutExtension(safeFileName) + ".vtt");
        await File.WriteAllTextAsync(vttPath, SubtitleParser.ToWebVtt(document.Cues), Encoding.UTF8);

        var metadata = SubtitleFileMetadataService.Infer(sourceFileName);
        var track = new SubtitleTrack
        {
            Label = metadata.Label,
            Language = metadata.Language,
            Role = metadata.Role,
            GroupKey = metadata.GroupKey,
            Format = document.Format,
            SourceUri = sourcePath,
            SourceFileName = sourceFileName,
            LocalPath = originalPath,
            VttCachePath = vttPath,
            CueCount = document.Cues.Count,
            Cues = document.Cues
        };

        movie.SubtitleTracks.RemoveAll(existing =>
            string.Equals(existing.SourceFileName, track.SourceFileName, StringComparison.OrdinalIgnoreCase));
        movie.SubtitleTracks.Add(track);
        RefreshMovieSceneMarkers(movie);
        await _libraryStore.UpsertMovieAsync(movie);
        return track;
    }

    private async Task<int> ImportLearningNotesAsync(Movie movie, SubtitleTrack targetTrack, string notesOutputPath)
    {
        var json = await File.ReadAllTextAsync(notesOutputPath, Encoding.UTF8);
        var notes = ParseLearningNotesJson(json);
        if (notes.Count == 0)
        {
            throw new InvalidOperationException("AIメモJSONに取り込めるメモが見つかりませんでした。");
        }

        ValidateLearningNotesQuality(notes, targetTrack.Cues.Count);

        var importedCount = 0;
        foreach (var note in notes)
        {
            if (note.Index <= 0)
            {
                continue;
            }

            var cue = targetTrack.Cues.FirstOrDefault(candidate => candidate.Index == note.Index);
            if (cue is null)
            {
                continue;
            }

            var aiNote = NormalizeLearningNoteText(note);
            if (aiNote is null)
            {
                var existingState = FindCueLearningState(targetTrack, cue.Id, cue.Index);
                if (!string.IsNullOrWhiteSpace(existingState?.AiNote))
                {
                    existingState.AiNote = null;
                    existingState.UpdatedAt = DateTimeOffset.UtcNow;
                    importedCount++;
                }

                continue;
            }

            var state = EnsureCueLearningState(targetTrack, cue.Id, cue.Index);
            if (string.Equals(state.AiNote, aiNote, StringComparison.Ordinal))
            {
                continue;
            }

            state.AiNote = aiNote;
            state.UpdatedAt = DateTimeOffset.UtcNow;
            importedCount++;
        }

        if (importedCount > 0)
        {
            await _libraryStore.UpsertMovieAsync(movie);
        }

        return importedCount;
    }

    private static SubtitleTrack? FindLearningNotesTargetTrack(Movie movie, string englishSrtPath)
    {
        var sourceFileName = Path.GetFileName(englishSrtPath);
        var exactTrack = movie.SubtitleTracks.FirstOrDefault(track =>
            IsEnglishSubtitleTrack(track)
            && (string.Equals(track.SourceUri, englishSrtPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(track.SourceFileName, sourceFileName, StringComparison.OrdinalIgnoreCase)));
        if (exactTrack is not null)
        {
            return exactTrack;
        }

        var metadata = SubtitleFileMetadataService.Infer(sourceFileName);
        if (!string.IsNullOrWhiteSpace(metadata.GroupKey))
        {
            var groupedTrack = movie.SubtitleTracks.FirstOrDefault(track =>
                IsEnglishSubtitleTrack(track)
                && string.Equals(track.GroupKey, metadata.GroupKey, StringComparison.OrdinalIgnoreCase));
            if (groupedTrack is not null)
            {
                return groupedTrack;
            }
        }

        return movie.SubtitleTracks.FirstOrDefault(IsEnglishSubtitleTrack);
    }

    private static List<LearningNoteImportRow> ParseLearningNotesJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var notesElement = root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (TryGetProperty(root, "notes", out var notesProperty))
            {
                notesElement = notesProperty;
            }
            else if (TryGetProperty(root, "items", out var itemsProperty))
            {
                notesElement = itemsProperty;
            }
        }

        if (notesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new List<LearningNoteImportRow>();
        foreach (var item in notesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            rows.Add(new LearningNoteImportRow(
                TryGetInt(item, "index")
                    ?? TryGetInt(item, "cueIndex")
                    ?? TryGetInt(item, "cue_index")
                    ?? 0,
                TryGetString(item, "cefr")
                    ?? TryGetString(item, "level"),
                TryGetString(item, "note")
                    ?? TryGetString(item, "memo")
                    ?? TryGetString(item, "comment")));
        }

        return rows;
    }

    private static string? NormalizeLearningNoteText(LearningNoteImportRow note)
    {
        var text = NormalizeOptionalText(note.Note);
        var cefr = NormalizeOptionalText(note.Cefr);
        if (text is null)
        {
            return null;
        }

        if (IsNoDisplayLearningNoteText(text))
        {
            return null;
        }

        if (cefr is not null
            && !text.Contains("CEFR", StringComparison.OrdinalIgnoreCase)
            && !text.Contains(cefr, StringComparison.OrdinalIgnoreCase))
        {
            text = $"CEFR {cefr}: {text}";
        }

        return text;
    }

    private static bool IsNoDisplayLearningNoteText(string text)
    {
        return text.Contains("コメント不要", StringComparison.Ordinal)
            || text.Contains("解説不要", StringComparison.Ordinal)
            || text.Contains("メモ不要", StringComparison.Ordinal)
            || text.Contains("対象者レベル以下", StringComparison.Ordinal)
            || text.Contains("対象レベル以下", StringComparison.Ordinal);
    }

    private static bool IsLegacyLearningNotesPrompt(string prompt)
    {
        return !prompt.Contains("{audienceLevel}", StringComparison.OrdinalIgnoreCase)
            || !prompt.Contains("コメント不要", StringComparison.Ordinal);
    }

    private static void ValidateLearningNotesQuality(IReadOnlyList<LearningNoteImportRow> notes, int expectedCueCount)
    {
        if (expectedCueCount > 0 && notes.Count != expectedCueCount)
        {
            throw new InvalidOperationException(
                $"AIメモJSONの件数が字幕数と一致しません: notes={notes.Count}, cues={expectedCueCount}");
        }

        var normalizedNotes = notes
            .Select(NormalizeLearningNoteText)
            .Where(note => !string.IsNullOrWhiteSpace(note))
            .Cast<string>()
            .ToList();
        if (normalizedNotes.Count == 0)
        {
            throw new InvalidOperationException("AIメモJSONに取り込めるnoteがありません。重要な語彙・構文・世界観語だけをnoteにしてください。");
        }

        var placeholderNotes = normalizedNotes
            .Where(note =>
                note.Contains("$k", StringComparison.OrdinalIgnoreCase)
                || note.Contains("{", StringComparison.Ordinal)
                || note.Contains("}", StringComparison.Ordinal)
                || note.Contains("など抽象語", StringComparison.Ordinal)
                || note.Contains("基本表現。", StringComparison.Ordinal)
                || note.Contains("最小文型", StringComparison.Ordinal)
                || note.Contains("日常の応答や確認", StringComparison.Ordinal)
                || note.Contains("動詞フレーズ中心", StringComparison.Ordinal)
                || note.Contains("時制・文の型", StringComparison.Ordinal)
                || note.Contains("理由や条件を含み", StringComparison.Ordinal)
                || note.Contains("習得しやすい", StringComparison.Ordinal)
                || note.Contains("基礎語彙", StringComparison.Ordinal))
            .Take(3)
            .ToList();
        if (placeholderNotes.Count > 0)
        {
            throw new InvalidOperationException(
                "生成されたAIメモの品質が低いため取り込みませんでした。テンプレート/プレースホルダのような文があります: "
                + string.Join(" / ", placeholderNotes));
        }

        if (expectedCueCount >= 40 && normalizedNotes.Count > (int)Math.Ceiling(expectedCueCount * 0.55))
        {
            throw new InvalidOperationException(
                $"生成されたAIメモの品質が低いため取り込みませんでした。noteが多すぎます: notes={normalizedNotes.Count}, cues={expectedCueCount}。"
                + " B1以上の重要表現、世界観語、特殊な口語だけに絞ってください。");
        }

        if (normalizedNotes.Count < 30)
        {
            return;
        }

        var noteGroups = normalizedNotes
            .GroupBy(note => note, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ToList();
        var mostRepeated = noteGroups.First();
        var uniqueCount = noteGroups.Count;
        var maxAllowedRepeat = Math.Max(8, (int)Math.Ceiling(normalizedNotes.Count * 0.20));
        var minExpectedUnique = Math.Max(15, (int)Math.Ceiling(normalizedNotes.Count * 0.30));

        if (mostRepeated.Count() > maxAllowedRepeat)
        {
            throw new InvalidOperationException(
                $"生成されたAIメモの品質が低いため取り込みませんでした。同じnoteが多すぎます: {mostRepeated.Count()}件 / {normalizedNotes.Count}件。"
                + $" 例: {mostRepeated.Key}");
        }

        if (uniqueCount < minExpectedUnique)
        {
            throw new InvalidOperationException(
                $"生成されたAIメモの品質が低いため取り込みませんでした。内容が単調すぎます: unique={uniqueCount}, notes={normalizedNotes.Count}。"
                + " 各字幕固有の語句と理由を含めて再生成してください。");
        }
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static int? TryGetInt(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
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

    private async Task RefreshMoviesAsync(string? selectedMovieId = null)
    {
        var library = await _libraryStore.LoadAsync();
        ApplyStudioPreferences(library);
        var movies = library.Movies
            .OrderByDescending(movie => movie.UpdatedAt)
            .ToList();

        _movies.Clear();
        foreach (var movie in movies)
        {
            _movies.Add(new MovieListItem(movie));
        }

        SummaryTextBlock.Text = $"{_movies.Count} movies";

        var selectedItem = !string.IsNullOrWhiteSpace(selectedMovieId)
            ? _movies.FirstOrDefault(item => string.Equals(item.MovieId, selectedMovieId, StringComparison.Ordinal))
            : _movies.FirstOrDefault();

        MoviesListBox.SelectedItem = selectedItem;
        if (selectedItem is null)
        {
            _selectedMovie = null;
            RenderMovieDetails(null);
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
                FileNameTextBlock.Text = "動画を追加してください";
                CachePathTextBlock.Text = string.Empty;
                SizeTextBlock.Text = string.Empty;
                UpdateSubtitleGenerationPanel(null);
                _previewSubtitleTrack = null;
                SubtitlesDataGrid.ItemsSource = null;
                ScenesDataGrid.ItemsSource = null;
                HidePreviewSubtitle();
                HideFullPreviewSubtitle();
                return;
            }

            TitleTextBox.Text = movie.Title;
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
        AddSubtitleButton.IsEnabled = enabled;
        RemoveSubtitleButton.IsEnabled = enabled && SubtitlesDataGrid.SelectedItem is not null;
        WriteSidecarButton.IsEnabled = enabled;
        ExportDrivePackageButton.IsEnabled = enabled;
        DualSubtitleButton.IsEnabled = enabled;
        LearningNotesButton.IsEnabled = enabled;
        PlayButton.IsEnabled = enabled;
        PauseButton.IsEnabled = enabled;
        StopButton.IsEnabled = enabled;
        CreateThumbnailButton.IsEnabled = enabled;
        PlayThumbnailClipButton.IsEnabled = enabled && _selectedMovie?.Video.ThumbnailTimestampSeconds is not null;
        FullPreviewPlayButton.IsEnabled = enabled;
        FullPreviewPauseButton.IsEnabled = enabled;
        FullPreviewStopButton.IsEnabled = enabled;
        FullPreviewLearningNotesButton.IsEnabled = enabled;
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

    private void SetPackageExportProgress(CoffeeMoviePackageExportProgress progress)
    {
        StatusProgressBar.Visibility = Visibility.Visible;
        StatusProgressTextBlock.Visibility = Visibility.Visible;
        StatusProgressBar.Value = progress.Percent;
        StatusProgressTextBlock.Text = $"{progress.Percent:0}%";
        StatusTextBlock.Text = $"{progress.Stage}: {FormatBytes(progress.BytesWritten)} / {FormatBytes(progress.TotalBytes)}";
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

    private void RenderSceneRows(SubtitleTrack? subtitleTrack)
    {
        if (subtitleTrack is null)
        {
            ScenesDataGrid.ItemsSource = null;
            return;
        }

        var rows = subtitleTrack.Cues
            .Where(cue => !string.IsNullOrWhiteSpace(cue.Text))
            .Take(1000)
            .Select(cue =>
            {
                var learningState = FindCueLearningState(subtitleTrack, cue);
                return new SceneRow(cue, learningState, CreateSceneRowBackground(learningState, _subtitleTagHighlightColor));
            })
            .ToList();

        if (FlaggedOnlyCheckBox.IsChecked == true)
        {
            rows = rows.Where(row => row.IsFlagged).ToList();
        }

        ScenesDataGrid.ItemsSource = rows;
    }

    private async Task SaveSceneRowLearningStateAsync(SceneRow row)
    {
        if (_selectedMovie is null || _previewSubtitleTrack is null)
        {
            return;
        }

        var tags = ParseTags(row.Tags);
        if (row.IsFlagged)
        {
            AddTag(tags, FlagTagName);
        }
        else
        {
            tags.RemoveAll(tag => IsFlagTag(tag));
        }

        var note = NormalizeOptionalText(row.Note);
        var state = FindCueLearningState(_previewSubtitleTrack, row.CueId, row.CueIndex);
        if (state is null && !row.IsFlagged && tags.Count == 0 && note is null)
        {
            return;
        }

        state ??= EnsureCueLearningState(_previewSubtitleTrack, row.CueId, row.CueIndex);
        var isDirty = state.IsFlagged != row.IsFlagged
            || !state.Tags.SequenceEqual(tags, StringComparer.OrdinalIgnoreCase)
            || !string.Equals(state.Note, note, StringComparison.Ordinal);
        if (!isDirty)
        {
            return;
        }

        state.IsFlagged = tags.Any(IsFlagTag);
        state.Tags = tags;
        state.Note = note;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        row.IsFlagged = state.IsFlagged;
        row.Tags = string.Join(", ", tags);
        row.Note = note ?? string.Empty;

        await _libraryStore.UpsertMovieAsync(_selectedMovie);
        if (FlaggedOnlyCheckBox.IsChecked == true && !row.IsFlagged)
        {
            RenderSceneRows(_previewSubtitleTrack);
        }

        SetStatus("字幕の学習メタデータを保存しました。");
    }

    private async Task ShiftSelectedCueTimingAsync(int direction)
    {
        if (_selectedMovie is null || _previewSubtitleTrack is null || ScenesDataGrid.SelectedItem is not SceneRow row)
        {
            SetStatus("タイミングを調整する字幕行を選択してください。");
            return;
        }

        if (!TryGetTimingShiftMilliseconds(out var milliseconds))
        {
            SetStatus("タイミング補正値は 1 以上のミリ秒で入力してください。");
            return;
        }

        var offset = TimeSpan.FromMilliseconds(direction * milliseconds);
        var targetTracks = GetTimingShiftTargetTracks(_selectedMovie, _previewSubtitleTrack).ToList();
        var changedTracks = new List<SubtitleTrack>();
        var originalWriteCount = 0;
        TimeSpan? selectedStart = null;
        TimeSpan? selectedEnd = null;

        foreach (var track in targetTracks)
        {
            var cue = string.Equals(track.Id, _previewSubtitleTrack.Id, StringComparison.Ordinal)
                ? track.Cues.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, row.CueId, StringComparison.Ordinal)
                    || candidate.Index == row.CueIndex)
                : track.Cues.FirstOrDefault(candidate => candidate.Index == row.CueIndex);
            if (cue is null)
            {
                continue;
            }

            ShiftCue(cue, offset);
            if (string.Equals(track.Id, _previewSubtitleTrack.Id, StringComparison.Ordinal))
            {
                selectedStart = cue.Start;
                selectedEnd = cue.End;
            }

            changedTracks.Add(track);
            if (await RewriteSubtitleTrackFilesAsync(track, OriginalSubtitleWriteBackCheckBox.IsChecked == true))
            {
                originalWriteCount++;
            }
        }

        if (changedTracks.Count == 0)
        {
            SetStatus("調整対象の字幕が見つかりませんでした。");
            return;
        }

        RefreshMovieSceneMarkers(_selectedMovie);
        await _libraryStore.UpsertMovieAsync(_selectedMovie);
        RenderSceneRows(_previewSubtitleTrack);
        if (selectedStart is not null && selectedEnd is not null)
        {
            row.Timestamp = FormatCueEditTimestamp(selectedStart.Value);
            row.EndTimestamp = FormatCueEditTimestamp(selectedEnd.Value);
        }

        SelectSceneRow(row.CueId);
        UpdatePreviewSubtitleAtCurrentPosition();

        var directionText = direction > 0 ? "遅らせました" : "早めました";
        var syncText = changedTracks.Count > 1 ? $" / {changedTracks.Count} tracks synced" : string.Empty;
        var originalText = OriginalSubtitleWriteBackCheckBox.IsChecked == true
            ? $" / 原本更新 {originalWriteCount}"
            : string.Empty;
        SetStatus($"字幕タイミングを {milliseconds}ms {directionText}{syncText}{originalText}");
    }

    private async Task SetSelectedCueBoundaryFromPreviewAsync(bool setStart)
    {
        if (PreviewPlayer.Source is null || ScenesDataGrid.SelectedItem is not SceneRow row)
        {
            SetStatus("プレビュー再生中に字幕行を選択してください。");
            return;
        }

        var value = FormatCueEditTimestamp(PreviewPlayer.Position);
        if (setStart)
        {
            row.Timestamp = value;
        }
        else
        {
            row.EndTimestamp = value;
        }

        await SaveSceneRowTimingAsync(row);
    }

    private async Task SaveSceneRowTimingAsync(SceneRow row)
    {
        if (_selectedMovie is null || _previewSubtitleTrack is null)
        {
            return;
        }

        if (!TryParseCueTimestamp(row.Timestamp, out var start)
            || !TryParseCueTimestamp(row.EndTimestamp, out var end))
        {
            SetStatus("Start / End は 01:23.456 または 00:01:23.456 の形式で入力してください。");
            return;
        }

        if (end <= start)
        {
            SetStatus("End は Start より後にしてください。");
            return;
        }

        var targetTracks = GetTimingShiftTargetTracks(_selectedMovie, _previewSubtitleTrack).ToList();
        var changedTracks = new List<SubtitleTrack>();
        var originalWriteCount = 0;

        foreach (var track in targetTracks)
        {
            var cue = string.Equals(track.Id, _previewSubtitleTrack.Id, StringComparison.Ordinal)
                ? track.Cues.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, row.CueId, StringComparison.Ordinal)
                    || candidate.Index == row.CueIndex)
                : track.Cues.FirstOrDefault(candidate => candidate.Index == row.CueIndex);
            if (cue is null)
            {
                continue;
            }

            cue.Start = start;
            cue.End = end;
            changedTracks.Add(track);
            if (await RewriteSubtitleTrackFilesAsync(track, OriginalSubtitleWriteBackCheckBox.IsChecked == true))
            {
                originalWriteCount++;
            }
        }

        if (changedTracks.Count == 0)
        {
            SetStatus("調整対象の字幕が見つかりませんでした。");
            return;
        }

        RefreshMovieSceneMarkers(_selectedMovie);
        await _libraryStore.UpsertMovieAsync(_selectedMovie);
        row.Timestamp = FormatCueEditTimestamp(start);
        row.EndTimestamp = FormatCueEditTimestamp(end);
        RenderSceneRows(_previewSubtitleTrack);
        SelectSceneRow(row.CueId);
        UpdatePreviewSubtitleAtCurrentPosition();

        var syncText = changedTracks.Count > 1 ? $" / {changedTracks.Count} tracks synced" : string.Empty;
        var originalText = OriginalSubtitleWriteBackCheckBox.IsChecked == true
            ? $" / 原本更新 {originalWriteCount}"
            : string.Empty;
        SetStatus($"字幕タイミングを保存しました: {row.Timestamp} - {row.EndTimestamp}{syncText}{originalText}");
    }

    private bool TryGetTimingShiftMilliseconds(out int milliseconds)
    {
        return int.TryParse(TimingShiftTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out milliseconds)
            && milliseconds > 0
            && milliseconds <= 60_000;
    }

    private IEnumerable<SubtitleTrack> GetTimingShiftTargetTracks(Movie movie, SubtitleTrack selectedTrack)
    {
        yield return selectedTrack;

        if (SyncPairedSubtitleCheckBox.IsChecked != true)
        {
            yield break;
        }

        foreach (var track in movie.SubtitleTracks)
        {
            if (!string.Equals(track.Id, selectedTrack.Id, StringComparison.Ordinal)
                && HasSameSubtitleGroup(selectedTrack, track))
            {
                yield return track;
            }
        }
    }

    private static void ShiftCue(SubtitleCue cue, TimeSpan offset)
    {
        var duration = cue.End > cue.Start
            ? cue.End - cue.Start
            : TimeSpan.FromMilliseconds(1);
        var start = cue.Start + offset;
        if (start < TimeSpan.Zero)
        {
            start = TimeSpan.Zero;
        }

        cue.Start = start;
        cue.End = start + duration;
    }

    private void UpdatePreviewSubtitleAtCurrentPosition()
    {
        if (PreviewPlayer.Source is null)
        {
            HidePreviewSubtitle();
            return;
        }

        UpdatePreviewSubtitle(PreviewPlayer.Position);
    }

    private void UpdatePreviewSubtitle(TimeSpan position)
    {
        var lines = CreatePreviewSubtitleLines(position);
        if (lines.Count == 0)
        {
            HidePreviewSubtitle();
            return;
        }

        _currentPreviewCue = lines[0].Cue;
        RenderOverlayPanels(
            PreviewAboveOverlayPanel,
            PreviewBelowOverlayPanel,
            CreateOverlayItems(position, lines),
            isFullPreview: false);
        PreviewSubtitleOverlay.Visibility = Visibility.Collapsed;
        PreviewLearningNoteOverlay.Visibility = Visibility.Collapsed;
    }

    private void HidePreviewSubtitle()
    {
        _currentPreviewCue = null;
        PreviewAboveOverlayPanel.Children.Clear();
        PreviewAboveOverlayPanel.Visibility = Visibility.Collapsed;
        PreviewBelowOverlayPanel.Children.Clear();
        PreviewBelowOverlayPanel.Visibility = Visibility.Collapsed;
        PreviewSubtitlePrimaryTextBlock.Text = string.Empty;
        PreviewSubtitleSecondaryTextBlock.Text = string.Empty;
        PreviewSubtitleSecondaryTextBlock.Visibility = Visibility.Collapsed;
        PreviewSubtitleOverlay.BorderThickness = new Thickness(0);
        PreviewSubtitleOverlay.Visibility = Visibility.Collapsed;
        HidePreviewLearningNoteOverlay();
    }

    private void UpdateFullPreviewSubtitle(TimeSpan position)
    {
        var lines = CreatePreviewSubtitleLines(position);
        if (lines.Count == 0)
        {
            HideFullPreviewSubtitle();
            return;
        }

        RenderOverlayPanels(
            FullPreviewAboveOverlayPanel,
            FullPreviewBelowOverlayPanel,
            CreateOverlayItems(position, lines),
            isFullPreview: true);
        FullPreviewSubtitleOverlay.Visibility = Visibility.Collapsed;
        FullPreviewLearningNoteOverlay.Visibility = Visibility.Collapsed;
    }

    private void HideFullPreviewSubtitle()
    {
        FullPreviewAboveOverlayPanel.Children.Clear();
        FullPreviewAboveOverlayPanel.Visibility = Visibility.Collapsed;
        FullPreviewBelowOverlayPanel.Children.Clear();
        FullPreviewBelowOverlayPanel.Visibility = Visibility.Collapsed;
        FullPreviewSubtitlePrimaryTextBlock.Text = string.Empty;
        FullPreviewSubtitleSecondaryTextBlock.Text = string.Empty;
        FullPreviewSubtitleSecondaryTextBlock.Visibility = Visibility.Collapsed;
        FullPreviewSubtitleOverlay.BorderThickness = new Thickness(0);
        FullPreviewSubtitleOverlay.Visibility = Visibility.Collapsed;
        HideFullPreviewLearningNoteOverlay();
    }

    private List<PreviewOverlayItem> CreateOverlayItems(TimeSpan position, IReadOnlyList<PreviewSubtitleLine> lines)
    {
        var items = new List<PreviewOverlayItem>();
        foreach (var line in lines)
        {
            var text = NormalizePreviewSubtitleText(line.Cue.Text);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var isJapanese = IsJapaneseSubtitleTrack(line.Track);
            items.Add(new PreviewOverlayItem(
                isJapanese ? PreviewOverlayKind.JapaneseSubtitle : PreviewOverlayKind.EnglishSubtitle,
                text,
                isJapanese ? _japaneseSubtitleOverlayPosition : _englishSubtitleOverlayPosition,
                HasSubtitleTags(FindCueLearningState(line.Track, line.Cue))));
        }

        if (_showLearningNotes && FindLearningNoteState(position, lines) is { } state)
        {
            if (NormalizeOptionalText(state.AiNote) is { } aiNote)
            {
                items.Add(new PreviewOverlayItem(
                    PreviewOverlayKind.AiNote,
                    "AI: " + aiNote,
                    _aiNoteOverlayPosition,
                    HasHighlight: false));
            }

            if (NormalizeOptionalText(state.Note) is { } note)
            {
                items.Add(new PreviewOverlayItem(
                    PreviewOverlayKind.UserNote,
                    "MEMO: " + note,
                    _userNoteOverlayPosition,
                    HasHighlight: false));
            }
        }

        return items;
    }

    private void RenderOverlayPanels(
        StackPanel abovePanel,
        StackPanel belowPanel,
        IReadOnlyList<PreviewOverlayItem> items,
        bool isFullPreview)
    {
        abovePanel.Children.Clear();
        belowPanel.Children.Clear();

        var aboveItems = items
            .Select(item => new PositionedOverlayItem(item, ParseOverlayPosition(item.Position)))
            .Where(item => item.Placement.Side == OverlaySide.Above)
            .OrderBy(item => item.Placement.Order)
            .ThenBy(item => item.Item.SortPriority);
        foreach (var item in aboveItems)
        {
            abovePanel.Children.Add(CreateOverlayCard(item.Item, isFullPreview));
        }

        var belowItems = items
            .Select(item => new PositionedOverlayItem(item, ParseOverlayPosition(item.Position)))
            .Where(item => item.Placement.Side == OverlaySide.Below)
            .OrderByDescending(item => item.Placement.Order)
            .ThenBy(item => item.Item.SortPriority);
        foreach (var item in belowItems)
        {
            belowPanel.Children.Add(CreateOverlayCard(item.Item, isFullPreview));
        }

        abovePanel.Visibility = abovePanel.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        belowPanel.Visibility = belowPanel.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private FrameworkElement CreateOverlayCard(PreviewOverlayItem item, bool isFullPreview)
    {
        var isSubtitle = item.Kind is PreviewOverlayKind.EnglishSubtitle or PreviewOverlayKind.JapaneseSubtitle;
        var isJapanese = item.Kind == PreviewOverlayKind.JapaneseSubtitle;
        var fontSize = item.Kind switch
        {
            PreviewOverlayKind.EnglishSubtitle => isFullPreview ? 26 : 18,
            PreviewOverlayKind.JapaneseSubtitle => isFullPreview ? 20 : 15,
            _ => isFullPreview ? 18 : 13
        };

        var border = new Border
        {
            Background = isSubtitle
                ? new SolidColorBrush(Color.FromArgb(0xB0, 0x00, 0x00, 0x00))
                : new SolidColorBrush(Color.FromArgb(0xC0, 0x0B, 0x11, 0x1A)),
            BorderBrush = item.HasHighlight
                ? CreateBrush(_subtitleTagHighlightColor)
                : isSubtitle ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(0x5D, 0xE0, 0xD0)),
            BorderThickness = item.HasHighlight || !isSubtitle ? new Thickness(1) : new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Padding = isFullPreview ? new Thickness(18, 10, 18, 10) : new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 3, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new TextBlock
            {
                Text = item.Text,
                Foreground = isSubtitle
                    ? isJapanese ? new SolidColorBrush(Color.FromRgb(0xE0, 0xE7, 0xF0)) : Brushes.White
                    : new SolidColorBrush(Color.FromRgb(0xEA, 0xFB, 0xF8)),
                FontSize = fontSize,
                FontWeight = item.Kind == PreviewOverlayKind.EnglishSubtitle ? FontWeights.SemiBold : FontWeights.Normal,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            }
        };

        if (isSubtitle && !isFullPreview)
        {
            border.Cursor = Cursors.Hand;
            border.MouseLeftButtonUp += OnPreviewSubtitleClicked;
        }

        return border;
    }

    private void UpdatePreviewLearningNoteOverlay(TimeSpan position, IReadOnlyList<PreviewSubtitleLine> lines)
    {
        var noteText = CreateLearningNoteOverlayText(position, lines);
        if (noteText is null)
        {
            HidePreviewLearningNoteOverlay();
            return;
        }

        PreviewLearningNoteTextBlock.Text = noteText;
        PreviewLearningNoteOverlay.Visibility = Visibility.Visible;
    }

    private void HidePreviewLearningNoteOverlay()
    {
        PreviewLearningNoteTextBlock.Text = string.Empty;
        PreviewLearningNoteOverlay.Visibility = Visibility.Collapsed;
    }

    private void UpdateFullPreviewLearningNoteOverlay(TimeSpan position, IReadOnlyList<PreviewSubtitleLine> lines)
    {
        var noteText = CreateLearningNoteOverlayText(position, lines);
        if (noteText is null)
        {
            HideFullPreviewLearningNoteOverlay();
            return;
        }

        FullPreviewLearningNoteTextBlock.Text = noteText;
        FullPreviewLearningNoteOverlay.Visibility = Visibility.Visible;
    }

    private void HideFullPreviewLearningNoteOverlay()
    {
        FullPreviewLearningNoteTextBlock.Text = string.Empty;
        FullPreviewLearningNoteOverlay.Visibility = Visibility.Collapsed;
    }

    private string? CreateLearningNoteOverlayText(TimeSpan position, IReadOnlyList<PreviewSubtitleLine> lines)
    {
        if (!_showLearningNotes)
        {
            return null;
        }

        var state = FindLearningNoteState(position, lines);
        if (state is null)
        {
            return null;
        }

        var parts = new List<string>();
        if (NormalizeOptionalText(state.AiNote) is { } aiNote)
        {
            parts.Add("AI: " + aiNote);
        }

        if (NormalizeOptionalText(state.Note) is { } note)
        {
            parts.Add("MEMO: " + note);
        }

        return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    private SubtitleCueLearningState? FindLearningNoteState(TimeSpan position, IReadOnlyList<PreviewSubtitleLine> lines)
    {
        foreach (var line in lines)
        {
            if (IsEnglishSubtitleTrack(line.Track)
                && FindCueLearningState(line.Track, line.Cue) is { } state
                && HasLearningNote(state))
            {
                return state;
            }
        }

        if (_selectedMovie is not null)
        {
            var learningTrack = _previewSubtitleTrack is not null
                ? FindSubtitleTrackByRole(_selectedMovie, _previewSubtitleTrack, SubtitleTrackRole.LearningTarget)
                : _selectedMovie.SubtitleTracks.FirstOrDefault(IsEnglishSubtitleTrack);
            if (learningTrack is not null
                && FindActiveCue(learningTrack, position) is { } cue
                && FindCueLearningState(learningTrack, cue) is { } state
                && HasLearningNote(state))
            {
                return state;
            }
        }

        foreach (var line in lines)
        {
            if (FindCueLearningState(line.Track, line.Cue) is { } state && HasLearningNote(state))
            {
                return state;
            }
        }

        return null;
    }

    private static bool HasLearningNote(SubtitleCueLearningState state)
    {
        return !string.IsNullOrWhiteSpace(state.AiNote) || !string.IsNullOrWhiteSpace(state.Note);
    }

    private static bool IsJapaneseSubtitleTrack(SubtitleTrack track)
    {
        return track.Role == SubtitleTrackRole.Translation
            || string.Equals(track.Language, "ja", StringComparison.OrdinalIgnoreCase)
            || string.Equals(track.Language, "jp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(track.Language, "jpn", StringComparison.OrdinalIgnoreCase);
    }

    private static OverlayPlacement ParseOverlayPosition(string? position)
    {
        var normalized = NormalizeOverlayPosition(position, DefaultEnglishSubtitleOverlayPosition);
        var side = normalized.StartsWith("above", StringComparison.OrdinalIgnoreCase)
            ? OverlaySide.Above
            : OverlaySide.Below;
        var orderText = normalized[^1].ToString();
        var order = int.TryParse(orderText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOrder)
            ? Math.Clamp(parsedOrder, 1, 4)
            : 1;
        return new OverlayPlacement(side, order);
    }

    private static string NormalizeOverlayPosition(string? position, string fallback)
    {
        var normalized = NormalizeOptionalText(position)?.ToLowerInvariant();
        if (normalized is "above1" or "above2" or "above3" or "above4"
            or "below1" or "below2" or "below3" or "below4")
        {
            return normalized;
        }

        return fallback;
    }

    private void SetDefaultOverlayPositions()
    {
        _englishSubtitleOverlayPosition = DefaultEnglishSubtitleOverlayPosition;
        _japaneseSubtitleOverlayPosition = DefaultJapaneseSubtitleOverlayPosition;
        _aiNoteOverlayPosition = DefaultAiNoteOverlayPosition;
        _userNoteOverlayPosition = DefaultUserNoteOverlayPosition;
    }

    private void ApplyOverlayPositionComboBoxes()
    {
        EnglishSubtitlePositionComboBox.SelectedValue = _englishSubtitleOverlayPosition;
        JapaneseSubtitlePositionComboBox.SelectedValue = _japaneseSubtitleOverlayPosition;
        AiNotePositionComboBox.SelectedValue = _aiNoteOverlayPosition;
        UserNotePositionComboBox.SelectedValue = _userNoteOverlayPosition;
    }

    private void ReadOverlayPositionComboBoxes()
    {
        _englishSubtitleOverlayPosition = NormalizeOverlayPosition(
            EnglishSubtitlePositionComboBox.SelectedValue as string,
            DefaultEnglishSubtitleOverlayPosition);
        _japaneseSubtitleOverlayPosition = NormalizeOverlayPosition(
            JapaneseSubtitlePositionComboBox.SelectedValue as string,
            DefaultJapaneseSubtitleOverlayPosition);
        _aiNoteOverlayPosition = NormalizeOverlayPosition(
            AiNotePositionComboBox.SelectedValue as string,
            DefaultAiNoteOverlayPosition);
        _userNoteOverlayPosition = NormalizeOverlayPosition(
            UserNotePositionComboBox.SelectedValue as string,
            DefaultUserNoteOverlayPosition);
    }

    private void UpdatePlaybackButtonContent()
    {
        PauseButton.Content = _isPreviewPlaying ? "一時停止" : "再開";
        FullPreviewPauseButton.Content = _isFullPreviewPlaying ? "一時停止" : "再開";
    }

    private static bool IsInteractiveInputFocused(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TextBoxBase
                or System.Windows.Controls.ComboBox
                or ButtonBase
                or System.Windows.Controls.Slider
                or System.Windows.Controls.DataGrid)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private List<PreviewSubtitleLine> CreatePreviewSubtitleLines(TimeSpan position)
    {
        if (_selectedMovie is null || _previewSubtitleTrack is null)
        {
            return [];
        }

        if (!_showDualSubtitles)
        {
            var cue = FindActiveCue(_previewSubtitleTrack, position);
            return cue is null ? [] : [new PreviewSubtitleLine(_previewSubtitleTrack, cue)];
        }

        var topTrack = FindSubtitleTrackByRole(_selectedMovie, _previewSubtitleTrack, SubtitleTrackRole.LearningTarget)
            ?? _previewSubtitleTrack;
        var bottomTrack = FindSubtitleTrackByRole(_selectedMovie, _previewSubtitleTrack, SubtitleTrackRole.Translation);

        if (bottomTrack is not null && string.Equals(bottomTrack.Id, topTrack.Id, StringComparison.Ordinal))
        {
            bottomTrack = null;
        }

        var lines = new List<PreviewSubtitleLine>();
        var topCue = FindActiveCue(topTrack, position);
        if (topCue is not null)
        {
            lines.Add(new PreviewSubtitleLine(topTrack, topCue));
        }

        if (bottomTrack is not null)
        {
            var bottomCue = FindActiveCue(bottomTrack, position);
            if (bottomCue is not null)
            {
                lines.Add(new PreviewSubtitleLine(bottomTrack, bottomCue));
            }
        }

        if (lines.Count == 0)
        {
            var cue = FindActiveCue(_previewSubtitleTrack, position);
            if (cue is not null)
            {
                lines.Add(new PreviewSubtitleLine(_previewSubtitleTrack, cue));
            }
        }

        return lines;
    }

    private static SubtitleCue? FindActiveCue(SubtitleTrack track, TimeSpan position)
    {
        return track.Cues.FirstOrDefault(candidate =>
            candidate.Start <= position
            && position < candidate.End
            && !string.IsNullOrWhiteSpace(candidate.Text));
    }

    private static SubtitleTrack? FindSubtitleTrackByRole(Movie movie, SubtitleTrack anchor, SubtitleTrackRole role)
    {
        if (anchor.Role == role)
        {
            return anchor;
        }

        var groupedTrack = movie.SubtitleTracks.FirstOrDefault(track =>
            track.Role == role
            && HasSameSubtitleGroup(anchor, track));
        if (groupedTrack is not null)
        {
            return groupedTrack;
        }

        return movie.SubtitleTracks.FirstOrDefault(track => track.Role == role);
    }

    private static bool HasSameSubtitleGroup(SubtitleTrack left, SubtitleTrack right)
    {
        return !string.IsNullOrWhiteSpace(left.GroupKey)
            && string.Equals(left.GroupKey, right.GroupKey, StringComparison.OrdinalIgnoreCase);
    }

    private void StartPreview(TimeSpan? startPosition = null)
    {
        if (_selectedMovie?.Video.CachePath is null || !File.Exists(_selectedMovie.Video.CachePath))
        {
            SetStatus("プレビューできる動画ファイルがありません。");
            return;
        }

        if (!EnsurePreviewSource(_selectedMovie, playWhenReady: true, startPosition))
        {
            SetStatus("プレビューを準備中です。");
            return;
        }

        if (startPosition is { } position)
        {
            SeekPreviewTo(position);
        }

        PreviewPlayer.Play();
        _isPreviewPlaying = true;
        _previewTimer.Start();
        UpdatePlaybackButtonContent();
        SetStatus("プレビュー再生中です。");
    }

    private void JumpPreviewTo(TimeSpan position)
    {
        _previewStopAt = null;
        if (_selectedMovie?.Video.CachePath is null || !File.Exists(_selectedMovie.Video.CachePath))
        {
            SetStatus("プレビューできる動画ファイルがありません。");
            return;
        }

        if (!EnsurePreviewSource(_selectedMovie, playWhenReady: true, position))
        {
            SetStatus("プレビューを準備中です。");
            return;
        }

        SeekPreviewTo(position);
        PreviewPlayer.Play();
        _isPreviewPlaying = true;
        _previewTimer.Start();
        UpdatePlaybackButtonContent();
    }

    private void StartFullPreview(TimeSpan? startPosition = null)
    {
        if (_selectedMovie?.Video.CachePath is null || !File.Exists(_selectedMovie.Video.CachePath))
        {
            SetStatus("フルプレビューできる動画ファイルがありません。");
            return;
        }

        if (!EnsureFullPreviewSource(_selectedMovie, playWhenReady: true, startPosition))
        {
            SetStatus("フルプレビューを準備中です。");
            return;
        }

        if (startPosition is { } position)
        {
            SeekFullPreviewTo(position);
        }

        FullPreviewPlayer.Play();
        _isFullPreviewPlaying = true;
        _previewTimer.Start();
        UpdatePlaybackButtonContent();
        SetStatus("フルプレビュー再生中です。");
    }

    private void TogglePreviewPlayback()
    {
        if (_isPreviewPlaying)
        {
            PreviewPlayer.Pause();
            _isPreviewPlaying = false;
            UpdatePreviewSeekFromPlayer();
            UpdatePlaybackButtonContent();
            SetStatus("プレビューを一時停止しました。");
            return;
        }

        StartPreview(PreviewPlayer.Source is null ? null : PreviewPlayer.Position);
    }

    private void ToggleFullPreviewPlayback()
    {
        if (_isFullPreviewPlaying)
        {
            FullPreviewPlayer.Pause();
            _isFullPreviewPlaying = false;
            UpdateFullPreviewSeekFromPlayer();
            UpdatePlaybackButtonContent();
            SetStatus("フルプレビューを一時停止しました。");
            return;
        }

        StartFullPreview(FullPreviewPlayer.Source is null ? null : FullPreviewPlayer.Position);
    }

    private void SelectSceneRow(string cueId)
    {
        if (ScenesDataGrid.ItemsSource is not IEnumerable<SceneRow> rows)
        {
            return;
        }

        var row = rows.FirstOrDefault(candidate => string.Equals(candidate.CueId, cueId, StringComparison.Ordinal));
        if (row is null)
        {
            return;
        }

        ScenesDataGrid.SelectedItem = row;
        ScenesDataGrid.ScrollIntoView(row);
    }

    private static SubtitleCueLearningState? FindCueLearningState(SubtitleTrack track, SubtitleCue cue)
    {
        return FindCueLearningState(track, cue.Id, cue.Index);
    }

    private static SubtitleCueLearningState? FindCueLearningState(SubtitleTrack track, string cueId, int cueIndex)
    {
        return track.CueLearningStates.FirstOrDefault(state =>
            string.Equals(state.CueId, cueId, StringComparison.Ordinal)
            || state.CueIndex == cueIndex);
    }

    private static SubtitleCueLearningState EnsureCueLearningState(SubtitleTrack track, string cueId, int cueIndex)
    {
        var state = FindCueLearningState(track, cueId, cueIndex);
        if (state is not null)
        {
            if (string.IsNullOrWhiteSpace(state.CueId))
            {
                state.CueId = cueId;
            }

            return state;
        }

        state = new SubtitleCueLearningState
        {
            CueId = cueId,
            CueIndex = cueIndex
        };
        track.CueLearningStates.Add(state);
        return state;
    }

    private static bool IsFlaggedLearningState(SubtitleCueLearningState? state)
    {
        return state?.IsFlagged == true || state?.Tags.Any(IsFlagTag) == true;
    }

    private static bool HasSubtitleTags(SubtitleCueLearningState? state)
    {
        return state?.IsFlagged == true || state?.Tags.Count > 0;
    }

    private static System.Windows.Media.Brush CreateSceneRowBackground(SubtitleCueLearningState? state, string highlightColor)
    {
        return HasSubtitleTags(state)
            ? CreateBrush(highlightColor, 0x4D)
            : new SolidColorBrush(Color.FromRgb(0x0B, 0x11, 0x1A));
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

    private static bool IsFlagTag(string tag)
    {
        return string.Equals(tag, FlagTagName, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddTag(List<string> tags, string tag)
    {
        var normalized = tag.Trim();
        if (normalized.Length == 0 || tags.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        tags.Add(normalized);
    }

    private static string NormalizedTagKey(string tag)
    {
        return tag.Trim().ToLowerInvariant();
    }

    private static void MergeTagDefinitionsFromLibrary(MovieLibrary library)
    {
        foreach (var movie in library.Movies)
        {
            foreach (var tag in movie.Tags)
            {
                AddTagDefinition(library, TagScope.Movie, tag);
            }

            foreach (var state in movie.SubtitleTracks.SelectMany(track => track.CueLearningStates))
            {
                if (state.IsFlagged)
                {
                    AddTag(state.Tags, FlagTagName);
                }

                foreach (var tag in state.Tags)
                {
                    AddTagDefinition(library, TagScope.Subtitle, tag);
                }
            }
        }

        AddTagDefinition(library, TagScope.Subtitle, FlagTagName);
    }

    private static void AddTagDefinition(MovieLibrary library, TagScope scope, string tag)
    {
        var normalized = tag.Trim();
        if (normalized.Length == 0)
        {
            return;
        }

        if (library.TagDefinitions.Any(existing =>
            existing.Scope == scope
            && string.Equals(existing.Name, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        library.TagDefinitions.Add(new TagDefinition
        {
            Name = normalized,
            Scope = scope,
            SortOrder = library.TagDefinitions.Count(existing => existing.Scope == scope)
        });
    }

    private Window CreateTagManagerWindow(
        ObservableCollection<TagDefinitionRow> movieTags,
        ObservableCollection<TagDefinitionRow> subtitleTags)
    {
        var maxWindowHeight = Math.Max(360, SystemParameters.WorkArea.Height - 80);
        var window = new Window
        {
            Title = "タグ管理",
            Owner = this,
            Width = 720,
            Height = 520,
            MaxHeight = maxWindowHeight,
            MinWidth = 620,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = FindResource("PanelBrush") as System.Windows.Media.Brush,
            Foreground = System.Windows.Media.Brushes.White
        };

        var movieList = CreateTagDefinitionListBox(movieTags);
        var subtitleList = CreateTagDefinitionListBox(subtitleTags);
        var moviePanel = CreateTagScopePanel("動画タグ", movieTags, movieList);
        var subtitlePanel = CreateTagScopePanel("字幕タグ", subtitleTags, subtitleList);

        var content = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            },
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(14) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };
        content.Children.Add(moviePanel);
        Grid.SetColumn(subtitlePanel, 2);
        content.Children.Add(subtitlePanel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 3);
        var saveButton = new Button { Content = "保存", MinWidth = 86 };
        var cancelButton = new Button
        {
            Content = "キャンセル",
            MinWidth = 86,
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = FindResource("BorderBrush") as System.Windows.Media.Brush
        };
        saveButton.Click += (_, _) => window.DialogResult = true;
        cancelButton.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);
        content.Children.Add(buttons);

        window.Content = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        return window;
    }

    private static System.Windows.Controls.ListBox CreateTagDefinitionListBox(ObservableCollection<TagDefinitionRow> tags)
    {
        var listBox = new System.Windows.Controls.ListBox
        {
            ItemsSource = tags,
            DisplayMemberPath = nameof(TagDefinitionRow.Name),
            Margin = new Thickness(0, 8, 0, 8),
            MinHeight = 180
        };

        ScrollViewer.SetVerticalScrollBarVisibility(listBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Disabled);
        return listBox;
    }

    private FrameworkElement CreateTagScopePanel(
        string title,
        ObservableCollection<TagDefinitionRow> tags,
        System.Windows.Controls.ListBox listBox)
    {
        var textBox = new TextBox { Margin = new Thickness(0, 0, 8, 0) };
        var addButton = new Button { Content = "追加", MinWidth = 70 };
        var deleteButton = new Button
        {
            Content = "削除",
            MinWidth = 70,
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = FindResource("BorderBrush") as System.Windows.Media.Brush
        };

        addButton.Click += (_, _) =>
        {
            var tag = NormalizeOptionalText(textBox.Text);
            if (tag is null || tags.Any(existing => string.Equals(existing.Name, tag, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            tags.Add(new TagDefinitionRow(new TagDefinition { Name = tag, SortOrder = tags.Count }));
            textBox.Text = string.Empty;
        };
        deleteButton.Click += (_, _) =>
        {
            if (listBox.SelectedItem is TagDefinitionRow row)
            {
                tags.Remove(row);
            }
        };

        var panel = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            }
        };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold });

        var addRow = new Grid
        {
            Margin = new Thickness(0, 10, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        addRow.Children.Add(textBox);
        Grid.SetColumn(addButton, 1);
        addRow.Children.Add(addButton);
        Grid.SetRow(addRow, 1);
        panel.Children.Add(addRow);

        Grid.SetRow(listBox, 2);
        panel.Children.Add(listBox);
        Grid.SetRow(deleteButton, 3);
        deleteButton.HorizontalAlignment = HorizontalAlignment.Right;
        panel.Children.Add(deleteButton);
        return panel;
    }

    private static List<string> ParseTags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Split([',', '、', '，', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizeOptionalText(string? text)
    {
        var normalized = text?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private string ResolveGenerationVideoPath(Movie movie)
    {
        if (!string.IsNullOrWhiteSpace(movie.Video.SourceUri)
            && File.Exists(movie.Video.SourceUri))
        {
            return movie.Video.SourceUri;
        }

        if (!string.IsNullOrWhiteSpace(movie.Video.CachePath)
            && File.Exists(movie.Video.CachePath))
        {
            return movie.Video.CachePath;
        }

        throw new FileNotFoundException("字幕生成に使える動画ファイルが見つかりません。", movie.Video.FileName);
    }

    private string GetMovieThumbnailPath(string movieId)
    {
        Directory.CreateDirectory(_paths.ThumbnailCachePath);
        return Path.Combine(_paths.ThumbnailCachePath, $"{SanitizeFileName(movieId)}.jpg");
    }

    private static async Task CreateThumbnailAsync(string videoPath, string outputPath, TimeSpan position)
    {
        var ffmpegPath = ResolveFfmpegPath();
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = outputPath + ".tmp.jpg";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add(Math.Max(0, position.TotalSeconds).ToString("0.###", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(videoPath);
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add("scale=640:-2:force_original_aspect_ratio=decrease");
        startInfo.ArgumentList.Add("-q:v");
        startInfo.ArgumentList.Add("3");
        startInfo.ArgumentList.Add(tempPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("ffmpegを起動できませんでした。");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw new TimeoutException("ffmpegのサムネイル作成がタイムアウトしました。動画ファイルまたは指定位置を確認してください。");
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0 || !File.Exists(tempPath))
        {
            var message = string.Join(
                Environment.NewLine,
                new[] { error, output }.Where(text => !string.IsNullOrWhiteSpace(text)));
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(message)
                    ? "ffmpegがサムネイルを作成できませんでした。"
                    : message.Trim());
        }

        File.Move(tempPath, outputPath, overwrite: true);
    }

    private static string ResolveFfmpegPath()
    {
        foreach (var envName in new[] { "COFFEEMOVIE_FFMPEG_PATH", "FFMPEG_PATH" })
        {
            var configuredPath = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            {
                return configuredPath;
            }
        }

        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var directory in paths)
        {
            var candidate = Path.Combine(directory, "ffmpeg.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "ffmpeg.exe が見つかりません。PATHにffmpegを追加するか、COFFEEMOVIE_FFMPEG_PATH に ffmpeg.exe のフルパスを設定してください。");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private string ResolveEnglishSubtitlePath(Movie movie, string outputDirectory, string baseName)
    {
        var outputCandidate = Path.Combine(outputDirectory, baseName + ".en.srt");
        if (File.Exists(outputCandidate))
        {
            return outputCandidate;
        }

        var selectedTrackPath = _previewSubtitleTrack is not null
            && movie.SubtitleTracks.Any(track => string.Equals(track.Id, _previewSubtitleTrack.Id, StringComparison.Ordinal))
            && IsEnglishSubtitleTrack(_previewSubtitleTrack)
                ? ResolveSubtitleTrackFilePath(_previewSubtitleTrack)
                : null;
        if (selectedTrackPath is not null)
        {
            return selectedTrackPath;
        }

        foreach (var track in movie.SubtitleTracks.Where(IsEnglishSubtitleTrack))
        {
            if (ResolveSubtitleTrackFilePath(track) is { } trackPath)
            {
                return trackPath;
            }
        }

        throw new FileNotFoundException("日本語訳に使う英語字幕(.en.srt)が見つかりません。先に英語字幕を生成するか、英語字幕トラックを取り込んでください。", outputCandidate);
    }

    private static bool IsEnglishSubtitleTrack(SubtitleTrack track)
    {
        return track.Role == SubtitleTrackRole.LearningTarget
            || string.Equals(track.Language, "en", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveSubtitleTrackFilePath(SubtitleTrack track)
    {
        if (!string.IsNullOrWhiteSpace(track.SourceUri))
        {
            if (File.Exists(track.SourceUri))
            {
                return track.SourceUri;
            }

            if (Uri.TryCreate(track.SourceUri, UriKind.Absolute, out var sourceUri)
                && sourceUri.IsFile
                && File.Exists(sourceUri.LocalPath))
            {
                return sourceUri.LocalPath;
            }
        }

        return !string.IsNullOrWhiteSpace(track.LocalPath) && File.Exists(track.LocalPath)
            ? track.LocalPath
            : null;
    }

    private static string EnsureFileAvailableInWorkingDirectory(string sourcePath, string workingDirectory)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var workingFullPath = NormalizeDirectoryPath(workingDirectory);
        var sourceDirectory = Path.GetDirectoryName(sourceFullPath);
        if (sourceDirectory is not null
            && string.Equals(NormalizeDirectoryPath(sourceDirectory), workingFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return sourceFullPath;
        }

        var destinationPath = Path.Combine(workingFullPath, Path.GetFileName(sourceFullPath));
        if (!string.Equals(sourceFullPath, Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourceFullPath, destinationPath, overwrite: true);
        }

        return destinationPath;
    }

    private static string FormatExternalProcessPath(string path, string workingDirectory, bool preferRelativePath)
    {
        if (!preferRelativePath)
        {
            return path;
        }

        var relativePath = Path.GetRelativePath(workingDirectory, path);
        return IsSafeRelativePath(relativePath)
            ? relativePath
            : path;
    }

    private static string FormatExternalProcessDirectory(string path, string workingDirectory, bool preferRelativePath)
    {
        if (!preferRelativePath)
        {
            return path;
        }

        var relativePath = Path.GetRelativePath(workingDirectory, path);
        if (relativePath == ".")
        {
            return ".";
        }

        return IsSafeRelativePath(relativePath)
            ? relativePath
            : path;
    }

    private static bool IsSafeRelativePath(string path)
    {
        return !Path.IsPathRooted(path)
            && path != ".."
            && !path.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !path.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private string GetDefaultSubtitleGenerationDirectory(Movie? movie = null)
    {
        if (!string.IsNullOrWhiteSpace(WhisperOutputDirectoryTextBox.Text))
        {
            return WhisperOutputDirectoryTextBox.Text;
        }

        const string knownWorkspace = @"D:\英語\subtitile";
        if (Directory.Exists(knownWorkspace))
        {
            return knownWorkspace;
        }

        if (!string.IsNullOrWhiteSpace(movie?.Video.SourceUri)
            && File.Exists(movie.Video.SourceUri)
            && Path.GetDirectoryName(movie.Video.SourceUri) is { } sourceDirectory)
        {
            return sourceDirectory;
        }

        if (!string.IsNullOrWhiteSpace(movie?.Video.CachePath)
            && Path.GetDirectoryName(movie.Video.CachePath) is { } cacheDirectory)
        {
            return cacheDirectory;
        }

        return _paths.SubtitlePath;
    }

    private static string SelectedComboText(System.Windows.Controls.ComboBox comboBox, string fallback)
    {
        return comboBox.SelectedItem is ComboBoxItem item
            ? item.Content?.ToString() ?? fallback
            : fallback;
    }

    private static void SelectComboBoxItem(System.Windows.Controls.ComboBox comboBox, string? value, string fallback)
    {
        var target = string.IsNullOrWhiteSpace(value) ? fallback : value;
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), target, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), fallback, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private static string ApplyArgumentTemplate(string template, IReadOnlyDictionary<string, string> replacements)
    {
        var result = template;
        foreach (var (key, value) in replacements)
        {
            result = result.Replace("{" + key + "}", value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static ProcessStartInfo CreateTranslationProcessStartInfo(
        string translationCommand,
        IReadOnlyList<string> translationArguments,
        string workingDirectory,
        string? codexModel = null)
    {
        var fileName = translationCommand;
        var arguments = translationArguments;
        if (IsCodexSparkCommand(translationCommand))
        {
            fileName = ResolveCodexExecutable();
            arguments = EnsureCodexExecArguments(translationArguments, codexModel);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
            WorkingDirectory = Directory.Exists(workingDirectory)
                ? workingDirectory
                : Environment.CurrentDirectory
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        return startInfo;
    }

    private static bool IsCodexSparkCommand(string command)
    {
        return string.Equals(command, DefaultTranslationCommand, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> EnsureCodexExecArguments(IReadOnlyList<string> arguments, string? codexModel)
    {
        List<string> result;
        if (arguments.Count > 0
            && string.Equals(arguments[0], "exec", StringComparison.OrdinalIgnoreCase))
        {
            result = arguments.ToList();
        }
        else
        {
            result = ["exec", .. arguments];
        }

        var model = NormalizeOptionalText(codexModel) ?? DefaultCodexSparkModel;
        if (!HasCodexModelArgument(result))
        {
            result.Insert(1, model);
            result.Insert(1, "-m");
        }

        return result;
    }

    private static bool HasCodexModelArgument(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (string.Equals(argument, "-m", StringComparison.OrdinalIgnoreCase)
                || string.Equals(argument, "--model", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("--model=", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveCodexExecutable()
    {
        var configuredPath = TryReadCodexCliPathFromConfig();
        if (configuredPath is not null)
        {
            return configuredPath;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, "codex.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "codex";
    }

    private static string? TryReadCodexCliPathFromConfig()
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "config.toml");
        if (!File.Exists(configPath))
        {
            return null;
        }

        foreach (var line in File.ReadLines(configPath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("CODEX_CLI_PATH", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            var value = trimmed[(separatorIndex + 1)..].Trim().Trim('"', '\'');
            if (File.Exists(value))
            {
                return value;
            }
        }

        return null;
    }

    private static void BackupExistingFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileName(path);
        var backupPath = Path.Combine(directory, $"{name}.{DateTime.Now:yyyyMMddHHmmss}.bak");
        File.Move(path, backupPath);
    }

    private static DateTime PrepareGeneratedOutputPath(string path)
    {
        var startedAtUtc = DateTime.UtcNow;
        BackupExistingFile(path);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return startedAtUtc;
    }

    private static void EnsureGeneratedOutputIsFresh(string path, DateTime startedAtUtc, string message)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(message, path);
        }

        var lastWriteUtc = File.GetLastWriteTimeUtc(path);
        if (lastWriteUtc < startedAtUtc.AddSeconds(-2))
        {
            throw new InvalidOperationException(
                $"{message} The existing output was not updated: {path}");
        }
    }

    private async Task PumpProcessOutputAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            AppendSubtitleGenerationLog(line);
        }
    }

    private void AppendSubtitleGenerationLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendSubtitleGenerationLog(message));
            return;
        }

        SubtitleGenerationLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        SubtitleGenerationLogTextBox.ScrollToEnd();
    }

    private static string FormatProcessCommand(ProcessStartInfo startInfo)
    {
        return string.Join(
            ' ',
            new[] { QuoteCommandPart(startInfo.FileName) }.Concat(startInfo.ArgumentList.Select(QuoteCommandPart)));
    }

    private static string QuoteCommandPart(string value)
    {
        return value.Any(char.IsWhiteSpace) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;
    }

    private static List<string> SplitCommandLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var arguments = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        foreach (var character in text)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (builder.Length > 0)
                {
                    arguments.Add(builder.ToString());
                    builder.Clear();
                }

                continue;
            }

            builder.Append(character);
        }

        if (builder.Length > 0)
        {
            arguments.Add(builder.ToString());
        }

        return arguments;
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

    private void ResetPreviewIfMovieChanged(Movie? movie)
    {
        var currentPath = PreviewPlayer.Source?.LocalPath;
        var nextPath = movie?.Video.CachePath;
        if (string.Equals(currentPath, nextPath, StringComparison.OrdinalIgnoreCase)
            && (FullPreviewPlayer.Source is null
                || string.Equals(FullPreviewPlayer.Source.LocalPath, nextPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _previewTimer.Stop();
        PreviewPlayer.Stop();
        _playPreviewWhenMediaOpened = false;
        _isPreviewPlaying = false;
        _isPreviewMediaOpened = false;
        PreviewPlayer.Source = null;
        ResetPreviewSeek();
        FullPreviewPlayer.Stop();
        _playFullPreviewWhenMediaOpened = false;
        _isFullPreviewPlaying = false;
        _isFullPreviewMediaOpened = false;
        FullPreviewPlayer.Source = null;
        ResetFullPreviewSeek();
        UpdatePlaybackButtonContent();
        if (!string.IsNullOrWhiteSpace(nextPath) && File.Exists(nextPath))
        {
            PreviewPlayer.Source = new Uri(nextPath);
        }
    }

    private bool EnsurePreviewSource(Movie movie, bool playWhenReady, TimeSpan? startPosition)
    {
        if (movie.Video.CachePath is null || !File.Exists(movie.Video.CachePath))
        {
            return false;
        }

        var source = new Uri(movie.Video.CachePath);
        var isSameSource = PreviewPlayer.Source is not null
            && string.Equals(PreviewPlayer.Source.LocalPath, source.LocalPath, StringComparison.OrdinalIgnoreCase);
        if (!isSameSource)
        {
            _previewTimer.Stop();
            PreviewPlayer.Stop();
            _isPreviewPlaying = false;
            _isPreviewMediaOpened = false;
            _playPreviewWhenMediaOpened = playWhenReady;
            ResetPreviewSeek();
            _pendingPreviewSeek = startPosition;
            PreviewPlayer.Source = source;
            return false;
        }

        if (startPosition is not null)
        {
            _pendingPreviewSeek = startPosition;
        }

        if (!_isPreviewMediaOpened || _previewDuration <= TimeSpan.Zero)
        {
            _playPreviewWhenMediaOpened = _playPreviewWhenMediaOpened || playWhenReady;
            return false;
        }

        _playPreviewWhenMediaOpened = false;
        return true;
    }

    private bool EnsureFullPreviewSource(Movie movie, bool playWhenReady, TimeSpan? startPosition)
    {
        if (movie.Video.CachePath is null || !File.Exists(movie.Video.CachePath))
        {
            return false;
        }

        var source = new Uri(movie.Video.CachePath);
        var isSameSource = FullPreviewPlayer.Source is not null
            && string.Equals(FullPreviewPlayer.Source.LocalPath, source.LocalPath, StringComparison.OrdinalIgnoreCase);
        if (!isSameSource)
        {
            FullPreviewPlayer.Stop();
            _isFullPreviewPlaying = false;
            _isFullPreviewMediaOpened = false;
            _playFullPreviewWhenMediaOpened = playWhenReady;
            ResetFullPreviewSeek();
            _pendingFullPreviewSeek = startPosition;
            FullPreviewPlayer.Source = source;
            return false;
        }

        if (startPosition is not null)
        {
            _pendingFullPreviewSeek = startPosition;
        }

        if (!_isFullPreviewMediaOpened || _fullPreviewDuration <= TimeSpan.Zero)
        {
            _playFullPreviewWhenMediaOpened = _playFullPreviewWhenMediaOpened || playWhenReady;
            return false;
        }

        _playFullPreviewWhenMediaOpened = false;
        return true;
    }

    private void ResetPreviewSeek()
    {
        _previewDuration = TimeSpan.Zero;
        _pendingPreviewSeek = null;
        _isPreviewMediaOpened = false;
        _isPreviewSeeking = false;

        _isUpdatingPreviewSlider = true;
        try
        {
            PreviewSeekSlider.Minimum = 0;
            PreviewSeekSlider.Maximum = 1;
            PreviewSeekSlider.Value = 0;
            PreviewSeekSlider.IsEnabled = false;
            PreviewPositionTextBlock.Text = FormatPlaybackPosition(TimeSpan.Zero, TimeSpan.Zero);
            HidePreviewSubtitle();
        }
        finally
        {
            _isUpdatingPreviewSlider = false;
        }
    }

    private void UpdatePreviewSeekFromPlayer()
    {
        if (_isPreviewSeeking || PreviewPlayer.Source is null)
        {
            return;
        }

        var position = PreviewPlayer.Position;
        if (_previewStopAt is { } stopAt && position >= stopAt)
        {
            _previewStopAt = null;
            PreviewPlayer.Pause();
            _isPreviewPlaying = false;
            UpdatePlaybackButtonContent();
            SetStatus("サムネイル位置の5秒再生を停止しました。");
        }

        SetPreviewSeek(position);
    }

    private void BeginPreviewSeek()
    {
        if (PreviewSeekSlider.IsEnabled)
        {
            _isPreviewSeeking = true;
        }
    }

    private void CompletePreviewSeek()
    {
        if (!PreviewSeekSlider.IsEnabled)
        {
            _isPreviewSeeking = false;
            return;
        }

        SeekPreviewToSliderValue();
        _isPreviewSeeking = false;
    }

    private void SetPreviewSeek(TimeSpan position)
    {
        var maxSeconds = Math.Max(0.0, PreviewSeekSlider.Maximum);
        var seconds = Math.Clamp(position.TotalSeconds, 0.0, maxSeconds);
        var displayPosition = TimeSpan.FromSeconds(seconds);

        _isUpdatingPreviewSlider = true;
        try
        {
            PreviewSeekSlider.Value = seconds;
            PreviewPositionTextBlock.Text = FormatPlaybackPosition(displayPosition, _previewDuration);
            UpdatePreviewSubtitle(displayPosition);
        }
        finally
        {
            _isUpdatingPreviewSlider = false;
        }
    }

    private void SeekPreviewToSliderValue()
    {
        SeekPreviewTo(TimeSpan.FromSeconds(Math.Clamp(PreviewSeekSlider.Value, 0.0, PreviewSeekSlider.Maximum)));
    }

    private void SeekPreviewTo(TimeSpan position)
    {
        if (PreviewPlayer.Source is null || _previewDuration <= TimeSpan.Zero)
        {
            return;
        }

        position = ClampPreviewPosition(position);
        PreviewPlayer.Position = position;
        SetPreviewSeek(position);
    }

    private TimeSpan ClampPreviewPosition(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return position > _previewDuration ? _previewDuration : position;
    }

    private void ResetFullPreviewSeek()
    {
        _fullPreviewDuration = TimeSpan.Zero;
        _pendingFullPreviewSeek = null;
        _isFullPreviewMediaOpened = false;
        _isFullPreviewSeeking = false;

        _isUpdatingFullPreviewSlider = true;
        try
        {
            FullPreviewSeekSlider.Minimum = 0;
            FullPreviewSeekSlider.Maximum = 1;
            FullPreviewSeekSlider.Value = 0;
            FullPreviewSeekSlider.IsEnabled = false;
            FullPreviewPositionTextBlock.Text = FormatPlaybackPosition(TimeSpan.Zero, TimeSpan.Zero);
            HideFullPreviewSubtitle();
        }
        finally
        {
            _isUpdatingFullPreviewSlider = false;
        }
    }

    private void UpdateFullPreviewSeekFromPlayer()
    {
        if (_isFullPreviewSeeking || FullPreviewPlayer.Source is null)
        {
            return;
        }

        SetFullPreviewSeek(FullPreviewPlayer.Position);
    }

    private void BeginFullPreviewSeek()
    {
        if (FullPreviewSeekSlider.IsEnabled)
        {
            _isFullPreviewSeeking = true;
        }
    }

    private void CompleteFullPreviewSeek()
    {
        if (!FullPreviewSeekSlider.IsEnabled)
        {
            _isFullPreviewSeeking = false;
            return;
        }

        SeekFullPreviewToSliderValue();
        _isFullPreviewSeeking = false;
    }

    private void SetFullPreviewSeek(TimeSpan position)
    {
        var maxSeconds = Math.Max(0.0, FullPreviewSeekSlider.Maximum);
        var seconds = Math.Clamp(position.TotalSeconds, 0.0, maxSeconds);
        var displayPosition = TimeSpan.FromSeconds(seconds);

        _isUpdatingFullPreviewSlider = true;
        try
        {
            FullPreviewSeekSlider.Value = seconds;
            FullPreviewPositionTextBlock.Text = FormatPlaybackPosition(displayPosition, _fullPreviewDuration);
            UpdateFullPreviewSubtitle(displayPosition);
        }
        finally
        {
            _isUpdatingFullPreviewSlider = false;
        }
    }

    private void SeekFullPreviewToSliderValue()
    {
        SeekFullPreviewTo(TimeSpan.FromSeconds(Math.Clamp(FullPreviewSeekSlider.Value, 0.0, FullPreviewSeekSlider.Maximum)));
    }

    private void SeekFullPreviewTo(TimeSpan position)
    {
        if (FullPreviewPlayer.Source is null || _fullPreviewDuration <= TimeSpan.Zero)
        {
            return;
        }

        position = ClampFullPreviewPosition(position);
        FullPreviewPlayer.Position = position;
        SetFullPreviewSeek(position);
    }

    private TimeSpan ClampFullPreviewPosition(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return position > _fullPreviewDuration ? _fullPreviewDuration : position;
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

    private static string NormalizePreviewSubtitleText(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private sealed class MovieListItem
    {
        public MovieListItem(Movie movie)
        {
            MovieId = movie.Id;
            Title = movie.Title;
            Detail = $"{movie.SubtitleTracks.Count} subtitle / {movie.SceneMarkers.Count} scene";
            CacheState = movie.Video.HasLocalCache ? "cached" : "not cached";
            ThumbnailPath = !string.IsNullOrWhiteSpace(movie.Video.ThumbnailPath) && File.Exists(movie.Video.ThumbnailPath)
                ? movie.Video.ThumbnailPath
                : null;
        }

        public string MovieId { get; }

        public string Title { get; }

        public string Detail { get; }

        public string CacheState { get; }

        public string? ThumbnailPath { get; }
    }

    private sealed class SubtitleRow
    {
        public SubtitleRow(SubtitleTrack track)
        {
            TrackId = track.Id;
            Label = track.Label;
            Language = track.Language ?? string.Empty;
            Role = track.Role.ToString();
            Format = track.Format.ToString();
            CueCount = track.CueCount;
        }

        public string TrackId { get; }

        public string Label { get; }

        public string Language { get; }

        public string Role { get; }

        public string Format { get; }

        public int CueCount { get; }
    }

    private sealed record PreviewSubtitleLine(SubtitleTrack Track, SubtitleCue Cue);

    private enum PreviewOverlayKind
    {
        EnglishSubtitle,
        JapaneseSubtitle,
        AiNote,
        UserNote
    }

    private enum OverlaySide
    {
        Above,
        Below
    }

    private sealed record OverlayPlacement(OverlaySide Side, int Order);

    private sealed record PositionedOverlayItem(PreviewOverlayItem Item, OverlayPlacement Placement);

    private sealed record PreviewOverlayItem(
        PreviewOverlayKind Kind,
        string Text,
        string Position,
        bool HasHighlight)
    {
        public int SortPriority => Kind switch
        {
            PreviewOverlayKind.EnglishSubtitle => 0,
            PreviewOverlayKind.JapaneseSubtitle => 1,
            PreviewOverlayKind.AiNote => 2,
            PreviewOverlayKind.UserNote => 3,
            _ => 4
        };
    }

    private sealed record LearningNoteImportRow(int Index, string? Cefr, string? Note);

    private sealed class TagDefinitionRow
    {
        public TagDefinitionRow(TagDefinition tag)
        {
            Name = tag.Name;
            SortOrder = tag.SortOrder;
            CreatedAt = tag.CreatedAt;
        }

        public string Name { get; }

        public int SortOrder { get; }

        public DateTimeOffset CreatedAt { get; }

        public TagDefinition ToDefinition(TagScope scope, int index)
        {
            return new TagDefinition
            {
                Name = Name.Trim(),
                Scope = scope,
                SortOrder = index,
                CreatedAt = CreatedAt
            };
        }
    }

    private sealed class SceneRow
    {
        public SceneRow(SubtitleCue cue, SubtitleCueLearningState? learningState, System.Windows.Media.Brush rowBackgroundBrush)
        {
            CueId = cue.Id;
            CueIndex = cue.Index;
            Start = cue.Start;
            End = cue.End;
            Timestamp = FormatCueEditTimestamp(cue.Start);
            EndTimestamp = FormatCueEditTimestamp(cue.End);
            Label = CompactCueText(cue.Text);
            IsFlagged = IsFlaggedLearningState(learningState);
            ListeningAccuracy = FormatAccuracy(learningState?.Listening.LastAccuracy);
            ShadowingAccuracy = FormatAccuracy(learningState?.Shadowing.LastAccuracy);
            Tags = learningState is null ? string.Empty : string.Join(", ", learningState.Tags);
            AiNote = learningState?.AiNote ?? string.Empty;
            Note = learningState?.Note ?? string.Empty;
            RowBackgroundBrush = rowBackgroundBrush;
        }

        public string CueId { get; }

        public int CueIndex { get; }

        public TimeSpan Start { get; }

        public TimeSpan End { get; }

        public string Timestamp { get; set; }

        public string EndTimestamp { get; set; }

        public string Label { get; }

        public bool IsFlagged { get; set; }

        public string ListeningAccuracy { get; }

        public string ShadowingAccuracy { get; }

        public string Tags { get; set; }

        public string AiNote { get; }

        public string Note { get; set; }

        public System.Windows.Media.Brush RowBackgroundBrush { get; }

        private static string CompactCueText(string text)
        {
            var normalized = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            return normalized.Length <= 120 ? normalized : normalized[..117] + "...";
        }

        private static string FormatAccuracy(double? value)
        {
            return value is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{value.Value:P0}");
        }
    }
}

