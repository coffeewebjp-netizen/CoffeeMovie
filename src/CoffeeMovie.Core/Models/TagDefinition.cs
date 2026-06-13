namespace CoffeeMovie.Core.Models;

public sealed class TagDefinition
{
    public string Name { get; set; } = string.Empty;

    public TagScope Scope { get; set; } = TagScope.Movie;

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
