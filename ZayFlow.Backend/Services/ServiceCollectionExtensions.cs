using Microsoft.Extensions.DependencyInjection;
using ZayFlow.Backend.Contracts;
using ZayFlow.Backend.Persistence;

namespace ZayFlow.Backend.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddZayFlowLocalBackend(this IServiceCollection services)
    {
        // Core persistence stores
        services.AddSingleton<ITokenStore, JsonTokenStore>();
        services.AddSingleton<IQueuedPlanStore, JsonQueuedPlanStore>();
        services.AddSingleton<IExecutionAuditStore, JsonExecutionAuditStore>();
        services.AddSingleton<ISubscriptionProvider, LocalSubscriptionProvider>();
        services.AddSingleton<JsonBackendStateStore>();

        // Step 7: Offline Intelligence Layer
        services.AddSingleton<ITaskMemoryStore, JsonTaskMemoryStore>();
        services.AddSingleton<IPathSafetyValidator, PathSafetyValidator>();
        services.AddSingleton<IIntentDetector, IntentDetector>();
        services.AddSingleton<IHeuristicRuleEngine, HeuristicRuleEngine>();
        services.AddSingleton<ITaskMemoryManager, TaskMemoryManager>();
        services.AddSingleton<ISuggestionService, SuggestionService>();

        // Token management
        services.AddSingleton<LocalTokenService>();

        // Step 6: Planner infrastructure
        // Planners (offline-first, LLM-ready)
        services.AddSingleton<RuleBasedPlannerBackend>();
        services.AddSingleton<LLMPlannerBackend>();

        // Planner strategy provider (selects which planner to use)
        services.AddSingleton<PlannerStrategyProvider>();

        // Planner action logger (structured logging for audit trail)
        services.AddSingleton<PlannerActionLogger>();

        // Main backend service orchestrator
        services.AddSingleton<ILocalBackendService, LocalBackendService>();

        // Backend AI Configuration (API key management)
        services.AddSingleton<IBackendAIConfiguration, BackendAIConfigurationService>();

        return services;
    }
}
