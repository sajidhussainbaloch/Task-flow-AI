using Microsoft.Extensions.Logging;
using ZayFlow.Backend.Contracts;
using ZayFlow.Backend.DTOs;

namespace ZayFlow.Backend.Services;

/// <summary>
/// Stub for future LLM-based planner.
/// When integrated with a VPS/AI service, this will:
/// - Send user input to remote LLM endpoint
/// - Parse LLM response into structured actions
/// - Fall back to RuleBasedPlanner if online unavailable
/// - Cache LLM responses locally for offline access
/// </summary>
public sealed class LLMPlannerBackend : IPlanner
{
    private readonly ILogger<LLMPlannerBackend> _logger;

    public LLMPlannerBackend(ILogger<LLMPlannerBackend> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generate plan using LLM. Currently not implemented.
    /// Falls back to RuleBasedPlanner while LLM integration is in progress.
    /// </summary>
    public async Task<PlanDTO> GeneratePlanAsync(string userInput, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("LLMPlanner.GeneratePlanAsync called with input: {Input}", userInput);

        await Task.Yield();
        throw new NotImplementedException("LLM planner is not implemented yet. Use offline RuleBased planner.");
    }
}
