using MaxCube.Application.Abstractions;
using MaxCube.Application.Models;

namespace MaxCube.UI.Fakes;

/// <summary>In-Memory-Store für die XAML-Vorschau; speichert nichts dauerhaft.</summary>
public sealed class DesignTimeStore : ICubeStateStore
{
    public Task<CubeSnapshot?> LoadAsync(string serialNumber, CancellationToken cancellationToken = default)
        => Task.FromResult<CubeSnapshot?>(null);

    public Task SaveAsync(CubeSnapshot snapshot, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task AppendAuditAsync(
        string message, AuditSource source = AuditSource.User, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<int> DeleteAuditOlderThanAsync(
        DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    public Task<IReadOnlyList<string>> ReadAuditAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}
