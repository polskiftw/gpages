using GDupe.Core.Abstractions;
using GDupe.Core.Data;
using GDupe.Core.Services;
using System.IO;
using System.Windows;

namespace GDupe.App;

public partial class App : Application
{
    private IFileDatabase? _database;
    private IndexingController? _controller;
    private IAppLogger? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            await RunSmokeTestAsync();
            return;
        }

        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GDupe");
        Directory.CreateDirectory(dataDirectory);
        _logger = new FileLogger(Path.Combine(dataDirectory, "gdupe.log"));

        try
        {
            _database = new SqliteFileDatabase(Path.Combine(dataDirectory, "index.db"));
            await _database.InitializeAsync();
            var indexer = new FileIndexService(_database, new Sha256FileHasher(), new MediaMetadataReader(), _logger);
            _controller = new IndexingController(indexer, _logger);
            var window = new MainWindow(_database, _controller, _logger, dataDirectory);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            _logger.Error("GDupe could not start.", ex);
            MessageBox.Show($"GDupe could not start.\n\n{ex.Message}\n\nDetails were written to the log in:\n{dataDirectory}",
                "GDupe", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task RunSmokeTestAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gdupe-smoke-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            await using var database = new SqliteFileDatabase(Path.Combine(directory, "smoke.db"));
            await database.InitializeAsync();
            Shutdown(0);
        }
        catch
        {
            Shutdown(1);
        }
        finally
        {
            try { Directory.Delete(directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_controller is not null) _controller.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (_database is not null) _database.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.Error("Shutdown failed.", ex);
        }
        base.OnExit(e);
    }
}
