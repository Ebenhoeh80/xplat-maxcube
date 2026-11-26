using System.IO.Compression;
using System.Runtime.InteropServices;
using Avalonia;
using MaxCube.Application.Abstractions;
using MaxCube.Infrastructure.Platform;
using MaxCube.UI.Localization;
using MaxCube.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MaxCube.UI.Desktop;

/// <summary>Nativer Einstiegspunkt ohne Java, Browser oder Administratorrechte (Windows und Linux).</summary>
internal static class Program
{
    // Aus den DllImports der Avalonia.X11/Skia-Assemblies belegte Systembibliotheken.
    private static readonly (string Library, string Package)[] LinuxLibraries =
    {
        ("libfontconfig.so.1", "libfontconfig1"),
        ("libX11.so.6", "libx11-6"),
        ("libXcursor.so.1", "libxcursor1"),
        ("libXext.so.6", "libxext6"),
        ("libXfixes.so.3", "libxfixes3"),
        ("libXi.so.6", "libxi6"),
        ("libXrandr.so.2", "libxrandr2"),
        ("libICE.so.6", "libice6"),
        ("libSM.so.6", "libsm6"),
        ("libGL.so.1", "libgl1"),
        ("libvulkan.so.1", "libvulkan1"),
    };

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--selfcheck", StringComparer.Ordinal))
        {
            return SelfCheck();
        }

        if (args.Contains("--check-linux-deps", StringComparer.Ordinal))
        {
            return CheckLinuxDependencies();
        }

        // SkiaSharp lädt libfontconfig.so.1 dynamisch. Ohne das Desktop-Paket bricht
        // Avalonia sonst mit einer schwer verständlichen TypeInitializationException ab.
        if (OperatingSystem.IsLinux() && CheckLinuxDependencies(verbose: false) != 0)
        {
            return 2;
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (DllNotFoundException exception)
        {
            Console.Error.WriteLine("Native Bibliothek fehlt: " + exception.Message);
            Console.Error.WriteLine("Debian/Ubuntu: sudo apt-get update && sudo apt-get install -y "
                + string.Join(" ", LinuxLibraries.Select(entry => entry.Package)));
            return 2;
        }
    }

