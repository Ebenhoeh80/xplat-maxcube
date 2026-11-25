using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.UI.ViewModels;

/// <summary>
/// Ein Abschnitt der Tagesleiste: Start/Breite als Anteil des Tages (0..1), Temperatur und
/// Farbe als Hexwert (blau = kalt, rot = warm). Enthält keine Avalonia-Typen.
/// </summary>
public sealed record TimelineSegment(double Start, double Width, double Temperature, string Color);

/// <summary>Ein bearbeitbarer Schaltpunkt: bis Uhrzeit gilt Temperatur.</summary>
public sealed partial class SwitchPointViewModel : ObservableObject
{
    private const double MinimumTemperature = 4.5;
    private const double MaximumTemperature = 30.5;
    private const int StepMinutes = 5;

    [ObservableProperty]
    private string untilText = "06:00";

    [ObservableProperty]
    private double temperature = 21.0;

    /// <summary>Schnellbearbeitung: 0,5 °C wärmer (weekly: +/- Buttons im Diagramm).</summary>
    [RelayCommand]
    private void Warmer() => Temperature = Math.Min(MaximumTemperature, Temperature + 0.5);

    [RelayCommand]
    private void Colder() => Temperature = Math.Max(MinimumTemperature, Temperature - 0.5);

    [RelayCommand]
    private void Later() => Shift(StepMinutes);

    [RelayCommand]
    private void Earlier() => Shift(-StepMinutes);

