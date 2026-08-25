namespace GDupe.Core.Abstractions;

public interface IFileHasher
{
    Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken);
}
