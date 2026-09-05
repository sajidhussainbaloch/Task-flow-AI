using ZayFlow.App.Services.CodeGeneration.Contracts;
using ZayFlow.App.Services.CodeGeneration.Intelligence;
using ZayFlow.App.Services.CodeGeneration.Templates;

namespace ZayFlow.App.Services.CodeGeneration.Verification;

/// <summary>
/// Framework-aware static validator — checks generated files against a <see cref="ProjectBlueprint"/>
/// for structural completeness: required file presence, content markers, folder conventions,
/// and cross-file reference integrity.
/// </summary>
public sealed class FrameworkValidatorService
{
    /// <summary>
    /// Validate generated files against the project blueprint.
    /// Returns a list of findings with severity and suggested fixes.
    /// </summary>
    public List<VerificationFinding> Validate(List<GeneratedFile> files, ProjectBlueprint? blueprint)
    {
        if (blueprint == null || blueprint.Complexity == ProjectComplexity.SingleFile)
            return [];

        var findings = new List<VerificationFinding>();

        ValidateRequiredFiles(files, blueprint, findings);
        ValidateContentMarkers(files, blueprint, findings);
        ValidateFolderConventions(files, blueprint, findings);
        ValidateMinimumFileCount(files, blueprint, findings);
        ValidateAcceptanceCriteria(files, blueprint, findings);

        return findings;
    }

    /// <summary>Check that all required files from the blueprint are present in the generated output.</summary>
    private static void ValidateRequiredFiles(List<GeneratedFile> files, ProjectBlueprint blueprint, List<VerificationFinding> findings)
    {
        var generatedPaths = files
            .Select(f => NormalizePath(f.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expected in blueprint.FileManifest.Where(f => f.Required))
        {
            var expectedPath = NormalizePath(expected.RelativePath);

            // Handle wildcard patterns (e.g. "*.csproj")
            if (expectedPath.Contains('*'))
            {
                var pattern = expectedPath.Replace("*", "");
                if (!generatedPaths.Any(p => p.EndsWith(pattern, StringComparison.OrdinalIgnoreCase)))
                {
                    findings.Add(new VerificationFinding
                    {
                        Category = "completeness",
                        Severity = "error",
                        FilePath = expected.RelativePath,
                        Description = $"Required file matching '{expected.RelativePath}' is missing: {expected.Description}",
                        SuggestedFix = $"Add a file matching '{expected.RelativePath}' to the project."
                    });
                }
                continue;
            }

            if (!generatedPaths.Contains(expectedPath))
            {
                findings.Add(new VerificationFinding
                {
                    Category = "completeness",
                    Severity = "error",
                    FilePath = expected.RelativePath,
                    Description = $"Required file '{expected.RelativePath}' is missing: {expected.Description}",
                    SuggestedFix = $"Generate the file '{expected.RelativePath}' with the expected content."
                });
            }
        }
    }

    /// <summary>Check that generated files contain expected content markers from the blueprint.</summary>
    private static void ValidateContentMarkers(List<GeneratedFile> files, ProjectBlueprint blueprint, List<VerificationFinding> findings)
    {
        foreach (var expected in blueprint.FileManifest)
        {
            if (expected.ContentMarkers.Count == 0) continue;

            var matchingFile = files.FirstOrDefault(f =>
                NormalizePath(f.RelativePath).Equals(NormalizePath(expected.RelativePath), StringComparison.OrdinalIgnoreCase));

            if (matchingFile == null) continue; // Missing files are caught by ValidateRequiredFiles

            foreach (var marker in expected.ContentMarkers)
            {
                if (!matchingFile.Content.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new VerificationFinding
                    {
                        Category = "completeness",
                        Severity = expected.Required ? "error" : "warning",
                        FilePath = expected.RelativePath,
                        Description = $"File '{expected.RelativePath}' is missing expected content marker: '{marker}'",
                        SuggestedFix = $"Ensure the file contains '{marker}' as expected for this framework."
                    });
                }
            }
        }
    }

    /// <summary>Check that the output follows expected folder conventions.</summary>
    private static void ValidateFolderConventions(List<GeneratedFile> files, ProjectBlueprint blueprint, List<VerificationFinding> findings)
    {
        if (blueprint.ExpectedFolders.Count == 0) return;

        var usedFolders = files
            .Select(f => NormalizePath(System.IO.Path.GetDirectoryName(f.RelativePath) ?? ""))
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.TrimEnd('/') + "/")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expectedFolder in blueprint.ExpectedFolders)
        {
            var normalizedFolder = NormalizePath(expectedFolder).TrimEnd('/') + "/";
            if (!usedFolders.Any(f => f.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new VerificationFinding
                {
                    Category = "completeness",
                    Severity = "warning",
                    FilePath = expectedFolder,
                    Description = $"Expected folder convention '{expectedFolder}' has no files. This framework typically has files in this directory.",
                    SuggestedFix = $"Consider adding files under '{expectedFolder}' following the framework convention."
                });
            }
        }
    }

    /// <summary>Check that the project has at least the minimum expected number of files.</summary>
    private static void ValidateMinimumFileCount(List<GeneratedFile> files, ProjectBlueprint blueprint, List<VerificationFinding> findings)
    {
        if (blueprint.EstimatedFileCount <= 1) return;

        var minExpected = blueprint.FileManifest.Count(f => f.Required);
        if (minExpected <= 0) minExpected = 2; // At least config + entry point

        if (files.Count < minExpected)
        {
            findings.Add(new VerificationFinding
            {
                Category = "completeness",
                Severity = "error",
                FilePath = "",
                Description = $"Project has only {files.Count} file(s), but this framework type requires at least {minExpected} files.",
                SuggestedFix = $"Generate the missing required files for this project type."
            });
        }
    }

    /// <summary>Check blueprint acceptance criteria against generated content.</summary>
    private static void ValidateAcceptanceCriteria(List<GeneratedFile> files, ProjectBlueprint blueprint, List<VerificationFinding> findings)
    {
        // We can only do simple heuristic checks here — deep semantic checks are done by the AI verifier.
        // This layer focuses on structural / presence checks.
        var allContent = string.Join("\n", files.Select(f => f.Content));

        foreach (var criteria in blueprint.AcceptanceCriteria)
        {
            // Extract key terms from the criteria for a simple content check
            var lowerCriteria = criteria.ToLowerInvariant();

            // Check "must have/include/define X" patterns
            if (lowerCriteria.Contains("must") && lowerCriteria.Contains("import"))
            {
                // Extract the import target
                var importIdx = lowerCriteria.IndexOf("import", StringComparison.OrdinalIgnoreCase);
                if (importIdx >= 0)
                {
                    var afterImport = criteria[(importIdx + 6)..].Trim().Trim('\'', '"', '`');
                    if (afterImport.Length > 2 && !allContent.Contains(afterImport, StringComparison.OrdinalIgnoreCase))
                    {
                        findings.Add(new VerificationFinding
                        {
                            Category = "completeness",
                            Severity = "warning",
                            FilePath = "",
                            Description = $"Acceptance criteria may not be met: '{criteria}'",
                            SuggestedFix = "Review the generated code to ensure this criteria is satisfied."
                        });
                    }
                }
            }
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }
}
