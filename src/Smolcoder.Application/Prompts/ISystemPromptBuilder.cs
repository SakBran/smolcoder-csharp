using Smolcoder.Domain.Modes;

namespace Smolcoder.Application.Prompts;

public interface ISystemPromptBuilder
{
    string Build(string workspace, PermissionMode mode, string shellLabel, string? agentsMd);
}
