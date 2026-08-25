using GDupe.Core.Models;

namespace GDupe.Core.Abstractions;

public interface IMediaMetadataReader
{
    Task<MediaMetadata> ReadAsync(string path, MediaKind kind, CancellationToken cancellationToken);
}
