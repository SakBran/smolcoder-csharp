using Smolcoder.Domain.Modes;

namespace Smolcoder.Application.Sessions;

public interface IAgentSessionBuilder
{
    AgentSessionParts Build(AgentSessionRequest request);

    /// <summary>
    /// The system prompt for a workspace and mode, AGENTS.md included.
    /// </summary>
    string SystemPrompt(string workspace, PermissionMode mode);

    /// <summary>
    /// How many tokens a model's reply may use, based on its window.
    /// </summary>
    int OutputBudget(int contextWindow);
}