    /// <summary>
    /// Fensterloser Abhängigkeitscheck für Linux (auch im veröffentlichten ZIP nutzbar):
    /// SkiaSharp und Avalonia.X11 laden diese Systembibliotheken dynamisch; sie sind nicht
    /// Teil des selbstständigen Pakets und fehlen auf schlanken Debian-Installationen.
    /// </summary>
    private static int CheckLinuxDependencies(bool verbose = true)
    {
        if (!OperatingSystem.IsLinux())
        {
            if (verbose)
            {
                Console.WriteLine("LINUX DEPS: unter diesem Betriebssystem nicht erforderlich – OK.");
            }
            return 0;
        }

        var missing = new List<string>();
        foreach (var (library, _) in LinuxLibraries)
        {
            if (NativeLibrary.TryLoad(library, out var handle))
            {
                NativeLibrary.Free(handle);
            }
            else
            {
                missing.Add(library);
            }
        }

        if (missing.Count == 0)
        {
            if (verbose)
            {
                Console.WriteLine("LINUX DEPS OK: fontconfig und X11-Bibliotheken vorhanden.");
            }
            return 0;
        }

        var packages = LinuxLibraries
            .Where(entry => missing.Contains(entry.Library, StringComparer.Ordinal))
            .Select(entry => entry.Package);
        Console.Error.WriteLine(
            "LINUX DEPS FEHLER: fehlende Bibliotheken: " + string.Join(", ", missing) + "\n"
            + "Debian/Ubuntu: sudo apt-get update && sudo apt-get install -y " + string.Join(" ", packages) + "\n"
            + "Danach die App erneut starten.");
        return 2;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    /// <summary>
    /// Fensterloser Selbsttest für CI, Container und Linux-Abnahme: baut die komplette Objektkette
    /// (Pfade, SQLite mit nativer Bibliothek, Gateway, Dienst, ViewModel, Lokalisierung), prüft den
    /// Instanzschutz und beendet sich mit 0. Es wird weder ein Cube kontaktiert noch ein Broadcast gesendet.
    /// </summary>
    private static int SelfCheck()
    {
        var root = Path.Combine(Path.GetTempPath(), "maxcube-selfcheck-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root).EnsureCreated();
            using var first = InstanceLock.TryAcquire(paths.LockFile);
            using var second = InstanceLock.TryAcquire(paths.LockFile);
            if (first is null || second is not null)
            {
                Console.Error.WriteLine("SELFCHECK FEHLER: Instanzschutz funktioniert nicht.");
                return 2;
            }

            // CubeGateway und SQLite-Store sind IAsyncDisposable: der Container muss asynchron enden.
            var provider = Composition.Build(paths, new ImmediateUiDispatcher());
            try
            {
                return RunChecks(provider, paths);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("SELFCHECK FEHLER: " + exception);
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Aufräumen des Temp-Ordners ist optional.
            }
        }
    }

    private static int RunChecks(ServiceProvider provider, AppPaths paths)
    {
        var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        viewModel.InitializeAsync("de").GetAwaiter().GetResult();

        // QA 0.6.1: der Log-Schalter muss beide Zustände protokollieren. Der alte
        // Ablauf verlor „ausgeschaltet", weil der Store vor dem Schreiben stumm geschaltet wurde.
        viewModel.LogEnabled = true;
        viewModel.LogEnabled = false;
        var logEntries = provider.GetRequiredService<ILogStore>().ReadRecent(10);
        if (!logEntries.Any(entry => entry.Contains("Protokoll eingeschaltet", StringComparison.Ordinal))
            || !logEntries.Any(entry => entry.Contains("Protokoll ausgeschaltet", StringComparison.Ordinal)))
        {
            Console.Error.WriteLine("SELFCHECK FEHLER: Log-Schalter protokolliert nicht beide Zustände.");
            return 5;
        }

        foreach (var entry in logEntries.Where(entry => entry.Contains("Protokoll ", StringComparison.Ordinal)))
        {
            Console.WriteLine("SELFCHECK LOG: " + entry);
        }

        // 0.6.2: der redigierte Export muss lokal funktionieren und mindestens eine Zeile enthalten.
        var exportPath = provider.GetRequiredService<ILogStore>().Export(
            Path.Combine(paths.Root, "export"), new LogExportOptions { RedactPersonalData = true });
        if (string.IsNullOrWhiteSpace(exportPath) || !File.Exists(exportPath))
        {
            Console.Error.WriteLine("SELFCHECK FEHLER: Log-Export wurde nicht erzeugt.");
            return 6;
        }

        using (var zip = ZipFile.OpenRead(exportPath))
        {
            var exportedEntry = zip.Entries.FirstOrDefault();
            if (exportedEntry is null)
            {
                Console.Error.WriteLine("SELFCHECK FEHLER: Log-Export enthält keine Datei.");
                return 6;
            }

            using var reader = new StreamReader(exportedEntry.Open());
            var exportedText = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(exportedText)
                || !exportedText.Contains("[INFO] Protokoll", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("SELFCHECK FEHLER: Log-Export enthält keine Protokollzeile.");
                return 6;
            }
        }

        var localizer = provider.GetRequiredService<Localizer>();
        foreach (var language in Localizer.Languages)
        {
            localizer.SetLanguage(language.Code);
            foreach (var key in Localizer.Keys)
            {
                if (localizer[key].StartsWith('!'))
                {
                    Console.Error.WriteLine($"SELFCHECK FEHLER: Text fehlt in {language.Code}: {key}.");
                    return 3;
                }
            }
        }

        Console.WriteLine(
            $"SELFCHECK OK: {viewModel.WindowTitle} | {System.Runtime.InteropServices.RuntimeInformation.OSDescription} | "
            + $"Daten {paths.Root} | SQLite {(File.Exists(paths.Database) ? "ok" : "fehlt")}");
        return File.Exists(paths.Database) ? 0 : 4;
    }
}
