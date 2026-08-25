using GDupe.Core.Abstractions;
using GDupe.Core.Models;

namespace GDupe.Core.Services;

public sealed class FileIndexService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic", ".avif"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".avi", ".mkv", ".webm", ".wmv", ".mpeg", ".mpg", ".ts"
    };

    private readonly IFileDatabase _database;
    private readonly IFileHasher _hasher;
    private readonly IMediaMetadataReader _metadataReader;
    private readonly IAppLogger _logger;

    public FileIndexService(IFileDatabase database, IFileHasher hasher, IMediaMetadataReader metadataReader, IAppLogger logger)
    {
        _database = database;
        _hasher = hasher;
        _metadataReader = metadataReader;
        _logger = logger;
    }

    public static bool IsSupported(string path) => GetKind(path) is not null;

    public Task KeepOnlyRootAsync(string root, CancellationToken cancellationToken) =>
        _database.KeepOnlyRootAsync(Path.GetFullPath(root), cancellationToken);

    public async Task ScanAsync(string root, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        root = Path.GetFullPath(root);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var indexed = 0;
        var failures = 0;

        foreach (var path in EnumerateMediaFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            seen.Add(path);
            try
            {
                await IndexPathAsync(path, cancellationToken);
                indexed++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                failures++;
                _logger.Error($"Could not index '{path}'.", ex);
            }

            progress?.Report(new(path, indexed, failures));
        }

        await _database.ReconcileRootAsync(root, seen, cancellationToken);
        progress?.Report(new(root, indexed, failures, true));
    }

    public async Task IndexPathAsync(string path, CancellationToken cancellationToken)
    {
        path = Path.GetFullPath(path);
        var kind = GetKind(path);
        if (kind is null)
        {
            await _database.RemoveAsync(path, cancellationToken);
            return;
        }

        if (!File.Exists(path))
        {
            await _database.RemoveAsync(path, cancellationToken);
            return;
        }

        Exception? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var before = new FileInfo(path);
                var existing = await _database.GetAsync(path, cancellationToken);
                if (existing is not null && existing.SizeBytes == before.Length && existing.LastWriteUtcTicks == before.LastWriteTimeUtc.Ticks)
                {
                    return;
                }

                var hash = await _hasher.ComputeSha256Async(path, cancellationToken);
                var metadata = await _metadataReader.ReadAsync(path, kind.Value, cancellationToken);
                var after = new FileInfo(path);
                if (before.Length != after.Length || before.LastWriteTimeUtc.Ticks != after.LastWriteTimeUtc.Ticks)
                {
                    lastError = new IOException("The file changed while it was being indexed.");
                    await Task.Delay(TimeSpan.FromMilliseconds(150 * (attempt + 1)), cancellationToken);
                    continue;
                }

                var record = new FileRecord(
                    path,
                    after.Length,
                    after.LastWriteTimeUtc.Ticks,
                    hash,
                    kind.Value,
                    metadata.Width,
                    metadata.Height,
                    metadata.DurationMilliseconds,
                    DateTime.UtcNow.Ticks);
                await _database.UpsertAsync(record, cancellationToken);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (attempt < 2)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(150 * (attempt + 1)), cancellationToken);
            }
        }

        throw new IOException($"The file could not be indexed after three attempts: {path}", lastError);
    }

    private static MediaKind? GetKind(string path)
    {
        var extension = Path.GetExtension(path);
        if (ImageExtensions.Contains(extension)) return MediaKind.Image;
        if (VideoExtensions.Contains(extension)) return MediaKind.Video;
        return null;
    }

    private IEnumerable<string> EnumerateMediaFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> directories;
            IEnumerable<string> files;
            try
            {
                directories = Directory.EnumerateDirectories(directory).ToArray();
                files = Directory.EnumerateFiles(directory).ToArray();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                _logger.Error($"Could not enumerate '{directory}'.", ex);
                continue;
            }

            foreach (var child in directories) pending.Push(child);
            foreach (var file in files)
            {
                if (IsSupported(file)) yield return Path.GetFullPath(file);
            }
        }
    }
}
