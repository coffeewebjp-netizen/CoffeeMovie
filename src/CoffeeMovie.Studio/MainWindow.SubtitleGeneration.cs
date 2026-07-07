using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Services;
using CoffeeMovie.Studio.Services;
using Microsoft.Win32;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
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

    private async Task<string> GenerateEnglishSubtitleAsync(Movie movie)
    {
        var videoPath = ResolveGenerationVideoPath(movie);
        var outputDirectory = NormalizeOptionalText(WhisperOutputDirectoryTextBox.Text)
            ?? GetDefaultSubtitleGenerationDirectory(movie);
        var options = new EnglishSubtitleGenerationOptions(
            videoPath,
            outputDirectory,
            OverwriteGeneratedSubtitleCheckBox.IsChecked == true,
            NormalizeOptionalText(WhisperPythonCommandTextBox.Text) ?? "py",
            WhisperPythonArgumentsTextBox.Text,
            NormalizeOptionalText(WhisperModelTextBox.Text) ?? "medium",
            NormalizeOptionalText(WhisperLanguageTextBox.Text) ?? "en",
            SelectedComboText(WhisperDeviceComboBox, "cuda"),
            SelectedComboText(WhisperComputeTypeComboBox, "float16"),
            string.Equals(
                SelectedComboValue(EnglishSubtitleGenerationModeComboBox, "normal"),
                "review",
                StringComparison.OrdinalIgnoreCase)
                    ? EnglishSubtitleGenerationMode.Review
                    : EnglishSubtitleGenerationMode.Normal,
            movie.Title,
            NormalizeOptionalText(TranslationCommandTextBox.Text) ?? DefaultTranslationCommand,
            DefaultEnglishSubtitleReviewArguments,
            DefaultEnglishSubtitleReviewPrompt,
            NormalizeOptionalText(TranslationModelTextBox.Text) ?? DefaultCodexSparkModel,
            DefaultTranslationCommand,
            DefaultCodexSparkModel);

        return await _subtitleGenerationJobService.GenerateEnglishSubtitleAsync(
            options,
            AppendSubtitleGenerationLog);
    }

    private async Task<string> GenerateJapaneseSubtitleAsync(Movie movie, string? englishSrtPath = null)
    {
        var videoPath = ResolveGenerationVideoPath(movie);
        var outputDirectory = NormalizeOptionalText(WhisperOutputDirectoryTextBox.Text)
            ?? GetDefaultSubtitleGenerationDirectory(movie);
        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        englishSrtPath ??= ResolveEnglishSubtitlePath(movie, outputDirectory, baseName);

        var translationCommand = NormalizeOptionalText(TranslationCommandTextBox.Text) ?? DefaultTranslationCommand;
        var argumentTemplate = NormalizeOptionalText(TranslationArgumentsTextBox.Text)
            ?? DefaultTranslationArguments;

        var options = new JapaneseSubtitleGenerationOptions(
            movie.Title,
            videoPath,
            englishSrtPath,
            outputDirectory,
            OverwriteJapaneseSubtitleCheckBox.IsChecked == true,
            translationCommand,
            argumentTemplate,
            NormalizeOptionalText(TranslationPromptTextBox.Text) ?? DefaultTranslationPrompt,
            NormalizeOptionalText(TranslationSourceLanguageTextBox.Text) ?? "en",
            NormalizeOptionalText(TranslationTargetLanguageTextBox.Text) ?? "ja",
            NormalizeOptionalText(TranslationModelTextBox.Text) ?? DefaultCodexSparkModel,
            DefaultTranslationCommand,
            DefaultTranslationArguments,
            DefaultCodexSparkModel);

        return await _subtitleGenerationJobService.GenerateJapaneseSubtitleAsync(
            options,
            AppendSubtitleGenerationLog);
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

        throw new FileNotFoundException(
            "日本語訳に使う英語字幕(.en.srt)が見つかりません。先に英語字幕を生成するか、英語字幕トラックを取り込んでください。",
            outputCandidate);
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

    private static string SelectedComboValue(System.Windows.Controls.ComboBox comboBox, string fallback)
    {
        return comboBox.SelectedValue?.ToString() ?? fallback;
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

    private static void SelectComboBoxValue(System.Windows.Controls.ComboBox comboBox, string? value, string fallback)
    {
        var target = string.IsNullOrWhiteSpace(value) ? fallback : value;
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), target, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), fallback, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
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
}
