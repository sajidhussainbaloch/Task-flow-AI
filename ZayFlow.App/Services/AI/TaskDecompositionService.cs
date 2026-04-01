using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Providers;
using ZayFlow.App.Services.Assistant;

namespace ZayFlow.App.Services.AI;

/// <summary>
/// Phase 2 â€” Decomposes complex user requests into sub-tasks with dependencies.
/// Tracks progress per sub-task and provides step-by-step visibility.
/// </summary>
public sealed class TaskDecompositionService
{
    private readonly CloudflareProvider _openRouterProvider;
    private readonly ILogger<TaskDecompositionService> _logger;

    public TaskDecompositionService(CloudflareProvider openRouterProvider, ILogger<TaskDecompositionService> logger)
    {
        _openRouterProvider = openRouterProvider;
        _logger = logger;
    }

    /// <summary>
    /// Determines whether a request is complex enough to benefit from decomposition.
    /// </summary>
    public bool ShouldDecompose(string userMessage)
    {
        var msg = userMessage.ToLowerInvariant();

        // Multiple explicit tasks (numbered or comma-separated)
        if (msg.Contains(" and then ", StringComparison.Ordinal)
            || msg.Contains(" after that ", StringComparison.Ordinal)
            || msg.Contains("step 1", StringComparison.Ordinal)
            || msg.Contains("1.", StringComparison.Ordinal) && msg.Contains("2.", StringComparison.Ordinal))
            return true;

        // Broad multi-concern requests
        var complexKeywords = new[] { "full stack", "fullstack", "complete project", "entire app",
            "with tests", "with documentation", "with auth", "with database", "end to end", "e2e" };
        var matches = complexKeywords.Count(kw => msg.Contains(kw, StringComparison.Ordinal));
        return matches >= 2;
    }

    /// <summary>
    /// Decomposes a complex request into a structured task plan.
    /// </summary>
    public async Task<TaskPlan?> DecomposeAsync(string userMessage, string workspaceRoot, CancellationToken ct = default)
    {
        _logger.LogInformation("TaskDecompositionService: decomposing request");

        var systemPrompt = """
You are a task planning assistant. Break down the user's complex request into a sequenced list of sub-tasks.

## RESPONSE FORMAT (strict JSON):
{
  "tasks": [
    {
      "id": 1,
      "title": "Short description",
      "description": "Detailed description of what this step does",
      "dependsOn": [],
      "category": "code|config|test|docs|setup",
      "estimatedComplexity": "low|medium|high"
    }
  ],
  "summary": "One-line summary of the overall plan"
}

## RULES:
- Each task should be independently actionable.
- Use dependsOn to express ordering (task IDs that must complete first).
- Keep tasks granular but not trivially small â€” each should produce a visible outcome.
- Category determines how the task will be routed (code â†’ code engine, docs â†’ document pipeline, etc.).
- Maximum 10 tasks. If the request is simpler than expected, return fewer tasks.
- Order tasks by dependency: foundational tasks first, dependent tasks later.
""";

        var userPrompt = $"UserMessage: {userMessage}\nWorkspaceRoot: {workspaceRoot}";

        var result = await _openRouterProvider.SendStructuredTurnAsync(
            systemPrompt,
            userPrompt,
            CloudflareProvider.DefaultCodeModel,
            Array.Empty<AssistantAttachment>(),
            ct).ConfigureAwait(false);

        return ParseTaskPlan(result.Message);
    }

    /// <summary>
    /// Formats a task plan as a rich markdown summary for display.
    /// </summary>
    public static string FormatPlan(TaskPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### ðŸ“‹ Task Plan: {plan.Summary}");
        sb.AppendLine();

        foreach (var task in plan.Tasks)
        {
            var statusIcon = task.Status switch
            {
                SubTaskStatus.Completed => "âœ…",
                SubTaskStatus.InProgress => "ðŸ”„",
                SubTaskStatus.Failed => "âŒ",
                _ => "â¬œ"
            };

            var complexity = task.EstimatedComplexity switch
            {
                "high" => "ðŸ”´",
                "medium" => "ðŸŸ¡",
                _ => "ðŸŸ¢"
            };

            var deps = task.DependsOn.Count > 0
                ? $" (after: {string.Join(", ", task.DependsOn.Select(d => $"#{d}"))})"
                : "";

            sb.AppendLine($"{statusIcon} **{task.Id}. {task.Title}** {complexity}{deps}");
            sb.AppendLine($"   {task.Description}");
        }

        var completed = plan.Tasks.Count(t => t.Status == SubTaskStatus.Completed);
        sb.AppendLine();
        sb.AppendLine($"**Progress**: {completed}/{plan.Tasks.Count} tasks completed");

        return sb.ToString().TrimEnd();
    }

    private TaskPlan? ParseTaskPlan(string message)
    {
        try
        {
            // The LLM may return the plan inside the message field; try parsing it as JSON
            var trimmed = message.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                return JsonSerializer.Deserialize<TaskPlan>(trimmed, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }

            // Try extracting JSON from within the message
            var jsonStart = trimmed.IndexOf('{');
            var jsonEnd = trimmed.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonBlock = trimmed[jsonStart..(jsonEnd + 1)];
                return JsonSerializer.Deserialize<TaskPlan>(jsonBlock, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse task plan from LLM response");
        }

        return null;
    }
}

public sealed class TaskPlan
{
    [JsonPropertyName("tasks")]
    public List<SubTask> Tasks { get; set; } = [];

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
}

public sealed class SubTask
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("dependsOn")]
    public List<int> DependsOn { get; set; } = [];

    [JsonPropertyName("category")]
    public string Category { get; set; } = "code";

    [JsonPropertyName("estimatedComplexity")]
    public string EstimatedComplexity { get; set; } = "medium";

    [JsonIgnore]
    public SubTaskStatus Status { get; set; } = SubTaskStatus.Pending;

    [JsonIgnore]
    public string? ResultSummary { get; set; }
}

public enum SubTaskStatus
{
    Pending,
    InProgress,
    Completed,
    Failed
}