    /// <summary>
    /// Verschiebt den Punkt um 5 Minuten. Der Tagesabschluss 00:00 (= 24:00) bleibt unverändert,
    /// sonst entstünde ein nicht sendbares Profil.
    /// </summary>
    private void Shift(int minutes)
    {
        if (!HasValidUntil || Until == TimeOnly.MinValue)
        {
            return;
        }

        var current = (Until.Hour * 60) + Until.Minute;
        var shifted = Math.Clamp(current + minutes, StepMinutes, 24 * 60 - StepMinutes);
        UntilText = new TimeOnly(shifted / 60, shifted % 60).ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Strenges HH:mm-Format; ungültige Eingaben werden beim Senden gemeldet.</summary>
    public TimeOnly Until => TimeOnly.TryParseExact(
        UntilText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
        ? value
        : TimeOnly.MinValue;

    public bool HasValidUntil => TimeOnly.TryParseExact(
        UntilText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    partial void OnUntilTextChanged(string value)
    {
        OnPropertyChanged(nameof(Until));
        OnPropertyChanged(nameof(HasValidUntil));
    }
}

/// <summary>Ein Wochentag des Wochenprofils (1..13 Schaltpunkte).</summary>
public sealed partial class DayProfileViewModel : ObservableObject
{
    public DayProfileViewModel(DayOfWeek day)
    {
        Day = day;
        Points.CollectionChanged += OnPointsChanged;
    }

    public DayOfWeek Day { get; }

    public string DisplayName => Day.ToString();

    public ObservableCollection<SwitchPointViewModel> Points { get; } = new();

    /// <summary>Grafische Tagesleiste, aus den Schaltpunkten abgeleitet.</summary>
    public ObservableCollection<TimelineSegment> Segments { get; } = new();

    private void OnPointsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        if (args.OldItems is not null)
        {
            foreach (SwitchPointViewModel point in args.OldItems)
            {
                point.PropertyChanged -= OnPointPropertyChanged;
            }
        }

        if (args.NewItems is not null)
        {
            foreach (SwitchPointViewModel point in args.NewItems)
            {
                point.PropertyChanged += OnPointPropertyChanged;
            }
        }

        RebuildSegments();
    }

    private void OnPointPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SwitchPointViewModel.UntilText) or nameof(SwitchPointViewModel.Temperature))
        {
            RebuildSegments();
        }
    }

    /// <summary>
    /// Erzeugt die Segmente von 00:00 bis 24:00. Der Endpunkt 00:00 gilt als 24:00; Punkte mit
    /// ungültiger oder rückwärts laufender Zeit werden übersprungen, damit die Leiste nie bricht.
    /// </summary>
    public void RebuildSegments()
    {
        Segments.Clear();
        var start = 0;
        foreach (var point in Points)
        {
            if (!point.HasValidUntil)
            {
                continue;
            }

            var end = point.Until == TimeOnly.MinValue ? 24 * 60 : (point.Until.Hour * 60) + point.Until.Minute;
            if (end <= start)
            {
                continue;
            }

            Segments.Add(new TimelineSegment(
                start / 1440.0, (end - start) / 1440.0, point.Temperature, ColorFor(point.Temperature)));
            start = end;
        }
    }

    /// <summary>Linearer Verlauf 4,5 °C (blau) bis 30,5 °C (rot) über Gelb.</summary>
    public static string ColorFor(double temperature)
    {
        var ratio = Math.Clamp((temperature - 4.5) / (30.5 - 4.5), 0, 1);
        (int R, int G, int B) cold = (0x3A, 0x7B, 0xD5);
        (int R, int G, int B) mid = (0xF2, 0xC1, 0x4E);
        (int R, int G, int B) warm = (0xD8, 0x4B, 0x3A);
        var (from, to, local) = ratio < 0.5 ? (cold, mid, ratio * 2) : (mid, warm, (ratio - 0.5) * 2);
        int Lerp(int a, int b) => (int)Math.Round(a + ((b - a) * local));
        return $"#{Lerp(from.R, to.R):X2}{Lerp(from.G, to.G):X2}{Lerp(from.B, to.B):X2}";
    }

    public bool CanAddPoint => Points.Count < WeekTemperatureProfile.MaxPointsPerDay;

    public void AddPoint()
    {
        if (!CanAddPoint)
        {
            return;
        }

        // Der letzte Punkt ist 00:00 (= 24:00). Neue Punkte werden davor eingefügt,
        // damit das Profil sendbar bleibt und nicht über Mitternacht läuft.
        var end = Points.Count > 0 ? Points[^1] : null;
        var isEnd = end is not null && end.Until == TimeOnly.MinValue;
        var previous = isEnd
            ? (Points.Count > 1 ? Points[^2] : null)
            : end;
        if (previous is not null && previous.Until >= new TimeOnly(23, 55))
        {
            return;
        }

        var next = previous is null ? new TimeOnly(6, 0) : previous.Until.AddMinutes(60);
        var point = new SwitchPointViewModel
        {
            UntilText = next.ToString("HH:mm", CultureInfo.InvariantCulture),
            Temperature = previous?.Temperature ?? 21.0,
        };
        if (isEnd)
        {
            Points.Insert(Points.Count - 1, point);
        }
        else
        {
            Points.Add(point);
        }

        OnPropertyChanged(nameof(CanAddPoint));
    }

    public void RemovePoint(SwitchPointViewModel point)
    {
        if (Points.Count <= 1 || !Points.Remove(point))
        {
            return;
        }

        OnPropertyChanged(nameof(CanAddPoint));
    }

    public DayProfile ToProfile()
    {
        var invalid = Points.FirstOrDefault(point => !point.HasValidUntil);
        if (invalid is not null)
        {
            throw new MaxCube.Domain.Exceptions.DomainValidationException(
                MaxCube.Domain.Exceptions.DomainValidationCodes.Validation,
                $"Ungültige Uhrzeit '{invalid.UntilText}' – bitte HH:mm eingeben.");
        }

        var profile = new DayProfile(
            Day,
            Points.Select(point => new SwitchPoint(point.Until, new Temperature(point.Temperature))).ToArray());
        DayProfile.Validate(profile);
        return profile;
    }

    /// <summary>Ersetzt den Tag durch den bekannten Default (nur für die Anzeige, nie zum Senden).</summary>
    public void Reset() => Load(new DayProfile(Day, [new SwitchPoint(TimeOnly.MinValue, new Temperature(21.0))]));

    public void Load(DayProfile profile)
    {
        Points.Clear();
        foreach (var point in profile.Points)
        {
            Points.Add(new SwitchPointViewModel
            {
                UntilText = point.Until.ToString("HH:mm"),
                Temperature = point.Temperature.Value,
            });
        }

        OnPropertyChanged(nameof(CanAddPoint));
    }
}
