using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CoffeeMovie.Studio;

internal sealed class FullPreviewPopupWindow : Window
{
    private readonly MediaElement _player;
    private Uri? _source;
    private TimeSpan? _pendingPosition;
    private bool _playWhenOpened;
    private bool _isMediaOpened;
    private bool _isPlaying;

    public FullPreviewPopupWindow()
    {
        Title = "CoffeeMovie Studio Preview";
        Width = 960;
        Height = 540;
        MinWidth = 520;
        MinHeight = 320;
        Background = Brushes.Black;

        var root = new Grid
        {
            ClipToBounds = true,
            Background = Brushes.Black
        };
        Content = root;

        _player = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            ScrubbingEnabled = true,
            Stretch = Stretch.Uniform,
            IsMuted = true
        };
        _player.MediaOpened += OnMediaOpened;
        _player.MediaEnded += (_, _) =>
        {
            _isPlaying = false;
            _player.Stop();
        };
        root.Children.Add(_player);

        AboveOverlayPanel = new StackPanel
        {
            Margin = new Thickness(56, 24, 56, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        root.Children.Add(AboveOverlayPanel);

        BelowOverlayPanel = new StackPanel
        {
            Margin = new Thickness(56, 0, 56, 28),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        root.Children.Add(BelowOverlayPanel);
    }

    public StackPanel AboveOverlayPanel { get; }

    public StackPanel BelowOverlayPanel { get; }

    public void Sync(string videoPath, TimeSpan position, bool shouldPlay, bool forceSeek = false)
    {
        if (!File.Exists(videoPath))
        {
            Clear();
            return;
        }

        var source = new Uri(videoPath);
        var isSameSource = _source is not null
            && string.Equals(_source.LocalPath, source.LocalPath, StringComparison.OrdinalIgnoreCase);
        if (!isSameSource)
        {
            _player.Stop();
            _source = source;
            _isMediaOpened = false;
            _isPlaying = false;
            _pendingPosition = position;
            _playWhenOpened = shouldPlay;
            _player.Source = source;
            return;
        }

        if (!_isMediaOpened)
        {
            _pendingPosition = position;
            _playWhenOpened = shouldPlay;
            return;
        }

        var drift = (_player.Position - position).Duration();
        if (forceSeek || !shouldPlay || drift > TimeSpan.FromMilliseconds(700))
        {
            _player.Position = position;
        }

        if (shouldPlay)
        {
            if (!_isPlaying)
            {
                _player.Play();
                _isPlaying = true;
            }
        }
        else if (_isPlaying)
        {
            _player.Pause();
            _isPlaying = false;
        }
    }

    public void Clear()
    {
        _player.Stop();
        _player.Source = null;
        _source = null;
        _isMediaOpened = false;
        _isPlaying = false;
        _pendingPosition = null;
        AboveOverlayPanel.Children.Clear();
        BelowOverlayPanel.Children.Clear();
    }

    protected override void OnClosed(EventArgs e)
    {
        Clear();
        base.OnClosed(e);
    }

    private void OnMediaOpened(object sender, RoutedEventArgs e)
    {
        _isMediaOpened = true;
        if (_pendingPosition is { } position)
        {
            _player.Position = position;
            _pendingPosition = null;
        }

        if (_playWhenOpened)
        {
            _player.Play();
            _isPlaying = true;
            _playWhenOpened = false;
        }
    }
}
