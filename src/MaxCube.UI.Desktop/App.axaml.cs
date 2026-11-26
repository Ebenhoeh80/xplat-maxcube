using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MaxCube.Infrastructure.Platform;
using MaxCube.UI.Localization;
using MaxCube.UI.ViewModels;
using MaxCube.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace MaxCube.UI.Desktop;

/// <summary>
/// Avalonia-Anwendung für Windows und Linux. Der Instanzschutz verhindert eine zweite Sitzung zum
/// Cube; das Systemtray-Symbol ist optional (ohne Desktop-Unterstützung bleibt es wirkungslos).
/// </summary>
public sealed partial class App : global::Avalonia.Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private bool _shuttingDown;
    private InstanceLock? _instanceLock;
    private TrayIcon? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = new AppPaths().EnsureCreated();
            _instanceLock = InstanceLock.TryAcquire(paths.LockFile);
            if (_instanceLock is null)
            {
                // Die Sprache steht vor dem Start noch nicht fest; die Systemsprache genügt für diesen Hinweis.
                var language = new Localizer(Localizer.Detect(null, SystemLanguage.Detect()));
                var window = new AlreadyRunningWindow(language["AlreadyRunningTitle"], language["AlreadyRunning"], language["Ok"]);
                desktop.MainWindow = window;
                base.OnFrameworkInitializationCompleted();
                return;
            }

            Services = Composition.Build(paths, new AvaloniaUiDispatcher());
            var viewModel = Services.GetRequiredService<MainWindowViewModel>();
            viewModel.ReportDirectory = paths.Reports;
            var main = Services.GetRequiredService<MainWindow>();
            main.DataContext = viewModel;
            desktop.MainWindow = main;
            _ = viewModel.InitializeAsync(SystemLanguage.Detect());

            SetUpTray(desktop, main, viewModel);

            main.Closing += (_, args) =>
            {
                if (!_shuttingDown && viewModel.CloseToTray)
                {
                    args.Cancel = true;
                    main.Hide();
                }
            };

            desktop.ShutdownRequested += (_, args) =>
            {
                // Auch bei einem zweiten Request abbrechen: ein forcierter Shutdown
                // würde sonst mitten im asynchronen Cleanup und damit vor q: enden.
                args.Cancel = true;
                if (_shuttingDown)
                {
                    return;
                }

                // Avalonia erwartet einen synchronen Handler. Erst asynchron aufräumen,
                // dann neu Shutdown auslösen – kein blockierendes GetResult (N7).
                _shuttingDown = true;
                _ = ShutdownAsync(desktop);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetUpTray(IClassicDesktopStyleApplicationLifetime desktop, Window main, MainWindowViewModel viewModel)
    {
        try
        {
            var show = new NativeMenuItem(viewModel.Loc["TrayShow"]);
            show.Click += (_, _) =>
            {
                main.Show();
                main.WindowState = WindowState.Normal;
                main.Activate();
            };
            var exit = new NativeMenuItem(viewModel.Loc["TrayExit"]);
            // TryShutdown löst ShutdownRequested aus und damit den Cleanup-Pfad aus.
            // Ein direkter Shutdown() wäre forciert: ohne q:, ohne Socket-Close.
            exit.Click += (_, _) => desktop.TryShutdown();
            var menu = new NativeMenu { show, exit };

            _tray = new TrayIcon
            {
                Icon = TrayIconFactory.Create(),
                ToolTipText = viewModel.WindowTitle,
                Menu = menu,
            };
            _tray.Clicked += (_, _) =>
            {
                main.Show();
                main.Activate();
            };
            TrayIcon.SetIcons(this, new TrayIcons { _tray });

            // Beschriftung und Tooltip folgen der Sprachwahl.
            viewModel.Loc.PropertyChanged += (_, _) =>
            {
                show.Header = viewModel.Loc["TrayShow"];
                exit.Header = viewModel.Loc["TrayExit"];
                _tray.ToolTipText = viewModel.WindowTitle;
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Ein Desktop ohne Tray (z. B. minimaler Linux-Fenstermanager) darf den Start nicht verhindern.
            _tray = null;
            System.Diagnostics.Trace.TraceWarning("Tray-Symbol nicht verfügbar: " + exception.Message);
        }
    }

    /// <summary>Schließt SQLite-Verbindung und Cube-Socket asynchron beim Beenden.</summary>
    private async Task ShutdownAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            _tray?.Dispose();
            if (Services is ServiceProvider provider)
            {
                await provider.DisposeAsync().ConfigureAwait(true);
            }
        }
        catch
        {
            // Das Beenden darf nicht an einem fehlerhaften Store scheitern.
        }
        finally
        {
            _instanceLock?.Dispose();
            desktop.Shutdown();
        }
    }
}
