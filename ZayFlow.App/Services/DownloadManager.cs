using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ZayFlow.App.Services;

/// <summary>
/// Represents the state of a single download.
/// </summary>
public enum DownloadStatus
{
    Queued,
    Downloading,
    Paused,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Observable model for a single download item tracked by the DownloadManager.
/// </summary>
public class DownloadItem : INotifyPropertyChanged
{
    private string _fileName = string.Empty;
    private string _url = string.Empty;
    private string _savePath = string.Empty;
    private string _fullPath = string.Empty;
    private DownloadStatus _status = DownloadStatus.Queued;
    private double _progressPercent;
    private long _bytesDownloaded;
    private long _totalBytes;
    private string _speedText = string.Empty;
    private string _statusText = "Queued";
    private string _errorMessage = string.Empty;
    private DateTime _startedAt;
    private DateTime? _completedAt;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    
    public string FileName
    {
        get => _fileName;
        set { _fileName = value; OnPropertyChanged(); }
    }

    public string Url
    {
        get => _url;
        set { _url = value; OnPropertyChanged(); }
    }

    public string SavePath
    {
        get => _savePath;
        set { _savePath = value; OnPropertyChanged(); }
    }

    public string FullPath
    {
        get => _fullPath;
        set { _fullPath = value; OnPropertyChanged(); }
    }

