using System.Text.RegularExpressions;
using ZayFlow.App.Services.Assistant;
using ZayFlow.App.Services.CodeGeneration.Contracts;
using ZayFlow.App.Services.CodeGeneration.Intelligence;

namespace ZayFlow.App.Services.CodeGeneration;

/// <summary>
/// Output assembler — converts a <see cref="FinalOutputPackage"/> into the existing
/// <see cref="AssistantTurnResult"/> contract so the UI, preview/confirm flow, and
/// execution pipeline remain completely unchanged.
/// </summary>
public sealed class OutputAssembler
{
    /// <summary>
    /// Converts the engine's final output into the AssistantTurnResult the existing
    /// assistant UI already knows how to display and execute.
    /// </summary>
    public AssistantTurnResult Assemble(FinalOutputPackage package, ProjectBlueprint? blueprint = null)
    {
        var result = new AssistantTurnResult
        {
            Mode = AssistantTurnMode.Code,
            Message = BuildRichSummary(package, blueprint),
            Intent = package.Intent,
            Parameters = new Dictionary<string, object>(package.Parameters, StringComparer.OrdinalIgnoreCase),
            RequiresConfirmation = package.Success,
            ConfirmationMessage = package.Success
                ? $"Apply generated code? ({package.Files.Count} file(s), confidence: {package.ConfidenceScore:P0})"
                : string.Empty,
            ToolTraceSummary = package.Success
                ? $"Code engine: {package.IterationsUsed} iteration(s), confidence {package.ConfidenceScore:P0}"
                : $"Code engine blocked: {package.BlockedReason}"
        };

        result.CodeSession = new CodeSessionInfo
        {
            CurrentStage = package.Success ? "Finalization" : "Blocked",
            IterationsUsed = package.IterationsUsed,
            GeneratedFileCount = package.Files.Count,
            ConfidenceScore = package.ConfidenceScore,
            IsBlocked = !package.Success,
            BlockedReason = package.BlockedReason,
            OutputType = package.Intent,
            VerificationSummary = package.LastVerification?.Summary ?? string.Empty
        };

        if (!package.Success)
        {
            result.Message = $"I couldn't generate reliable code for this request. {package.BlockedReason}";
            result.Intent = "chat";
            return result;
        }

        // For single-file output, map directly to create_file/edit_file parameters
        // so IntentExecutionService can execute without changes
        if (package.Files.Count == 1)
        {
            var file = package.Files[0];
            result.Parameters["fileName"] = System.IO.Path.GetFileName(file.RelativePath);
            result.Parameters["content"] = file.Content;
            result.Parameters["language"] = file.Language;

            if (!result.Parameters.ContainsKey("savePath"))
                result.Parameters["savePath"] = "Desktop";

            // Add code preview artifact for the UI
            result.Artifacts.Add(new AssistantArtifact
            {
                Kind = AssistantArtifactKind.CodePreview,
                Title = file.RelativePath,
                Content = file.Content,
                Language = file.Language,
                FilePath = file.RelativePath,
                IsPreviewOnly = true,
                Summary = $"Generated {file.Language} code ({file.Content.Split('\n').Length} lines)"
            });
        }
        else
        {
            // Multi-file: set up project folder and add each as a separate artifact
            result.Intent = "create_project";

            if (!string.IsNullOrWhiteSpace(package.ProjectName))
                result.Parameters["projectName"] = package.ProjectName;
            else
                result.Parameters["projectName"] = "project";

            if (!result.Parameters.ContainsKey("savePath"))
                result.Parameters["savePath"] = "Desktop";

            foreach (var file in package.Files)
            {
                result.Artifacts.Add(new AssistantArtifact
                {
                    Kind = AssistantArtifactKind.CodePreview,
                    Title = file.RelativePath,
                    Content = file.Content,
                    Language = file.Language,
                    FilePath = file.RelativePath,
                    IsPreviewOnly = true,
                    Summary = $"{file.Operation}: {file.RelativePath} ({file.Content.Split('\n').Length} lines)"
                });
            }

            // Set multi-file intent parameters
            result.Parameters["files"] = package.Files.Select(f => new Dictionary<string, object>
            {
                ["fileName"] = f.RelativePath,  // Keep relative path for subfolder creation
                ["content"] = f.Content,
                ["language"] = f.Language,
                ["operation"] = f.Operation
            }).ToList();
        }

        // Carry assumptions forward as metadata
        if (package.Assumptions.Count > 0)
        {
            result.ToolTraceSummary += " | Assumptions: " + string.Join("; ", package.Assumptions);
        }

        if (package.Telemetry.StageDurationsMs.Count > 0)
        {
            result.ToolTraceSummary += $" | Duration {package.Telemetry.TotalDurationMs}ms";
        }

        return result;
    }

