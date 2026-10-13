using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Domain.Messages;

namespace Smolcoder.Application.Context;

/// <summary>
/// Keeps the conversation inside the model's context window.
/// </summary>
public interface IContextManager
{
    void SetWindow(int window, int? reserve);

    void RecordUsage(int? promptTokens, int? completionTokens, int messageCount);

    void ResetAnchor();

    void SetReplayThinking(bool value);

    int EstimatePrompt(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools);

    int UsableWindow();

    int ToolResultCharLimit();

    void Calibrate(int? promptTokens, IReadOnlyList<Message> input, IReadOnlyList<ToolSpec> tools);

    ContextBudget Budget(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools);

    void AssertFits(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools);

    int FillPercent(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools);

    bool NeedsAttention(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools);

    int EvictStaleReads(List<Message> messages, string filePath);

    void PrepareBackground(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools, IModelProvider provider, CompactState state);

    void CancelBackground(bool discard);

    Task ForegroundAsync();

    Task<CompactionResult> ManageAsync(List<Message> messages, IReadOnlyList<ToolSpec> tools, IModelProvider provider, CompactState state, bool force, bool deterministic, CancellationToken cancellationToken);
}
