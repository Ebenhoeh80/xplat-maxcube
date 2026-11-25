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
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--selfcheck", StringComparer.Ordinal))
        {
            return SelfCheck();
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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

        var localizer = provider.GetRequiredService<Localizer>();
        foreach (var language in Localizer.Languages)
        {
            localizer.SetLanguage(language.Code);
            if (localizer["Connect"].StartsWith('!'))
            {
                Console.Error.WriteLine($"SELFCHECK FEHLER: Text fehlt in {language.Code}.");
                return 3;
            }
        }

        Console.WriteLine(
            $"SELFCHECK OK: {viewModel.WindowTitle} | {System.Runtime.InteropServices.RuntimeInformation.OSDescription} | "
            + $"Daten {paths.Root} | SQLite {(File.Exists(paths.Database) ? "ok" : "fehlt")}");
        return File.Exists(paths.Database) ? 0 : 4;
    }
}
