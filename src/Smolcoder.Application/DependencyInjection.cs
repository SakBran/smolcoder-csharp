using Microsoft.Extensions.DependencyInjection;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Application.Abstractions.Time;
using Smolcoder.Application.Agents;
using Smolcoder.Application.Prompts;
using Smolcoder.Application.Providers;
using Smolcoder.Application.Sessions;
using Smolcoder.Application.Time;
using Smolcoder.Application.Tools;

namespace Smolcoder.Application;

/// <summary>
/// Registers the application layer services.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IInferenceScheduler, InferenceScheduler>();
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<IToolDispatcher, ToolDispatcher>();
        services.AddSingleton<ISystemPromptBuilder, SystemPromptBuilder>();
        services.AddSingleton<IAgentFactory, AgentFactory>();
        services.AddSingleton<IModelSelector, ModelSelector>();
        services.AddSingleton<IAgentSessionBuilder, AgentSessionBuilder>();
        services.AddSingleton<IInteractiveSessionFactory, InteractiveSessionFactory>();
        return services;
    }
}
