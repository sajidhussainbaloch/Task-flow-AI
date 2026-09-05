using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.CodeGeneration.Contracts;

namespace ZayFlow.App.Services.CodeGeneration.Memory;

/// <summary>Phase 2 — Interface for persistent code engine session memory.</summary>
public interface ICodeEngineMemoryService
{
    /// <summary>Persist a summary of a completed session for future context retrieval.</summary>
    Task RecordSessionAsync(CodeGenerationSession session, CancellationToken ct = default);

    /// <summary>Returns the most recent N session summaries.</summary>
    Task<List<CodeSessionMemoryEntry>> GetRecentSessionsAsync(int count = 5, CancellationToken ct = default);

    /// <summary>
    /// Returns a compact textual summary of recent sessions relevant to the given request,
    /// ready to be injected as context into a system prompt.
    /// </summary>
    Task<string> GetContextSummaryAsync(string userRequest, CancellationToken ct = default);

    /// <summary>Record a user preference observed from a code generation session.</summary>
    Task RecordPreferenceAsync(string key, string value, CancellationToken ct = default);

    /// <summary>Get all recorded user preferences.</summary>
    Task<Dictionary<string, string>> GetPreferencesAsync(CancellationToken ct = default);

    /// <summary>Find the most recent session for a given workspace for cross-session continuity.</summary>
    Task<CodeSessionMemoryEntry?> FindLastSessionForWorkspaceAsync(string workspaceRoot, CancellationToken ct = default);
}

/// <summary>A compact persisted summary of one completed code-generation session.</summary>
public sealed class CodeSessionMemoryEntry
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
    public string Intent { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string ProjectType { get; set; } = string.Empty;
    public List<string> GeneratedFiles { get; set; } = [];
    public int IterationsUsed { get; set; }
    public double ConfidenceScore { get; set; }
    public bool WasSuccessful { get; set; }
    public string WorkspaceRoot { get; set; } = string.Empty;
    public string FrameworkId { get; set; } = string.Empty;
    public List<string> Patterns { get; set; } = [];
}

/// <summary>
/// Phase 2 — File-backed code engine memory.
/// Stores up to MaxEntries session summaries as a JSON file in the user's AppData folder.
/// Provides context retrieval for future understanding-layer prompts.
/// </summary>
public sealed class CodeEngineMemoryService : ICodeEngineMemoryService
{
    private const int MaxEntries = 20;
    private static readonly SemaphoreSlim _fileLock = new(1, 1);

    private readonly string _storePath;
    private readonly string _prefsPath;
    private readonly ILogger<CodeEngineMemoryService> _logger;

