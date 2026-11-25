using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaxCube.UI.Localization;

namespace MaxCube.UI.ViewModels;

/// <summary>Ein Hilfethema mit lokalisiertem Titel und Text.</summary>
public sealed record HelpTopic(string Key, string Title, string Body)
{
    public override string ToString() => Title;
}

/// <summary>
/// Gremium Runde 5: App-Einstellungen (Sprache, Log-Aufbewahrung, gebündeltes Senden) und das
/// mehrsprachige Hilfesystem. Einstellungen werden im lokalen SQLite-Cache abgelegt.
/// </summary>
public partial class MainWindowViewModel
{
    public const string LanguageSetting = "ui.language";
    public const string LogRetentionSetting = "log.retentionDays";
    public const string BundleSetting = "ui.bundleTemperatures";
    public const string CloseToTraySetting = "ui.closeToTray";
    public const int DefaultLogRetentionDays = 30;

    private static readonly string[] HelpKeys = ["Connect", "Rooms", "Week", "Config", "Devices", "Cube"];

    private bool _loadingSettings;

    public IReadOnlyList<LanguageOption> Languages => Localizer.Languages;

    public ObservableCollection<HelpTopic> HelpTopics { get; } = new();

    [ObservableProperty]
    private LanguageOption? selectedLanguage;

    [ObservableProperty]
    private HelpTopic? selectedHelpTopic;

    [ObservableProperty]
    private int logRetentionDays = DefaultLogRetentionDays;

    /// <summary>Temperaturänderungen sammeln und nach dem Sammelfenster gemeinsam senden.</summary>
    [ObservableProperty]
    private bool bundleTemperatures;

    /// <summary>
    /// Schließen versteckt das Fenster, statt die App zu beenden (Systemtray, Dauerbetrieb). Standardmäßig
    /// aus: Ohne Tray-Unterstützung des Desktops wäre ein verstecktes Fenster nicht wieder erreichbar.
    /// </summary>
    [ObservableProperty]
    private bool closeToTray;

    partial void OnCloseToTrayChanged(bool value)
    {
        if (!_loadingSettings)
        {
            _ = PersistAsync(CloseToTraySetting, value ? "1" : "0");
        }
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is null)
        {
            return;
        }

        _loc.SetLanguage(value.Code);
        if (!_loadingSettings)
        {
            _ = PersistAsync(LanguageSetting, value.Code);
        }
    }

    partial void OnLogRetentionDaysChanged(int value)
    {
        if (!_loadingSettings)
        {
            _ = PersistAsync(LogRetentionSetting, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    partial void OnBundleTemperaturesChanged(bool value)
    {
        if (!_loadingSettings)
        {
            _ = PersistAsync(BundleSetting, value ? "1" : "0");
        }
    }

    /// <summary>
    /// Lädt die gespeicherten Einstellungen (Sprache, Aufbewahrung, Bündelung) und räumt alte Logs auf.
    /// Wird einmal beim Start aufgerufen; ein Lesefehler lässt die Standardwerte stehen.
    /// </summary>
    public async Task InitializeAsync(string? systemLanguage = null)
    {
        _loadingSettings = true;
        try
        {
            string? language = null;
            string? retention = null;
            string? bundle = null;
            string? tray = null;
            string? logging = null;
            try
            {
                language = await _service.ReadSettingAsync(LanguageSetting);
                retention = await _service.ReadSettingAsync(LogRetentionSetting);
                bundle = await _service.ReadSettingAsync(BundleSetting);
                tray = await _service.ReadSettingAsync(CloseToTraySetting);
                logging = await _service.ReadSettingAsync(LogEnabledSetting);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _log.Write("WARN", "Einstellungen konnten nicht gelesen werden: " + exception.Message);
            }

            var code = Localizer.Detect(language, systemLanguage);
            SelectedLanguage = Localizer.Languages.First(option => option.Code == code);
            LogRetentionDays = int.TryParse(retention, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days)
                && days is >= 1 and <= 365
                ? days
                : DefaultLogRetentionDays;
            BundleTemperatures = bundle == "1";
            CloseToTray = tray == "1";
            // Opt-in: ohne gespeicherte Freigabe bleibt das Protokoll aus.
            LogEnabled = logging == "1";
        }
        finally
        {
            _loadingSettings = false;
        }

        var removed = _log.Cleanup(LogRetentionDays);
        if (removed > 0)
        {
            _log.Write("INFO", $"{removed} alte Logdatei(en) entfernt (Aufbewahrung {LogRetentionDays} Tage).");
        }

        // Erstkontakt: den gespeicherten Endpoint übernehmen statt 127.0.0.1 raten zu lassen.
        try
        {
            if (await _service.GetLastEndpointAsync() is { } endpoint)
            {
                Host = endpoint.Host;
                Port = endpoint.Port;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _log.Write("WARN", $"Letzter Endpoint konnte nicht geladen werden: {exception.Message}");
        }

        _log.Write("INFO", $"App {AppVersion} gestartet; Protokoll {(LogEnabled ? "an" : "aus")}.");
        LoadLog();
    }

    private async Task PersistAsync(string key, string value)
    {
        try
        {
            await _service.WriteSettingAsync(key, value);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _log.Write("WARN", $"Einstellung {key} konnte nicht gespeichert werden: {exception.Message}");
        }
    }

    private void BuildHelpTopics()
    {
        var previous = SelectedHelpTopic?.Key;
        HelpTopics.Clear();
        foreach (var key in HelpKeys)
        {
            HelpTopics.Add(new HelpTopic(key, L($"Help{key}Title"), L($"Help{key}Body")));
        }

        SelectedHelpTopic = HelpTopics.FirstOrDefault(topic => topic.Key == previous) ?? HelpTopics.FirstOrDefault();
    }

    /// <summary>Kontexthilfe: wechselt in den Hilfe-Reiter und wählt das passende Thema.</summary>
    [RelayCommand]
    private void ShowHelp(string? topic)
    {
        SelectedHelpTopic = HelpTopics.FirstOrDefault(entry => entry.Key == topic) ?? SelectedHelpTopic;
        SelectedTabIndex = TabIndex.Help;
    }
}