    public DownloadStatus Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(CanPause));
            OnPropertyChanged(nameof(CanResume));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(StatusIcon));
            OnPropertyChanged(nameof(StatusColor));
        }
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        set { _progressPercent = value; OnPropertyChanged(); }
    }

    public long BytesDownloaded
    {
        get => _bytesDownloaded;
        set { _bytesDownloaded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); }
    }

    public long TotalBytes
    {
        get => _totalBytes;
        set { _totalBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); }
    }

    public string SpeedText
    {
        get => _speedText;
        set { _speedText = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); }
    }

    public DateTime StartedAt
    {
        get => _startedAt;
        set { _startedAt = value; OnPropertyChanged(); OnPropertyChanged(nameof(StartedAtText)); }
    }

    public DateTime? CompletedAt
    {
        get => _completedAt;
        set { _completedAt = value; OnPropertyChanged(); }
    }

    // Computed properties
    public bool IsActive => Status == DownloadStatus.Downloading || Status == DownloadStatus.Queued;
    public bool CanPause => Status == DownloadStatus.Downloading;
    public bool CanResume => Status == DownloadStatus.Paused;
    public bool CanCancel => Status == DownloadStatus.Downloading || Status == DownloadStatus.Paused || Status == DownloadStatus.Queued;

    public string StatusIcon => Status switch
    {
        DownloadStatus.Queued => "⏳",
        DownloadStatus.Downloading => "📥",
        DownloadStatus.Paused => "⏸️",
        DownloadStatus.Completed => "✅",
        DownloadStatus.Failed => "❌",
        DownloadStatus.Cancelled => "🚫",
        _ => "📥"
    };

    public string StatusColor => Status switch
    {
        DownloadStatus.Downloading => "#60A5FA",
        DownloadStatus.Paused => "#FBBF24",
        DownloadStatus.Completed => "#34D399",
        DownloadStatus.Failed => "#F87171",
        DownloadStatus.Cancelled => "#9CA3AF",
        _ => "#9CA3AF"
    };

    public string ProgressText
    {
        get
        {
            var dl = FormatBytes(BytesDownloaded);
            if (TotalBytes > 0)
            {
                var total = FormatBytes(TotalBytes);
                return $"{dl} / {total}";
            }
            return BytesDownloaded > 0 ? dl : "";
        }
    }

    public string StartedAtText => StartedAt == default ? "" : StartedAt.ToString("HH:mm:ss");

    public string FileSizeText => TotalBytes > 0 ? FormatBytes(TotalBytes) : "Unknown";

    // Internal control
    internal CancellationTokenSource? Cts { get; set; }
    internal ManualResetEventSlim PauseEvent { get; set; } = new(true); // starts signaled (not paused)

    private static string FormatBytes(long bytes)
    {
        return bytes < 1024 ? $"{bytes} B"
            : bytes < 1024 * 1024 ? $"{bytes / 1024.0:F1} KB"
            : bytes < 1024L * 1024 * 1024 ? $"{bytes / (1024.0 * 1024):F1} MB"
            : $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Centralized download manager. Tracks all downloads with pause/resume/cancel support.
/// Registered as a singleton in DI.
/// </summary>
public class DownloadManager
{
    private readonly Dispatcher _dispatcher;

    public ObservableCollection<DownloadItem> Downloads { get; } = new();

    public DownloadManager()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    /// <summary>
    /// Enqueue and start a new download. Returns the DownloadItem for tracking.
    /// </summary>
    public DownloadItem StartDownload(string url, string savePath, string fileName)
    {
        var item = new DownloadItem
        {
            Url = url,
            SavePath = savePath,
            FileName = string.IsNullOrWhiteSpace(fileName) ? GetFileNameFromUrl(url) : SanitizeFileName(fileName),
            StartedAt = DateTime.Now,
            Status = DownloadStatus.Queued,
            StatusText = "Queued"
        };

        _dispatcher.Invoke(() => Downloads.Insert(0, item));

        // Fire and forget — the download runs in the background
        _ = Task.Run(() => ExecuteDownloadAsync(item));

        return item;
    }

    /// <summary>
    /// Pause an active download.
    /// </summary>
    public void PauseDownload(DownloadItem item)
    {
        if (item.Status != DownloadStatus.Downloading) return;
        item.PauseEvent.Reset(); // block the read loop
        item.Status = DownloadStatus.Paused;
        item.StatusText = "Paused";
        item.SpeedText = "";
    }

    /// <summary>
    /// Resume a paused download.
    /// </summary>
    public void ResumeDownload(DownloadItem item)
    {
        if (item.Status != DownloadStatus.Paused) return;
        item.Status = DownloadStatus.Downloading;
        item.StatusText = "Downloading...";
        item.PauseEvent.Set(); // unblock the read loop
    }

    /// <summary>
    /// Cancel/stop a download.
    /// </summary>
    public void CancelDownload(DownloadItem item)
    {
        if (!item.CanCancel) return;
        item.Cts?.Cancel();
        item.PauseEvent.Set(); // unblock in case paused so the loop can exit
        item.Status = DownloadStatus.Cancelled;
        item.StatusText = "Cancelled";
        item.SpeedText = "";
    }

    /// <summary>
    /// Remove a completed/failed/cancelled download from the list.
    /// </summary>
    public void RemoveDownload(DownloadItem item)
    {
        if (item.IsActive)
            CancelDownload(item);
        _dispatcher.Invoke(() => Downloads.Remove(item));
    }

    /// <summary>
    /// Clear all finished (completed/failed/cancelled) downloads from the list.
    /// </summary>
    public void ClearFinished()
    {
        _dispatcher.Invoke(() =>
        {
            for (int i = Downloads.Count - 1; i >= 0; i--)
            {
                if (!Downloads[i].IsActive)
                    Downloads.RemoveAt(i);
            }
        });
    }

    /// <summary>
    /// Open the folder containing a downloaded file.
    /// </summary>
    public void OpenFolder(DownloadItem item)
    {
        if (string.IsNullOrWhiteSpace(item.FullPath)) return;
        var dir = Path.GetDirectoryName(item.FullPath);
        if (dir != null && Directory.Exists(dir))
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
        }
    }

    /// <summary>
    /// Open the downloaded file with default application.
    /// </summary>
    public void OpenFile(DownloadItem item)
    {
        if (string.IsNullOrWhiteSpace(item.FullPath) || !File.Exists(item.FullPath)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = item.FullPath,
            UseShellExecute = true
        });
    }

    // ─── Core download logic with pause/resume/cancel ────────────────

    private async Task ExecuteDownloadAsync(DownloadItem item)
    {
        item.Cts = new CancellationTokenSource();
        var ct = item.Cts.Token;

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(30);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            // Resolve save directory
            var resolvedDir = ResolvePath(item.SavePath);
            Directory.CreateDirectory(resolvedDir);

            var fullPath = Path.Combine(resolvedDir, item.FileName);

            // Avoid overwriting
            var counter = 1;
            var baseName = Path.GetFileNameWithoutExtension(fullPath);
            var ext = Path.GetExtension(fullPath);
            var dir = Path.GetDirectoryName(fullPath)!;
            while (File.Exists(fullPath))
            {
                fullPath = Path.Combine(dir, $"{baseName} ({counter}){ext}");
                counter++;
            }

            item.FullPath = fullPath;
            UpdateOnUI(item, i =>
            {
                i.Status = DownloadStatus.Downloading;
                i.StatusText = "Connecting...";
            });

            using var response = await httpClient.GetAsync(new Uri(item.Url), HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            UpdateOnUI(item, i =>
            {
                i.TotalBytes = totalBytes > 0 ? totalBytes : 0;
                i.StatusText = "Downloading...";
            });

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = File.Create(fullPath);

            var buffer = new byte[81920]; // 80 KB
            long totalRead = 0;
            int bytesRead;
            var lastSpeedCalc = DateTime.Now;
            long lastSpeedBytes = 0;

            while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
            {
                // Check for pause
                item.PauseEvent.Wait(ct);

                ct.ThrowIfCancellationRequested();

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;

                // Calculate speed every 500ms
                var now = DateTime.Now;
                var elapsed = (now - lastSpeedCalc).TotalSeconds;
                
                if (elapsed >= 0.5)
                {
                    var bytesPerSec = (totalRead - lastSpeedBytes) / elapsed;
                    lastSpeedCalc = now;
                    lastSpeedBytes = totalRead;

                    var speedStr = FormatSpeed(bytesPerSec);
                    var pct = totalBytes > 0 ? (double)totalRead / totalBytes * 100 : -1;

                    UpdateOnUI(item, i =>
                    {
                        i.BytesDownloaded = totalRead;
                        i.ProgressPercent = pct > 0 ? pct : 0;
                        i.SpeedText = speedStr;
                    });
                }
            }

            // Final update
            var fileSize = new FileInfo(fullPath).Length;
            UpdateOnUI(item, i =>
            {
                i.BytesDownloaded = fileSize;
                i.TotalBytes = fileSize;
                i.ProgressPercent = 100;
                i.Status = DownloadStatus.Completed;
                i.StatusText = "Completed";
                i.SpeedText = "";
                i.CompletedAt = DateTime.Now;
            });
        }
        catch (OperationCanceledException)
        {
            // Already set to Cancelled by CancelDownload
            if (item.Status != DownloadStatus.Cancelled)
            {
                UpdateOnUI(item, i =>
                {
                    i.Status = DownloadStatus.Cancelled;
                    i.StatusText = "Cancelled";
                    i.SpeedText = "";
                });
            }
        }
        catch (Exception ex)
        {
            UpdateOnUI(item, i =>
            {
                i.Status = DownloadStatus.Failed;
                i.StatusText = "Failed";
                i.ErrorMessage = ex.Message;
                i.SpeedText = "";
            });
        }
        finally
        {
            item.Cts?.Dispose();
            item.Cts = null;
        }
    }

    private void UpdateOnUI(DownloadItem item, Action<DownloadItem> update)
    {
        _dispatcher.Invoke(() => update(item));
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        return bytesPerSec < 1024 ? $"{bytesPerSec:F0} B/s"
            : bytesPerSec < 1024 * 1024 ? $"{bytesPerSec / 1024:F1} KB/s"
            : $"{bytesPerSec / (1024.0 * 1024):F1} MB/s";
    }

    private static string ResolvePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) input = "Downloads";
        var knownFolders = new Dictionary<string, Environment.SpecialFolder>(StringComparer.OrdinalIgnoreCase)
        {
            ["Desktop"] = Environment.SpecialFolder.Desktop,
            ["Documents"] = Environment.SpecialFolder.MyDocuments,
            ["Downloads"] = Environment.SpecialFolder.UserProfile,
            ["Music"] = Environment.SpecialFolder.MyMusic,
            ["Videos"] = Environment.SpecialFolder.MyVideos,
            ["Pictures"] = Environment.SpecialFolder.MyPictures
        };

        foreach (var kv in knownFolders)
        {
            if (input.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))
            {
                var basePath = Environment.GetFolderPath(kv.Value);
                if (kv.Key.Equals("Downloads", StringComparison.OrdinalIgnoreCase))
                    return Path.Combine(basePath, "Downloads");
                return basePath;
            }
        }

        if (Path.IsPathRooted(input)) return input;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", input);
    }

    private static string GetFileNameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(name) && name.Contains('.'))
                return SanitizeFileName(name);
        }
        catch { }
        return "download";
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 200 ? name[..200] : name;
    }
}
