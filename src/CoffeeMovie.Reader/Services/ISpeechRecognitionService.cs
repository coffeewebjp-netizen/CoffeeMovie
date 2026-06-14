namespace CoffeeMovie.Reader.Services;

public interface ISpeechRecognitionService
{
    Task<string> RecognizeEnglishAsync(CancellationToken cancellationToken = default);
}
