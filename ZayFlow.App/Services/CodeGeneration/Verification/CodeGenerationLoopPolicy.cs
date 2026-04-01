using ZayFlow.App.Services.CodeGeneration.Contracts;

namespace ZayFlow.App.Services.CodeGeneration.Verification;

public sealed class CodeGenerationLoopPolicyOptions
{
    public int MaxIterations { get; set; } = 3;
    public double ConfidenceThreshold { get; set; } = 0.85;
    public bool StopOnRepeatedErrors { get; set; } = true;
}

public sealed class CodeGenerationLoopDecision
{
    public bool AcceptOutput { get; init; }
    public bool StopLoop { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class CodeGenerationLoopPolicy
{
    private readonly CodeGenerationLoopPolicyOptions _options;

    public CodeGenerationLoopPolicy(CodeGenerationLoopPolicyOptions? options = null)
    {
        _options = options ?? new CodeGenerationLoopPolicyOptions();
    }

    public CodeGenerationLoopDecision Evaluate(
        VerificationReport currentReport,
        IReadOnlyList<VerificationReport> history,
        int iteration)
    {
        if (currentReport.Passed && currentReport.ConfidenceScore >= _options.ConfidenceThreshold)
        {
            return new CodeGenerationLoopDecision
            {
                AcceptOutput = true,
                StopLoop = true,
                Reason = $"Passed quality gate at confidence {currentReport.ConfidenceScore:F2}"
            };
        }

        if (iteration >= _options.MaxIterations)
        {
            return new CodeGenerationLoopDecision
            {
                AcceptOutput = false,
                StopLoop = true,
                Reason = $"Reached max iterations ({_options.MaxIterations})"
            };
        }

        if (_options.StopOnRepeatedErrors && history.Count >= 2 && AreFindingsRepeating(history[^2], currentReport))
        {
            return new CodeGenerationLoopDecision
            {
                AcceptOutput = false,
                StopLoop = true,
                Reason = "Repeated error findings detected"
            };
        }

        return new CodeGenerationLoopDecision
        {
            AcceptOutput = false,
            StopLoop = false,
            Reason = "Run targeted improvement"
        };
    }

    private static bool AreFindingsRepeating(VerificationReport previous, VerificationReport current)
    {
        var prevErrors = previous.Findings
            .Where(f => string.Equals(f.Severity, "error", StringComparison.OrdinalIgnoreCase))
            .Select(f => $"{f.FilePath}|{f.Description}")
            .OrderBy(v => v)
            .ToList();

        var currentErrors = current.Findings
            .Where(f => string.Equals(f.Severity, "error", StringComparison.OrdinalIgnoreCase))
            .Select(f => $"{f.FilePath}|{f.Description}")
            .OrderBy(v => v)
            .ToList();

        return prevErrors.SequenceEqual(currentErrors);
    }
}
