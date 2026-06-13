using CoffeeMovie.Reader.Services;
using Microsoft.Extensions.Logging;

namespace CoffeeMovie.Reader;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton<ReaderLibraryService>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

