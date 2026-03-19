using Microsoft.Extensions.Logging;
using ZayFlow.Backend.Contracts;

namespace ZayFlow.Backend.Services;

/// <summary>
/// Planner strategy selector. Chooses between RuleBasedPlanner and LLMPlanner
/// based on configuration and availability. Supports hot-swapping planners
/// for future AI integration without changing frontend code.
/// </summary>
public sealed class PlannerStrategyProvider
{
    private readonly ILogger<PlannerStrategyProvider> _logger;
    private readonly RuleBasedPlannerBackend _ruleBasedPlanner;
    private readonly LLMPlannerBackend _llmPlanner;
    
    // Configuration: can be toggled at runtime
    private bool _useLLMIfAvailable = false;

    public PlannerStrategyProvider(
        ILogger<PlannerStrategyProvider> logger,
        RuleBasedPlannerBackend ruleBasedPlanner,
        LLMPlannerBackend llmPlanner)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ruleBasedPlanner = ruleBasedPlanner ?? throw new ArgumentNullException(nameof(ruleBasedPlanner));
        _llmPlanner = llmPlanner ?? throw new ArgumentNullException(nameof(llmPlanner));
    }

    /// <summary>
    /// Get the active planner strategy.
    /// Returns LLM planner if enabled, otherwise returns offline rule-based planner.
    /// </summary>
    public IPlanner GetActivePlanner()
    {
        if (_useLLMIfAvailable)
        {
            _logger.LogDebug("Using LLMPlanner strategy");
            return _llmPlanner;
        }

        _logger.LogDebug("Using RuleBasedPlanner strategy");
        return _ruleBasedPlanner;
    }

    /// <summary>
    /// Enable or disable LLM planner usage.
    /// When disabled, always uses offline RuleBasedPlanner.
    /// </summary>
    public void SetUseLLMPlanner(bool enabled)
    {
        _useLLMIfAvailable = enabled;
        _logger.LogInformation("LLM planner enabled: {Enabled}", enabled);
    }

    /// <summary>
    /// Check if LLM planner is currently enabled.
    /// </summary>
    public bool IsLLMPlannerEnabled => _useLLMIfAvailable;

    /// <summary>
    /// Get details about available planners for UI/debugging.
    /// </summary>
    public PlannerStrategyInfo GetStrategyInfo()
    {
        return new PlannerStrategyInfo
        {
            ActiveStrategy = _useLLMIfAvailable ? "LLM (AI-based)" : "RuleBasedPlanner (Offline)",
            AvailableStrategies = new[] { "RuleBasedPlanner (Offline)", "LLMPlanner (AI - not yet implemented)" },
            OfflineMode = !_useLLMIfAvailable,
            Timestamp = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Information about active planner strategy for logging and UI.
/// </summary>
public sealed class PlannerStrategyInfo
{
    public string ActiveStrategy { get; set; } = string.Empty;
    public string[] AvailableStrategies { get; set; } = Array.Empty<string>();
    public bool OfflineMode { get; set; }
    public DateTime Timestamp { get; set; }

    public override string ToString() =>
        $"Active: {ActiveStrategy} | Offline: {OfflineMode} | Available: {string.Join(", ", AvailableStrategies)}";
}
