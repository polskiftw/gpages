using System.Threading.Channels;
using GDupe.Core.Abstractions;
using GDupe.Core.Models;

namespace GDupe.Core.Services;

public sealed class IndexingController : IAsyncDisposable
{
    private readonly FileIndexService _indexer;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _sessionCts;
    private Task? _sessionTask;
    private FileSystemWatcher? _watcher;
    private string? _root;

    public event EventHandler? IndexChanged;
    public event EventHandler<ScanProgress>? ProgressChanged;
    public event EventHandler<Exception>? Failed;

    public bool IsRunning => _sessionTask is { IsCompleted: false };
    public string? Root => _root;

    public IndexingController(FileIndexService indexer, IAppLogger logger)
    {
        _indexer = indexer;
        _logger = logger;
    }

    public async Task StartAsync(string root)
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
            var sessionRoot = Path.GetFullPath(root);
            var sessionCts = new CancellationTokenSource();
            _root = sessionRoot;
            _sessionCts = sessionCts;
            await _indexer.KeepOnlyRootAsync(sessionRoot, sessionCts.Token).ConfigureAwait(false);
            _sessionTask = Task.Run(() => RunSessionAsync(sessionRoot, sessionCts.Token));
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public Task RestartAsync() => _root is null ? Task.CompletedTask : StartAsync(_root);

    public async Task CancelAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }

    private async Task RunSessionAsync(string root, CancellationToken cancellationToken)
    {
        var changes = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        try
        {
            var progress = new Progress<ScanProgress>(p => ProgressChanged?.Invoke(this, p));
            _watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
                EnableRaisingEvents = true
            };
            _watcher.Created += (_, e) => changes.Writer.TryWrite(e.FullPath);
            _watcher.Changed += (_, e) => changes.Writer.TryWrite(e.FullPath);
            _watcher.Deleted += (_, e) => changes.Writer.TryWrite(e.FullPath);
            _watcher.Renamed += (_, e) => { changes.Writer.TryWrite(e.OldFullPath); changes.Writer.TryWrite(e.FullPath); };
            _watcher.Error += (_, e) =>
            {
                _logger.Error("The folder watcher overflowed; scheduling a full rescan.", e.GetException());
                changes.Writer.TryWrite(root);
            };

            await _indexer.ScanAsync(root, progress, cancellationToken).ConfigureAwait(false);
            IndexChanged?.Invoke(this, EventArgs.Empty);

            while (await changes.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (changes.Reader.TryRead(out var path)) pending.Add(path);
                await Task.Delay(350, cancellationToken).ConfigureAwait(false);
                while (changes.Reader.TryRead(out var path)) pending.Add(path);

                if (pending.Contains(root) || pending.Any(path => Directory.Exists(path) || !FileIndexService.IsSupported(path)))
                {
                    await _indexer.ScanAsync(root, progress, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    foreach (var path in pending)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            await _indexer.IndexPathAsync(path, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            _logger.Error($"Could not process folder change for '{path}'.", ex);
                            Failed?.Invoke(this, ex);
                        }
                    }
                }

                IndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.Info("Indexing session cancelled.");
        }
        catch (Exception ex)
        {
            _logger.Error("Indexing session stopped unexpectedly.", ex);
            Failed?.Invoke(this, ex);
        }
        finally
        {
            changes.Writer.TryComplete();
        }
    }

    private async Task StopCoreAsync()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (_sessionCts is not null)
        {
            _sessionCts.Cancel();
            if (_sessionTask is not null)
            {
                try { await _sessionTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            _sessionCts.Dispose();
        }
        _sessionCts = null;
        _sessionTask = null;
    }

    public async ValueTask DisposeAsync()
    {
        await CancelAsync().ConfigureAwait(false);
        _lifecycle.Dispose();
    }
}
