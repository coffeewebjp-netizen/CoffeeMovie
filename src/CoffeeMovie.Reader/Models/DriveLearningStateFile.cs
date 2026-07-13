namespace CoffeeMovie.Reader.Models;

public sealed record DriveLearningStateFile(
    string Id,
    string FileName,
    long? LastModified,
    long? Size);
