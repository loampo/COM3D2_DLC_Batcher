using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using COM3D2_DLC_Batcher.Models;
using COM3D2_DLC_Batcher.Services;
using Microsoft.Win32;

namespace COM3D2_DLC_Batcher.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly DlcProcessor _processor = new();
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _statsCts;
    private ScanResult? _scanResult;

    public GameProfile[] Profiles => GameProfiles.All;
    private GameProfile _selectedProfile = GameProfiles.All[0];
    public GameProfile SelectedProfile
    {
        get => _selectedProfile;
        set { _selectedProfile = value; OnPropertyChanged(); UpdateStats(); }
    }

    private string _sourcePath = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
    public string SourcePath
    {
        get => _sourcePath;
        set { _sourcePath = value; OnPropertyChanged(); UpdateStats(); }
    }

    private string _destPath = "";
    public string DestPath
    {
        get => _destPath;
        set { _destPath = value; OnPropertyChanged(); UpdateStats(); }
    }

    private string _statsText = "Drag a folder onto the SOURCE or DESTINATION field to set it.";
    public string StatsText
    {
        get => _statsText;
        set { _statsText = value; OnPropertyChanged(); }
    }

    private string _progressText = "";
    public string ProgressText
    {
        get => _progressText;
        set { _progressText = value; OnPropertyChanged(); }
    }

    private double _progressValue;
    public double ProgressValue
    {
        get => _progressValue;
        set { _progressValue = value; OnPropertyChanged(); }
    }

    private double _progressMax = 1;
    public double ProgressMax
    {
        get => _progressMax;
        set { _progressMax = value; OnPropertyChanged(); }
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); InvalidateCommands(); }
    }
    public bool IsIdle => !_isRunning;

    public ObservableCollection<LogEntry> LogEntries { get; } = [];
    public event Action? LogEntryAdded;

    public RelayCommand ScanCommand { get; }
    public RelayCommand ExtractCommand { get; }
    public RelayCommand ProcessCommand { get; }
    public RelayCommand RunAllCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand BrowseSrcCommand { get; }
    public RelayCommand BrowseDstCommand { get; }

    public MainViewModel()
    {
        ScanCommand      = new(async _ => await RunScanAsync(),      _ => IsIdle);
        ExtractCommand   = new(async _ => await RunExtractAsync(),   _ => IsIdle && _scanResult is not null);
        ProcessCommand   = new(async _ => await RunProcessAsync(),   _ => IsIdle);
        RunAllCommand    = new(async _ => await RunAllAsync(),       _ => IsIdle);
        CancelCommand    = new(_ => _cts?.Cancel(),                  _ => IsRunning);
        BrowseSrcCommand = new(_ => BrowseFolder(isSource: true));
        BrowseDstCommand = new(_ => BrowseFolder(isSource: false));

        Log("=== COM3D2 DLC Batcher ===", "Lime");
        Log($"Archiver : {_processor.ArchiverDisplayName}", "White");
        Log("", "White");
    }

    private void InvalidateCommands()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ScanCommand.Invalidate();
            ExtractCommand.Invalidate();
            ProcessCommand.Invalidate();
            RunAllCommand.Invalidate();
            CancelCommand.Invalidate();
        });
    }

    private async Task RunInCancellableScope(Func<CancellationToken, Task> action)
    {
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<ProgressInfo>(p =>
        {
            ProgressValue = p.Value;
            ProgressMax   = Math.Max(p.Max, 1);
            ProgressText  = p.Message;
        });

        try
        {
            await action(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("  -- Cancelled by user --", "Orange");
        }
        catch (Exception ex)
        {
            Log($"FATAL: {ex.Message}", "Red");
            MessageBox.Show(ex.Message, "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts      = null;
            IsRunning = false;
            ProgressText = "Done.";
        }
    }

    private async Task RunScanAsync()
    {
        await RunInCancellableScope(async (ct) =>
        {
            ClearLogAndState();
            await _processor.CheckGameVersionAsync(SelectedProfile, DestPath, Log);
            _scanResult = await _processor.ScanSourceAsync(SourcePath, DestPath, new Progress<ProgressInfo>(), Log, ct);
            ExtractCommand.Invalidate();
        });
    }

    private async Task RunExtractAsync()
    {
        if (_scanResult is null)
        {
            Log("Please run Scan first.", "Orange");
            return;
        }
        await RunInCancellableScope(async (ct) =>
        {
            var progress = new Progress<ProgressInfo>(p => { ProgressValue = p.Value; ProgressMax = p.Max; ProgressText = p.Message; });
            await _processor.ExtractAndCopyAsync(DestPath, _scanResult, progress, Log, ct);
        });
    }

    private async Task RunProcessAsync()
    {
        await RunInCancellableScope(async (ct) =>
        {
            _processor.ResetSession();
            var progress = new Progress<ProgressInfo>(p => { ProgressValue = p.Value; ProgressMax = p.Max; ProgressText = p.Message; });
            await _processor.ProcessDlcsAsync(SelectedProfile, DestPath, progress, Log, ShowError, ct);
        });
    }

    private async Task RunAllAsync()
    {
        await RunInCancellableScope(async (ct) =>
        {
            ClearLogAndState();
            var progress = new Progress<ProgressInfo>(p => { ProgressValue = p.Value; ProgressMax = p.Max; ProgressText = p.Message; });
            
            await _processor.CheckGameVersionAsync(SelectedProfile, DestPath, Log);
            
            var scanResult = await _processor.ScanSourceAsync(SourcePath, DestPath, progress, Log, ct);
            
            await _processor.ExtractAndCopyAsync(DestPath, scanResult, progress, Log, ct);
            
            _processor.ResetSession();
            await _processor.ProcessDlcsAsync(SelectedProfile, DestPath, progress, Log, ShowError, ct);
        });
    }

    private void ClearLogAndState()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            LogEntries.Clear();
            _scanResult = null;
            ProgressValue = 0;
            ProgressMax = 1;
            ProgressText = "";
            Log("=== COM3D2 DLC Batcher ===", "Lime");
            Log($"Version  : {SelectedProfile.DisplayName}" +
                   (string.IsNullOrEmpty(SelectedProfile.DlcPrefix) ? "" : $" [prefix: {SelectedProfile.DlcPrefix}]"), "White");
            Log($"Source   : {SourcePath}", "White");
            Log($"Dest     : {DestPath}", "White");
            Log($"Archiver : {_processor.ArchiverDisplayName}", "White");
            Log("", "White");

            if (!Directory.Exists(SourcePath)) { Log("ERROR: SOURCE folder not found!", "Red"); IsRunning = false; return; }
            if (!Directory.Exists(DestPath))   { Log("ERROR: DESTINATION folder not found!", "Red"); IsRunning = false; return; }
        });
    }

    private void BrowseFolder(bool isSource)
    {
        var current = isSource ? SourcePath : DestPath;
        var dlg = new OpenFolderDialog { Title = isSource ? "Select SOURCE folder" : "Select DESTINATION (dlc) folder", InitialDirectory = Directory.Exists(current) ? current : null };
        if (dlg.ShowDialog() == true)
        {
            if (isSource) SourcePath = dlg.FolderName;
            else DestPath = dlg.FolderName;
        }
    }

    private void UpdateStats()
    {
        _statsCts?.Cancel();
        _statsCts = new CancellationTokenSource();
        var token = _statsCts.Token;

        Task.Run(async () =>
        {
            await Task.Delay(300, token); // Debounce
            if (token.IsCancellationRequested) return;

            bool srcOk = Directory.Exists(SourcePath);
            bool dstOk = Directory.Exists(DestPath);

            string text = (srcOk, dstOk) switch
            {
                (true, true)  => $"Ready  |  {SelectedProfile.DisplayName}  |  {Path.GetFileName(SourcePath)} -> {Path.GetFileName(DestPath)}",
                (false, _)    => "SOURCE folder not found.",
                (_, false)    => "Set the DESTINATION folder.",
            };
            
            Application.Current.Dispatcher.Invoke(() => StatsText = text);
        }, token);
    }

    public void Log(string text, string colorName)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var brush = colorName switch
            {
                "Lime"     => Brushes.Lime,
                "Red"      => Brushes.OrangeRed,
                "Cyan"     => Brushes.Cyan,
                "Yellow"   => Brushes.Yellow,
                "Orange"   => new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                "Magenta"  => Brushes.Violet,
                "DarkGray" => new SolidColorBrush(Color.FromRgb(100, 100, 120)),
                "Gray"     => new SolidColorBrush(Color.FromRgb(130, 130, 150)),
                _          => new SolidColorBrush(Color.FromRgb(205, 214, 244))
            };
            LogEntries.Add(new LogEntry(text, brush));
            LogEntryAdded?.Invoke();
        });
    }
    
    private void ShowError(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
