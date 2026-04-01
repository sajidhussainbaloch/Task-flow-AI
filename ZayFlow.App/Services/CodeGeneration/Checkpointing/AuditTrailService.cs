using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ZayFlow.App.Services.CodeGeneration.Checkpointing;

/// <summary>
/// Phase 3 — Append-only audit trail that logs every decision, command,
/// LLM call, file write, and error during a code generation workflow.
/// Persisted to disk for post-mortem analysis and transparency.
/// </summary>
public sealed class AuditTrailService
{
    private static readonly string AuditDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZayFlow", "AuditTrail");

    private readonly ConcurrentDictionary<string, List<AuditEntry>> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<AuditTrailService> _logger;

    public AuditTrailService(ILogger<AuditTrailService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Records an audit entry for a session. Entries are append-only.
    /// </summary>
    public void Record(string sessionId, AuditEventType eventType, string description, string? detail = null)
    {
        var entry = new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = eventType,
            Description = description,
            Detail = detail ?? string.Empty
        };

        var entries = _sessions.GetOrAdd(sessionId, _ => new List<AuditEntry>());
        lock (entries)
        {
            entries.Add(entry);
        }
    }

    /// <summary>
    /// Records a decision the engine made.
    /// </summary>
    public void RecordDecision(string sessionId, string decision, string rationale)
    {
        Record(sessionId, AuditEventType.Decision, decision, rationale);
    }

    /// <summary>
    /// Records an LLM call with token usage.
    /// </summary>
    public void RecordLlmCall(string sessionId, string stage, string model, int? promptTokens = null, int? completionTokens = null)
    {
        var detail = $"Model: {model}";
        if (promptTokens.HasValue)
            detail += $" | Prompt: {promptTokens}";
        if (completionTokens.HasValue)
            detail += $" | Completion: {completionTokens}";

        Record(sessionId, AuditEventType.LlmCall, $"LLM call during {stage}", detail);
    }

    /// <summary>
    /// Records a file operation.
    /// </summary>
    public void RecordFileOperation(string sessionId, string operation, string filePath)
    {
        Record(sessionId, AuditEventType.FileWrite, $"{operation}: {filePath}");
    }

    /// <summary>
    /// Records an error.
    /// </summary>
    public void RecordError(string sessionId, string context, string errorMessage)
    {
        Record(sessionId, AuditEventType.Error, $"Error in {context}", errorMessage);
    }

    /// <summary>
    /// Records a retry attempt.
    /// </summary>
    public void RecordRetry(string sessionId, int attempt, int maxAttempts, string reason)
    {
        Record(sessionId, AuditEventType.Retry, $"Retry {attempt}/{maxAttempts}", reason);
    }

    /// <summary>
    /// Gets all audit entries for a session.
    /// </summary>
    public IReadOnlyList<AuditEntry> GetEntries(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var entries))
        {
            lock (entries)
            {
                return entries.ToList();
            }
        }

        return Array.Empty<AuditEntry>();
    }

    /// <summary>
    /// Flushes a session's audit trail to disk and clears the in-memory buffer.
    /// Called when a workflow completes or fails.
    /// </summary>
    public async Task FlushAsync(string sessionId, CancellationToken ct = default)
    {
        if (!_sessions.TryRemove(sessionId, out var entries))
            return;

        try
        {
            Directory.CreateDirectory(AuditDir);
            var filePath = Path.Combine(AuditDir, $"{SanitizeFileName(sessionId)}.log");

            var sb = new StringBuilder();
            sb.AppendLine($"=== Audit Trail: {sessionId} ===");
            sb.AppendLine($"Flushed at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine(new string('─', 80));

            lock (entries)
            {
                foreach (var entry in entries)
                {
                    sb.AppendLine($"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.EventType}] {entry.Description}");
                    if (!string.IsNullOrWhiteSpace(entry.Detail))
                        sb.AppendLine($"    Detail: {entry.Detail}");
                }
            }

            sb.AppendLine(new string('─', 80));
            sb.AppendLine($"Total entries: {entries.Count}");

            await File.AppendAllTextAsync(filePath, sb.ToString(), ct).ConfigureAwait(false);
            _logger.LogDebug("Audit trail flushed for session {SessionId}: {Count} entries", sessionId, entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush audit trail for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Formats the audit trail as markdown for display.
    /// </summary>
    public string FormatAsMarkdown(string sessionId)
    {
        var entries = GetEntries(sessionId);
        if (entries.Count == 0)
            return "_No audit entries recorded._";

        var sb = new StringBuilder();
        sb.AppendLine("### 📜 Audit Trail");
        sb.AppendLine($"Session: `{sessionId}` | {entries.Count} entries");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            var icon = entry.EventType switch
            {
                AuditEventType.Decision => "🧭",
                AuditEventType.LlmCall => "🤖",
                AuditEventType.FileWrite => "📝",
                AuditEventType.Error => "❌",
                AuditEventType.Retry => "🔁",
                AuditEventType.StepStarted => "▶️",
                AuditEventType.StepCompleted => "✅",
                _ => "ℹ️"
            };

            sb.AppendLine($"- {icon} `{entry.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}` {entry.Description}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Cleans up audit logs older than the specified age.
    /// </summary>
    public void CleanupOldLogs(TimeSpan maxAge)
    {
        if (!Directory.Exists(AuditDir)) return;

        var cutoff = DateTime.UtcNow - maxAge;
        foreach (var file in Directory.GetFiles(AuditDir, "*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
            catch
            {
                // Best effort
            }
        }
    }

    private static string SanitizeFileName(string name)
    {
        return string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    }
}

// ── Models ──────────────────────────────────────────────────────────────────

public enum AuditEventType
{
    Decision,
    LlmCall,
    FileWrite,
    Error,
    Retry,
    StepStarted,
    StepCompleted,
    Info
}

public sealed class AuditEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public AuditEventType EventType { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}
