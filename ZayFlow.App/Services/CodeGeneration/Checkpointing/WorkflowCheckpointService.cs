using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.CodeGeneration.Agent;

namespace ZayFlow.App.Services.CodeGeneration.Checkpointing;

/// <summary>
/// Phase 3 — Saves workflow state to disk at each major step.
/// If the app crashes or the user cancels, the checkpoint allows resuming from
/// the last completed step instead of restarting from scratch.
/// </summary>
public sealed class WorkflowCheckpointService
{
    private static readonly string CheckpointDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZayFlow", "Checkpoints");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<WorkflowCheckpointService> _logger;

    public WorkflowCheckpointService(ILogger<WorkflowCheckpointService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Saves the current workflow state to a checkpoint file.
    /// Called after each major step completes.
    /// </summary>
    public async Task SaveCheckpointAsync(WorkflowCheckpoint checkpoint, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(CheckpointDir);
            var filePath = GetCheckpointPath(checkpoint.SessionId);
            checkpoint.UpdatedAt = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(checkpoint, JsonOptions);
            await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);

            _logger.LogDebug("Checkpoint saved: {SessionId} at step {Step}", checkpoint.SessionId, checkpoint.CurrentStepIndex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save checkpoint {SessionId}", checkpoint.SessionId);
        }
    }

    /// <summary>
    /// Attempts to load a checkpoint for the given session.
    /// Returns null if no checkpoint exists.
    /// </summary>
    public async Task<WorkflowCheckpoint?> LoadCheckpointAsync(string sessionId, CancellationToken ct = default)
    {
        var filePath = GetCheckpointPath(sessionId);
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<WorkflowCheckpoint>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load checkpoint {SessionId}", sessionId);
            return null;
        }
    }

    /// <summary>
    /// Finds any incomplete checkpoints that could be resumed.
    /// Returns the most recent one that is not yet complete.
    /// </summary>
    public async Task<WorkflowCheckpoint?> FindResumableCheckpointAsync(string workspaceRoot, CancellationToken ct = default)
    {
        if (!Directory.Exists(CheckpointDir))
            return null;

        try
        {
            WorkflowCheckpoint? best = null;
            foreach (var file in Directory.GetFiles(CheckpointDir, "*.json"))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                    var checkpoint = JsonSerializer.Deserialize<WorkflowCheckpoint>(json, JsonOptions);
                    if (checkpoint == null || checkpoint.IsComplete) continue;
                    if (!string.Equals(checkpoint.WorkspaceRoot, workspaceRoot, StringComparison.OrdinalIgnoreCase)) continue;

                    // Only consider checkpoints from the last 24 hours
                    if (checkpoint.UpdatedAt < DateTime.UtcNow.AddHours(-24)) continue;

                    if (best == null || checkpoint.UpdatedAt > best.UpdatedAt)
                        best = checkpoint;
                }
                catch
                {
                    // Skip corrupt checkpoint files
                }
            }

            return best;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search for resumable checkpoints");
            return null;
        }
    }

    /// <summary>
    /// Marks a checkpoint as complete and cleans up.
    /// </summary>
    public void CompleteCheckpoint(string sessionId)
    {
        var filePath = GetCheckpointPath(sessionId);
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up checkpoint {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Removes all checkpoints older than the specified age.
    /// </summary>
    public void CleanupOldCheckpoints(TimeSpan maxAge)
    {
        if (!Directory.Exists(CheckpointDir)) return;

        var cutoff = DateTime.UtcNow - maxAge;
        foreach (var file in Directory.GetFiles(CheckpointDir, "*.json"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    private static string GetCheckpointPath(string sessionId)
    {
        // Sanitize session ID for file system
        var safe = string.Join("_", sessionId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(CheckpointDir, $"{safe}.json");
    }
}

// ── Models ──────────────────────────────────────────────────────────────────

public sealed class WorkflowCheckpoint
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("workspaceRoot")]
    public string WorkspaceRoot { get; set; } = string.Empty;

    [JsonPropertyName("originalRequest")]
    public string OriginalRequest { get; set; } = string.Empty;

    [JsonPropertyName("planTitle")]
    public string PlanTitle { get; set; } = string.Empty;

    [JsonPropertyName("totalSteps")]
    public int TotalSteps { get; set; }

    [JsonPropertyName("currentStepIndex")]
    public int CurrentStepIndex { get; set; }

    [JsonPropertyName("completedFiles")]
    public List<string> CompletedFiles { get; set; } = [];

    [JsonPropertyName("completedStepIds")]
    public List<int> CompletedStepIds { get; set; } = [];

    [JsonPropertyName("failedStepIds")]
    public List<int> FailedStepIds { get; set; } = [];

    [JsonPropertyName("verificationState")]
    public string VerificationState { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("isComplete")]
    public bool IsComplete { get; set; }

    /// <summary>Creates a checkpoint from an ExecutionPlan.</summary>
    public static WorkflowCheckpoint FromPlan(ExecutionPlan plan, string sessionId)
    {
        return new WorkflowCheckpoint
        {
            SessionId = sessionId,
            WorkspaceRoot = plan.WorkspaceRoot,
            OriginalRequest = plan.OriginalRequest,
            PlanTitle = plan.Title,
            TotalSteps = plan.Steps.Count,
            CurrentStepIndex = 0
        };
    }

    /// <summary>Updates the checkpoint after a step completes.</summary>
    public void RecordStepCompleted(int stepId, IReadOnlyList<string>? filesCreated = null)
    {
        CompletedStepIds.Add(stepId);
        CurrentStepIndex = CompletedStepIds.Count;
        if (filesCreated != null)
            CompletedFiles.AddRange(filesCreated);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Updates the checkpoint after a step fails.</summary>
    public void RecordStepFailed(int stepId)
    {
        FailedStepIds.Add(stepId);
        UpdatedAt = DateTime.UtcNow;
    }
}
