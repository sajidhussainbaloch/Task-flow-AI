using ZayFlow.App.Services.CodeGeneration.Contracts;

namespace ZayFlow.App.Services.CodeGeneration.Verification;

public sealed class CodeGenerationScenarioDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Prompt { get; init; } = string.Empty;
    public string ExpectedShape { get; init; } = string.Empty;
}

public sealed class CodeGenerationScenarioValidator
{
    public IReadOnlyList<CodeGenerationScenarioDefinition> GetPhaseOneScenarios() =>
    [
        new() { Name = "Single-file CLI", Prompt = "Create a Python calculator", ExpectedShape = "single-file interactive CLI" },
        new() { Name = "Single-file GUI", Prompt = "Create a Python tkinter calculator", ExpectedShape = "single-file interactive GUI" },
        new() { Name = "Multi-file generation", Prompt = "Create a Flutter todo app", ExpectedShape = "multi-file project" },
        new() { Name = "Ambiguous request", Prompt = "Make me an app", ExpectedShape = "blocked or assumption-heavy brief" },
        new() { Name = "Repair loop", Prompt = "Generate code with intentional import issue and recover", ExpectedShape = "verification and improvement loop" }
    ];

    public List<string> ValidateSession(CodeGenerationSession session)
    {
        var notes = new List<string>();

        if (session.Brief == null)
            notes.Add("Missing normalized brief.");

        if (session.Plan == null)
            notes.Add("Missing implementation plan.");

        if (session.Telemetry.Events.Count == 0)
            notes.Add("No stage events were emitted.");

        if (session.Telemetry.StageDurationsMs.Count == 0)
            notes.Add("No stage durations were recorded.");

        if (session.GeneratedFiles.Count == 0 && session.FinalOutput?.Success == true)
            notes.Add("Successful output reported without generated files.");

        if (session.VerificationHistory.Count == 0 && session.FinalOutput?.Success == true)
            notes.Add("Successful output reported without verification history.");

        return notes;
    }
}
