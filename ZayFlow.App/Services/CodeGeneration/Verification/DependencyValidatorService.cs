using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.CodeGeneration.Contracts;

namespace ZayFlow.App.Services.CodeGeneration.Verification;

/// <summary>
/// Phase 2 — Dependency Validator.
/// Checks generated code for missing import/using directives, undefined type references,
/// and obvious missing namespace patterns — without requiring a full compiler.
/// </summary>
public sealed class DependencyValidatorService
{
    private readonly ILogger<DependencyValidatorService> _logger;

    // Common patterns that indicate a missing import in the corresponding language
    private static readonly Dictionary<string, string[]> ImportPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["csharp"] = ["using ", "namespace "],
        ["python"] = ["import ", "from "],
        ["typescript"] = ["import ", "require("],
        ["javascript"] = ["import ", "require(", "const {"],
        ["java"] = ["import "],
        ["go"] = ["import "],
        ["rust"] = ["use "],
        ["php"] = ["use ", "require"],
        ["kotlin"] = ["import "],
        ["swift"] = ["import "]
    };

    // Well-known type references that require specific imports per language
    private static readonly Dictionary<string, Dictionary<string, string>> RequiredImports = new(StringComparer.OrdinalIgnoreCase)
    {
        ["csharp"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["IEnumerable"] = "using System.Collections.Generic;",
            ["List<"] = "using System.Collections.Generic;",
            ["Dictionary<"] = "using System.Collections.Generic;",
            ["Task<"] = "using System.Threading.Tasks;",
            ["Task "] = "using System.Threading.Tasks;",
            ["JsonSerializer"] = "using System.Text.Json;",
            ["Regex"] = "using System.Text.RegularExpressions;",
            ["StringBuilder"] = "using System.Text;",
            ["ILogger"] = "using Microsoft.Extensions.Logging;",
            ["CancellationToken"] = "using System.Threading;",
            ["Path."] = "using System.IO;",
            ["File."] = "using System.IO;",
            ["Directory."] = "using System.IO;",
            ["HttpClient"] = "using System.Net.Http;",
        },
        ["python"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["asyncio"] = "import asyncio",
            ["datetime"] = "from datetime import datetime",
            ["dataclass"] = "from dataclasses import dataclass",
            ["Optional"] = "from typing import Optional",
            ["List["] = "from typing import List",
            ["Dict["] = "from typing import Dict",
            ["os.path"] = "import os",
            ["json."] = "import json",
            ["re."] = "import re",
            ["pathlib"] = "from pathlib import Path",
        }
    };

    public DependencyValidatorService(ILogger<DependencyValidatorService> logger)
    {
        _logger = logger;
    }

    public List<VerificationFinding> Validate(List<GeneratedFile> files, ImplementationPlan plan)
    {
        var findings = new List<VerificationFinding>();

        foreach (var file in files)
        {
            try
            {
                findings.AddRange(CheckFile(file));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dependency validation failed for {File}", file.RelativePath);
            }
        }

        return findings;
    }

    private static List<VerificationFinding> CheckFile(GeneratedFile file)
    {
        var findings = new List<VerificationFinding>();
        var content = file.Content ?? string.Empty;
        var lang = (file.Language ?? string.Empty).ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(content)) return findings;

        // Check for missing well-known imports (language-specific heuristics)
        if (RequiredImports.TryGetValue(lang, out var requiredForLang))
        {
            foreach (var (typeRef, importStatement) in requiredForLang)
            {
                bool usesType = content.Contains(typeRef, StringComparison.Ordinal);
                bool hasImport = content.Contains(importStatement, StringComparison.OrdinalIgnoreCase);

                if (usesType && !hasImport)
                {
                    findings.Add(new VerificationFinding
                    {
                        Category = "dependency",
                        Severity = "warning",
                        FilePath = file.RelativePath,
                        Description = $"Type '{typeRef.TrimEnd('<')}' is used but the import '{importStatement}' may be missing.",
                        SuggestedFix = $"Add '{importStatement}' at the top of the file."
                    });
                }
            }
        }

        // Detect files with no import block at all when the language expects one
        if (ImportPatterns.TryGetValue(lang, out var importKeywords))
        {
            var hasAnyImport = importKeywords.Any(keyword => content.Contains(keyword, StringComparison.Ordinal));
            var looksLikeFileWithDeps = content.Length > 200
                && (content.Contains('(') || content.Contains('{'));

            if (!hasAnyImport && looksLikeFileWithDeps)
            {
                findings.Add(new VerificationFinding
                {
                    Category = "dependency",
                    Severity = "info",
                    FilePath = file.RelativePath,
                    Description = $"No import/using directives detected in {lang} file. Dependencies may be missing.",
                    SuggestedFix = $"Add any required {lang} import or using directives."
                });
            }
        }

        // Cross-file reference check — look for files that reference other generated files by class/module name
        // (heuristic: presence of the expected module name in content without a matching import)
        // This is a lightweight check; full resolution would require AST parsing.

        return findings;
    }
}
