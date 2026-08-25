namespace GDupe.Core.Models;

public sealed record DuplicateGroup(string Sha256, long FileSize, IReadOnlyList<FileRecord> Files)
{
    public int Count => Files.Count;
    public long ReclaimableBytes => FileSize * Math.Max(0, Count - 1);
    public string Title => $"{Count} copies · {FormatBytes(ReclaimableBytes)} reclaimable";

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
