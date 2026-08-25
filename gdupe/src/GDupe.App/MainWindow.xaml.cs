using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using GDupe.Core.Abstractions;
using GDupe.Core.Models;
using GDupe.Core.Services;
using Microsoft.Win32;

namespace GDupe.App;

public partial class MainWindow : Window
{
    private readonly IFileDatabase _database;
    private readonly IndexingController _controller;
    private readonly IAppLogger _logger;
    private readonly string _settingsPath;
    private readonly ObservableCollection<DuplicateGroup> _groups = [];
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private FileRecord? _selectedFile;

    public MainWindow(IFileDatabase database, IndexingController controller, IAppLogger logger, string dataDirectory)
    {
        InitializeComponent();
        _database = database;
        _controller = controller;
        _logger = logger;
        _settingsPath = Path.Combine(dataDirectory, "settings.json");
        GroupsList.ItemsSource = _groups;
        _controller.ProgressChanged += Controller_ProgressChanged;
        _controller.IndexChanged += Controller_IndexChanged;
        _controller.Failed += Controller_Failed;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshResultsAsync();
        var settings = AppSettings.Load(_settingsPath);
        var monitoredFolder = settings.MonitoredFolder;
        if (monitoredFolder is not null && Directory.Exists(monitoredFolder))
        {
            await StartFolderAsync(monitoredFolder);
        }
    }

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the folder GDupe should monitor", Multiselect = false };
        if (dialog.ShowDialog(this) == true) await StartFolderAsync(dialog.FolderName);
    }

    private async Task StartFolderAsync(string path)
    {
        try
        {
            AppSettings settings = new(path);
            settings.Save(_settingsPath);
            FolderText.Text = path;
            StatusText.Text = "Starting scan…";
            BusyBar.Visibility = Visibility.Visible;
            CancelButton.IsEnabled = true;
            RescanButton.IsEnabled = true;
            await _controller.StartAsync(path);
        }
        catch (Exception ex)
        {
            ShowFailure("The scan could not start.", ex);
        }
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Restarting scan…";
            BusyBar.Visibility = Visibility.Visible;
            CancelButton.IsEnabled = true;
            await _controller.RestartAsync();
        }
        catch (Exception ex) { ShowFailure("The scan could not restart.", ex); }
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _controller.CancelAsync();
            StatusText.Text = "Cancelled. Existing results are still available.";
            BusyBar.Visibility = Visibility.Collapsed;
            CancelButton.IsEnabled = false;
        }
        catch (Exception ex) { ShowFailure("Cancellation failed.", ex); }
    }

    private void Controller_ProgressChanged(object? sender, ScanProgress e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.IsWatching)
            {
                StatusText.Text = $"Watching · {e.FilesIndexed:N0} files checked · {e.Failures:N0} failures";
                BusyBar.Visibility = Visibility.Collapsed;
            }
            else
            {
                StatusText.Text = $"Indexing {e.FilesIndexed:N0} · {Path.GetFileName(e.CurrentPath)}";
                BusyBar.Visibility = Visibility.Visible;
            }
        });
    }

    private async void Controller_IndexChanged(object? sender, EventArgs e)
    {
        var refresh = await Dispatcher.InvokeAsync(RefreshResultsAsync);
        await refresh;
    }

    private void Controller_Failed(object? sender, Exception e) => Dispatcher.Invoke(() =>
        StatusText.Text = $"A file could not be indexed: {e.Message}");

    private async Task RefreshResultsAsync()
    {
        if (!await _refreshGate.WaitAsync(0)) return;
        try
        {
            var selectedHash = (GroupsList.SelectedItem as DuplicateGroup)?.Sha256;
            var groups = await _database.GetDuplicateGroupsAsync();
            _groups.Clear();
            foreach (var group in groups) _groups.Add(group);
            SummaryText.Text = $"{groups.Count:N0} duplicate groups · {groups.Sum(g => g.ReclaimableBytes) / 1024d / 1024d:0.##} MB reclaimable";
            GroupsList.SelectedItem = groups.FirstOrDefault(g => g.Sha256 == selectedHash) ?? groups.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.Error("Could not refresh results.", ex);
            StatusText.Text = $"Could not refresh results: {ex.Message}";
        }
        finally { _refreshGate.Release(); }
    }

    private void GroupsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        FilesGrid.ItemsSource = (GroupsList.SelectedItem as DuplicateGroup)?.Files;
        FilesGrid.SelectedIndex = FilesGrid.Items.Count > 0 ? 0 : -1;
    }

    private void FilesGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selectedFile = FilesGrid.SelectedItem as FileRecord;
        ShowPreview(_selectedFile);
    }

    private void ShowPreview(FileRecord? file)
    {
        ImagePreview.Source = null;
        ImagePreview.Visibility = Visibility.Collapsed;
        VideoPreview.Stop();
        VideoPreview.Source = null;
        VideoPreview.Visibility = Visibility.Collapsed;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        OpenFileButton.IsEnabled = file is not null && File.Exists(file.FullPath);
        OpenFolderButton.IsEnabled = file is not null && File.Exists(file.FullPath);
        PreviewName.Text = file?.Name ?? string.Empty;
        PreviewDetails.Text = file is null ? string.Empty : $"{file.Kind} · {file.SizeDisplay} · {file.DimensionsDisplay} · {file.DurationDisplay}\n{file.FullPath}";
        if (file is null || !File.Exists(file.FullPath)) return;

        try
        {
            if (file.Kind == MediaKind.Image)
            {
                using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 900;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                ImagePreview.Source = bitmap;
                ImagePreview.Visibility = Visibility.Visible;
            }
            else
            {
                VideoPreview.Source = new Uri(file.FullPath);
                VideoPreview.Visibility = Visibility.Visible;
                VideoPreview.Pause();
            }
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not preview '{file.FullPath}'.", ex);
            PreviewPlaceholder.Text = "Preview unavailable";
        }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e) => OpenSelected(false);
    private void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenSelected(true);
    private void FilesGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenSelected(true);

    private void OpenSelected(bool selectInExplorer)
    {
        var selectedFile = _selectedFile;
        if (selectedFile is null || !File.Exists(selectedFile.FullPath)) return;
        try
        {
            var info = selectInExplorer
                ? new ProcessStartInfo("explorer.exe", $"/select,\"{selectedFile.FullPath}\"") { UseShellExecute = true }
                : new ProcessStartInfo(selectedFile.FullPath) { UseShellExecute = true };
            Process.Start(info);
        }
        catch (Exception ex) { ShowFailure("Windows could not open that file.", ex); }
    }

    private void ShowFailure(string message, Exception ex)
    {
        _logger.Error(message, ex);
        StatusText.Text = $"{message} {ex.Message}";
        BusyBar.Visibility = Visibility.Collapsed;
    }
}
