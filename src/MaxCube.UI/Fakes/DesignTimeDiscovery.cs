using MaxCube.Application.Abstractions;
using MaxCube.Application.Models;

namespace MaxCube.UI.Fakes;

/// <summary>Designzeit- und Teststandard: keine Netzwerkaktivität, keine Treffer.</summary>
public sealed class DesignTimeDiscovery : ICubeDiscovery
{
    public Task<IReadOnlyList<DiscoveredCube>> DiscoverAsync(
        TimeSpan duration, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DiscoveredCube>>(Array.Empty<DiscoveredCube>());
}