    /// <summary>
    /// Builds a detailed summary message with file info, required libraries, and run instructions.
    /// </summary>
    private static string BuildRichSummary(FinalOutputPackage package, ProjectBlueprint? blueprint = null)
    {
        if (!package.Success)
            return $"I couldn't generate reliable code for this request. {package.BlockedReason}";

        var sb = new System.Text.StringBuilder();
        var language = package.Files.FirstOrDefault()?.Language ?? "code";
        var fileCount = package.Files.Count;

        // Title
        sb.AppendLine($"**Generated {fileCount} {language} file(s)** — {package.Summary}");
        sb.AppendLine();

        // File list
        if (fileCount == 1)
        {
            var file = package.Files[0];
            var lines = file.Content.Split('\n').Length;
            sb.AppendLine($"📄 **File:** `{file.RelativePath}` ({lines} lines)");
        }
        else
        {
            sb.AppendLine("📁 **Files:**");
            foreach (var file in package.Files)
            {
                var lines = file.Content.Split('\n').Length;
                sb.AppendLine($"  - `{file.RelativePath}` ({lines} lines)");
            }
        }

        // Detect required libraries from code content
        var allContent = string.Join("\n", package.Files.Select(f => f.Content));
        var libraries = DetectRequiredLibraries(allContent, language);
        if (libraries.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("📦 **Required libraries** (install before running):");
            var installCmd = language.ToLowerInvariant() switch
            {
                "python" => "pip install",
                "javascript" or "typescript" => "npm install",
                "dart" or "flutter" => "flutter pub add",
                "ruby" => "gem install",
                "go" => "go get",
                "rust" => "cargo add",
                _ => "install"
            };
            sb.AppendLine($"```");
            sb.AppendLine($"{installCmd} {string.Join(" ", libraries)}");
            sb.AppendLine($"```");
        }

        // Run instructions
        sb.AppendLine();
        var runCmd = language.ToLowerInvariant() switch
        {
            "python" => $"▶️ **Run:** `python {package.Files[0].RelativePath}`",
            "javascript" => $"▶️ **Run:** `node {package.Files[0].RelativePath}`",
            "typescript" => $"▶️ **Run:** `npx ts-node {package.Files[0].RelativePath}`",
            "html" => "▶️ **Run:** Open the `.html` file in your browser",
            "java" => $"▶️ **Run:** `javac {package.Files[0].RelativePath} && java {System.IO.Path.GetFileNameWithoutExtension(package.Files[0].RelativePath)}`",
            "dart" or "flutter" => "▶️ **Run:** `flutter run`",
            "go" => $"▶️ **Run:** `go run {package.Files[0].RelativePath}`",
            "rust" => "▶️ **Run:** `cargo run`",
            "c" or "cpp" or "c++" => $"▶️ **Run:** `gcc {package.Files[0].RelativePath} -o app && ./app`",
            _ => $"▶️ **Run:** Execute `{package.Files[0].RelativePath}`"
        };
        sb.AppendLine(runCmd);

        if (fileCount > 1 && blueprint?.IsKnownFramework == true && blueprint.Framework != null)
        {
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(blueprint.BootstrapCommand))
                sb.AppendLine($"🔧 **Setup:** `{blueprint.BootstrapCommand}`");
            if (!string.IsNullOrWhiteSpace(blueprint.RunCommand))
                sb.AppendLine($"▶️ **Run:** `{blueprint.RunCommand}`");
            if (!string.IsNullOrWhiteSpace(blueprint.TestCommand))
                sb.AppendLine($"🧪 **Test:** `{blueprint.TestCommand}`");
            if (!string.IsNullOrWhiteSpace(blueprint.Framework.BootstrapHint))
                sb.AppendLine($"\n🧭 {blueprint.Framework.BootstrapHint}");
        }
        else if (fileCount > 1 && (language.Equals("dart", StringComparison.OrdinalIgnoreCase) || language.Equals("flutter", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine();
            sb.AppendLine("🧭 **After you approve file creation, ZayFlow should guide you through Flutter scaffold generation and verification.**");
        }

        // Confidence
        if (package.ConfidenceScore < 0.85)
        {
            sb.AppendLine();
            sb.AppendLine($"⚠️ Confidence: {package.ConfidenceScore:P0} — review the code before running.");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Scans code content for import/require statements and identifies third-party libraries.
    /// </summary>
    private static List<string> DetectRequiredLibraries(string code, string language)
    {
        var libs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Python standard library modules to exclude
        var pythonStdLib = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sys", "os", "math", "random", "datetime", "json", "re", "io", "time",
            "collections", "itertools", "functools", "pathlib", "typing", "abc",
            "string", "struct", "copy", "enum", "dataclasses", "argparse",
            "subprocess", "threading", "multiprocessing", "socket", "http",
            "urllib", "email", "html", "xml", "csv", "sqlite3", "hashlib",
            "logging", "unittest", "pdb", "traceback", "warnings", "contextlib",
            "tempfile", "shutil", "glob", "fnmatch", "stat", "zipfile", "gzip",
            "tarfile", "pickle", "shelve", "dbm", "platform", "ctypes",
            "tkinter", "turtle", "webbrowser", "uuid", "secrets", "textwrap",
            "difflib", "pprint", "getpass", "gettext", "locale", "calendar",
            "heapq", "bisect", "array", "queue", "types", "inspect", "dis",
            "code", "codeop", "ast", "compileall", "token", "tokenize",
            "signal", "mmap", "select", "selectors", "asyncio", "concurrent",
            "decimal", "fractions", "statistics", "operator", "numbers"
        };

        if (language.Equals("python", StringComparison.OrdinalIgnoreCase))
        {
            // Match: import X, from X import Y
            var importMatches = Regex.Matches(code, @"(?:^|\n)\s*(?:import|from)\s+(\w+)", RegexOptions.Multiline);
            foreach (Match m in importMatches)
            {
                var lib = m.Groups[1].Value;
                if (!pythonStdLib.Contains(lib))
                    libs.Add(lib);
            }
        }
        else if (language.Equals("javascript", StringComparison.OrdinalIgnoreCase) ||
                 language.Equals("typescript", StringComparison.OrdinalIgnoreCase))
        {
            // Match: require('x') or import ... from 'x'
            var requireMatches = Regex.Matches(code, @"(?:require\(['""]|from\s+['""])([^'""./][^'""]*)['""]");
            foreach (Match m in requireMatches)
                libs.Add(m.Groups[1].Value);
        }

        return libs.ToList();
    }
}
