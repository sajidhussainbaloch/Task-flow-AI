using ZayFlow.App.Services.CodeGeneration.Templates;

namespace ZayFlow.App.Services.CodeGeneration.Intelligence;

/// <summary>
/// The result of project intelligence analysis.
/// Describes what the generated project should look like — file manifest,
/// dependencies, build system, and structural expectations.
/// Works for both known frameworks (via FrameworkTemplate) and unknown/custom projects (via AI analysis).
/// </summary>
public sealed class ProjectBlueprint
{
    /// <summary>Whether a known framework was detected.</summary>
    public bool IsKnownFramework { get; set; }

    /// <summary>The matched framework template, or null for unknown project types.</summary>
    public FrameworkTemplate? Framework { get; set; }

    /// <summary>Detected project type description (e.g. "Flutter mobile app", "Custom CLI tool").</summary>
    public string ProjectType { get; set; } = string.Empty;

    /// <summary>Primary programming language.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Complexity tier: single-file, multi-file, full-project.</summary>
    public ProjectComplexity Complexity { get; set; } = ProjectComplexity.SingleFile;

    /// <summary>Complete file manifest — every file the project should contain.</summary>
    public List<BlueprintFile> FileManifest { get; set; } = [];

    /// <summary>Folder structure the project should follow.</summary>
    public List<string> ExpectedFolders { get; set; } = [];

    /// <summary>External package dependencies the project needs.</summary>
    public List<string> Dependencies { get; set; } = [];

    /// <summary>Acceptance criteria derived from template + user request.</summary>
    public List<string> AcceptanceCriteria { get; set; } = [];

    /// <summary>Verification goals for the verification layer.</summary>
    public List<string> VerificationGoals { get; set; } = [];

    /// <summary>Bootstrap command to run after file creation.</summary>
    public string BootstrapCommand { get; set; } = string.Empty;

    /// <summary>Run command.</summary>
    public string RunCommand { get; set; } = string.Empty;

    /// <summary>Test command.</summary>
    public string TestCommand { get; set; } = string.Empty;

    /// <summary>Build command.</summary>
    public string BuildCommand { get; set; } = string.Empty;

    /// <summary>Human-readable bootstrap hint for the user.</summary>
    public string BootstrapHint { get; set; } = string.Empty;

    /// <summary>Estimated total file count for this project type.</summary>
    public int EstimatedFileCount { get; set; } = 1;

    /// <summary>Creates a blueprint for a simple single-file project (no framework).</summary>
    public static ProjectBlueprint SingleFile(string language, string fileName)
    {
        return new ProjectBlueprint
        {
            IsKnownFramework = false,
            ProjectType = $"Single {language} file",
            Language = language,
            Complexity = ProjectComplexity.SingleFile,
            EstimatedFileCount = 1,
            FileManifest =
            [
                new BlueprintFile
                {
                    RelativePath = fileName,
                    Description = "Main source file",
                    Required = true,
                    Language = language
                }
            ]
        };
    }

    /// <summary>Creates a blueprint from a known framework template.</summary>
    public static ProjectBlueprint FromTemplate(FrameworkTemplate template, string projectName)
    {
        var blueprint = new ProjectBlueprint
        {
            IsKnownFramework = true,
            Framework = template,
            ProjectType = template.DisplayName,
            Language = template.PrimaryLanguage,
            Complexity = ProjectComplexity.FullProject,
            ExpectedFolders = [.. template.FolderConventions],
            AcceptanceCriteria = [.. template.AcceptanceCriteria],
            VerificationGoals = [.. template.VerificationGoals],
            BootstrapCommand = template.BootstrapCommand,
            RunCommand = template.RunCommand,
            TestCommand = template.TestCommand,
            BuildCommand = template.BuildCommand,
            BootstrapHint = template.BootstrapHint,
            EstimatedFileCount = template.RequiredFiles.Count + template.RecommendedFiles.Count
        };

        foreach (var tf in template.RequiredFiles)
        {
            blueprint.FileManifest.Add(new BlueprintFile
            {
                RelativePath = tf.RelativePath,
                Description = tf.Description,
                Required = true,
                Language = tf.Language,
                ContentMarkers = [.. tf.ContentMarkers]
            });
        }

        foreach (var tf in template.RecommendedFiles)
        {
            blueprint.FileManifest.Add(new BlueprintFile
            {
                RelativePath = tf.RelativePath,
                Description = tf.Description,
                Required = false,
                Language = tf.Language,
                ContentMarkers = [.. tf.ContentMarkers]
            });
        }

        return blueprint;
    }
}

/// <summary>
/// A single file in the project blueprint.
/// </summary>
public sealed class BlueprintFile
{
    /// <summary>Relative path from project root.</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>What this file does.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Whether this file is required (true) or recommended (false).</summary>
    public bool Required { get; set; }

    /// <summary>Programming language of this file.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Content markers that should appear in the generated file.</summary>
    public List<string> ContentMarkers { get; set; } = [];

    /// <summary>Files this file depends on (imports, references).</summary>
    public List<string> DependsOn { get; set; } = [];
}

/// <summary>
/// How complex the project is — determines generation strategy.
/// </summary>
public enum ProjectComplexity
{
    /// <summary>Everything fits in a single file.</summary>
    SingleFile,

    /// <summary>A few related files without full project scaffolding.</summary>
    MultiFile,

    /// <summary>Full framework project with config, source, tests, and build system.</summary>
    FullProject
}
