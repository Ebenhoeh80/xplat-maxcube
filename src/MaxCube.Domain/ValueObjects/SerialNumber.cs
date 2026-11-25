using System.Text;
using MaxCube.Domain.Exceptions;

namespace MaxCube.Domain.ValueObjects;

/// <summary>
/// Seriennummer eines Geräts. BEOBACHTUNG: genau zehn Byte werden übertragen, fehlende
/// Zeichen werden mit Leerzeichen aufgefüllt (Device.java:135-144). Ein Pattern ist nicht
/// belegt (UNBEKANNT): errorcodes.properties:43 benennt MAX1041, liefert aber keine Regex.
/// INFERENZ: die Obergrenze von zehn Zeichen und die Zuordnung zu MAX1040 (`:42`) sind
/// aus der Übertragungslänge abgeleitet; der Java-Konstruktor prüft weder Länge noch Pattern.
/// </summary>
public readonly record struct SerialNumber
{
    public const int WireLength = 10;

    public string Value { get; }

    public SerialNumber(string value)
    {
        if (value is null || value.Length > WireLength)
        {
            throw new DomainValidationException(
                DomainValidationCodes.SerialNumberInvalidLength,
                "Serial number must contain at most 10 characters.");
        }

        Value = value;
    }

    public byte[] ToWireBytes()
    {
        // default(SerialNumber) kann die Konstruktorprüfung umgehen; kein NullReference-Fehler.
        if (Value is null)
        {
            throw new DomainValidationException(
                DomainValidationCodes.SerialNumberInvalidLength, "Serial number is not initialized.");
        }

        var bytes = new byte[WireLength];
        var source = Encoding.ASCII.GetBytes(Value);
        Array.Copy(source, bytes, source.Length);
        for (var index = source.Length; index < WireLength; index++)
        {
            bytes[index] = (byte)' ';
        }

        return bytes;
    }

    public override string ToString() => Value;
}
