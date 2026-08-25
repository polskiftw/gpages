using GDupe.Core.Data;
using GDupe.Core.Services;

namespace GDupe.Tests;

public sealed class FileIndexServiceTests
{
    [Fact]
    public async Task ScanFindsExactDuplicatesAndIgnoresUnsupportedFiles()
    {
        using var temp = new TempDirectory();
        await File.WriteAllBytesAsync(temp.File("one.png"), PngBytes);
        await File.WriteAllBytesAsync(temp.File("two.png"), PngBytes);
        await File.WriteAllTextAsync(temp.File("notes.txt"), "same bytes do not make unsupported files media");

        await using var database = new SqliteFileDatabase(temp.File("index.db"));
        await database.InitializeAsync();
        var service = CreateService(database);
        await service.ScanAsync(temp.Path, null, CancellationToken.None);

        var all = await database.GetAllAsync();
        var duplicates = await database.GetDuplicateGroupsAsync();
        Assert.Collection(all, _ => { }, _ => { });
        Assert.Single(duplicates);
        Assert.Equal(2, duplicates[0].Count);
    }

    [Fact]
    public async Task RehashesChangedFilesAndRemovesDeletedFiles()
    {
        using var temp = new TempDirectory();
        var firstPath = temp.File("one.jpg");
        var secondPath = temp.File("two.jpg");
        await File.WriteAllTextAsync(firstPath, "first version");
        await File.WriteAllTextAsync(secondPath, "first version");

        await using var database = new SqliteFileDatabase(temp.File("index.db"));
        await database.InitializeAsync();
        var service = CreateService(database);
        await service.ScanAsync(temp.Path, null, CancellationToken.None);
        Assert.Single(await database.GetDuplicateGroupsAsync());

        await File.WriteAllTextAsync(firstPath, "a completely different and longer second version");
        File.SetLastWriteTimeUtc(firstPath, DateTime.UtcNow.AddSeconds(1));
        File.Delete(secondPath);
        await service.ScanAsync(temp.Path, null, CancellationToken.None);

        var remaining = Assert.Single(await database.GetAllAsync());
        Assert.Equal(firstPath, remaining.FullPath, ignoreCase: true);
        Assert.Empty(await database.GetDuplicateGroupsAsync());
    }

    [Fact]
    public async Task CancelledScanDoesNotReconcileAwayExistingRecords()
    {
        using var temp = new TempDirectory();
        var path = temp.File("one.png");
        await File.WriteAllBytesAsync(path, PngBytes);
        await using var database = new SqliteFileDatabase(temp.File("index.db"));
        await database.InitializeAsync();
        var service = CreateService(database);
        await service.ScanAsync(temp.Path, null, CancellationToken.None);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ScanAsync(temp.Path, null, cancellation.Token));

        Assert.Single(await database.GetAllAsync());
    }

    private static FileIndexService CreateService(SqliteFileDatabase database) =>
        new(database, new Sha256FileHasher(), new TestMetadataReader(), new NullLogger());

    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
}
