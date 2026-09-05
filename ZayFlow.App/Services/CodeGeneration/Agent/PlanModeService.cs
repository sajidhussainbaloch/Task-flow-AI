using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Providers;
using ZayFlow.App.Services.Assistant;

namespace ZayFlow.App.Services.CodeGeneration.Agent;

/// <summary>
/// Phase 3 — Plan Mode: AI proposes a structured execution plan before performing actions.
/// User can approve, modify, or reject the plan. Approved steps execute sequentially with progress.
/// </summary>
public sealed class PlanModeService
{
    private readonly OpenRouterProvider _openRouterProvider;
    private readonly ILogger<PlanModeService> _logger;

    public PlanModeService(OpenRouterProvider openRouterProvider, ILogger<PlanModeService> logger)
    {
        _openRouterProvider = openRouterProvider;
        _logger = logger;
    }

    /// <summary>
    /// Generates a structured execution plan for the user's request.
    /// Returns an ExecutionPlan with steps the user can approve/modify/reject.
    /// </summary>
    public async Task<ExecutionPlan?> GeneratePlanAsync(
        string userMessage,
        string workspaceRoot,
        IReadOnlyList<string> workspaceFiles,
        CancellationToken ct = default)
    {
        _logger.LogInformation("PlanModeService: generating execution plan");

        var systemPrompt = """
You are an execution planning assistant. Analyze the user's request and produce a structured execution plan.

## RESPONSE FORMAT (strict JSON):
{
  "title": "Short title of the plan",
  "summary": "One-line summary of what will happen",
  "steps": [
    {
      "id": 1,
      "description": "What this step does",
      "action": "create_file|edit_file|create_project|run_command|generate_code|review_code|other",
      "target": "File path or target description",
      "estimatedImpact": "low|medium|high",
      "riskLevel": "safe|caution|risky",
      "reversible": true,
      "dependsOn": []
    }
  ],
  "totalEstimatedFiles": 0,
  "overallRisk": "safe|caution|risky"
}

## RULES:
- Each step must be independently describable and verifiable.
- Mark steps that modify existing files as riskLevel "caution" or "risky".
- Delete/overwrite operations should be "risky" and reversible=false.
- Steps that only create new files are "safe" and reversible=true.
- Order steps by dependency: foundational steps first.
- Maximum 15 steps. Combine trivially small steps.
- Be specific about file paths using the workspace context.
""";

        var fileContext = workspaceFiles.Count > 0
            ? $"\nWorkspace files:\n{string.Join("\n", workspaceFiles.Take(30))}"
            : "";

        var userPrompt = $"Request: {userMessage}\nWorkspace: {workspaceRoot}{fileContext}";

        var result = await _openRouterProvider.SendStructuredTurnAsync(
            systemPrompt,
            userPrompt,
            "llama-3.3-70b-versatile",
            Array.Empty<AssistantAttachment>(),
            ct).ConfigureAwait(false);

        var plan = ParsePlan(result.Message);
        if (plan != null)
        {
            plan.OriginalRequest = userMessage;
            plan.WorkspaceRoot = workspaceRoot;
            plan.CreatedAt = DateTime.UtcNow;
        }

        return plan;
    }

