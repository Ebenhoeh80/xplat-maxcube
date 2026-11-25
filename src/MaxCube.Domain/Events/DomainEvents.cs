using MaxCube.Domain.ValueObjects;

namespace MaxCube.Domain.Events;

/// <summary>Fachliche Änderung am Aggregat. Bewusst ohne Zeitstempel (deterministische Tests).</summary>
public abstract record DomainEvent;

/// <summary>Raumzustand hat sich geändert (Raum ist die Steuereinheit, Room.java:14-41).</summary>
public sealed record RoomStateChanged(int RoomId, string Reason) : DomainEvent;

/// <summary>Gerätezustand hat sich geändert (Device.java:13-25).</summary>
public sealed record DeviceStateChanged(RadioAddress RadioAddress, string Reason) : DomainEvent;
