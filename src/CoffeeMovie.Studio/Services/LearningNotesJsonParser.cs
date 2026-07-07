using System.Globalization;
using System.Text.Json;

namespace CoffeeMovie.Studio.Services;

internal static class LearningNotesJsonParser
{
    internal static List<LearningNoteImportRow> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var notesElement = root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (TryGetProperty(root, "notes", out var notesProperty))
            {
                notesElement = notesProperty;
            }
            else if (TryGetProperty(root, "items", out var itemsProperty))
            {
                notesElement = itemsProperty;
            }
        }

        if (notesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new List<LearningNoteImportRow>();
        foreach (var item in notesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            rows.Add(new LearningNoteImportRow(
                TryGetInt(item, "index")
                    ?? TryGetInt(item, "cueIndex")
                    ?? TryGetInt(item, "cue_index")
                    ?? 0,
                TryGetString(item, "cefr")
                    ?? TryGetString(item, "level"),
                TryGetString(item, "focus")
                    ?? TryGetString(item, "phrase")
                    ?? TryGetString(item, "word"),
                TryGetString(item, "note")
                    ?? TryGetString(item, "memo")
                    ?? TryGetString(item, "comment")));
        }

        return rows;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static int? TryGetInt(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }
}
