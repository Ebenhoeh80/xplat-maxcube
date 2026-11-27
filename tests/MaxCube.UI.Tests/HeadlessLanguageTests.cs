using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using MaxCube.UI.ViewModels;
using MaxCube.UI.Views;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(MaxCube.UI.Tests.HeadlessTestApp))]

namespace MaxCube.UI.Tests;

/// <summary>Minimal headless application: the real MainWindow XAML is rendered without a desktop.</summary>
public sealed class HeadlessTestApp : global::Avalonia.Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

public class HeadlessLanguageTests
{
    [AvaloniaFact]
    public void Rendered_tab_headers_and_device_labels_follow_the_language_switch()
    {
        using var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        viewModel.SelectedTabIndex = TabIndex.Devices;
        Dispatcher.UIThread.RunJobs();

        var before = RenderedTexts(window);
        before.Should().Contain("Verbinden", "the German button starts as expected: " + string.Join(" | ", before));
        before.Should().Contain("Trennen");
        before.Should().Contain("Geräte", "the devices tab starts in German");
        before.Should().Contain("Gerät", "the device-structure heading starts in German");
        before.Should().Contain("Heizkörperthermostat", "the device type starts in German");

        viewModel.SelectedLanguage = viewModel.Languages.Single(language => language.Code == "en");
        Dispatcher.UIThread.RunJobs();

        var after = RenderedTexts(window);
        after.Should().Contain("Connect", "the XAML indexer binding must re-render after the switch: " + string.Join(" | ", after));
        after.Should().Contain("Disconnect", "every second localized label must follow as well");
        after.Should().Contain("Devices", "the devices tab and heading must follow the switch");
        after.Should().Contain("Device", "the device-structure heading must follow the switch");
        after.Should().Contain("Heating thermostat", "device types follow the language switch as well");
        after.Should().NotContain("Verbinden", "no German button text may remain after the switch");
        after.Should().NotContain("Trennen", "no German button text may remain after the switch");
        after.Should().NotContain("Geräte", "no German devices heading may remain");
        after.Should().NotContain("Gerät", "no German device heading may remain");
        after.Should().NotContain("Heizkörperthermostat", "no German device type may remain");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("en", "Devices", "Device", "Heating thermostat")]
    [InlineData("fr", "Appareils", "Appareil", "Thermostat de radiateur")]
    [InlineData("it", "Dispositivi", "Dispositivo", "Termostato radiatore")]
    [InlineData("nl", "Apparaten", "Apparaat", "Radiatorthermostaat")]
    [InlineData("pl", "Urządzenia", "Urządzenie", "Termostat grzejnikowy")]
    public void Rendered_device_texts_follow_every_language(
        string code, string devices, string device, string deviceType)
    {
        using var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        viewModel.SelectedTabIndex = TabIndex.Devices;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        viewModel.SelectedLanguage = viewModel.Languages.Single(language => language.Code == code);
        Dispatcher.UIThread.RunJobs();

        var texts = RenderedTexts(window);
        texts.Should().Contain(devices, "the devices tab and heading must be localized");
        texts.Should().Contain(device, "the device-structure heading must be localized");
        texts.Should().Contain(deviceType, "the device type must be localized");
        texts.Should().NotContain("Geräte", "no German devices heading may remain");
        texts.Should().NotContain("Gerät", "no German device heading may remain");
        texts.Should().NotContain("Heizkörperthermostat", "no German device type may remain");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("de", "Audit aufbewahren (Tage)")]
    [InlineData("en", "Keep audit history (days)")]
    [InlineData("fr", "Conserver l'historique d'audit (jours)")]
    [InlineData("it", "Conservare la cronologia audit (giorni)")]
    [InlineData("nl", "Auditgeschiedenis bewaren (dagen)")]
    [InlineData("pl", "Przechowywanie audytu (dni)")]
    public void Rendered_audit_retention_label_follows_every_language(string code, string label)
    {
        using var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        viewModel.SelectedTabIndex = TabIndex.Settings;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        viewModel.SelectedLanguage = viewModel.Languages.Single(language => language.Code == code);
        Dispatcher.UIThread.RunJobs();

        RenderedTexts(window).Should().Contain(label, "the settings tab must render the new retention label");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("de", "Endzeit")]
    [InlineData("en", "End time")]
    [InlineData("fr", "Heure de fin")]
    [InlineData("it", "Ora di fine")]
    [InlineData("nl", "Eindtijd")]
    [InlineData("pl", "Czas zakończenia")]
    public void Rendered_temporary_end_label_follows_every_language(string code, string label)
    {
        using var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        viewModel.SelectedLanguage = viewModel.Languages.Single(language => language.Code == code);
        Dispatcher.UIThread.RunJobs();

        RenderedTexts(window).Should().Contain(label, "the rooms tab must render the free end-time label");
        window.Close();
    }

    [AvaloniaFact]
    public void Weekly_timeline_is_configured_as_a_drag_target()
    {
        using var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        viewModel.SelectedTabIndex = TabIndex.Week;
        Dispatcher.UIThread.RunJobs();

        var target = window.FindControl<Border>("WeekTimelineDropTarget");
        target.Should().NotBeNull();
        DragDrop.GetAllowDrop(target!).Should().BeTrue();
        window.GetVisualDescendants().OfType<Border>()
            .Should().Contain(control => control.Name == "SwitchPointDragHandle",
                "nur der Griff, nicht die ganze Zeile, darf den Drag starten");

        window.Close();
    }

    private static List<string> RenderedTexts(Window window)
        => window.GetVisualDescendants()
            .SelectMany(visual => visual switch
            {
                TextBlock text => new[] { text.Text ?? string.Empty },
                TabItem tab => new[] { tab.Header?.ToString() ?? string.Empty },
                _ => Array.Empty<string>(),
            })
            .Where(text => text.Length > 0)
            .Distinct()
            .ToList();

}
