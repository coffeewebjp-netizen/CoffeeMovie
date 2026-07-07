using CoffeeMovie.Core.Services;
using CoffeeMovie.Reader.Models;
using CoffeeMovie.Reader.Services;

namespace CoffeeMovie.Reader.Pages;

public sealed partial class MovieShelfPage
{
    private async Task ManageOtherActionsAsync()
    {
        if (_isSyncing)
        {
            return;
        }

        var choice = await DisplayActionSheetAsync(
            "その他",
            "キャンセル",
            null,
            "CoffeeLearningログイン取得",
            "CoffeeLearning設定",
            "CoffeeLearning scoring",
            "Backup");
        if (choice == "CoffeeLearningログイン取得")
        {
            await Navigation.PushAsync(new CoffeeLearningLoginPage(_coffeeLearningService));
        }
        else if (choice == "CoffeeLearning設定")
        {
            await ConfigureCoffeeLearningAsync();
        }
        else if (choice == "CoffeeLearning scoring")
        {
            await ConfigureCoffeeLearningScoringAsync();
        }
        else if (choice == "Backup")
        {
            await ManageLearningBackupAsync();
        }
    }

    private async Task ConfigureCoffeeLearningAsync()
    {
        try
        {
            var settings = await _syncSettingsService.LoadSettingsAsync();
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
                return;
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
                return;
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
                return;
            }

            await _coffeeLearningService.SaveConfigurationAsync(baseUrl, deckId, authHeader);
            _summaryLabel.Text = await _coffeeLearningService.IsConfiguredAsync()
                ? "CoffeeLearning設定を保存しました"
                : "CoffeeLearning URL/deckIdを保存しました。認証ヘッダーは未設定です";
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("CoffeeLearning設定に失敗しました", ex.Message, "閉じる");
        }
    }

    private async Task ConfigureCoffeeLearningScoringAsync()
    {
        try
        {
            var settings = await _syncSettingsService.LoadSettingsAsync();
            var modeChoice = await DisplayActionSheetAsync(
                "CoffeeLearning scoring",
                "Cancel",
                null,
                "AI provider",
                "CoffeeLearning API",
                "Simple estimate");
            if (string.IsNullOrWhiteSpace(modeChoice) || modeChoice == "Cancel")
            {
                return;
            }

            var mode = modeChoice switch
            {
                "CoffeeLearning API" => CoffeeLearningScoringDefaults.ModeCoffeeLearning,
                "Simple estimate" => CoffeeLearningScoringDefaults.ModeSimple,
                _ => CoffeeLearningScoringDefaults.ModeAiProvider
            };

            var provider = settings.CoffeeLearningScoringProvider ?? CoffeeLearningScoringDefaults.ProviderOpenAi;
            var baseUrl = settings.CoffeeLearningScoringProviderBaseUrl;
            var model = settings.CoffeeLearningScoringProviderModel;
            string? apiKey = null;

            if (mode == CoffeeLearningScoringDefaults.ModeAiProvider)
            {
                var providerChoice = await DisplayActionSheetAsync(
                    "AI provider",
                    "Cancel",
                    null,
                    "GPT / OpenAI",
                    "Gemini",
                    "DeepSeek",
                    "Local LLM");
                if (string.IsNullOrWhiteSpace(providerChoice) || providerChoice == "Cancel")
                {
                    return;
                }

                provider = providerChoice switch
                {
                    "Gemini" => CoffeeLearningScoringDefaults.ProviderGemini,
                    "DeepSeek" => CoffeeLearningScoringDefaults.ProviderDeepSeek,
                    "Local LLM" => CoffeeLearningScoringDefaults.ProviderLocal,
                    _ => CoffeeLearningScoringDefaults.ProviderOpenAi
                };

                baseUrl = await DisplayPromptAsync(
                    "AI provider",
                    "Base URL (blank = default)",
                    "Next",
                    "Cancel",
                    initialValue: settings.CoffeeLearningScoringProviderBaseUrl ?? string.Empty);
                if (baseUrl is null)
                {
                    return;
                }

                model = await DisplayPromptAsync(
                    "AI provider",
                    "Model",
                    "Next",
                    "Cancel",
                    initialValue: settings.CoffeeLearningScoringProviderModel ?? string.Empty);
                if (model is null)
                {
                    return;
                }

                apiKey = await DisplayPromptAsync(
                    "AI provider",
                    "API key (blank keeps saved key; Local LLM can be blank)",
                    "Save",
                    "Cancel",
                    placeholder: "sk-...",
                    maxLength: 4096,
                    keyboard: Keyboard.Text);
                if (apiKey is null)
                {
                    return;
                }
            }

            await _coffeeLearningService.SaveScoringConfigurationAsync(mode, provider, baseUrl, model, apiKey);
            _summaryLabel.Text = $"CoffeeLearning scoring saved: {mode}";
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("CoffeeLearning scoring failed", ex.Message, "Close");
        }
    }
    private async Task ManageLearningBackupAsync()
    {
        if (_isSyncing)
        {
            return;
        }

        var choice = await DisplayActionSheetAsync(
            "Learning backup",
            "Cancel",
            null,
            "Export",
            "Import");
        switch (choice)
        {
            case "Export":
                await ExportLearningBackupAsync();
                break;
            case "Import":
                await ImportLearningBackupAsync();
                break;
        }
    }

    private async Task ExportLearningBackupAsync()
    {
        SetBackupBusy(true, "Exporting learning backup...");
        try
        {
            var result = await _libraryService.ExportLearningStateBackupAsync();
            _summaryLabel.Text =
                $"Learning backup exported: {result.MovieCount} movies / {result.LearningStateCount} cue states";

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "CoffeeMovie learning backup",
                File = new ShareFile(result.FilePath)
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Learning backup export failed", ex.Message, "Close");
        }
        finally
        {
            SetBackupBusy(false);
        }
    }

    private async Task ImportLearningBackupAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select CoffeeMovie learning backup",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.Android] = ["application/json", "text/json", "text/plain"],
                [DevicePlatform.WinUI] = [".json"]
            })
        });
        if (file is null)
        {
            return;
        }

        SetBackupBusy(true, "Importing learning backup...");
        try
        {
            LearningStateBackupImportResult result = await _libraryService.ImportLearningStateBackupAsync(file);
            await ReloadAsync();
            _summaryLabel.Text =
                $"Learning backup imported: {result.MoviesChanged} movies / {result.TracksChanged} tracks / {result.LearningStateCount} cue states / skipped {result.MoviesSkipped}";
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Learning backup import failed", ex.Message, "Close");
        }
        finally
        {
            SetBackupBusy(false);
        }
    }

    private void SetBackupBusy(bool busy, string? message = null)
    {
        _moreButton.IsEnabled = !busy;
        _syncButton.IsEnabled = !busy;
        _driveSettingsButton.IsEnabled = !busy;
        _importButton.IsEnabled = !busy;
        _moviesView.IsEnabled = !busy;
        _moreButton.Opacity = busy ? 0.55 : 1;
        _syncButton.Opacity = busy ? 0.55 : 1;
        _driveSettingsButton.Opacity = busy ? 0.55 : 1;
        _importButton.Opacity = busy ? 0.55 : 1;
        _moviesView.Opacity = busy ? 0.65 : 1;
        if (!string.IsNullOrWhiteSpace(message))
        {
            _summaryLabel.Text = message;
        }
    }
}
