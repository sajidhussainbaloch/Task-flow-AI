using System.Text;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Providers;
using ZayFlow.App.Services.Assistant;

namespace ZayFlow.App.Services.AI;

/// <summary>
/// Phase 2 â€” Proposes and generates refactored code with before/after preview.
/// Supports: extract method/class, rename, move, inline, simplify, restructure.
/// </summary>
public sealed class RefactorService
{
    private readonly CloudflareProvider _openRouterProvider;
    private readonly ILogger<RefactorService> _logger;

    public RefactorService(CloudflareProvider openRouterProvider, ILogger<RefactorService> logger)
    {
        _openRouterProvider = openRouterProvider;
        _logger = logger;
    }

    public async Task<AssistantTurnResult> RefactorAsync(
        AssistantTurnRequest request,
        WorkspaceContextSnapshot snapshot,
        CancellationToken ct = default)
    {
        _logger.LogInformation("RefactorService: starting refactor for message: {Msg}", request.UserMessage[..Math.Min(80, request.UserMessage.Length)]);

        var codeSnippet = ExtractCodeFromContext(request, snapshot);
        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(request.UserMessage, codeSnippet, snapshot);

        var result = await _openRouterProvider.SendStructuredTurnAsync(
            systemPrompt,
            userPrompt,
            CloudflareProvider.DefaultCodeModel,
            Array.Empty<AssistantAttachment>(),
            ct).ConfigureAwait(false);

        result.Mode = AssistantTurnMode.Refactor;

        // Add original code as artifact for before/after comparison
        if (!string.IsNullOrWhiteSpace(codeSnippet) && result.Artifacts.All(a => a.Kind != AssistantArtifactKind.Diff))
        {
            result.Artifacts.Insert(0, new AssistantArtifact
            {
                Kind = AssistantArtifactKind.CodePreview,
                Title = "Original Code",
                Content = codeSnippet,
                Language = DetectLanguage(codeSnippet),
                IsPreviewOnly = true,
                Metadata = { ["role"] = "before" }
            });
        }

        return result;
    }

    private static string BuildSystemPrompt()
    {
        return """
You are ZayFlow AI performing a code refactoring. Analyze the code and propose specific refactoring changes.

## RESPONSE FORMAT (strict JSON):
{"intent":"edit_file","message":"<refactoring explanation in markdown>","mode":"refactor","parameters":{"content":"<FULL refactored code>","fileName":"<filename if known>","language":"<language>"},"confirmationMessage":"Apply refactoring changes","requiresConfirmation":true,"tokenCost":1,"artifacts":[],"tools":[]}

## REFACTORING APPROACH:
1. **Identify** what to refactor (the user's goal + your analysis).
2. **Explain** each change with reasoning.
3. **Produce** the COMPLETE refactored code in parameters.content.

## MESSAGE FORMAT (rich markdown):
### ðŸ”„ Refactoring Summary
Brief description of what was refactored and why.

### Changes Applied:
1. **[Change Type]**: Description of change and reasoning.
2. **[Change Type]**: Description of change and reasoning.

### Before â†’ After Highlights:
Show key before/after snippets for the most important changes.

## SUPPORTED REFACTORINGS:
- Extract method/function â€” pull logic into a named method
- Extract class â€” split a large class into focused classes
- Rename â€” improve naming for clarity
- Inline â€” remove unnecessary indirection
- Simplify â€” reduce complexity, remove dead code
- Restructure â€” reorganize code layout, improve separation

## RULES:
- parameters.content MUST contain the ENTIRE refactored file â€” no placeholders, no "..." or "// rest of code".
- If no code is provided, ask the user to share the code to refactor.
- Preserve all existing functionality â€” refactoring must not change behavior.
- Use intent "edit_file" with the full refactored content.
- Set requiresConfirmation to true so the user can review before applying.
""";
    }

    private static string BuildUserPrompt(string userMessage, string codeSnippet, WorkspaceContextSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"UserMessage: {userMessage}");

        if (!string.IsNullOrWhiteSpace(codeSnippet))
        {
            sb.AppendLine();
            sb.AppendLine("Code to refactor:");
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
        if (code.Contains("func ", StringComparison.Ordinal) && code.Contains("package ", StringComparison.Ordinal)) return "go";
        if (code.Contains("fn ", StringComparison.Ordinal) && code.Contains("let ", StringComparison.Ordinal)) return "rust";
        return "text";
    }
}
