using System.Text;

namespace ZayFlow.App.Services.CodeGeneration.Notes;

/// <summary>
/// Phase 3 — Emits structured workflow notes at key points during code generation:
/// pre-action ("I'm going to..."), in-flight ("Generated 5/8 files..."),
/// post-failure ("Build failed because..."), post-success ("Project created. Next: run...").
/// </summary>
public sealed class WorkflowNotesService
{
    /// <summary>
    /// Generates a pre-action note describing what the engine is about to do.
    /// </summary>
    public string EmitPreAction(string planTitle, int totalSteps, int estimatedFiles, string overallRisk)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🚀 **Starting**: {planTitle}");
        sb.AppendLine($"   {totalSteps} step(s) | ~{estimatedFiles} file(s) | Risk: {FormatRisk(overallRisk)}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Generates an in-flight progress note.
    /// </summary>
    public string EmitProgress(int completedSteps, int totalSteps, int filesGenerated, string currentStepDescription)
    {
        var percent = totalSteps > 0 ? (completedSteps * 100 / totalSteps) : 0;
        return $"🔄 **Progress**: {completedSteps}/{totalSteps} steps ({percent}%) | " +
               $"{filesGenerated} file(s) generated\n" +
               $"   Current: {currentStepDescription}";
    }

    /// <summary>
    /// Generates a post-failure note with actionable guidance.
    /// </summary>
    public string EmitFailure(string stepDescription, string errorMessage, int retriesUsed, int maxRetries, FailureGuidance guidance)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"❌ **Failed**: {stepDescription}");
        sb.AppendLine($"   Error: {errorMessage}");
        sb.AppendLine($"   Retries: {retriesUsed}/{maxRetries}");

        if (!string.IsNullOrWhiteSpace(guidance.Suggestion))
            sb.AppendLine($"   💡 Suggestion: {guidance.Suggestion}");
        if (!string.IsNullOrWhiteSpace(guidance.NextAction))
            sb.AppendLine($"   ➡️ Next: {guidance.NextAction}");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Generates a post-success note with next steps.
    /// </summary>
    public string EmitSuccess(string planTitle, int totalFiles, double confidenceScore, IReadOnlyList<string> nextSteps)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"✅ **Completed**: {planTitle}");
        sb.AppendLine($"   {totalFiles} file(s) generated | Confidence: {confidenceScore:P0}");

        if (nextSteps.Count > 0)
        {
            sb.AppendLine("   **Next steps:**");
            foreach (var step in nextSteps)
                sb.AppendLine($"   • {step}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Generates a checkpoint-resumed note when continuing from a saved state.
    /// </summary>
    public string EmitResumed(string planTitle, int completedSteps, int totalSteps)
    {
        return $"🔁 **Resumed**: {planTitle}\n" +
               $"   Continuing from step {completedSteps + 1}/{totalSteps}";
    }

    private static string FormatRisk(string risk)
    {
        return risk switch
        {
            "risky" => "🔴 High",
            "caution" => "🟡 Medium",
            _ => "🟢 Low"
        };
    }
}

/// <summary>
/// Phase 3 — Provides actionable failure guidance based on error patterns.
/// </summary>
public sealed class FailureGuidanceService
{
    /// <summary>
    /// Analyzes an error and returns structured guidance.
    /// </summary>
    public FailureGuidance Analyze(string errorMessage, string stepAction, int retriesRemaining)
    {
        var error = errorMessage.ToLowerInvariant();
        var guidance = new FailureGuidance();

        // Rate limiting / API errors
        if (error.Contains("rate limit", StringComparison.Ordinal) || error.Contains("429", StringComparison.Ordinal))
        {
            guidance.Category = "rate_limit";
            guidance.Suggestion = "API rate limit reached. The system will retry with exponential backoff.";
            guidance.NextAction = retriesRemaining > 0 ? "Retrying automatically..." : "Wait a moment and try again.";
            guidance.ShouldRetry = retriesRemaining > 0;
            guidance.RetryDelayMs = 2000 * (4 - retriesRemaining); // exponential backoff
            return guidance;
        }

        // Network/timeout errors
        if (error.Contains("timeout", StringComparison.Ordinal) || error.Contains("timed out", StringComparison.Ordinal)
            || error.Contains("network", StringComparison.Ordinal))
        {
            guidance.Category = "network";
            guidance.Suggestion = "Network connectivity issue.";
            guidance.NextAction = retriesRemaining > 0 ? "Retrying..." : "Check your internet connection and try again.";
            guidance.ShouldRetry = retriesRemaining > 0;
            guidance.RetryDelayMs = 1000;
            return guidance;
        }

        // JSON parse errors
        if (error.Contains("json", StringComparison.Ordinal) || error.Contains("deserializ", StringComparison.Ordinal)
            || error.Contains("parse", StringComparison.Ordinal))
        {
            guidance.Category = "parse_error";
            guidance.Suggestion = "The AI returned malformed output. Retrying with a clarified prompt.";
            guidance.NextAction = retriesRemaining > 0 ? "Retrying with adjusted prompt..." : "Try simplifying the request.";
            guidance.ShouldRetry = retriesRemaining > 0;
            guidance.RetryDelayMs = 500;
            return guidance;
        }

        // File system errors
        if (error.Contains("access denied", StringComparison.Ordinal) || error.Contains("permission", StringComparison.Ordinal)
            || error.Contains("file in use", StringComparison.Ordinal))
        {
            guidance.Category = "file_system";
            guidance.Suggestion = "File system permission issue. Check that the target path is writable.";
            guidance.NextAction = "Escalating to user — manual intervention needed.";
            guidance.ShouldRetry = false;
            return guidance;
        }

        // Verification failures
        if (stepAction.Contains("verif", StringComparison.OrdinalIgnoreCase))
        {
            guidance.Category = "verification";
            guidance.Suggestion = "Generated code did not pass verification checks.";
            guidance.NextAction = retriesRemaining > 0 ? "Re-generating with improvement pass..." : "Review the generated code manually.";
            guidance.ShouldRetry = retriesRemaining > 0;
            guidance.RetryDelayMs = 0;
            return guidance;
        }

        // Generic fallback
        guidance.Category = "unknown";
        guidance.Suggestion = $"Unexpected error: {Truncate(errorMessage, 200)}";
        guidance.NextAction = retriesRemaining > 0 ? "Retrying..." : "Try rephrasing the request or breaking it into smaller steps.";
        guidance.ShouldRetry = retriesRemaining > 0;
        guidance.RetryDelayMs = 500;

        return guidance;
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}

public sealed class FailureGuidance
{
    public string Category { get; set; } = "unknown";
    public string Suggestion { get; set; } = string.Empty;
    public string NextAction { get; set; } = string.Empty;
    public bool ShouldRetry { get; set; }
    public int RetryDelayMs { get; set; }
}
