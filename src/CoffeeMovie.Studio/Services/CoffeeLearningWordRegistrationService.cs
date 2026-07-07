using CoffeeMovie.Core.Services;

namespace CoffeeMovie.Studio.Services;

public sealed class CoffeeLearningWordRegistrationService
{
    public const string DefaultBaseUrl = CoffeeLearningRegistrationDefaults.DefaultBaseUrl;
    public const string DefaultDeckId = CoffeeLearningRegistrationDefaults.DefaultDeckId;

    private readonly CoffeeLearningRegistrationClient _client = new();

    public static bool IsConfigured(CoffeeLearningConnectionSettings settings)
    {
        return CoffeeLearningRegistrationClient.IsConfigured(settings);
    }

    public Task<CoffeeLearningWordRegistrationResult> RegisterWordAsync(
        CoffeeLearningConnectionSettings settings,
        CoffeeLearningWordRegistrationRequest registration,
        CancellationToken cancellationToken = default)
    {
        return _client.RegisterWordAsync(settings, registration, cancellationToken);
    }

    public static string NormalizeBaseUrl(string? baseUrl)
    {
        return CoffeeLearningRegistrationClient.NormalizeBaseUrl(baseUrl);
    }
}
