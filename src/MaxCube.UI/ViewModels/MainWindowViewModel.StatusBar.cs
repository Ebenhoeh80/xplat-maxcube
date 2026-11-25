using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaxCube.Application.Abstractions;
using MaxCube.UI.Localization;

namespace MaxCube.UI.ViewModels;

/// <summary>
/// Dauerhaft sichtbare Statusleiste und lokales Anwendungsprotokoll (Gremium 0.6.0:
/// UX, Architektur, QA). Die Leiste zeigt Verbindung, Datenzustand, letzte Aktion und
/// Protokollstatus mit Symbol UND Text; das Protokoll ist opt-in, lokal und exportierbar.
/// </summary>
public partial class MainWindowViewModel
{
    public const string LogEnabledSetting = "log.enabled";
    private const int LogViewerLines = 200;

    private readonly ILogStore? _logStore;
    private DateTimeOffset? _lastActionAt;

    [ObservableProperty] private bool logEnabled;

    [ObservableProperty] private string logText = string.Empty;

    [ObservableProperty] private string logCountText = string.Empty;

    [ObservableProperty] private string statusBarConnectionText = string.Empty;

    [ObservableProperty] private string statusBarDataText = string.Empty;

    [ObservableProperty] private string statusBarActionText = string.Empty;

    [ObservableProperty] private string statusBarLogText = string.Empty;

    [ObservableProperty] private string errorTimeText = string.Empty;

    partial void OnLogEnabledChanged(bool value)
    {
        if (_logStore is not null)
        {
            _logStore.IsEnabled = value;
        }

        if (!_loadingSettings)
        {
            _ = PersistAsync(LogEnabledSetting, value ? "1" : "0");
            _log.Write("INFO", value ? "Protokoll eingeschaltet." : "Protokoll ausgeschaltet.");
            LoadLog();
        }

        RefreshStatusBar();
    }

    partial void OnStatusTextChanged(string value) => RefreshStatusBar();

    partial void OnSuccessTextChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _lastActionAt = _clock.Now;
        }

        RefreshStatusBar();
    }

    partial void OnWarningTextChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _lastActionAt = _clock.Now;
        }

        RefreshStatusBar();
    }

    partial void OnIsStaleChanged(bool value) => RefreshStatusBar();

    /// <summary>Ein Binding je Feld; Farbe ist nie die einzige Information.</summary>
    public void RefreshStatusBar()
    {
        var glyph = IsStale ? "⚠" : IsBusy ? "◌" : IsConnected ? "●" : "○";
        StatusBarConnectionText = $"{glyph} {StatusText}";

        var read = _service.LastReadAt is { } at
            ? L("StatusBarLastRead", at.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture))
            : L("StatusBarNoRead");
        StatusBarDataText = L("StatusBarAreas", $"{RoomsStatus.Glyph} {read}");

        var action = !string.IsNullOrWhiteSpace(WarningText)
            ? WarningText
            : SuccessText;
        StatusBarActionText = string.IsNullOrWhiteSpace(action)
            ? L("StatusBarNoAction")
            : L("StatusBarLastAction", $"{action} ({(_lastActionAt ?? _clock.Now).ToLocalTime():HH:mm:ss})");

        StatusBarLogText = LogEnabled ? L("StatusBarLogOn") : L("StatusBarLogOff");
    }

    public void LoadLog()
    {
        var lines = _logStore?.ReadRecent(LogViewerLines) ?? [];
        LogText = lines.Count == 0 ? L("LogEmpty") : string.Join(Environment.NewLine, lines);
        LogCountText = L("LogLines", lines.Count);
    }

    [RelayCommand]
    private void RefreshLog() => LoadLog();

    [RelayCommand]
    private void ShowLog()
    {
        SelectedTabIndex = TabIndex.Status;
        LoadLog();
    }

    /// <summary>Export nur auf Klick, rein lokal, ohne Netzwerk und ohne Dialog.</summary>
    [RelayCommand]
    private void ExportLog()
    {
        try
        {
            var directory = Path.GetDirectoryName(_logStore?.Directory ?? string.Empty);
            if (string.IsNullOrWhiteSpace(directory))
            {
                SuccessText = L("LogExportFailed", L("LogEmpty"));
                return;
            }

            var path = _logStore!.Export(directory);
            SuccessText = L("LogExportDone", path);
            _log.Write("INFO", $"Protokoll exportiert: {path}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SuccessText = L("LogExportFailed", exception.Message);
            _log.Write("WARN", $"Protokollexport fehlgeschlagen: {exception.Message}");
        }
    }

    /// <summary>Öffnet den lokalen Ordner ausschließlich nach Benutzerklick.</summary>
    [RelayCommand]
    private void OpenLogFolder()
    {
        var directory = _logStore?.Directory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo("xdg-open", directory) { UseShellExecute = true });
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or System.ComponentModel.Win32Exception)
        {
            SuccessText = L("LogExportFailed", exception.Message);
        }
    }
}
