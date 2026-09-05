using Microsoft.Extensions.Logging;
using ZayFlow.App.Services;
using ZayFlow.App.Services.AI;
using ZayFlow.App.Services.AI.Providers;
using ZayFlow.App.Services.CodeGeneration;
using ZayFlow.App.Services.CodeGeneration.Agent;
using ZayFlow.App.Services.CodeGeneration.Checkpointing;
using ZayFlow.App.Services.CodeGeneration.Notes;

namespace ZayFlow.App.Services.Assistant;

public sealed class AssistantOrchestrator : IAssistantOrchestrator
{
    private readonly OpenRouterProvider _openRouterProvider;
    private readonly IWorkspaceContextService _workspaceContextService;
    private readonly ICodeContextBuilder _codeContextBuilder;
    private readonly ICodeEditPlanner _codeEditPlanner;
    private readonly IImagePreprocessService _imagePreprocessService;
    private readonly IOcrService _ocrService;
    private readonly IVisionAnalysisService _visionAnalysisService;
    private readonly IAppPreferencesService _preferencesService;
    private readonly CodeGenerationOrchestrator _codeGenEngine;
    private readonly CodeReviewService _codeReviewService;
    private readonly RefactorService _refactorService;
    private readonly TaskDecompositionService _taskDecompositionService;
    private readonly PlanModeService _planModeService;
    private readonly WorkflowCheckpointService _checkpointService;
    private readonly AuditTrailService _auditTrail;
    private readonly WorkflowNotesService _workflowNotes;
    private readonly ILogger<AssistantOrchestrator> _logger;

    /// <summary>Phase 3 — Tracks the active execution plan awaiting approval.</summary>
    private ExecutionPlan? _pendingPlan;

    public AssistantOrchestrator(
        OpenRouterProvider openRouterProvider,
        IWorkspaceContextService workspaceContextService,
        ICodeContextBuilder codeContextBuilder,
        ICodeEditPlanner codeEditPlanner,
        IImagePreprocessService imagePreprocessService,
        IOcrService ocrService,
        IVisionAnalysisService visionAnalysisService,
        IAppPreferencesService preferencesService,
        CodeGenerationOrchestrator codeGenEngine,
        CodeReviewService codeReviewService,
        RefactorService refactorService,
        TaskDecompositionService taskDecompositionService,
        PlanModeService planModeService,
        WorkflowCheckpointService checkpointService,
        AuditTrailService auditTrail,
        WorkflowNotesService workflowNotes,
        ILogger<AssistantOrchestrator> logger)
    {
        _openRouterProvider = openRouterProvider;
        _workspaceContextService = workspaceContextService;
        _codeContextBuilder = codeContextBuilder;
        _codeEditPlanner = codeEditPlanner;
        _imagePreprocessService = imagePreprocessService;
        _ocrService = ocrService;
        _visionAnalysisService = visionAnalysisService;
        _preferencesService = preferencesService;
        _codeGenEngine = codeGenEngine;
        _codeReviewService = codeReviewService;
        _refactorService = refactorService;
        _taskDecompositionService = taskDecompositionService;
        _planModeService = planModeService;
        _checkpointService = checkpointService;
        _auditTrail = auditTrail;
        _workflowNotes = workflowNotes;
        _logger = logger;
    }

