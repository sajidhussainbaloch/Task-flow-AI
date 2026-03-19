using System.IO;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Contracts;

namespace ZayFlow.App.Services.AI.Handlers;

/// <summary>
/// Tier 6: System power tools — startup manager, service manager, environment variables, performance report, power plan, storage analyzer.
/// </summary>
public class SystemPowerHandler
{
    private readonly ILogger<SystemPowerHandler> _logger;

    public SystemPowerHandler(ILogger<SystemPowerHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>startup_manager — List/manage startup programs.</summary>
    public async Task<ActionResult> ExecuteStartupManagerAsync(string action, CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();

        switch (action.ToLowerInvariant())
        {
            case "list" or "show" or "":
                // Read from registry and startup folders
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                var startupFiles = Directory.Exists(startupFolder) ? Directory.GetFiles(startupFolder) : Array.Empty<string>();

                sb.AppendLine("🚀 Startup Programs:\n");

                // Registry startup items via PowerShell
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -Command \"Get-CimInstance Win32_StartupCommand | Select-Object Name, Command, Location | ConvertTo-Json\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = System.Diagnostics.Process.Start(psi);
                    var output = await proc!.StandardOutput.ReadToEndAsync(ct);
                    proc.WaitForExit(10000);

                    try
                    {
                        var items = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, System.Text.Json.JsonElement>>>(output);
                        if (items != null)
                        {
                            foreach (var item in items.Take(25))
                            {
                                var name = item.TryGetValue("Name", out var n) ? n.ToString() : "Unknown";
                                var location = item.TryGetValue("Location", out var l) ? l.ToString() : "";
                                sb.AppendLine($"  📌 {name} [{location}]");
                            }
                        }
                    }
                    catch
                    {
                        // Single item (not array)
                        sb.AppendLine($"  {output.Trim()}");
                    }
                }
                catch { sb.AppendLine("  (Could not query registry)"); }

                if (startupFiles.Length > 0)
                {
                    sb.AppendLine($"\n📁 Startup folder ({startupFiles.Length} items):");
                    foreach (var f in startupFiles)
                        sb.AppendLine($"  📄 {Path.GetFileName(f)}");
                }

                return new ActionResult { Success = true, Message = sb.ToString() };

            default:
                return new ActionResult { Success = false, Message = "Use action 'list' to see startup programs." };
        }
    }

    /// <summary>service_manager — List/query Windows services.</summary>
    public async Task<ActionResult> ExecuteServiceManagerAsync(string action, string serviceName, CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();

        switch (action.ToLowerInvariant())
        {
            case "list" or "":
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"Get-Service | Where-Object {$_.Status -eq 'Running'} | Select-Object -First 30 Name, DisplayName, Status | Format-Table -AutoSize | Out-String\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    var output = await proc!.StandardOutput.ReadToEndAsync(ct);
                    proc.WaitForExit(10000);
                    sb.AppendLine("⚙️ Running Services (top 30):\n");
                    sb.AppendLine(output.Trim());
                }
                break;

            case "find" or "search" when !string.IsNullOrWhiteSpace(serviceName):
                var findPsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"Get-Service | Where-Object {{$_.DisplayName -like '*{serviceName}*' -or $_.Name -like '*{serviceName}*'}} | Select-Object Name, DisplayName, Status | Format-Table -AutoSize | Out-String\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var findProc = System.Diagnostics.Process.Start(findPsi))
                {
                    var findOutput = await findProc!.StandardOutput.ReadToEndAsync(ct);
                    findProc.WaitForExit(10000);
                    sb.AppendLine($"⚙️ Services matching '{serviceName}':\n");
                    sb.AppendLine(string.IsNullOrWhiteSpace(findOutput) ? "No services found." : findOutput.Trim());
                }
                break;

            default:
                return new ActionResult { Success = false, Message = "Use action 'list' or 'find' with a service name." };
        }

