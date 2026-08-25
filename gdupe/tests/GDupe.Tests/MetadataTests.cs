using GDupe.Core.Models;
using GDupe.Core.Services;

namespace GDupe.Tests;

public sealed class MetadataTests
{
    [Fact]
    public async Task ReadsPngDimensionsWithoutLockingTheFile()
    {
        using var temp = new TempDirectory();
        var path = temp.File("image.png");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        var reader = new MediaMetadataReader();

        var metadata = await reader.ReadAsync(path, MediaKind.Image, CancellationToken.None);
        File.Delete(path);

        Assert.Equal(1, metadata.Width);
        Assert.Equal(1, metadata.Height);
    }
}