    /// <summary>
    /// Formats an execution plan as rich markdown for display in the chat UI.
    /// </summary>
    public static string FormatPlan(ExecutionPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### 📋 Execution Plan: {plan.Title}");
        sb.AppendLine($"_{plan.Summary}_");
        sb.AppendLine();

        foreach (var step in plan.Steps)
        {
            var statusIcon = step.Status switch
            {
                PlanStepStatus.Completed => "✅",
                PlanStepStatus.InProgress => "🔄",
                PlanStepStatus.Failed => "❌",
                PlanStepStatus.Skipped => "⏭️",
                _ => "⬜"
            };

            var riskBadge = step.RiskLevel switch
            {
                "risky" => " 🔴",
                "caution" => " 🟡",
                _ => " 🟢"
            };

            var reversibleTag = step.Reversible ? "" : " ⚠️irreversible";

            var deps = step.DependsOn.Count > 0
                ? $" (after: {string.Join(", ", step.DependsOn.Select(d => $"#{d}"))})"
                : "";

            sb.AppendLine($"{statusIcon} **{step.Id}. {step.Description}**{riskBadge}{reversibleTag}{deps}");
            sb.AppendLine($"   Action: `{step.Action}` → `{step.Target}`");

            if (!string.IsNullOrWhiteSpace(step.ResultSummary))
                sb.AppendLine($"   Result: {step.ResultSummary}");
        }

        var completed = plan.Steps.Count(s => s.Status == PlanStepStatus.Completed);
        var failed = plan.Steps.Count(s => s.Status == PlanStepStatus.Failed);
        sb.AppendLine();
        sb.AppendLine($"**Progress**: {completed}/{plan.Steps.Count} completed{(failed > 0 ? $", {failed} failed" : "")}");
        sb.AppendLine($"**Overall risk**: {plan.OverallRisk} | **Files**: ~{plan.TotalEstimatedFiles}");
        sb.AppendLine();
        sb.AppendLine("Reply **approve** to execute, **reject** to cancel, or describe modifications.");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Formats a compact progress update for a step that just completed.
    /// </summary>
    public static string FormatStepProgress(ExecutionPlan plan, PlanStepItem step)
    {
        var completed = plan.Steps.Count(s => s.Status == PlanStepStatus.Completed);
        return $"✅ Step {step.Id}/{plan.Steps.Count}: {step.Description}\n" +
               $"   {step.ResultSummary}\n" +
               $"   Progress: {completed}/{plan.Steps.Count}";
    }

    private ExecutionPlan? ParsePlan(string message)
    {
        try
        {
            var trimmed = message.Trim();
            string? json = null;

            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                json = trimmed;
            }
            else
            {
                var start = trimmed.IndexOf('{');
                var end = trimmed.LastIndexOf('}');
                if (start >= 0 && end > start)
                    json = trimmed[start..(end + 1)];
            }

            if (json == null) return null;

            return JsonSerializer.Deserialize<ExecutionPlan>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse execution plan from LLM response");
            return null;
        }
    }
}

// ── Models ──────────────────────────────────────────────────────────────────

public enum PlanStepStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Skipped
}

public sealed class PlanStepItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = "other";

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("estimatedImpact")]
    public string EstimatedImpact { get; set; } = "medium";

    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; set; } = "safe";

    [JsonPropertyName("reversible")]
    public bool Reversible { get; set; } = true;

    [JsonPropertyName("dependsOn")]
    public List<int> DependsOn { get; set; } = [];

    [JsonIgnore]
    public PlanStepStatus Status { get; set; } = PlanStepStatus.Pending;

    [JsonIgnore]
    public string ResultSummary { get; set; } = string.Empty;

    [JsonIgnore]
    public DateTime? StartedAt { get; set; }

    [JsonIgnore]
    public DateTime? CompletedAt { get; set; }
}

public sealed class ExecutionPlan
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("steps")]
    public List<PlanStepItem> Steps { get; set; } = [];

    [JsonPropertyName("totalEstimatedFiles")]
    public int TotalEstimatedFiles { get; set; }

    [JsonPropertyName("overallRisk")]
    public string OverallRisk { get; set; } = "safe";

    [JsonIgnore]
    public string OriginalRequest { get; set; } = string.Empty;

    [JsonIgnore]
    public string WorkspaceRoot { get; set; } = string.Empty;

    [JsonIgnore]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public bool IsApproved { get; set; }

    [JsonIgnore]
    public bool IsRejected { get; set; }

    /// <summary>Returns the next step that is ready to execute (all dependencies met).</summary>
    public PlanStepItem? GetNextReadyStep()
    {
        var completedIds = new HashSet<int>(Steps.Where(s => s.Status == PlanStepStatus.Completed).Select(s => s.Id));
        return Steps.FirstOrDefault(s =>
            s.Status == PlanStepStatus.Pending &&
            s.DependsOn.All(dep => completedIds.Contains(dep)));
    }

    /// <summary>True when all steps are completed or skipped.</summary>
    public bool IsComplete => Steps.All(s => s.Status is PlanStepStatus.Completed or PlanStepStatus.Skipped);

    /// <summary>True when any step has failed.</summary>
    public bool HasFailures => Steps.Any(s => s.Status == PlanStepStatus.Failed);
}
