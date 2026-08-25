using GDupe.Core.Abstractions;
using GDupe.Core.Models;
using Microsoft.Data.Sqlite;

namespace GDupe.Core.Data;

public sealed class SqliteFileDatabase : IFileDatabase
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqliteFileDatabase(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        _connection = new SqliteConnection(builder.ToString());
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _connection.OpenAsync(cancellationToken);
            await using var command = _connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                CREATE TABLE IF NOT EXISTS files (
                    full_path TEXT PRIMARY KEY COLLATE NOCASE,
                    size_bytes INTEGER NOT NULL,
                    last_write_utc_ticks INTEGER NOT NULL,
                    sha256 TEXT NOT NULL,
                    media_kind INTEGER NOT NULL,
                    width INTEGER NULL,
                    height INTEGER NULL,
                    duration_ms INTEGER NULL,
                    indexed_utc_ticks INTEGER NOT NULL,
                    error TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_files_hash_size ON files(sha256, size_bytes);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FileRecord?> GetAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT * FROM files WHERE full_path = $path;";
            command.Parameters.AddWithValue("$path", Path.GetFullPath(fullPath));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
        }
        finally { _gate.Release(); }
    }

    public async Task UpsertAsync(FileRecord record, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO files(full_path, size_bytes, last_write_utc_ticks, sha256, media_kind, width, height, duration_ms, indexed_utc_ticks, error)
                VALUES($path, $size, $write, $hash, $kind, $width, $height, $duration, $indexed, $error)
                ON CONFLICT(full_path) DO UPDATE SET
                    size_bytes = excluded.size_bytes,
                    last_write_utc_ticks = excluded.last_write_utc_ticks,
                    sha256 = excluded.sha256,
                    media_kind = excluded.media_kind,
                    width = excluded.width,
                    height = excluded.height,
                    duration_ms = excluded.duration_ms,
                    indexed_utc_ticks = excluded.indexed_utc_ticks,
                    error = excluded.error;
                """;
            AddRecordParameters(command, record);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task RemoveAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM files WHERE full_path = $path;";
            command.Parameters.AddWithValue("$path", Path.GetFullPath(fullPath));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task KeepOnlyRootAsync(string root, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM files WHERE substr(full_path, 1, length($root)) <> $root COLLATE NOCASE;";
            command.Parameters.AddWithValue("$root", EnsureTrailingSeparator(Path.GetFullPath(root)));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task ReconcileRootAsync(string root, IReadOnlyCollection<string> seenPaths, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
            await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
            await using (var create = _connection.CreateCommand())
            {
                create.Transaction = (SqliteTransaction)transaction;
                create.CommandText = "CREATE TEMP TABLE IF NOT EXISTS seen_paths(path TEXT PRIMARY KEY COLLATE NOCASE); DELETE FROM seen_paths;";
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var insert = _connection.CreateCommand())
            {
                insert.Transaction = (SqliteTransaction)transaction;
                insert.CommandText = "INSERT OR IGNORE INTO seen_paths(path) VALUES($path);";
                var parameter = insert.Parameters.Add("$path", SqliteType.Text);
                foreach (var path in seenPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    parameter.Value = Path.GetFullPath(path);
                    await insert.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await using (var remove = _connection.CreateCommand())
            {
                remove.Transaction = (SqliteTransaction)transaction;
                remove.CommandText = """
                    DELETE FROM files
                    WHERE substr(full_path, 1, length($root)) = $root COLLATE NOCASE
                      AND full_path NOT IN (SELECT path FROM seen_paths);
                    """;
                remove.Parameters.AddWithValue("$root", normalizedRoot);
                await remove.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<FileRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var records = new List<FileRecord>();
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT * FROM files ORDER BY full_path COLLATE NOCASE;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) records.Add(ReadRecord(reader));
            return records;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<DuplicateGroup>> GetDuplicateGroupsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var groups = new List<DuplicateGroup>();
            await using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT f.*
                FROM files f
                JOIN (
                    SELECT sha256, size_bytes
                    FROM files
                    WHERE error IS NULL
                    GROUP BY sha256, size_bytes
                    HAVING COUNT(*) > 1
                ) d ON d.sha256 = f.sha256 AND d.size_bytes = f.size_bytes
                ORDER BY f.size_bytes DESC, f.sha256, f.full_path COLLATE NOCASE;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var current = new List<FileRecord>();
            string? hash = null;
            long size = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                var record = ReadRecord(reader);
                if (hash is not null && (!hash.Equals(record.Sha256, StringComparison.Ordinal) || size != record.SizeBytes))
                {
                    groups.Add(new(hash, size, current.ToArray()));
                    current.Clear();
                }
                hash = record.Sha256;
                size = record.SizeBytes;
                current.Add(record);
            }
            if (hash is not null) groups.Add(new(hash, size, current.ToArray()));
            return groups;
        }
        finally { _gate.Release(); }
    }

    private static FileRecord ReadRecord(SqliteDataReader reader) => new(
        reader.GetString(reader.GetOrdinal("full_path")),
        reader.GetInt64(reader.GetOrdinal("size_bytes")),
        reader.GetInt64(reader.GetOrdinal("last_write_utc_ticks")),
        reader.GetString(reader.GetOrdinal("sha256")),
        (MediaKind)reader.GetInt32(reader.GetOrdinal("media_kind")),
        GetNullableInt32(reader, "width"),
        GetNullableInt32(reader, "height"),
        GetNullableInt64(reader, "duration_ms"),
        reader.GetInt64(reader.GetOrdinal("indexed_utc_ticks")),
        GetNullableString(reader, "error"));

    private static int? GetNullableInt32(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static long? GetNullableInt64(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static string? GetNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static void AddRecordParameters(SqliteCommand command, FileRecord record)
    {
        command.Parameters.AddWithValue("$path", Path.GetFullPath(record.FullPath));
        command.Parameters.AddWithValue("$size", record.SizeBytes);
        command.Parameters.AddWithValue("$write", record.LastWriteUtcTicks);
        command.Parameters.AddWithValue("$hash", record.Sha256);
        command.Parameters.AddWithValue("$kind", (int)record.Kind);
        command.Parameters.AddWithValue("$width", (object?)record.Width ?? DBNull.Value);
        command.Parameters.AddWithValue("$height", (object?)record.Height ?? DBNull.Value);
        command.Parameters.AddWithValue("$duration", (object?)record.DurationMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$indexed", record.IndexedUtcTicks);
        command.Parameters.AddWithValue("$error", (object?)record.Error ?? DBNull.Value);
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        _gate.Dispose();
    }
}
