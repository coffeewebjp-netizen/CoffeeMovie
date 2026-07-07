using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CoffeeMovie.Core.Services;
using CoffeeMovie.Studio.Services;

namespace CoffeeMovie.Studio;

internal sealed class CoffeeLearningSettingsWindow : Window
{
    private readonly CoffeeLearningBrowserTokenService _browserTokenService = new();
    private readonly TextBox _baseUrlTextBox = new();
    private readonly TextBox _deckIdTextBox = new();
    private readonly TextBox _authHeaderTextBox = new();
    private readonly ComboBox _scoringModeComboBox = new();
    private readonly TextBox _aiAgentCommandTextBox = new();
    private readonly TextBox _aiAgentModelTextBox = new();
    private readonly TextBox _aiAgentArgumentsTextBox = new();
    private readonly ComboBox _providerComboBox = new();
    private readonly TextBox _providerBaseUrlTextBox = new();
    private readonly TextBox _providerModelTextBox = new();
    private readonly PasswordBox _providerApiKeyPasswordBox = new();
    private readonly TextBlock _statusTextBlock = new();
    private readonly Button _browserTokenButton;

    public CoffeeLearningConnectionSettings? Settings { get; private set; }

    public CoffeeLearningSettingsWindow(CoffeeLearningConnectionSettings current)
    {
        Title = "CoffeeLearning settings";
        Width = 760;
        Height = 720;
        MinWidth = 620;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x05, 0x07, 0x0B));
        Foreground = Brushes.White;

        _baseUrlTextBox.Text = string.IsNullOrWhiteSpace(current.BaseUrl)
            ? CoffeeLearningWordRegistrationService.DefaultBaseUrl
            : current.BaseUrl;
        _deckIdTextBox.Text = string.IsNullOrWhiteSpace(current.DeckId)
            ? CoffeeLearningWordRegistrationService.DefaultDeckId
            : current.DeckId;
        _authHeaderTextBox.Text = current.AuthHeader ?? string.Empty;
        _authHeaderTextBox.AcceptsReturn = true;
        _authHeaderTextBox.TextWrapping = TextWrapping.Wrap;
        _authHeaderTextBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        AddComboItem(_scoringModeComboBox, "AIAGENT (PC)", CoffeeLearningScoringDefaults.ModeAiAgent);
        AddComboItem(_scoringModeComboBox, "AI provider", CoffeeLearningScoringDefaults.ModeAiProvider);
        AddComboItem(_scoringModeComboBox, "CoffeeLearning API", CoffeeLearningScoringDefaults.ModeCoffeeLearning);
        AddComboItem(_scoringModeComboBox, "Simple estimate", CoffeeLearningScoringDefaults.ModeSimple);
        SelectComboValue(_scoringModeComboBox, current.ScoringMode, CoffeeLearningScoringDefaults.ModeAiAgent);

        _aiAgentCommandTextBox.Text = string.IsNullOrWhiteSpace(current.ScoringAiAgentCommand)
            ? CoffeeLearningScoringDefaults.DefaultAiAgentCommand
            : current.ScoringAiAgentCommand;
        _aiAgentModelTextBox.Text = string.IsNullOrWhiteSpace(current.ScoringAiAgentModel)
            ? CoffeeLearningScoringDefaults.DefaultAiAgentModel
            : current.ScoringAiAgentModel;
        _aiAgentArgumentsTextBox.Text = string.IsNullOrWhiteSpace(current.ScoringAiAgentArguments)
            ? CoffeeLearningScoringDefaults.DefaultAiAgentArguments
            : current.ScoringAiAgentArguments;
        _aiAgentArgumentsTextBox.AcceptsReturn = true;
        _aiAgentArgumentsTextBox.TextWrapping = TextWrapping.Wrap;
        _aiAgentArgumentsTextBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        AddComboItem(_providerComboBox, "GPT / OpenAI", CoffeeLearningScoringDefaults.ProviderOpenAi);
        AddComboItem(_providerComboBox, "Gemini", CoffeeLearningScoringDefaults.ProviderGemini);
        AddComboItem(_providerComboBox, "DeepSeek", CoffeeLearningScoringDefaults.ProviderDeepSeek);
        AddComboItem(_providerComboBox, "Local LLM", CoffeeLearningScoringDefaults.ProviderLocal);
        SelectComboValue(_providerComboBox, current.ScoringProvider, CoffeeLearningScoringDefaults.ProviderOpenAi);
        _providerBaseUrlTextBox.Text = current.ScoringProviderBaseUrl ?? string.Empty;
        _providerModelTextBox.Text = current.ScoringProviderModel ?? string.Empty;
        _providerApiKeyPasswordBox.Password = current.ScoringProviderApiKey ?? string.Empty;

        _statusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xA5, 0xB3, 0xC6));
        _statusTextBlock.TextWrapping = TextWrapping.Wrap;
        _statusTextBlock.Margin = new Thickness(0, 8, 0, 0);
        _statusTextBlock.Text = "For registration, CoffeeMovie needs CoffeeLearning auth. For scoring, choose AIAGENT on PC or an AI provider key.";

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var form = new StackPanel { Orientation = Orientation.Vertical };
        AddField(form, "API URL", _baseUrlTextBox, 34);

        _browserTokenButton = CreateButton("Get from browser", isPrimary: true);
        _browserTokenButton.MinWidth = 156;
        _browserTokenButton.HorizontalAlignment = HorizontalAlignment.Left;
        _browserTokenButton.Margin = new Thickness(0, 12, 0, 8);
        _browserTokenButton.Click += OnBrowserTokenClicked;
        form.Children.Add(_browserTokenButton);

        AddField(form, "deckId", _deckIdTextBox, 34);
        AddField(form, "Authorization header / Cookie", _authHeaderTextBox, 82);
        AddSeparator(form);
        AddComboField(form, "Scoring mode", _scoringModeComboBox);
        AddField(form, "AIAGENT command", _aiAgentCommandTextBox, 34);
        AddField(form, "AIAGENT model", _aiAgentModelTextBox, 34);
        AddField(form, "AIAGENT arguments", _aiAgentArgumentsTextBox, 74);
        AddComboField(form, "AI provider", _providerComboBox);
        AddField(form, "Provider base URL", _providerBaseUrlTextBox, 34);
        AddField(form, "Provider model", _providerModelTextBox, 34);
        AddPasswordField(form, "Provider API key", _providerApiKeyPasswordBox);
        form.Children.Add(_statusTextBlock);

        var scrollViewer = new ScrollViewer
        {
            Content = form,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(scrollViewer, 0);
        root.Children.Add(scrollViewer);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var okButton = CreateButton("Save", isPrimary: true);
        okButton.IsDefault = true;
        okButton.Click += OnOkClicked;
        var cancelButton = CreateButton("Cancel", isPrimary: false);
        cancelButton.IsCancel = true;
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);

        Content = root;
    }

    private async void OnBrowserTokenClicked(object sender, RoutedEventArgs e)
    {
        _browserTokenButton.IsEnabled = false;
        _statusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xF6, 0xD3, 0x65));
        _statusTextBlock.Text = "Opening browser. If CoffeeLearning is already logged in, the token will be captured automatically.";

        try
        {
            var result = await _browserTokenService.AcquireAsync(_baseUrlTextBox.Text);
            _authHeaderTextBox.Text = result.Bearer;
            if (!string.IsNullOrWhiteSpace(result.DeckId))
            {
                _deckIdTextBox.Text = result.DeckId;
            }

            _statusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x8C, 0xE7, 0xB2));
            _statusTextBlock.Text = "CoffeeLearning token captured. Press Save.";
        }
        catch (Exception ex)
        {
            _statusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9A, 0xA5));
            _statusTextBlock.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "CoffeeLearning token", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _browserTokenButton.IsEnabled = true;
        }
    }

    private void OnOkClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var baseUrl = CoffeeLearningWordRegistrationService.NormalizeBaseUrl(_baseUrlTextBox.Text);
            Settings = new CoffeeLearningConnectionSettings(
                baseUrl,
                string.IsNullOrWhiteSpace(_deckIdTextBox.Text)
                    ? CoffeeLearningWordRegistrationService.DefaultDeckId
                    : _deckIdTextBox.Text.Trim(),
                _authHeaderTextBox.Text.Trim(),
                SelectedComboValue(_scoringModeComboBox, CoffeeLearningScoringDefaults.ModeAiAgent),
                NormalizeOptionalText(_aiAgentCommandTextBox.Text) ?? CoffeeLearningScoringDefaults.DefaultAiAgentCommand,
                NormalizeOptionalText(_aiAgentModelTextBox.Text) ?? CoffeeLearningScoringDefaults.DefaultAiAgentModel,
                NormalizeOptionalText(_aiAgentArgumentsTextBox.Text) ?? CoffeeLearningScoringDefaults.DefaultAiAgentArguments,
                SelectedComboValue(_providerComboBox, CoffeeLearningScoringDefaults.ProviderOpenAi),
                NormalizeOptionalText(_providerBaseUrlTextBox.Text),
                NormalizeOptionalText(_providerModelTextBox.Text),
                NormalizeOptionalText(_providerApiKeyPasswordBox.Password));
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "CoffeeLearning settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void AddField(StackPanel panel, string label, TextBox textBox, double height)
    {
        AddLabel(panel, label);
        textBox.Height = height;
        textBox.Padding = new Thickness(8, 4, 8, 4);
        StyleControl(textBox);
        panel.Children.Add(textBox);
    }

    private static void AddPasswordField(StackPanel panel, string label, PasswordBox passwordBox)
    {
        AddLabel(panel, label);
        passwordBox.Height = 34;
        passwordBox.Padding = new Thickness(8, 4, 8, 4);
        StyleControl(passwordBox);
        panel.Children.Add(passwordBox);
    }

    private static void AddComboField(StackPanel panel, string label, ComboBox comboBox)
    {
        AddLabel(panel, label);
        comboBox.Height = 34;
        comboBox.Padding = new Thickness(6, 2, 6, 2);
        StyleControl(comboBox);
        panel.Children.Add(comboBox);
    }

    private static void AddLabel(StackPanel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA5, 0xB3, 0xC6)),
            Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 10, 0, 4)
        });
    }

    private static void AddSeparator(StackPanel panel)
    {
        panel.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 16, 0, 4),
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0x36, 0x4A))
        });
    }

    private static void StyleControl(Control control)
    {
        control.Background = new SolidColorBrush(Color.FromRgb(0x11, 0x1A, 0x27));
        control.Foreground = Brushes.White;
        control.BorderBrush = new SolidColorBrush(Color.FromRgb(0x27, 0x36, 0x4A));
    }

    private static Button CreateButton(string text, bool isPrimary)
    {
        return new Button
        {
            Content = text,
            MinWidth = 96,
            Height = 34,
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(isPrimary
                ? Color.FromRgb(0x5D, 0xE0, 0xD0)
                : Color.FromRgb(0x12, 0x1A, 0x26)),
            Foreground = new SolidColorBrush(isPrimary
                ? Color.FromRgb(0x04, 0x10, 0x0F)
                : Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x27, 0x36, 0x4A))
        };
    }

    private static void AddComboItem(ComboBox comboBox, string text, string value)
    {
        comboBox.Items.Add(new ComboBoxItem { Content = text, Tag = value });
    }

    private static void SelectComboValue(ComboBox comboBox, string? value, string fallback)
    {
        var selectedValue = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (ComboBoxItem item in comboBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), selectedValue, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static string SelectedComboValue(ComboBox comboBox, string fallback)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;
    }

    private static string? NormalizeOptionalText(string? text)
    {
        var normalized = text?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}