        return new ActionResult { Success = true, Message = sb.ToString() };
    }

    /// <summary>env_variables — View/search environment variables.</summary>
    public Task<ActionResult> ExecuteEnvVariablesAsync(string action, string name, CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();

        switch (action.ToLowerInvariant())
        {
            case "list" or "show" or "":
                var vars = Environment.GetEnvironmentVariables();
                sb.AppendLine($"🔧 Environment Variables ({vars.Count}):\n");
                foreach (System.Collections.DictionaryEntry entry in vars)
                {
                    var val = entry.Value?.ToString() ?? "";
                    if (val.Length > 80) val = val[..80] + "...";
                    sb.AppendLine($"  {entry.Key} = {val}");
                }
                break;

            case "get" or "find" when !string.IsNullOrWhiteSpace(name):
                var value = Environment.GetEnvironmentVariable(name);
                if (value != null)
                    sb.AppendLine($"🔧 {name} = {value}");
                else
                {
                    // Search for partial match
                    var matches = Environment.GetEnvironmentVariables()
                        .Cast<System.Collections.DictionaryEntry>()
                        .Where(e => e.Key.ToString()!.Contains(name, StringComparison.OrdinalIgnoreCase))
                        .Take(10);
                    foreach (var m in matches)
                        sb.AppendLine($"🔧 {m.Key} = {m.Value}");
                    if (sb.Length == 0)
                        sb.AppendLine($"No environment variable matching '{name}' found.");
                }
                break;

            case "path":
                var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
                var paths = pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries);
                sb.AppendLine($"🔧 PATH entries ({paths.Length}):\n");
                foreach (var p in paths)
                    sb.AppendLine($"  {(Directory.Exists(p) ? "✅" : "❌")} {p}");
                break;

            default:
                return Task.FromResult(new ActionResult { Success = false, Message = "Use action 'list', 'get', or 'path'." });
        }

        return Task.FromResult(new ActionResult { Success = true, Message = sb.ToString() });
    }

    /// <summary>performance_report — CPU, memory, disk I/O performance snapshot.</summary>
    public async Task<ActionResult> ExecutePerformanceReportAsync(CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("📊 Performance Report\n");
        sb.AppendLine($"Generated: {DateTime.Now:f}\n");

        // Top processes by memory
        var procs = System.Diagnostics.Process.GetProcesses()
            .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0; } })
            .Take(10)
            .ToList();

        var totalMemory = procs.Sum(p => { try { return p.WorkingSet64; } catch { return 0L; } });
        sb.AppendLine("🏭 Top 10 Processes by Memory:");
        sb.AppendLine($"{"Process",-28} {"Memory",10} {"Threads",8}");
        sb.AppendLine(new string('─', 48));
        foreach (var p in procs)
        {
            try
            {
                sb.AppendLine($"{p.ProcessName,-28} {p.WorkingSet64 / 1024 / 1024,8}MB {p.Threads.Count,8}");
            }
            catch { }
        }

        // Disk usage
        sb.AppendLine("\n💿 Disk Usage:");
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var pct = 100.0 * (drive.TotalSize - drive.TotalFreeSpace) / drive.TotalSize;
            var bar = new string('█', (int)(pct / 5)) + new string('░', 20 - (int)(pct / 5));
            sb.AppendLine($"  {drive.Name} [{bar}] {pct:F0}% ({drive.TotalFreeSpace / 1024 / 1024 / 1024}GB free)");
        }

        // System uptime
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        sb.AppendLine($"\n⏱️ Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m");
        sb.AppendLine($"📋 Total processes: {System.Diagnostics.Process.GetProcesses().Length}");

        return new ActionResult { Success = true, Message = sb.ToString() };
    }

    /// <summary>power_plan — View/set power plan.</summary>
    public async Task<ActionResult> ExecutePowerPlanAsync(string action, CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powercfg",
            Arguments = action.ToLowerInvariant() switch
            {
                "list" or "show" or "" => "/list",
                "active" => "/getactivescheme",
                _ => "/getactivescheme"
            },
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var proc = System.Diagnostics.Process.Start(psi);
            var output = await proc!.StandardOutput.ReadToEndAsync(ct);
            proc.WaitForExit(5000);
            sb.AppendLine("⚡ Power Plans:\n");
            sb.AppendLine(output.Trim());
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Power plan query failed: {ex.Message}" };
        }

        return new ActionResult { Success = true, Message = sb.ToString() };
    }

    /// <summary>storage_analyzer — Analyze disk usage by folder, find large files.</summary>
    public Task<ActionResult> ExecuteStorageAnalyzerAsync(string resolvedPath, CancellationToken ct)
    {
        if (!Directory.Exists(resolvedPath))
            return Task.FromResult(new ActionResult { Success = false, Message = $"Folder not found: {resolvedPath}" });

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"💾 Storage Analysis: {resolvedPath}\n");

        // Top folders by size
        var subdirs = Directory.GetDirectories(resolvedPath)
            .Select(d =>
            {
                long size = 0;
                try { size = Directory.GetFiles(d, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); }
                catch { }
                return new { Name = Path.GetFileName(d), Size = size };
            })
            .OrderByDescending(x => x.Size)
            .Take(10)
            .ToList();

        var totalSize = subdirs.Sum(x => x.Size);
        sb.AppendLine("📂 Top folders:");
        foreach (var dir in subdirs)
        {
            var pct = totalSize > 0 ? dir.Size * 100.0 / totalSize : 0;
            var bar = new string('█', Math.Max(1, (int)(pct / 5)));
            sb.AppendLine($"  {bar.PadRight(20)} {pct:F0}% {dir.Name} ({dir.Size / 1024 / 1024}MB)");
        }

        // Large files
        var largeFiles = Directory.GetFiles(resolvedPath, "*", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.Length)
            .Take(10)
            .ToList();

        if (largeFiles.Any())
        {
            sb.AppendLine("\n📄 Largest files:");
            foreach (var f in largeFiles)
                sb.AppendLine($"  {f.Length / 1024 / 1024,6}MB  {Path.GetRelativePath(resolvedPath, f.FullName)}");
        }

        // Extension breakdown
        var extGroups = Directory.GetFiles(resolvedPath, "*", SearchOption.AllDirectories)
            .GroupBy(f => Path.GetExtension(f).ToLowerInvariant())
            .Select(g => new { Ext = string.IsNullOrEmpty(g.Key) ? "(none)" : g.Key, Count = g.Count(), Size = g.Sum(f => new FileInfo(f).Length) })
            .OrderByDescending(x => x.Size)
            .Take(10)
            .ToList();

        sb.AppendLine("\n📊 File types:");
        foreach (var eg in extGroups)
            sb.AppendLine($"  {eg.Ext,-10} {eg.Count,5} files  {eg.Size / 1024 / 1024,6}MB");

        return Task.FromResult(new ActionResult { Success = true, Message = sb.ToString() });
    }
}