    public async Task<AssistantTurnResult> ProcessTurnAsync(AssistantTurnRequest request, CancellationToken ct = default)
    {
        // ── Phase 3: handle plan-mode follow-ups (approve/reject/skip/retry/abort) ──
        if (_pendingPlan != null)
        {
            var planResponse = HandlePlanFollowUp(request.UserMessage);
            if (planResponse != null)
                return planResponse;
        }

        var preparedAttachments = await PrepareAttachmentsAsync(request, ct).ConfigureAwait(false);
        var mode = DetectMode(request.UserMessage, request.WorkspaceRoot, preparedAttachments);

        // Workspace context is needed for code-adjacent modes too
        var needsWorkspace = mode is AssistantTurnMode.Code or AssistantTurnMode.CodeReview
            or AssistantTurnMode.Refactor or AssistantTurnMode.Research;
        var snapshot = needsWorkspace
            ? _workspaceContextService.Capture(request.WorkspaceRoot, request.UserMessage, request.LastCreatedFilePath)
            : new WorkspaceContextSnapshot();

        // ── Route CodeReview mode ──
        if (mode == AssistantTurnMode.CodeReview)
        {
            try
            {
                _logger.LogInformation("Routing through CodeReviewService");
                var reviewResult = await _codeReviewService.ReviewAsync(request, snapshot, ct).ConfigureAwait(false);
                reviewResult.Mode = mode;
                return reviewResult;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CodeReviewService failed, falling back to standard pipeline");
            }
        }

        // ── Route Refactor mode ──
        if (mode == AssistantTurnMode.Refactor)
        {
            try
            {
                _logger.LogInformation("Routing through RefactorService");
                var refactorResult = await _refactorService.RefactorAsync(request, snapshot, ct).ConfigureAwait(false);
                refactorResult.Mode = mode;
                return refactorResult;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RefactorService failed, falling back to standard pipeline");
            }
        }

        // Route code-generation requests through the layered code-generation engine
        // Only for explicit code creation/editing — NOT for chat, sorting, file ops, etc.
        if (mode == AssistantTurnMode.Code && preparedAttachments.Count == 0 && IsCodeGenerationRequest(request.UserMessage))
        {
            try
            {
                _logger.LogInformation("Routing code request through layered code-generation engine");
                var engineResult = await _codeGenEngine.RunAsync(request, snapshot, ct).ConfigureAwait(false);
                engineResult.Mode = mode;
                engineResult.SuggestedWorkspaceRoot = string.IsNullOrWhiteSpace(request.WorkspaceRoot) ? snapshot.RootPath : request.WorkspaceRoot;

                // Apply existing enrichment for confirmation flow
                if (string.Equals(engineResult.Intent, "create_file", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(engineResult.Intent, "edit_file", StringComparison.OrdinalIgnoreCase))
                {
                    engineResult.RequiresConfirmation = true;
                }

                return engineResult;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Code generation engine failed, falling back to standard pipeline");
                // Fall through to the original pipeline
            }
        }

        var codeContext = mode is AssistantTurnMode.Code or AssistantTurnMode.Research
            ? _codeContextBuilder.BuildPromptContext(request, snapshot)
            : string.Empty;

        OcrResult? ocrResult = null;
        var firstOcrAttachment = preparedAttachments.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.OcrText));
        if (firstOcrAttachment != null)
        {
            ocrResult = new OcrResult
            {
                Success = true,
                Text = firstOcrAttachment.OcrText,
                Lines = firstOcrAttachment.OcrText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).ToList(),
                Summary = firstOcrAttachment.PreviewText,
                IsCodeLike = firstOcrAttachment.OcrText.Contains("class ", StringComparison.OrdinalIgnoreCase)
                    || firstOcrAttachment.OcrText.Contains("def ", StringComparison.OrdinalIgnoreCase)
            };
        }

        var visionAnalysis = ocrResult != null ? _visionAnalysisService.Analyze(ocrResult.Text) : null;
        var prompt = AssistantPromptBuilder.BuildUserPrompt(request, snapshot, codeContext, ocrResult, visionAnalysis);
        var model = SelectModel(mode);

        AssistantTurnResult result;
        try
        {
            result = await _openRouterProvider.SendStructuredTurnAsync(
                AssistantPromptBuilder.BuildSystemPrompt(mode),
                prompt,
                model,
                preparedAttachments,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI request failed");
            return new AssistantTurnResult
            {
                Mode = mode,
                Message = $"**Connection error:** {ex.Message}\n\nPlease check your internet connection and API key, then try again.",
                Intent = "chat"
            };
        }

        result.Mode = mode;
        result.Attachments = preparedAttachments.ToList();
        result.SuggestedWorkspaceRoot = string.IsNullOrWhiteSpace(request.WorkspaceRoot) ? snapshot.RootPath : request.WorkspaceRoot;

        if (preparedAttachments.Count > 0)
        {
            foreach (var prepared in preparedAttachments)
            {
                result.Artifacts.Insert(0, new AssistantArtifact
                {
                    Kind = AssistantArtifactKind.ImageAttachment,
                    Title = prepared.Title,
                    Summary = string.IsNullOrWhiteSpace(prepared.PreviewText) ? "Attached image" : prepared.PreviewText,
                    Content = prepared.LocalPath,
                    FilePath = prepared.LocalPath,
                    IsPreviewOnly = true
                });
            }

            if (ocrResult is { Success: true })
            {
                result.Artifacts.Add(new AssistantArtifact
                {
                    Kind = AssistantArtifactKind.Ocr,
                    Title = "OCR Preview",
                    Summary = ocrResult.Summary,
                    Content = ocrResult.Text,
                    Language = "text",
                    IsPreviewOnly = true
                });
            }
        }

        if (mode == AssistantTurnMode.Code)
        {
            _codeEditPlanner.EnrichCodeArtifacts(result, request, snapshot);
            if (string.Equals(result.Intent, "create_file", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result.Intent, "edit_file", StringComparison.OrdinalIgnoreCase))
            {
                result.RequiresConfirmation = true;
                result.ToolTraceSummary = "Code proposal ready for preview and apply.";
            }
        }

        if (string.IsNullOrWhiteSpace(result.ToolTraceSummary) && result.ToolInvocations.Count > 0)
        {
            result.ToolTraceSummary = string.Join(" � ", result.ToolInvocations.Select(tool => $"{tool.Name}: {tool.Status}"));
        }

        if (result.Artifacts.Count == 0 && result.Attachments.Count == 0 && mode == AssistantTurnMode.Vision && ocrResult is { Success: true })
        {
            result.Artifacts.Add(new AssistantArtifact
            {
                Kind = AssistantArtifactKind.Ocr,
                Title = "Vision Notes",
                Summary = "Local OCR fallback",
                Content = ocrResult.Text,
                IsPreviewOnly = true
            });
        }

        return result;
    }

    private async Task<IReadOnlyList<AssistantAttachment>> PrepareAttachmentsAsync(AssistantTurnRequest request, CancellationToken ct)
    {
        var prepared = new List<AssistantAttachment>();
        foreach (var attachment in request.Attachments)
        {
            var next = new AssistantAttachment
            {
                Id = attachment.Id,
                Kind = attachment.Kind,
                Title = attachment.Title,
                LocalPath = attachment.LocalPath,
                MimeType = attachment.MimeType,
                PreviewText = attachment.PreviewText,
                OcrText = attachment.OcrText,
                IsCloudReady = attachment.IsCloudReady,
                Width = attachment.Width,
                Height = attachment.Height,
                CreatedAt = attachment.CreatedAt
            };

            next = _imagePreprocessService.Prepare(next);
            if (request.UseLocalOcrFirst && string.IsNullOrWhiteSpace(next.OcrText))
            {
                var ocr = await _ocrService.ExtractTextAsync(next.LocalPath, ct).ConfigureAwait(false);
                if (ocr.Success)
                {
                    next.OcrText = ocr.Text;
                    next.PreviewText = string.IsNullOrWhiteSpace(next.PreviewText) ? ocr.Summary : next.PreviewText;
                }
                else
                {
                    _logger.LogDebug("Local OCR did not produce text for {Attachment}: {Summary}", next.Title, ocr.Summary);
                }
            }

            prepared.Add(next);
        }

        return prepared;
    }

    private string SelectModel(AssistantTurnMode mode)
    {
        var preferences = _preferencesService.Get();
        return mode switch
        {
            AssistantTurnMode.Code or AssistantTurnMode.CodeReview or AssistantTurnMode.Refactor =>
                string.IsNullOrWhiteSpace(preferences.PreferredCodingModel)
                    ? OpenRouterProvider.DefaultCodeModel
                    : preferences.PreferredCodingModel,
            AssistantTurnMode.Vision => string.IsNullOrWhiteSpace(preferences.PreferredVisionModel)
                ? OpenRouterProvider.DefaultVisionModel
                : preferences.PreferredVisionModel,
            _ => OpenRouterProvider.DefaultChatModel
        };
    }

    /// <summary>
    /// Returns true only when the user is explicitly asking to CREATE or WRITE code/program/app.
    /// This prevents non-code requests (chat, file sorting, etc.) from being routed to the code engine.
    /// </summary>
    private static bool IsCodeGenerationRequest(string message)
    {
        var msg = message.ToLowerInvariant();

        if (LooksLikeInformationalRequest(msg) && !HasExplicitCodeTarget(msg))
            return false;

        // Must contain a code-creation verb...
        var hasCreationVerb =
            msg.Contains("create", StringComparison.Ordinal) ||
            msg.Contains("make", StringComparison.Ordinal) ||
            msg.Contains("build", StringComparison.Ordinal) ||
            msg.Contains("write", StringComparison.Ordinal) ||
            msg.Contains("generate", StringComparison.Ordinal) ||
            msg.Contains("code", StringComparison.Ordinal) ||
            msg.Contains("develop", StringComparison.Ordinal) ||
            msg.Contains("implement", StringComparison.Ordinal) ||
            msg.Contains("program", StringComparison.Ordinal) ||
            msg.Contains("design", StringComparison.Ordinal) ||
            msg.Contains("scaffold", StringComparison.Ordinal) ||
            msg.Contains("setup", StringComparison.Ordinal) ||
            msg.Contains("set up", StringComparison.Ordinal) ||
            msg.Contains("add ", StringComparison.Ordinal) ||
            msg.Contains("give me", StringComparison.Ordinal) ||
            msg.Contains("i want", StringComparison.Ordinal) ||
            msg.Contains("i need", StringComparison.Ordinal);

        if (!hasCreationVerb) return false;

        return HasExplicitCodeTarget(msg);
    }

    private static readonly string[] CodeTargetKeywords =
    [
        // Core code artifacts
        "app", "script", "program", "code", "function", "class", "module", "library",
        "component", "widget", "interface", "service", "endpoint", "middleware",
        // App types
        "game", "website", "webpage", "server", "bot", "tool", "calculator", "gui",
        "api", "project", "dashboard", "portal", "form", "page", "panel",
        "timer", "clock", "counter", "converter", "tracker", "planner",
        "player", "viewer", "editor", "manager", "browser", "launcher",
        "chat", "messenger", "calendar", "scheduler", "reminder",
        "login", "signup", "register", "auth", "crud",
        "database", "backend", "frontend", "fullstack",
        // Languages / frameworks
        "python", "html", "javascript", "typescript", "flutter", "react",
        "angular", "vue", "django", "express", "csharp", "java", "kotlin",
        "swift", "ruby", "php", "golang", "rust", "wpf", "winform",
        "tkinter", "pygame", "flask", "fastapi", "nextjs", "node",
        // File extensions
        ".py", ".js", ".ts", ".cs", ".html", ".css", ".java", ".dart",
        ".rb", ".php", ".go", ".rs", ".kt", ".swift", ".xaml"
    ];

    private static bool HasExplicitCodeTarget(string msg)
    {
        foreach (var keyword in CodeTargetKeywords)
        {
            if (msg.Contains(keyword, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool LooksLikeInformationalRequest(string normalized)
    {
        return normalized.Contains("summary", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("summarize", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("explain", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("tell me about", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("about ", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("overview", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("what is", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("who is", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("describe", StringComparison.OrdinalIgnoreCase);
    }

    private static AssistantTurnMode DetectMode(string message, string workspaceRoot, IReadOnlyList<AssistantAttachment> attachments)
    {
        if (attachments.Count > 0)
        {
            return AssistantTurnMode.Vision;
        }

        var n = message.ToLowerInvariant();
        var words = n.Split([' ', ',', '.', '!', '?', ';', ':', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);

        // ── Weighted scoring: each mode accumulates a relevance score ──
        var scores = new Dictionary<AssistantTurnMode, int>
        {
            [AssistantTurnMode.Chat] = 0,
            [AssistantTurnMode.Code] = 0,
            [AssistantTurnMode.DesktopAction] = 0,
            [AssistantTurnMode.CodeReview] = 0,
            [AssistantTurnMode.Refactor] = 0,
            [AssistantTurnMode.Research] = 0,
            [AssistantTurnMode.Document] = 0,
        };

        // ── CodeReview signals (exact + fuzzy) ──
        if (ContainsFuzzy(n, words, "review")) scores[AssistantTurnMode.CodeReview] += 5;
        if (ContainsFuzzy(n, words, "bugs")) scores[AssistantTurnMode.CodeReview] += 4;
        if (n.Contains("find bugs", StringComparison.Ordinal) || FuzzyContainsPhrase(words, "find", "bugs")) scores[AssistantTurnMode.CodeReview] += 6;
        if (n.Contains("find issue", StringComparison.Ordinal) || FuzzyContainsPhrase(words, "find", "issue")) scores[AssistantTurnMode.CodeReview] += 5;
        if (ContainsFuzzy(n, words, "security") && ContainsFuzzy(n, words, "check")) scores[AssistantTurnMode.CodeReview] += 5;
        if (ContainsFuzzy(n, words, "vulnerability") || ContainsFuzzy(n, words, "vulnerabilities")) scores[AssistantTurnMode.CodeReview] += 5;
        if (n.Contains("code quality", StringComparison.Ordinal)) scores[AssistantTurnMode.CodeReview] += 5;
        if (ContainsFuzzy(n, words, "analyze") && HasExplicitCodeTarget(n)) scores[AssistantTurnMode.CodeReview] += 4;
        if (ContainsFuzzy(n, words, "audit") && HasExplicitCodeTarget(n)) scores[AssistantTurnMode.CodeReview] += 4;
        if (ContainsFuzzy(n, words, "performance") && HasExplicitCodeTarget(n)) scores[AssistantTurnMode.CodeReview] += 3;
        if (n.Contains("best practice", StringComparison.Ordinal)) scores[AssistantTurnMode.CodeReview] += 3;

        // ── Refactor signals (exact + fuzzy) ──
        if (ContainsFuzzy(n, words, "refactor")) scores[AssistantTurnMode.Refactor] += 6;
        if (ContainsFuzzy(n, words, "extract") && (ContainsFuzzy(n, words, "method") || ContainsFuzzy(n, words, "class"))) scores[AssistantTurnMode.Refactor] += 6;
        if (ContainsFuzzy(n, words, "simplify") && HasExplicitCodeTarget(n)) scores[AssistantTurnMode.Refactor] += 4;
        if (n.Contains("clean up", StringComparison.Ordinal) && HasExplicitCodeTarget(n)) scores[AssistantTurnMode.Refactor] += 4;
        if (ContainsFuzzy(n, words, "restructure")) scores[AssistantTurnMode.Refactor] += 5;
        if (ContainsFuzzy(n, words, "inline") && HasExplicitCodeTarget(n)) scores[AssistantTurnMode.Refactor] += 4;

        // ── Research signals (exact + fuzzy) ──
        if (ContainsFuzzy(n, words, "research")) scores[AssistantTurnMode.Research] += 6;
        if (ContainsFuzzy(n, words, "investigate")) scores[AssistantTurnMode.Research] += 5;
        if (ContainsFuzzy(n, words, "compare") && (ContainsFuzzy(n, words, "approach") || ContainsFuzzy(n, words, "option") || ContainsFuzzy(n, words, "framework"))) scores[AssistantTurnMode.Research] += 5;
        if (n.Contains("pros and cons", StringComparison.Ordinal) || ContainsFuzzy(n, words, "tradeoff") || n.Contains("trade-off", StringComparison.Ordinal)) scores[AssistantTurnMode.Research] += 5;
        if (n.Contains("best way to", StringComparison.Ordinal)) scores[AssistantTurnMode.Research] += 4;
        if (ContainsFuzzy(n, words, "alternatives")) scores[AssistantTurnMode.Research] += 3;

        // ── Document signals (exact + fuzzy) ──
        if (ContainsFuzzy(n, words, "report") && (ContainsFuzzy(n, words, "write") || ContainsFuzzy(n, words, "generate"))) scores[AssistantTurnMode.Document] += 6;
        if (ContainsFuzzy(n, words, "documentation") || ContainsFuzzy(n, words, "docs")) scores[AssistantTurnMode.Document] += 5;
        if (ContainsFuzzy(n, words, "readme")) scores[AssistantTurnMode.Document] += 6;
        if (ContainsFuzzy(n, words, "specification")) scores[AssistantTurnMode.Document] += 5;
        if (ContainsFuzzy(n, words, "summarize") && !HasExplicitCodeTarget(n)) scores[AssistantTurnMode.Document] += 4;
        if (ContainsFuzzy(n, words, "draft") && (ContainsFuzzy(n, words, "email") || ContainsFuzzy(n, words, "proposal") || ContainsFuzzy(n, words, "report"))) scores[AssistantTurnMode.Document] += 5;

        // ── Desktop action signals (exact + fuzzy) ──
        string[] desktopKeywords = ["sort", "organize", "compress", "backup", "download",
            "clipboard", "screenshot", "wallpaper", "shutdown", "wifi",
            "briefing", "encrypt", "decrypt"];
        foreach (var kw in desktopKeywords)
            if (ContainsFuzzy(n, words, kw)) scores[AssistantTurnMode.DesktopAction] += 4;

        // Multi-word desktop phrases (exact only — these are unlikely to be fuzzy-matched sensibly)
        if (n.Contains("focus mode", StringComparison.Ordinal)) scores[AssistantTurnMode.DesktopAction] += 4;
        if (n.Contains("system info", StringComparison.Ordinal)) scores[AssistantTurnMode.DesktopAction] += 4;
        if (n.Contains("qr code", StringComparison.Ordinal) || n.Contains("qrcode", StringComparison.Ordinal)) scores[AssistantTurnMode.DesktopAction] += 4;

        // Desktop-specific verbs (that are NOT code targets)
        if (!HasExplicitCodeTarget(n))
        {
            if (ContainsFuzzy(n, words, "delete")) scores[AssistantTurnMode.DesktopAction] += 4;
            if (n.Contains("move ", StringComparison.Ordinal)) scores[AssistantTurnMode.DesktopAction] += 4;
            if (ContainsFuzzy(n, words, "remind") || ContainsFuzzy(n, words, "reminder")) scores[AssistantTurnMode.DesktopAction] += 4;
            if (n.Contains("ping ", StringComparison.Ordinal)) scores[AssistantTurnMode.DesktopAction] += 4;
            if (ContainsFuzzy(n, words, "translate")) scores[AssistantTurnMode.DesktopAction] += 4;
            if (ContainsFuzzy(n, words, "disk")) scores[AssistantTurnMode.DesktopAction] += 4;
            if (ContainsFuzzy(n, words, "clean")) scores[AssistantTurnMode.DesktopAction] += 3;
            if (ContainsFuzzy(n, words, "rename")) scores[AssistantTurnMode.DesktopAction] += 3;
        }

        // ── Code signals (exact + fuzzy for languages) ──
        string[] codeExtensions = [".cs", ".py", ".js", ".xaml", ".html"];
        foreach (var ext in codeExtensions)
            if (n.Contains(ext, StringComparison.Ordinal)) scores[AssistantTurnMode.Code] += 3;

        string[] codeLangs = ["python", "javascript", "csharp", "flutter", "react",
            "debug", "compile", "algorithm", "typescript", "angular", "vue", "django", "express",
            "kotlin", "swift", "java", "ruby", "php", "golang", "rust", "wpf", "tkinter",
            "pygame", "flask", "fastapi", "nextjs", "node"];
        foreach (var lang in codeLangs)
            if (ContainsFuzzy(n, words, lang)) scores[AssistantTurnMode.Code] += 3;
        // c# is special — exact match only
        if (n.Contains("c#", StringComparison.Ordinal)) scores[AssistantTurnMode.Code] += 3;

        // Common app types boost Code
        string[] appTypes = ["dashboard", "form", "page", "panel", "timer", "clock",
            "counter", "converter", "tracker", "planner", "player", "viewer", "editor",
            "manager", "launcher", "calendar", "scheduler", "login", "signup", "crud",
            "database", "backend", "frontend", "fullstack", "portfolio", "chat",
            "messenger", "widget", "component", "endpoint", "middleware"];
        foreach (var appType in appTypes)
            if (ContainsFuzzy(n, words, appType)) scores[AssistantTurnMode.Code] += 3;

        // Creation verbs boost Code if there's a code target
        if (HasExplicitCodeTarget(n))
        {
            string[] creationVerbs = ["create", "make", "build", "write", "generate", "develop", "implement",
                "design", "scaffold", "setup"];
            foreach (var verb in creationVerbs)
                if (ContainsFuzzy(n, words, verb)) scores[AssistantTurnMode.Code] += 4;

            // Informal request patterns also boost Code
            if (n.Contains("i want", StringComparison.Ordinal) || n.Contains("i need", StringComparison.Ordinal)
                || n.Contains("give me", StringComparison.Ordinal))
                scores[AssistantTurnMode.Code] += 4;
        }

        // ── General action verbs → DesktopAction (lower weight fallback) ──
        string[] generalActionVerbs = ["open", "search", "image", "note", "template", "password",
            "convert", "hash", "process"];
        foreach (var verb in generalActionVerbs)
            if (ContainsFuzzy(n, words, verb)) scores[AssistantTurnMode.DesktopAction] += 2;

        // ── Chat signals (informational) ──
        if (LooksLikeInformationalRequest(n) && !HasExplicitCodeTarget(n))
            scores[AssistantTurnMode.Chat] += 4;

        // ── Pick highest score; fallback to Chat ──
        var best = scores.OrderByDescending(kv => kv.Value).First();
        return best.Value > 0 ? best.Key : AssistantTurnMode.Chat;
    }

    // ───────────────────── Fuzzy matching helpers ─────────────────────

    /// <summary>
    /// Returns true if the text contains the keyword exactly, or if any word in the
    /// message is within edit-distance 2 of the keyword (for words 4+ chars).
    /// Short keywords (≤3 chars) require exact match to avoid false positives.
    /// </summary>
    private static bool ContainsFuzzy(string fullText, string[] words, string keyword)
    {
        // Fast path: exact substring match
        if (fullText.Contains(keyword, StringComparison.Ordinal))
            return true;

        // Only fuzzy-match keywords that are 4+ characters — short words produce too many false hits
        if (keyword.Length < 4)
            return false;

        // Allow edit distance 1 for short keywords (4-5), distance 2 for longer ones
        var maxDistance = keyword.Length <= 5 ? 1 : 2;

        foreach (var word in words)
        {
            // Skip words that are very different in length — can't be a fuzzy match
            if (Math.Abs(word.Length - keyword.Length) > maxDistance)
                continue;

            if (LevenshteinDistance(word, keyword) <= maxDistance)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if two keywords appear near each other in the word list (as adjacent or with 1 gap),
    /// with fuzzy matching on each word.
    /// </summary>
    private static bool FuzzyContainsPhrase(string[] words, string word1, string word2)
    {
        for (var i = 0; i < words.Length - 1; i++)
        {
            var matchesFirst = IsWordFuzzyMatch(words[i], word1);
            if (!matchesFirst) continue;

            // Check next word or word after that (allow 1-word gap like "find the bugs")
            var limit = Math.Min(i + 3, words.Length);
            for (var j = i + 1; j < limit; j++)
            {
                if (IsWordFuzzyMatch(words[j], word2))
                    return true;
            }
        }

        return false;
    }

    private static bool IsWordFuzzyMatch(string word, string keyword)
    {
        if (string.Equals(word, keyword, StringComparison.Ordinal))
            return true;
        if (keyword.Length < 4)
            return false;
        var maxDist = keyword.Length <= 5 ? 1 : 2;
        return Math.Abs(word.Length - keyword.Length) <= maxDist && LevenshteinDistance(word, keyword) <= maxDist;
    }

    /// <summary>
    /// Computes the Levenshtein edit distance between two strings.
    /// Optimized with a single-row DP approach.
    /// </summary>
    private static int LevenshteinDistance(string s, string t)
    {
        var sLen = s.Length;
        var tLen = t.Length;

        if (sLen == 0) return tLen;
        if (tLen == 0) return sLen;

        var prev = new int[tLen + 1];
        var curr = new int[tLen + 1];

        for (var j = 0; j <= tLen; j++)
            prev[j] = j;

        for (var i = 1; i <= sLen; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= tLen; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[tLen];
    }

    // ───────────────── Phase 3: Plan Mode helpers ─────────────────

    /// <summary>
    /// Phase 3 — Handles user responses to a pending execution plan.
    /// Returns null if the message is not a plan follow-up.
    /// </summary>
    private AssistantTurnResult? HandlePlanFollowUp(string message)
    {
        var msg = message.Trim().ToLowerInvariant();

        if (msg is "approve" or "yes" or "go" or "execute" or "run" or "ok")
        {
            _pendingPlan!.IsApproved = true;
            var note = _workflowNotes.EmitPreAction(
                _pendingPlan.Title,
                _pendingPlan.Steps.Count,
                _pendingPlan.TotalEstimatedFiles,
                _pendingPlan.OverallRisk);

            var result = new AssistantTurnResult
            {
                Mode = AssistantTurnMode.Code,
                Message = $"{note}\n\nPlan approved. The code engine will now execute each step.",
                Intent = "plan_approved",
                ToolTraceSummary = "Plan mode: approved by user"
            };
            // Plan is now approved — the ViewModel will drive execution
            return result;
        }

        if (msg is "reject" or "no" or "cancel" or "abort")
        {
            _pendingPlan!.IsRejected = true;
            var plan = _pendingPlan;
            _pendingPlan = null;

            return new AssistantTurnResult
            {
                Mode = AssistantTurnMode.Chat,
                Message = $"Plan \"{plan.Title}\" has been rejected. No actions were taken.",
                Intent = "plan_rejected",
                ToolTraceSummary = "Plan mode: rejected by user"
            };
        }

        if (msg is "skip")
        {
            var current = _pendingPlan!.GetNextReadyStep();
            if (current != null)
            {
                current.Status = PlanStepStatus.Skipped;
                current.ResultSummary = "Skipped by user";
                var next = _pendingPlan.GetNextReadyStep();
                if (next == null)
                {
                    _pendingPlan = null;
                    return new AssistantTurnResult
                    {
                        Mode = AssistantTurnMode.Chat,
                        Message = "All remaining steps have been skipped. Plan complete.",
                        Intent = "plan_complete"
                    };
                }

                return new AssistantTurnResult
                {
                    Mode = AssistantTurnMode.Code,
                    Message = $"Skipped step. Next: **{next.Description}**\n\n" + PlanModeService.FormatPlan(_pendingPlan),
                    Intent = "plan_step_skipped"
                };
            }
        }

        // Not a plan follow-up — return null to continue normal processing
        return null;
    }

    /// <summary>
    /// Phase 3 — Generates an execution plan and presents it to the user for approval.
    /// Called from the ViewModel when plan mode is active.
    /// </summary>
    public async Task<AssistantTurnResult> GeneratePlanForRequestAsync(
        string userMessage, string workspaceRoot, IReadOnlyList<string> workspaceFiles, CancellationToken ct = default)
    {
        ExecutionPlan? plan;
        try
        {
            plan = await _planModeService.GeneratePlanAsync(userMessage, workspaceRoot, workspaceFiles, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new AssistantTurnResult
            {
                Mode = AssistantTurnMode.Chat,
                Message = $"Plan generation failed: {ex.Message}",
                Intent = "plan_failed"
            };
        }

        if (plan == null)
        {
            return new AssistantTurnResult
            {
                Mode = AssistantTurnMode.Chat,
                Message = "Could not generate an execution plan for this request. Try rephrasing or breaking it into smaller parts.",
                Intent = "plan_failed"
            };
        }

        _pendingPlan = plan;
        _auditTrail.RecordDecision("plan-mode", "Generated execution plan",
            $"Title: {plan.Title}, Steps: {plan.Steps.Count}, Risk: {plan.OverallRisk}");

        return new AssistantTurnResult
        {
            Mode = AssistantTurnMode.Code,
            Message = PlanModeService.FormatPlan(plan),
            Intent = "plan_proposed",
            ToolTraceSummary = $"Plan mode: {plan.Steps.Count} steps proposed",
            RequiresConfirmation = false // Approval handled via follow-up messages
        };
    }

    /// <summary>Phase 3 — Returns the currently pending plan, if any.</summary>
    public ExecutionPlan? GetPendingPlan() => _pendingPlan;

    /// <summary>Phase 3 — Clears the pending plan (e.g. after execution completes).</summary>
    public void ClearPendingPlan() => _pendingPlan = null;
}
