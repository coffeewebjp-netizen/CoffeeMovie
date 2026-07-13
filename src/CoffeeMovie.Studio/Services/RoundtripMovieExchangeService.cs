using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Models;
using CoffeeMovie.Storage.Services;

namespace CoffeeMovie.Studio.Services;

public sealed record RoundtripMovieExportResult(
    string ExportDirectory,
    int SubtitleFileCount,
    int NoteRowCount);

public sealed record RoundtripMovieImportResult(
    string MovieId,
    int SubtitleTracksUpdated,
    int SubtitleCuesUpdated,
    int LearningStatesUpdated,
    int RowsSkipped,
    IReadOnlyList<string> ChangedTrackIds,
    IReadOnlyList<string> Warnings);

public sealed class RoundtripMovieExchangeService
{
    private const int ManifestVersion = 1;
    private const string ManifestFileName = "manifest.json";
    private const string NotesFileName = "notes.csv";
    private const string SubtitlesDirectoryName = "subtitles";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<RoundtripMovieExportResult> ExportAsync(
        Movie movie,
        string exportDirectory,
        CancellationToken cancellationToken = default)
    {
        if (movie is null)
        {
            throw new ArgumentNullException(nameof(movie));
        }

        if (string.IsNullOrWhiteSpace(exportDirectory))
        {
            throw new ArgumentException("Export directory is empty.", nameof(exportDirectory));
        }

        Directory.CreateDirectory(exportDirectory);
        var subtitlesDirectory = Path.Combine(exportDirectory, SubtitlesDirectoryName);
        Directory.CreateDirectory(subtitlesDirectory);

        var manifest = new RoundtripManifest
        {
            MovieId = movie.Id,
            Title = movie.Title,
            SeriesTitle = movie.SeriesTitle,
            SeasonNumber = movie.SeasonNumber,
            EpisodeNumber = movie.EpisodeNumber,
            ExportedAt = DateTimeOffset.UtcNow
        };

        var subtitleFileCount = 0;
        for (var index = 0; index < movie.SubtitleTracks.Count; index++)
        {
            var track = movie.SubtitleTracks[index];
            var fileName = CreateSubtitleFileName(track, index + 1);
            var relativePath = Path.Combine(SubtitlesDirectoryName, fileName).Replace('\\', '/');
            var outputPath = Path.Combine(exportDirectory, relativePath);
            await File.WriteAllTextAsync(outputPath, SubtitleParser.ToSrt(track.Cues), Encoding.UTF8, cancellationToken);
            subtitleFileCount++;

            manifest.SubtitleTracks.Add(new RoundtripTrackManifest
            {
                TrackId = track.Id,
                Label = track.Label,
                Language = track.Language,
                Role = track.Role.ToString(),
                GroupKey = track.GroupKey,
                Format = SubtitleFormat.Srt.ToString(),
                SourceFileName = track.SourceFileName,
                SubtitleFile = relativePath,
                CueCount = track.Cues.Count
            });
        }

        await WriteJsonAsync(Path.Combine(exportDirectory, ManifestFileName), manifest, cancellationToken);
        var noteRowCount = await WriteNotesCsvAsync(movie, Path.Combine(exportDirectory, NotesFileName), cancellationToken);

        return new RoundtripMovieExportResult(exportDirectory, subtitleFileCount, noteRowCount);
    }

