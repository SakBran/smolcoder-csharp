using Smolcoder.Application.Abstractions.Interaction;
using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Providers;

namespace Smolcoder.Application.Sessions;

public sealed class InteractiveSessionFactory : IInteractiveSessionFactory
{
    private readonly IAgentSessionBuilder sessionBuilder;
    private readonly IModelCatalog modelCatalog;
    private readonly ISettingsStore settingsStore;

    public InteractiveSessionFactory(IAgentSessionBuilder sessionBuilder, IModelCatalog modelCatalog, ISettingsStore settingsStore)
    {
        this.sessionBuilder = sessionBuilder;
        this.modelCatalog = modelCatalog;
        this.settingsStore = settingsStore;
    }

    public InteractiveSession Create(ISessionInteraction ui, string workspace, ModelDescriptor chosen, SessionPreferences preferences, AppSettings settings)
    {
        return new InteractiveSession(ui, workspace, chosen, preferences, settings, sessionBuilder, modelCatalog, settingsStore);
    }
}
