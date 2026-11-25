using System.Text.RegularExpressions;

namespace MaxCube.Application.Models;

/// <summary>
/// Fehlercode-Katalog MAX0001–MAX1051 (extracted/essentials/errorcodes.properties:1-53,
/// docs/maxcube-protocol.md:556-586). Die Symbole sind belegt; die deutschen und englischen
/// Erklärungen sind lesbare Deutungen der Symbolnamen (INFERENZ) und keine Herstellertexte.
/// </summary>
public static partial class ErrorCatalog
{
    public sealed record Entry(string Code, string Symbol, string German, string English);

    private static readonly Entry[] Entries =
    [
        new("MAX0001", "GeneralTechnicalFailure", "Allgemeiner technischer Fehler", "General technical failure"),
        new("MAX1001", "AESKeyInvalidLength", "AES-Schlüssel hat eine ungültige Länge", "AES key has an invalid length"),
        new("MAX1002", "AESKeyIsNotHex", "AES-Schlüssel ist nicht hexadezimal", "AES key is not hexadecimal"),
        new("MAX1003", "AdminValidator", "Administrationsprüfung fehlgeschlagen", "Administration validation failed"),
        new("MAX1004", "AlreadyConnectedToCube", "Es besteht bereits eine Verbindung zum Cube", "Already connected to a cube"),
        new("MAX1005", "Application", "Anwendungsfehler", "Application error"),
        new("MAX1006", "CanNotUpdateState", "Zustand konnte nicht aktualisiert werden", "State could not be updated"),
        new("MAX1007", "ConnectionLostUpdate", "Verbindung während des Updates verloren", "Connection lost during update"),
        new("MAX1008", "CubeCommunicationFailed", "Kommunikation mit dem Cube fehlgeschlagen", "Cube communication failed"),
        new("MAX1009", "CubeIsInInclusionMode", "Der Cube befindet sich im Anlernmodus", "The cube is in inclusion mode"),
        new("MAX1010", "CubeNotInventoried", "Cube ist nicht inventarisiert", "Cube is not inventoried"),
        new("MAX1011", "DeserializationFailed", "Daten konnten nicht gelesen werden", "Deserialization failed"),
        new("MAX1012", "Device", "Gerätefehler", "Device error"),
        new("MAX1013", "DeviceClientCommand", "Gerätebefehl fehlgeschlagen", "Device command failed"),
        new("MAX1014", "DeviceIsNoNewDevice", "Das Gerät ist kein neues Gerät", "The device is not a new device"),
        new("MAX1015", "DeviceNameAlreadyExists", "Ein Gerät mit diesem Namen existiert bereits", "A device with this name already exists"),
        new("MAX1016", "DownloadURLIsNull", "Download-Adresse fehlt", "Download URL is missing"),
        new("MAX1017", "EncryptionFailed", "Verschlüsselung fehlgeschlagen", "Encryption failed"),
        new("MAX1018", "FileWithoutContent", "Datei ohne Inhalt", "File without content"),
        new("MAX1019", "FirmwareVersionIsInvalid", "Firmware-Version ist ungültig", "Firmware version is invalid"),
        new("MAX1020", "FirmwareVersionIsNull", "Firmware-Version fehlt", "Firmware version is missing"),
        new("MAX1021", "InvalidArguments", "Ungültige Argumente", "Invalid arguments"),
        new("MAX1022", "InvalidDeviceRadioAddress", "Ungültige oder unbekannte Funkadresse", "Invalid or unknown radio address"),
        new("MAX1023", "InvalidFirmwareFileUpdate", "Ungültige Firmware-Datei", "Invalid firmware file"),
        new("MAX1024", "InvalidInventoryListEntry", "Ungültiger Eintrag in der Inventarliste", "Invalid inventory list entry"),
        new("MAX1025", "InvalidResponseUpdate", "Ungültige Antwort beim Update", "Invalid response during update"),
        new("MAX1026", "InvalidUserName", "Ungültiger Benutzername", "Invalid user name"),
        new("MAX1027", "LocalValidation", "Lokale Validierung fehlgeschlagen", "Local validation failed"),
        new("MAX1028", "Monitoring", "Überwachungsfehler", "Monitoring error"),
        new("MAX1029", "NameIsNull", "Es wurde kein Name angegeben", "No name was given"),
        new("MAX1030", "NameTooLong", "Der Name ist zu lang (höchstens 32 Zeichen)", "The name is too long (at most 32 characters)"),
        new("MAX1031", "NoUserName", "Kein Benutzername", "No user name"),
        new("MAX1032", "NotConnectedToCube", "Keine Verbindung zum Cube", "Not connected to a cube"),
        new("MAX1033", "NotImplemented", "Nicht implementiert", "Not implemented"),
        new("MAX1034", "RadioAddressHasInvalidLength", "Funkadresse hat eine ungültige Länge", "Radio address has an invalid length"),
        new("MAX1035", "RadioAddressIsNotHex", "Funkadresse ist nicht hexadezimal", "Radio address is not hexadecimal"),
        new("MAX1036", "RadiogramCreationFailed", "Funktelegramm konnte nicht erzeugt werden", "Radiogram creation failed"),
        new("MAX1037", "RemoteAccessDisabled", "Fernzugriff ist deaktiviert", "Remote access is disabled"),
        new("MAX10038", "RemoteServiceUnavailable", "Fernzugriffsdienst nicht verfügbar", "Remote service unavailable"),
        new("MAX1039", "SecurityViolation", "Sicherheitsverletzung", "Security violation"),
        new("MAX1040", "SerialNumberInvalidLength", "Seriennummer hat eine ungültige Länge", "Serial number has an invalid length"),
        new("MAX1041", "SerialNumberInvalidPattern", "Seriennummer hat ein ungültiges Muster", "Serial number has an invalid pattern"),
        new("MAX1042", "Serialization", "Daten konnten nicht geschrieben werden", "Serialization failed"),
        new("MAX1043", "SoftwareInformationHasInvalidCountOfArguments", "Softwareinformation unvollständig", "Software information has an invalid number of arguments"),
        new("MAX1044", "SoftwareVersionIsInvalid", "Software-Version ist ungültig", "Software version is invalid"),
        new("MAX1045", "SoftwareVersionIsNull", "Software-Version fehlt", "Software version is missing"),
        new("MAX1046", "SubjectNotAuthorized", "Nicht berechtigt", "Subject not authorized"),
        new("MAX1047", "UnknownCredentials", "Unbekannte Zugangsdaten", "Unknown credentials"),
        new("MAX1048", "Update", "Updatefehler", "Update error"),
        new("MAX1049", "UpdateAlreadyInProgress", "Ein Update läuft bereits", "An update is already in progress"),
        new("MAX1050", "Validation", "Eingabe ist ungültig", "Validation failed"),
        new("MAX1051", "WrongSize", "Falsche Größe", "Wrong size"),
    ];

    private static readonly IReadOnlyDictionary<string, Entry> ByCode =
        Entries.ToDictionary(entry => entry.Code, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<Entry> All => Entries;

    public static Entry? Find(string? code)
        => code is not null && ByCode.TryGetValue(code.Trim(), out var entry) ? entry : null;

    /// <summary>Sucht den ersten bekannten Fehlercode in einer Meldung und liefert eine lesbare Erklärung.</summary>
    public static string? Describe(string? message, bool german = true)
    {
        if (string.IsNullOrEmpty(message))
        {
            return null;
        }

        foreach (Match match in CodePattern().Matches(message))
        {
            if (Find(match.Value) is { } entry)
            {
                return $"{entry.Code}: {(german ? entry.German : entry.English)}";
            }
        }

        return null;
    }

    [GeneratedRegex("MAX\\d{4,5}", RegexOptions.IgnoreCase)]
    private static partial Regex CodePattern();
}

/// <summary>Firmwarefenster, das die App bedient (max.local.properties:17-18, CubeConnection.java:57,64).</summary>
public static class FirmwareSupport
{
    public const int Minimum = 273;
    public const int Maximum = 275;

    public static bool IsSupported(int firmware) => firmware is >= Minimum and <= Maximum;
}
