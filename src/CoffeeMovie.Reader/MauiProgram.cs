using CoffeeMovie.Reader.Services;
using Microsoft.Extensions.Logging;
#if ANDROID
using CoffeeMovie.Reader.Platforms.Android;
using Microsoft.Maui.Handlers;
#endif

namespace CoffeeMovie.Reader;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if ANDROID
        WebViewHandler.Mapper.AppendToMapping("CoffeeMovieFilePlayback", (handler, _) =>
        {
            handler.PlatformView.Settings.AllowFileAccess = true;
            handler.PlatformView.Settings.AllowContentAccess = true;
            handler.PlatformView.Settings.AllowFileAccessFromFileURLs = true;
            handler.PlatformView.Settings.AllowUniversalAccessFromFileURLs = true;
            handler.PlatformView.Settings.MediaPlaybackRequiresUserGesture = false;
        });
#endif

        builder.Services.AddSingleton<ReaderSyncSettingsService>();
        builder.Services.AddSingleton<GoogleDriveSyncService>();
        builder.Services.AddSingleton<CoffeeLearningWordRegistrationService>();
        builder.Services.AddSingleton<ReaderLibraryService>();
#if ANDROID
        builder.Services.AddSingleton<ISpeechRecognitionService, AndroidSpeechRecognitionService>();
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

