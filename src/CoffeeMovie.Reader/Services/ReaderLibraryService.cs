using System.Text;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Services;

namespace CoffeeMovie.Reader.Services;

public sealed class ReaderLibraryService
{
    private readonly CoffeeMoviePaths _paths;
    private readonly MovieLibraryStore _libraryStore;

    public ReaderLibraryService()
    {
        _paths = new CoffeeMoviePaths(FileSystem.AppDataDirectory, FileSystem.CacheDirectory);
        _paths.EnsureCreated();
        _libraryStore = new MovieLibraryStore(_paths);
    }

    public async Task<IReadOnlyList<Movie>> LoadMoviesAsync(CancellationToken cancellationToken = default)
    {
        var library = await _libraryStore.LoadAsync(cancellationToken);
        return library.Movies
            .OrderByDescending(movie => movie.UpdatedAt)
            .ToList();
    }

    public async Task<Movie?> GetMovieAsync(string movieId, CancellationToken cancellationToken = default)
    {
        var library = await _libraryStore.LoadAsync(cancellationToken);
        return library.Movies.FirstOrDefault(movie => string.Equals(movie.Id, movieId, StringComparison.Ordinal));
    }

    public async Task<Movie> ImportVideoAsync(FileResult file, CancellationToken cancellationToken = default)
    {
        var movieId = MovieId.New();
        var safeFileName = SanitizeFileName(file.FileName);
        var movieDirectory = _paths.GetMovieVideoDirectory(movieId);
        Directory.CreateDirectory(movieDirectory);
        var targetPath = EnsureUniquePath(Path.Combine(movieDirectory, safeFileName));

        await using (var input = await file.OpenReadAsync())
        await using (var output = File.Create(targetPath))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        var fileInfo = new FileInfo(targetPath);
        var movie = new Movie
        {
            Id = movieId,
            Title = Path.GetFileNameWithoutExtension(file.FileName),
            Video = new VideoAsset
            {
                SourceKind = VideoSourceKind.LocalFile,
                SourceUri = file.FullPath ?? file.FileName,
                SourceKey = $"local:{movieId}",
                FileName = safeFileName,
                ContentType = GuessVideoContentType(safeFileName, file.ContentType),
                SizeBytes = fileInfo.Length,
                ModifiedAt = fileInfo.LastWriteTimeUtc,
                CachePath = targetPath
            }
        };

        await _libraryStore.UpsertMovieAsync(movie, cancellationToken);
        return movie;
    }

    public async Task<SubtitleTrack> ImportSubtitleAsync(
        Movie movie,
        FileResult file,
        CancellationToken cancellationToken = default)
    {
        var safeFileName = SanitizeFileName(file.FileName);
        var subtitleDirectory = _paths.GetMovieSubtitleDirectory(movie.Id);
        Directory.CreateDirectory(subtitleDirectory);

        string content;
        await using (var input = await file.OpenReadAsync())
        using (var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            content = await reader.ReadToEndAsync(cancellationToken);
        }

        var originalPath = EnsureUniquePath(Path.Combine(subtitleDirectory, safeFileName));
        await File.WriteAllTextAsync(originalPath, content, Encoding.UTF8, cancellationToken);

        var document = SubtitleParser.Parse(content, file.FileName);
        if (document.Cues.Count == 0)
        {
            throw new InvalidOperationException("字幕キューが見つかりませんでした。SRT または WebVTT の形式を確認してください。");
        }

        var vttPath = Path.Combine(subtitleDirectory, Path.GetFileNameWithoutExtension(safeFileName) + ".vtt");
        await File.WriteAllTextAsync(vttPath, SubtitleParser.ToWebVtt(document.Cues), Encoding.UTF8, cancellationToken);

        var metadata = SubtitleFileMetadataService.Infer(file.FileName);
        var track = new SubtitleTrack
        {
            Label = metadata.Label,
            Language = metadata.Language,
            Role = metadata.Role,
            GroupKey = metadata.GroupKey,
            Format = document.Format,
            SourceFileName = file.FileName,
            LocalPath = originalPath,
            VttCachePath = vttPath,
            CueCount = document.Cues.Count,
            Cues = document.Cues
        };

        movie.SubtitleTracks.RemoveAll(existing =>
            string.Equals(existing.SourceFileName, track.SourceFileName, StringComparison.OrdinalIgnoreCase));
        movie.SubtitleTracks.Add(track);
        movie.SceneMarkers = SubtitleSceneFactory.CreateSceneMarkers(track);
        await _libraryStore.UpsertMovieAsync(movie, cancellationToken);
        return track;
    }

    private static string EnsureUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, $"{name}-{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"保存先ファイル名を決定できませんでした: {path}");
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(fileName.Length);
        foreach (var character in fileName)
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        var sanitized = builder.ToString().Trim();
        return sanitized.Length == 0 ? "movie" : sanitized;
    }

    private static string GuessVideoContentType(string fileName, string? pickerContentType)
    {
        if (!string.IsNullOrWhiteSpace(pickerContentType))
        {
            return pickerContentType;
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".m4v" => "video/x-m4v",
            ".mkv" => "video/x-matroska",
            _ => "video/mp4"
        };
    }
}

