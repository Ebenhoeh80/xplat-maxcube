using System.Globalization;
using MaxCube.Domain.Exceptions;

namespace MaxCube.Domain.ValueObjects;

/// <summary>
/// Temperatur mit den Sonderzuständen AUS und AN. Immutable Ausführung des Java-Wertobjekts.
/// Belege: Temperature.java:7-16 (Konstanten/Range), :31-49 (Normalisierung),
/// :61-64 (isValid), :76-80 (fromInt). Abweichung: exakter Wertvergleich statt der
/// Java-Toleranz &lt; 0,1 °C (`:82-93`), siehe docs/roadmap.md AP-2.1.
/// </summary>
public readonly record struct Temperature
{
    public const double OffValue = 4.5;
    public const double OnValue = 30.5;
    public const double DefaultValue = 21.0;
    public const double MinValue = 0.0;
    public const double MaxValue = 40.0;

    public static readonly Temperature Default = new(DefaultValue);
    public static readonly Temperature Off = new(OffValue);
    public static readonly Temperature On = new(OnValue);

    public double Value { get; }

    public bool IsOn { get; }

    public bool IsOff { get; }

    public bool IsValid => !IsOn && !IsOff;

    public Temperature(double value)
    {
        // INFERENZ: @Range(0..40) (Temperature.java:13) gilt auch für den Konstruktor;
        // der Java-Konstruktor selbst normalisiert ohne Bereichsprüfung.
        if (double.IsNaN(value) || double.IsInfinity(value) || value < MinValue || value > MaxValue)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation,
                $"Temperature {value} is outside the validated range {MinValue}..{MaxValue}.");
        }

        if (value <= OffValue)
        {
            Value = OffValue;
            IsOff = true;
            IsOn = false;
        }
        else if (value >= OnValue)
        {
            Value = OnValue;
            IsOn = true;
            IsOff = false;
        }
        else
        {
            Value = value;
            IsOn = false;
            IsOff = false;
        }
    }

    /// <summary>
    /// Drahtwert wird durch 2 geteilt (Temperature.java:76-80). Werte außerhalb 0..40 werden
    /// vor der Konstruktorprüfung geklemmt, weil Java setValue() auch dort nach OFF/ON
    /// normalisiert (`:31-49`; CubeConfigurationSerializer.java:79 maskiert mit &amp; 63).
    /// </summary>
    public static Temperature FromWire(int wireValue) => new(Math.Clamp(wireValue / 2.0, MinValue, MaxValue));

    /// <summary>INFERENZ: Inverse zu FromWire, auf den nächsten 0,5-°C-Schritt gerundet.</summary>
    public int ToWire() => (int)Math.Round(Value * 2, MidpointRounding.AwayFromZero);

    public override string ToString() => $"{Value.ToString("F1", CultureInfo.InvariantCulture)}°C";
}
