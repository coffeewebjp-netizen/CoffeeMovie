using System.Text.Json;
using System.Text.RegularExpressions;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Core.Services;
using CoffeeMovie.Reader.Services;

namespace CoffeeMovie.Reader.Pages;

public sealed partial class MoviePlayerPage
{
    private static readonly Regex CefrPattern = new(
        @"\b(?:CEFR|CERF)\s*[:：\-]?\s*(A1|A2|B1|B2|C1|C2)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private async Task RegisterCurrentCueInCoffeeLearningAsync()
    {
        if (_movie is null || _activeEnglishCue is null || _activeLearningState is null)
        {
            await DisplayAlertAsync("CoffeeLearning", "登録する英語字幕がありません。", "閉じる");
            return;
        }

        var word = CollapseWhitespace(_activeEnglishCue.Text);
        if (string.IsNullOrWhiteSpace(word))
        {
            await DisplayAlertAsync("CoffeeLearning", "登録する英語字幕が空です。", "閉じる");
            return;
        }

        if (IsCoffeeLearningRegistered(_activeLearningState))
        {
            await DisplayAlertAsync("CoffeeLearning", "この字幕はCoffeeLearningに登録済みです。", "閉じる");
            UpdateCoffeeLearningRegisterButtons(enabled: true);
            return;
        }

        var japaneseCue = FindJapaneseCueForEnglishCue(_movie, _activeEnglishCue);
        var meaning = CollapseWhitespace(japaneseCue?.Text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(meaning))
        {
            var input = await DisplayPromptAsync(
                "CoffeeLearning",
                "日本語訳が見つかりません。意味を入力してください。",
                "登録",
                "キャンセル",
                initialValue: string.Empty);
            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }

            meaning = input.Trim();
        }

        if (!await EnsureCoffeeLearningConfiguredAsync())
        {
            return;
        }

        UpdateActiveLearningStateFromFields();
        await _libraryService.SaveMovieAsync(_movie);

        var memo = BuildCoffeeLearningMemo(_activeLearningState);
        var cefr = ExtractCefr(memo);
        var fallbackScore = CoffeeLearningWordScoreEstimator.Estimate(cefr, word);
        var labelNames = BuildCoffeeLearningLabelNames(_movie, _activeLearningState);
        var scoring = await _coffeeLearningService.ScoreForRegistrationAsync(
            new CoffeeLearningWordScoreInput(word, meaning, memo, labelNames),
            fallbackScore);
        var registrationMemo = CoffeeLearningRegistrationMemoBuilder.Build(memo, scoring.Score);

        SetCoffeeLearningRegisterBusy(true);
        _learningMessageLabel.Text = "CoffeeLearningへ登録中...";
        _learningMessageLabel.TextColor = Color.FromArgb("#F6D365");
        SetPlayerMessage("CoffeeLearningへ登録中...", Color.FromArgb("#F6D365"));

        try
        {
            var result = await _coffeeLearningService.RegisterWordAsync(new CoffeeLearningWordRegistrationRequest(
                word,
                meaning,
                registrationMemo,
                scoring.Score.Cefr,
                labelNames,
                scoring.Score.Point,
                scoring.AutoAnalyze));
            MarkCoffeeLearningRegistered(result);
            await _libraryService.SaveMovieAsync(_movie);
            await MarkCurrentCueRegisteredInPlayerAsync();
            var driveShareResult = await TryShareCoffeeLearningRegistrationAsync();
            _learningMessageLabel.Text = driveShareResult switch
            {
                true => "CoffeeLearningに登録しました / Drive共有済",
                false => "CoffeeLearningに登録しました / Drive共有は次回同期",
                null => "CoffeeLearningに登録しました"
            };
            _learningMessageLabel.TextColor = Color.FromArgb("#5DE0D0");
            SetPlayerMessage(_learningMessageLabel.Text, Color.FromArgb("#5DE0D0"));
        }
        catch (Exception ex)
        {
            _learningMessageLabel.Text = "CoffeeLearning登録に失敗しました";
            _learningMessageLabel.TextColor = Color.FromArgb("#FF9AA5");
            SetPlayerMessage("CoffeeLearning登録に失敗しました", Color.FromArgb("#FF9AA5"));
            await DisplayAlertAsync("CoffeeLearning登録に失敗しました", ex.Message, "閉じる");
        }
        finally
        {
            SetCoffeeLearningRegisterBusy(false);
            SetLearningControlsEnabled(_activeLearningState is not null);
        }
    }

    private async Task<bool> EnsureCoffeeLearningConfiguredAsync()
    {
        if (await _coffeeLearningService.IsConfiguredAsync())
        {
            return true;
        }

        var choice = await DisplayActionSheetAsync(
            "CoffeeLearning認証が未設定です",
            "キャンセル",
            null,
            "ログインして取得",
            "手入力設定");
        if (choice == "ログインして取得")
        {
            if (_isFullscreen)
            {
                SetFullscreen(false);
            }

            await Navigation.PushAsync(new CoffeeLearningLoginPage(_coffeeLearningService));
            return false;
        }

        return choice == "手入力設定" && await ConfigureCoffeeLearningFromPlayerAsync();
    }

