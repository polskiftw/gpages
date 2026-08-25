namespace GDupe.Core.Models;

public sealed record FileRecord(
    string FullPath,
    long SizeBytes,
    long LastWriteUtcTicks,
    string Sha256,
    MediaKind Kind,
    int? Width,
    int? Height,
    long? DurationMilliseconds,
    long IndexedUtcTicks,
    string? Error = null)
{
    public string Name => Path.GetFileName(FullPath);
    public DateTime LastWriteUtc => new(LastWriteUtcTicks, DateTimeKind.Utc);
    public DateTime ModifiedLocal => LastWriteUtc.ToLocalTime();
    public string SizeDisplay => FormatBytes(SizeBytes);
    public string DimensionsDisplay => Width.HasValue && Height.HasValue ? $"{Width} × {Height}" : "—";
    public string DurationDisplay => DurationMilliseconds.HasValue
        ? TimeSpan.FromMilliseconds(DurationMilliseconds.Value).ToString(DurationMilliseconds.Value >= 3_600_000 ? @"h\:mm\:ss" : @"m\:ss")
        : "—";

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var suffix = 0;
        while (value >= 1024 && suffix < suffixes.Length - 1)
        {
            value /= 1024;
            suffix++;
        }
        return $"{value:0.##} {suffixes[suffix]}";
    }
}
