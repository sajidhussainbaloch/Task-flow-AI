namespace ZayFlow.App.Services.CodeGeneration.Contracts;

/// <summary>Phase 2 — repo-aware editing strategy for generated code steps.</summary>
public enum RepoFileEditStrategy
{
    /// <summary>Target file does not exist; write a new file.</summary>
    CreateNew,

    /// <summary>Target file exists; replace the full content with the generated output.</summary>
    OverwriteExisting,

    /// <summary>Target file exists; apply targeted surgical edits using find-replace hunks.</summary>
    PatchExisting
}

/// <summary>
/// Snapshot of an existing file in the workspace that the code engine intends to edit.
/// Populated by the Understanding Layer when the user's request targets a known file.
/// </summary>
public sealed class ExistingFileContext
{
    /// <summary>Absolute or workspace-relative path of the existing file.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Full text content of the existing file.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Programming language of the existing file.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Total line count of the existing file.</summary>
    public int LineCount { get; set; }

    /// <summary>Last-modified timestamp on disk.</summary>
    public DateTime? LastModified { get; set; }

    /// <summary>Whether the file was successfully read from disk.</summary>
    public bool IsReadable { get; set; }
}

/// <summary>
/// A surgical patch hunk produced by the Generation Layer when strategy is PatchExisting.
/// Describes a single find → replace substitution to apply to the existing file.
/// </summary>
public sealed class CodePatchHunk
{
    /// <summary>Exact text to locate in the original file.</summary>
    public string Find { get; set; } = string.Empty;

    /// <summary>Replacement text that replaces the found region.</summary>
    public string Replace { get; set; } = string.Empty;

    /// <summary>Human-readable description of what this patch does.</summary>
    public string Description { get; set; } = string.Empty;
}
