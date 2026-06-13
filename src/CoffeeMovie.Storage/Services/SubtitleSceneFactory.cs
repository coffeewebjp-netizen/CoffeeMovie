using CoffeeMovie.Core.Models;

namespace CoffeeMovie.Storage.Services;

public static class SubtitleSceneFactory
{
    public static List<SceneMarker> CreateSceneMarkers(SubtitleTrack track, int maxMarkers = 300)
    {
        return track.Cues
            .Where(cue => !string.IsNullOrWhiteSpace(cue.Text))
            .Take(maxMarkers)
            .Select(cue => new SceneMarker
            {
                Label = Compact(cue.Text),
                Start = cue.Start,
                End = cue.End,
                SourceSubtitleTrackId = track.Id,
                SourceCueIndex = cue.Index
            })
            .ToList();
    }

    private static string Compact(string text)
    {
        var normalized = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 80 ? normalized : normalized[..77] + "...";
    }
}

