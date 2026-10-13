namespace Smolcoder.Application.Agents;

public interface IAgentFactory
{
    IAgentService Create(AgentCreationOptions options);
}
