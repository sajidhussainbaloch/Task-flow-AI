using System.IO;

namespace ZayFlow.App.Services.AI.Contracts;

/// <summary>
/// File management operations that can be safely executed.
/// </summary>
public interface IFileActionService
{
    /// <summary>
    /// Safely rename file with validation.
    /// </summary>
    Task<ActionResult> RenameFileAsync(string filePath, string newName);
    
    /// <summary>
    /// Safely move file with validation.
    /// </summary>
    Task<ActionResult> MoveFileAsync(string filePath, string destinationFolder);
    
    /// <summary>
    /// Safely delete file with confirmation.
    /// </summary>
    Task<ActionResult> DeleteFileAsync(string filePath, bool moveToRecycleBin = true);
    
    /// <summary>
    /// Create a folder.
    /// </summary>
    Task<ActionResult> CreateFolderAsync(string folderPath);
    
    /// <summary>
    /// Read file content (txt, md, docx basic support).
    /// </summary>
    Task<(bool Success, string Content)> ReadFileAsync(string filePath);
    
    /// <summary>
    /// Detect duplicate files in folder.
    /// </summary>
    Task<List<(string File1, string File2)>> DetectDuplicatesAsync(string folderPath);
    
    /// <summary>
    /// Get file info (size, created, modified).
    /// </summary>
    Task<FileInfo?> GetFileInfoAsync(string filePath);
    
    /// <summary>
    /// List files with filters.
    /// </summary>
    Task<List<string>> ListFilesAsync(string folderPath, string? pattern = null);
}

/// <summary>
/// System-level safe actions.
/// </summary>
public interface ISystemActionService
{
    /// <summary>
    /// Change desktop wallpaper.
    /// </summary>
    Task<ActionResult> ChangeWallpaperAsync(string imagePath);
    
    /// <summary>
    /// Open application.
    /// </summary>
    Task<ActionResult> OpenApplicationAsync(string appName);
    
    /// <summary>
    /// Get disk usage info.
    /// </summary>
    Task<DiskUsageInfo> GetDiskUsageAsync(string driveLetter = "C");
    
    /// <summary>
    /// Clean temporary files.
    /// </summary>
    Task<ActionResult> CleanTempFilesAsync();
    
    /// <summary>
    /// Get system info.
    /// </summary>
    Task<SystemInfo> GetSystemInfoAsync();
}

/// <summary>
/// Result of an action execution.
/// </summary>
public class ActionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorDetails { get; set; }
}

/// <summary>
/// Progress update for long-running actions (download, compress, extract, search).
/// </summary>
public class ActionProgress
{
    public string Status { get; set; } = string.Empty;
    public double Percent { get; set; } = -1; // -1 = indeterminate
    public string Icon { get; set; } = "⏳";
}

/// <summary>
/// Disk usage information.
/// </summary>
public class DiskUsageInfo
{
    public string Drive { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public long UsedSize { get; set; }
    public long FreeSize { get; set; }
    public double UsagePercent => TotalSize > 0 ? (double)UsedSize / TotalSize * 100 : 0;
}

/// <summary>
/// System information.
/// </summary>
public class SystemInfo
{
    public string OSVersion { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public long TotalMemory { get; set; }
    public DateTime LastBoot { get; set; }
}
