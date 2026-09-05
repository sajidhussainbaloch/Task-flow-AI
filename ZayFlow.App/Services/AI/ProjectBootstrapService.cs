using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ZayFlow.App.Services.AI;

public sealed class ProjectBootstrapService
{
    private readonly ILogger<ProjectBootstrapService> _logger;

    public ProjectBootstrapService(ILogger<ProjectBootstrapService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ProjectBootstrapPlan Analyze(string projectFolder, IReadOnlyList<ProjectFileSpec> files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(files);

        var normalizedFiles = files
            .Where(file => !string.IsNullOrWhiteSpace(file.FileName))
            .ToList();

        bool hasPubspec = normalizedFiles.Any(file => string.Equals(Path.GetFileName(file.FileName), "pubspec.yaml", StringComparison.OrdinalIgnoreCase));
        bool hasDart = normalizedFiles.Any(file => file.FileName.EndsWith(".dart", StringComparison.OrdinalIgnoreCase));
        bool isFlutter = hasPubspec && normalizedFiles.Any(file => file.Content.Contains("flutter:", StringComparison.OrdinalIgnoreCase) || hasDart);
        if (isFlutter)
        {
            return new ProjectBootstrapPlan
            {
                FrameworkId = "flutter",
                DisplayName = "Flutter",
                ProjectFolder = projectFolder,
                ToolCommand = "flutter",
                BootstrapCommand = "flutter create .",
                BootstrapShell = "powershell",
                RequiredProjectFiles = new List<string> { "pubspec.yaml", "lib/main.dart" },
                ExpectedArtifacts = new List<string> { "pubspec.lock", ".dart_tool/package_config.json", "windows", "android", "web" },
                VerificationNote = "Flutter projects need generated platform folders as well as resolved packages."
            };
        }

        bool hasPackageJson = normalizedFiles.Any(file => string.Equals(Path.GetFileName(file.FileName), "package.json", StringComparison.OrdinalIgnoreCase));
        if (hasPackageJson)
        {
            var packageJson = normalizedFiles.FirstOrDefault(file => string.Equals(Path.GetFileName(file.FileName), "package.json", StringComparison.OrdinalIgnoreCase))?.Content ?? string.Empty;
            var hasYarnLock = normalizedFiles.Any(file => string.Equals(Path.GetFileName(file.FileName), "yarn.lock", StringComparison.OrdinalIgnoreCase));
            var hasPnpmLock = normalizedFiles.Any(file => string.Equals(Path.GetFileName(file.FileName), "pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase));
            var hasBunLock = normalizedFiles.Any(file => string.Equals(Path.GetFileName(file.FileName), "bun.lockb", StringComparison.OrdinalIgnoreCase));
            var packageManager = packageJson.Contains("\"packageManager\": \"pnpm", StringComparison.OrdinalIgnoreCase) ? "pnpm"
                : packageJson.Contains("\"packageManager\": \"yarn", StringComparison.OrdinalIgnoreCase) ? "yarn"
                : packageJson.Contains("\"packageManager\": \"bun", StringComparison.OrdinalIgnoreCase) ? "bun"
                : hasPnpmLock ? "pnpm"
                : hasYarnLock ? "yarn"
                : hasBunLock ? "bun"
                : "npm";

            var bootstrapCommand = packageManager switch
            {
                "pnpm" => "pnpm install",
                "yarn" => "yarn install",
                "bun" => "bun install",
                _ => "npm install"
            };

            return new ProjectBootstrapPlan
            {
                FrameworkId = "node",
                DisplayName = "Node / Web",
                ProjectFolder = projectFolder,
                ToolCommand = packageManager,
                BootstrapCommand = bootstrapCommand,
                BootstrapShell = "powershell",
                ExpectedArtifacts = new List<string> { "node_modules" },
                RequiredProjectFiles = new List<string> { "package.json" },
                VerificationNote = "Dependency installation is verified through the local node_modules directory."
            };
        }

        bool hasDotnetProject = normalizedFiles.Any(file =>
            file.FileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            file.FileName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase));
        if (hasDotnetProject)
        {
            return new ProjectBootstrapPlan
            {
                FrameworkId = "dotnet",
                DisplayName = ".NET",
                ProjectFolder = projectFolder,
                ToolCommand = "dotnet",
                BootstrapCommand = "dotnet restore",
                BootstrapShell = "powershell",
                ExpectedArtifacts = new List<string> { "**/project.assets.json" },
                RequiredProjectFiles = new List<string> { "**/*.csproj" },
                VerificationNote = "A .NET restore is considered ready when NuGet assets are generated."
            };
        }

        bool hasRequirementsTxt = normalizedFiles.Any(file => string.Equals(Path.GetFileName(file.FileName), "requirements.txt", StringComparison.OrdinalIgnoreCase));
        bool hasPyproject = normalizedFiles.Any(file => string.Equals(Path.GetFileName(file.FileName), "pyproject.toml", StringComparison.OrdinalIgnoreCase));
        if (hasRequirementsTxt || hasPyproject)
        {
            var bootstrapCommand = hasRequirementsTxt
                ? "python -m pip install -r requirements.txt"
                : "python -m pip install -e .";

            return new ProjectBootstrapPlan
            {
                FrameworkId = "python",
                DisplayName = "Python",
                ProjectFolder = projectFolder,
                ToolCommand = "python",
                BootstrapCommand = bootstrapCommand,
                BootstrapShell = "powershell",
                RequiredProjectFiles = hasRequirementsTxt
                    ? new List<string> { "requirements.txt" }
                    : new List<string> { "pyproject.toml" },
                VerificationNote = "Python dependency installation has no reliable local artifact by default, so success is based on command completion."
            };
        }

        bool hasCargoToml = normalizedFiles.Any(file => string.Equals(Path.GetFileName(file.FileName), "Cargo.toml", StringComparison.OrdinalIgnoreCase));
        if (hasCargoToml)
        {
            return new ProjectBootstrapPlan
            {
                FrameworkId = "rust",
                DisplayName = "Rust",
                ProjectFolder = projectFolder,
                ToolCommand = "cargo",
                BootstrapCommand = "cargo fetch",
                BootstrapShell = "powershell",
                ExpectedArtifacts = new List<string> { "Cargo.lock" },
                RequiredProjectFiles = new List<string> { "Cargo.toml" },
                VerificationNote = "Rust bootstrap is verified by dependency resolution and a generated Cargo.lock file."
            };
        }

        bool hasGoMod = normalizedFiles.Any(file => string.Equals(Path.GetFileName(file.FileName), "go.mod", StringComparison.OrdinalIgnoreCase));
        if (hasGoMod)
        {
            return new ProjectBootstrapPlan
            {
                FrameworkId = "go",
                DisplayName = "Go",
                ProjectFolder = projectFolder,
                ToolCommand = "go",
                BootstrapCommand = "go mod tidy",
                BootstrapShell = "powershell",
                ExpectedArtifacts = new List<string> { "go.sum" },
                RequiredProjectFiles = new List<string> { "go.mod" },
                VerificationNote = "Go module readiness is verified by a resolved go.sum file."
            };
        }

        return ProjectBootstrapPlan.None(projectFolder);
    }

    public async Task<ProjectToolCheckResult> CheckToolAsync(ProjectBootstrapPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.RequiresBootstrap || string.IsNullOrWhiteSpace(plan.ToolCommand))
        {
            return new ProjectToolCheckResult { IsAvailable = true };
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = plan.ToolCommand,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);

            if (process.ExitCode == 0)
            {
                var firstMatch = output
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault() ?? string.Empty;

                return new ProjectToolCheckResult
                {
                    IsAvailable = true,
                    ResolvedPath = firstMatch
                };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool detection failed for {ToolCommand}", plan.ToolCommand);
        }

        return new ProjectToolCheckResult
        {
            IsAvailable = false,
            FailureReason = $"{plan.DisplayName} tooling was not found in PATH. Install `{plan.ToolCommand}` and rerun bootstrap."
        };
    }

    public ProjectBootstrapValidationResult Validate(string projectFolder, ProjectBootstrapPlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(plan);

        var result = new ProjectBootstrapValidationResult();
        result.MissingRequiredFiles.AddRange(GetMissingArtifacts(projectFolder, plan.RequiredProjectFiles));
        result.MissingArtifacts.AddRange(GetMissingArtifacts(projectFolder, plan.ExpectedArtifacts));
        return result;
    }

    public ProjectBootstrapValidationResult ValidateExpectedArtifacts(string projectFolder, IEnumerable<string> expectedArtifacts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(expectedArtifacts);

        return new ProjectBootstrapValidationResult
        {
            MissingArtifacts = GetMissingArtifacts(projectFolder, expectedArtifacts)
        };
    }

    public BootstrapFailureAnalysis AnalyzeFailure(ProjectBootstrapPlan plan, string command, string output, string error, int exitCode, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var combined = $"{output}\n{error}".Trim();
        var analysis = new BootstrapFailureAnalysis
        {
            Summary = $"Bootstrap failed for {plan.DisplayName}.",
            CombinedOutput = combined
        };

        if (string.IsNullOrWhiteSpace(combined))
        {
            return analysis;
        }

        if (Regex.IsMatch(combined, @"not recognized|command not found|No such file or directory", RegexOptions.IgnoreCase))
        {
            analysis.Summary = $"{plan.DisplayName} tooling is not available on this machine.";
            return analysis;
        }

        if (Regex.IsMatch(combined, @"permission denied|access is denied|EPERM|EACCES", RegexOptions.IgnoreCase))
        {
            analysis.Summary = "Bootstrap failed because the command did not have permission to write or update files.";
            return analysis;
        }

        if (Regex.IsMatch(combined, @"timed out|ETIMEDOUT|ECONNRESET|ENOTFOUND|socket|temporary failure|503|502|unable to load|unable to download|connection", RegexOptions.IgnoreCase))
        {
            analysis.Summary = "Bootstrap looks like a transient network or timeout failure.";
            analysis.CanRetry = true;
            analysis.RetryCommand = command;
            analysis.RetryShell = plan.BootstrapShell;
            analysis.TimeoutSeconds = Math.Max(plan.GetDefaultTimeoutSeconds() + 30, plan.GetDefaultTimeoutSeconds());
            analysis.RecoveryReason = "Retry bootstrap after a transient network or timeout failure.";
            return analysis;
        }

        if (plan.FrameworkId.Equals("flutter", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(combined, @"package_config|pub cache|lockfile|\.dart_tool|corrupt|Invalid kernel binary", RegexOptions.IgnoreCase))
        {
            analysis.Summary = "Flutter bootstrap appears to have stale, incomplete, or corrupted generated artifacts.";
            analysis.CanRetry = true;
            analysis.RetryCommand = "flutter create .";
            analysis.RetryShell = plan.BootstrapShell;
            analysis.TimeoutSeconds = 180;
            analysis.RecoveryReason = "Regenerate Flutter scaffold files and platforms in the existing project folder.";
            return analysis;
        }

        if (plan.FrameworkId.Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(combined, @"project.assets.json|restore failed|NU1301|NU1101|Unable to load the service index", RegexOptions.IgnoreCase))
        {
            analysis.Summary = ".NET restore failed while resolving packages.";
            analysis.CanRetry = true;
            analysis.RetryCommand = "dotnet restore --force";
            analysis.RetryShell = plan.BootstrapShell;
            analysis.TimeoutSeconds = 150;
            analysis.RecoveryReason = "Retry restore with forced dependency resolution.";
            return analysis;
        }

        if (plan.FrameworkId.Equals("node", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(combined, @"package-lock|node_modules|npm ERR! tracker|cache|idealTree", RegexOptions.IgnoreCase))
        {
            analysis.Summary = "Node dependency installation failed while rebuilding local package state.";
            analysis.CanRetry = true;
            analysis.RetryCommand = command;
            analysis.RetryShell = plan.BootstrapShell;
            analysis.TimeoutSeconds = 150;
            analysis.RecoveryReason = "Retry dependency installation after a package-state failure.";
            return analysis;
        }

        if (Regex.IsMatch(combined, @"version solving failed|depends on|conflict|could not resolve", RegexOptions.IgnoreCase))
        {
            analysis.Summary = "Bootstrap hit a dependency conflict in the generated project definition.";
            return analysis;
        }

        if (exitCode != 0)
        {
            analysis.Summary = $"Bootstrap command exited with code {exitCode}.";
        }

        return analysis;
    }

    public BootstrapFailureAnalysis AnalyzeVerificationFailure(ProjectBootstrapPlan plan, string command, ProjectBootstrapValidationResult validation)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(validation);

        var analysis = new BootstrapFailureAnalysis
        {
            Summary = $"Bootstrap finished but {plan.DisplayName} verification is incomplete.",
            MissingArtifacts = validation.MissingArtifacts.ToList()
        };

        if (!validation.HasRequiredStructure)
        {
            analysis.Summary = $"The generated {plan.DisplayName} project is missing required files: {string.Join(", ", validation.MissingRequiredFiles)}";
            return analysis;
        }

        if (validation.MissingArtifacts.Count == 0)
            return analysis;

        analysis.CanRetry = true;
        analysis.RetryShell = plan.BootstrapShell;
        analysis.TimeoutSeconds = plan.GetDefaultTimeoutSeconds();
        analysis.RecoveryReason = "Retry bootstrap because expected verification artifacts were not produced.";
        analysis.RetryCommand = plan.FrameworkId switch
        {
            "flutter" => "flutter create .",
            "dotnet" => "dotnet restore --force",
            _ => command
        };

        return analysis;
    }

    private static List<string> GetMissingArtifacts(string projectFolder, IEnumerable<string> artifacts)
    {
        var missing = new List<string>();
        foreach (var artifact in artifacts.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            if (!ArtifactExists(projectFolder, artifact))
                missing.Add(artifact);
        }

        return missing;
    }

    private static bool ArtifactExists(string projectFolder, string artifact)
    {
        if (artifact.StartsWith("**/", StringComparison.Ordinal))
        {
            var fileName = artifact[3..];
            return Directory.EnumerateFiles(projectFolder, Path.GetFileName(fileName), SearchOption.AllDirectories).Any();
        }

        var normalizedPath = artifact.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(projectFolder, normalizedPath);
        return File.Exists(fullPath) || Directory.Exists(fullPath);
    }
}

public sealed class ProjectFileSpec
{
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
}

public sealed class ProjectBootstrapPlan
{
    public string FrameworkId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ProjectFolder { get; set; } = string.Empty;
    public string ToolCommand { get; set; } = string.Empty;
    public string BootstrapCommand { get; set; } = string.Empty;
    public string BootstrapShell { get; set; } = "powershell";
    public List<string> RequiredProjectFiles { get; set; } = new();
    public List<string> ExpectedArtifacts { get; set; } = new();
    public string VerificationNote { get; set; } = string.Empty;
    public bool RequiresBootstrap => !string.IsNullOrWhiteSpace(BootstrapCommand);

    public static ProjectBootstrapPlan None(string projectFolder) => new()
    {
        ProjectFolder = projectFolder
    };

    public int GetDefaultTimeoutSeconds() => FrameworkId switch
    {
        "flutter" => 180,
        "node" => 120,
        "dotnet" => 90,
        "python" => 90,
        "rust" => 90,
        "go" => 90,
        _ => 60
    };

    public Dictionary<string, object> ToCommandParameters(string? overrideCommand = null, int? overrideTimeoutSeconds = null, string? recoveryReason = null)
    {
        var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["command"] = string.IsNullOrWhiteSpace(overrideCommand) ? BootstrapCommand : overrideCommand,
            ["shell"] = BootstrapShell,
            ["workingDirectory"] = ProjectFolder,
            ["timeoutSeconds"] = overrideTimeoutSeconds ?? GetDefaultTimeoutSeconds(),
            ["frameworkId"] = FrameworkId,
            ["expectedArtifacts"] = ExpectedArtifacts.Cast<object>().ToList(),
            ["verificationNote"] = VerificationNote
        };

        if (!string.IsNullOrWhiteSpace(recoveryReason))
            parameters["recoveryReason"] = recoveryReason;

        return parameters;
    }
}

public sealed class ProjectToolCheckResult
{
    public bool IsAvailable { get; set; }
    public string ResolvedPath { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
}

public sealed class ProjectBootstrapValidationResult
{
    public List<string> MissingRequiredFiles { get; set; } = new();
    public List<string> MissingArtifacts { get; set; } = new();
    public bool HasRequiredStructure => MissingRequiredFiles.Count == 0;
    public bool HasExpectedArtifacts => MissingArtifacts.Count == 0;
    public bool IsReady => HasRequiredStructure && HasExpectedArtifacts;
}

public sealed class BootstrapFailureAnalysis
{
    public bool CanRetry { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string RetryCommand { get; set; } = string.Empty;
    public string RetryShell { get; set; } = "powershell";
    public int TimeoutSeconds { get; set; } = 90;
    public string RecoveryReason { get; set; } = string.Empty;
    public string CombinedOutput { get; set; } = string.Empty;
    public List<string> MissingArtifacts { get; set; } = new();
}