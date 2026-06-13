using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoffeeMovie.Storage.Services;

internal static class JsonStoreOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };
}

