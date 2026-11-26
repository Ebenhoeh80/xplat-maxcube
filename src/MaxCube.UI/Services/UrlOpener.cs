using System.Diagnostics;

namespace MaxCube.UI.Services;

/// <summary>
/// Öffnet externe Ziele (Projektseite, Protokollordner) ausschließlich nach Benutzerklick.
/// </summary>
public interface IUrlOpener
{
    /// <summary>True, wenn ein externer Opener das Ziel übernommen hat.</summary>
    bool TryOpen(string target);
}

/// <summary>
/// Windows nutzt die Shell, Linux `xdg-open`/`gio`/`wslview`, macOS `open`. Auf schlanken
/// Linux-Installationen ist keiner dieser Opener vorhanden; dann liefert <see cref="TryOpen"/>
/// false, statt eine Win32Exception durch die UI zu jagen und die App zu beenden.
/// </summary>
public sealed class DesktopUrlOpener : IUrlOpener
{
    private static readonly (string Executable, string[] Prefix)[] LinuxOpeners =
    [
        ("xdg-open", []),
        ("gio", ["open"]),
        ("wslview", []),
    ];

    public bool TryOpen(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return TryShell(target);
        }

        if (OperatingSystem.IsMacOS())
        {
            return TryProcess("open", target);
        }

        foreach (var (executable, prefix) in LinuxOpeners)
        {
            if (TryProcess(executable, [.. prefix, target]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryShell(string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return process is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryProcess(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
