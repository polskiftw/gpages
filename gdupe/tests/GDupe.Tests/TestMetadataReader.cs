using GDupe.Core.Abstractions;
using GDupe.Core.Models;

namespace GDupe.Tests;

internal sealed class TestMetadataReader : IMediaMetadataReader
{
    public Task<MediaMetadata> ReadAsync(string path, MediaKind kind, CancellationToken cancellationToken) =>
        Task.FromResult(new MediaMetadata(100, 50, kind == MediaKind.Video ? 1234 : null));
}
