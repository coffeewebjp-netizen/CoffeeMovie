using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
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

    private void ConfigureMovieShelfGrouping()
    {
        var view = CollectionViewSource.GetDefaultView(_movies);
        view.GroupDescriptions.Clear();
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MovieListItem.SeriesGroup)));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MovieListItem.SeasonGroup)));

        MoviesListBox.GroupStyle.Clear();
        MoviesListBox.GroupStyle.Add(CreateMovieGroupStyle(0));
        MoviesListBox.GroupStyle.Add(CreateMovieGroupStyle(1));
    }

    private GroupStyle CreateMovieGroupStyle(int level)
    {
        var expanderFactory = new FrameworkElementFactory(typeof(Expander));
        expanderFactory.SetValue(Expander.IsExpandedProperty, true);
        expanderFactory.SetValue(Expander.ForegroundProperty, System.Windows.Media.Brushes.White);
        expanderFactory.SetValue(Expander.MarginProperty, new Thickness(level == 0 ? 6 : 16, 6, 6, 2));
        expanderFactory.SetBinding(HeaderedContentControl.HeaderProperty, new System.Windows.Data.Binding("Name"));

        var itemsPresenterFactory = new FrameworkElementFactory(typeof(ItemsPresenter));
        expanderFactory.AppendChild(itemsPresenterFactory);

        return new GroupStyle
        {
            ContainerStyle = new Style(typeof(GroupItem))
            {
                Setters =
                {
                    new Setter(Control.TemplateProperty, new ControlTemplate(typeof(GroupItem))
                    {
                        VisualTree = expanderFactory
                    })
                }
            }
        };
    }

    private void InstallTagSelectorButtons()
    {
        MovieTagFilterTextBox.Margin = new Thickness(0, 0, 152, 8);
        AddGridTagSelectorButtons(MovieTagFilterTextBox, OnSelectMovieTagFilterClicked, OnClearMovieTagFilterClicked);

        SubtitleTagFilterTextBox.Margin = new Thickness(0, 0, 152, 0);
        AddGridTagSelectorButtons(SubtitleTagFilterTextBox, OnSelectSubtitleTagFilterClicked, OnClearSubtitleTagFilterClicked);


        if (SceneTagFilterTextBox.Parent is Panel panel)
        {
            panel.Children.Add(CreateTagSelectorButton(OnSelectSceneTagFilterClicked, new Thickness(8, 0, 0, 0), "選択"));
            panel.Children.Add(CreateTagSelectorButton(OnClearSceneTagFilterClicked, new Thickness(4, 0, 0, 0), "クリア"));
        }
    }

    private static void AddGridTagSelectorButtons(
        System.Windows.Controls.TextBox target,
        RoutedEventHandler selectHandler,
        RoutedEventHandler clearHandler)
    {
        if (target.Parent is not Grid grid)
        {
            return;
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, target.Margin.Top, 0, target.Margin.Bottom)
        };

        buttons.Children.Add(CreateTagSelectorButton(selectHandler, new Thickness(0, 0, 4, 0), "選択"));
        buttons.Children.Add(CreateTagSelectorButton(clearHandler, new Thickness(0), "クリア"));
        Grid.SetRow(buttons, Grid.GetRow(target));
        Grid.SetColumn(buttons, Grid.GetColumn(target));
        grid.Children.Add(buttons);
    }


    private static Button CreateTagSelectorButton(RoutedEventHandler clickHandler, Thickness margin, string text = "選択")
    {
        var button = new Button
        {
            Content = text,
            Width = 72,
            MinWidth = 72,
            Margin = margin,
            Padding = new Thickness(8, 7, 8, 7),
            Background = CreateBrush("#121A26"),
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = CreateBrush("#27364A")
        };
        button.Click += clickHandler;
        return button;
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

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await RefreshMoviesAsync(_selectedMovie?.Id, forceReload: true);
    }

}