    public async Task<RoundtripMovieImportResult> ImportAsync(
        MovieLibrary library,
        Movie? selectedMovie,
        string importDirectory,
        string flagTagName,
        CancellationToken cancellationToken = default)
    {
        if (library is null)
        {
            throw new ArgumentNullException(nameof(library));
        }

        if (string.IsNullOrWhiteSpace(importDirectory))
        {
            throw new ArgumentException("Import directory is empty.", nameof(importDirectory));
        }

        var manifestPath = Path.Combine(importDirectory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Roundtrip manifest was not found.", manifestPath);
        }

        var manifest = await ReadJsonAsync<RoundtripManifest>(manifestPath, cancellationToken)
            ?? throw new InvalidOperationException("Roundtrip manifest is empty.");
        if (manifest.Version != ManifestVersion)
        {
            throw new InvalidOperationException($"Unsupported roundtrip manifest version: {manifest.Version}");
        }

        var movie = ResolveMovie(library, selectedMovie, manifest.MovieId);
        var changedTrackIds = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var subtitleTracksUpdated = 0;
        var subtitleCuesUpdated = 0;

        foreach (var trackManifest in manifest.SubtitleTracks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var track = movie.SubtitleTracks.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, trackManifest.TrackId, StringComparison.Ordinal));
            if (track is null)
            {
                warnings.Add($"Track not found: {trackManifest.TrackId}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(trackManifest.SubtitleFile))
            {
                continue;
            }

            var subtitlePath = Path.Combine(importDirectory, trackManifest.SubtitleFile.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(subtitlePath))
            {
                warnings.Add($"Subtitle file not found: {trackManifest.SubtitleFile}");
                continue;
            }

            var content = await File.ReadAllTextAsync(subtitlePath, Encoding.UTF8, cancellationToken);
            var parsed = SubtitleParser.Parse(content, subtitlePath);
            if (parsed.Cues.Count == 0)
            {
                warnings.Add($"Subtitle file contained no cues: {trackManifest.SubtitleFile}");
                continue;
            }

            var changedCueCount = ApplySubtitleCues(track, parsed.Cues);
            if (changedCueCount > 0)
            {
                subtitleTracksUpdated++;
                subtitleCuesUpdated += changedCueCount;
                changedTrackIds.Add(track.Id);
            }

            if (trackManifest.CueCount > 0 && parsed.Cues.Count != trackManifest.CueCount)
            {
                warnings.Add(
                    $"Cue count changed for {track.Label}: exported={trackManifest.CueCount}, imported={parsed.Cues.Count}");
            }
        }

        var notesPath = Path.Combine(importDirectory, NotesFileName);
        var learningStatesUpdated = 0;
        var rowsSkipped = 0;
        if (File.Exists(notesPath))
        {
            var importResult = await ImportNotesCsvAsync(movie, notesPath, flagTagName, cancellationToken);
            learningStatesUpdated = importResult.Updated;
            rowsSkipped = importResult.Skipped;
            foreach (var trackId in importResult.ChangedTrackIds)
            {
                changedTrackIds.Add(trackId);
            }

            warnings.AddRange(importResult.Warnings);
        }
        else
        {
            warnings.Add("notes.csv was not found. Subtitle files were imported without note/tag changes.");
        }

        if (subtitleCuesUpdated > 0 || learningStatesUpdated > 0)
        {
            movie.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return new RoundtripMovieImportResult(
            movie.Id,
            subtitleTracksUpdated,
            subtitleCuesUpdated,
            learningStatesUpdated,
            rowsSkipped,
            changedTrackIds.ToList(),
            warnings);
    }

    private static Movie ResolveMovie(MovieLibrary library, Movie? selectedMovie, string movieId)
    {
        if (selectedMovie is not null && string.Equals(selectedMovie.Id, movieId, StringComparison.Ordinal))
        {
            return selectedMovie;
        }

        return library.Movies.FirstOrDefault(movie => string.Equals(movie.Id, movieId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Movie was not found for roundtrip import: {movieId}");
    }

    private static int ApplySubtitleCues(SubtitleTrack track, IReadOnlyList<SubtitleCue> importedCues)
    {
        var changed = 0;
        foreach (var importedCue in importedCues)
        {
            var cue = track.Cues.FirstOrDefault(candidate => candidate.Index == importedCue.Index);
            if (cue is null)
            {
                track.Cues.Add(importedCue);
                changed++;
                continue;
            }

            if (cue.Start != importedCue.Start
                || cue.End != importedCue.End
                || !string.Equals(cue.Text, importedCue.Text, StringComparison.Ordinal))
            {
                cue.Start = importedCue.Start;
                cue.End = importedCue.End;
                cue.Text = importedCue.Text;
                changed++;
            }
        }

        if (changed > 0)
        {
            track.Cues = track.Cues
                .OrderBy(cue => cue.Index)
                .ToList();
            track.CueCount = track.Cues.Count;
        }

        return changed;
    }

    private static async Task<int> WriteNotesCsvAsync(
        Movie movie,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', NoteColumns.Select(EscapeCsvCell)));
        var rowCount = 0;

        foreach (var track in movie.SubtitleTracks)
        {
            foreach (var cue in track.Cues.OrderBy(cue => cue.Index))
            {
                var state = FindLearningState(track, cue);
                var values = new[]
                {
                    movie.Id,
                    movie.Title,
                    track.Id,
                    track.Label,
                    track.Role.ToString(),
                    track.Language ?? string.Empty,
                    cue.Id,
                    cue.Index.ToString(CultureInfo.InvariantCulture),
                    SubtitleParser.FormatSrtTimestamp(cue.Start),
                    SubtitleParser.FormatSrtTimestamp(cue.End),
                    EncodeMultilineCell(cue.Text),
                    EncodeMultilineCell(state?.AiNote),
                    EncodeMultilineCell(state?.Note),
                    string.Join("; ", state?.Tags ?? []),
                    (state?.IsFlagged == true).ToString(CultureInfo.InvariantCulture),
                    state?.CoffeeLearningRegisteredAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                    state?.CoffeeLearningWordId ?? string.Empty,
                    state?.CoffeeLearningDeckId ?? string.Empty
                };
                builder.AppendLine(string.Join(',', values.Select(EscapeCsvCell)));
                rowCount++;
            }
        }

        await File.WriteAllTextAsync(outputPath, builder.ToString(), new UTF8Encoding(true), cancellationToken);
        return rowCount;
    }

    private static async Task<NotesImportResult> ImportNotesCsvAsync(
        Movie movie,
        string notesPath,
        string flagTagName,
        CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(notesPath, Encoding.UTF8, cancellationToken);
        if (lines.Length == 0)
        {
            return new NotesImportResult(0, 0, [], ["notes.csv was empty."]);
        }

        var header = ParseCsvLine(lines[0]);
        var columnIndexes = header
            .Select((name, index) => (name, index))
            .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
        var updated = 0;
        var skipped = 0;
        var changedTrackIds = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();

        for (var rowIndex = 1; rowIndex < lines.Length; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(lines[rowIndex]))
            {
                continue;
            }

            var row = ParseCsvLine(lines[rowIndex]);
            var rowMovieId = GetCsvValue(row, columnIndexes, "movieId");
            if (!string.Equals(rowMovieId, movie.Id, StringComparison.Ordinal))
            {
                skipped++;
                continue;
            }

            var trackId = GetCsvValue(row, columnIndexes, "trackId");
            var cueIndexText = GetCsvValue(row, columnIndexes, "cueIndex");
            if (string.IsNullOrWhiteSpace(trackId)
                || !int.TryParse(cueIndexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cueIndex))
            {
                skipped++;
                warnings.Add($"Invalid row key at notes.csv line {rowIndex + 1}.");
                continue;
            }

            var track = movie.SubtitleTracks.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, trackId, StringComparison.Ordinal));
            var cue = track?.Cues.FirstOrDefault(candidate => candidate.Index == cueIndex);
            if (track is null || cue is null)
            {
                skipped++;
                warnings.Add($"Cue not found at notes.csv line {rowIndex + 1}: track={trackId}, cue={cueIndex}");
                continue;
            }

            var state = EnsureLearningState(track, cue);
            var tags = TagFilterService.ParseTags(GetCsvValue(row, columnIndexes, "tags"));
            var isFlagged = ParseBoolean(GetCsvValue(row, columnIndexes, "isFlagged"));
            if (isFlagged)
            {
                AddTag(tags, flagTagName);
            }
            else
            {
                tags.RemoveAll(tag => TagFilterService.IsFlagTag(tag, flagTagName));
            }

            var aiNote = NormalizeOptionalText(DecodeMultilineCell(GetCsvValue(row, columnIndexes, "aiNote")));
            var userNote = NormalizeOptionalText(DecodeMultilineCell(GetCsvValue(row, columnIndexes, "userNote")));
            var registeredAt = ParseDateTimeOffset(GetCsvValue(row, columnIndexes, "coffeeLearningRegisteredAt"));
            var wordId = NormalizeOptionalText(GetCsvValue(row, columnIndexes, "coffeeLearningWordId"));
            var deckId = NormalizeOptionalText(GetCsvValue(row, columnIndexes, "coffeeLearningDeckId"));

            var dirty = state.IsFlagged != isFlagged
                || !state.Tags.SequenceEqual(tags, StringComparer.OrdinalIgnoreCase)
                || !string.Equals(state.AiNote, aiNote, StringComparison.Ordinal)
                || !string.Equals(state.Note, userNote, StringComparison.Ordinal)
                || state.CoffeeLearningRegisteredAt != registeredAt
                || !string.Equals(state.CoffeeLearningWordId, wordId, StringComparison.Ordinal)
                || !string.Equals(state.CoffeeLearningDeckId, deckId, StringComparison.Ordinal);
            if (!dirty)
            {
                continue;
            }

            state.IsFlagged = isFlagged;
            state.Tags = tags;
            state.AiNote = aiNote;
            state.Note = userNote;
            state.CoffeeLearningRegisteredAt = registeredAt;
            state.CoffeeLearningWordId = wordId;
            state.CoffeeLearningDeckId = deckId;
            state.UpdatedAt = DateTimeOffset.UtcNow;
            updated++;
            changedTrackIds.Add(track.Id);
        }

        return new NotesImportResult(updated, skipped, changedTrackIds.ToList(), warnings);
    }

