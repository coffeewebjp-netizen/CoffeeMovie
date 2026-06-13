using CoffeeMovie.Core.Models;

namespace CoffeeMovie.Storage.Models;

public sealed class MovieLibrary
{
    public StudioPreferences Studio { get; set; } = new();

    public List<TagDefinition> TagDefinitions { get; set; } = [];

    public List<Movie> Movies { get; set; } = [];
}

