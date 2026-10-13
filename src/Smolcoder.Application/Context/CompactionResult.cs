using Smolcoder.Domain.Messages;

namespace Smolcoder.Application.Context;

public sealed class CompactionResult
{
    public CompactionResult(List<Message> messages, CompactionReport report)
    {
        Messages = messages;
        Report = report;
    }

    public List<Message> Messages { get; }

    public CompactionReport Report { get; }
}