    private static SubtitleCueLearningState? FindLearningState(SubtitleTrack track, SubtitleCue cue)
    {
        return track.CueLearningStates.FirstOrDefault(state =>
            string.Equals(state.CueId, cue.Id, StringComparison.Ordinal)
            || state.CueIndex == cue.Index);
    }

    private static SubtitleCueLearningState EnsureLearningState(SubtitleTrack track, SubtitleCue cue)
    {
        var state = FindLearningState(track, cue);
        if (state is not null)
        {
            if (string.IsNullOrWhiteSpace(state.CueId))
            {
                state.CueId = cue.Id;
            }

            return state;
        }

        state = new SubtitleCueLearningState
        {
            CueId = cue.Id,
            CueIndex = cue.Index
        };
        track.CueLearningStates.Add(state);
        return state;
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static string CreateSubtitleFileName(SubtitleTrack track, int index)
    {
        var role = string.IsNullOrWhiteSpace(track.Role.ToString()) ? "track" : track.Role.ToString().ToLowerInvariant();
        var language = string.IsNullOrWhiteSpace(track.Language) ? "und" : track.Language.Trim().ToLowerInvariant();
        var label = string.IsNullOrWhiteSpace(track.Label) ? track.SourceFileName : track.Label;
        var safeLabel = CreateSafeFileName(label);
        var shortId = track.Id.Length <= 8 ? track.Id : track.Id[..8];
        return string.Create(CultureInfo.InvariantCulture, $"{index:00}.{role}.{language}.{safeLabel}.{shortId}.srt");
    }

    private static string CreateSafeFileName(string? value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder();
        foreach (var character in value ?? string.Empty)
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        var normalized = builder.ToString().Trim();
        if (normalized.Length == 0)
        {
            normalized = "subtitle";
        }

        return normalized.Length <= 60 ? normalized : normalized[..60];
    }

    private static string EscapeCsvCell(string? value)
    {
        value ??= string.Empty;
        return value.Contains('"', StringComparison.Ordinal)
            || value.Contains(',', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal)
            || value.Contains('\r', StringComparison.Ordinal)
                ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
                : value;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (character == ',' && !inQuotes)
            {
                values.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(character);
        }

        values.Add(builder.ToString());
        return values;
    }

    private static string GetCsvValue(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> columnIndexes,
        string columnName)
    {
        return columnIndexes.TryGetValue(columnName, out var index) && index >= 0 && index < row.Count
            ? row[index]
            : string.Empty;
    }

    private static bool ParseBoolean(string? value)
    {
        return bool.TryParse(value, out var parsed) && parsed;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static string EncodeMultilineCell(string? value)
    {
        return value?
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal) ?? string.Empty;
    }

    private static string DecodeMultilineCell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\\' && index + 1 < value.Length)
            {
                var next = value[index + 1];
                if (next == 'n')
                {
                    builder.Append('\n');
                    index++;
                    continue;
                }

                if (next == '\\')
                {
                    builder.Append('\\');
                    index++;
                    continue;
                }
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string? NormalizeOptionalText(string? text)
    {
        var normalized = text?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void AddTag(List<string> tags, string tag)
    {
        var normalized = tag.Trim();
        if (normalized.Length == 0
            || tags.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        tags.Add(normalized);
    }

    private static readonly string[] NoteColumns =
    [
        "movieId",
        "movieTitle",
        "trackId",
        "trackLabel",
        "trackRole",
        "trackLanguage",
        "cueId",
        "cueIndex",
        "start",
        "end",
        "text",
        "aiNote",
        "userNote",
        "tags",
        "isFlagged",
        "coffeeLearningRegisteredAt",
        "coffeeLearningWordId",
        "coffeeLearningDeckId"
    ];

    private sealed class RoundtripManifest
    {
        public int Version { get; set; } = ManifestVersion;

        public string MovieId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string? SeriesTitle { get; set; }

        public int? SeasonNumber { get; set; }

        public int? EpisodeNumber { get; set; }

        public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;

        public List<RoundtripTrackManifest> SubtitleTracks { get; set; } = [];
    }

    private sealed class RoundtripTrackManifest
    {
        public string TrackId { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public string? Language { get; set; }

        public string Role { get; set; } = string.Empty;

        public string? GroupKey { get; set; }

        public string Format { get; set; } = string.Empty;

        public string SourceFileName { get; set; } = string.Empty;

        public string SubtitleFile { get; set; } = string.Empty;

        public int CueCount { get; set; }
    }

    private sealed record NotesImportResult(
        int Updated,
        int Skipped,
        IReadOnlyList<string> ChangedTrackIds,
        IReadOnlyList<string> Warnings);
}
