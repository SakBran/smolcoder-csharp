using Smolcoder.Domain.Sessions;

namespace Smolcoder.Application.Abstractions.Persistence;

/// <summary>
/// The project folders shown in the sidebar.
/// </summary>
public interface IWorkspaceRepository
{
    Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds the folder, or marks it as just opened when it is already there.
    /// </summary>
    Task AddAsync(string path, CancellationToken cancellationToken);

    Task TouchAsync(string path, CancellationToken cancellationToken);

    Task RemoveAsync(string path, CancellationToken cancellationToken);
}
