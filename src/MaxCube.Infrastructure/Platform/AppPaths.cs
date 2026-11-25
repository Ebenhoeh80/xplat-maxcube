namespace MaxCube.Infrastructure.Platform;

/// <summary>
/// Datenverzeichnis der App, plattformneutral: unter Windows <c>%LOCALAPPDATA%\MaxCube</c>,
/// unter Linux <c>$XDG_DATA_HOME/MaxCube</c> bzw. <c>~/.local/share/MaxCube</c> (so bildet .NET
/// <c>LocalApplicationData</c> ab). <c>MAXCUBE_DATA_DIR</c> überschreibt den Ort (portabel/Tests).
/// </summary>
public sealed class AppPaths
{
    public const string DataDirectoryVariable = "MAXCUBE_DATA_DIR";

    public AppPaths(string? rootOverride = null)
    {
        var configured = rootOverride ?? Environment.GetEnvironmentVariable(DataDirectoryVariable);
        Root = !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured)
            : Path.Combine(DefaultBase(), "MaxCube");
    }

    public string Root { get; }

    public string Database => Path.Combine(Root, "maxcube.db");

    public string Logs => Path.Combine(Root, "logs");

    public string Reports => Path.Combine(Root, "reports");

    public string LockFile => Path.Combine(Root, "maxcube.lock");

    public AppPaths EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Reports);
        return this;
    }

    private static string DefaultBase()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            return folder;
        }

        // Ohne HOME (Container, Dienstkonto) bleibt die App startfähig.
        return Path.Combine(Path.GetTempPath(), "maxcube-data");
    }
}
