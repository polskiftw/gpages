using GDupe.Core.Models;

namespace GDupe.Core.Abstractions;

public interface IFileDatabase : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<FileRecord?> GetAsync(string fullPath, CancellationToken cancellationToken = default);
    Task UpsertAsync(FileRecord record, CancellationToken cancellationToken = default);
    Task RemoveAsync(string fullPath, CancellationToken cancellationToken = default);
    Task KeepOnlyRootAsync(string root, CancellationToken cancellationToken = default);
    Task ReconcileRootAsync(string root, IReadOnlyCollection<string> seenPaths, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileRecord>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DuplicateGroup>> GetDuplicateGroupsAsync(CancellationToken cancellationToken = default);
}
