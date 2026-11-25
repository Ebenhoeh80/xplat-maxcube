using System.Reflection;
using MaxCube.Application.Abstractions;

namespace MaxCube.UI;

/// <summary>
/// Liest die zentrale App-Version aus dem Assembly-Attribut. Damit gibt es genau eine
/// Wahrheit: die <c>Version</c> aus <c>Directory.Build.props</c>. Ein <c>+Build</c>-Suffix
/// wird für die Anzeige entfernt, bleibt aber im Assembly-Attribut erhalten.
/// </summary>
public sealed class AppVersionProvider : IAppVersionProvider
{
    public AppVersionProvider(string? version = null)
    {
        var raw = version
            ?? typeof(AppVersionProvider).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(AppVersionProvider).Assembly.GetName().Version?.ToString()
            ?? "unbekannt";
        var plus = raw.IndexOf('+');
        Version = plus >= 0 ? raw[..plus] : raw;
    }

    public string Version { get; }
}
