using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Application.Context;
using Smolcoder.Domain.Attachments;
using Smolcoder.Domain.Messages;
using Smolcoder.Domain.Modes;

namespace Smolcoder.Application.Agents;

/// <summary>
/// One agent: one conversation with one model.
/// </summary>
public interface IAgentService
{
    List<Message> Messages { get; }

    IReadOnlyList<ToolSpec> Tools { get; }

    IModelProvider Provider { get; }

    PermissionMode Mode { get; }

    string OriginalRequest { get; }

    string CurrentRequest { get; }

    TurnStats? LastTurnStats { get; }

    AgentOutcome Outcome { get; }

    string? LastError { get; }

    VerificationResult? LastVerification { get; }

    void SetMode(PermissionMode mode, string systemPrompt);

    void SetProvider(IModelProvider provider);

    void ResetTranscript();

    void RestoreTranscript(List<Message> messages, string originalRequest, string currentRequest);

    void Cancel();

    int ContextPercent();

    ContextBudget ContextBudget();

    Task CompactNowAsync(bool force, bool deterministic);

    Task RunTurnAsync(string userInput, IReadOnlyList<Attachment> attachments);
}
