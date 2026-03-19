using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Contracts;

namespace ZayFlow.App.Services.AI.Handlers;

/// <summary>
/// Tier 1 (Premium): Background automation — watch folder, scheduled tasks, auto backup.
/// </summary>
public class BackgroundAutomationHandler
{
    private readonly ILogger<BackgroundAutomationHandler> _logger;
    private static readonly Dictionary<string, FileSystemWatcher> _activeWatchers = new();

    public BackgroundAutomationHandler(ILogger<BackgroundAutomationHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>watch_folder — Start/stop a FileSystemWatcher on a directory.</summary>
    public Task<ActionResult> ExecuteWatchFolderAsync(string resolvedPath, string action, CancellationToken ct)
    {
        var sb = new StringBuilder();

        switch (action.ToLowerInvariant())
        {
            case "start" or "":
                if (!Directory.Exists(resolvedPath))
                    return Task.FromResult(new ActionResult { Success = false, Message = $"Folder not found: {resolvedPath}" });

                if (_activeWatchers.ContainsKey(resolvedPath))
                    return Task.FromResult(new ActionResult { Success = true, Message = $"👁️ Already watching: {resolvedPath}" });

                var watcher = new FileSystemWatcher(resolvedPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                var logPath = Path.Combine(resolvedPath, ".zayflow_watch.log");
                watcher.Created += (s, e) => File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] CREATED: {e.Name}\n");
                watcher.Deleted += (s, e) => File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] DELETED: {e.Name}\n");
                watcher.Changed += (s, e) => File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] CHANGED: {e.Name}\n");
                watcher.Renamed += (s, e) => File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] RENAMED: {e.OldName} → {e.Name}\n");

                _activeWatchers[resolvedPath] = watcher;
                sb.AppendLine($"👁️ Now watching: {resolvedPath}");
                sb.AppendLine($"📄 Log file: {logPath}");
                sb.AppendLine("All file changes will be logged. Use action 'stop' to stop watching.");
                break;

            case "stop":
                if (_activeWatchers.TryGetValue(resolvedPath, out var existing))
                {
                    existing.Dispose();
                    _activeWatchers.Remove(resolvedPath);
                    sb.AppendLine($"⏹️ Stopped watching: {resolvedPath}");
                }
                else
                    sb.AppendLine($"No active watcher for: {resolvedPath}");
                break;

            case "list":
                if (_activeWatchers.Count == 0)
                    sb.AppendLine("No active folder watchers.");
                else
                {
                    sb.AppendLine($"👁️ Active watchers ({_activeWatchers.Count}):\n");
                    foreach (var w in _activeWatchers)
                        sb.AppendLine($"  📂 {w.Key}");
                }
                break;

            case "log":
                var existingLogPath = Path.Combine(resolvedPath, ".zayflow_watch.log");
                if (File.Exists(existingLogPath))
                {
                    var logContent = File.ReadAllText(existingLogPath);
                    var logLines = logContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    sb.AppendLine($"📋 Watch log ({logLines.Length} events):\n");
                    foreach (var line in logLines.TakeLast(30))
                        sb.AppendLine($"  {line}");
                }
                else
                    sb.AppendLine("No watch log found. Start watching first.");
                break;

            default:
                return Task.FromResult(new ActionResult { Success = false, Message = "Actions: 'start', 'stop', 'list', 'log'." });
        }

        return Task.FromResult(new ActionResult { Success = true, Message = sb.ToString() });
    }

    /// <summary>scheduled_task — View/create Windows scheduled tasks.</summary>
    public async Task<ActionResult> ExecuteScheduledTaskAsync(string action, string taskName, CancellationToken ct)
    {
        var sb = new StringBuilder();

        switch (action.ToLowerInvariant())
        {
            case "list" or "":
                var listPsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/query /fo TABLE /nh",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var proc = System.Diagnostics.Process.Start(listPsi))
                {
                    var output = await proc!.StandardOutput.ReadToEndAsync(ct);
                    proc.WaitForExit(10000);
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .Take(25);
                    sb.AppendLine("📅 Scheduled Tasks (top 25):\n");
                    foreach (var line in lines)
                        sb.AppendLine($"  {line.Trim()}");
                }
                break;

            case "find" when !string.IsNullOrWhiteSpace(taskName):
                var findPsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/query /fo LIST /v /tn \"{taskName}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var findProc = System.Diagnostics.Process.Start(findPsi))
                {
                    var output = await findProc!.StandardOutput.ReadToEndAsync(ct);
                    var error = await findProc.StandardError.ReadToEndAsync(ct);
                    findProc.WaitForExit(10000);
                    if (!string.IsNullOrWhiteSpace(error))
                        sb.AppendLine($"Task '{taskName}' not found.");
                    else
                        sb.AppendLine(output.Trim());
                }
                break;

            default:
                return new ActionResult { Success = false, Message = "Actions: 'list', 'find' with a task name." };
        }

        return new ActionResult { Success = true, Message = sb.ToString() };
    }

    /// <summary>auto_backup — Create timestamped ZIP backup of a folder.</summary>
    public async Task<ActionResult> ExecuteAutoBackupAsync(string resolvedPath, string destination, CancellationToken ct)
    {
        if (!Directory.Exists(resolvedPath))
            return new ActionResult { Success = false, Message = $"Source folder not found: {resolvedPath}" };

        var folderName = Path.GetFileName(resolvedPath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupName = $"{folderName}_backup_{timestamp}.zip";

        // Default destination to a Backups subfolder in parent directory
        var backupDir = !string.IsNullOrWhiteSpace(destination) ? destination : Path.Combine(Path.GetDirectoryName(resolvedPath)!, "ZayFlow_Backups");
        Directory.CreateDirectory(backupDir);
        var zipPath = Path.Combine(backupDir, backupName);

        try
        {
            ZipFile.CreateFromDirectory(resolvedPath, zipPath, CompressionLevel.Optimal, true);
            var zipInfo = new FileInfo(zipPath);

            var sb = new StringBuilder();
            sb.AppendLine($"💾 Backup Created Successfully!\n");
            sb.AppendLine($"  📂 Source:  {resolvedPath}");
            sb.AppendLine($"  📦 Backup:  {zipPath}");
            sb.AppendLine($"  📊 Size:    {zipInfo.Length / 1024.0 / 1024.0:F1}MB");
            sb.AppendLine($"  🕐 Time:    {DateTime.Now:f}");

            return new ActionResult { Success = true, Message = sb.ToString() };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Backup failed: {ex.Message}" };
        }
    }
}