    public CodeEngineMemoryService(ILogger<CodeEngineMemoryService> logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "ZayFlow", "CodeEngineMemory");
        Directory.CreateDirectory(dir);
        _storePath = Path.Combine(dir, "sessions.json");
        _prefsPath = Path.Combine(dir, "preferences.json");
    }

    public async Task RecordSessionAsync(CodeGenerationSession session, CancellationToken ct = default)
    {
        if (session.FinalOutput == null) return;

        var entry = new CodeSessionMemoryEntry
        {
            SessionId = session.SessionId,
            CompletedAt = session.CompletedAt ?? DateTime.UtcNow,
            Intent = session.FinalOutput.Intent,
            Goal = session.Brief?.Goal ?? string.Empty,
            Language = session.Brief?.Language ?? string.Empty,
            ProjectType = session.RepositoryContext.DetectedProjectType,
            GeneratedFiles = session.GeneratedFiles.Select(f => f.RelativePath).ToList(),
            IterationsUsed = session.Telemetry.IterationsUsed,
            ConfidenceScore = session.Telemetry.FinalConfidenceScore,
            WasSuccessful = session.FinalOutput.Success,
            WorkspaceRoot = session.RepositoryContext.WorkspaceRoot,
            FrameworkId = session.Brief?.Blueprint?.Framework?.FrameworkId ?? string.Empty,
            Patterns = InferPatterns(session)
        };

        // Auto-learn preferences from successful sessions
        if (entry.WasSuccessful && !string.IsNullOrWhiteSpace(entry.Language))
        {
            await RecordPreferenceAsync($"last_{entry.Language}_framework", entry.FrameworkId, ct).ConfigureAwait(false);
        }

        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existing = await LoadAsync(ct).ConfigureAwait(false);
            existing.Insert(0, entry);
            if (existing.Count > MaxEntries)
                existing = existing.Take(MaxEntries).ToList();

            var json = JsonSerializer.Serialize(existing, new JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllTextAsync(_storePath, json, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist code session memory");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<List<CodeSessionMemoryEntry>> GetRecentSessionsAsync(int count = 5, CancellationToken ct = default)
    {
        try
        {
            var all = await LoadAsync(ct).ConfigureAwait(false);
            return all.Take(count).ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<string> GetContextSummaryAsync(string userRequest, CancellationToken ct = default)
    {
        var recent = await GetRecentSessionsAsync(5, ct).ConfigureAwait(false);
        var prefs = await GetPreferencesAsync(ct).ConfigureAwait(false);

        var sb = new System.Text.StringBuilder();

        if (prefs.Count > 0)
        {
            sb.AppendLine("## User preferences (learned from past sessions)");
            foreach (var (key, value) in prefs)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    sb.AppendLine($"- {key}: {value}");
            }

            sb.AppendLine();
        }

        if (recent.Count == 0) return sb.ToString().TrimEnd();

        sb.AppendLine("## Recent code-generation sessions (for context)");

        foreach (var entry in recent)
        {
            var age = (DateTime.UtcNow - entry.CompletedAt).TotalMinutes < 60
                ? $"{(int)(DateTime.UtcNow - entry.CompletedAt).TotalMinutes}m ago"
                : $"{(int)(DateTime.UtcNow - entry.CompletedAt).TotalHours}h ago";

            var status = entry.WasSuccessful ? "✓" : "✗";
            var files = entry.GeneratedFiles.Count > 0
                ? string.Join(", ", entry.GeneratedFiles.Take(3))
                : "(no files)";

            var framework = !string.IsNullOrWhiteSpace(entry.FrameworkId) ? $" [{entry.FrameworkId}]" : "";
            var patterns = entry.Patterns.Count > 0 ? $" patterns: {string.Join(", ", entry.Patterns)}" : "";

            sb.AppendLine($"- {status} [{entry.Language}]{framework} {entry.Goal} — {files} — {age} ({entry.IterationsUsed} iter, {entry.ConfidenceScore:P0} confidence){patterns}");
        }

        // Cross-session hint: if user seems to be continuing previous work
        var reqLower = userRequest.ToLowerInvariant();
        if (reqLower.Contains("continue", StringComparison.Ordinal) || reqLower.Contains("last time", StringComparison.Ordinal)
            || reqLower.Contains("from before", StringComparison.Ordinal))
        {
            var lastSuccessful = recent.FirstOrDefault(e => e.WasSuccessful);
            if (lastSuccessful != null)
            {
                sb.AppendLine();
                sb.AppendLine($"## Continuation hint: The user's last successful session was [{lastSuccessful.Language}] \"{lastSuccessful.Goal}\" with files: {string.Join(", ", lastSuccessful.GeneratedFiles)}. They may want to extend or modify this project.");
            }
        }

        return sb.ToString().TrimEnd();
    }

    public async Task RecordPreferenceAsync(string key, string value, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) return;

        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var prefs = await LoadPreferencesAsync(ct).ConfigureAwait(false);
            prefs[key] = value;
            var json = JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllTextAsync(_prefsPath, json, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist preference {Key}", key);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<Dictionary<string, string>> GetPreferencesAsync(CancellationToken ct = default)
    {
        try
        {
            return await LoadPreferencesAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    public async Task<CodeSessionMemoryEntry?> FindLastSessionForWorkspaceAsync(string workspaceRoot, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) return null;

        try
        {
            var all = await LoadAsync(ct).ConfigureAwait(false);
            return all.FirstOrDefault(e =>
                string.Equals(e.WorkspaceRoot, workspaceRoot, StringComparison.OrdinalIgnoreCase) && e.WasSuccessful);
        }
        catch
        {
            return null;
        }
    }

    private static List<string> InferPatterns(CodeGenerationSession session)
    {
        var patterns = new List<string>();
        var goal = session.Brief?.Goal?.ToLowerInvariant() ?? "";

        if (goal.Contains("state management", StringComparison.Ordinal) || goal.Contains("provider", StringComparison.Ordinal)
            || goal.Contains("riverpod", StringComparison.Ordinal) || goal.Contains("bloc", StringComparison.Ordinal)
            || goal.Contains("redux", StringComparison.Ordinal))
            patterns.Add("state-management");

        if (goal.Contains("rest api", StringComparison.Ordinal) || goal.Contains("api", StringComparison.Ordinal))
            patterns.Add("api");

        if (goal.Contains("auth", StringComparison.Ordinal) || goal.Contains("login", StringComparison.Ordinal))
            patterns.Add("auth");

        if (goal.Contains("database", StringComparison.Ordinal) || goal.Contains("sqlite", StringComparison.Ordinal)
            || goal.Contains("orm", StringComparison.Ordinal))
            patterns.Add("database");

        if (goal.Contains("test", StringComparison.Ordinal))
            patterns.Add("testing");

        if (session.GeneratedFiles.Count > 5)
            patterns.Add("multi-file");

        return patterns;
    }

    private async Task<Dictionary<string, string>> LoadPreferencesAsync(CancellationToken ct)
    {
        if (!File.Exists(_prefsPath)) return new Dictionary<string, string>();

        try
        {
            var json = await File.ReadAllTextAsync(_prefsPath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private async Task<List<CodeSessionMemoryEntry>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_storePath)) return [];

        try
        {
            var json = await File.ReadAllTextAsync(_storePath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<CodeSessionMemoryEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
