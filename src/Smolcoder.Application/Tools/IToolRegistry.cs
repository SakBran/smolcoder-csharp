using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Domain.Modes;

namespace Smolcoder.Application.Tools;

public interface IToolRegistry
{
    /// <summary>
    /// The tool schemas sent to the model in this mode.
    /// </summary>
    IReadOnlyList<ToolSpec> BuildToolSpecs(PermissionMode mode);
}
