using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Speech;
using CoffeeMovie.Reader.Services;
using Application = Android.App.Application;

namespace CoffeeMovie.Reader.Platforms.Android;

public sealed class AndroidSpeechRecognitionService : Java.Lang.Object, ISpeechRecognitionService
{
    public async Task<string> RecognizeEnglishAsync(CancellationToken cancellationToken = default)
    {
        var permission = await Permissions.RequestAsync<Permissions.Microphone>();
        if (permission != PermissionStatus.Granted)
        {
            throw new InvalidOperationException("音声入力にはマイク権限が必要です。");
        }

        if (!SpeechRecognizer.IsRecognitionAvailable(Application.Context))
        {
            throw new InvalidOperationException("この端末で音声認識を利用できません。Google音声入力などを有効にしてください。");
        }

        return await MainThread.InvokeOnMainThreadAsync(() => RecognizeOnMainThreadAsync(cancellationToken));
    }

    private static Task<string> RecognizeOnMainThreadAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recognizer = SpeechRecognizer.CreateSpeechRecognizer(Application.Context);
        if (recognizer is null)
        {
            completion.SetException(new InvalidOperationException("音声認識エンジンを開始できませんでした。"));
            return completion.Task;
        }

        var listener = new SpeechRecognitionListener(completion);
        recognizer.SetRecognitionListener(listener);

        var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
        intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
        intent.PutExtra(RecognizerIntent.ExtraLanguage, "en-US");
        intent.PutExtra(RecognizerIntent.ExtraLanguagePreference, "en-US");
        intent.PutExtra(RecognizerIntent.ExtraOnlyReturnLanguagePreference, "en-US");
        intent.PutExtra(RecognizerIntent.ExtraPartialResults, false);
        intent.PutExtra(RecognizerIntent.ExtraMaxResults, 3);
        intent.PutExtra(RecognizerIntent.ExtraPrompt, "Shadow the current subtitle");

        CancellationTokenRegistration registration = default;
        registration = cancellationToken.Register(() =>
        {
            try
            {
                recognizer.Cancel();
            }
            catch
            {
                // Best-effort cancellation.
            }

            completion.TrySetCanceled(cancellationToken);
        });

        completion.Task.ContinueWith(_ =>
        {
            registration.Dispose();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    recognizer.Destroy();
                }
                catch
                {
                    // SpeechRecognizer cleanup is best-effort.
                }
            });
        }, TaskScheduler.Default);

        recognizer.StartListening(intent);
        return completion.Task;
    }

    private sealed class SpeechRecognitionListener(TaskCompletionSource<string> completion) : Java.Lang.Object, IRecognitionListener
    {
        public void OnBeginningOfSpeech()
        {
        }

        public void OnBufferReceived(byte[]? buffer)
        {
        }

        public void OnEndOfSpeech()
        {
        }

        public void OnEvent(int eventType, Bundle? @params)
        {
        }

        public void OnPartialResults(Bundle? partialResults)
        {
        }

        public void OnReadyForSpeech(Bundle? @params)
        {
        }

        public void OnRmsChanged(float rmsdB)
        {
        }

        public void OnError([GeneratedEnum] SpeechRecognizerError error)
        {
            completion.TrySetException(new InvalidOperationException(CreateErrorMessage(error)));
        }

        public void OnResults(Bundle? results)
        {
            var matches = results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
            var transcript = matches?.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
            if (string.IsNullOrWhiteSpace(transcript))
            {
                completion.TrySetException(new InvalidOperationException("音声を文字列として認識できませんでした。"));
                return;
            }

            completion.TrySetResult(transcript);
        }

        private static string CreateErrorMessage(SpeechRecognizerError error)
        {
            return error switch
            {
                SpeechRecognizerError.NoMatch => "音声を認識できませんでした。もう一度発音してください。",
                SpeechRecognizerError.SpeechTimeout => "音声が検出されませんでした。",
                SpeechRecognizerError.Network or SpeechRecognizerError.NetworkTimeout => "音声認識のネットワーク処理に失敗しました。",
                SpeechRecognizerError.InsufficientPermissions => "マイク権限がありません。",
                SpeechRecognizerError.RecognizerBusy => "音声認識が処理中です。少し待ってから再試行してください。",
                _ => $"音声認識に失敗しました: {error}"
            };
        }
    }
}
