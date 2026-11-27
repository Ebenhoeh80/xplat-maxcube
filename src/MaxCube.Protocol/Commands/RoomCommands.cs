using MaxCube.Domain.Enums;
using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;
using MaxCube.Protocol.Radiograms;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Protocol.Commands;

/// <summary>SetRoomAutoMode.java:40-43 – Frame 0x40, Modus 0.</summary>
public sealed class SetRoomAutoModeCommand : SendRadiogramCommand
{
    public SetRoomAutoModeCommand(RadioAddress master, int roomId)
        : base(RadiogramBuilder.ForRoom(FrameTypes.ControlMode, master, roomId)
            .WriteControlMode(10, 6, RoomControlMode.Auto)
            .ToBytes())
    { }
}

/// <summary>SetRoomPermanentMode.java:36-39 – Frame 0x40, Modus 1 + Solltemperatur in Byte 10.</summary>
public sealed class SetRoomPermanentModeCommand : SendRadiogramCommand
{
    public SetRoomPermanentModeCommand(RadioAddress master, int roomId, Temperature temperature)
        : base(RadiogramBuilder.ForRoom(FrameTypes.ControlMode, master, roomId)
            .WriteControlMode(10, 6, RoomControlMode.Permanently)
            .WriteTemperature(10, 0, temperature)
            .ToBytes())
    { }
}

/// <summary>
/// SetRoomTemporaryMode.java:45-48 – Frame 0x40, Modus 2, Temperatur und Enddatum.
/// Die ungewöhnlichen Bitpositionen (Monat in 11/4 und 12/7, Tag 11/0, Jahr 12/0, Zeit 13/0)
/// sind absichtlich Java-getreu; RadiogramSpecification.createBitSet() überschreibt sich
/// überlagernde Felder in der dortigen Reihenfolge (RadiogramSpecification.java:128-137).
/// </summary>
public sealed class SetRoomTemporaryModeCommand : SendRadiogramCommand
{
    public SetRoomTemporaryModeCommand(RadioAddress master, int roomId, Temperature temperature, DateTimeOffset until)
        : base(RadiogramBuilder.ForRoom(FrameTypes.ControlMode, master, roomId)
            .WriteControlMode(10, 6, RoomControlMode.Temporary)
            .WriteTemperature(10, 0, temperature)
            .WriteMonth(11, 4, until)
            .WriteMonth(12, 7, until)
            .WriteDay(11, 0, until)
            .WriteYear(12, 0, until)
            .WriteTime30Minutes(13, 0, until)
            .ToBytes())
    { }
}

/// <summary>
/// SetRoomConfigTemperatures.java / AbstractRoomClientCommand.java:117-121 – Frame 0x11.
/// Reihenfolge: Comfort, Eco, Max, Min, Offset, Fenster-auf-Temperatur, Fenster-auf-Dauer.
/// </summary>
public sealed class SetRoomConfigurationCommand : SendRadiogramCommand
{
    public SetRoomConfigurationCommand(
        RadioAddress master,
        int roomId,
        Temperature comfort,
        Temperature eco,
        Temperature maximum,
        Temperature minimum,
        double offset,
        Temperature windowOpen,
        int windowOpenMinutes)
        : base(RadiogramBuilder.ForRoom(FrameTypes.ConfigTemperatures, master, roomId)
            .WriteTemperature(10, 0, comfort)
            .WriteTemperature(11, 0, eco)
            .WriteTemperature(12, 0, maximum)
            .WriteTemperature(13, 0, minimum)
            .WriteOffsetTemperature(14, offset)
            .WriteTemperature(15, 0, windowOpen)
            .WriteWindowOpenTime(16, windowOpenMinutes)
            .ToBytes())
    { }
}

