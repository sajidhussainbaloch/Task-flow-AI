namespace ZayFlow.App.Services.CodeGeneration.Templates;

/// <summary>
/// Defines the structural blueprint for a known framework or project type.
/// Used by the planning, generation, and verification layers to ensure
/// generated projects have the correct file structure, content, and conventions.
/// </summary>
public sealed class FrameworkTemplate
{
    /// <summary>Unique identifier (e.g. "flutter", "react", "dotnet").</summary>
    public string FrameworkId { get; init; } = string.Empty;

    /// <summary>Human-readable name (e.g. "Flutter", "React (Create React App)").</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Primary language (e.g. "dart", "javascript", "csharp").</summary>
    public string PrimaryLanguage { get; init; } = string.Empty;

    /// <summary>Files that MUST exist for the project to be valid.</summary>
    public List<TemplateFile> RequiredFiles { get; init; } = [];

    /// <summary>Files that SHOULD exist for a useful starter project.</summary>
    public List<TemplateFile> RecommendedFiles { get; init; } = [];

    /// <summary>Expected folder structure (e.g. "lib/", "src/", "test/").</summary>
    public List<string> FolderConventions { get; init; } = [];

    /// <summary>Acceptance criteria the generated project must satisfy.</summary>
    public List<string> AcceptanceCriteria { get; init; } = [];

    /// <summary>What the verification layer should specifically check.</summary>
    public List<string> VerificationGoals { get; init; } = [];

    /// <summary>CLI command to bootstrap/hydrate the project after file creation.</summary>
    public string BootstrapCommand { get; init; } = string.Empty;

    /// <summary>CLI command to run the project.</summary>
    public string RunCommand { get; init; } = string.Empty;

    /// <summary>CLI command to run tests.</summary>
    public string TestCommand { get; init; } = string.Empty;

    /// <summary>CLI command to build the project.</summary>
    public string BuildCommand { get; init; } = string.Empty;

    /// <summary>Human-readable hint about bootstrap requirements.</summary>
    public string BootstrapHint { get; init; } = string.Empty;

    /// <summary>Keywords that trigger detection of this framework in user requests.</summary>
    public List<string> DetectionKeywords { get; init; } = [];

    /// <summary>Minimum number of files a valid project should have.</summary>
    public int MinimumFileCount { get; init; } = 1;
}

/// <summary>
/// A single file in a framework template with path, purpose, and content expectations.
/// </summary>
public sealed class TemplateFile
{
    /// <summary>Relative path from project root (e.g. "lib/main.dart", "package.json").</summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>Short description of what this file does.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Whether this file is required (true) or recommended (false).</summary>
    public bool Required { get; init; }

    /// <summary>
    /// Content markers that MUST appear in the generated file.
    /// E.g. for pubspec.yaml: ["flutter:", "name:"], for main.dart: ["runApp(", "MaterialApp"].
    /// </summary>
    public List<string> ContentMarkers { get; init; } = [];

    /// <summary>The programming language of this file (for syntax validation).</summary>
    public string Language { get; init; } = string.Empty;
}