    private async Task<bool> ConfigureCoffeeLearningFromPlayerAsync()
    {
        try
        {
            var settings = await _coffeeLearningService.LoadSettingsAsync();
            var baseUrl = await DisplayPromptAsync(
                "CoffeeLearning設定",
                "API URL",
                "次へ",
                "キャンセル",
                initialValue: string.IsNullOrWhiteSpace(settings.CoffeeLearningBaseUrl)
                    ? CoffeeLearningWordRegistrationService.DefaultBaseUrl
                    : settings.CoffeeLearningBaseUrl);
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return false;
            }

            var deckId = await DisplayPromptAsync(
                "CoffeeLearning設定",
                "登録先deckId",
                "次へ",
                "キャンセル",
                initialValue: string.IsNullOrWhiteSpace(settings.CoffeeLearningDeckId)
                    ? CoffeeLearningWordRegistrationService.DefaultDeckId
                    : settings.CoffeeLearningDeckId);
            if (string.IsNullOrWhiteSpace(deckId))
            {
                return false;
            }

            var authHeader = await DisplayPromptAsync(
                "CoffeeLearning設定",
                "認証ヘッダー。例: Cookie: connect.sid=... / Authorization: Bearer ...。空欄なら保存済みの値を維持します。",
                "保存",
                "キャンセル",
                placeholder: "Cookie: connect.sid=...",
                maxLength: 4096,
                keyboard: Keyboard.Text);
            if (authHeader is null)
            {
                return false;
            }

            await _coffeeLearningService.SaveConfigurationAsync(baseUrl, deckId, authHeader);
            var configured = await _coffeeLearningService.IsConfiguredAsync();
            if (!configured)
            {
                await DisplayAlertAsync("CoffeeLearning設定", "認証ヘッダーが未設定です。", "閉じる");
            }

            return configured;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("CoffeeLearning設定に失敗しました", ex.Message, "閉じる");
            return false;
        }
    }

    private void UpdateActiveLearningStateFromFields()
    {
        if (_activeLearningState is null)
        {
            return;
        }

        _activeLearningState.Tags = ParseTags(_tagsEntry.Text);
        _activeLearningState.Note = string.IsNullOrWhiteSpace(_noteEditor.Text)
            ? null
            : _noteEditor.Text.Trim();
        _activeLearningState.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void SetCoffeeLearningRegisterBusy(bool busy)
    {
        _isCoffeeLearningRegisterBusy = busy;
        UpdateCoffeeLearningRegisterButtons(_activeLearningState is not null);
    }

    private void UpdateCoffeeLearningRegisterButtons(bool enabled)
    {
        var registered = IsCoffeeLearningRegistered(_activeLearningState);
        var text = _isCoffeeLearningRegisterBusy
            ? "登録中"
            : registered ? "登録済" : "単語登録";
        var canRegister = enabled
            && !_isCoffeeLearningRegisterBusy
            && !registered
            && _activeEnglishCue is not null;
        var opacity = !enabled
            ? 0.45d
            : registered ? 0.72d : 1d;

        _registerCoffeeLearningButton.Text = text;
        _fullscreenRegisterCoffeeLearningButton.Text = text;
        _registerCoffeeLearningButton.IsEnabled = canRegister;
        _fullscreenRegisterCoffeeLearningButton.IsEnabled = canRegister;
        _registerCoffeeLearningButton.Opacity = opacity;
        _fullscreenRegisterCoffeeLearningButton.Opacity = opacity;
    }

    private void MarkCoffeeLearningRegistered(CoffeeLearningWordRegistrationResult result)
    {
        if (_activeLearningState is null)
        {
            return;
        }

        _activeLearningState.CoffeeLearningRegisteredAt = DateTimeOffset.UtcNow;
        _activeLearningState.CoffeeLearningWordId = string.IsNullOrWhiteSpace(result.WordId)
            ? null
            : result.WordId.Trim();
        _activeLearningState.CoffeeLearningDeckId = string.IsNullOrWhiteSpace(result.DeckId)
            ? null
            : result.DeckId.Trim();
        _activeLearningState.UpdatedAt = DateTimeOffset.UtcNow;
        _coffeeLearningMemoStatusLabel.IsVisible = true;
        UpdateCoffeeLearningRegisterButtons(enabled: true);
    }

    private async Task<bool?> TryShareCoffeeLearningRegistrationAsync()
    {
        if (!await _googleDriveSyncService.IsConfiguredAsync())
        {
            return null;
        }

        try
        {
            var snapshot = await _libraryService.ExportCoffeeLearningRegistrationSyncAsync();
            await _googleDriveSyncService.UploadCoffeeLearningRegistrationStateAsync(snapshot);
            return true;
        }
        catch
        {
            return false;
        }
    }
    private async Task MarkCurrentCueRegisteredInPlayerAsync()
    {
        if (_activeEnglishCue is null)
        {
            return;
        }

        try
        {
            var cueId = JsonSerializer.Serialize(_activeEnglishCue.Id);
            await _webView.EvaluateJavaScriptAsync(
                $"window.coffeeMovieMarkCueRegistered && window.coffeeMovieMarkCueRegistered({cueId});");
        }
        catch
        {
            // The WebView may not have loaded the player script yet.
        }
    }

    private static bool IsCoffeeLearningRegistered(SubtitleCueLearningState? state)
    {
        return state?.CoffeeLearningRegisteredAt is not null
            || !string.IsNullOrWhiteSpace(state?.CoffeeLearningWordId);
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

    private static string[] BuildCoffeeLearningLabelNames(Movie movie, SubtitleCueLearningState state)
    {
        var labels = new List<string>();
        foreach (var tag in movie.Tags)
        {
            AddLabel(labels, tag);
        }

        foreach (var tag in state.Tags)
        {
            AddLabel(labels, tag);
        }

        if (state.IsFlagged)
        {
            AddLabel(labels, "flag");
        }

        return labels.ToArray();
    }

    private static void AddLabel(List<string> labels, string? value)
    {
        var label = value?.Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        if (!labels.Any(existing => string.Equals(existing, label, StringComparison.OrdinalIgnoreCase)))
        {
            labels.Add(label);
        }
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

}