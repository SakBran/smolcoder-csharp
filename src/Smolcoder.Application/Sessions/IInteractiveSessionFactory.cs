using Smolcoder.Application.Abstractions.Interaction;
using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Providers;

namespace Smolcoder.Application.Sessions;

public interface IInteractiveSessionFactory
{
    InteractiveSession Create(ISessionInteraction ui, string workspace, ModelDescriptor chosen, SessionPreferences preferences, AppSettings settings);
}
