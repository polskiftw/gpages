namespace GDupe.Core.Models;

public sealed record ScanProgress(string CurrentPath, int FilesIndexed, int Failures, bool IsWatching = false);
