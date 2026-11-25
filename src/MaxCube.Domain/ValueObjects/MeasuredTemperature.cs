using MaxCube.Domain.Exceptions;

namespace MaxCube.Domain.ValueObjects;

/// <summary>
/// Gemessene Ist-Temperatur. Im Gegensatz zu <see cref="Temperature"/> gibt es keine
/// Sonderzustände AUS/AN: 4,5 °C und 30,5 °C sind echte Messwerte, keine Befehlswerte.
/// Der Java-Code unterscheidet diese Ebenen ebenfalls (Room.java:39,134 vs.
/// Temperature.java:31-49). 0,0 °C bedeutet „keine Messung“ (protokoll-referenzen.md:139-140).
/// </summary>
public readonly record struct MeasuredTemperature
{
    public const double MinValue = 0.0;
    public const double MaxValue = 40.0;

    public double Value { get; }

    /// <summary>0,0 °C ist der belegte „keine Messung“-Zustand.</summary>
    public bool IsValid => Value > MinValue;

    public MeasuredTemperature(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < MinValue || value > MaxValue)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation,
                $"Measured temperature {value} is outside {MinValue}..{MaxValue}.");
        }

        Value = value;
    }

    /// <summary>
    /// Wandthermostat-LSB/Highbit (Zehntel °C). Ein Wert außerhalb 0..40 °C ist kein
    /// plausibler Messwert: er wird verworfen (0 = „keine Messung“) statt zu einem
    /// erfundenen 40,0 °C zu werden (Sonnet N4). Java zeigt den Rohwert ungeprüft an;
    /// die 40-°C-Grenze ist eine INFERENZ und gilt nur für diese Anzeige.
    /// </summary>
    public static MeasuredTemperature FromWireTenths(int tenths)
    {
        var value = tenths / 10.0;
        return value is >= MinValue and <= MaxValue ? new MeasuredTemperature(value) : new MeasuredTemperature(MinValue);
    }

    public override string ToString() => $"{Value:F1} °C";
}
