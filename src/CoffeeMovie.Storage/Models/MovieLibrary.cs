using CoffeeMovie.Core.Models;

namespace CoffeeMovie.Storage.Models;

public sealed class MovieLibrary
{
    public List<Movie> Movies { get; set; } = [];
}

