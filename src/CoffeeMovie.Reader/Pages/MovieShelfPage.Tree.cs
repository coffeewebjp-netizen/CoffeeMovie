using CoffeeMovie.Reader.Models;
using Microsoft.Maui.Controls.Shapes;

namespace CoffeeMovie.Reader.Pages;

public sealed partial class MovieShelfPage
{
    private IReadOnlyList<object> BuildShelfRows(IReadOnlyList<MovieListItem> items)
    {
        var rows = new List<object>();
        foreach (var seriesGroup in items.GroupBy(item => item.SeriesKey))
        {
            var seriesItems = seriesGroup.ToList();
            var seriesTitle = seriesItems[0].SeriesTitle;
            var seriesExpanded = !_collapsedSeries.Contains(seriesGroup.Key);
            rows.Add(new ShelfHeaderRow(
                Key: seriesGroup.Key,
                ParentKey: null,
                Title: seriesTitle,
                Detail: $"{seriesItems.Count} episode",
                Level: 0,
                IsExpanded: seriesExpanded));
            if (!seriesExpanded)
            {
                continue;
            }

            foreach (var seasonGroup in seriesItems.GroupBy(item => item.SeasonKey))
            {
                var seasonItems = seasonGroup.ToList();
                var seasonKey = $"{seriesGroup.Key}|{seasonGroup.Key}";
                var seasonExpanded = !_collapsedSeasons.Contains(seasonKey);
                rows.Add(new ShelfHeaderRow(
                    Key: seasonKey,
                    ParentKey: seriesGroup.Key,
                    Title: seasonItems[0].SeasonTitle,
                    Detail: $"{seasonItems.Count} episode",
                    Level: 1,
                    IsExpanded: seasonExpanded));
                if (seasonExpanded)
                {
                    rows.AddRange(seasonItems);
                }
            }
        }

        return rows;
    }

    private View CreateSeriesHeaderRow()
    {
        return CreateShelfHeaderRow(Color.FromArgb("#F6D365"), new Thickness(18, 10, 18, 4));
    }

    private View CreateSeasonHeaderRow()
    {
        return CreateShelfHeaderRow(Color.FromArgb("#5DE0D0"), new Thickness(34, 6, 18, 4));
    }

    private View CreateShelfHeaderRow(Color accentColor, Thickness margin)
    {
        var marker = new Label
        {
            TextColor = accentColor,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center,
            WidthRequest = 24
        };
        marker.SetBinding(Label.TextProperty, nameof(ShelfHeaderRow.Marker));

        var title = new Label
        {
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 15,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        title.SetBinding(Label.TextProperty, nameof(ShelfHeaderRow.Title));

        var detail = new Label
        {
            TextColor = Color.FromArgb("#A5B3C6"),
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center
        };
        detail.SetBinding(Label.TextProperty, nameof(ShelfHeaderRow.Detail));

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children = { marker, title, detail }
        };
        Grid.SetColumn(title, 1);
        Grid.SetColumn(detail, 2);

        var border = new Border
        {
            Margin = margin,
            Padding = new Thickness(10, 8),
            Stroke = Color.FromArgb("#1E2A3A"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Color.FromArgb("#111A27"),
            Content = grid
        };
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command<ShelfHeaderRow?>(ToggleShelfHeader),
            CommandParameter = border.BindingContext
        });
        border.BindingContextChanged += (_, _) =>
        {
            if (border.GestureRecognizers.OfType<TapGestureRecognizer>().FirstOrDefault() is { } tap)
            {
                tap.CommandParameter = border.BindingContext;
            }
        };
        return border;
    }

    private void ToggleShelfHeader(ShelfHeaderRow? row)
    {
        if (row is null)
        {
            return;
        }

        var target = row.Level == 0 ? _collapsedSeries : _collapsedSeasons;
        if (!target.Add(row.Key))
        {
            target.Remove(row.Key);
        }

        RenderShelfRows();
    }

    private sealed record ShelfHeaderRow(
        string Key,
        string? ParentKey,
        string Title,
        string Detail,
        int Level,
        bool IsExpanded)
    {
        public string Marker => IsExpanded ? "v" : ">";
    }

    private sealed class ShelfRowTemplateSelector : DataTemplateSelector
    {
        public DataTemplate SeriesTemplate { get; set; } = null!;

        public DataTemplate SeasonTemplate { get; set; } = null!;

        public DataTemplate MovieTemplate { get; set; } = null!;

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        {
            return item switch
            {
                ShelfHeaderRow { Level: 0 } => SeriesTemplate,
                ShelfHeaderRow => SeasonTemplate,
                _ => MovieTemplate
            };
        }
    }
}
