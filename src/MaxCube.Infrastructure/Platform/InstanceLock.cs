using System.Diagnostics;

namespace MaxCube.Infrastructure.Platform;

/// <summary>
/// Verhindert eine zweite App-Instanz (Instanzschutz, Application.java:75-91,
/// InstanceLock.java:1-78). Der Cube erlaubt nur eine TCP-Session; zwei Instanzen würden sich
/// gegenseitig aussperren. Die Sperre ist eine exklusiv geöffnete Datei (Windows: Share-Mode,
/// Linux: <c>flock</c> durch .NET) und verschwindet mit dem Prozess – auch nach einem Absturz,
/// weil das Betriebssystem den Dateihandle freigibt.
/// </summary>
public sealed class InstanceLock : IDisposable
{
    private FileStream? _stream;

    private InstanceLock(FileStream stream) => _stream = stream;

    /// <summary>Liefert die Sperre oder null, wenn bereits eine andere Instanz läuft.</summary>
    public static InstanceLock? TryAcquire(string lockFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockFilePath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(lockFilePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        FileStream? stream = null;
        try
        {
            stream = new FileStream(
                lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            stream.SetLength(0);
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.Flush();
            return new InstanceLock(stream);
        }
        catch (IOException)
        {
            stream?.Dispose();
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // Datei gehört einer anderen Instanz/einem anderen Benutzer: als belegt behandeln.
            stream?.Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        var stream = Interlocked.Exchange(ref _stream, null);
        stream?.Dispose();
    }
}
