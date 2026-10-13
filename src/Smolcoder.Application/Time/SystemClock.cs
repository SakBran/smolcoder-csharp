using Smolcoder.Application.Abstractions.Time;

namespace Smolcoder.Application.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow
    {
        get
        {
            return DateTime.UtcNow;
        }
    }
}