/// <summary>
/// ConfigValveFunctions (0x12): Boost-Dauer/-Ventilwinkel, Entkalkung, maximale
/// Ventilöffnung und Ventil-Offset. INFERENZ: Das Schreibformat ist das Spiegelbild des
/// belegten Lesepfads (CubeStateAssembler: config[7] Bit 5..7 Boost-Dauer, Bit 0..4
/// Ventilwinkel/5; config[8] Entkalkung Tag/Stunde; config[9]/[10] Ventilwerte) und der
/// Java-Renderer BoostTime/WeekDay (RadiogramSpecification.java:281-290). Länge 14 Byte
/// wie Java (ToBytes(0)); am 25.11.2026 mit gelesenen Ursprungswerten hardwaregesendet
/// und mit `S:36,0,30` quittiert, Read-back der fünf Felder weiterhin offen.
/// Der Aufrufer reicht die vom Cube gelesenen Rohwerte durch.
/// </summary>
public sealed class SetRoomValveFunctionsCommand : SendRadiogramCommand
{
    private static readonly int[] BoostMinutes = [0, 5, 10, 15, 20, 25, 30, 60];

    public SetRoomValveFunctionsCommand(
        RadioAddress master,
        int roomId,
        int boostMinutes,
        int boostValveAngle,
        DayOfWeek decalcificationDay,
        int decalcificationHour,
        int maxValveRaw,
        int valveOffsetRaw)
        : base(Build(master, roomId, boostMinutes, boostValveAngle, decalcificationDay,
            decalcificationHour, maxValveRaw, valveOffsetRaw))
    { }

    /// <summary>Boost-Dauer in 3-Bit-Code; nur die Werte der Lesetabelle sind erlaubt.</summary>
    public static int BoostCode(int minutes)
    {
        var index = Array.IndexOf(BoostMinutes, minutes);
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "Boost duration must be 0, 5, 10, 15, 20, 25, 30 or 60 minutes.");
        }

        return index;
    }

    private static byte[] Build(
        RadioAddress master, int roomId, int boostMinutes, int valveAngle, DayOfWeek day,
        int hour, int maxValveRaw, int valveOffsetRaw)
    {
        if (valveAngle is < 0 or > 155 || valveAngle % 5 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(valveAngle), valveAngle, "Valve angle must be a multiple of 5 in 0..155.");
        }

        if (hour is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(hour), hour, "Decalcification hour must be 0..23.");
        }

        if (maxValveRaw is < 0 or > 255 || valveOffsetRaw is < 0 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(maxValveRaw), "Valve raw values must fit in one byte.");
        }

        return RadiogramBuilder.ForRoom(FrameTypes.ConfigValveFunctions, master, roomId)
            .WriteBits(10, 5, 3, BoostCode(boostMinutes))
            .WriteBits(10, 0, 5, valveAngle / 5)
            .WriteBits(11, 5, 3, ((int)day + 1) % 7)
            .WriteBits(11, 0, 5, hour)
            .WriteBits(12, 0, 8, maxValveRaw)
            .WriteBits(13, 0, 8, valveOffsetRaw)
            // Java kürzt auf Byte 13 (14 Byte), nicht auf die 17-Byte-Standardlänge
            // (RadiogramSpecification.java:140-147). FAKT (Code), hardwaregesendet 25.11.2026.
            .ToBytes(0);
    }
}

/// <summary>
/// SwitchDisplaySetPointActual (0x82) nur für Wandthermostate: Byte 10 = 0x04 zeigt die
/// Ist-, 0x00 die Solltemperatur. Live am echten Wandthermostat bestätigt
/// (ha-maxcube display.py:16-30, ha-maxcube-erkenntnisse.md:46,240); die 11-Byte-Länge
/// entspricht der dort bewährten Nachricht.
/// </summary>
public sealed class SetDisplayModeCommand : SendRadiogramCommand
{
    public SetDisplayModeCommand(RadioAddress wallThermostat, bool showActualTemperature)
        : base(RadiogramBuilder.ForDevice(FrameTypes.SwitchDisplaySetPointActual, wallThermostat)
            .WriteBits(10, 0, 8, showActualTemperature ? 0x04 : 0x00)
            .ToBytes(11))
    { }
}

/// <summary>
/// SetGroupAddress (0x22): ordnet ein Gerät einer Gruppe (= Raum-ID) zu. INFERENZ aus
/// FrameType.java:18 und der Community-Spezifikation: Gruppen-ID als Nutzlast-Byte 10.
/// </summary>
public sealed class SetGroupAddressCommand : SendRadiogramCommand
{
    public SetGroupAddressCommand(RadioAddress device, int groupId)
        : base(RadiogramBuilder.ForDevice(FrameTypes.SetGroupAddress, device)
            .WriteBits(10, 0, 8, groupId)
            .ToBytes(11))
    { }
}

