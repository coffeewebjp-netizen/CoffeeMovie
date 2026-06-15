using CoffeeMovie.Core.Models;
using CoffeeMovie.Reader.Services;
using Microsoft.Maui.Media;

namespace CoffeeMovie.Reader.Pages;

public sealed partial class MoviePlayerPage
{
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
            var accuracy = ReaderShadowingScorer.CalculateAccuracy(targetText, transcript);
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


}
