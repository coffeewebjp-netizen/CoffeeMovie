using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Models;
using CoffeeMovie.Studio.Services;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
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

    private async void OnMovieFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingSelection)
        {
            return;
        }

        await RefreshMoviesAsync(_selectedMovie?.Id);
    }

    private async void OnSelectMovieTagFilterClicked(object sender, RoutedEventArgs e)
    {
        if (await SelectTagsIntoTextBoxAsync(MovieTagFilterTextBox, TagScope.Movie, "動画タグで絞り込み"))
        {
            await RefreshMoviesAsync(_selectedMovie?.Id);
        }
    }

    private async void OnSelectSubtitleTagFilterClicked(object sender, RoutedEventArgs e)
    {
        if (await SelectTagsIntoTextBoxAsync(SubtitleTagFilterTextBox, TagScope.Subtitle, "字幕タグで動画を絞り込み"))
        {
            await RefreshMoviesAsync(_selectedMovie?.Id);
        }
    }

    private async void OnSelectMovieTagsClicked(object sender, RoutedEventArgs e)
    {
        if (await SelectTagsIntoTextBoxAsync(MovieTagsTextBox, TagScope.Movie, "動画タグを選択"))
        {
            OnMovieMetadataLostFocus(sender, e);
        }
    }

    private async void OnSelectSceneTagFilterClicked(object sender, RoutedEventArgs e)
    {
        if (await SelectTagsIntoTextBoxAsync(SceneTagFilterTextBox, TagScope.Subtitle, "字幕タグで行を絞り込み"))
        {
            RenderSceneRows(_previewSubtitleTrack);
        }
    }

    private async void OnSelectSelectedSceneTagsClicked(object sender, RoutedEventArgs e)
    {
        if (ScenesDataGrid.SelectedItem is not SceneRow row)
        {
            SetStatus("タグを付け替える字幕行を選択してください。");
            return;
        }

        var temp = new System.Windows.Controls.TextBox { Text = row.Tags };
        if (!await SelectTagsIntoTextBoxAsync(temp, TagScope.Subtitle, "選択中の字幕タグを付け替え"))
        {
            return;
        }

        row.Tags = temp.Text;
        row.IsFlagged = ParseTags(row.Tags).Any(IsFlagTag);
        await SaveSceneRowLearningStateAsync(row);
        RenderSceneRows(_previewSubtitleTrack);
    }

    private async Task<bool> SelectTagsIntoTextBoxAsync(
        System.Windows.Controls.TextBox target,
        TagScope scope,
        string title)
    {
        var library = await _libraryStore.LoadAsync();
        MergeTagDefinitionsFromLibrary(library);
        var currentTags = ParseTags(target.Text);
        var availableTags = library.TagDefinitions
            .Where(tag => tag.Scope == scope)
            .OrderBy(tag => tag.SortOrder)
            .Select(tag => tag.Name)
            .Concat(currentTags)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (availableTags.Count == 0)
        {
            MessageBox.Show(this, "登録済みタグがありません。先にタグ管理またはタグ入力でタグを作成してください。", title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var selectedKeys = currentTags.Select(NormalizedTagKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var checkBoxes = availableTags
            .Select(tag => new CheckBox
            {
                Content = tag,
                IsChecked = selectedKeys.Contains(NormalizedTagKey(tag)),
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            })
            .ToList();

        var listPanel = new StackPanel();
        foreach (var checkBox in checkBoxes)
        {
            listPanel.Children.Add(checkBox);
        }

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = listPanel
        };

        var window = new Window
        {
            Title = title,
            Owner = this,
            Width = 360,
            Height = Math.Min(520, Math.Max(300, SystemParameters.WorkArea.Height - 120)),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = FindResource("PanelBrush") as System.Windows.Media.Brush,
            Foreground = System.Windows.Media.Brushes.White
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var clearButton = new Button
        {
            Content = "クリア",
            MinWidth = 72,
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = CreateBrush("#27364A")
        };
        clearButton.Click += (_, _) =>
        {
            foreach (var checkBox in checkBoxes)
            {
                checkBox.IsChecked = false;
            }
        };
        var okButton = new Button { Content = "OK", MinWidth = 72 };
        okButton.Click += (_, _) => window.DialogResult = true;
        var cancelButton = new Button
        {
            Content = "キャンセル",
            MinWidth = 86,
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = CreateBrush("#27364A")
        };
        cancelButton.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(clearButton);
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        var content = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            }
        };
        content.Children.Add(scrollViewer);
        Grid.SetRow(buttons, 1);
        content.Children.Add(buttons);
        window.Content = content;

        if (window.ShowDialog() != true)
        {
            return false;
        }

        target.Text = string.Join(", ", checkBoxes
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => checkBox.Content?.ToString())
            .Where(tag => !string.IsNullOrWhiteSpace(tag)));
        return true;
    }

    private bool MatchesMovieFilters(Movie movie)
    {
        return TagFilterService.MatchesMovie(
            movie,
            MovieSearchTextBox.Text,
            MovieTagFilterTextBox.Text,
            SubtitleTagFilterTextBox.Text,
            FlagTagName);
    }

    private static bool IsFlagTag(string tag)
    {
        return TagFilterService.IsFlagTag(tag, FlagTagName);
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
        return TagFilterService.ParseTags(text);
    }
}