/// <summary>
/// RemoveGroupAddress (0x23): 11 Byte mit Raum-ID in Byte 10, wie Java
/// (AbstractRoomClientCommand.java:127-129). Die ältere 10-Byte-Variante war ein
/// statisch belegter Mismatch; am 25.11.2026 mit Dummy-Adresse hardwaregesendet.
/// </summary>
public sealed class RemoveGroupAddressCommand : SendRadiogramCommand
{
    public RemoveGroupAddressCommand(RadioAddress device, int roomId)
        : base(RadiogramBuilder.ForDevice(FrameTypes.RemoveGroupAddress, device)
            .WriteBits(10, 0, 8, roomId)
            .ToBytes(11))
    { }
}

/// <summary>
/// AddLinkPartner (0x20)/RemoveLinkPartner (0x21): Nutzlast = Partneradresse (3 Byte,
/// big-endian) und numerischer Gerätetyp des Partners (INFERENZ, FrameType.java:16-17).
/// </summary>
public sealed class LinkPartnerCommand : SendRadiogramCommand
{
    public LinkPartnerCommand(RadioAddress device, RadioAddress partner, int partnerNumericType, bool add)
        : base(Build(RadiogramBuilder.ForDevice(
            add ? FrameTypes.AddLinkPartner : FrameTypes.RemoveLinkPartner, device), partner, partnerNumericType))
    { }

    internal static byte[] Build(RadiogramBuilder builder, RadioAddress partner, int partnerNumericType)
    {
        var bytes = partner.ToBytes();
        return builder
            .WriteBits(10, 0, 8, bytes[0])
            .WriteBits(11, 0, 8, bytes[1])
            .WriteBits(12, 0, 8, bytes[2])
            .WriteBits(13, 0, 8, partnerNumericType)
            .ToBytes(14);
    }
}

/// <summary>
/// Java-Gruppenkopf-Variante von AddLinkPartner/RemoveLinkPartner beim Anlernen und
/// Entfernen: Byte 1 Bit 2 = 1, Byte 9 = Raum-ID, Byte 6..8 = bestätigendes
/// Master-Thermostat (AbstractRoomDeviceClientCommand.java:77-79,
/// AssociateNewDeviceToRoom.java:91-94, DisassociateDeviceFromRoom.java:100-103).
/// Die fachliche Wirkung ist nur INFERENZ; mit Dummy-Adresse hardwaregesendet.
/// </summary>
public sealed class LinkPartnerInRoomCommand : SendRadiogramCommand
{
    public LinkPartnerInRoomCommand(
        RadioAddress master, int roomId, RadioAddress partner, int partnerNumericType, bool add)
        : base(LinkPartnerCommand.Build(
            RadiogramBuilder.ForRoom(add ? FrameTypes.AddLinkPartner : FrameTypes.RemoveLinkPartner, master, roomId),
            partner,
            partnerNumericType))
    { }
}

/// <summary>Ein Tagesprofil-Telegramm (0x10). Für 8..13 Punkte zwei Instanzen senden.</summary>
public sealed class SetRoomWeekTemperatureProfileCommand : SendRadiogramCommand
{
    public SetRoomWeekTemperatureProfileCommand(byte[] payload)
        : base(payload) { }

    public SetRoomWeekTemperatureProfileCommand(DayProfile profile, RadioAddress master, int roomId, bool secondTelegram = false)
        : base(BuildPayload(profile, master, roomId, secondTelegram)) { }

    private static byte[] BuildPayload(DayProfile profile, RadioAddress master, int roomId, bool secondTelegram)
    {
        var telegrams = WeekProfileSerializer.Serialize(profile, master, roomId);
        var index = secondTelegram ? 1 : 0;
        if (index >= telegrams.Count)
        {
            throw new ArgumentException("A profile with at most 7 switch points has no second telegram.", nameof(profile));
        }

        return telegrams[index];
    }
}
