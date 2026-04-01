using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Providers;
using ZayFlow.App.Services.Assistant;

namespace ZayFlow.App.Services.AI;

/// <summary>
/// Phase 2 â€” Analyzes code for bugs, security vulnerabilities, performance issues, and best practices.
/// Returns a structured review as a rich markdown message.
/// </summary>
public sealed class CodeReviewService
{
    private readonly CloudflareProvider _openRouterProvider;
    private readonly ILogger<CodeReviewService> _logger;

    public CodeReviewService(CloudflareProvider openRouterProvider, ILogger<CodeReviewService> logger)
    {
        _openRouterProvider = openRouterProvider;
        _logger = logger;
    }

    public async Task<AssistantTurnResult> ReviewAsync(
        AssistantTurnRequest request,
        WorkspaceContextSnapshot snapshot,
        CancellationToken ct = default)
    {
        _logger.LogInformation("CodeReviewService: starting review for message: {Msg}", request.UserMessage[..Math.Min(80, request.UserMessage.Length)]);

        var codeSnippet = ExtractCodeFromContext(request, snapshot);

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(request.UserMessage, codeSnippet, snapshot);

        var result = await _openRouterProvider.SendStructuredTurnAsync(
            systemPrompt,
            userPrompt,
            CloudflareProvider.DefaultCodeModel,
            Array.Empty<AssistantAttachment>(),
            ct).ConfigureAwait(false);

        result.Mode = AssistantTurnMode.CodeReview;

        // Enrich with review metadata
        if (result.Artifacts.Count == 0 && !string.IsNullOrWhiteSpace(codeSnippet))
        {
            result.Artifacts.Add(new AssistantArtifact
            {
                Kind = AssistantArtifactKind.CodePreview,
                Title = "Reviewed Code",
                Content = codeSnippet,
                Language = DetectLanguage(codeSnippet),
                IsPreviewOnly = true
            });
        }

        return result;
    }

    private static string BuildSystemPrompt()
    {
        return """
You are ZayFlow AI performing a detailed code review. Analyze the provided code and return a JSON response.

## RESPONSE FORMAT (strict JSON):
{"intent":"chat","message":"<structured review in markdown>","mode":"code_review","parameters":{},"confirmationMessage":"","requiresConfirmation":false,"tokenCost":1,"artifacts":[],"tools":[]}

## REVIEW STRUCTURE (in the message field, use rich markdown):
### ðŸ” Code Review Summary
Brief overview of the code and overall quality rating (1-10).

### ðŸ› Bugs & Errors
- [CRITICAL/WARNING/INFO] **Location**: Description. **Fix**: suggestion.

### ðŸ”’ Security
- [CRITICAL/WARNING/INFO] **Location**: Description. **Fix**: suggestion.

### âš¡ Performance
- [WARNING/INFO] **Location**: Description. **Fix**: suggestion.

### ðŸ“ Code Style & Best Practices
- [INFO] **Location**: Description. **Suggestion**: improvement.

### âœ… What's Good
- Positive aspects of the code.

### ðŸ’¡ Recommendations
- Prioritized next steps.

## RULES:
- Be thorough but practical â€” focus on impactful findings first.
- For each finding, provide: severity, location (function/line), description, and a concrete fix.
- If no code is provided, ask the user to share the code to review.
- Rate the overall code quality 1-10 with justification.
- Do NOT invent issues that aren't there. Only report genuine findings.
""";
    }

    private static string BuildUserPrompt(string userMessage, string codeSnippet, WorkspaceContextSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"UserMessage: {userMessage}");

        if (!string.IsNullOrWhiteSpace(codeSnippet))
        {
            sb.AppendLine();
            sb.AppendLine("Code to review:");
            sb.AppendLine("```");
            sb.AppendLine(codeSnippet);
            sb.AppendLine("```");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.RootPath))
        {
            sb.AppendLine();
            sb.AppendLine($"WorkspaceRoot: {snapshot.RootPath}");
        }

        if (snapshot.RelativeFiles?.Count > 0)
        {
            sb.AppendLine("RecentFiles:");
            foreach (var file in snapshot.RelativeFiles.Take(10))
                sb.AppendLine($"  - {file}");
        }

        return sb.ToString().Trim();
    }

    private static string ExtractCodeFromContext(AssistantTurnRequest request, WorkspaceContextSnapshot snapshot)
    {
        // Check if user pasted code in the message (between ``` markers)
        var msg = request.UserMessage;
        var tripleBacktickStart = msg.IndexOf("```", StringComparison.Ordinal);
        if (tripleBacktickStart >= 0)
        {
            var afterLang = msg.IndexOf('\n', tripleBacktickStart);
            if (afterLang >= 0)
            {
                var end = msg.IndexOf("```", afterLang, StringComparison.Ordinal);
                if (end > afterLang)
                    return msg[(afterLang + 1)..end].Trim();
            }
        }

        // Check recent messages for code
        foreach (var recent in request.RecentMessages.Reverse())
        {
            if (recent.Content.Contains("```", StringComparison.Ordinal))
            {
                var start = recent.Content.IndexOf("```", StringComparison.Ordinal);
                var afterNl = recent.Content.IndexOf('\n', start);
                if (afterNl >= 0)
                {
                    var codeEnd = recent.Content.IndexOf("```", afterNl, StringComparison.Ordinal);
                    if (codeEnd > afterNl)
                        return recent.Content[(afterNl + 1)..codeEnd].Trim();
                }
            }
        }

        // Use workspace context file content if available
        if (snapshot.FileContents?.Count > 0)
        {
            var firstContent = snapshot.FileContents.Values.FirstOrDefault(c => c.Length > 0);
            if (firstContent != null) return firstContent;
        }

        return string.Empty;
    }

    private static string DetectLanguage(string code)
    {
        if (code.Contains("def ", StringComparison.Ordinal) && code.Contains(":", StringComparison.Ordinal)) return "python";
        if (code.Contains("namespace ", StringComparison.Ordinal) || code.Contains("using System", StringComparison.Ordinal)) return "csharp";
        if (code.Contains("function ", StringComparison.Ordinal) || code.Contains("const ", StringComparison.Ordinal)) return "javascript";
        if (code.Contains("import React", StringComparison.Ordinal) || code.Contains("jsx", StringComparison.Ordinal)) return "jsx";
        if (code.Contains("func ", StringComparison.Ordinal) && code.Contains("package ", StringComparison.Ordinal)) return "go";
        if (code.Contains("fn ", StringComparison.Ordinal) && code.Contains("let ", StringComparison.Ordinal)) return "rust";
        return "text";
    }
}
