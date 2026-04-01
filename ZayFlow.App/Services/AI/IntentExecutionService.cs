using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Contracts;
using ZayFlow.App.Services.AI.Handlers;

namespace ZayFlow.App.Services.AI;

/// <summary>
/// Executes structured intents from AI with safety validation.
/// </summary>
public class IntentExecutionService
{
    private readonly IFileActionService _fileService;
    private readonly ISystemActionService _systemService;
    private readonly DocumentCreationService _docService;
    private readonly ILogger<IntentExecutionService> _logger;
    private readonly DownloadManager _downloadManager;
    private IAIService? _aiService;
    private NotificationService? _notificationService;

    // Handler instances for new intent tiers
    private FileToolsHandler? _fileToolsHandler;
    private MediaHandler? _mediaHandler;
    private NetworkHandler? _networkHandler;
    private SystemPowerHandler? _systemPowerHandler;
    private DevToolsHandler? _devToolsHandler;
    private BackgroundAutomationHandler? _bgAutoHandler;
    private WorkflowHandler? _workflowHandler;
    private HubHandler? _hubHandler;
    private static readonly HashSet<string> ForbiddenPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
    };

    public IntentExecutionService(IFileActionService fileService, ISystemActionService systemService, DocumentCreationService docService, ILogger<IntentExecutionService> logger, DownloadManager downloadManager)
    {
        _fileService = fileService;
        _systemService = systemService;
        _docService = docService;
        _logger = logger;
        _downloadManager = downloadManager;
    }

    /// <summary>Late-bind AI service to avoid circular DI.</summary>
    public void SetAIService(IAIService aiService) => _aiService = aiService;

    /// <summary>Late-bind notification service for toasts.</summary>
    public void SetNotificationService(NotificationService notificationService) => _notificationService = notificationService;

    /// <summary>Late-bind handler services for new intent tiers.</summary>
    public void SetHandlers(
        FileToolsHandler fileTools, MediaHandler media, NetworkHandler network,
        SystemPowerHandler systemPower, DevToolsHandler devTools,
        BackgroundAutomationHandler bgAuto, WorkflowHandler workflow, HubHandler hub)
    {
        _fileToolsHandler = fileTools;
        _mediaHandler = media;
        _networkHandler = network;
        _systemPowerHandler = systemPower;
        _devToolsHandler = devTools;
        _bgAutoHandler = bgAuto;
        _workflowHandler = workflow;
        _hubHandler = hub;
    }

    /// <summary>Progress reporter set by the ViewModel for live UI updates.</summary>
    private IProgress<ActionProgress>? _progressReporter;
    public void SetProgressReporter(IProgress<ActionProgress>? reporter) => _progressReporter = reporter;

    private void ReportProgress(string status, double percent = -1, string icon = "⏳")
    {
        _progressReporter?.Report(new ActionProgress { Status = status, Percent = percent, Icon = icon });
    }

    /// <summary>
    /// Generates a detailed human-readable preview of what an action will do BEFORE executing it.
    /// Used for destructive/confirmation-required intents to build user trust.
    /// </summary>
    public string GenerateActionPreview(string? intent, Dictionary<string, object> parameters)
    {
        if (string.IsNullOrWhiteSpace(intent)) return string.Empty;

        try
        {
            return intent.ToLowerInvariant() switch
            {
                "organize_folder" => GenerateOrganizeFolderPreview(parameters),
                "clean_desktop" => GenerateCleanDesktopPreview(parameters),
                "clean_temp" => GenerateCleanTempPreview(),
                "delete_files" => GenerateDeletePreview(parameters),
                "move_files" => GenerateMovePreview(parameters),
                "rename_files" => GenerateRenamePreview(parameters),
                "ai_bulk_rename" => GenerateBulkRenamePreview(parameters),
                "secure_delete" => GenerateDeletePreview(parameters),
                _ => string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate action preview for {Intent}", intent);
            return string.Empty;
        }
    }

    private string GenerateOrganizeFolderPreview(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("folderPath", out var folderPath)) return string.Empty;

        var rawPath = folderPath?.ToString() ?? string.Empty;
        var path = ResolvePath(rawPath);
        var mode = parameters.TryGetValue("mode", out var modeObj) ? modeObj?.ToString()?.ToLowerInvariant() ?? "category" : "category";

        if (!Directory.Exists(path)) return $"⚠️ Folder not found: {path}";

        var categoryMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Images", new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg", ".webp", ".ico", ".tiff", ".raw", ".heic" } },
            { "Videos", new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v" } },
            { "Audio", new[] { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a" } },
            { "Documents", new[] { ".pdf", ".doc", ".docx", ".txt", ".rtf", ".odt", ".xls", ".xlsx", ".ppt", ".pptx", ".csv" } },
            { "Archives", new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz" } },
            { "Code", new[] { ".cs", ".py", ".js", ".ts", ".java", ".cpp", ".c", ".h", ".go", ".rs", ".rb", ".php", ".html", ".css", ".json", ".xml", ".yaml", ".yml", ".sql", ".sh", ".bat", ".ps1" } },
            { "Executables", new[] { ".exe", ".msi", ".bat", ".cmd", ".ps1", ".appx", ".msix" } },
            { "Fonts", new[] { ".ttf", ".otf", ".woff", ".woff2", ".eot" } },
            { "Data", new[] { ".db", ".sqlite", ".mdb", ".accdb", ".json", ".xml", ".csv", ".parquet" } },
        };

        var files = Directory.GetFiles(path);
        if (files.Length == 0) return "📂 Folder is empty — nothing to organize.";

        var movements = new Dictionary<string, List<string>>();
        var skipped = 0;

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext)) ext = ".unknown";

            string folderName;
            if (mode == "extension")
                folderName = ext.Trim('.').ToUpperInvariant();
            else
                folderName = categoryMap.FirstOrDefault(kvp => kvp.Value.Contains(ext)).Key ?? "Other";

            var targetPath = Path.Combine(path, folderName, fileName);
            if (File.Exists(targetPath)) { skipped++; continue; }

            if (!movements.ContainsKey(folderName))
                movements[folderName] = new List<string>();
            movements[folderName].Add(fileName);
        }

        if (movements.Count == 0) return "✅ All files are already organized or would be skipped.";

        var sb = new System.Text.StringBuilder();
        var folderDisplay = Path.GetFileName(path) ?? rawPath;
        sb.AppendLine($"📁 Organizing {files.Length} files in {folderDisplay}/ by {mode}:\n");

        var totalShown = 0;
        foreach (var kvp in movements.OrderByDescending(x => x.Value.Count))
        {
            sb.AppendLine($"  📂 {kvp.Key}/");
            var filesToShow = kvp.Value.Take(5).ToList();
            foreach (var f in filesToShow)
            {
                sb.AppendLine($"     {f}  →  {kvp.Key}/");
                totalShown++;
            }
            if (kvp.Value.Count > 5)
                sb.AppendLine($"     ... and {kvp.Value.Count - 5} more");
        }

        var totalMoved = movements.Values.Sum(v => v.Count);
        sb.AppendLine($"\n📊 Summary: {totalMoved} files → {movements.Count} folders");
        if (skipped > 0) sb.AppendLine($"   ⏭️ {skipped} files skipped (already exist at destination)");

        return sb.ToString();
    }

    private string GenerateCleanDesktopPreview(Dictionary<string, object> parameters)
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!Directory.Exists(desktopPath)) return "⚠️ Desktop folder not found.";

        var files = Directory.GetFiles(desktopPath)
            .Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            .Select(f => new FileInfo(f))
            .ToList();

        if (files.Count == 0) return "✅ Desktop is already clean!";

        var archiveName = parameters.TryGetValue("archivePath", out var archObj) && !string.IsNullOrWhiteSpace(archObj?.ToString())
            ? archObj.ToString()! : $"Desktop_Archive_{DateTime.Now:yyyyMMdd}";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"🧹 Moving {files.Count} files from Desktop → {Path.GetFileName(archiveName)}/\n");

        foreach (var fi in files.OrderByDescending(f => f.Length).Take(10))
        {
            var size = fi.Length < 1024 ? $"{fi.Length}B" : fi.Length < 1048576 ? $"{fi.Length / 1024}KB" : $"{fi.Length / 1048576}MB";
            sb.AppendLine($"  {fi.Name} ({size})  →  {Path.GetFileName(archiveName)}/");
        }
        if (files.Count > 10) sb.AppendLine($"  ... and {files.Count - 10} more files");

        var totalSize = files.Sum(f => f.Length);
        var totalSizeStr = totalSize < 1048576 ? $"{totalSize / 1024}KB" : $"{totalSize / 1048576}MB";
        sb.AppendLine($"\n📊 Total: {files.Count} files ({totalSizeStr})");
        return sb.ToString();
    }

    private string GenerateCleanTempPreview()
    {
        var tempPath = Path.GetTempPath();
        if (!Directory.Exists(tempPath)) return "⚠️ Temp folder not found.";

        try
        {
            var files = Directory.GetFiles(tempPath, "*", SearchOption.TopDirectoryOnly);
            var dirs = Directory.GetDirectories(tempPath);
            var totalSize = files.Select(f => { try { return new FileInfo(f).Length; } catch { return 0L; } }).Sum();
            var sizeStr = totalSize < 1048576 ? $"{totalSize / 1024}KB" : $"{totalSize / 1048576}MB";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"🧹 Cleaning temp folder: {tempPath}\n");
            sb.AppendLine($"  📄 {files.Length} files to delete");
            sb.AppendLine($"  📂 {dirs.Length} subfolders to clean");
            sb.AppendLine($"  💾 ~{sizeStr} will be freed");
            return sb.ToString();
        }
        catch { return string.Empty; }
    }

    private string GenerateDeletePreview(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("filePath", out var filePath)) return string.Empty;
        var rawPath = filePath?.ToString() ?? "";
        var path = ResolvePath(rawPath);

        if (File.Exists(path))
        {
            var fi = new FileInfo(path);
            var size = fi.Length < 1024 ? $"{fi.Length}B" : fi.Length < 1048576 ? $"{fi.Length / 1024}KB" : $"{fi.Length / 1048576}MB";
            return $"🗑️ Will delete: {fi.Name} ({size})\n   Location: {fi.DirectoryName}\n   Modified: {fi.LastWriteTime:g}\n   → Moved to Recycle Bin (recoverable)";
        }
        if (Directory.Exists(path))
        {
            var fileCount = SafeEnumerateFiles(path).Count();
            return $"🗑️ Will delete folder: {Path.GetFileName(path)}\n   Contains: {fileCount} files\n   → Moved to Recycle Bin (recoverable)";
        }
        return $"⚠️ Path not found: {path}";
    }

    private string GenerateMovePreview(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("filePath", out var filePath) || !parameters.TryGetValue("destination", out var dest))
            return string.Empty;

        var rawPath = filePath?.ToString() ?? "";
        var rawDest = dest?.ToString() ?? "";
        var path = ResolvePath(rawPath);
        var destPath = ResolvePath(rawDest);
        var fileName = Path.GetFileName(path);
        var exists = File.Exists(path) || Directory.Exists(path);

        if (!exists) return $"⚠️ Source not found: {path}";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"📦 Move preview:\n");
        sb.AppendLine($"  {fileName}");
        sb.AppendLine($"    From: {Path.GetDirectoryName(path)}");
        sb.AppendLine($"    To:   {destPath}");
        if (File.Exists(Path.Combine(destPath, fileName)))
            sb.AppendLine($"\n  ⚠️ Warning: File already exists at destination!");
        return sb.ToString();
    }

    private string GenerateRenamePreview(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("filePath", out var filePath) || !parameters.TryGetValue("newName", out var newName))
            return string.Empty;

        var rawPath = filePath?.ToString() ?? "";
        var path = ResolvePath(rawPath);
        var oldName = Path.GetFileName(path);
        var targetName = newName?.ToString() ?? "";

        if (!File.Exists(path) && !Directory.Exists(path)) return $"⚠️ File not found: {path}";

        return $"✏️ Rename preview:\n\n  {oldName}  →  {targetName}\n  Location: {Path.GetDirectoryName(path)}";
    }

    private string GenerateBulkRenamePreview(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("folderPath", out var folderPath)) return string.Empty;
        var rawPath = folderPath?.ToString() ?? string.Empty;
        var path = ResolvePath(rawPath);

        if (!Directory.Exists(path)) return $"⚠️ Folder not found: {path}";

        var files = Directory.GetFiles(path);
        if (files.Length == 0) return "📂 Folder is empty — nothing to rename.";

        var pattern = parameters.TryGetValue("pattern", out var p) ? p?.ToString() ?? "" : "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"✏️ Bulk rename preview ({files.Length} files in {Path.GetFileName(path)}/):\n");

        foreach (var file in files.Take(8))
            sb.AppendLine($"  {Path.GetFileName(file)}  →  (AI-suggested name)");
        if (files.Length > 8) sb.AppendLine($"  ... and {files.Length - 8} more files");
        if (!string.IsNullOrWhiteSpace(pattern)) sb.AppendLine($"\n  Pattern: {pattern}");

        return sb.ToString();
    }

    public async Task<ActionResult> ExecuteIntentAsync(AIResponse response, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation($"[EXECUTION] Intent={response.Intent}; Parameters={string.Join(",", response.Parameters.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");

            // ── HARD SAFETY BLOCK ── reject any intent that references system-critical paths
            var pathKeys = new[] { "folderPath", "filePath", "sourcePath", "destination", "path", "source", "target" };
            foreach (var key in pathKeys)
            {
                if (response.Parameters.TryGetValue(key, out var val) && val != null)
                {
                    var resolved = ResolvePath(val.ToString() ?? "");
                    if (!ValidatePath(val.ToString() ?? ""))
                    {
                        _logger.LogWarning($"[SAFETY] Blocked intent '{response.Intent}' targeting protected path: {resolved}");
                        return new ActionResult
                        {
                            Success = false,
                            Message = $"🛡️ Safety block: Cannot operate on protected system path.\nPath: {resolved}\n\nThis path is protected to prevent system damage."
                        };
                    }
                }
            }
            
            return response.Intent switch
            {
                "rename_files" => await ExecuteRenameAsync(response.Parameters, ct),
                "move_files" => await ExecuteMoveAsync(response.Parameters, ct),
                "delete_files" => await ExecuteDeleteAsync(response.Parameters, ct),
                "create_folder" => await ExecuteCreateFolderAsync(response.Parameters, ct),
                "organize_folder" => await ExecuteOrganizeFolderAsync(response.Parameters, ct),
                "detect_duplicates" => await ExecuteDetectDuplicatesAsync(response.Parameters, ct),
                "folder_insights" => await ExecuteFolderInsightsAsync(response.Parameters, ct),
                "find_old_files" => await ExecuteFindOldFilesAsync(response.Parameters, ct),
                "clean_desktop" => await ExecuteCleanDesktopAsync(response.Parameters, ct),
                "summarize_file" => await ExecuteSummarizeFileAsync(response.Parameters, ct),
                "generate_text" => await ExecuteGenerateTextAsync(response.Parameters, response.Message, ct),
                "clean_notes" => await ExecuteCleanNotesAsync(response.Parameters, response.Message, ct),
                "plan_tasks" => await ExecutePlanTasksAsync(response.Parameters, response.Message, ct),
                "create_document" => await ExecuteCreateDocumentAsync(response.Parameters, response.Message, ct),
                "quick_automation" => await ExecuteQuickAutomationAsync(response.Parameters, ct),
                "open_application" => await ExecuteOpenApplicationAsync(response.Parameters, ct),
                "open_url" => await ExecuteOpenUrlAsync(response.Parameters, ct),
                "open_file" => await ExecuteOpenFileAsync(response.Parameters, ct),
                "change_wallpaper" => await ExecuteChangeWallpaperAsync(response.Parameters, ct),
                "clean_temp" => await ExecuteCleanTempAsync(ct),
                "get_disk_info" => await ExecuteGetDiskInfoAsync(response.Parameters, ct),
                "read_file" => await ExecuteReadFileAsync(response.Parameters, ct),
                "smart_search" => await ExecuteSmartSearchAsync(response.Parameters, ct),
                "ai_bulk_rename" => await ExecuteAIBulkRenameAsync(response.Parameters, ct),
                "backup_suggestions" => await ExecuteBackupSuggestionsAsync(response.Parameters, ct),
                "smart_cleanup_schedule" => await ExecuteSmartCleanupScheduleAsync(response.Parameters, ct),
                "visual_analytics" => await ExecuteVisualAnalyticsAsync(response.Parameters, ct),
                "create_file" => await ExecuteCreateFileAsync(response.Parameters, response.Message, ct),
                "edit_file" => await ExecuteEditFileAsync(response.Parameters, response.Message, ct),
                "search_web" => await ExecuteSearchWebAsync(response.Parameters, ct),
                "run_command" => await ExecuteRunCommandAsync(response.Parameters, ct),
                "system_info" => await ExecuteSystemInfoAsync(response.Parameters, ct),
                "set_reminder" => await ExecuteSetReminderAsync(response.Parameters, ct),
                "compress_files" => await ExecuteCompressFilesAsync(response.Parameters, ct),
                "clipboard_action" => await ExecuteClipboardActionAsync(response.Parameters, ct),
                "translate_text" => await ExecuteTranslateTextAsync(response.Parameters, ct),
                "download_file" => await ExecuteDownloadFileAsync(response.Parameters, ct),
                "screenshot" => await ExecuteScreenshotAsync(response.Parameters, ct),
                "text_to_speech" => await ExecuteTextToSpeechAsync(response.Parameters, ct),
                "wifi_info" => await ExecuteWifiInfoAsync(response.Parameters, ct),
                "hash_file" => await ExecuteHashFileAsync(response.Parameters, ct),
                "schedule_shutdown" => await ExecuteScheduleShutdownAsync(response.Parameters, ct),
                "convert_units" => await ExecuteConvertUnitsAsync(response.Parameters, ct),
                "date_time" => await ExecuteDateTimeAsync(response.Parameters, ct),
                "generate_password" => await ExecuteGeneratePasswordAsync(response.Parameters, ct),
                "quick_math" => await ExecuteQuickMathAsync(response.Parameters, ct),
                "ping_host" => await ExecutePingHostAsync(response.Parameters, ct),
                "process_action" => await ExecuteProcessActionAsync(response.Parameters, ct),
                "batch_operations" => await ExecuteBatchOperationsAsync(response.Parameters, ct),
                "quick_note" => await ExecuteQuickNoteAsync(response.Parameters, response.Message, ct),
                "focus_mode" => await ExecuteFocusModeAsync(response.Parameters, ct),
                "daily_briefing" => await ExecuteDailyBriefingAsync(response.Parameters, ct),
                "generate_report" => await ExecuteGenerateReportAsync(response.Parameters, ct),
                "file_templates" => await ExecuteFileTemplatesAsync(response.Parameters, ct),
                "workspace_snapshot" => await ExecuteWorkspaceSnapshotAsync(response.Parameters, ct),
                "productivity_tips" => await ExecuteProductivityTipsAsync(response.Parameters, ct),
                "preview_changes" => await ExecutePreviewChangesAsync(response.Parameters, ct),
                "explain_action" => await ExecuteExplainActionAsync(response.Parameters, response.Message, ct),
                "suggest_workflow" => await ExecuteSuggestWorkflowAsync(response.Parameters, ct),

                // Tier 3: File Power Tools (FileToolsHandler)
                "sync_folders" => await ExecuteHandlerAsync(() => _fileToolsHandler!.ExecuteSyncFoldersAsync(
                    response.Parameters,
                    ResolvePath(response.Parameters.GetValueOrDefault("source", "")?.ToString() ?? ""),
                    ResolvePath(response.Parameters.GetValueOrDefault("destination", "")?.ToString() ?? ""), ct), "FileToolsHandler"),
                "file_diff" => await ExecuteHandlerAsync(() => _fileToolsHandler!.ExecuteFileDiffAsync(
                    ResolvePath(response.Parameters.GetValueOrDefault("file1", "")?.ToString() ?? ""),
                    ResolvePath(response.Parameters.GetValueOrDefault("file2", "")?.ToString() ?? ""), ct), "FileToolsHandler"),
                "encrypt_decrypt" => await ExecuteHandlerAsync(() => _fileToolsHandler!.ExecuteEncryptDecryptAsync(
                    ResolvePath(response.Parameters.GetValueOrDefault("path", "")?.ToString() ?? ""),
                    response.Parameters.GetValueOrDefault("action", "encrypt")?.ToString() ?? "encrypt",
                    response.Parameters.GetValueOrDefault("password", "")?.ToString() ?? "", ct), "FileToolsHandler"),
                "secure_delete" => await ExecuteHandlerAsync(() => _fileToolsHandler!.ExecuteSecureDeleteAsync(
                    ResolvePath(response.Parameters.GetValueOrDefault("path", "")?.ToString() ?? ""), ct), "FileToolsHandler"),
                "bulk_metadata" => await ExecuteHandlerAsync(() => _fileToolsHandler!.ExecuteBulkMetadataAsync(
                    ResolvePath(response.Parameters.GetValueOrDefault("path", "")?.ToString() ?? ""), ct), "FileToolsHandler"),
                "regex_search" => await ExecuteHandlerAsync(() => _fileToolsHandler!.ExecuteRegexSearchAsync(
                    ResolvePath(response.Parameters.GetValueOrDefault("path", "")?.ToString() ?? ""),
                    response.Parameters.GetValueOrDefault("pattern", "")?.ToString() ?? "", ct), "FileToolsHandler"),

                // Tier 4: Data & Media (MediaHandler)
                "data_convert" => await ExecuteHandlerAsync(() => _mediaHandler!.ExecuteDataConvertAsync(
                    ResolvePath(response.Parameters.GetValueOrDefault("path", "")?.ToString() ?? ""),
                    response.Parameters.GetValueOrDefault("format", "")?.ToString() ?? "", ct), "MediaHandler"),
                "text_transform" => await ExecuteHandlerAsync(() => _mediaHandler!.ExecuteTextTransformAsync(
                    ResolvePath(response.Parameters.GetValueOrDefault("path", "")?.ToString() ?? ""),
                    response.Parameters.GetValueOrDefault("operation", "stats")?.ToString() ?? "stats", ct), "MediaHandler"),
                "image_tools" => await ExecuteHandlerAsync(() => _mediaHandler!.ExecuteImageToolsAsync(
                    ResolvePath(response.Parameters.GetValueOrDefault("path", "")?.ToString() ?? ""),
                    response.Parameters.GetValueOrDefault("action", "info")?.ToString() ?? "info", ct), "MediaHandler"),
                "pdf_tools" => await ExecuteHandlerAsync(() => _mediaHandler!.ExecutePdfToolsAsync(
                    ResolvePath(response.Parameters.GetValueOrDefault("path", "")?.ToString() ?? ""),
                    response.Parameters.GetValueOrDefault("action", "info")?.ToString() ?? "info", ct), "MediaHandler"),
                "extract_text" => await ExecuteHandlerAsync(() => _mediaHandler!.ExecuteExtractTextAsync(
                    ResolvePath(response.Parameters.GetValueOrDefault("path", "")?.ToString() ?? ""), ct), "MediaHandler"),

                // Tier 5: Network (NetworkHandler)
                "network_diagnostics" => await ExecuteHandlerAsync(() => _networkHandler!.ExecuteNetworkDiagnosticsAsync(ct), "NetworkHandler"),
                "port_scan" => await ExecuteHandlerAsync(() => _networkHandler!.ExecutePortScanAsync(
                    response.Parameters.GetValueOrDefault("target", "localhost")?.ToString() ?? "localhost", ct), "NetworkHandler"),
                "dns_manage" => await ExecuteHandlerAsync(() => _networkHandler!.ExecuteDnsManageAsync(
                    response.Parameters.GetValueOrDefault("domain", "")?.ToString() ?? "",
                    response.Parameters.GetValueOrDefault("action", "lookup")?.ToString() ?? "lookup", ct), "NetworkHandler"),
                "hosts_file" => await ExecuteHandlerAsync(() => _networkHandler!.ExecuteHostsFileAsync(
                    response.Parameters.GetValueOrDefault("action", "view")?.ToString() ?? "view", ct), "NetworkHandler"),

                // Tier 6: System Power (SystemPowerHandler)
                "startup_manager" => await ExecuteHandlerAsync(() => _systemPowerHandler!.ExecuteStartupManagerAsync(
                    response.Parameters.GetValueOrDefault("action", "list")?.ToString() ?? "list", ct), "SystemPowerHandler"),
                "service_manager" => await ExecuteHandlerAsync(() => _systemPowerHandler!.ExecuteServiceManagerAsync(
                    response.Parameters.GetValueOrDefault("action", "list")?.ToString() ?? "list",
                    response.Parameters.GetValueOrDefault("name", "")?.ToString() ?? "", ct), "SystemPowerHandler"),
                "env_variables" => await ExecuteHandlerAsync(() => _systemPowerHandler!.ExecuteEnvVariablesAsync(
                    response.Parameters.GetValueOrDefault("action", "list")?.ToString() ?? "list",
                    response.Parameters.GetValueOrDefault("name", "")?.ToString() ?? "", ct), "SystemPowerHandler"),
                "performance_report" => await ExecuteHandlerAsync(() => _systemPowerHandler!.ExecutePerformanceReportAsync(ct), "SystemPowerHandler"),
                "power_plan" => await ExecuteHandlerAsync(() => _systemPowerHandler!.ExecutePowerPlanAsync(
                    response.Parameters.GetValueOrDefault("action", "list")?.ToString() ?? "list", ct), "SystemPowerHandler"),
                "storage_analyzer" => await ExecuteHandlerAsync(() => _systemPowerHandler!.ExecuteStorageAnalyzerAsync(
                    ResolvePath(response.Parameters.GetValueOrDefault("path", "")?.ToString() ?? ""), ct), "SystemPowerHandler"),

                // Tier 7: Dev Tools (DevToolsHandler)
                "git_quick" => await ExecuteHandlerAsync(() => _devToolsHandler!.ExecuteGitQuickAsync(
                    ResolvePath(response.Parameters.GetValueOrDefault("path", "")?.ToString() ?? ""),
                    response.Parameters.GetValueOrDefault("command", "status")?.ToString() ?? "status", ct), "DevToolsHandler"),
                "api_test" => await ExecuteHandlerAsync(() => _devToolsHandler!.ExecuteApiTestAsync(
                    response.Parameters.GetValueOrDefault("url", "")?.ToString() ?? "",
                    response.Parameters.GetValueOrDefault("method", "GET")?.ToString() ?? "GET",
                    response.Parameters.GetValueOrDefault("body", "")?.ToString() ?? "", ct), "DevToolsHandler"),
                "code_format" => await ExecuteHandlerAsync(() => _devToolsHandler!.ExecuteCodeFormatAsync(
                    ResolvePath(response.Parameters.GetValueOrDefault("path", "")?.ToString() ?? ""),
                    response.Parameters.GetValueOrDefault("action", "analyze")?.ToString() ?? "analyze", ct), "DevToolsHandler"),
                "qr_code" => await ExecuteHandlerAsync(() => _devToolsHandler!.ExecuteQrCodeAsync(
                    response.Parameters.GetValueOrDefault("text", "")?.ToString() ?? "", ct), "DevToolsHandler"),

                // Tier 1 Premium: Background Automation (BackgroundAutomationHandler)
                "watch_folder" => await ExecuteHandlerAsync(() => _bgAutoHandler!.ExecuteWatchFolderAsync(
                    ResolvePath(response.Parameters.GetValueOrDefault("path", "")?.ToString() ?? ""),
                    response.Parameters.GetValueOrDefault("action", "start")?.ToString() ?? "start", ct), "BackgroundAutomationHandler"),
                "scheduled_task" => await ExecuteHandlerAsync(() => _bgAutoHandler!.ExecuteScheduledTaskAsync(
                    response.Parameters.GetValueOrDefault("action", "list")?.ToString() ?? "list",
                    response.Parameters.GetValueOrDefault("name", "")?.ToString() ?? "", ct), "BackgroundAutomationHandler"),
                "auto_backup" => await ExecuteHandlerAsync(() => _bgAutoHandler!.ExecuteAutoBackupAsync(
                    ResolvePath(response.Parameters.GetValueOrDefault("path", "")?.ToString() ?? ""),
                    ResolvePath(response.Parameters.GetValueOrDefault("destination", "")?.ToString() ?? ""), ct), "BackgroundAutomationHandler"),

                // Tier 2 Premium: Workflow (WorkflowHandler)
                "batch_workflow" => await ExecuteHandlerAsync(() => _workflowHandler!.ExecuteBatchWorkflowAsync(
                    response.Parameters.GetValueOrDefault("workflow", "")?.ToString() ?? "", ct), "WorkflowHandler"),
                "save_template" => await ExecuteHandlerAsync(() => _workflowHandler!.ExecuteSaveTemplateAsync(
                    response.Parameters.GetValueOrDefault("name", "")?.ToString() ?? "",
                    response.Parameters.GetValueOrDefault("workflow", "")?.ToString() ?? "",
                    response.Parameters.GetValueOrDefault("action", "save")?.ToString() ?? "save", ct), "WorkflowHandler"),

                // Tier 8 Meta: Hub (HubHandler)
                "zayflow_hub" => await ExecuteHandlerAsync(() => _hubHandler!.ExecuteHubAsync(
                    response.Parameters.GetValueOrDefault("category", "all")?.ToString() ?? "all", ct), "HubHandler"),

                "chat" => new ActionResult { Success = true, Message = response.Message },
                _ => new ActionResult { Success = false, Message = $"Unknown intent: {response.Intent}" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Intent execution error: {ex.Message}");
            return new ActionResult { Success = false, Message = $"Execution error: {ex.Message}" };
        }
    }

    /// <summary>
    /// Resolves known folder names (Downloads, Desktop, Documents, etc.) to full paths.
    /// Handles both simple names (Downloads) and relative paths (Downloads/MyFolder).
    /// </summary>
    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;

        var trimmed = path.Trim();
        var userName = Environment.UserName;
        trimmed = trimmed
            .Replace("<username>", userName, StringComparison.OrdinalIgnoreCase)
            .Replace("{username}", userName, StringComparison.OrdinalIgnoreCase)
            .Replace("%username%", userName, StringComparison.OrdinalIgnoreCase)
            .Replace("<user>", userName, StringComparison.OrdinalIgnoreCase)
            .Replace("{user}", userName, StringComparison.OrdinalIgnoreCase);
        var lower = trimmed.ToLowerInvariant();

        // Map common folder names to actual paths
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var knownFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "downloads", Path.Combine(userProfile, "Downloads") },
            { "my downloads", Path.Combine(userProfile, "Downloads") },
            { "download", Path.Combine(userProfile, "Downloads") },
            { "desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop) },
            { "my desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop) },
            { "documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
            { "my documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
            { "pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) },
            { "my pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) },
            { "music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) },
            { "my music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) },
            { "videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) },
            { "my videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) },
            { "temp", Path.GetTempPath() },
            { "appdata", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) }
        };

        // Handle paths with folder names as prefix (e.g., "Downloads/MyFolder" or "Documents/Work/Project")
        var parts = trimmed.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && knownFolders.TryGetValue(parts[0], out var basePath))
        {
            // Reconstruct: base path + remaining path components
            if (parts.Length > 1)
                return Path.Combine(basePath, Path.Combine(parts.Skip(1).ToArray()));
            else
                return basePath;
        }

        // Check if the entire path is a known folder name
        if (knownFolders.TryGetValue(lower, out var resolvedPath))
            return resolvedPath;

        // Handle paths starting with ~/ or ~\\
        if (trimmed.StartsWith("~/") || trimmed.StartsWith("~\\"))
            return Path.Combine(userProfile, trimmed.Substring(2));
        if (trimmed == "~")
            return userProfile;

        // Return original path if it's already an absolute path
        if (Path.IsPathRooted(trimmed))
            return trimmed;

        // For relative paths, expand to user profile (not current directory!)
        return Path.Combine(userProfile, trimmed);
    }

    private bool ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var resolvedPath = ResolvePath(path);
        var fullPath = Path.GetFullPath(resolvedPath);
        var root = Path.GetPathRoot(fullPath);
        if (string.Equals(root, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !ForbiddenPaths.Any(forbidden =>
            !string.IsNullOrEmpty(forbidden) &&
            fullPath.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Crash-safe file enumeration that skips inaccessible directories and files.
    /// Replaces Directory.GetFiles(..., SearchOption.AllDirectories) which throws on permission errors.
    /// </summary>
    private static IEnumerable<string> SafeEnumerateFiles(string path, string pattern = "*", bool recurse = true)
    {
        return Directory.EnumerateFiles(path, pattern, new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = recurse,
            AttributesToSkip = FileAttributes.ReparsePoint
        });
    }

    /// <summary>
    /// Crash-safe directory enumeration that skips inaccessible entries.
    /// </summary>
    private static IEnumerable<string> SafeEnumerateDirectories(string path, string pattern = "*", bool recurse = true)
    {
        return Directory.EnumerateDirectories(path, pattern, new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = recurse,
            AttributesToSkip = FileAttributes.ReparsePoint
        });
    }

    private async Task<ActionResult> ExecuteRenameAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("filePath", out var filePath) || !parameters.TryGetValue("newName", out var newName))
            return new ActionResult { Success = false, Message = "Missing parameters: filePath, newName" };

        var rawPath = filePath?.ToString() ?? "";
        var path = ResolvePath(rawPath);
        if (!ValidatePath(rawPath))
            return new ActionResult { Success = false, Message = "Access denied: protected system path" };

        return await _fileService.RenameFileAsync(path, newName?.ToString() ?? "");
    }

    private async Task<ActionResult> ExecuteMoveAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("filePath", out var filePath) || !parameters.TryGetValue("destination", out var dest))
            return new ActionResult { Success = false, Message = "Missing parameters: filePath, destination" };

        var rawPath = filePath?.ToString() ?? "";
        var rawDestination = dest?.ToString() ?? "";
        var path = ResolvePath(rawPath);
        var destination = ResolvePath(rawDestination);
        if (!ValidatePath(rawPath) || !ValidatePath(rawDestination))
            return new ActionResult { Success = false, Message = "Access denied: protected system path" };

        return await _fileService.MoveFileAsync(path, destination);
    }

    private async Task<ActionResult> ExecuteDeleteAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("filePath", out var filePath))
            return new ActionResult { Success = false, Message = "Missing parameter: filePath" };

        var rawPath = filePath?.ToString() ?? "";
        var path = ResolvePath(rawPath);
        if (!ValidatePath(rawPath))
            return new ActionResult { Success = false, Message = "Access denied: protected system path" };

        return await _fileService.DeleteFileAsync(path, moveToRecycleBin: true);
    }

    private async Task<ActionResult> ExecuteCreateFolderAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("folderPath", out var folderPath))
            return new ActionResult { Success = false, Message = "Missing parameter: folderPath" };

        var rawPath = folderPath?.ToString() ?? "";
        var path = ResolvePath(rawPath);
        var template = parameters.TryGetValue("template", out var tpl) ? tpl?.ToString()?.ToLowerInvariant() ?? "" : "";
        _logger.LogInformation($"[CREATE_FOLDER] Raw={rawPath}; Resolved={path}; Template={template}");
        
        if (!ValidatePath(rawPath))
            return new ActionResult { Success = false, Message = "Access denied: protected system path" };

        if (Directory.Exists(path))
        {
            _logger.LogInformation($"[CREATE_FOLDER] Path already exists: {path}");
            return new ActionResult { Success = false, Message = $"Folder already exists at: {path}" };
        }

        var result = await _fileService.CreateFolderAsync(path);
        if (!result.Success) return result;

        // Create sub-folders based on template
        var createdDirs = new List<string>();
        var subfolders = template switch
        {
            "project" => new[] { "src", "docs", "assets", "tests", "build" },
            "media" => new[] { "images", "videos", "audio", "thumbnails" },
            "web" => new[] { "html", "css", "js", "images", "fonts" },
            "school" or "academic" => new[] { "notes", "assignments", "resources", "exams" },
            "photography" => new[] { "raw", "edited", "exports", "albums" },
            "client" or "business" => new[] { "contracts", "invoices", "deliverables", "correspondence" },
            _ => Array.Empty<string>()
        };

        foreach (var sub in subfolders)
        {
            var subPath = Path.Combine(path, sub);
            Directory.CreateDirectory(subPath);
            createdDirs.Add(sub);
        }

        var msg = result.Message;
        if (createdDirs.Count > 0)
            msg += $"\n📂 Template '{template}' applied: {string.Join(", ", createdDirs)}";

        _logger.LogInformation($"[CREATE_FOLDER_RESULT] Path={path}; Success={result.Success}; Template={template}; SubDirs={createdDirs.Count}");
        return new ActionResult { Success = true, Message = msg };
    }

    private Task<ActionResult> ExecuteOrganizeFolderAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("folderPath", out var folderPath))
            return Task.FromResult(new ActionResult { Success = false, Message = "Missing parameter: folderPath" });

        var rawPath = folderPath?.ToString() ?? string.Empty;
        var path = ResolvePath(rawPath);
        var mode = parameters.TryGetValue("mode", out var modeObj) ? modeObj?.ToString()?.ToLowerInvariant() ?? "category" : "category";
        
        if (!ValidatePath(rawPath))
            return Task.FromResult(new ActionResult { Success = false, Message = "Access denied: protected system path" });
        if (!Directory.Exists(path))
            return Task.FromResult(new ActionResult { Success = false, Message = $"Folder not found: {path}" });

        // Category-based grouping (default) vs raw extension grouping
        var categoryMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Images", new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg", ".webp", ".ico", ".tiff", ".raw", ".heic" } },
            { "Videos", new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v" } },
            { "Audio", new[] { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a" } },
            { "Documents", new[] { ".pdf", ".doc", ".docx", ".txt", ".rtf", ".odt", ".xls", ".xlsx", ".ppt", ".pptx", ".csv" } },
            { "Archives", new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz" } },
            { "Code", new[] { ".cs", ".py", ".js", ".ts", ".java", ".cpp", ".c", ".h", ".go", ".rs", ".rb", ".php", ".html", ".css", ".json", ".xml", ".yaml", ".yml", ".sql", ".sh", ".bat", ".ps1" } },
            { "Executables", new[] { ".exe", ".msi", ".bat", ".cmd", ".ps1", ".appx", ".msix" } },
            { "Fonts", new[] { ".ttf", ".otf", ".woff", ".woff2", ".eot" } },
            { "Data", new[] { ".db", ".sqlite", ".mdb", ".accdb", ".json", ".xml", ".csv", ".parquet" } },
        };

        var files = Directory.GetFiles(path);
        var movedCount = 0;
        var categorySummary = new Dictionary<string, int>();

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext)) ext = ".unknown";

            string folderName;
            if (mode == "extension")
            {
                folderName = ext.Trim('.').ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(folderName)) folderName = "OTHER";
            }
            else
            {
                folderName = categoryMap.FirstOrDefault(kvp => kvp.Value.Contains(ext)).Key ?? "Other";
            }

            var destination = Path.Combine(path, folderName);
            Directory.CreateDirectory(destination);
            var targetPath = Path.Combine(destination, Path.GetFileName(file));
            if (File.Exists(targetPath)) continue;

            File.Move(file, targetPath);
            movedCount++;
            categorySummary[folderName] = categorySummary.GetValueOrDefault(folderName) + 1;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"✅ Organized {movedCount} files by {mode} in {path}\n");
        foreach (var kvp in categorySummary.OrderByDescending(x => x.Value))
            sb.AppendLine($"  📂 {kvp.Key}: {kvp.Value} file{(kvp.Value > 1 ? "s" : "")}");

        return Task.FromResult(new ActionResult { Success = true, Message = sb.ToString() });
    }

    private async Task<ActionResult> ExecuteDetectDuplicatesAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("folderPath", out var folderPath))
            return new ActionResult { Success = false, Message = "Missing parameter: folderPath" };

        var rawPath = folderPath?.ToString() ?? string.Empty;
        var path = ResolvePath(rawPath);
        var minSizeKB = parameters.TryGetValue("minSize", out var sizeObj) && int.TryParse(sizeObj?.ToString(), out var ms) ? ms : 0;
        
        if (!ValidatePath(rawPath))
            return new ActionResult { Success = false, Message = "Access denied: protected system path" };
        if (!Directory.Exists(path))
            return new ActionResult { Success = false, Message = $"Folder not found: {path}" };

        ReportProgress("🔍 Scanning files for duplicates...", 10, "🔍");

        try
        {
            var files = SafeEnumerateFiles(path)
                .Select(f => new FileInfo(f))
                .Where(f => f.Length >= minSizeKB * 1024)
                .ToList();

            // Group by size first (fast pre-filter)
            var sizeGroups = files.GroupBy(f => f.Length)
                .Where(g => g.Count() > 1)
                .ToList();

            ReportProgress($"🔍 Hashing {sizeGroups.Sum(g => g.Count())} potential duplicates...", 40, "🔍");

            // Hash only files with matching sizes
            var duplicateGroups = new List<(string Hash, List<FileInfo> Files)>();
            using var sha256 = System.Security.Cryptography.SHA256.Create();

            foreach (var group in sizeGroups)
            {
                var hashMap = new Dictionary<string, List<FileInfo>>();
                foreach (var fi in group)
                {
                    try
                    {
                        using var stream = fi.OpenRead();
                        var hash = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "");
                        if (!hashMap.ContainsKey(hash)) hashMap[hash] = new List<FileInfo>();
                        hashMap[hash].Add(fi);
                    }
                    catch { /* Skip locked files */ }
                }
                foreach (var kvp in hashMap.Where(h => h.Value.Count > 1))
                    duplicateGroups.Add((kvp.Key, kvp.Value));
            }

            if (duplicateGroups.Count == 0)
                return new ActionResult { Success = true, Message = "✅ No duplicates found!" };

            var totalWasted = duplicateGroups.Sum(g => g.Files.Skip(1).Sum(f => f.Length));
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"🔍 Found {duplicateGroups.Count} duplicate groups ({totalWasted / 1024 / 1024}MB wasted):\n");

            foreach (var (hash, groupFiles) in duplicateGroups.Take(15))
            {
                sb.AppendLine($"📎 Group (SHA256: {hash[..12]}...) — {groupFiles[0].Length / 1024}KB each:");
                foreach (var f in groupFiles)
                    sb.AppendLine($"  • {Path.GetRelativePath(path, f.FullName)}");
            }

            if (duplicateGroups.Count > 15)
                sb.AppendLine($"\n... and {duplicateGroups.Count - 15} more groups");

            return new ActionResult { Success = true, Message = sb.ToString() };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Duplicate detection error: {ex.Message}");
            return new ActionResult { Success = false, Message = $"Duplicate scan failed: {ex.Message}" };
        }
    }

    private Task<ActionResult> ExecuteFolderInsightsAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("folderPath", out var folderPath))
            return Task.FromResult(new ActionResult { Success = false, Message = "Missing parameter: folderPath" });

        var rawPath = folderPath?.ToString() ?? string.Empty;
        var path = ResolvePath(rawPath);
        
        if (!ValidatePath(rawPath))
            return Task.FromResult(new ActionResult { Success = false, Message = "Access denied: protected system path" });
        if (!Directory.Exists(path))
            return Task.FromResult(new ActionResult { Success = false, Message = $"📁 Folder not found: {path}" });

        try
        {
            var di = new DirectoryInfo(path);
            var allFiles = di.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToList();
            
            if (allFiles.Count == 0)
                return Task.FromResult(new ActionResult { Success = true, Message = "📂 Folder is empty" });

            long totalSize = allFiles.Sum(f => f.Length);
            var largestFiles = allFiles.OrderByDescending(f => f.Length).Take(10).ToList();
            var subfolders = di.GetDirectories().Length;
            var files = allFiles.Count;
            var avgSize = files > 0 ? totalSize / files : 0;

            var report = new System.Text.StringBuilder();
            report.AppendLine($"📊 FOLDER ANALYSIS: {Path.GetFileName(path) ?? "Root"}");
            report.AppendLine($"   Location: {path}");
            report.AppendLine();
            report.AppendLine($"📈 Statistics:");
            report.AppendLine($"   Total Files: {files}");
            report.AppendLine($"   Subfolders: {subfolders}");
            report.AppendLine($"   Total Size: {(totalSize / 1024 / 1024 / 1024)}GB ({(totalSize / 1024 / 1024)}MB)");
            report.AppendLine($"   Average File Size: {(avgSize / 1024)}KB");
            report.AppendLine();
            report.AppendLine($"📌 Largest Files (Top 10):");
            
            int i = 1;
            foreach (var file in largestFiles)
            {
                var sizeDisplay = file.Length > 1024 * 1024 
                    ? $"{file.Length / 1024 / 1024}MB" 
                    : $"{file.Length / 1024}KB";
                report.AppendLine($"   {i}. {file.Name} - {sizeDisplay}");
                i++;
            }

            return Task.FromResult(new ActionResult { Success = true, Message = report.ToString() });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Folder insights error: {ex.Message}");
            return Task.FromResult(new ActionResult { Success = false, Message = $"❌ Analysis failed: {ex.Message}" });
        }
    }

    private async Task<ActionResult> ExecuteCreateDocumentAsync(Dictionary<string, object> parameters, string aiContent, CancellationToken ct)
    {
        var title = parameters.TryGetValue("title", out var t) ? t?.ToString() ?? "Document" : "Document";
        var content = parameters.TryGetValue("content", out var c) ? c?.ToString() ?? aiContent : aiContent;
        var type = parameters.TryGetValue("type", out var tp) ? tp?.ToString() ?? "document" : "document";
        var openAfter = !parameters.TryGetValue("open", out var o) || (o?.ToString()?.ToLower() != "false");

        try
        {
            var (success, filePath, message) = await _docService.CreateFormattedDocumentAsync(title, content, type);
            if (!success) return new ActionResult { Success = false, Message = message };

            // Auto-open the file after creation
            if (openAfter && File.Exists(filePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }

            return new ActionResult { Success = true, Message = $"{message}\n📂 Saved to Desktop and opened for editing." };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Create document error: {ex.Message}");
            return new ActionResult { Success = false, Message = $"Failed to create document: {ex.Message}" };
        }
    }

    private async Task<ActionResult> ExecuteOpenApplicationAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        // Accept both "appName" and "application" parameter names
        var appRaw = "";
        if (parameters.TryGetValue("appName", out var a1)) appRaw = a1?.ToString() ?? "";
        else if (parameters.TryGetValue("application", out var a2)) appRaw = a2?.ToString() ?? "";
        else if (parameters.TryGetValue("app", out var a3)) appRaw = a3?.ToString() ?? "";

        if (string.IsNullOrWhiteSpace(appRaw))
            return new ActionResult { Success = false, Message = "Please specify which application to open (e.g. notepad, word, chrome, explorer)." };

        var appName = appRaw.Trim().ToLowerInvariant();

        // Map common names to executable paths
        var appMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "word", "winword" },
            { "microsoft word", "winword" },
            { "ms word", "winword" },
            { "notepad", "notepad.exe" },
            { "calculator", "calc.exe" },
            { "calc", "calc.exe" },
            { "paint", "mspaint.exe" },
            { "explorer", "explorer.exe" },
            { "file explorer", "explorer.exe" },
            { "cmd", "cmd.exe" },
            { "powershell", "powershell.exe" },
            { "chrome", "chrome" },
            { "google chrome", "chrome" },
            { "firefox", "firefox" },
            { "edge", "msedge" },
            { "microsoft edge", "msedge" },
            { "vlc", "vlc" },
            { "spotify", "spotify" },
            { "excel", "excel" },
            { "powerpoint", "powerpnt" },
            { "outlook", "outlook" },
            { "teams", "teams" },
            { "zoom", "zoom" },
            { "vscode", "code" },
            { "visual studio code", "code" },
        };

        // If the app couldn't be found in map, try fuzzy matching
        string exe;
        if (appMap.TryGetValue(appName, out var mapped))
        {
            exe = mapped;
        }
        else
        {
            // Fuzzy match: find closest key using substring matching
            var fuzzyMatch = appMap.Keys.FirstOrDefault(k => 
                k.Contains(appName, StringComparison.OrdinalIgnoreCase) ||
                appName.Contains(k, StringComparison.OrdinalIgnoreCase));
            
            if (fuzzyMatch != null)
            {
                exe = appMap[fuzzyMatch];
            }
            else
            {
                // Try to find in Start Menu shortcuts
                var startMenuPaths = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
                };
                var shortcut = startMenuPaths
                    .Where(Directory.Exists)
                    .SelectMany(p => Directory.GetFiles(p, "*.lnk", SearchOption.AllDirectories))
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Contains(appRaw, StringComparison.OrdinalIgnoreCase));
                
                exe = shortcut ?? appRaw;
            }
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = exe, UseShellExecute = true });
            return new ActionResult { Success = true, Message = $"✅ Opened {appRaw}." };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Open application error: {ex.Message}");
            return new ActionResult { Success = false, Message = $"Could not open '{appRaw}': {ex.Message}" };
        }
    }

    private async Task<ActionResult> ExecuteOpenUrlAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        var url = "";
        if (parameters.TryGetValue("url", out var u)) url = u?.ToString() ?? "";
        else if (parameters.TryGetValue("website", out var w)) url = w?.ToString() ?? "";
        else if (parameters.TryGetValue("site", out var s)) url = s?.ToString() ?? "";

        if (string.IsNullOrWhiteSpace(url))
            return new ActionResult { Success = false, Message = "Please specify a URL or website name." };

        // Map common services to URLs
        var serviceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "gmail", "https://mail.google.com" },
            { "google mail", "https://mail.google.com" },
            { "email", "https://mail.google.com" },
            { "youtube", "https://www.youtube.com" },
            { "google", "https://www.google.com" },
            { "github", "https://github.com" },
            { "twitter", "https://twitter.com" },
            { "x", "https://x.com" },
            { "facebook", "https://www.facebook.com" },
            { "instagram", "https://www.instagram.com" },
            { "reddit", "https://www.reddit.com" },
            { "linkedin", "https://www.linkedin.com" },
            { "whatsapp", "https://web.whatsapp.com" },
            { "whatsapp web", "https://web.whatsapp.com" },
            { "telegram", "https://web.telegram.org" },
            { "discord", "https://discord.com/app" },
            { "netflix", "https://www.netflix.com" },
            { "spotify", "https://open.spotify.com" },
            { "amazon", "https://www.amazon.com" },
            { "chatgpt", "https://chat.openai.com" },
            { "google drive", "https://drive.google.com" },
            { "google docs", "https://docs.google.com" },
            { "google sheets", "https://sheets.google.com" },
            { "google maps", "https://maps.google.com" },
            { "stackoverflow", "https://stackoverflow.com" },
            { "stack overflow", "https://stackoverflow.com" },
            { "outlook", "https://outlook.live.com" },
            { "hotmail", "https://outlook.live.com" },
            { "yahoo", "https://mail.yahoo.com" },
            { "yahoo mail", "https://mail.yahoo.com" },
            { "notion", "https://www.notion.so" },
            { "trello", "https://trello.com" },
            { "figma", "https://www.figma.com" },
            { "canva", "https://www.canva.com" },
        };

        var finalUrl = serviceMap.TryGetValue(url.Trim(), out var mapped) ? mapped : url.Trim();

        // Smart URL building: add .com for bare words, handle partial domains
        if (!finalUrl.Contains('.') && !finalUrl.Contains('/'))
        {
            // It's a bare word like "google" — try fuzzy match in serviceMap
            var fuzzyKey = serviceMap.Keys.FirstOrDefault(k => 
                k.Contains(finalUrl, StringComparison.OrdinalIgnoreCase) ||
                finalUrl.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (fuzzyKey != null)
                finalUrl = serviceMap[fuzzyKey];
            else
                finalUrl = $"www.{finalUrl}.com"; // Assume .com TLD
        }

        // Add https:// if missing
        if (!finalUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !finalUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            finalUrl = "https://" + finalUrl;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = finalUrl,
                UseShellExecute = true
            });
            return new ActionResult { Success = true, Message = $"✅ Opened {url} in your browser." };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Open URL error: {ex.Message}");
            return new ActionResult { Success = false, Message = $"Could not open '{url}': {ex.Message}" };
        }
    }

    private async Task<ActionResult> ExecuteOpenFileAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        // Get the file path from parameters
        var filePath = "";
        if (parameters.TryGetValue("filePath", out var fp)) filePath = fp?.ToString() ?? "";
        else if (parameters.TryGetValue("path", out var p)) filePath = p?.ToString() ?? "";
        else if (parameters.TryGetValue("file", out var f)) filePath = f?.ToString() ?? "";

        if (string.IsNullOrWhiteSpace(filePath))
            return new ActionResult { Success = false, Message = "Please specify which file to open." };

        // Resolve path
        filePath = ResolvePath(filePath);

        if (!File.Exists(filePath))
        {
            // Try to find it on Desktop if it's just a filename
            var desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Path.GetFileName(filePath));
            if (File.Exists(desktopPath))
                filePath = desktopPath;
            else
                return new ActionResult { Success = false, Message = $"File not found: {filePath}" };
        }

        // Check if user wants to open with a specific app
        var withApp = "";
        if (parameters.TryGetValue("application", out var app)) withApp = app?.ToString() ?? "";
        else if (parameters.TryGetValue("app", out var a)) withApp = a?.ToString() ?? "";
        else if (parameters.TryGetValue("openWith", out var ow)) withApp = ow?.ToString() ?? "";

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo();

            if (!string.IsNullOrWhiteSpace(withApp))
            {
                // Map common app names to executables
                var appMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "word", "winword" }, { "microsoft word", "winword" }, { "ms word", "winword" },
                    { "notepad", "notepad.exe" }, { "excel", "excel" }, { "powerpoint", "powerpnt" },
                    { "vscode", "code" }, { "code", "code" },
                };
                var exe = appMap.TryGetValue(withApp.Trim(), out var mapped) ? mapped : withApp;
                psi.FileName = exe;
                psi.Arguments = $"\"{filePath}\"";
                psi.UseShellExecute = false;
            }
            else
            {
                // Open with default associated application
                psi.FileName = filePath;
                psi.UseShellExecute = true;
            }

            System.Diagnostics.Process.Start(psi);
            var appLabel = string.IsNullOrWhiteSpace(withApp) ? "default app" : withApp;
            return new ActionResult { Success = true, Message = $"✅ Opened {Path.GetFileName(filePath)} with {appLabel}." };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Open file error: {ex.Message}");
            return new ActionResult { Success = false, Message = $"Could not open file: {ex.Message}" };
        }
    }

    private async Task<ActionResult> ExecuteChangeWallpaperAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("imagePath", out var imagePath))
            return new ActionResult { Success = false, Message = "Missing parameter: imagePath" };

        var rawPath = imagePath?.ToString() ?? "";
        var path = ResolvePath(rawPath);
        if (!File.Exists(path))
            return new ActionResult { Success = false, Message = $"Image file not found: {path}" };

        return await _systemService.ChangeWallpaperAsync(path);
    }

    private async Task<ActionResult> ExecuteCleanTempAsync(CancellationToken ct)
    {
        return await _systemService.CleanTempFilesAsync();
    }

    private async Task<ActionResult> ExecuteGetDiskInfoAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        var drive = parameters.TryGetValue("drive", out var d) ? d?.ToString() ?? "C" : "C";
        var info = await _systemService.GetDiskUsageAsync(drive);
        var percent = info.UsagePercent;
        return new ActionResult
        {
            Success = true,
            Message = $"Drive {info.Drive}: {percent:F1}% used ({info.UsedSize / (1024 * 1024 * 1024)}GB / {info.TotalSize / (1024 * 1024 * 1024)}GB)"
        };
    }

    private async Task<ActionResult> ExecuteReadFileAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("filePath", out var filePath))
            return new ActionResult { Success = false, Message = "Missing parameter: filePath" };

        var rawPath = filePath?.ToString() ?? "";
        var path = ResolvePath(rawPath);
        if (!ValidatePath(rawPath))
            return new ActionResult { Success = false, Message = "Access denied: protected system path" };

        var (success, content) = await _fileService.ReadFileAsync(path);
        return new ActionResult { Success = success, Message = success ? content : "Failed to read file" };
    }

    private Task<ActionResult> ExecuteFindOldFilesAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("folderPath", out var folderPath))
            return Task.FromResult(new ActionResult { Success = false, Message = "Missing parameter: folderPath" });

        var rawPath = folderPath?.ToString() ?? string.Empty;
        var path = ResolvePath(rawPath);
        
        if (!ValidatePath(rawPath))
            return Task.FromResult(new ActionResult { Success = false, Message = "Access denied: protected system path" });
        if (!Directory.Exists(path))
            return Task.FromResult(new ActionResult { Success = false, Message = $"📁 Folder not found: {path}" });

        try
        {
            var daysOld = 90;
            if (parameters.TryGetValue("daysOld", out var daysObj) && int.TryParse(daysObj?.ToString(), out var parsed))
                daysOld = parsed;

            var cutoff = DateTime.Now.AddDays(-daysOld);
            var oldFiles = SafeEnumerateFiles(path)
                .Select(f => new FileInfo(f))
                .Where(f => f.LastWriteTime < cutoff)
                .OrderBy(f => f.LastWriteTime)
                .Take(30)
                .ToList();

            if (oldFiles.Count == 0)
                return Task.FromResult(new ActionResult { Success = true, Message = $"✅ No files older than {daysOld} days found in {Path.GetFileName(path)}" });

            long totalOldSize = oldFiles.Sum(f => f.Length);
            var report = new System.Text.StringBuilder();
            report.AppendLine($"🕐 OLD FILES (older than {daysOld} days):");
            report.AppendLine($"   Found: {oldFiles.Count} files");
            report.AppendLine($"   Total Size: {(totalOldSize / 1024 / 1024)}MB");
            report.AppendLine();
            
            foreach (var file in oldFiles.Take(20))
            {
                var sizeDisplay = file.Length > 1024 * 1024 ? $"{file.Length / 1024 / 1024}MB" : $"{file.Length / 1024}KB";
                var daysAgoCount = (DateTime.Now - file.LastWriteTime).Days;
                report.AppendLine($"   📄 {file.Name}");
                report.AppendLine($"      Last modified: {file.LastWriteTime:MMM dd, yyyy} ({daysAgoCount} days ago)");
                report.AppendLine($"      Size: {sizeDisplay}");
            }

            if (oldFiles.Count > 20)
            {
                report.AppendLine($"\n   ... and {oldFiles.Count - 20} more files");
            }

            report.AppendLine("\n💡 Tip: These files are candidates for archiving or deletion to free up space.");

            return Task.FromResult(new ActionResult { Success = true, Message = report.ToString() });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Find old files error: {ex.Message}");
            return Task.FromResult(new ActionResult { Success = false, Message = $"❌ Failed: {ex.Message}" });
        }
    }

    private Task<ActionResult> ExecuteCleanDesktopAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var archiveName = parameters.TryGetValue("archivePath", out var archObj) && !string.IsNullOrWhiteSpace(archObj?.ToString())
            ? archObj.ToString()!
            : Path.Combine(desktopPath, "Desktop_Archive_" + DateTime.Now.ToString("yyyyMMdd"));

        if (!Directory.Exists(desktopPath))
            return Task.FromResult(new ActionResult { Success = false, Message = "Desktop folder not found" });

        var files = Directory.GetFiles(desktopPath)
            .Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (files.Count == 0)
            return Task.FromResult(new ActionResult { Success = true, Message = "Desktop is already clean!" });

        Directory.CreateDirectory(archiveName);
        var movedCount = 0;
        foreach (var file in files)
        {
            try
            {
                var dest = Path.Combine(archiveName, Path.GetFileName(file));
                if (!File.Exists(dest))
                {
                    File.Move(file, dest);
                    movedCount++;
                }
            }
            catch { /* Skip files that can't be moved */ }
        }

        return Task.FromResult(new ActionResult
        {
            Success = true,
            Message = $"Moved {movedCount} files from Desktop to {archiveName}"
        });
    }

    private async Task<ActionResult> ExecuteSummarizeFileAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("filePath", out var filePath))
            return new ActionResult { Success = false, Message = "Missing parameter: filePath" };

        var rawPath = filePath?.ToString() ?? "";
        var path = ResolvePath(rawPath);
        if (!ValidatePath(rawPath))
            return new ActionResult { Success = false, Message = "Access denied: protected system path" };

        var (success, content) = await _fileService.ReadFileAsync(path);
        if (!success)
            return new ActionResult { Success = false, Message = "Failed to read file for summarization" };

        // Actually summarize via AI
        if (_aiService != null && content.Length > 0)
        {
            try
            {
                var truncated = content.Length > 8000 ? content.Substring(0, 8000) + "\n... (truncated)" : content;
                var summaryPrompt = $"Summarize this file content in 3-5 bullet points. Be concise and focus on key information:\n\n{truncated}";
                var summary = await _aiService.GenerateTextAsync(summaryPrompt);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    return new ActionResult
                    {
                        Success = true,
                        Message = $"📄 **Summary of {Path.GetFileName(path)}** ({content.Length:N0} chars):\n\n{summary}"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"AI summarization failed, falling back: {ex.Message}");
            }
        }

        // Fallback: return file stats and preview
        var preview = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
        return new ActionResult
        {
            Success = true,
            Message = $"📄 **{Path.GetFileName(path)}** ({content.Length:N0} characters):\n\n{preview}"
        };
    }

    private async Task<ActionResult> ExecuteGenerateTextAsync(Dictionary<string, object> parameters, string generatedText, CancellationToken ct)
    {
        try
        {
            var type = parameters.TryGetValue("type", out var typeObj) ? typeObj?.ToString() ?? "text" : "text";
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var fileName = type switch
            {
                "email" => $"email_{timestamp}.txt",
                "proposal" => $"proposal_{timestamp}.txt",
                "reply" => $"reply_{timestamp}.txt",
                "message" => $"message_{timestamp}.txt",
                _ => $"generated_{timestamp}.txt"
            };

            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var filePath = Path.Combine(desktopPath, fileName);

            await File.WriteAllTextAsync(filePath, generatedText, ct);
            _logger.LogInformation($"✅ Generated {type} saved to: {filePath}");

            return new ActionResult
            {
                Success = true,
                Message = $"✅ {(string.IsNullOrEmpty(type) ? "Text" : char.ToUpper(type[0]) + type.Substring(1))} has been saved to your Desktop as {fileName}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Generate text error: {ex.Message}");
            return new ActionResult { Success = false, Message = $"Failed to save text: {ex.Message}" };
        }
    }

    private async Task<ActionResult> ExecuteCleanNotesAsync(Dictionary<string, object> parameters, string cleanedText, CancellationToken ct)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var fileName = $"notes_cleaned_{timestamp}.txt";
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var filePath = Path.Combine(desktopPath, fileName);

            await File.WriteAllTextAsync(filePath, cleanedText, ct);
            _logger.LogInformation($"✅ Cleaned notes saved to: {filePath}");

            return new ActionResult
            {
                Success = true,
                Message = $"✅ Your notes have been cleaned and organized. Saved to Desktop as {fileName}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Clean notes error: {ex.Message}");
            return new ActionResult { Success = false, Message = $"Failed to save cleaned notes: {ex.Message}" };
        }
    }

    private async Task<ActionResult> ExecutePlanTasksAsync(Dictionary<string, object> parameters, string planText, CancellationToken ct)
    {
        try
        {
            var goal = parameters.TryGetValue("goal", out var goalObj) ? goalObj?.ToString() ?? "Plan" : "Plan";
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var fileName = $"plan_{timestamp}.txt";
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var filePath = Path.Combine(desktopPath, fileName);

            var content = $"GOAL: {goal}\n\n{DateTime.Now:f}\n\n{planText}";
            await File.WriteAllTextAsync(filePath, content, ct);
            _logger.LogInformation($"✅ Action plan saved to: {filePath}");

            return new ActionResult
            {
                Success = true,
                Message = $"✅ Your action plan has been created and saved to Desktop as {fileName}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Plan tasks error: {ex.Message}");
            return new ActionResult { Success = false, Message = $"Failed to save plan: {ex.Message}" };
        }
    }

    private async Task<ActionResult> ExecuteQuickAutomationAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        var task = parameters.TryGetValue("task", out var taskObj) ? taskObj?.ToString() ?? "" : "";
        
        // Support chaining: comma or pipe-separated tasks
        var tasks = task.Split(new[] { ',', '|', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        if (tasks.Count == 0)
            return new ActionResult { Success = false, Message = "No automation task specified. Available: clean_temp, organize_downloads, clean_desktop, detect_duplicates" };

        var results = new System.Text.StringBuilder();
        var successCount = 0;
        var totalTasks = tasks.Count;

        for (int i = 0; i < tasks.Count; i++)
        {
            var currentTask = tasks[i];
            ReportProgress($"🔄 Running {currentTask} ({i + 1}/{totalTasks})...", (double)(i) / totalTasks * 100, "⚡");

            var result = currentTask switch
            {
                "clean_temp" or "clean temp" => await ExecuteCleanTempAsync(ct),
                "organize_downloads" or "organize downloads" => await ExecuteOrganizeFolderAsync(
                    new Dictionary<string, object> { { "folderPath", "Downloads" } }, ct),
                "clean_desktop" or "clean desktop" => await ExecuteCleanDesktopAsync(parameters, ct),
                "detect_duplicates" or "find duplicates" => await ExecuteDetectDuplicatesAsync(
                    new Dictionary<string, object> { { "folderPath", "Downloads" } }, ct),
                _ => new ActionResult { Success = false, Message = $"Unknown task: {currentTask}" }
            };

            results.AppendLine($"{(result.Success ? "✅" : "❌")} {currentTask}: {result.Message.Split('\n')[0]}");
            if (result.Success) successCount++;
        }

        return new ActionResult
        {
            Success = successCount > 0,
            Message = totalTasks > 1 
                ? $"⚡ Automation chain complete ({successCount}/{totalTasks} succeeded):\n\n{results}"
                : results.ToString().Trim()
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // 🔥 PREMIUM FEATURES - High-value productivity boosters
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// PREMIUM: Smart Search - Find files by name AND content using natural language.
    /// </summary>
    private async Task<ActionResult> ExecuteSmartSearchAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("query", out var queryObj))
            return new ActionResult { Success = false, Message = "Missing parameter: query" };

        var query = queryObj?.ToString() ?? string.Empty;
        var scope = parameters.TryGetValue("scope", out var scopeObj) ? scopeObj?.ToString() ?? "Documents" : "Documents";

        try
        {
            var searchPath = ResolvePath(scope);
            if (!Directory.Exists(searchPath))
                return new ActionResult { Success = false, Message = $"Folder not found: {searchPath}" };

            ReportProgress("🔍 Searching by file name...", 10, "🔎");

            var keywords = query.ToLowerInvariant()
                .Split(new[] { ' ', ',', '.', ':', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2)
                .ToList();

            var allFiles = SafeEnumerateFiles(searchPath).ToArray();
            var nameMatches = new List<(FileInfo File, string MatchType)>();
            var contentMatches = new List<(FileInfo File, string MatchType, string Snippet)>();

            // Phase 1: Name-based search (fast)
            foreach (var f in allFiles)
            {
                var nameL = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                if (keywords.Any(k => nameL.Contains(k)))
                    nameMatches.Add((new FileInfo(f), "📄 Name match"));
            }

            ReportProgress($"🔍 Found {nameMatches.Count} name matches, now searching file contents...", 40, "🔎");

            // Phase 2: Content search for text files (deeper)
            var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".txt", ".md", ".cs", ".py", ".js", ".ts", ".json", ".xml", ".html", ".css",
                ".yaml", ".yml", ".toml", ".ini", ".cfg", ".log", ".csv", ".sql", ".sh", ".bat",
                ".ps1", ".jsx", ".tsx", ".vue", ".svelte", ".java", ".cpp", ".c", ".h", ".go",
                ".rs", ".rb", ".php", ".swift", ".kt", ".r", ".env", ".gitignore", ".dockerfile"
            };
            var maxContentScanBytes = 100_000; // 100KB max per file
            var contentScanned = 0;

            foreach (var filePath in allFiles)
            {
                if (ct.IsCancellationRequested) break;
                if (contentScanned >= 200) break; // Limit content scan to 200 files
                var ext = Path.GetExtension(filePath);
                if (!textExtensions.Contains(ext)) continue;
                var fi = new FileInfo(filePath);
                if (fi.Length > maxContentScanBytes || fi.Length == 0) continue;

                try
                {
                    contentScanned++;
                    var content = await File.ReadAllTextAsync(filePath, ct);
                    var contentL = content.ToLowerInvariant();
                    var matchedKeyword = keywords.FirstOrDefault(k => contentL.Contains(k));
                    if (matchedKeyword != null && !nameMatches.Any(nm => nm.File.FullName == filePath))
                    {
                        // Extract snippet around match
                        var idx = contentL.IndexOf(matchedKeyword);
                        var start = Math.Max(0, idx - 40);
                        var len = Math.Min(100, content.Length - start);
                        var snippet = content.Substring(start, len).Replace("\n", " ").Replace("\r", "").Trim();
                        contentMatches.Add((fi, "📝 Content match", snippet));
                    }
                }
                catch { /* Skip unreadable files */ }
            }

            var totalResults = nameMatches.Count + contentMatches.Count;
            if (totalResults == 0)
                return new ActionResult { Success = true, Message = $"No files found matching '{query}' in {scope}" };

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"✅ Found {totalResults} results for '{query}':");

            if (nameMatches.Count > 0)
            {
                sb.AppendLine($"\n📄 **Name matches** ({nameMatches.Count}):");
                foreach (var (fi, _) in nameMatches.OrderByDescending(x => x.File.LastWriteTime).Take(15))
                    sb.AppendLine($"  📄 {fi.Name} ({fi.Length / 1024}KB) — {fi.LastWriteTime:MMM dd}");
            }

            if (contentMatches.Count > 0)
            {
                sb.AppendLine($"\n📝 **Content matches** ({contentMatches.Count}):");
                foreach (var (fi, _, snippet) in contentMatches.OrderByDescending(x => x.File.LastWriteTime).Take(10))
                    sb.AppendLine($"  📝 {fi.Name} — \"{snippet}...\"");
            }

            return new ActionResult { Success = true, Message = sb.ToString() };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Smart search error: {ex.Message}");
            return new ActionResult { Success = false, Message = $"Search failed: {ex.Message}" };
        }
    }

    /// <summary>
    /// PREMIUM: AI Bulk Rename - Intelligently rename multiple files based on content/pattern.
    /// </summary>
    private async Task<ActionResult> ExecuteAIBulkRenameAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("folderPath", out var folderPathObj))
            return new ActionResult { Success = false, Message = "Missing parameter: folderPath" };

        var rawPath = folderPathObj?.ToString() ?? string.Empty;
        var path = ResolvePath(rawPath);
        var pattern = parameters.TryGetValue("pattern", out var patternObj) ? patternObj?.ToString() ?? "smart" : "smart";

        if (!ValidatePath(rawPath))
            return new ActionResult { Success = false, Message = "Access denied: protected system path" };
        if (!Directory.Exists(path))
            return new ActionResult { Success = false, Message = $"Folder not found: {path}" };

        try
        {
            var files = Directory.GetFiles(path).OrderBy(f => f).ToList();
            if (files.Count == 0)
                return new ActionResult { Success = true, Message = "No files to rename" };
            if (files.Count > 50)
                return new ActionResult { Success = false, Message = $"Too many files ({files.Count}). AI bulk rename supports up to 50 files at a time for safety." };

            var renamedCount = 0;
            var folderName = Path.GetFileName(path);
            var renameLog = new System.Text.StringBuilder();

            // For "smart" or "descriptive" pattern, try to use AI
            if ((pattern.Equals("smart", StringComparison.OrdinalIgnoreCase) || 
                 pattern.Equals("descriptive", StringComparison.OrdinalIgnoreCase)) && _aiService != null)
            {
                ReportProgress($"🤖 AI analyzing {files.Count} files...", 10, "🧠");

                // Build file list for AI
                var fileList = new System.Text.StringBuilder();
                foreach (var file in files)
                {
                    var fi = new FileInfo(file);
                    fileList.AppendLine($"- {fi.Name} ({fi.Length / 1024}KB, modified {fi.LastWriteTime:yyyy-MM-dd})");
                }

                var aiPrompt = $"I have these files in a folder called '{folderName}':\n{fileList}\n" +
                    $"Suggest clean, descriptive file names for each. Keep the original extension. " +
                    $"Use lowercase_with_underscores. Be concise but descriptive. " +
                    $"Return ONLY a JSON array of objects with 'old' and 'new' keys, no explanation. Example: " +
                    $"[{{\"old\": \"IMG_001.jpg\", \"new\": \"sunset_beach.jpg\"}}]";

                try
                {
                    var aiResponse = await _aiService.GenerateTextAsync(aiPrompt, ct);
                    // Extract JSON array from response
                    var jsonStart = aiResponse.IndexOf('[');
                    var jsonEnd = aiResponse.LastIndexOf(']');
                    if (jsonStart >= 0 && jsonEnd > jsonStart)
                    {
                        var json = aiResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                        var renameMap = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json);
                        if (renameMap != null)
                        {
                            ReportProgress($"🤖 Renaming {renameMap.Count} files with AI suggestions...", 50, "📝");
                            foreach (var entry in renameMap)
                            {
                                if (!entry.TryGetValue("old", out var oldName) || !entry.TryGetValue("new", out var newName))
                                    continue;
                                var oldPath = Path.Combine(path, oldName);
                                var newPath = Path.Combine(path, newName);
                                if (File.Exists(oldPath) && !File.Exists(newPath))
                                {
                                    File.Move(oldPath, newPath);
                                    renameLog.AppendLine($"  {oldName} → {newName}");
                                    renamedCount++;
                                }
                            }
                            return new ActionResult
                            {
                                Success = true,
                                Message = $"🤖 AI renamed {renamedCount} files in {path}:\n\n{renameLog}"
                            };
                        }
                    }
                }
                catch (Exception aiEx)
                {
                    _logger.LogWarning($"AI rename failed, falling back to pattern: {aiEx.Message}");
                    // Fall through to pattern-based rename below
                }
            }

            // Pattern-based fallback (or explicit pattern request)
            ReportProgress($"📝 Renaming {files.Count} files with '{pattern}' pattern...", 50, "📝");
            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                var ext = Path.GetExtension(file);
                var dir = Path.GetDirectoryName(file) ?? "";

                var newName = pattern.ToLowerInvariant() switch
                {
                    "numbered" => $"{folderName}_{(i + 1):D3}{ext}",
                    "dated" => $"{folderName}_{DateTime.Now:yyyyMMdd}_{(i + 1):D2}{ext}",
                    _ => $"{folderName}_{(i + 1):D3}{ext}"
                };

                var newPath = Path.Combine(dir, newName);
                if (!File.Exists(newPath))
                {
                    var oldName = Path.GetFileName(file);
                    File.Move(file, newPath);
                    renameLog.AppendLine($"  {oldName} → {newName}");
                    renamedCount++;
                }
            }

            return new ActionResult
            {
                Success = true,
                Message = $"✅ Renamed {renamedCount} files using '{pattern}' pattern in {path}:\n\n{renameLog}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"AI bulk rename error: {ex.Message}");
            return new ActionResult { Success = false, Message = $"Bulk rename failed: {ex.Message}" };
        }
    }

    /// <summary>
    /// PREMIUM: Backup Suggestions - Analyze important files and recommend what to backup.
    /// </summary>
    private Task<ActionResult> ExecuteBackupSuggestionsAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        var scope = parameters.TryGetValue("scope", out var scopeObj) ? scopeObj?.ToString() ?? "Documents" : "Documents";

        try
        {
            var basePath = ResolvePath(scope);
            if (!Directory.Exists(basePath))
                return Task.FromResult(new ActionResult { Success = false, Message = $"Folder not found: {basePath}" });

            var importantExtensions = new[] { ".docx", ".xlsx", ".pdf", ".pptx", ".txt", ".jpg", ".png", ".mp4" };
            var files = SafeEnumerateFiles(basePath)
                .Select(f => new FileInfo(f))
                .Where(f => importantExtensions.Contains(f.Extension.ToLowerInvariant()))
                .OrderByDescending(f => f.LastWriteTime)
                .Take(100)
                .ToList();

            var recentFiles = files.Where(f => f.LastWriteTime > DateTime.Now.AddDays(-30)).ToList();
            var largeFiles = files.Where(f => f.Length > 10 * 1024 * 1024).ToList(); // > 10MB

            var suggestions = new System.Text.StringBuilder();
            suggestions.AppendLine($"📊 Backup Analysis for {scope}:\n");
            suggestions.AppendLine($"📁 Total important files: {files.Count}");
            suggestions.AppendLine($"🆕 Recently modified (last 30 days): {recentFiles.Count}");
            suggestions.AppendLine($"💾 Large files (>10MB): {largeFiles.Count}\n");
            suggestions.AppendLine("🎯 Suggested backup priorities:");
            suggestions.AppendLine("1. Recently modified documents (most likely active work)");
            suggestions.AppendLine("2. Large media files (photos/videos)");
            suggestions.AppendLine("3. PDF documents (contracts, receipts)\n");
            
            if (recentFiles.Count > 0)
            {
                suggestions.AppendLine("📄 Top recent files to backup:");
                foreach (var file in recentFiles.Take(10))
                {
                    suggestions.AppendLine($"  • {file.Name} ({file.Length / 1024 / 1024}MB) — {file.LastWriteTime:MMM dd}");
                }
            }

            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = suggestions.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Backup suggestions error: {ex.Message}");
            return Task.FromResult(new ActionResult { Success = false, Message = $"Analysis failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// PREMIUM: Smart Cleanup Schedule - Analyze usage patterns and suggest cleanup routines.
    /// </summary>
    private Task<ActionResult> ExecuteSmartCleanupScheduleAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        try
        {
            var analyzeTarget = parameters.TryGetValue("analyze", out var analyzeObj)
                ? analyzeObj?.ToString()?.ToLowerInvariant() ?? "all"
                : "all";

            var recommendations = new System.Text.StringBuilder();
            recommendations.AppendLine("🧹 Smart Cleanup Analysis:\n");

            var shouldAnalyzeTemp = analyzeTarget is "all" or "temp";
            var shouldAnalyzeDownloads = analyzeTarget is "all" or "downloads";
            var shouldAnalyzeDesktop = analyzeTarget is "all" or "desktop";
            var shouldAnalyzeRecycle = analyzeTarget is "all" or "recycle";
            var isPaths = !shouldAnalyzeTemp && !shouldAnalyzeDownloads && !shouldAnalyzeDesktop && !shouldAnalyzeRecycle;

            if (isPaths)
            {
                // Custom folder analysis
                var customPath = ResolvePath(analyzeTarget);
                if (Directory.Exists(customPath))
                {
                    var size = GetFolderSize(customPath);
                    var fileCount = SafeEnumerateFiles(customPath).Count();
                    var oldFiles = SafeEnumerateFiles(customPath)
                        .Select(f => new FileInfo(f))
                        .Where(f => f.LastWriteTime < DateTime.Now.AddDays(-90))
                        .OrderBy(f => f.LastWriteTime)
                        .Take(10)
                        .ToList();
                    recommendations.AppendLine($"📁 {customPath}");
                    recommendations.AppendLine($"   Size: {size / 1024 / 1024}MB | Files: {fileCount}");
                    if (oldFiles.Count > 0)
                    {
                        recommendations.AppendLine($"\n⏳ Old files (>90 days):");
                        foreach (var f in oldFiles)
                            recommendations.AppendLine($"   {f.Name} — {f.LastWriteTime:MMM dd, yyyy} ({f.Length / 1024}KB)");
                    }
                }
                else
                {
                    recommendations.AppendLine($"Folder not found: {customPath}");
                }
            }
            else
            {
                if (shouldAnalyzeTemp)
                {
                    var tempSize = GetFolderSize(Path.GetTempPath());
                    var tempFiles = Directory.GetFiles(Path.GetTempPath()).Length;
                    recommendations.AppendLine($"🗑️ Temp: {tempSize / 1024 / 1024}MB ({tempFiles} files)");
                    if (tempSize > 500L * 1024 * 1024)
                        recommendations.AppendLine($"  ⚠️ Over 500MB — recommend weekly cleanup");
                    else
                        recommendations.AppendLine($"  ✅ Within healthy range");
                }

                if (shouldAnalyzeDownloads)
                {
                    var downloadsPath = ResolvePath("Downloads");
                    var downloadsSize = GetFolderSize(downloadsPath);
                    var dlOld = Directory.Exists(downloadsPath)
                        ? Directory.GetFiles(downloadsPath)
                            .Select(f => new FileInfo(f))
                            .Where(f => f.LastWriteTime < DateTime.Now.AddDays(-30))
                            .Sum(f => f.Length)
                        : 0;
                    recommendations.AppendLine($"\n📥 Downloads: {downloadsSize / 1024 / 1024}MB total");
                    if (dlOld > 0)
                        recommendations.AppendLine($"  ⚠️ {dlOld / 1024 / 1024}MB older than 30 days");
                    if (downloadsSize > 5L * 1024 * 1024 * 1024)
                        recommendations.AppendLine($"  ⚠️ Over 5GB — organize or archive old files");
                }

                if (shouldAnalyzeDesktop)
                {
                    var desktopPath = ResolvePath("Desktop");
                    var desktopFileCount = Directory.Exists(desktopPath) ? Directory.GetFiles(desktopPath).Length : 0;
                    recommendations.AppendLine($"\n🖥️ Desktop: {desktopFileCount} files");
                    if (desktopFileCount > 20)
                        recommendations.AppendLine($"  ⚠️ Cluttered — consider organizing into folders");
                    else
                        recommendations.AppendLine($"  ✅ Tidy");
                }
            }

            recommendations.AppendLine("\n💡 Suggested Schedule:");
            recommendations.AppendLine("• Weekly: Clean temp files");
            recommendations.AppendLine("• Bi-weekly: Organize Downloads");
            recommendations.AppendLine("• Monthly: Desktop cleanup & backup");
            recommendations.AppendLine("• Quarterly: Full disk analysis & duplicate detection");

            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = recommendations.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Smart cleanup schedule error: {ex.Message}");
            return Task.FromResult(new ActionResult { Success = false, Message = $"Analysis failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// PREMIUM: Visual Analytics - Generate visual breakdown of folder sizes.
    /// </summary>
    private Task<ActionResult> ExecuteVisualAnalyticsAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("folderPath", out var folderPathObj))
            return Task.FromResult(new ActionResult { Success = false, Message = "Missing parameter: folderPath" });

        var rawPath = folderPathObj?.ToString() ?? string.Empty;
        var path = ResolvePath(rawPath);
        var depth = parameters.TryGetValue("depth", out var depthObj) && int.TryParse(depthObj?.ToString(), out var d) ? d : 1;

        if (!ValidatePath(rawPath))
            return Task.FromResult(new ActionResult { Success = false, Message = "Access denied: protected system path" });
        if (!Directory.Exists(path))
            return Task.FromResult(new ActionResult { Success = false, Message = $"Folder not found: {path}" });

        try
        {
            var subdirs = SafeEnumerateDirectories(path, "*", depth > 1)
                .Select(d => new DirectoryInfo(d))
                .Where(di => depth > 1 || di.Parent?.FullName.Equals(path, StringComparison.OrdinalIgnoreCase) == true)
                .Select(di => new
                {
                    Name = Path.GetRelativePath(path, di.FullName).Replace('\\', '/'),
                    Size = GetFolderSize(di.FullName),
                    FileCount = SafeEnumerateFiles(di.FullName).Count()
                })
                .OrderByDescending(x => x.Size)
                .Take(20)
                .ToList();

            if (subdirs.Count == 0)
                return Task.FromResult(new ActionResult { Success = true, Message = "No subfolders to analyze" });

            var totalSize = subdirs.Sum(x => x.Size);
            var report = new System.Text.StringBuilder();
            report.AppendLine($"📊 Folder Analytics: {Path.GetFileName(path)}\n");
            report.AppendLine($"Total analyzed: {totalSize / 1024 / 1024}MB across {subdirs.Count} folders\n");

            foreach (var dir in subdirs)
            {
                var percent = totalSize > 0 ? (dir.Size * 100.0 / totalSize) : 0;
                var bar = new string('█', (int)(percent / 5));
                report.AppendLine($"{bar.PadRight(20)} {percent:F1}% {dir.Name} ({dir.Size / 1024 / 1024}MB, {dir.FileCount} files)");
            }

            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = report.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Visual analytics error: {ex.Message}");
            return Task.FromResult(new ActionResult { Success = false, Message = $"Analytics failed: {ex.Message}" });
        }
    }

    private long GetFolderSize(string path)
    {
        try
        {
            return Directory.Exists(path)
                ? SafeEnumerateFiles(path).Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } })
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  FEATURE BATCH 1: Smart File Creation, Edit, Search, Command, System Info
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates any file type with the correct extension (.py, .js, .html, .java, .cpp, .md, .json, etc.)
    /// The AI generates the actual code/content and we save it with the right extension.
    /// </summary>
    private async Task<ActionResult> ExecuteCreateFileAsync(Dictionary<string, object> parameters, string aiContent, CancellationToken ct)
    {
        var fileName = parameters.TryGetValue("fileName", out var fn) ? fn?.ToString() ?? "" : "";
        var content = parameters.TryGetValue("content", out var c) ? c?.ToString() ?? "" : "";
        var language = parameters.TryGetValue("language", out var lang) ? lang?.ToString()?.ToLowerInvariant() ?? "" : "";
        var savePath = parameters.TryGetValue("savePath", out var sp) ? sp?.ToString() ?? "Desktop" : "Desktop";

        // SAFETY: Only use AI message as fallback if it actually looks like code.
        // Never write the friendly chat message as file content.
        if (string.IsNullOrWhiteSpace(content))
        {
            // Check if aiContent looks like actual code (contains typical code indicators)
            var looksLikeCode = !string.IsNullOrWhiteSpace(aiContent) &&
                (aiContent.Contains('{') || aiContent.Contains("import ") || aiContent.Contains("def ") ||
                 aiContent.Contains("class ") || aiContent.Contains("#include") || aiContent.Contains("function ") ||
                 aiContent.Contains("using ") || aiContent.Contains("package ") || aiContent.Contains("<!DOCTYPE") ||
                 aiContent.Contains("<html"));
            if (looksLikeCode)
                content = aiContent;
            else
                return new ActionResult { Success = false, Message = "No code content was generated. Please try again with a more specific request." };
        }

        // Auto-detect extension from language if fileName doesn't have one
        if (string.IsNullOrWhiteSpace(fileName))
        {
            var ext = GetExtensionForLanguage(language);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            fileName = $"{(string.IsNullOrWhiteSpace(language) ? "file" : language)}_{timestamp}{ext}";
        }
        else if (!Path.HasExtension(fileName))
        {
            fileName += GetExtensionForLanguage(language);
        }

        try
        {
            var resolvedFolder = ResolvePath(savePath);
            if (string.IsNullOrWhiteSpace(resolvedFolder))
                resolvedFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            if (!Directory.Exists(resolvedFolder))
                Directory.CreateDirectory(resolvedFolder);

            var filePath = Path.Combine(resolvedFolder, SanitizeFileName(fileName));
            await File.WriteAllTextAsync(filePath, content, ct);

            // Auto-open in default editor
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch { /* ignore if no default app */ }

            _logger.LogInformation($"Created file: {filePath}");

            // Build a result that shows the code preview in chat
            var langTag = string.IsNullOrWhiteSpace(language) ? "" : language;
            var codePreview = content.Length > 3000 ? content[..3000] + "\n// ... (truncated)" : content;
            var message = $"✅ **File created:** `{fileName}`\n📂 **Location:** `{resolvedFolder}`\n📄 Opened in default editor.\n\n**Code:**\n```{langTag}\n{codePreview}\n```";

            return new ActionResult
            {
                Success = true,
                Message = message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Create file error: {ex.Message}");
            return new ActionResult { Success = false, Message = $"Failed to create file: {ex.Message}" };
        }
    }

    /// <summary>
    /// Edit an existing file — supports append, replace, or overwrite operations.
    /// </summary>
    private async Task<ActionResult> ExecuteEditFileAsync(Dictionary<string, object> parameters, string aiContent, CancellationToken ct)
    {
        var filePath = parameters.TryGetValue("filePath", out var fp) ? fp?.ToString() ?? "" : "";
        var newContent = parameters.TryGetValue("content", out var c) ? c?.ToString() ?? "" : "";
        var mode = parameters.TryGetValue("mode", out var m) ? m?.ToString()?.ToLowerInvariant() ?? "overwrite" : "overwrite";
        var findText = parameters.TryGetValue("find", out var ft) ? ft?.ToString() ?? "" : "";
        var replaceText = parameters.TryGetValue("replace", out var rt) ? rt?.ToString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(newContent)) newContent = aiContent;
        if (string.IsNullOrWhiteSpace(filePath))
            return new ActionResult { Success = false, Message = "Please specify which file to edit." };

        filePath = ResolvePath(filePath);

        // Try Desktop if not found
        if (!File.Exists(filePath))
        {
            var desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Path.GetFileName(filePath));
            if (File.Exists(desktopPath)) filePath = desktopPath;
            else return new ActionResult { Success = false, Message = $"File not found: {filePath}" };
        }

        try
        {
            var existingContent = await File.ReadAllTextAsync(filePath, ct);
            string finalContent;

            switch (mode)
            {
                case "append":
                    finalContent = existingContent + "\n" + newContent;
                    break;
                case "prepend":
                    finalContent = newContent + "\n" + existingContent;
                    break;
                case "replace" when !string.IsNullOrWhiteSpace(findText):
                    finalContent = existingContent.Replace(findText, replaceText);
                    break;
                case "overwrite":
                default:
                    finalContent = newContent;
                    break;
            }

            await File.WriteAllTextAsync(filePath, finalContent, ct);
            _logger.LogInformation($"Edited file: {filePath} (mode={mode})");
            return new ActionResult
            {
                Success = true,
                Message = $"✅ File edited: {Path.GetFileName(filePath)} ({mode} mode)\n📂 {Path.GetDirectoryName(filePath)}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Edit file error: {ex.Message}");
            return new ActionResult { Success = false, Message = $"Failed to edit file: {ex.Message}" };
        }
    }

    /// <summary>
    /// Search the web — opens a browser search for the query.
    /// </summary>
    private async Task<ActionResult> ExecuteSearchWebAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        var query = parameters.TryGetValue("query", out var q) ? q?.ToString() ?? "" : "";
        var engine = parameters.TryGetValue("engine", out var e) ? e?.ToString()?.ToLowerInvariant() ?? "google" : "google";

        if (string.IsNullOrWhiteSpace(query))
            return new ActionResult { Success = false, Message = "Please specify what to search for." };

        var encoded = Uri.EscapeDataString(query);
        var url = engine switch
        {
            "bing" => $"https://www.bing.com/search?q={encoded}",
            "duckduckgo" or "ddg" => $"https://duckduckgo.com/?q={encoded}",
            "youtube" => $"https://www.youtube.com/results?search_query={encoded}",
            "github" => $"https://github.com/search?q={encoded}",
            "stackoverflow" or "so" => $"https://stackoverflow.com/search?q={encoded}",
            "reddit" => $"https://www.reddit.com/search/?q={encoded}",
            "amazon" => $"https://www.amazon.com/s?k={encoded}",
            "pypi" => $"https://pypi.org/search/?q={encoded}",
            "npm" => $"https://www.npmjs.com/search?q={encoded}",
            "nuget" => $"https://www.nuget.org/packages?q={encoded}",
            _ => $"https://www.google.com/search?q={encoded}"
        };

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return new ActionResult { Success = true, Message = $"✅ Searching {engine} for: {query}" };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Search web error: {ex.Message}");
            return new ActionResult { Success = false, Message = $"Search failed: {ex.Message}" };
        }
    }

    /// <summary>
    /// Run a terminal command and return the output. Blocked for dangerous commands.
    /// </summary>
    private async Task<ActionResult> ExecuteRunCommandAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        var command = parameters.TryGetValue("command", out var cmd) ? cmd?.ToString() ?? "" : "";
        var shell = parameters.TryGetValue("shell", out var sh) ? sh?.ToString()?.ToLowerInvariant() ?? "powershell" : "powershell";

        if (string.IsNullOrWhiteSpace(command))
            return new ActionResult { Success = false, Message = "No command specified." };

        // Safety: block dangerous commands
        var blocked = new[] { "format", "del /s", "rd /s", "rmdir /s", "rm -rf", "shutdown", "restart",
            "reg delete", "net user", "net localgroup", ":(){", "fork bomb", "taskkill /f /im explorer",
            "Remove-Item -Recurse -Force C:", "Remove-Item -Recurse -Force /", "Stop-Computer", "Restart-Computer" };
        var cmdLower = command.ToLowerInvariant();
        if (blocked.Any(b => cmdLower.Contains(b.ToLowerInvariant())))
            return new ActionResult { Success = false, Message = "⚠️ This command was blocked for safety reasons." };

        try
        {
            var isCmd = shell == "cmd";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = isCmd ? "cmd.exe" : "powershell.exe",
                Arguments = isCmd ? $"/c {command}" : $"-NoProfile -Command \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
                return new ActionResult { Success = false, Message = "Failed to start process." };

            // Timeout after 30 seconds
            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);
            var completed = process.WaitForExit(30000);

            if (!completed)
            {
                process.Kill();
                return new ActionResult { Success = false, Message = "⚠️ Command timed out after 30 seconds." };
            }

            var output = await outputTask;
            var error = await errorTask;

            // Truncate very long outputs
            if (output.Length > 3000) output = output[..3000] + "\n... (output truncated)";
            if (error.Length > 1000) error = error[..1000] + "\n... (error truncated)";

            if (process.ExitCode == 0)
            {
                return new ActionResult
                {
                    Success = true,
                    Message = $"✅ Command executed successfully:\n```\n{(string.IsNullOrWhiteSpace(output) ? "(no output)" : output.Trim())}\n```"
                };
            }
            else
            {
                return new ActionResult
                {
                    Success = false,
                    Message = $"❌ Command failed (exit code {process.ExitCode}):\n{error.Trim()}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Run command error: {ex.Message}");
            return new ActionResult { Success = false, Message = $"Command failed: {ex.Message}" };
        }
    }

    /// <summary>
    /// Get system information — RAM, CPU, running processes, disk, battery.
    /// </summary>
    private async Task<ActionResult> ExecuteSystemInfoAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        var infoType = parameters.TryGetValue("type", out var t) ? t?.ToString()?.ToLowerInvariant() ?? "overview" : "overview";

        try
        {
            var report = new System.Text.StringBuilder();

            switch (infoType)
            {
                case "processes":
                case "running":
                    var procs = System.Diagnostics.Process.GetProcesses()
                        .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0; } })
                        .Take(20)
                        .ToList();

                    report.AppendLine("📋 Top 20 Running Processes (by memory):\n");
                    report.AppendLine($"{"Name",-30} {"Memory (MB)",12} {"PID",8}");
                    report.AppendLine(new string('─', 52));
                    foreach (var p in procs)
                    {
                        try
                        {
                            var memMb = p.WorkingSet64 / 1024.0 / 1024.0;
                            report.AppendLine($"{p.ProcessName,-30} {memMb,10:F1}MB {p.Id,8}");
                        }
                        catch { }
                    }
                    break;

                case "memory":
                case "ram":
                    var gcInfo = GC.GetGCMemoryInfo();
                    var totalMemMb = gcInfo.TotalAvailableMemoryBytes / 1024.0 / 1024.0 / 1024.0;

                    // Use PowerShell to get real system memory
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = "-NoProfile -Command \"$os = Get-CimInstance Win32_OperatingSystem; [math]::Round($os.TotalVisibleMemorySize/1MB,1).ToString() + '|' + [math]::Round(($os.TotalVisibleMemorySize - $os.FreePhysicalMemory)/1MB,1).ToString() + '|' + [math]::Round($os.FreePhysicalMemory/1MB,1).ToString()\"",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = System.Diagnostics.Process.Start(psi);
                        var memOutput = await proc!.StandardOutput.ReadToEndAsync(ct);
                        proc.WaitForExit(10000);
                        var parts = memOutput.Trim().Split('|');
                        if (parts.Length == 3)
                        {
                            report.AppendLine("💾 Memory Usage:\n");
                            report.AppendLine($"  Total RAM:     {parts[0]} GB");
                            report.AppendLine($"  Used:          {parts[1]} GB");
                            report.AppendLine($"  Free:          {parts[2]} GB");
                        }
                    }
                    catch
                    {
                        report.AppendLine($"💾 Available system memory: ~{totalMemMb:F1} GB");
                    }
                    break;

                default: // overview
                    report.AppendLine("🖥️ System Overview:\n");
                    report.AppendLine($"  Machine:       {Environment.MachineName}");
                    report.AppendLine($"  User:          {Environment.UserName}");
                    report.AppendLine($"  OS:            {Environment.OSVersion}");
                    report.AppendLine($"  Processors:    {Environment.ProcessorCount} cores");
                    report.AppendLine($"  .NET Version:  {Environment.Version}");

                    // GPU info via WMI
                    try
                    {
                        var gpuPsi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = "-NoProfile -Command \"Get-CimInstance Win32_VideoController | ForEach-Object { $_.Name + '|' + [math]::Round($_.AdapterRAM/1GB,1) }\"",
                            RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
                        };
                        using var gpuProc = System.Diagnostics.Process.Start(gpuPsi);
                        var gpuOutput = await gpuProc!.StandardOutput.ReadToEndAsync(ct);
                        gpuProc.WaitForExit(5000);
                        var gpuLines = gpuOutput.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        if (gpuLines.Length > 0)
                        {
                            report.AppendLine("\n🎮 GPU:");
                            foreach (var line in gpuLines)
                            {
                                var gParts = line.Trim().Split('|');
                                report.AppendLine(gParts.Length >= 2
                                    ? $"  {gParts[0]} ({gParts[1]}GB VRAM)"
                                    : $"  {gParts[0]}");
                            }
                        }
                    }
                    catch { /* GPU info optional */ }

                    // RAM summary
                    try
                    {
                        var ramPsi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = "-NoProfile -Command \"$os = Get-CimInstance Win32_OperatingSystem; [math]::Round($os.TotalVisibleMemorySize/1MB,1).ToString() + '|' + [math]::Round(($os.TotalVisibleMemorySize - $os.FreePhysicalMemory)/1MB,1).ToString()\"",
                            RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
                        };
                        using var ramProc = System.Diagnostics.Process.Start(ramPsi);
                        var ramOutput = await ramProc!.StandardOutput.ReadToEndAsync(ct);
                        ramProc.WaitForExit(5000);
                        var ramParts = ramOutput.Trim().Split('|');
                        if (ramParts.Length == 2)
                            report.AppendLine($"\n💾 RAM: {ramParts[1]}GB used / {ramParts[0]}GB total");
                    }
                    catch { /* RAM info optional */ }

                    // Disk info
                    report.AppendLine("\n💿 Drives:");
                    foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                    {
                        var usedPct = 100.0 * (drive.TotalSize - drive.TotalFreeSpace) / drive.TotalSize;
                        report.AppendLine($"  {drive.Name} {drive.DriveFormat}  {drive.TotalFreeSpace / 1024 / 1024 / 1024}GB free / {drive.TotalSize / 1024 / 1024 / 1024}GB ({usedPct:F0}% used)");
                    }

                    // Battery
                    try
                    {
                        var batPsi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = "-NoProfile -Command \"$b = Get-CimInstance Win32_Battery; if($b) { $b.EstimatedChargeRemaining.ToString() + '|' + $b.BatteryStatus.ToString() } else { 'none' }\"",
                            RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
                        };
                        using var batProc = System.Diagnostics.Process.Start(batPsi);
                        var batOutput = await batProc!.StandardOutput.ReadToEndAsync(ct);
                        batProc.WaitForExit(5000);
                        if (!batOutput.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
                        {
                            var bParts = batOutput.Trim().Split('|');
                            var statusText = bParts.Length > 1 && bParts[1] == "2" ? "🔌 Charging" : "🔋 On Battery";
                            report.AppendLine($"\n🔋 Battery: {bParts[0]}% ({statusText})");
                        }
                    }
                    catch { /* Battery info optional */ }

                    // Process count
                    report.AppendLine($"\n📋 Running processes: {System.Diagnostics.Process.GetProcesses().Length}");

                    // Uptime
                    var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                    report.AppendLine($"⏱️ Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m");
                    break;
            }

            return new ActionResult { Success = true, Message = report.ToString() };
        }
        catch (Exception ex)
        {
            _logger.LogError($"System info error: {ex.Message}");
            return new ActionResult { Success = false, Message = $"Failed to get system info: {ex.Message}" };
        }
    }

    // ─── SET REMINDER ───────────────────────────────────────────────
    private async Task<ActionResult> ExecuteSetReminderAsync(Dictionary<string, object>? p, CancellationToken ct)
    {
        var message = p?.GetValueOrDefault("message")?.ToString() ?? "Reminder!";
        var minutesStr = p?.GetValueOrDefault("minutes")?.ToString() ?? "5";

        if (!double.TryParse(minutesStr, out var minutes) || minutes <= 0)
            return new ActionResult { Success = false, Message = "Invalid reminder time. Please specify a positive number of minutes." };

        if (minutes > 1440) // max 24 hours
            return new ActionResult { Success = false, Message = "Reminders are limited to 24 hours (1440 minutes)." };

        var dueTime = DateTime.Now.AddMinutes(minutes);

        // Fire-and-forget timer on the UI thread
        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(minutes)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                // Show toast notification via NotificationService
                if (_notificationService != null)
                {
                    _notificationService.ShowInfo($"⏰ {message}", "ZayFlow Reminder");
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $"⏰ ZayFlow Reminder\n\n{message}",
                        "ZayFlow Reminder",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            };
            timer.Start();
        });

        var timeLabel = minutes >= 60
            ? $"{minutes / 60:F0}h {minutes % 60:F0}m"
            : $"{minutes} minute{(minutes != 1 ? "s" : "")}";

        return new ActionResult
        {
            Success = true,
            Message = $"✅ Reminder set! I'll remind you in {timeLabel} (at {dueTime:hh:mm tt}).\n\n📝 \"{message}\""
        };
    }

    // ─── COMPRESS FILES (with progress) ────────────────────────────
    private async Task<ActionResult> ExecuteCompressFilesAsync(Dictionary<string, object>? p, CancellationToken ct)
    {
        var sourcePath = ResolvePath(p?.GetValueOrDefault("sourcePath")?.ToString() ?? "");
        var destName = p?.GetValueOrDefault("archiveName")?.ToString() ?? "";
        var mode = p?.GetValueOrDefault("mode")?.ToString()?.ToLowerInvariant() ?? "compress";

        if (string.IsNullOrWhiteSpace(sourcePath))
            return new ActionResult { Success = false, Message = "No source path provided." };

        if (!Directory.Exists(sourcePath) && !File.Exists(sourcePath))
            return new ActionResult { Success = false, Message = $"Source path not found: {sourcePath}" };

        try
        {
            if (mode == "extract")
            {
                if (!File.Exists(sourcePath) || !sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    return new ActionResult { Success = false, Message = "Source must be a .zip file for extraction." };

                ReportProgress("📂 Preparing to extract...", 0, "📦");

                var extractDir = string.IsNullOrWhiteSpace(destName)
                    ? Path.Combine(Path.GetDirectoryName(sourcePath)!, Path.GetFileNameWithoutExtension(sourcePath))
                    : ResolvePath(destName);

                Directory.CreateDirectory(extractDir);

                await Task.Run(() =>
                {
                    using var archive = ZipFile.OpenRead(sourcePath);
                    var total = archive.Entries.Count;
                    var done = 0;
                    foreach (var entry in archive.Entries)
                    {
                        ct.ThrowIfCancellationRequested();
                        var destFile = Path.Combine(extractDir, entry.FullName);
                        var destDir2 = Path.GetDirectoryName(destFile)!;
                        Directory.CreateDirectory(destDir2);
                        if (!string.IsNullOrEmpty(entry.Name))
                            entry.ExtractToFile(destFile, overwrite: true);
                        done++;
                        var pct = total > 0 ? (double)done / total * 100 : 0;
                        ReportProgress($"📂 Extracting... ({done}/{total} files)", pct, "📦");
                    }
                }, ct);

                var count = SafeEnumerateFiles(extractDir).Count();
                return new ActionResult
                {
                    Success = true,
                    Message = $"✅ Extracted {count} files to:\n📁 {extractDir}"
                };
            }
            else
            {
                // --- Compress ---
                if (string.IsNullOrWhiteSpace(destName))
                {
                    var baseName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    destName = baseName + ".zip";
                }
                if (!destName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    destName += ".zip";

                var parentDir = Directory.Exists(sourcePath)
                    ? Path.GetDirectoryName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))!
                    : Path.GetDirectoryName(sourcePath)!;
                var destPath = Path.Combine(parentDir, destName);

                if (File.Exists(destPath)) File.Delete(destPath);

                if (Directory.Exists(sourcePath))
                {
                    var files = SafeEnumerateFiles(sourcePath).ToArray();
                    var totalFiles = files.Length;
                    ReportProgress($"📦 Compressing {totalFiles} files...", 0, "📦");

                    await Task.Run(() =>
                    {
                        using var archive = ZipFile.Open(destPath, ZipArchiveMode.Create);
                        var done = 0;
                        var baseDir = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        var baseFolderName = Path.GetFileName(baseDir);

                        foreach (var file in files)
                        {
                            ct.ThrowIfCancellationRequested();
                            var relativePath = Path.Combine(baseFolderName, Path.GetRelativePath(baseDir, file));
                            archive.CreateEntryFromFile(file, relativePath, CompressionLevel.Fastest);
                            done++;
                            var pct = totalFiles > 0 ? (double)done / totalFiles * 100 : 0;
                            ReportProgress($"📦 Compressing... ({done}/{totalFiles} files)", pct, "📦");
                        }
                    }, ct);
                }
                else if (File.Exists(sourcePath))
                {
                    ReportProgress("📦 Compressing file...", 50, "📦");
                    await Task.Run(() =>
                    {
                        using var zip = ZipFile.Open(destPath, ZipArchiveMode.Create);
                        zip.CreateEntryFromFile(sourcePath, Path.GetFileName(sourcePath), CompressionLevel.Fastest);
                    }, ct);
                }

                if (!File.Exists(destPath))
                    return new ActionResult { Success = false, Message = "Zip file was not created. The source may be empty." };

                var size = new FileInfo(destPath).Length;
                if (size == 0)
                    return new ActionResult { Success = false, Message = "Zip file created but is 0 KB — source folder may be empty or access denied." };

                var sizeStr = size < 1024 ? $"{size} B"
                    : size < 1024 * 1024 ? $"{size / 1024.0:F1} KB"
                    : size < 1024L * 1024 * 1024 ? $"{size / (1024.0 * 1024):F1} MB"
                    : $"{size / (1024.0 * 1024 * 1024):F2} GB";

                return new ActionResult
                {
                    Success = true,
                    Message = $"✅ Compressed successfully!\n📦 {destPath}\n📊 Size: {sizeStr}"
                };
            }
        }
        catch (UnauthorizedAccessException)
        {
            return new ActionResult { Success = false, Message = $"Access denied to: {sourcePath}. Try running as administrator or choose a different folder." };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Compression error: {ex.Message}" };
        }
    }

    // ─── CLIPBOARD ACTION ───────────────────────────────────────────
    private async Task<ActionResult> ExecuteClipboardActionAsync(Dictionary<string, object>? p, CancellationToken ct)
    {
        var mode = p?.GetValueOrDefault("mode")?.ToString()?.ToLowerInvariant() ?? "read";
        var content = p?.GetValueOrDefault("content")?.ToString() ?? "";

        try
        {
            if (mode == "write" || mode == "copy")
            {
                if (string.IsNullOrWhiteSpace(content))
                    return new ActionResult { Success = false, Message = "No content to copy to clipboard." };

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    System.Windows.Clipboard.SetText(content);
                });

                var preview = content.Length > 100 ? content[..100] + "..." : content;
                return new ActionResult
                {
                    Success = true,
                    Message = $"✅ Copied to clipboard!\n📋 \"{preview}\""
                };
            }
            else // read
            {
                string? clipText = null;
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (System.Windows.Clipboard.ContainsText())
                        clipText = System.Windows.Clipboard.GetText();
                });

                if (string.IsNullOrWhiteSpace(clipText))
                    return new ActionResult { Success = true, Message = "📋 Clipboard is empty or doesn't contain text." };

                var preview = clipText.Length > 500 ? clipText[..500] + $"\n... ({clipText.Length} chars total)" : clipText;
                return new ActionResult
                {
                    Success = true,
                    Message = $"📋 **Clipboard content:**\n\n{preview}"
                };
            }
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Clipboard error: {ex.Message}" };
        }
    }

    // ─── TRANSLATE TEXT (AI-powered, in-chat) ────────────────────────
    private async Task<ActionResult> ExecuteTranslateTextAsync(Dictionary<string, object>? p, CancellationToken ct)
    {
        var text = p?.GetValueOrDefault("text")?.ToString() ?? "";
        var from = p?.GetValueOrDefault("from")?.ToString() ?? "auto";
        var to = p?.GetValueOrDefault("to")?.ToString() ?? "english";

        if (string.IsNullOrWhiteSpace(text))
            return new ActionResult { Success = false, Message = "No text provided to translate." };

        if (_aiService == null)
            return new ActionResult { Success = false, Message = "Translation service not available." };

        try
        {
            var fromLabel = from == "auto" ? "the original language" : from;
            var prompt = $@"Translate the following text from {fromLabel} to {to}.
Rules:
- Return ONLY the translated text. No explanations, no extra words.
- Preserve formatting, line breaks, and punctuation.
- If the text is already in {to}, return it unchanged.
- For technical terms, keep them as-is if there's no good translation.

Text to translate:
{text}";

            var translated = await _aiService.GenerateTextAsync(prompt, ct);

            if (string.IsNullOrWhiteSpace(translated))
                return new ActionResult { Success = false, Message = "Translation returned empty. Please try again." };

            // Clean up any AI prefixes that might sneak in
            translated = translated.Trim();
            if (translated.StartsWith('"') && translated.EndsWith('"'))
                translated = translated[1..^1];

            var preview = text.Length > 100 ? text[..100] + "..." : text;
            return new ActionResult
            {
                Success = true,
                Message = $"🌐 **Translation** ({fromLabel} → {to})\n\n📝 **Original:**\n{preview}\n\n✅ **Translated:**\n{translated}"
            };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Translation error: {ex.Message}" };
        }
    }

    // ─── DOWNLOAD FILE (smart search + progress) ──────────────────
    private async Task<ActionResult> ExecuteDownloadFileAsync(Dictionary<string, object>? p, CancellationToken ct)
    {
        var url = p?.GetValueOrDefault("url")?.ToString()?.Trim() ?? "";
        var savePath = p?.GetValueOrDefault("savePath")?.ToString() ?? "Downloads";
        var fileName = p?.GetValueOrDefault("fileName")?.ToString() ?? "";
        var searchTerm = p?.GetValueOrDefault("searchTerm")?.ToString()?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(url))
            return new ActionResult { Success = false, Message = "No URL provided." };

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return new ActionResult { Success = false, Message = "Invalid URL. Must start with http:// or https://" };

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            ReportProgress("🔍 Checking URL...", -1, "🌐");

            // HEAD request to check content type
            using var headReq = new HttpRequestMessage(HttpMethod.Head, uri);
            HttpResponseMessage? headResp = null;
            try { headResp = await httpClient.SendAsync(headReq, ct); } catch { /* fallback to GET */ }

            var contentType = headResp?.Content?.Headers?.ContentType?.MediaType ?? "";
            var isDirectFile = IsDirectFileUrl(uri, contentType);

            if (isDirectFile)
            {
                return await DownloadDirectFileAsync(httpClient, uri, savePath, fileName, ct);
            }
            else
            {
                return await ScanAndDownloadFromPageAsync(httpClient, uri, savePath, fileName, searchTerm, ct);
            }
        }
        catch (TaskCanceledException)
        {
            return new ActionResult { Success = false, Message = "Download timed out (10 minute limit)." };
        }
        catch (HttpRequestException ex)
        {
            return new ActionResult { Success = false, Message = $"Download failed: {ex.Message}" };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Download error: {ex.Message}" };
        }
    }

    private static bool IsDirectFileUrl(Uri uri, string contentType)
    {
        var fileTypes = new[] { "application/octet-stream", "application/zip", "application/x-zip",
            "application/pdf", "application/x-msdownload", "application/x-msi",
            "application/x-7z-compressed", "application/x-rar-compressed", "application/gzip",
            "application/x-tar", "image/", "audio/", "video/", "application/x-bittorrent" };
        if (fileTypes.Any(t => contentType.StartsWith(t, StringComparison.OrdinalIgnoreCase)))
            return true;

        var path = uri.LocalPath.ToLowerInvariant();
        var downloadExts = new[] { ".exe", ".msi", ".zip", ".7z", ".rar", ".tar", ".gz", ".pdf",
            ".dmg", ".deb", ".rpm", ".appimage", ".apk", ".iso",
            ".mp3", ".mp4", ".avi", ".mkv", ".mov", ".flac", ".wav",
            ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".bmp",
            ".docx", ".xlsx", ".pptx", ".csv", ".json", ".xml",
            ".py", ".js", ".jar", ".war", ".whl", ".torrent" };
        return downloadExts.Any(e => path.EndsWith(e));
    }

    private async Task<ActionResult> DownloadDirectFileAsync(HttpClient httpClient, Uri uri, string savePath, string fileName, CancellationToken ct)
    {
        // Route through DownloadManager for tracking + pause/resume/cancel support
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName) || !fileName.Contains('.'))
                fileName = "downloaded_file";
        }
        fileName = SanitizeFileName(fileName);

        ReportProgress($"📥 Queuing download: {fileName}...", 0, "📥");

        var item = _downloadManager.StartDownload(uri.AbsoluteUri, savePath, fileName);

        // Wait for download to finish (or cancel via ct)
        while (item.IsActive || item.Status == DownloadStatus.Paused)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(500, ct);

            // Mirror progress to chat
            if (item.Status == DownloadStatus.Downloading)
            {
                var speedInfo = !string.IsNullOrWhiteSpace(item.SpeedText) ? $" • {item.SpeedText}" : "";
                ReportProgress($"📥 Downloading {fileName}... {item.ProgressText}{speedInfo}", item.ProgressPercent, "📥");
            }
            else if (item.Status == DownloadStatus.Paused)
            {
                ReportProgress($"⏸️ Download paused: {fileName} — resume from Downloads tab", item.ProgressPercent, "⏸️");
            }
        }

        return item.Status switch
        {
            DownloadStatus.Completed => new ActionResult
            {
                Success = true,
                Message = $"✅ Downloaded successfully!\n📥 {item.FullPath}\n📊 Size: {item.FileSizeText}\n\n💡 View in the **Downloads** tab for details."
            },
            DownloadStatus.Cancelled => new ActionResult
            {
                Success = false,
                Message = $"🚫 Download cancelled: {fileName}"
            },
            _ => new ActionResult
            {
                Success = false,
                Message = $"❌ Download failed: {item.ErrorMessage}"
            }
        };
    }

    private static string FormatBytes(long bytes)
    {
        return bytes < 1024 ? $"{bytes} B"
            : bytes < 1024 * 1024 ? $"{bytes / 1024.0:F1} KB"
            : bytes < 1024L * 1024 * 1024 ? $"{bytes / (1024.0 * 1024):F1} MB"
            : $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private async Task<ActionResult> ScanAndDownloadFromPageAsync(HttpClient httpClient, Uri pageUri, string savePath, string targetFileName, string searchTerm, CancellationToken ct)
    {
        ReportProgress($"🔍 Scanning {pageUri.Host} for content...", -1, "🔍");

        // Fetch page HTML
        string html;
        try
        {
            html = await httpClient.GetStringAsync(pageUri, ct);
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Could not load page: {ex.Message}" };
        }

        // Build search keywords from the searchTerm
        var keywords = new List<string>();
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            keywords.AddRange(searchTerm.ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2));
        }

        // Extract all links with their surrounding text context
        var linkWithTextPattern = new Regex(
            @"<a\s+[^>]*href\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var allMatches = linkWithTextPattern.Matches(html);

        var downloadExts = new[] { ".exe", ".msi", ".zip", ".7z", ".rar", ".tar", ".gz",
            ".pdf", ".dmg", ".deb", ".rpm", ".appimage", ".apk", ".iso",
            ".mp3", ".mp4", ".avi", ".mkv", ".mov", ".jar", ".whl", ".torrent" };

        var allLinks = new List<(string Url, string FileName, string Ext, string LinkText, int Relevance)>();

        ReportProgress($"🔍 Analyzing links on {pageUri.Host}...", 30, "🔍");

        foreach (Match m in allMatches)
        {
            var href = m.Groups[1].Value.Trim();
            var linkText = Regex.Replace(m.Groups[2].Value, "<[^>]+>", "").Trim(); // Strip inner HTML tags

            if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#') || href.StartsWith("javascript:"))
                continue;

            // Resolve relative URLs
            if (!Uri.TryCreate(href, UriKind.Absolute, out var linkUri))
            {
                if (Uri.TryCreate(pageUri, href, out linkUri)) { /* resolved */ }
                else continue;
            }

            var linkPath = linkUri.LocalPath.ToLowerInvariant();
            var ext = Path.GetExtension(linkPath);
            var fName = Path.GetFileName(linkUri.LocalPath);
            var isDownloadable = downloadExts.Contains(ext);

            // Also check if href contains "download" pattern
            var hrefLower = href.ToLowerInvariant();
            var isDownloadLink = hrefLower.Contains("download") || hrefLower.Contains("get-file") ||
                                 hrefLower.Contains("/dl/") || hrefLower.Contains("?file=");

            if (!isDownloadable && !isDownloadLink) continue;

            // Calculate relevance score based on search term match
            var relevance = 0;
            var combinedText = $"{fName} {linkText} {href}".ToLowerInvariant();

            if (keywords.Count > 0)
            {
                foreach (var kw in keywords)
                {
                    if (combinedText.Contains(kw)) relevance += 10;
                }
                // Bonus for matching multiple keywords
                if (relevance >= keywords.Count * 10) relevance += 50;
            }
            else
            {
                relevance = isDownloadable ? 5 : 1;
            }

            if (isDownloadable) relevance += 3;

            if (!string.IsNullOrWhiteSpace(fName) && fName.Contains('.'))
                allLinks.Add((linkUri.AbsoluteUri, fName, ext, linkText, relevance));
            else if (isDownloadLink)
                allLinks.Add((linkUri.AbsoluteUri, string.IsNullOrWhiteSpace(linkText) ? "download" : linkText, "", linkText, relevance));
        }

        // Remove duplicates and sort by relevance (highest first)
        allLinks = allLinks.DistinctBy(d => d.Url).OrderByDescending(d => d.Relevance).Take(20).ToList();

        ReportProgress($"🔍 Found {allLinks.Count} links, analyzing...", 60, "🔍");

        // If user specified a search term, filter to only relevant links
        if (keywords.Count > 0)
        {
            var relevant = allLinks.Where(l => l.Relevance >= 10).ToList();

            if (relevant.Count == 0)
            {
                // Nothing matched the search term
                var searchLabel = searchTerm;
                return new ActionResult
                {
                    Success = false,
                    Message = $"❌ **\"{searchLabel}\" not found on {pageUri.Host}**\n\n" +
                              $"🔍 I scanned the page but couldn't find any download matching \"{searchLabel}\".\n\n" +
                              (allLinks.Count > 0
                                  ? $"I found {allLinks.Count} other downloadable files, but none match your search.\nSay \"show me what's available on that site\" to see all links."
                                  : "The site doesn't seem to have any downloadable files.\nTry a different website or provide a direct download link.")
                };
            }

            allLinks = relevant;
        }

        if (allLinks.Count == 0)
        {
            return new ActionResult
            {
                Success = false,
                Message = $"🔍 No downloadable files found on {pageUri.Host}.\n\nTip: Try right-clicking the download button → Copy link address, then give me the direct link."
            };
        }

        // If 1 highly relevant match, auto-download it
        if (allLinks.Count == 1 || (allLinks.Count > 0 && allLinks[0].Relevance >= 50))
        {
            var best = allLinks[0];
            if (Uri.TryCreate(best.Url, UriKind.Absolute, out var dlUri))
            {
                ReportProgress($"📥 Found match! Downloading {best.FileName}...", 80, "📥");
                return await DownloadDirectFileAsync(httpClient, dlUri, savePath, best.FileName, ct);
            }
        }

        // Multiple links found — list them
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"🔍 Found {allLinks.Count} downloadable files on **{pageUri.Host}**:\n");
        for (int i = 0; i < Math.Min(allLinks.Count, 10); i++)
        {
            var d = allLinks[i];
            var textLabel = !string.IsNullOrWhiteSpace(d.LinkText) && d.LinkText != d.FileName
                ? $" — *{d.LinkText}*" : "";
            sb.AppendLine($"{i + 1}. **{d.FileName}**{textLabel}");
        }
        if (!string.IsNullOrWhiteSpace(searchTerm))
            sb.AppendLine($"\n🔎 Searched for: \"{searchTerm}\"");
        sb.AppendLine($"\n💡 Say \"download the first one\" or \"download #3\" to start downloading.");

        return new ActionResult
        {
            Success = true,
            Message = sb.ToString()
        };
    }

    /// <summary>Maps a programming language name to file extension.</summary>
    private static string GetExtensionForLanguage(string language) => language switch
    {
        "python" or "py" => ".py",
        "javascript" or "js" => ".js",
        "typescript" or "ts" => ".ts",
        "html" => ".html",
        "css" => ".css",
        "java" => ".java",
        "c" => ".c",
        "cpp" or "c++" => ".cpp",
        "csharp" or "c#" or "cs" => ".cs",
        "go" or "golang" => ".go",
        "rust" or "rs" => ".rs",
        "ruby" or "rb" => ".rb",
        "php" => ".php",
        "swift" => ".swift",
        "kotlin" or "kt" => ".kt",
        "dart" => ".dart",
        "lua" => ".lua",
        "perl" or "pl" => ".pl",
        "r" => ".R",
        "scala" => ".scala",
        "shell" or "bash" or "sh" => ".sh",
        "powershell" or "ps1" => ".ps1",
        "batch" or "bat" or "cmd" => ".bat",
        "sql" => ".sql",
        "json" => ".json",
        "xml" => ".xml",
        "yaml" or "yml" => ".yaml",
        "toml" => ".toml",
        "markdown" or "md" => ".md",
        "txt" or "text" => ".txt",
        "csv" => ".csv",
        "ini" or "config" => ".ini",
        "dockerfile" => "Dockerfile",
        "makefile" => "Makefile",
        _ => ".txt"
    };

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).Take(100).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "file.txt" : sanitized;
    }

    // ─── SCREENSHOT ──────────────────────────────────────────────────
    private async Task<ActionResult> ExecuteScreenshotAsync(Dictionary<string, object>? p, CancellationToken ct)
    {
        var mode = p?.GetValueOrDefault("mode")?.ToString()?.ToLowerInvariant() ?? "fullscreen";
        var savePath = p?.GetValueOrDefault("savePath")?.ToString() ?? "Desktop";

        try
        {
            ReportProgress("📸 Taking screenshot...", -1, "📸");

            var resolvedDir = ResolvePath(savePath);
            Directory.CreateDirectory(resolvedDir);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var fileName = $"Screenshot_{timestamp}.png";
            var fullPath = Path.Combine(resolvedDir, fileName);

            // Use System.Drawing via interop for screenshot
            var screenWidth = (int)System.Windows.SystemParameters.PrimaryScreenWidth;
            var screenHeight = (int)System.Windows.SystemParameters.PrimaryScreenHeight;

            // Get actual DPI-scaled resolution
            using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters());
            var dpiX = source.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            var dpiY = source.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            var actualWidth = (int)(screenWidth * dpiX);
            var actualHeight = (int)(screenHeight * dpiY);

            using var bitmap = new System.Drawing.Bitmap(actualWidth, actualHeight);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);

            if (mode == "window")
            {
                // Give user 3 seconds to focus target window
                ReportProgress("📸 Window screenshot in 3 seconds — switch to target window!", -1, "⏳");
                await Task.Delay(3000, ct);
                ReportProgress("📸 Capturing...", -1, "📸");

                var hwnd = GetForegroundWindow();
                if (GetWindowRect(hwnd, out RECT rect))
                {
                    var w = rect.Right - rect.Left;
                    var h = rect.Bottom - rect.Top;
                    using var windowBitmap = new System.Drawing.Bitmap(w, h);
                    using var windowGraphics = System.Drawing.Graphics.FromImage(windowBitmap);
                    windowGraphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(w, h));
                    windowBitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);

                    return new ActionResult
                    {
                        Success = true,
                        Message = $"📸 Active window screenshot saved!\n📁 {fullPath}\n📐 {w}x{h} pixels"
                    };
                }
            }

            // Full screen screenshot
            graphics.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(actualWidth, actualHeight));
            bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);

            return new ActionResult
            {
                Success = true,
                Message = $"📸 Screenshot saved!\n📁 {fullPath}\n📐 {actualWidth}x{actualHeight} pixels"
            };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Screenshot failed: {ex.Message}" };
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    // ─── TEXT TO SPEECH ──────────────────────────────────────────────
    private async Task<ActionResult> ExecuteTextToSpeechAsync(Dictionary<string, object>? p, CancellationToken ct)
    {
        var text = p?.GetValueOrDefault("text")?.ToString() ?? "";
        var rate = 0;
        if (p?.GetValueOrDefault("speed")?.ToString() is string speedStr)
        {
            rate = speedStr.ToLowerInvariant() switch
            {
                "slow" => -3,
                "fast" => 3,
                "very slow" => -5,
                "very fast" => 5,
                _ => 0
            };
        }

        if (string.IsNullOrWhiteSpace(text))
            return new ActionResult { Success = false, Message = "No text provided to speak." };

        try
        {
            ReportProgress("🔊 Speaking...", -1, "🔊");

            // Run speech on a background thread so it doesn't block
            _ = Task.Run(() =>
            {
                using var synth = new System.Speech.Synthesis.SpeechSynthesizer();
                synth.Rate = rate;
                synth.Volume = 100;
                synth.Speak(text);
            }, ct);

            var preview = text.Length > 100 ? text[..100] + "..." : text;
            return new ActionResult
            {
                Success = true,
                Message = $"🔊 Speaking aloud:\n\n\"{preview}\""
            };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Speech failed: {ex.Message}" };
        }
    }

    // ─── WIFI INFO ───────────────────────────────────────────────────
    private async Task<ActionResult> ExecuteWifiInfoAsync(Dictionary<string, object>? p, CancellationToken ct)
    {
        var showPassword = p?.GetValueOrDefault("showPassword")?.ToString()?.ToLowerInvariant() == "true";

        try
        {
            ReportProgress("📶 Getting WiFi info...", -1, "📶");

            var sb = new System.Text.StringBuilder();

            // Get current WiFi profile using netsh
            var profileResult = await RunNetshCommandAsync("netsh wlan show interfaces", ct);

            if (string.IsNullOrWhiteSpace(profileResult) || profileResult.Contains("not running"))
            {
                return new ActionResult { Success = false, Message = "📶 WiFi adapter not found or disabled." };
            }

            sb.AppendLine("📶 WiFi Network Information");
            sb.AppendLine();

            // Parse interface info
            var lines = profileResult.Split('\n');
            string? currentSsid = null;
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("BSSID"))
                {
                    var val = line.Split(':', 2).LastOrDefault()?.Trim();
                    if (!string.IsNullOrWhiteSpace(val)) { currentSsid = val; sb.AppendLine($"🌐 Network: {val}"); }
                }
                else if (line.StartsWith("Signal", StringComparison.OrdinalIgnoreCase))
                {
                    var val = line.Split(':', 2).LastOrDefault()?.Trim();
                    sb.AppendLine($"📡 Signal: {val}");
                }
                else if (line.StartsWith("Radio type", StringComparison.OrdinalIgnoreCase))
                {
                    var val = line.Split(':', 2).LastOrDefault()?.Trim();
                    sb.AppendLine($"📻 Radio: {val}");
                }
                else if (line.StartsWith("Band", StringComparison.OrdinalIgnoreCase))
                {
                    var val = line.Split(':', 2).LastOrDefault()?.Trim();
                    sb.AppendLine($"📊 Band: {val}");
                }
                else if (line.StartsWith("Receive rate", StringComparison.OrdinalIgnoreCase))
                {
                    var val = line.Split(':', 2).LastOrDefault()?.Trim();
                    sb.AppendLine($"⬇️ Receive: {val}");
                }
                else if (line.StartsWith("Transmit rate", StringComparison.OrdinalIgnoreCase))
                {
                    var val = line.Split(':', 2).LastOrDefault()?.Trim();
                    sb.AppendLine($"⬆️ Transmit: {val}");
                }
                else if (line.StartsWith("Authentication", StringComparison.OrdinalIgnoreCase))
                {
                    var val = line.Split(':', 2).LastOrDefault()?.Trim();
                    sb.AppendLine($"🔒 Auth: {val}");
                }
                else if (line.StartsWith("State", StringComparison.OrdinalIgnoreCase))
                {
                    var val = line.Split(':', 2).LastOrDefault()?.Trim();
                    sb.AppendLine($"✅ State: {val}");
                }
            }

            // Get password if requested
            if (showPassword && !string.IsNullOrWhiteSpace(currentSsid))
            {
                var passResult = await RunNetshCommandAsync($"netsh wlan show profile name=\"{currentSsid}\" key=clear", ct);
                var passLines = passResult?.Split('\n') ?? Array.Empty<string>();
                foreach (var rawLine in passLines)
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith("Key Content", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = line.Split(':', 2).LastOrDefault()?.Trim();
                        sb.AppendLine();
                        sb.AppendLine($"🔑 Password: {val}");
                        break;
                    }
                }
            }
            else if (!showPassword)
            {
                sb.AppendLine();
                sb.AppendLine("💡 Say \"show wifi password\" to reveal the password.");
            }

            return new ActionResult { Success = true, Message = sb.ToString() };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"WiFi info error: {ex.Message}" };
        }
    }

    private static async Task<string> RunNetshCommandAsync(string command, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {command}",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) return "";
        var output = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return output;
    }

    // ─── HASH FILE ───────────────────────────────────────────────────
    private async Task<ActionResult> ExecuteHashFileAsync(Dictionary<string, object>? p, CancellationToken ct)
    {
        var filePath = p?.GetValueOrDefault("filePath")?.ToString() ?? "";
        var algorithm = p?.GetValueOrDefault("algorithm")?.ToString()?.ToUpperInvariant() ?? "SHA256";

        if (string.IsNullOrWhiteSpace(filePath))
            return new ActionResult { Success = false, Message = "No file path provided." };

        var resolved = ResolvePath(filePath);
        if (!File.Exists(resolved))
            return new ActionResult { Success = false, Message = $"File not found: {resolved}" };

        try
        {
            var fileName = Path.GetFileName(resolved);
            ReportProgress($"🔐 Calculating {algorithm} hash for {fileName}...", -1, "🔐");

            using var stream = File.OpenRead(resolved);
            var fileSize = stream.Length;
            byte[] hashBytes;

            using var hashAlgo = algorithm switch
            {
                "MD5" => (System.Security.Cryptography.HashAlgorithm)System.Security.Cryptography.MD5.Create(),
                "SHA1" => System.Security.Cryptography.SHA1.Create(),
                "SHA512" => System.Security.Cryptography.SHA512.Create(),
                _ => System.Security.Cryptography.SHA256.Create()
            };

            hashBytes = await hashAlgo.ComputeHashAsync(stream, ct);
            var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            var sizeStr = FormatBytes(fileSize);

            return new ActionResult
            {
                Success = true,
                Message = $"🔐 **File Hash**\n\n📁 **File:** {fileName}\n📊 **Size:** {sizeStr}\n🔒 **Algorithm:** {algorithm}\n\n`{hashString}`"
            };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Hash calculation failed: {ex.Message}" };
        }
    }

    // ─── SCHEDULE SHUTDOWN ───────────────────────────────────────────
    private async Task<ActionResult> ExecuteScheduleShutdownAsync(Dictionary<string, object>? p, CancellationToken ct)
    {
        var action = p?.GetValueOrDefault("action")?.ToString()?.ToLowerInvariant() ?? "shutdown";
        var minutesStr = p?.GetValueOrDefault("minutes")?.ToString() ?? "0";
        var cancel = p?.GetValueOrDefault("cancel")?.ToString()?.ToLowerInvariant() == "true";

        if (cancel)
        {
            try
            {
                var cancelPsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/a",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                System.Diagnostics.Process.Start(cancelPsi);
                return new ActionResult { Success = true, Message = "✅ Scheduled shutdown/restart has been **cancelled**." };
            }
            catch (Exception ex)
            {
                return new ActionResult { Success = false, Message = $"Failed to cancel: {ex.Message}" };
            }
        }

        if (!int.TryParse(minutesStr, out var minutes) || minutes < 0)
            return new ActionResult { Success = false, Message = "Invalid minutes value." };

        var seconds = minutes * 60;
        var flag = action switch
        {
            "restart" => "/r",
            "sleep" => "/h",    // hibernate/sleep
            "logoff" => "/l",
            _ => "/s"           // shutdown
        };

        var actionName = action switch
        {
            "restart" => "restart",
            "sleep" => "hibernate/sleep",
            "logoff" => "log off",
            _ => "shutdown"
        };

        try
        {
            if (action == "sleep")
            {
                // Sleep needs different approach — use rundll32
                if (minutes > 0)
                {
                    // Schedule sleep using a delayed task
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromMinutes(minutes));
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "rundll32.exe",
                            Arguments = "powrprof.dll,SetSuspendState 0,1,0",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        System.Diagnostics.Process.Start(psi);
                    });
                }
                else
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "rundll32.exe",
                        Arguments = "powrprof.dll,SetSuspendState 0,1,0",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    System.Diagnostics.Process.Start(psi);
                }
            }
            else
            {
                var args = minutes > 0 ? $"{flag} /t {seconds}" : flag;
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                System.Diagnostics.Process.Start(psi);
            }

            var timeStr = minutes > 0 ? $"in **{minutes} minute(s)**" : "**now**";
            return new ActionResult
            {
                Success = true,
                Message = $"⏻ PC will **{actionName}** {timeStr}.\n\n💡 Say \"cancel shutdown\" to abort."
            };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Failed to schedule {actionName}: {ex.Message}" };
        }
    }

    // ─── CONVERT UNITS ───────────────────────────────────────────────
    private async Task<ActionResult> ExecuteConvertUnitsAsync(Dictionary<string, object>? p, CancellationToken ct)
    {
        var valueStr = p?.GetValueOrDefault("value")?.ToString() ?? "0";
        var fromUnit = p?.GetValueOrDefault("from")?.ToString()?.ToLowerInvariant()?.Trim() ?? "";
        var toUnit = p?.GetValueOrDefault("to")?.ToString()?.ToLowerInvariant()?.Trim() ?? "";

        if (!double.TryParse(valueStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value))
            return new ActionResult { Success = false, Message = "Invalid number value." };

        if (string.IsNullOrWhiteSpace(fromUnit) || string.IsNullOrWhiteSpace(toUnit))
            return new ActionResult { Success = false, Message = "Please specify both 'from' and 'to' units." };

        try
        {
            double result;
            var category = "";

            // ── Temperature ──
            if (IsTemp(fromUnit) && IsTemp(toUnit))
            {
                category = "🌡️ Temperature";
                var celsius = fromUnit switch
                {
                    "f" or "fahrenheit" => (value - 32) * 5.0 / 9.0,
                    "k" or "kelvin" => value - 273.15,
                    _ => value // celsius
                };
                result = toUnit switch
                {
                    "f" or "fahrenheit" => celsius * 9.0 / 5.0 + 32,
                    "k" or "kelvin" => celsius + 273.15,
                    _ => celsius
                };
            }
            // ── Weight ──
            else if (IsWeight(fromUnit) && IsWeight(toUnit))
            {
                category = "⚖️ Weight";
                var grams = fromUnit switch
                {
                    "kg" or "kilogram" or "kilograms" => value * 1000,
                    "lb" or "lbs" or "pound" or "pounds" => value * 453.592,
                    "oz" or "ounce" or "ounces" => value * 28.3495,
                    "ton" or "tons" or "tonne" or "tonnes" => value * 1_000_000,
                    "mg" or "milligram" or "milligrams" => value / 1000.0,
                    _ => value // grams
                };
                result = toUnit switch
                {
                    "kg" or "kilogram" or "kilograms" => grams / 1000,
                    "lb" or "lbs" or "pound" or "pounds" => grams / 453.592,
                    "oz" or "ounce" or "ounces" => grams / 28.3495,
                    "ton" or "tons" or "tonne" or "tonnes" => grams / 1_000_000,
                    "mg" or "milligram" or "milligrams" => grams * 1000,
                    _ => grams
                };
            }
            // ── Length ──
            else if (IsLength(fromUnit) && IsLength(toUnit))
            {
                category = "📏 Length";
                var meters = fromUnit switch
                {
                    "km" or "kilometer" or "kilometers" => value * 1000,
                    "cm" or "centimeter" or "centimeters" => value / 100,
                    "mm" or "millimeter" or "millimeters" => value / 1000,
                    "mi" or "mile" or "miles" => value * 1609.344,
                    "ft" or "foot" or "feet" => value * 0.3048,
                    "in" or "inch" or "inches" => value * 0.0254,
                    "yd" or "yard" or "yards" => value * 0.9144,
                    _ => value // meters
                };
                result = toUnit switch
                {
                    "km" or "kilometer" or "kilometers" => meters / 1000,
                    "cm" or "centimeter" or "centimeters" => meters * 100,
                    "mm" or "millimeter" or "millimeters" => meters * 1000,
                    "mi" or "mile" or "miles" => meters / 1609.344,
                    "ft" or "foot" or "feet" => meters / 0.3048,
                    "in" or "inch" or "inches" => meters / 0.0254,
                    "yd" or "yard" or "yards" => meters / 0.9144,
                    _ => meters
                };
            }
            // ── Data ──
            else if (IsData(fromUnit) && IsData(toUnit))
            {
                category = "💾 Data";
                var bytes = fromUnit switch
                {
                    "kb" or "kilobyte" or "kilobytes" => value * 1024,
                    "mb" or "megabyte" or "megabytes" => value * 1024 * 1024,
                    "gb" or "gigabyte" or "gigabytes" => value * 1024 * 1024 * 1024,
                    "tb" or "terabyte" or "terabytes" => value * 1024L * 1024 * 1024 * 1024,
                    "bit" or "bits" => value / 8.0,
                    "kbit" or "kilobit" or "kilobits" => value * 128,
                    "mbit" or "megabit" or "megabits" => value * 131072,
                    _ => value // bytes
                };
                result = toUnit switch
                {
                    "kb" or "kilobyte" or "kilobytes" => bytes / 1024,
                    "mb" or "megabyte" or "megabytes" => bytes / (1024 * 1024),
                    "gb" or "gigabyte" or "gigabytes" => bytes / (1024.0 * 1024 * 1024),
                    "tb" or "terabyte" or "terabytes" => bytes / (1024.0 * 1024 * 1024 * 1024),
                    "bit" or "bits" => bytes * 8,
                    "kbit" or "kilobit" or "kilobits" => bytes / 128,
                    "mbit" or "megabit" or "megabits" => bytes / 131072,
                    _ => bytes
                };
            }
            // ── Time ──
            else if (IsTime(fromUnit) && IsTime(toUnit))
            {
                category = "⏱️ Time";
                var seconds = fromUnit switch
                {
                    "min" or "minute" or "minutes" => value * 60,
                    "hr" or "hour" or "hours" => value * 3600,
                    "day" or "days" => value * 86400,
                    "week" or "weeks" => value * 604800,
                    "month" or "months" => value * 2_592_000,
                    "year" or "years" => value * 31_536_000,
                    "ms" or "millisecond" or "milliseconds" => value / 1000,
                    _ => value // seconds
                };
                result = toUnit switch
                {
                    "min" or "minute" or "minutes" => seconds / 60,
                    "hr" or "hour" or "hours" => seconds / 3600,
                    "day" or "days" => seconds / 86400,
                    "week" or "weeks" => seconds / 604800,
                    "month" or "months" => seconds / 2_592_000,
                    "year" or "years" => seconds / 31_536_000,
                    "ms" or "millisecond" or "milliseconds" => seconds * 1000,
                    _ => seconds
                };
            }
            else
            {
                return new ActionResult
                {
                    Success = false,
                    Message = $"❌ Cannot convert from **{fromUnit}** to **{toUnit}**.\n\nSupported: temperature (°C/°F/K), weight (kg/lb/oz/g), length (m/km/mi/ft/in), data (B/KB/MB/GB/TB), time (s/min/hr/day)"
                };
            }

            var resultStr = result % 1 == 0 ? result.ToString("N0") : result.ToString("G10");
            return new ActionResult
            {
                Success = true,
                Message = $"{category}\n\n**{value} {fromUnit}** = **{resultStr} {toUnit}**"
            };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Conversion error: {ex.Message}" };
        }
    }

    private static bool IsTemp(string u) => u is "c" or "celsius" or "f" or "fahrenheit" or "k" or "kelvin";
    private static bool IsWeight(string u) => u is "g" or "gram" or "grams" or "kg" or "kilogram" or "kilograms" or "lb" or "lbs" or "pound" or "pounds" or "oz" or "ounce" or "ounces" or "ton" or "tons" or "tonne" or "tonnes" or "mg" or "milligram" or "milligrams";
    private static bool IsLength(string u) => u is "m" or "meter" or "meters" or "km" or "kilometer" or "kilometers" or "cm" or "centimeter" or "centimeters" or "mm" or "millimeter" or "millimeters" or "mi" or "mile" or "miles" or "ft" or "foot" or "feet" or "in" or "inch" or "inches" or "yd" or "yard" or "yards";
    private static bool IsData(string u) => u is "b" or "byte" or "bytes" or "kb" or "kilobyte" or "kilobytes" or "mb" or "megabyte" or "megabytes" or "gb" or "gigabyte" or "gigabytes" or "tb" or "terabyte" or "terabytes" or "bit" or "bits" or "kbit" or "kilobit" or "kilobits" or "mbit" or "megabit" or "megabits";
    private static bool IsTime(string u) => u is "s" or "sec" or "second" or "seconds" or "min" or "minute" or "minutes" or "hr" or "hour" or "hours" or "day" or "days" or "week" or "weeks" or "month" or "months" or "year" or "years" or "ms" or "millisecond" or "milliseconds";

    // ─── DATE & TIME ────────────────────────────────────────────────
    private async Task<ActionResult> ExecuteDateTimeAsync(Dictionary<string, object>? p, CancellationToken ct)
    {
        await Task.Yield();

        var mode = p?.GetValueOrDefault("mode")?.ToString()?.ToLowerInvariant() ?? "datetime";
        var now = DateTime.Now;

        var message = mode switch
        {
            "date" => $"📅 Today is {now:dddd, MMMM dd, yyyy}",
            "time" => $"🕒 Current time is {now:hh:mm:ss tt}",
            "utc" => $"🌐 Current UTC time is {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            _ => $"📅 {now:dddd, MMMM dd, yyyy}\n🕒 {now:hh:mm:ss tt}"
        };

        return new ActionResult { Success = true, Message = message };
    }

    // ─── PASSWORD GENERATOR ─────────────────────────────────────────
    private async Task<ActionResult> ExecuteGeneratePasswordAsync(Dictionary<string, object>? p, CancellationToken ct)
    {
        await Task.Yield();

        var length = 16;
        if (int.TryParse(p?.GetValueOrDefault("length")?.ToString(), out var parsedLength))
            length = Math.Clamp(parsedLength, 8, 64);

        var includeSymbols = p?.GetValueOrDefault("includeSymbols")?.ToString()?.ToLowerInvariant() != "false";
        var includeNumbers = p?.GetValueOrDefault("includeNumbers")?.ToString()?.ToLowerInvariant() != "false";
        var includeUpper = p?.GetValueOrDefault("includeUpper")?.ToString()?.ToLowerInvariant() != "false";
        var includeLower = p?.GetValueOrDefault("includeLower")?.ToString()?.ToLowerInvariant() != "false";

        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "23456789";
        const string symbols = "!@#$%^&*-_=+?";

        var pools = new List<string>();
        if (includeLower) pools.Add(lower);
        if (includeUpper) pools.Add(upper);
        if (includeNumbers) pools.Add(digits);
        if (includeSymbols) pools.Add(symbols);

        if (pools.Count == 0)
            return new ActionResult { Success = false, Message = "Select at least one character group for password generation." };

        var combined = string.Concat(pools);
        var chars = new List<char>(length);

        foreach (var pool in pools)
            chars.Add(pool[System.Security.Cryptography.RandomNumberGenerator.GetInt32(pool.Length)]);

        while (chars.Count < length)
            chars.Add(combined[System.Security.Cryptography.RandomNumberGenerator.GetInt32(combined.Length)]);

        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = System.Security.Cryptography.RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        var password = new string(chars.ToArray());

        return new ActionResult
        {
            Success = true,
            Message = $"🔐 Generated password ({length} chars):\n\n`{password}`\n\n💡 Save this in your password manager."
        };
    }

    // ─── QUICK MATH ─────────────────────────────────────────────────
    private async Task<ActionResult> ExecuteQuickMathAsync(Dictionary<string, object>? p, CancellationToken ct)
    {
        await Task.Yield();

        var expression = p?.GetValueOrDefault("expression")?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(expression))
            return new ActionResult { Success = false, Message = "No expression provided." };

        expression = expression.Replace("×", "*").Replace("÷", "/").Trim();

        if (!Regex.IsMatch(expression, @"^[0-9\s\+\-\*/\(\)\.]+$"))
            return new ActionResult { Success = false, Message = "Only numeric math expressions are supported (e.g. (25+5)*3/2)." };

        try
        {
            var table = new System.Data.DataTable();
            var resultObj = table.Compute(expression, string.Empty);
            var result = Convert.ToDouble(resultObj, System.Globalization.CultureInfo.InvariantCulture);
            var formatted = result % 1 == 0 ? result.ToString("N0") : result.ToString("G12");

            return new ActionResult
            {
                Success = true,
                Message = $"🧮 {expression} = **{formatted}**"
            };
        }
        catch
        {
            return new ActionResult { Success = false, Message = "Could not evaluate that expression. Check syntax and try again." };
        }
    }

    // ─── PING HOST ──────────────────────────────────────────────────
    private async Task<ActionResult> ExecutePingHostAsync(Dictionary<string, object>? p, CancellationToken ct)
    {
        var host = p?.GetValueOrDefault("host")?.ToString()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(host))
            return new ActionResult { Success = false, Message = "Please provide a host (e.g. google.com)." };

        var count = 4;
        if (int.TryParse(p?.GetValueOrDefault("count")?.ToString(), out var parsedCount))
            count = Math.Clamp(parsedCount, 1, 10);

        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var latencies = new List<long>();

            for (var i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var reply = await ping.SendPingAsync(host, 3000);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                    latencies.Add(reply.RoundtripTime);
            }

            if (latencies.Count == 0)
                return new ActionResult { Success = false, Message = $"📡 Ping failed for {host}. Host unreachable or blocked." };

            var avg = latencies.Average();
            return new ActionResult
            {
                Success = true,
                Message = $"📡 Ping results for **{host}**\n✅ Replies: {latencies.Count}/{count}\n⚡ Avg latency: {avg:F1} ms\n📉 Min/Max: {latencies.Min()} / {latencies.Max()} ms"
            };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Ping error: {ex.Message}" };
        }
    }

    // ─── PROCESS ACTIONS ────────────────────────────────────────────
    private async Task<ActionResult> ExecuteProcessActionAsync(Dictionary<string, object>? p, CancellationToken ct)
    {
        await Task.Yield();

        var action = p?.GetValueOrDefault("action")?.ToString()?.ToLowerInvariant() ?? "list";

        if (action is "list" or "top")
        {
            var take = 12;
            if (int.TryParse(p?.GetValueOrDefault("limit")?.ToString(), out var parsedTake))
                take = Math.Clamp(parsedTake, 5, 30);

            var processes = System.Diagnostics.Process.GetProcesses()
                .Where(proc =>
                {
                    try { _ = proc.ProcessName; return true; }
                    catch { return false; }
                })
                .Select(proc =>
                {
                    try
                    {
                        return new { proc.ProcessName, proc.Id, MemoryMb = proc.WorkingSet64 / (1024.0 * 1024.0) };
                    }
                    catch
                    {
                        return new { ProcessName = "unknown", Id = -1, MemoryMb = 0.0 };
                    }
                })
                .Where(x => x.Id > 0);

            var sorted = action == "top"
                ? processes.OrderByDescending(x => x.MemoryMb).ThenBy(x => x.ProcessName)
                : processes.OrderBy(x => x.ProcessName).ThenBy(x => x.Id);

            var list = sorted.Take(take).ToList();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(action == "top" ? "🧠 Top memory processes:" : "🧾 Running processes:");
            sb.AppendLine();

            foreach (var proc in list)
                sb.AppendLine($"• {proc.ProcessName} (PID {proc.Id}) — {proc.MemoryMb:F0} MB");

            sb.AppendLine("\n💡 To close one: 'end process notepad' or 'kill process id 1234'.");

            return new ActionResult { Success = true, Message = sb.ToString() };
        }

        if (action == "kill")
        {
            var protectedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "system", "idle", "csrss", "smss", "wininit", "services", "lsass", "dwm", "explorer"
            };

            var name = p?.GetValueOrDefault("processName")?.ToString()?.Trim() ?? "";
            var pidStr = p?.GetValueOrDefault("processId")?.ToString()?.Trim() ?? "";
            var targets = new List<System.Diagnostics.Process>();

            if (int.TryParse(pidStr, out var pid) && pid > 0)
            {
                try { targets.Add(System.Diagnostics.Process.GetProcessById(pid)); }
                catch { }
            }
            else if (!string.IsNullOrWhiteSpace(name))
            {
                var normalized = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
                targets.AddRange(System.Diagnostics.Process.GetProcessesByName(normalized));
            }

            if (targets.Count == 0)
                return new ActionResult { Success = false, Message = "No matching process found to terminate." };

            var currentPid = Environment.ProcessId;
            var killed = 0;
            var skipped = 0;

            foreach (var target in targets.DistinctBy(x => x.Id))
            {
                try
                {
                    var procName = target.ProcessName;
                    if (target.Id == currentPid || protectedProcesses.Contains(procName))
                    {
                        skipped++;
                        continue;
                    }

                    target.Kill(true);
                    killed++;
                }
                catch
                {
                    skipped++;
                }
            }

            if (killed == 0)
                return new ActionResult { Success = false, Message = "No process was terminated (protected process or insufficient permission)." };

            return new ActionResult
            {
                Success = true,
                Message = $"🛑 Terminated {killed} process(es)." + (skipped > 0 ? $" Skipped {skipped} protected/unavailable process(es)." : string.Empty)
            };
        }

        return new ActionResult { Success = false, Message = "Unknown process action. Use: list, top, or kill." };
    }

    // ═══════════════════════════════════════════════════════════════
    // 📋 PREVIOUSLY MISSING B-GRADE INTENTS — Now Implemented
    // ═══════════════════════════════════════════════════════════════

    private async Task<ActionResult> ExecuteBatchOperationsAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        var operation = parameters.TryGetValue("operation", out var opObj) ? opObj?.ToString()?.ToLowerInvariant() ?? "" : "";
        var folderPath = parameters.TryGetValue("folderPath", out var fpObj) ? fpObj?.ToString() ?? "" : "";
        var destination = parameters.TryGetValue("destination", out var destObj) ? destObj?.ToString() ?? "" : "";
        var filter = parameters.TryGetValue("filter", out var filterObj) ? filterObj?.ToString() ?? "*" : "*";

        if (string.IsNullOrWhiteSpace(operation))
            return new ActionResult { Success = false, Message = "Missing parameter: operation (copy, move, delete, rename)" };

        var path = ResolvePath(folderPath);
        if (!ValidatePath(folderPath))
            return new ActionResult { Success = false, Message = "Access denied: protected system path" };
        if (!Directory.Exists(path))
            return new ActionResult { Success = false, Message = $"Folder not found: {path}" };

        var files = Directory.GetFiles(path, filter).ToList();
        if (files.Count == 0)
            return new ActionResult { Success = true, Message = $"No files matching '{filter}' in {path}" };

        var successCount = 0;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"📦 Batch {operation} — {files.Count} files:\n");

        for (int i = 0; i < files.Count; i++)
        {
            ReportProgress($"📦 {operation} ({i + 1}/{files.Count})...", (double)i / files.Count * 100, "📦");
            try
            {
                var file = files[i];
                var fileName = Path.GetFileName(file);
                switch (operation)
                {
                    case "copy":
                        var destPath = Path.Combine(ResolvePath(destination), fileName);
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                        File.Copy(file, destPath, overwrite: false);
                        break;
                    case "move":
                        var moveDest = Path.Combine(ResolvePath(destination), fileName);
                        Directory.CreateDirectory(Path.GetDirectoryName(moveDest)!);
                        File.Move(file, moveDest);
                        break;
                    case "delete":
                        File.Delete(file);
                        break;
                }
                successCount++;
                sb.AppendLine($"  ✅ {fileName}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  ❌ {Path.GetFileName(files[i])}: {ex.Message}");
            }
        }

        return new ActionResult { Success = successCount > 0, Message = $"📦 {operation} complete: {successCount}/{files.Count} succeeded\n\n{sb}" };
    }

    private async Task<ActionResult> ExecuteQuickNoteAsync(Dictionary<string, object> parameters, string aiMessage, CancellationToken ct)
    {
        var action = parameters.TryGetValue("action", out var actObj) ? actObj?.ToString()?.ToLowerInvariant() ?? "add" : "add";
        var content = parameters.TryGetValue("content", out var cObj) ? cObj?.ToString() ?? aiMessage : aiMessage;
        var tag = parameters.TryGetValue("tag", out var tagObj) ? tagObj?.ToString() ?? "" : "";

        var notesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZayFlow", "notes");
        Directory.CreateDirectory(notesDir);
        var notesFile = Path.Combine(notesDir, "quick_notes.json");

        // Load existing notes
        var notes = new List<Dictionary<string, string>>();
        if (File.Exists(notesFile))
        {
            try { notes = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(await File.ReadAllTextAsync(notesFile, ct)) ?? new(); }
            catch { notes = new(); }
        }

        switch (action)
        {
            case "add" or "create" or "save":
                if (string.IsNullOrWhiteSpace(content))
                    return new ActionResult { Success = false, Message = "Please provide note content." };
                notes.Add(new Dictionary<string, string>
                {
                    { "id", Guid.NewGuid().ToString("N")[..8] },
                    { "content", content },
                    { "tag", tag },
                    { "created", DateTime.Now.ToString("yyyy-MM-dd HH:mm") }
                });
                await File.WriteAllTextAsync(notesFile, System.Text.Json.JsonSerializer.Serialize(notes, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), ct);
                return new ActionResult { Success = true, Message = $"📝 Note saved! (#{notes.Count}){(string.IsNullOrEmpty(tag) ? "" : $" [#{tag}]")}" };

            case "list" or "show" or "all":
                if (notes.Count == 0)
                    return new ActionResult { Success = true, Message = "📝 No notes yet." };
                var filtered = string.IsNullOrWhiteSpace(tag) ? notes : notes.Where(n => n.GetValueOrDefault("tag", "").Contains(tag, StringComparison.OrdinalIgnoreCase)).ToList();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"📝 Quick Notes ({filtered.Count}):\n");
                foreach (var n in filtered.TakeLast(20))
                    sb.AppendLine($"  [{n.GetValueOrDefault("id", "?")}] {n.GetValueOrDefault("content", "")} — {n.GetValueOrDefault("created", "")}");
                return new ActionResult { Success = true, Message = sb.ToString() };

            case "delete" or "remove":
                var noteId = content.Trim();
                var before = notes.Count;
                notes.RemoveAll(n => n.GetValueOrDefault("id", "") == noteId);
                if (notes.Count == before)
                    return new ActionResult { Success = false, Message = $"Note '{noteId}' not found." };
                await File.WriteAllTextAsync(notesFile, System.Text.Json.JsonSerializer.Serialize(notes, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), ct);
                return new ActionResult { Success = true, Message = $"🗑️ Note '{noteId}' deleted." };

            case "clear":
                notes.Clear();
                await File.WriteAllTextAsync(notesFile, "[]", ct);
                return new ActionResult { Success = true, Message = "🗑️ All notes cleared." };

            default:
                return new ActionResult { Success = false, Message = "Unknown note action. Use: add, list, delete, or clear." };
        }
    }

    private Task<ActionResult> ExecuteFocusModeAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        var action = parameters.TryGetValue("action", out var actObj) ? actObj?.ToString()?.ToLowerInvariant() ?? "start" : "start";
        var durationMinutes = parameters.TryGetValue("duration", out var durObj) && int.TryParse(durObj?.ToString(), out var dur) ? dur : 25;

        var distractingApps = new[] { "chrome", "firefox", "msedge", "slack", "discord", "telegram", "spotify", "teams" };

        if (action == "start")
        {
            ReportProgress($"🎯 Focus Mode starting ({durationMinutes}min)...", -1, "🎯");

            // Minimize distracting windows
            var minimized = new List<string>();
            foreach (var proc in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (distractingApps.Any(a => proc.ProcessName.Contains(a, StringComparison.OrdinalIgnoreCase)) && proc.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(proc.MainWindowHandle, 6); // SW_MINIMIZE
                        minimized.Add(proc.ProcessName);
                    }
                }
                catch { }
            }

            // Set timer to end focus mode
            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(durationMinutes) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    _notificationService?.ShowInfo("🎯 Focus session complete!", $"You focused for {durationMinutes} minutes. Great work!");
                };
                timer.Start();
            });

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"🎯 Focus Mode activated for {durationMinutes} minutes!\n");
            if (minimized.Count > 0)
                sb.AppendLine($"📱 Minimized distracting apps: {string.Join(", ", minimized.Distinct())}");
            sb.AppendLine($"\n⏰ You'll be notified when your focus session ends.");
            sb.AppendLine("💡 Tip: Stay focused and avoid switching to other apps!");

            return Task.FromResult(new ActionResult { Success = true, Message = sb.ToString() });
        }

        return Task.FromResult(new ActionResult { Success = false, Message = "Use action 'start' to begin focus mode." });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private async Task<ActionResult> ExecuteDailyBriefingAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        ReportProgress("📰 Preparing your daily briefing...", -1, "📰");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"📰 Daily Briefing — {DateTime.Now:dddd, MMMM dd yyyy}\n");

        // System health
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
        var lowDisk = drives.Where(d => d.AvailableFreeSpace < 10L * 1024 * 1024 * 1024).ToList();
        sb.AppendLine("🖥️ System Health:");
        sb.AppendLine($"  Uptime: {TimeSpan.FromMilliseconds(Environment.TickCount64):d'd 'h'h 'm'm'}");
        sb.AppendLine($"  Processes: {System.Diagnostics.Process.GetProcesses().Length}");
        foreach (var d in drives)
        {
            var pct = 100.0 - (100.0 * d.AvailableFreeSpace / d.TotalSize);
            sb.AppendLine($"  {d.Name} {pct:F0}% used ({d.AvailableFreeSpace / 1024 / 1024 / 1024}GB free)");
        }
        if (lowDisk.Any())
            sb.AppendLine($"  ⚠️ Low disk space on: {string.Join(", ", lowDisk.Select(d => d.Name))}");

        // Recent file activity
        var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var recentFiles = new[] { docsPath, desktopPath }
            .Where(Directory.Exists)
            .SelectMany(p => { try { return Directory.GetFiles(p); } catch { return Array.Empty<string>(); } })
            .Select(f => new FileInfo(f))
            .Where(f => f.LastWriteTime > DateTime.Now.AddHours(-24))
            .OrderByDescending(f => f.LastWriteTime)
            .Take(5)
            .ToList();

        if (recentFiles.Any())
        {
            sb.AppendLine("\n📄 Recent files (last 24h):");
            foreach (var f in recentFiles)
                sb.AppendLine($"  {f.Name} — {f.LastWriteTime:HH:mm}");
        }

        // Downloads folder size
        var dlPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(dlPath))
        {
            var dlSize = GetFolderSize(dlPath);
            var dlCount = Directory.GetFiles(dlPath).Length;
            sb.AppendLine($"\n📥 Downloads: {dlCount} files ({dlSize / 1024 / 1024}MB)");
        }

        // Quick notes count
        var notesFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZayFlow", "notes", "quick_notes.json");
        if (File.Exists(notesFile))
        {
            try
            {
                var notes = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(await File.ReadAllTextAsync(notesFile, ct));
                if (notes?.Count > 0)
                    sb.AppendLine($"\n📝 You have {notes.Count} quick note{(notes.Count > 1 ? "s" : "")}");
            }
            catch { }
        }

        sb.AppendLine("\n💡 Tip: Try 'focus mode 25 minutes' for productive work sessions!");

        return new ActionResult { Success = true, Message = sb.ToString() };
    }

    private Task<ActionResult> ExecuteGenerateReportAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        var reportType = parameters.TryGetValue("type", out var rtObj) ? rtObj?.ToString()?.ToLowerInvariant() ?? "disk" : "disk";
        var scope = parameters.TryGetValue("scope", out var scopeObj) ? scopeObj?.ToString() ?? "" : "";

        var sb = new System.Text.StringBuilder();

        switch (reportType)
        {
            case "disk" or "storage":
                sb.AppendLine("📊 Disk Usage Report\n");
                sb.AppendLine($"Generated: {DateTime.Now:f}\n");
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    var used = drive.TotalSize - drive.TotalFreeSpace;
                    var pct = 100.0 * used / drive.TotalSize;
                    var bar = new string('█', (int)(pct / 5)) + new string('░', 20 - (int)(pct / 5));
                    sb.AppendLine($"  {drive.Name} [{bar}] {pct:F0}%");
                    sb.AppendLine($"    {used / 1024 / 1024 / 1024}GB used / {drive.TotalSize / 1024 / 1024 / 1024}GB total ({drive.TotalFreeSpace / 1024 / 1024 / 1024}GB free)\n");
                }
                break;

            case "folder" or "directory":
                var path = ResolvePath(string.IsNullOrWhiteSpace(scope) ? "Documents" : scope);
                if (!Directory.Exists(path))
                    return Task.FromResult(new ActionResult { Success = false, Message = $"Folder not found: {path}" });

                var subdirs = Directory.GetDirectories(path)
                    .Select(d => new { Name = Path.GetFileName(d), Size = GetFolderSize(d) })
                    .OrderByDescending(x => x.Size)
                    .Take(15)
                    .ToList();

                sb.AppendLine($"📊 Folder Report: {path}\n");
                sb.AppendLine($"Generated: {DateTime.Now:f}\n");
                var totalSize = subdirs.Sum(x => x.Size);
                foreach (var dir in subdirs)
                {
                    var pctF = totalSize > 0 ? dir.Size * 100.0 / totalSize : 0;
                    var barF = new string('█', Math.Max(1, (int)(pctF / 5)));
                    sb.AppendLine($"  {barF.PadRight(20)} {pctF:F1}% {dir.Name} ({dir.Size / 1024 / 1024}MB)");
                }
                break;

            case "process" or "performance":
                var procs = System.Diagnostics.Process.GetProcesses()
                    .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0; } })
                    .Take(15)
                    .ToList();

                sb.AppendLine("📊 Performance Report\n");
                sb.AppendLine($"Generated: {DateTime.Now:f}");
                sb.AppendLine($"Total processes: {System.Diagnostics.Process.GetProcesses().Length}\n");
                sb.AppendLine($"{"Process",-30} {"Memory",12}");
                sb.AppendLine(new string('─', 44));
                foreach (var p in procs)
                {
                    try { sb.AppendLine($"{p.ProcessName,-30} {p.WorkingSet64 / 1024 / 1024,10}MB"); } catch { }
                }
                break;

            default:
                return Task.FromResult(new ActionResult { Success = false, Message = "Unknown report type. Available: disk, folder, process" });
        }

        return Task.FromResult(new ActionResult { Success = true, Message = sb.ToString() });
    }

    private async Task<ActionResult> ExecuteFileTemplatesAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        var template = parameters.TryGetValue("template", out var tplObj) ? tplObj?.ToString()?.ToLowerInvariant() ?? "" : "";
        var name = parameters.TryGetValue("name", out var nameObj) ? nameObj?.ToString() ?? "untitled" : "untitled";
        var savePath = parameters.TryGetValue("savePath", out var spObj) ? spObj?.ToString() ?? "Desktop" : "Desktop";

        var resolvedDir = ResolvePath(savePath);
        Directory.CreateDirectory(resolvedDir);

        var templates = new Dictionary<string, (string FileName, string Content)>(StringComparer.OrdinalIgnoreCase)
        {
            { "readme", ($"{name}_README.md", $"# {name}\n\n## Description\n\n## Installation\n\n## Usage\n\n## License\n") },
            { "gitignore", (".gitignore", "bin/\nobj/\n.vs/\n*.user\n*.suo\nnode_modules/\n.env\ndist/\nbuild/\n*.log\n") },
            { "html", ($"{name}.html", $"<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n    <meta charset=\"UTF-8\">\n    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n    <title>{name}</title>\n    <style>\n        body {{ font-family: system-ui; margin: 2rem; }}\n    </style>\n</head>\n<body>\n    <h1>{name}</h1>\n</body>\n</html>") },
            { "python", ($"{name}.py", $"#!/usr/bin/env python3\n\"\"\"Module: {name}\"\"\"\n\n\ndef main():\n    print(\"Hello from {name}!\")\n\n\nif __name__ == \"__main__\":\n    main()\n") },
            { "csharp", ($"{name}.cs", $"using System;\n\nnamespace {name};\n\npublic class Program\n{{\n    static void Main(string[] args)\n    {{\n        Console.WriteLine(\"Hello from {name}!\");\n    }}\n}}\n") },
            { "json", ($"{name}.json", $"{{\n  \"name\": \"{name}\",\n  \"version\": \"1.0.0\",\n  \"description\": \"\"\n}}") },
            { "env", (".env", "# Environment Variables\nDATABASE_URL=\nAPI_KEY=\nDEBUG=true\nPORT=3000\n") },
            { "dockerfile", ("Dockerfile", "FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build\nWORKDIR /app\nCOPY . .\nRUN dotnet publish -c Release -o out\n\nFROM mcr.microsoft.com/dotnet/aspnet:8.0\nWORKDIR /app\nCOPY --from=build /app/out .\nENTRYPOINT [\"dotnet\", \"app.dll\"]\n") },
            { "todo", ($"{name}_TODO.md", $"# {name} — TODO\n\n## High Priority\n- [ ] \n\n## Medium Priority\n- [ ] \n\n## Low Priority\n- [ ] \n\n## Done\n- [x] Created this list ({DateTime.Now:yyyy-MM-dd})\n") },
            { "meeting", ($"meeting_{DateTime.Now:yyyy-MM-dd}.md", $"# Meeting Notes — {DateTime.Now:MMMM dd, yyyy}\n\n## Attendees\n- \n\n## Agenda\n1. \n\n## Discussion\n\n## Action Items\n- [ ] \n\n## Next Meeting\n") },
        };

        if (string.IsNullOrWhiteSpace(template) || template == "list")
        {
            var available = string.Join(", ", templates.Keys.OrderBy(k => k));
            return new ActionResult { Success = true, Message = $"📄 Available templates:\n{available}\n\nUsage: 'create template readme named MyProject'" };
        }

        if (!templates.TryGetValue(template, out var tpl))
            return new ActionResult { Success = false, Message = $"Unknown template: '{template}'. Available: {string.Join(", ", templates.Keys)}" };

        var filePath = Path.Combine(resolvedDir, tpl.FileName);
        await File.WriteAllTextAsync(filePath, tpl.Content, ct);
        return new ActionResult { Success = true, Message = $"📄 Created {tpl.FileName} in {resolvedDir}" };
    }

    private Task<ActionResult> ExecuteWorkspaceSnapshotAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        var action = parameters.TryGetValue("action", out var actObj) ? actObj?.ToString()?.ToLowerInvariant() ?? "capture" : "capture";
        var folderPath = parameters.TryGetValue("folderPath", out var fpObj) ? fpObj?.ToString() ?? "" : "";
        var path = ResolvePath(string.IsNullOrWhiteSpace(folderPath) ? "Documents" : folderPath);

        var snapshotDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZayFlow", "snapshots");
        Directory.CreateDirectory(snapshotDir);

        if (!Directory.Exists(path))
            return Task.FromResult(new ActionResult { Success = false, Message = $"Folder not found: {path}" });

        if (action is "capture" or "save")
        {
            var files = SafeEnumerateFiles(path)
                .Select(f => new FileInfo(f))
                .Select(fi => new Dictionary<string, object>
                {
                    { "path", Path.GetRelativePath(path, fi.FullName) },
                    { "size", fi.Length },
                    { "modified", fi.LastWriteTime.ToString("o") }
                })
                .ToList();

            var snapshot = new Dictionary<string, object>
            {
                { "root", path },
                { "captured", DateTime.Now.ToString("o") },
                { "fileCount", files.Count },
                { "totalSize", files.Sum(f => (long)f["size"]) },
                { "files", files }
            };

            var snapshotFile = Path.Combine(snapshotDir, $"snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(snapshotFile, System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = $"📸 Workspace snapshot captured!\n  📁 {path}\n  📄 {files.Count} files ({files.Sum(f => (long)f["size"]) / 1024 / 1024}MB)\n  💾 Saved to: {snapshotFile}"
            });
        }

        if (action is "list")
        {
            var snapshots = Directory.GetFiles(snapshotDir, "snapshot_*.json")
                .OrderByDescending(f => f)
                .Take(10)
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToList();

            if (snapshots.Count == 0)
                return Task.FromResult(new ActionResult { Success = true, Message = "No snapshots found." });

            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = $"📸 Available snapshots:\n{string.Join("\n", snapshots.Select(s => $"  • {s}"))}"
            });
        }

        return Task.FromResult(new ActionResult { Success = false, Message = "Unknown action. Use: capture, list" });
    }

    private async Task<ActionResult> ExecuteProductivityTipsAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        if (_aiService == null)
        {
            return new ActionResult
            {
                Success = true,
                Message = "💡 Productivity Tips:\n\n" +
                    "1. Use 'focus mode' for distraction-free work sessions\n" +
                    "2. Try 'daily briefing' to start your day informed\n" +
                    "3. Use 'organize downloads' to keep folders tidy\n" +
                    "4. Set reminders for important tasks\n" +
                    "5. Use 'batch operations' for repetitive file tasks"
            };
        }

        ReportProgress("💡 Generating personalized tips...", -1, "💡");

        // Gather context
        var desktopFiles = Directory.Exists(ResolvePath("Desktop")) ? Directory.GetFiles(ResolvePath("Desktop")).Length : 0;
        var dlSize = GetFolderSize(ResolvePath("Downloads"));
        var processCount = System.Diagnostics.Process.GetProcesses().Length;

        var prompt = $"Give me 5 brief, actionable productivity tips for a Windows user. Context: " +
            $"Desktop has {desktopFiles} files, Downloads is {dlSize / 1024 / 1024}MB, {processCount} processes running. " +
            $"Include specific ZayFlow commands they can use. Keep each tip to 1-2 sentences. Use emoji bullets.";

        try
        {
            var tips = await _aiService.GenerateTextAsync(prompt, ct);
            return new ActionResult { Success = true, Message = $"💡 Personalized Tips:\n\n{tips}" };
        }
        catch
        {
            return new ActionResult { Success = true, Message = "💡 Quick Tips:\n• Use 'focus mode' for deep work\n• 'organize downloads' keeps things tidy\n• 'quick note' captures ideas fast" };
        }
    }

    private Task<ActionResult> ExecutePreviewChangesAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        var operation = parameters.TryGetValue("operation", out var opObj) ? opObj?.ToString()?.ToLowerInvariant() ?? "" : "";
        var folderPath = parameters.TryGetValue("folderPath", out var fpObj) ? fpObj?.ToString() ?? "" : "";
        var path = ResolvePath(string.IsNullOrWhiteSpace(folderPath) ? "Desktop" : folderPath);

        if (!Directory.Exists(path))
            return Task.FromResult(new ActionResult { Success = false, Message = $"Folder not found: {path}" });

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"👁️ Preview — What '{operation}' would do:\n");

        switch (operation)
        {
            case "organize" or "organize_folder":
                var files = Directory.GetFiles(path);
                var groups = files.GroupBy(f => Path.GetExtension(f).Trim('.').ToUpperInvariant()).OrderByDescending(g => g.Count());
                sb.AppendLine($"📁 {files.Length} files in {Path.GetFileName(path)} would be organized:\n");
                foreach (var g in groups)
                    sb.AppendLine($"  📂 {(string.IsNullOrEmpty(g.Key) ? "OTHER" : g.Key)}/ → {g.Count()} file{(g.Count() > 1 ? "s" : "")}");
                break;

            case "clean_temp":
                var tempSize = GetFolderSize(Path.GetTempPath());
                var tempFiles = Directory.GetFiles(Path.GetTempPath()).Length;
                sb.AppendLine($"🧹 Would clean {tempFiles} file{(tempFiles > 1 ? "s" : "")} ({tempSize / 1024 / 1024}MB) from temp folder");
                break;

            case "delete":
                var delFiles = Directory.GetFiles(path);
                sb.AppendLine($"🗑️ Would delete {delFiles.Length} files from {Path.GetFileName(path)}:");
                foreach (var f in delFiles.Take(10))
                    sb.AppendLine($"  ❌ {Path.GetFileName(f)} ({new FileInfo(f).Length / 1024}KB)");
                if (delFiles.Length > 10)
                    sb.AppendLine($"  ... and {delFiles.Length - 10} more");
                break;

            default:
                sb.AppendLine("Specify an operation to preview: organize, clean_temp, delete");
                break;
        }

        sb.AppendLine("\n⚠️ This is a preview only. No changes have been made.");
        return Task.FromResult(new ActionResult { Success = true, Message = sb.ToString() });
    }

    private async Task<ActionResult> ExecuteExplainActionAsync(Dictionary<string, object> parameters, string aiMessage, CancellationToken ct)
    {
        var intent = parameters.TryGetValue("intent", out var iObj) ? iObj?.ToString() ?? "" : "";

        if (_aiService == null)
            return new ActionResult { Success = true, Message = $"ℹ️ '{intent}' is a ZayFlow command. Ask me to explain any specific command!" };

        var prompt = $"Explain what the ZayFlow intent '{intent}' does in 2-3 sentences. Include what parameters it accepts, " +
            $"what it does to the system, and any risks. Be concise and practical.";

        try
        {
            var explanation = await _aiService.GenerateTextAsync(prompt, ct);
            return new ActionResult { Success = true, Message = $"ℹ️ **{intent}**:\n\n{explanation}" };
        }
        catch
        {
            return new ActionResult { Success = true, Message = $"ℹ️ '{intent}' is a ZayFlow command. I couldn't generate a detailed explanation right now." };
        }
    }

    private async Task<ActionResult> ExecuteSuggestWorkflowAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        var context = parameters.TryGetValue("context", out var ctxObj) ? ctxObj?.ToString() ?? "" : "";

        if (_aiService == null)
        {
            return new ActionResult
            {
                Success = true,
                Message = "🔄 Suggested Workflows:\n\n" +
                    "**Morning Routine:** daily_briefing → organize downloads → clean_temp\n" +
                    "**Project Setup:** create_folder (template=project) → file_templates → quick_note\n" +
                    "**Cleanup Day:** detect_duplicates → smart_cleanup_schedule → batch_operations (delete)\n" +
                    "**Focus Session:** focus_mode → set_reminder → quick_note"
            };
        }

        var prompt = $"Suggest 3-4 automated workflows using these ZayFlow commands: organize_folder, clean_temp, " +
            $"detect_duplicates, focus_mode, set_reminder, quick_note, batch_operations, daily_briefing, " +
            $"backup_suggestions, smart_search. Context: {(string.IsNullOrWhiteSpace(context) ? "general productivity" : context)}. " +
            $"Format each as a named workflow with chained commands using arrows (→). Keep it brief.";

        try
        {
            var workflows = await _aiService.GenerateTextAsync(prompt, ct);
            return new ActionResult { Success = true, Message = $"🔄 Suggested Workflows:\n\n{workflows}" };
        }
        catch
        {
            return new ActionResult { Success = true, Message = "🔄 Try: 'daily briefing' → 'organize downloads' → 'focus mode 25 min'" };
        }
    }

    /// <summary>
    /// Wraps handler calls to provide null-check safety and consistent error handling.
    /// </summary>
    private async Task<ActionResult> ExecuteHandlerAsync(Func<Task<ActionResult>> handlerCall, string handlerName)
    {
        try
        {
            return await handlerCall();
        }
        catch (NullReferenceException)
        {
            _logger.LogError($"Handler {handlerName} not initialized. Call SetHandlers() first.");
            return new ActionResult { Success = false, Message = $"Internal error: {handlerName} not configured. Please restart ZayFlow." };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Handler {handlerName} error: {ex.Message}");
            return new ActionResult { Success = false, Message = $"Error in {handlerName}: {ex.Message}" };
        }
    }
}
