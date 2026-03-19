using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Contracts;

namespace ZayFlow.App.Services.AI;

/// <summary>
/// Implements system-level operations.
/// </summary>
public class SystemActionService : ISystemActionService
{
    private readonly ILogger<SystemActionService> _logger;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    private const int SPI_SETDESKWALLPAPER = 20;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    public SystemActionService(ILogger<SystemActionService> logger)
    {
        _logger = logger;
    }

    public async Task<ActionResult> ChangeWallpaperAsync(string imagePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(imagePath))
                    return new ActionResult { Success = false, Message = "Image file not found" };

                var extension = Path.GetExtension(imagePath).ToLower();
                if (!new[] { ".bmp", ".jpg", ".jpeg", ".png" }.Contains(extension))
                    return new ActionResult { Success = false, Message = "Unsupported image format. Use BMP, JPG, or PNG" };

                var result = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, imagePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                if (result == 0)
                    return new ActionResult { Success = false, Message = "Failed to change wallpaper" };

                _logger.LogInformation($"Wallpaper changed to {imagePath}");
                return new ActionResult { Success = true, Message = "Wallpaper changed successfully" };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Wallpaper error: {ex.Message}");
                return new ActionResult { Success = false, Message = $"Wallpaper change failed: {ex.Message}" };
            }
        });
    }

    public async Task<ActionResult> OpenApplicationAsync(string applicationPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(applicationPath) && !IsSystemApp(applicationPath))
                    return new ActionResult { Success = false, Message = "Application not found" };

                Process.Start(new ProcessStartInfo
                {
                    FileName = applicationPath,
                    UseShellExecute = true
                });

                _logger.LogInformation($"Opened application {applicationPath}");
                return new ActionResult { Success = true, Message = $"Application opened" };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Open app error: {ex.Message}");
                return new ActionResult { Success = false, Message = $"Failed to open application: {ex.Message}" };
            }
        });
    }

    public async Task<DiskUsageInfo> GetDiskUsageAsync(string drive)
    {
        return await Task.Run(() =>
        {
            try
            {
                var driveLetter = drive.Length > 0 ? drive[0].ToString().ToUpper() : "C";
                var driveInfo = DriveInfo.GetDrives().FirstOrDefault(d => d.Name.StartsWith(driveLetter));

                if (driveInfo == null)
                    return new DiskUsageInfo { Drive = driveLetter };

                var totalSize = driveInfo.TotalSize;
                var availableSize = driveInfo.AvailableFreeSpace;
                var usedSize = totalSize - availableSize;

                return new DiskUsageInfo
                {
                    Drive = driveLetter,
                    TotalSize = totalSize,
                    UsedSize = usedSize,
                    FreeSize = availableSize
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Disk usage error: {ex.Message}");
                return new DiskUsageInfo { Drive = drive };
            }
        });
    }

    public async Task<ActionResult> CleanTempFilesAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var tempPath = Path.GetTempPath();
                var deletedCount = 0;
                var totalSize = 0L;

                foreach (var file in Directory.GetFiles(tempPath))
                {
                    try
                    {
                        totalSize += new FileInfo(file).Length;
                        File.Delete(file);
                        deletedCount++;
                    }
                    catch { /* Skip files we can't delete */ }
                }

                // Try to delete subdirectories
                foreach (var dir in Directory.GetDirectories(tempPath))
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                    catch { /* Skip directories we can't delete */ }
                }

                var sizeMB = totalSize / (1024 * 1024);
                _logger.LogInformation($"Cleaned temp files: {deletedCount} items, {sizeMB}MB freed");
                return new ActionResult { Success = true, Message = $"Cleaned {deletedCount} files, freed {sizeMB}MB" };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Clean temp error: {ex.Message}");
                return new ActionResult { Success = false, Message = $"Cleanup failed: {ex.Message}" };
            }
        });
    }

    public async Task<SystemInfo> GetSystemInfoAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var osVersion = Environment.OSVersion.VersionString;
                var processorCount = Environment.ProcessorCount;
                var totalMemory = GC.GetTotalMemory(false) / (1024 * 1024); // Rough estimate
                var lastBoot = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);

                return new SystemInfo
                {
                    OSVersion = osVersion,
                    ProcessorCount = processorCount,
                    TotalMemory = totalMemory,
                    LastBoot = lastBoot
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"System info error: {ex.Message}");
                return new SystemInfo { OSVersion = "Unknown" };
            }
        });
    }

    private bool IsSystemApp(string appName)
    {
        var systemApps = new[] { "notepad", "calc", "mspaint", "explorer", "cmd", "powershell" };
        return systemApps.Contains(appName.ToLower());
    }
}
