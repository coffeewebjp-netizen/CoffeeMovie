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

        var movieTags = CreateTagRows(library, TagScope.Movie);
        var subtitleTags = CreateTagRows(library, TagScope.Subtitle);

        var window = CreateTagManagerWindow(movieTags, subtitleTags);
        if (window.ShowDialog() != true)
        {
            return;
        }

        ApplyTagManagerChanges(library, TagScope.Movie, movieTags);
        ApplyTagManagerChanges(library, TagScope.Subtitle, subtitleTags);

        await _libraryStore.SaveAsync(library);
        _currentLibrary = library;
        await RefreshMoviesAsync(_selectedMovie?.Id, forceReload: true);
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

    private async void OnClearMovieTagFilterClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(MovieTagFilterTextBox.Text))
        {
            return;
        }

        MovieTagFilterTextBox.Clear();
        await RefreshMoviesAsync(_selectedMovie?.Id);
    }

    private async void OnSelectSubtitleTagFilterClicked(object sender, RoutedEventArgs e)
    {
        if (await SelectTagsIntoTextBoxAsync(SubtitleTagFilterTextBox, TagScope.Subtitle, "字幕タグで動画を絞り込み"))
        {
            await RefreshMoviesAsync(_selectedMovie?.Id);
        }
    }

    private async void OnClearSubtitleTagFilterClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SubtitleTagFilterTextBox.Text))
        {
            return;
        }

        SubtitleTagFilterTextBox.Clear();
        await RefreshMoviesAsync(_selectedMovie?.Id);
    }

    private async void OnSelectMovieTagsClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie is null)
        {
            return;
        }

        var temp = new TextBox { Text = string.Join(", ", _selectedMovie.Tags) };
        if (await SelectTagsIntoTextBoxAsync(temp, TagScope.Movie, "動画タグを選択"))
        {
            await UpdateSelectedMovieTagsAsync(ParseTags(temp.Text));
        }
    }

    private async void OnSelectSceneTagFilterClicked(object sender, RoutedEventArgs e)
    {
        if (await SelectTagsIntoTextBoxAsync(SceneTagFilterTextBox, TagScope.Subtitle, "字幕タグで行を絞り込み"))
        {
            await RefreshSceneTagFilterResultsAsync();
        }
    }

    private async void OnClearSceneTagFilterClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SceneTagFilterTextBox.Text))
        {
            return;
        }

        SceneTagFilterTextBox.Clear();
        await RefreshSceneTagFilterResultsAsync();
    }

    private async void OnSelectSelectedSceneTagsClicked(object sender, RoutedEventArgs e)
    {
        if (ScenesDataGrid.SelectedItem is not SceneRow row)
        {
            SetStatus("タグを付け替える字幕行を選択してください。");
            return;
        }

        await SelectSceneRowTagsAsync(row);
    }

    private async void OnClearSelectedSceneTagsClicked(object sender, RoutedEventArgs e)
    {
        if (ScenesDataGrid.SelectedItem is not SceneRow row)
        {
            SetStatus("タグをクリアする字幕行を選択してください。");
            return;
        }

        if (row.IsGlobalResult)
        {
            SetStatus("検索結果の字幕タグは、動画を開いてから編集してください。");
            return;
        }

        row.Tags = string.Empty;
        row.IsFlagged = false;
        await SaveSceneRowLearningStateAsync(row);
        RenderSceneRows(_previewSubtitleTrack);
    }

    private async void OnSelectSceneRowTagsClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SceneRow row })
        {
            ScenesDataGrid.SelectedItem = row;
            await SelectSceneRowTagsAsync(row);
        }
    }

    private async Task SelectSceneRowTagsAsync(SceneRow row)
    {
        if (row.IsGlobalResult)
        {
            SetStatus("検索結果の字幕タグは、動画を開いてから編集してください。");
            return;
        }

        var temp = new TextBox { Text = row.Tags };
        if (!await SelectTagsIntoTextBoxAsync(temp, TagScope.Subtitle, "選択中の字幕タグを付け替え"))
        {
            return;
        }

        row.Tags = temp.Text;
        row.IsFlagged = ParseTags(row.Tags).Any(IsFlagTag);
        await SaveSceneRowLearningStateAsync(row);
        RenderSceneRows(_previewSubtitleTrack);
    }

    private async Task RefreshSceneTagFilterResultsAsync()
    {
        if (HasGlobalSubtitleTagFilter())
        {
            await RenderGlobalSubtitleTagResultsAsync();
            return;
        }

        RenderSceneRows(_previewSubtitleTrack);
    }

    private async Task<bool> SelectTagsIntoTextBoxAsync(TextBox target, TagScope scope, string title)
    {
        var library = await _libraryStore.LoadAsync();
        MergeTagDefinitionsFromLibrary(library);
        var currentTags = ParseTags(target.Text);
        var availableTags = library.TagDefinitions
            .Where(tag => tag.Scope == scope)
            .OrderBy(tag => tag.SortOrder)
            .ThenBy(tag => tag.Name)
            .Select(tag => tag.Name)
            .Concat(currentTags)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (availableTags.Count == 0)
        {
            MessageBox.Show(this, "登録済みタグがありません。先にタグ管理でタグを作成してください。", title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var selectedKeys = currentTags.Select(NormalizedTagKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var checkBoxes = availableTags
            .Select(tag => new CheckBox
            {
                Content = tag,
                IsChecked = selectedKeys.Contains(NormalizedTagKey(tag)),
                Foreground = Brushes.White,
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
            Background = FindResource("PanelBrush") as Brush,
            Foreground = Brushes.White
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var clearButton = CreateTagButton("クリア", isPrimary: false);
        clearButton.Click += (_, _) =>
        {
            foreach (var checkBox in checkBoxes)
            {
                checkBox.IsChecked = false;
            }
        };
        var okButton = CreateTagButton("OK", isPrimary: true);
        okButton.IsDefault = true;
        okButton.Click += (_, _) => window.DialogResult = true;
        var cancelButton = CreateTagButton("キャンセル", isPrimary: false);
        cancelButton.IsCancel = true;
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

    private static ObservableCollection<TagDefinitionRow> CreateTagRows(MovieLibrary library, TagScope scope)
    {
        return new ObservableCollection<TagDefinitionRow>(
            library.TagDefinitions
                .Where(tag => tag.Scope == scope)
                .OrderBy(tag => tag.SortOrder)
                .ThenBy(tag => tag.Name)
                .Select(tag => new TagDefinitionRow(tag)));
    }

    private void ApplyTagManagerChanges(MovieLibrary library, TagScope scope, IReadOnlyList<TagDefinitionRow> rows)
    {
        var originalNames = library.TagDefinitions
            .Where(tag => tag.Scope == scope)
            .Select(tag => tag.Name)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var newDefinitions = rows
            .Select((row, index) => row.ToDefinition(scope, index))
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Name))
            .GroupBy(tag => NormalizedTagKey(tag.Name))
            .Select(group => group.First())
            .ToList();

        var renameMap = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.OriginalName))
            .Where(row => !string.Equals(row.OriginalName, row.Name, StringComparison.OrdinalIgnoreCase))
            .GroupBy(row => NormalizedTagKey(row.OriginalName))
            .ToDictionary(group => group.Key, group => group.First().Name.Trim(), StringComparer.OrdinalIgnoreCase);

        var newKeys = newDefinitions
            .Select(tag => NormalizedTagKey(tag.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removedKeys = originalNames
            .Select(NormalizedTagKey)
            .Where(key => !newKeys.Contains(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        ApplyTagUsageChanges(library, scope, renameMap, removedKeys);

        library.TagDefinitions = library.TagDefinitions
            .Where(tag => tag.Scope != scope)
            .Concat(newDefinitions)
            .OrderBy(tag => tag.Scope)
            .ThenBy(tag => tag.SortOrder)
            .ToList();
    }

    private static void ApplyTagUsageChanges(
        MovieLibrary library,
        TagScope scope,
        IReadOnlyDictionary<string, string> renameMap,
        IReadOnlySet<string> removedKeys)
    {
        if (renameMap.Count == 0 && removedKeys.Count == 0)
        {
            return;
        }

        foreach (var movie in library.Movies)
        {
            var changed = false;
            if (scope == TagScope.Movie)
            {
                var updatedTags = TransformTags(movie.Tags, renameMap, removedKeys);
                changed = !movie.Tags.SequenceEqual(updatedTags, StringComparer.OrdinalIgnoreCase);
                movie.Tags = updatedTags;
            }
            else
            {
                foreach (var state in movie.SubtitleTracks.SelectMany(track => track.CueLearningStates))
                {
                    var updatedTags = TransformTags(state.Tags, renameMap, removedKeys);
                    if (!state.Tags.SequenceEqual(updatedTags, StringComparer.OrdinalIgnoreCase))
                    {
                        state.Tags = updatedTags;
                        state.IsFlagged = updatedTags.Any(tag => TagFilterService.IsFlagTag(tag, FlagTagName));
                        state.UpdatedAt = DateTimeOffset.UtcNow;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                movie.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    private static List<string> TransformTags(
        IEnumerable<string> tags,
        IReadOnlyDictionary<string, string> renameMap,
        IReadOnlySet<string> removedKeys)
    {
        var result = new List<string>();
        foreach (var tag in tags)
        {
            var normalized = tag.Trim();
            if (normalized.Length == 0)
            {
                continue;
            }

            var key = NormalizedTagKey(normalized);
            if (renameMap.TryGetValue(key, out var renamed))
            {
                AddTag(result, renamed);
                continue;
            }

            if (removedKeys.Contains(key))
            {
                continue;
            }

            AddTag(result, normalized);
        }

        return result;
    }

    private Window CreateTagManagerWindow(
        ObservableCollection<TagDefinitionRow> movieTags,
        ObservableCollection<TagDefinitionRow> subtitleTags)
    {
        var maxWindowHeight = Math.Max(400, SystemParameters.WorkArea.Height - 80);
        var window = new Window
        {
            Title = "タグ管理",
            Owner = this,
            Width = 780,
            Height = 560,
            MaxHeight = maxWindowHeight,
            MinWidth = 680,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = FindResource("PanelBrush") as Brush,
            Foreground = Brushes.White
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
        var saveButton = CreateTagButton("保存", isPrimary: true);
        var cancelButton = CreateTagButton("キャンセル", isPrimary: false);
        saveButton.IsDefault = true;
        cancelButton.IsCancel = true;
        saveButton.Click += (_, _) => window.DialogResult = true;
        cancelButton.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);
        content.Children.Add(buttons);

        window.Content = content;
        return window;
    }

    private static ListBox CreateTagDefinitionListBox(ObservableCollection<TagDefinitionRow> tags)
    {
        var listBox = new ListBox
        {
            ItemsSource = tags,
            DisplayMemberPath = nameof(TagDefinitionRow.Name),
            Margin = new Thickness(0, 8, 0, 8),
            MinHeight = 220,
            Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x11, 0x1A)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x27, 0x36, 0x4A))
        };

        ScrollViewer.SetVerticalScrollBarVisibility(listBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Disabled);
        return listBox;
    }

    private FrameworkElement CreateTagScopePanel(
        string title,
        ObservableCollection<TagDefinitionRow> tags,
        ListBox listBox)
    {
        var textBox = new TextBox
        {
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x11, 0x1A, 0x27)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x27, 0x36, 0x4A))
        };
        var addButton = CreateTagButton("追加", isPrimary: true);
        var renameButton = CreateTagButton("編集", isPrimary: false);
        var deleteButton = CreateTagButton("削除", isPrimary: false);
        var upButton = CreateTagButton("上へ", isPrimary: false);
        var downButton = CreateTagButton("下へ", isPrimary: false);

        addButton.Click += (_, _) =>
        {
            var tag = NormalizeOptionalText(textBox.Text);
            if (tag is null || tags.Any(existing => string.Equals(existing.Name, tag, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            tags.Add(new TagDefinitionRow(new TagDefinition { Name = tag, SortOrder = tags.Count }, originalName: string.Empty));
            textBox.Text = string.Empty;
            listBox.SelectedIndex = tags.Count - 1;
        };
        renameButton.Click += (_, _) => RenameSelectedTag(title, tags, listBox);
        deleteButton.Click += (_, _) =>
        {
            if (listBox.SelectedItem is not TagDefinitionRow row)
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                $"'{row.Name}' を削除します。保存すると既存の動画/字幕からもこのタグが外れます。",
                "タグ削除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var index = listBox.SelectedIndex;
            tags.Remove(row);
            listBox.SelectedIndex = Math.Min(index, tags.Count - 1);
        };
        upButton.Click += (_, _) => MoveSelectedTag(tags, listBox, -1);
        downButton.Click += (_, _) => MoveSelectedTag(tags, listBox, 1);

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

        var actions = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right
        };
        actions.Children.Add(renameButton);
        actions.Children.Add(deleteButton);
        actions.Children.Add(upButton);
        actions.Children.Add(downButton);
        Grid.SetRow(actions, 3);
        panel.Children.Add(actions);
        return panel;
    }

    private void RenameSelectedTag(string title, ObservableCollection<TagDefinitionRow> tags, ListBox listBox)
    {
        if (listBox.SelectedItem is not TagDefinitionRow row)
        {
            return;
        }

        var prompt = new TextPromptWindow(title, "新しいタグ名を入力してください。", row.Name)
        {
            Owner = this
        };
        if (prompt.ShowDialog() != true || NormalizeOptionalText(prompt.Response) is not { } renamed)
        {
            return;
        }

        if (tags.Any(existing => !ReferenceEquals(existing, row) && string.Equals(existing.Name, renamed, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "同じ名前のタグがすでにあります。", title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        row.Name = renamed;
        listBox.Items.Refresh();
    }

    private static void MoveSelectedTag(ObservableCollection<TagDefinitionRow> tags, ListBox listBox, int offset)
    {
        var index = listBox.SelectedIndex;
        var target = index + offset;
        if (index < 0 || target < 0 || target >= tags.Count)
        {
            return;
        }

        tags.Move(index, target);
        listBox.SelectedIndex = target;
    }

    private static Button CreateTagButton(string text, bool isPrimary)
    {
        return new Button
        {
            Content = text,
            MinWidth = 70,
            Height = 30,
            Margin = new Thickness(6, 0, 0, 0),
            Background = new SolidColorBrush(isPrimary ? Color.FromRgb(0x5D, 0xE0, 0xD0) : Color.FromRgb(0x12, 0x1A, 0x26)),
            Foreground = new SolidColorBrush(isPrimary ? Color.FromRgb(0x04, 0x10, 0x0F) : Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x27, 0x36, 0x4A))
        };
    }

    private static List<string> ParseTags(string? text)
    {
        return TagFilterService.ParseTags(text);
    }
}
