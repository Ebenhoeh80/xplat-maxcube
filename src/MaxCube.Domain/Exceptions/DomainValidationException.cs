namespace MaxCube.Domain.Exceptions;

/// <summary>
/// Fachlicher Validierungsfehler mit dem Fehlercode aus errorcodes.properties.
/// Die Codes sind Beobachtungen (extracted/essentials/errorcodes.properties:24,36,37,42,43,52).
/// </summary>
public sealed class DomainValidationException : Exception
{
    public string Code { get; }

    public DomainValidationException(string code, string message)
        : base($"{code}: {message}")
    {
        Code = code;
    }
}

/// <summary>Nur belegte Fehlercodes; ein unbekannter Code wird nicht erfunden.</summary>
public static class DomainValidationCodes
{
    public const string DeviceNameAlreadyExists = "MAX1015";
    public const string InvalidDeviceRadioAddress = "MAX1022";
    public const string NameIsNull = "MAX1029";
    public const string NameTooLong = "MAX1030";
    public const string RadioAddressHasInvalidLength = "MAX1034";
    public const string RadioAddressIsNotHex = "MAX1035";
    public const string SerialNumberInvalidLength = "MAX1040";

    /// <summary>Reserviert: das Serial-Number-Pattern ist UNBEKANNT (errorcodes.properties:43).</summary>
    public const string SerialNumberInvalidPattern = "MAX1041";

    public const string Validation = "MAX1050";
}
