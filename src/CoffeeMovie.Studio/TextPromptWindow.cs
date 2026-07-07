using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CoffeeMovie.Studio;

internal sealed class TextPromptWindow : Window
{
    private readonly TextBox _textBox = new();

    public string? Response { get; private set; }

    public TextPromptWindow(string title, string message, string initialValue = "")
    {
        Title = title;
        Width = 520;
        Height = 230;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x05, 0x07, 0x0B));
        Foreground = Brushes.White;

        _textBox.Text = initialValue;
        _textBox.Height = 70;
        _textBox.AcceptsReturn = true;
        _textBox.TextWrapping = TextWrapping.Wrap;
        _textBox.Padding = new Thickness(8, 4, 8, 4);
        _textBox.Background = new SolidColorBrush(Color.FromRgb(0x11, 0x1A, 0x27));
        _textBox.Foreground = Brushes.White;
        _textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0x27, 0x36, 0x4A));

        var messageBlock = new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA5, 0xB3, 0xC6)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var okButton = CreateButton("登録", isPrimary: true);
        okButton.IsDefault = true;
        okButton.Click += (_, _) =>
        {
            Response = _textBox.Text.Trim();
            DialogResult = true;
        };
        var cancelButton = CreateButton("キャンセル", isPrimary: false);
        cancelButton.IsCancel = true;
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Children = { messageBlock, _textBox, buttons }
        };
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
}