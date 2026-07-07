using System.Text.RegularExpressions;
using System.Windows;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Core.Services;
using CoffeeMovie.Studio.Services;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
    private static readonly Regex CefrPattern = new(
        @"\b(?:CEFR|CERF)\s*[:：・-]?\s*(A1|A2|B1|B2|C1|C2)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private async void OnConfigureCoffeeLearningClicked(object sender, RoutedEventArgs e)
    {
        await ConfigureCoffeeLearningAsync();
    }

    private async void OnRegisterCoffeeLearningClicked(object sender, RoutedEventArgs e)
    {
        await RegisterCurrentPreviewCueInCoffeeLearningAsync();
    }

    private async Task RegisterCurrentPreviewCueInCoffeeLearningAsync()
    {
        if (_selectedMovie is null)
        {
            SetStatus("CoffeeLearningに登録する動画が選択されていません。");
            return;
        }

        var englishTrack = FindEnglishTrack(_selectedMovie)
            ?? (_previewSubtitleTrack is not null && IsEnglishTrack(_previewSubtitleTrack) ? _previewSubtitleTrack : null);
        if (englishTrack is null)
        {
            SetStatus("CoffeeLearningに登録する英語字幕が見つかりません。");
            return;
        }

        var position = GetActivePreviewPosition();
        var englishCue = FindActiveCue(englishTrack, position);
        if (englishCue is null)
        {
            SetStatus("現在位置にCoffeeLearningへ登録できる英語字幕がありません。");
            return;
        }

        await RegisterPreviewCueInCoffeeLearningAsync(new PreviewOverlayItem(
            PreviewOverlayKind.EnglishSubtitle,
            CollapseWhitespace(englishCue.Text),
            _englishSubtitleOverlayPosition,
            HasSubtitleTags(FindCueLearningState(englishTrack, englishCue)),
            englishTrack.Id,
            englishCue.Id,
            englishCue.Index,
            englishCue.Start,
            IsCoffeeLearningRegistered(FindCueLearningState(englishTrack, englishCue))));
    }

    private async Task<bool> ConfigureCoffeeLearningAsync()
    {
        var current = CreateCoffeeLearningSettings(_currentLibrary.Studio);
        var window = new CoffeeLearningSettingsWindow(current)
        {
            Owner = this
        };

        if (window.ShowDialog() != true || window.Settings is null)
        {
            return false;
        }

        var settings = window.Settings;
        var library = await _libraryStore.LoadAsync();
        library.Studio.CoffeeLearningBaseUrl = settings.BaseUrl;
        library.Studio.CoffeeLearningDeckId = settings.DeckId;
        library.Studio.CoffeeLearningAuthHeader = settings.AuthHeader;
        library.Studio.CoffeeLearningScoringMode = NormalizeCoffeeLearningScoringMode(settings.ScoringMode);
        library.Studio.CoffeeLearningScoringAiAgentCommand = settings.ScoringAiAgentCommand;
        library.Studio.CoffeeLearningScoringAiAgentModel = settings.ScoringAiAgentModel;
        library.Studio.CoffeeLearningScoringAiAgentArguments = settings.ScoringAiAgentArguments ?? CoffeeLearningScoringDefaults.DefaultAiAgentArguments;
        library.Studio.CoffeeLearningScoringProvider = NormalizeCoffeeLearningScoringProvider(settings.ScoringProvider);
        library.Studio.CoffeeLearningScoringProviderBaseUrl = settings.ScoringProviderBaseUrl;
        library.Studio.CoffeeLearningScoringProviderModel = settings.ScoringProviderModel;
        library.Studio.CoffeeLearningScoringProviderApiKey = settings.ScoringProviderApiKey;
        await _libraryStore.SaveAsync(library);
        _currentLibrary = library;
        SetStatus("CoffeeLearning設定を保存しました。");
        return CoffeeLearningWordRegistrationService.IsConfigured(settings);
    }

    private async Task RegisterPreviewCueInCoffeeLearningAsync(PreviewOverlayItem item)
    {
        if (_selectedMovie is null)
        {
            SetStatus("CoffeeLearningに登録する動画が選択されていません。");
            return;
        }

        var context = CreateCoffeeLearningRegistrationContext(_selectedMovie, item);
        if (context is null)
        {
            SetStatus("CoffeeLearningに登録する英語字幕が見つかりません。");
            return;
        }

        var state = EnsureCueLearningState(context.EnglishTrack, context.EnglishCue.Id, context.EnglishCue.Index);
        if (IsCoffeeLearningRegistered(state))
        {
            SetStatus("この字幕はCoffeeLearningに登録済みです。");
            UpdatePreviewSubtitleAtCurrentPosition();
            return;
        }

        var word = CollapseWhitespace(context.EnglishCue.Text);
        if (string.IsNullOrWhiteSpace(word))
        {
            SetStatus("CoffeeLearningに登録する英語字幕が空です。");
            return;
        }

        var meaning = CollapseWhitespace(context.JapaneseCue?.Text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(meaning))
        {
            var prompt = new TextPromptWindow(
                "CoffeeLearning",
                "日本語訳が見つかりません。意味を入力してください。")
            {
                Owner = this
            };
            if (prompt.ShowDialog() != true || string.IsNullOrWhiteSpace(prompt.Response))
            {
                return;
            }

            meaning = prompt.Response.Trim();
        }

        if (!await EnsureCoffeeLearningConfiguredAsync())
        {
            return;
        }

        var settings = CreateCoffeeLearningSettings(_currentLibrary.Studio);
        var memo = BuildCoffeeLearningMemo(state);
        var cefr = ExtractCefr(memo);
        var fallbackScore = CoffeeLearningWordScoreEstimator.Estimate(cefr, word);
        var labelNames = BuildCoffeeLearningLabelNames(_selectedMovie, state);
        var scoring = await ScoreCoffeeLearningWordForRegistrationAsync(
            settings,
            word,
            meaning,
            memo,
            labelNames,
            fallbackScore);
        var registrationMemo = CoffeeLearningRegistrationMemoBuilder.Build(memo, scoring.Score);

        SetStatus("CoffeeLearningへ登録中...");
        try
        {
            var result = await _coffeeLearningService.RegisterWordAsync(
                settings,
                new CoffeeLearningWordRegistrationRequest(
                    word,
                    meaning,
                    registrationMemo,
                    scoring.Score.Cefr,
                    labelNames,
                    scoring.Score.Point,
                    scoring.AutoAnalyze));
            state.CoffeeLearningRegisteredAt = DateTimeOffset.UtcNow;
            state.CoffeeLearningWordId = string.IsNullOrWhiteSpace(result.WordId)
                ? null
                : result.WordId.Trim();
            state.CoffeeLearningDeckId = string.IsNullOrWhiteSpace(result.DeckId)
                ? settings.DeckId?.Trim()
                : result.DeckId.Trim();
            state.UpdatedAt = DateTimeOffset.UtcNow;

            await _libraryStore.UpsertMovieAsync(_selectedMovie);
            RenderSceneRows(_previewSubtitleTrack);
            UpdatePreviewSubtitleAtCurrentPosition();
            UpdateFullPreviewSubtitle(FullPreviewPlayer.Position);
            SyncPreviewPopupFromActiveSurface(forceSeek: true);
            SetStatus("CoffeeLearningに登録しました。");
        }
        catch (Exception ex)
        {
            ShowError("CoffeeLearning登録に失敗しました", ex);
        }
    }

    private async Task<bool> EnsureCoffeeLearningConfiguredAsync()
    {
        var settings = CreateCoffeeLearningSettings(_currentLibrary.Studio);
        if (CoffeeLearningWordRegistrationService.IsConfigured(settings))
        {
            return true;
        }

        var result = MessageBox.Show(
            this,
            "CoffeeLearningのAPI URL / deckId / Authorization header が未設定です。設定しますか？",
            "CoffeeLearning",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        return result == MessageBoxResult.Yes && await ConfigureCoffeeLearningAsync();
    }

    private static CoffeeLearningConnectionSettings CreateCoffeeLearningSettings(CoffeeMovie.Storage.Models.StudioPreferences preferences)
    {
        return new CoffeeLearningConnectionSettings(
            string.IsNullOrWhiteSpace(preferences.CoffeeLearningBaseUrl)
                ? CoffeeLearningWordRegistrationService.DefaultBaseUrl
                : preferences.CoffeeLearningBaseUrl,
            string.IsNullOrWhiteSpace(preferences.CoffeeLearningDeckId)
                ? CoffeeLearningWordRegistrationService.DefaultDeckId
                : preferences.CoffeeLearningDeckId,
            preferences.CoffeeLearningAuthHeader,
            NormalizeCoffeeLearningScoringMode(preferences.CoffeeLearningScoringMode),
            string.IsNullOrWhiteSpace(preferences.CoffeeLearningScoringAiAgentCommand)
                ? CoffeeLearningScoringDefaults.DefaultAiAgentCommand
                : preferences.CoffeeLearningScoringAiAgentCommand,
            string.IsNullOrWhiteSpace(preferences.CoffeeLearningScoringAiAgentModel)
                ? CoffeeLearningScoringDefaults.DefaultAiAgentModel
                : preferences.CoffeeLearningScoringAiAgentModel,
            string.IsNullOrWhiteSpace(preferences.CoffeeLearningScoringAiAgentArguments)
                ? CoffeeLearningScoringDefaults.DefaultAiAgentArguments
                : preferences.CoffeeLearningScoringAiAgentArguments,
            NormalizeCoffeeLearningScoringProvider(preferences.CoffeeLearningScoringProvider),
            preferences.CoffeeLearningScoringProviderBaseUrl,
            preferences.CoffeeLearningScoringProviderModel,
            preferences.CoffeeLearningScoringProviderApiKey);
    }

    private CoffeeLearningRegistrationContext? CreateCoffeeLearningRegistrationContext(Movie movie, PreviewOverlayItem item)
    {
        if (item.Kind == PreviewOverlayKind.EnglishSubtitle
            && FindPreviewOverlayTrack(movie, item) is { } clickedTrack
            && FindPreviewOverlayCue(clickedTrack, item) is { } clickedCue)
        {
            return new CoffeeLearningRegistrationContext(
                clickedTrack,
                clickedCue,
                FindJapaneseCueForEnglishCue(movie, clickedCue));
        }

        var position = item.CueStart ?? GetActivePreviewPosition();
        var englishTrack = FindEnglishTrack(movie)
            ?? (_previewSubtitleTrack is not null && IsEnglishTrack(_previewSubtitleTrack) ? _previewSubtitleTrack : null);
        if (englishTrack is null)
        {
            return null;
        }

        var englishCue = FindActiveCue(englishTrack, position)
            ?? englishTrack.Cues.FirstOrDefault(cue => cue.Index == item.CueIndex)
            ?? englishTrack.Cues
                .OrderBy(cue => Math.Abs((cue.Start - position).TotalSeconds))
                .FirstOrDefault();
        return englishCue is null
            ? null
            : new CoffeeLearningRegistrationContext(
                englishTrack,
                englishCue,
                FindJapaneseCueForEnglishCue(movie, englishCue));
    }

    private static SubtitleTrack? FindPreviewOverlayTrack(Movie movie, PreviewOverlayItem item)
    {
        return string.IsNullOrWhiteSpace(item.TrackId)
            ? null
            : movie.SubtitleTracks.FirstOrDefault(track => string.Equals(track.Id, item.TrackId, StringComparison.Ordinal));
    }

    private static SubtitleCue? FindPreviewOverlayCue(SubtitleTrack track, PreviewOverlayItem item)
    {
        return track.Cues.FirstOrDefault(cue =>
            string.Equals(cue.Id, item.CueId, StringComparison.Ordinal)
            || cue.Index == item.CueIndex);
    }

    private TimeSpan GetActivePreviewPosition()
    {
        if (FullPreviewTabItem.IsSelected)
        {
            return GetFullPreviewTimelinePosition();
        }

        return GetPreviewTimelinePosition();
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
        return track.Role == SubtitleTrackRole.LearningTarget
            || fileName.Contains(".en.", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".en.srt", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".eng.srt", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJapaneseTrack(SubtitleTrack track)
    {
        var language = track.Language?.Trim().ToLowerInvariant();
        if (language is "ja" or "jpn" or "jp")
        {
            return true;
        }

        var fileName = track.SourceFileName.ToLowerInvariant();
        return fileName.Contains(".ja.", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".jp.", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".ja.srt", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".jpn.srt", StringComparison.OrdinalIgnoreCase);
    }

    private static SubtitleCue? FindJapaneseCueForEnglishCue(Movie movie, SubtitleCue englishCue)
    {
        var track = FindJapaneseTrack(movie);
        if (track is null || track.Cues.Count == 0)
        {
            return null;
        }

        var center = englishCue.Start + TimeSpan.FromTicks(Math.Max(0, (englishCue.End - englishCue.Start).Ticks / 2));
        var centerCue = track.Cues.FirstOrDefault(cue => cue.Start <= center && center <= cue.End);
        if (centerCue is not null)
        {
            return centerCue;
        }

        var overlapCue = track.Cues
            .Select(cue => new { Cue = cue, Overlap = GetOverlap(cue, englishCue) })
            .Where(item => item.Overlap > TimeSpan.Zero)
            .OrderByDescending(item => item.Overlap)
            .Select(item => item.Cue)
            .FirstOrDefault();
        if (overlapCue is not null)
        {
            return overlapCue;
        }

        return track.Cues.FirstOrDefault(cue => cue.Index == englishCue.Index)
            ?? track.Cues
                .OrderBy(cue => Math.Abs((cue.Start - englishCue.Start).TotalSeconds))
                .FirstOrDefault();
    }

    private static TimeSpan GetOverlap(SubtitleCue left, SubtitleCue right)
    {
        var start = left.Start > right.Start ? left.Start : right.Start;
        var end = left.End < right.End ? left.End : right.End;
        return end > start ? end - start : TimeSpan.Zero;
    }

    private static bool IsCoffeeLearningRegistered(SubtitleCueLearningState? state)
    {
        return state?.CoffeeLearningRegisteredAt is not null
            || !string.IsNullOrWhiteSpace(state?.CoffeeLearningWordId);
    }

    private static string[] BuildCoffeeLearningLabelNames(Movie movie, SubtitleCueLearningState state)
    {
        var labels = new List<string>();
        foreach (var tag in movie.Tags)
        {
            AddTag(labels, tag);
        }

        foreach (var tag in state.Tags)
        {
            AddTag(labels, tag);
        }

        if (state.IsFlagged)
        {
            AddTag(labels, FlagTagName);
        }

        return labels.ToArray();
    }
    private static string BuildCoffeeLearningMemo(SubtitleCueLearningState state)
    {
        var parts = new[] { state.AiNote, state.Note }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => CollapseWhitespace(part!))
            .ToArray();
        return string.Join("\n", parts);
    }

    private static string? ExtractCefr(string memo)
    {
        if (string.IsNullOrWhiteSpace(memo))
        {
            return null;
        }

        var match = CefrPattern.Match(memo);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }


    private async Task<CoffeeLearningRegistrationScore> ScoreCoffeeLearningWordForRegistrationAsync(
        CoffeeLearningConnectionSettings settings,
        string word,
        string meaning,
        string memo,
        IReadOnlyList<string> labelNames,
        CoffeeLearningWordScore fallbackScore)
    {
        var mode = NormalizeCoffeeLearningScoringMode(settings.ScoringMode);
        if (mode == CoffeeLearningScoringDefaults.ModeCoffeeLearning)
        {
            return new CoffeeLearningRegistrationScore(fallbackScore, AutoAnalyze: true);
        }

        if (mode == CoffeeLearningScoringDefaults.ModeSimple)
        {
            return new CoffeeLearningRegistrationScore(fallbackScore, AutoAnalyze: false);
        }

        var input = new CoffeeLearningWordScoreInput(word, meaning, memo, labelNames);
        try
        {
            SetStatus(mode == CoffeeLearningScoringDefaults.ModeAiAgent
                ? "AIAGENT scoring..."
                : "AI provider scoring...");
            var score = mode == CoffeeLearningScoringDefaults.ModeAiAgent
                ? await _coffeeLearningAiAgentScoringService.ScoreAsync(
                    new CoffeeLearningAiAgentScoringSettings(
                        settings.ScoringAiAgentCommand,
                        settings.ScoringAiAgentModel,
                        settings.ScoringAiAgentArguments),
                    input)
                : await _coffeeLearningAiProviderScoringService.ScoreAsync(
                    new CoffeeLearningAiProviderScoringSettings(
                        settings.ScoringProvider,
                        settings.ScoringProviderBaseUrl,
                        settings.ScoringProviderModel,
                        settings.ScoringProviderApiKey),
                    input);
            return new CoffeeLearningRegistrationScore(score, AutoAnalyze: false);
        }
        catch (Exception ex)
        {
            SetStatus($"AI scoring failed; simple estimate will be used. {ex.Message}");
            return new CoffeeLearningRegistrationScore(fallbackScore, AutoAnalyze: false);
        }
    }

    private static string NormalizeCoffeeLearningScoringMode(string? mode)
    {
        var value = mode?.Trim().ToLowerInvariant();
        return value switch
        {
            CoffeeLearningScoringDefaults.ModeAiProvider => CoffeeLearningScoringDefaults.ModeAiProvider,
            CoffeeLearningScoringDefaults.ModeCoffeeLearning => CoffeeLearningScoringDefaults.ModeCoffeeLearning,
            CoffeeLearningScoringDefaults.ModeSimple => CoffeeLearningScoringDefaults.ModeSimple,
            _ => CoffeeLearningScoringDefaults.ModeAiAgent
        };
    }

    private static string NormalizeCoffeeLearningScoringProvider(string? provider)
    {
        var value = provider?.Trim().ToLowerInvariant();
        return value switch
        {
            "gpt" or "openai" => CoffeeLearningScoringDefaults.ProviderOpenAi,
            CoffeeLearningScoringDefaults.ProviderGemini => CoffeeLearningScoringDefaults.ProviderGemini,
            "deepseek" or "deepseek-chat" => CoffeeLearningScoringDefaults.ProviderDeepSeek,
            "local" or "local-llm" or "ollama" => CoffeeLearningScoringDefaults.ProviderLocal,
            _ => CoffeeLearningScoringDefaults.ProviderOpenAi
        };
    }

    private sealed record CoffeeLearningRegistrationScore(CoffeeLearningWordScore Score, bool AutoAnalyze);

    private sealed record CoffeeLearningRegistrationContext(
        SubtitleTrack EnglishTrack,
        SubtitleCue EnglishCue,
        SubtitleCue? JapaneseCue);
}