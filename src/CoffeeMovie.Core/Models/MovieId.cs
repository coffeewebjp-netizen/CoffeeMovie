namespace CoffeeMovie.Core.Models;

public static class MovieId
{
    public static string New()
    {
        return $"movie-{Guid.NewGuid().ToString("N")[..12]}";
    }
}

