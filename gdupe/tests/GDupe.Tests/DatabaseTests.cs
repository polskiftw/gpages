using GDupe.Core.Data;
using GDupe.Core.Models;

namespace GDupe.Tests;

public sealed class DatabaseTests
{
    [Fact]
    public async Task PersistsRecordsAcrossConnections()
    {
        using var temp = new TempDirectory();
        var databasePath = temp.File("index.db");
        var filePath = temp.File("sample.jpg");
        var expected = new FileRecord(filePath, 42, 123, "ABC", MediaKind.Image, 12, 34, null, 456);

        await using (var first = new SqliteFileDatabase(databasePath))
        {
            await first.InitializeAsync();
            await first.UpsertAsync(expected);
        }

        await using var second = new SqliteFileDatabase(databasePath);
        await second.InitializeAsync();
        var actual = await second.GetAsync(filePath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ReturnsOnlyRealDuplicateGroups()
    {
        using var temp = new TempDirectory();
        await using var database = new SqliteFileDatabase(temp.File("index.db"));
        await database.InitializeAsync();
        await database.UpsertAsync(Record(temp.File("a.jpg"), "SAME", 100));
        await database.UpsertAsync(Record(temp.File("b.jpg"), "SAME", 100));
        await database.UpsertAsync(Record(temp.File("c.jpg"), "UNIQUE", 100));

        var groups = await database.GetDuplicateGroupsAsync();

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Count);
        Assert.Equal(100, group.ReclaimableBytes);
    }

    private static FileRecord Record(string path, string hash, long size) =>
        new(path, size, DateTime.UtcNow.Ticks, hash, MediaKind.Image, null, null, null, DateTime.UtcNow.Ticks);
}
