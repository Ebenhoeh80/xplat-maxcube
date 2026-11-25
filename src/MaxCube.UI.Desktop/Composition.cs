using MaxCube.Application.Abstractions;
using MaxCube.Application.Services;
using MaxCube.Infrastructure.Connectivity;
using MaxCube.Infrastructure.Persistence;
using MaxCube.Infrastructure.Platform;
using MaxCube.UI.Localization;
using MaxCube.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MaxCube.UI.Desktop;

/// <summary>
/// Composition Root ohne Avalonia-Abhängigkeit: Windows, Linux und der Selbsttest bauen dieselbe
/// Objektkette. So lässt sich die Verdrahtung (SQLite, Pfade, Gateway) ohne Fenster prüfen.
/// </summary>
internal static class Composition
{
    public static ServiceProvider Build(AppPaths paths, IUiDispatcher? dispatcher = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAppVersionProvider, AppVersionProvider>();
        services.AddSingleton<ILogStore>(_ => new FileAppLog(paths.Logs));
        services.AddSingleton<IAppLog>(provider => provider.GetRequiredService<ILogStore>());

        // Ein Gateway-Objekt bedient Kern- und Admin-Schnittstelle: ein Socket, eine Queue, ein Worker.
        services.AddSingleton<CubeGateway>();
        services.AddSingleton<ICubeGateway>(provider => provider.GetRequiredService<CubeGateway>());
        services.AddSingleton<ICubeAdminGateway>(provider => provider.GetRequiredService<CubeGateway>());
        services.AddSingleton<ICubeNetworkConfigurator, UdpNetworkConfigurator>();
        services.AddSingleton<IDiscoveryTransport, UdpDiscoveryTransport>();
        services.AddSingleton<ICubeDiscovery, CubeDiscoveryService>();
        services.AddSingleton<ICubeStateStore>(provider => new SqliteStateStore(
            $"Data Source={paths.Database}",
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<IAppVersionProvider>()));
        services.AddSingleton<CubeApplicationService>();
        services.AddSingleton(_ => new Localizer(Localizer.Detect(null, SystemLanguage.Detect())));
        if (dispatcher is not null)
        {
            services.AddSingleton(dispatcher);
        }

        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<MaxCube.UI.Views.MainWindow>();
        return services.BuildServiceProvider();
    }
}
