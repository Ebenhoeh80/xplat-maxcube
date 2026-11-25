namespace MaxCube.Domain.Aggregates;

/// <summary>
/// Konfigurierte Obergrenzen aus extracted/settings/max.local.properties:1-3.
/// Die Grenzen sind dort als Configuration belegt; die AL-Klassen selbst prüfen sie nicht
/// (domain-model.md:94, :138). Die Durchsetzung im Aggregat ist daher eine bewusste
/// Neuimplementierungsentscheidung (INFERENZ).
/// </summary>
public static class DomainLimits
{
    public const int MaxNameLength = 32;
    public const int MaxRoomCount = 20;
    public const int MaxDeviceCount = 140;
}
