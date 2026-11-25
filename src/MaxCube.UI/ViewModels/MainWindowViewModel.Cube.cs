using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaxCube.Application.Models;
using MaxCube.Application.Services;
using MaxCube.Domain.Configuration;
using MaxCube.UI.Localization;

namespace MaxCube.UI.ViewModels;

/// <summary>
/// Gremium Runde 5: Cube-Einstellungen (Zeitzone, Zeit, NTP, Netzwerk), Firmware-Anzeige,
/// Funkauslastung und Diagnosebericht. Firmware-Schreiben und Cloud/Proxy bleiben ausgeschlossen.
/// </summary>
public partial class MainWindowViewModel
{
    public IReadOnlyList<string> ZonePresets { get; } = TimeZonePresets.All.Keys.ToArray();

    /// <summary>Ordner für gespeicherte Diagnoseberichte; vom Composition Root gesetzt.</summary>
    public string? ReportDirectory { get; set; }

    [ObservableProperty]
    private string selectedZonePreset = string.Empty;

    [ObservableProperty]
    private string timeZoneText = string.Empty;

    [ObservableProperty]
    private string ntpServer1 = "pool.ntp.org";

    [ObservableProperty]
    private string ntpServer2 = string.Empty;

    [ObservableProperty]
    private string networkIp = string.Empty;

    [ObservableProperty]
    private string networkMask = "255.255.255.0";

    [ObservableProperty]
    private string networkGateway = string.Empty;

    [ObservableProperty]
    private string networkDns1 = string.Empty;

    [ObservableProperty]
    private string networkDns2 = string.Empty;

    [ObservableProperty]
    private bool networkDhcp;

    [ObservableProperty]
    private bool confirmNetwork;

    [ObservableProperty]
    private string firmwareText = string.Empty;

    [ObservableProperty]
    private string airtimeText = string.Empty;

    [ObservableProperty]
    private string diagnosticText = string.Empty;

    private bool CanNetwork => CanAdmin && _service.SupportsNetworkConfiguration;

    private bool CanSendNetwork => CanNetwork && ConfirmNetwork;

    partial void OnConfirmNetworkChanged(bool value) => SendNetworkCommand.NotifyCanExecuteChanged();

    private string FirmwareDescription(int firmware)
        => L(FirmwareSupport.IsSupported(firmware) ? "FirmwareSupported" : "FirmwareUnsupported",
            firmware, FirmwareSupport.Minimum, FirmwareSupport.Maximum);

    [RelayCommand(CanExecute = nameof(CanAdmin))]
    private async Task ReadTimeZoneAsync()
    {
        await RunAsync(async token =>
        {
            var zone = await _service.ReadTimeZoneAsync(token);
            TimeZoneText = L(
                zone.UsesDaylightSaving ? "ZoneInfoDst" : "ZoneInfo",
                zone.Winter.Name, Offset(zone.Winter.OffsetSeconds), zone.Summer.Name, Offset(zone.Summer.OffsetSeconds));
            var match = TimeZonePresets.All.FirstOrDefault(pair => pair.Value == zone);
            if (match.Key is not null)
            {
                SelectedZonePreset = match.Key;
            }
        }, L("ErrCubeSetting"), L("OkZoneRead"));
    }

    [RelayCommand(CanExecute = nameof(CanAdmin))]
    private async Task SendTimeZoneAsync()
    {
        await RunAsync(async token =>
        {
            if (!TimeZonePresets.All.TryGetValue(SelectedZonePreset, out var zone))
            {
                throw new InvalidOperationException(L("ErrNoZone"));
            }

            await _service.SetTimeZoneAsync(zone, token);
            await RefreshAuditAsync();
        }, L("ErrCubeSetting"), L("OkZone"));
    }

    [RelayCommand(CanExecute = nameof(CanAdmin))]
    private async Task SetCubeTimeAsync()
    {
        await RunAsync(async token =>
        {
            await _service.SetCubeTimeAsync(token);
            await RefreshAuditAsync();
        }, L("ErrCubeSetting"), L("OkCubeTime"));
    }

    [RelayCommand(CanExecute = nameof(CanAdmin))]
    private async Task ReadNtpAsync()
    {
        await RunAsync(async token =>
        {
            var answer = await _service.QueryNtpServersAsync(token);
            // Format der F:-Antwort ist UNBEKANNT (docs/maxcube-protocol.md:327); er wird roh angezeigt.
            var servers = answer.StartsWith("F:", StringComparison.Ordinal) ? answer[2..] : answer;
            var parts = servers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            NtpServer1 = parts.ElementAtOrDefault(0) ?? string.Empty;
            NtpServer2 = parts.ElementAtOrDefault(1) ?? string.Empty;
        }, L("ErrCubeSetting"), L("OkNtpRead"));
    }

    [RelayCommand(CanExecute = nameof(CanAdmin))]
    private async Task SendNtpAsync()
    {
        await RunAsync(async token =>
        {
            await _service.SetNtpServersAsync([NtpServer1, NtpServer2], token);
            await RefreshAuditAsync();
        }, L("ErrCubeSetting"), L("OkNtp"));
    }

    [RelayCommand(CanExecute = nameof(CanNetwork))]
    private async Task ReadNetworkAsync()
    {
        await RunAsync(async token =>
        {
            var configuration = await _service.ReadNetworkConfigurationAsync(token);
            NetworkIp = configuration.IpAddress;
            NetworkMask = configuration.SubnetMask;
            NetworkGateway = configuration.Gateway;
            NetworkDns1 = configuration.Dns1;
            NetworkDns2 = configuration.Dns2;
            NetworkDhcp = configuration.Dhcp;
        }, L("ErrNetwork"), L("OkNetworkRead"));
    }

    [RelayCommand(CanExecute = nameof(CanSendNetwork))]
    private async Task SendNetworkAsync()
    {
        await RunAsync(async token =>
        {
            var configuration = new CubeNetworkConfiguration(
                NetworkIp.Trim(), NetworkMask.Trim(), NetworkGateway.Trim(), NetworkDns1.Trim(), NetworkDns2.Trim(), NetworkDhcp);
            await _service.WriteNetworkConfigurationAsync(configuration, ConfirmNetwork, token);
            ConfirmNetwork = false;
            await RefreshAuditAsync();
        }, L("ErrNetwork"), L("OkNetwork"));
    }

    /// <summary>
    /// Ersatz für „Fehlerbericht senden“: der Bericht wird nur angezeigt und optional als Datei
    /// gespeichert, nichts wird versendet und es werden keine Zugangsdaten aufgenommen.
    /// </summary>
    [RelayCommand]
    private async Task ExportDiagnosticsAsync()
    {
        await RunAsync(async token =>
        {
            var report = await _service.BuildDiagnosticReportAsync(AppVersion, token);
            DiagnosticText = report;
            if (!string.IsNullOrWhiteSpace(ReportDirectory))
            {
                Directory.CreateDirectory(ReportDirectory);
                var file = Path.Combine(
                    ReportDirectory, $"maxcube-diagnose-{_clock.Now:yyyyMMdd-HHmmss}.txt");
                await File.WriteAllTextAsync(file, report, token);
                DiagnosticText = $"{L("ReportSaved", file)}{Environment.NewLine}{Environment.NewLine}{report}";
            }
        }, L("ErrReport"), L("OkReport"));
    }

    private static string Offset(int seconds)
    {
        var hours = seconds / 3600.0;
        return "UTC" + (hours >= 0 ? "+" : "−") + Math.Abs(hours).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
