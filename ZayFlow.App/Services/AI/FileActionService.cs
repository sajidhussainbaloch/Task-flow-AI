using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Contracts;

namespace ZayFlow.App.Services.AI;

/// <summary>
/// Implements safe file operations with validation.
/// </summary>
public class FileActionService : IFileActionService
{
    private readonly ILogger<FileActionService> _logger;

    public FileActionService(ILogger<FileActionService> logger)
    {
        _logger = logger;
    }

    public async Task<ActionResult> RenameFileAsync(string filePath, string newName)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                    return new ActionResult { Success = false, Message = $"File not found: {filePath}" };

                if (string.IsNullOrWhiteSpace(newName))
                    return new ActionResult { Success = false, Message = "New name cannot be empty" };

                var directory = Path.GetDirectoryName(filePath);
                var newPath = Path.Combine(directory ?? "", newName);

                File.Move(filePath, newPath, overwrite: false);
                _logger.LogInformation($"Renamed {filePath} to {newPath}");
                return new ActionResult { Success = true, Message = $"File renamed to {newName}" };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Rename error: {ex.Message}");
                return new ActionResult { Success = false, Message = $"Rename failed: {ex.Message}" };
            }
        });
    }

    public async Task<ActionResult> MoveFileAsync(string filePath, string destination)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                    return new ActionResult { Success = false, Message = $"File not found: {filePath}" };

                if (!Directory.Exists(destination))
                    return new ActionResult { Success = false, Message = $"Destination folder not found: {destination}" };

                var fileName = Path.GetFileName(filePath);
                var newPath = Path.Combine(destination, fileName);

                File.Move(filePath, newPath, overwrite: false);
                _logger.LogInformation($"Moved {filePath} to {destination}");
                return new ActionResult { Success = true, Message = $"File moved to {destination}" };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Move error: {ex.Message}");
                return new ActionResult { Success = false, Message = $"Move failed: {ex.Message}" };
            }
        });
    }

    public async Task<ActionResult> DeleteFileAsync(string filePath, bool moveToRecycleBin = true)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                    return new ActionResult { Success = false, Message = $"File not found: {filePath}" };

                if (moveToRecycleBin)
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(filePath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                else
                {
                    File.Delete(filePath);
                }

                _logger.LogInformation($"Deleted {filePath}");
                return new ActionResult { Success = true, Message = "File deleted" };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Delete error: {ex.Message}");
                return new ActionResult { Success = false, Message = $"Delete failed: {ex.Message}" };
            }
        });
    }

    public async Task<ActionResult> CreateFolderAsync(string folderPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogInformation($"[FileService] CreateFolderAsync called with: {folderPath}");
                _logger.LogInformation($"[FileService] Directory.Exists check: {Directory.Exists(folderPath)}");
                
                if (Directory.Exists(folderPath))
                {
                    _logger.LogWarning($"[FileService] Folder already exists at: {folderPath}");
                    return new ActionResult { Success = false, Message = $"Folder already exists at: {folderPath}" };
                }

                var parentDir = Path.GetDirectoryName(folderPath);
                if (!Directory.Exists(parentDir))
                {
                    _logger.LogWarning($"[FileService] Parent directory does not exist. Creating parent first: {parentDir}");
                }

                Directory.CreateDirectory(folderPath);
                
                // Verify it was actually created
                var createdSuccessfully = Directory.Exists(folderPath);
                _logger.LogInformation($"[FileService] Directory.CreateDirectory completed. Exists now: {createdSuccessfully}");
                
                if (!createdSuccessfully)
                {
                    return new ActionResult { Success = false, Message = "Folder was not created (unknown reason)" };
                }

                _logger.LogInformation($"✅ Created folder {folderPath}");
                return new ActionResult { Success = true, Message = $"✅ Folder created at: {folderPath}" };
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError($"[FileService] Access denied creating folder at {folderPath}: {ex.Message}");
                return new ActionResult { Success = false, Message = $"Access denied: {ex.Message}" };
            }
            catch (Exception ex)
            {
                _logger.LogError($"[FileService] Create folder error at {folderPath}: {ex.Message}");
                return new ActionResult { Success = false, Message = $"Create folder failed: {ex.Message}" };
            }
        });
    }

    public async Task<(bool Success, string Content)> ReadFileAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                    return (false, "File not found");

                var extension = Path.GetExtension(filePath).ToLower();
                if (!IsTextFile(extension))
                    return (false, "Only text files (.txt, .md, .json, .xml, .csv) are supported");

                var content = File.ReadAllText(filePath);
                _logger.LogInformation($"Read file {filePath}");
                return (true, content);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Read error: {ex.Message}");
                return (false, $"Read failed: {ex.Message}");
            }
        });
    }

    public async Task<List<string>> ListFilesAsync(string folderPath, string? pattern = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(folderPath))
                    return new List<string>();

                var files = Directory.GetFiles(folderPath, pattern ?? "*")
                    .Take(100)
                    .Select(f => Path.GetFileName(f))
                    .ToList();

                return files;
            }
            catch (Exception ex)
            {
                _logger.LogError($"List error: {ex.Message}");
                return new List<string>();
            }
        });
    }

    public async Task<FileInfo?> GetFileInfoAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                var info = new FileInfo(filePath);
                return info;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Get info error: {ex.Message}");
                return null;
            }
        });
    }

    public async Task<List<(string File1, string File2)>> DetectDuplicatesAsync(string folderPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(folderPath))
                    return new List<(string, string)>();

                var filesBySize = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories)
                    .GroupBy(f => new FileInfo(f).Length)
                    .Where(g => g.Count() > 1)
                    .Take(50);

                var duplicates = new List<(string, string)>();
                foreach (var group in filesBySize)
                {
                    var files = group.ToList();
                    for (int i = 0; i < files.Count - 1; i++)
                    {
                        duplicates.Add((files[i], files[i + 1]));
                    }
                }

                return duplicates;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Detect duplicates error: {ex.Message}");
                return new List<(string, string)>();
            }
        });
    }

    private bool IsTextFile(string extension)
    {
        var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Documents & data
            ".txt", ".md", ".json", ".xml", ".csv", ".log", ".ini", ".config", ".yaml", ".yml", ".toml",
            ".env", ".properties", ".cfg",
            // Web
            ".html", ".htm", ".css", ".scss", ".sass", ".less", ".js", ".jsx", ".ts", ".tsx", ".vue", ".svelte",
            // Programming languages
            ".py", ".cs", ".java", ".c", ".cpp", ".h", ".hpp", ".go", ".rs", ".rb", ".php", ".swift", ".kt",
            ".scala", ".r", ".m", ".mm", ".lua", ".pl", ".pm", ".ex", ".exs", ".clj", ".hs", ".fs", ".fsx",
            ".dart", ".groovy", ".v", ".zig", ".nim",
            // Scripts & shell
            ".sh", ".bash", ".zsh", ".fish", ".bat", ".cmd", ".ps1", ".psm1",
            // Query & database
            ".sql", ".graphql", ".gql",
            // Config & DevOps
            ".dockerfile", ".dockerignore", ".gitignore", ".gitattributes", ".editorconfig",
            ".eslintrc", ".prettierrc", ".babelrc", ".npmrc",
            // Markup
            ".tex", ".latex", ".rst", ".adoc", ".org", ".wiki",
            // Other
            ".svg", ".srt", ".vtt", ".ics", ".vcf", ".diff", ".patch"
        };
        return textExtensions.Contains(extension);
    }
}
