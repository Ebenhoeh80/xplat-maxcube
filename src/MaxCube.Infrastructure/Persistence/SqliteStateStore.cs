using MaxCube.Application.Abstractions;
using MaxCube.Application.Models;
using MaxCube.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace MaxCube.Infrastructure.Persistence;

/// <summary>
/// SQLite-Cache und Einstellungen. Es gibt keine Datenbank im Original; dieser Store ist
/// ein Neubau (target-architecture.md:15). Der Cube bleibt Source of Truth, der Cache
/// wird bei jedem Connect vollständig überschrieben (A3, Konsistenzregel Abschnitt 7).
/// </summary>
public sealed class SqliteStateStore : ICubeStateStore, IAsyncDisposable
{
    /// <summary>Erste Schemafassung mit Wochenprofil-Spalte und Versionssettings.</summary>
    public const int CurrentSchemaVersion = 1;

    private readonly SqliteConnection _connection;
    private readonly IClock _clock;
    private readonly string _appVersion;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqliteStateStore(
        string connectionString, IClock clock, IAppVersionProvider? appVersionProvider = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A SQLite connection string is required.", nameof(connectionString));
        }

        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _appVersion = string.IsNullOrWhiteSpace(appVersionProvider?.Version)
            ? "unbekannt"
            : appVersionProvider!.Version;
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS cubes (
                serial TEXT PRIMARY KEY,
                firmware INTEGER NOT NULL,
                rf_address TEXT NOT NULL,
                daylight INTEGER NOT NULL,
                captured_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS rooms (
                serial TEXT NOT NULL,
                id INTEGER NOT NULL,
                name TEXT NOT NULL,
                master_address TEXT NOT NULL,
                set_point REAL NOT NULL,
                actual REAL NOT NULL,
                control_mode INTEGER NOT NULL,
                temperature_mode INTEGER NOT NULL,
                comfort REAL NOT NULL,
                eco REAL NOT NULL,
                maximum REAL NOT NULL,
                minimum REAL NOT NULL,
                window_open REAL NOT NULL,
                window_open_minutes INTEGER NOT NULL,
                temp_offset REAL NOT NULL,
                boost_minutes INTEGER NOT NULL,
                boost_valve INTEGER NOT NULL,
                decalc_day INTEGER NOT NULL,
                decalc_hour INTEGER NOT NULL,
                PRIMARY KEY (serial, id)
            );
            CREATE TABLE IF NOT EXISTS devices (
                serial TEXT NOT NULL,
                address TEXT NOT NULL,
                type INTEGER NOT NULL,
                numeric_type INTEGER NOT NULL,
                serial_number TEXT NOT NULL,
                name TEXT NOT NULL,
                room_id INTEGER NOT NULL,
                state_info INTEGER NOT NULL,
                radio_state INTEGER NOT NULL,
                PRIMARY KEY (serial, address)
            );
            CREATE TABLE IF NOT EXISTS audit_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                message TEXT NOT NULL,
                at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        // M-F: bestehende Caches haben noch keine Profilspalte. CREATE TABLE IF NOT EXISTS
        // ändert sie nicht, daher bewusst eine additive, idempotente Migration.
        using var columns = _connection.CreateCommand();
        columns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('rooms') WHERE name = 'week_profile'";
        if (Convert.ToInt32(columns.ExecuteScalar()) == 0)
        {
            using var alter = _connection.CreateCommand();
            alter.CommandText = "ALTER TABLE rooms ADD COLUMN week_profile TEXT NULL";
            alter.ExecuteNonQuery();
        }

        // Versionierung des Caches: PRAGMA user_version ist die kompakte, von SQLite
        // selbst verwaltete Zahl; die both Settings sind für Anzeige und Diagnose
        // lesbar. Beides ist idempotent und überschreibt keine Nutzerdaten.
        using var version = _connection.CreateCommand();
        version.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion}";
        version.ExecuteNonQuery();
        UpsertSetting("schema.version", CurrentSchemaVersion.ToString());
        UpsertSetting("app.version", _appVersion);
    }

    private void UpsertSetting(string key, string value)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT value FROM settings WHERE key = $key";
            command.Parameters.AddWithValue("$key", key);
            return (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO settings (key, value) VALUES ($key, $value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAuditAsync(
        string message, AuditSource source = AuditSource.User, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "INSERT INTO audit_log (message, at) VALUES ($message, $at)";
            // Sprachneutraler Quell-Tag im Rohtext; Audit ist historisches Beweismaterial.
            command.Parameters.AddWithValue(
                "$message", $"[{source.ToString().ToLowerInvariant()}] {message}");
            command.Parameters.AddWithValue("$at", _clock.Now.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> DeleteAuditOlderThanAsync(
        DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var command = _connection.CreateCommand();
            // julianday() versteht den gespeicherten ISO-8601-Text inklusive Zeitzonen-Offset.
            command.CommandText = "DELETE FROM audit_log WHERE julianday(at) < julianday($cutoff)";
            command.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> ReadAuditAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var command = _connection.CreateCommand();
            // Die UI zeigt die letzten 100 Einträge; ein unbegrenztes Laden würde bei
            // langem Betrieb die gesamte Historie in den Speicher ziehen.
            command.CommandText = "SELECT message FROM (SELECT message, id FROM audit_log ORDER BY id DESC LIMIT 500) ORDER BY id";
            var messages = new List<string>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                messages.Add(reader.GetString(0));
            }

            return messages;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(CubeSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            using (var cube = _connection.CreateCommand())
            {
                cube.Transaction = transaction;
                cube.CommandText = """
                    INSERT INTO cubes (serial, firmware, rf_address, daylight, captured_at)
                    VALUES ($serial, $firmware, $rf, $daylight, $captured)
                    ON CONFLICT(serial) DO UPDATE SET
                        firmware = excluded.firmware,
                        rf_address = excluded.rf_address,
                        daylight = excluded.daylight,
                        captured_at = excluded.captured_at
                    """;
                cube.Parameters.AddWithValue("$serial", snapshot.SerialNumber);
                cube.Parameters.AddWithValue("$firmware", snapshot.FirmwareVersion);
                cube.Parameters.AddWithValue("$rf", snapshot.RfAddress);
                cube.Parameters.AddWithValue("$daylight", snapshot.DaylightSaving ? 1 : 0);
                cube.Parameters.AddWithValue("$captured", snapshot.CapturedAt.ToString("O"));
                await cube.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await ReplaceRowsAsync(transaction, "rooms", snapshot.SerialNumber, cancellationToken).ConfigureAwait(false);
            foreach (var room in snapshot.Rooms)
            {
                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO rooms (
                        serial, id, name, master_address, set_point, actual, control_mode, temperature_mode,
                        comfort, eco, maximum, minimum, window_open, window_open_minutes, temp_offset,
                        boost_minutes, boost_valve, decalc_day, decalc_hour, week_profile)
                    VALUES (
                        $serial, $id, $name, $master, $setPoint, $actual, $control, $tempMode,
                        $comfort, $eco, $maximum, $minimum, $windowOpen, $windowMinutes, $offset,
                        $boostMinutes, $boostValve, $decalcDay, $decalcHour, $weekProfile)
                    """;
                AddRoomParameters(command, snapshot.SerialNumber, room);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await ReplaceRowsAsync(transaction, "devices", snapshot.SerialNumber, cancellationToken).ConfigureAwait(false);
            foreach (var device in snapshot.Devices)
            {
                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO devices (
                        serial, address, type, numeric_type, serial_number, name, room_id, state_info, radio_state)
                    VALUES (
                        $serial, $address, $type, $numericType, $serialNumber, $name, $roomId, $stateInfo, $radioState)
                    """;
                command.Parameters.AddWithValue("$serial", snapshot.SerialNumber);
                command.Parameters.AddWithValue("$address", device.Address);
                command.Parameters.AddWithValue("$type", (int)device.Type);
                command.Parameters.AddWithValue("$numericType", device.NumericType);
                command.Parameters.AddWithValue("$serialNumber", device.SerialNumber);
                command.Parameters.AddWithValue("$name", device.Name);
                command.Parameters.AddWithValue("$roomId", device.RoomId);
                command.Parameters.AddWithValue("$stateInfo", (int)device.StateInfo);
                command.Parameters.AddWithValue("$radioState", (int)device.RadioState);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CubeSnapshot?> LoadAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string firmware;
            string rfAddress;
            bool daylight;
            string captured;
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT firmware, rf_address, daylight, captured_at FROM cubes WHERE serial = $serial
                    """;
                command.Parameters.AddWithValue("$serial", serialNumber);
                using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                firmware = reader.GetInt32(0).ToString();
                rfAddress = reader.GetString(1);
                daylight = reader.GetInt32(2) != 0;
                captured = reader.GetString(3);
            }

            var rooms = new List<RoomSnapshot>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM rooms WHERE serial = $serial ORDER BY id";
                command.Parameters.AddWithValue("$serial", serialNumber);
                using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rooms.Add(new RoomSnapshot(
                        reader.GetInt32(reader.GetOrdinal("id")),
                        reader.GetString(reader.GetOrdinal("name")),
                        reader.GetString(reader.GetOrdinal("master_address")),
                        reader.GetDouble(reader.GetOrdinal("set_point")),
                        reader.GetDouble(reader.GetOrdinal("actual")),
                        (RoomControlMode)reader.GetInt32(reader.GetOrdinal("control_mode")),
                        (RoomTemperatureMode)reader.GetInt32(reader.GetOrdinal("temperature_mode")),
                        reader.GetDouble(reader.GetOrdinal("comfort")),
                        reader.GetDouble(reader.GetOrdinal("eco")),
                        reader.GetDouble(reader.GetOrdinal("maximum")),
                        reader.GetDouble(reader.GetOrdinal("minimum")),
                        reader.GetDouble(reader.GetOrdinal("window_open")),
                        reader.GetInt32(reader.GetOrdinal("window_open_minutes")),
                        reader.GetDouble(reader.GetOrdinal("temp_offset")),
                        reader.GetInt32(reader.GetOrdinal("boost_minutes")),
                        reader.GetInt32(reader.GetOrdinal("boost_valve")),
                        reader.GetInt32(reader.GetOrdinal("decalc_day")),
                        reader.GetInt32(reader.GetOrdinal("decalc_hour")),
                        reader.IsDBNull(reader.GetOrdinal("week_profile"))
                            ? null
                            : reader.GetString(reader.GetOrdinal("week_profile"))));
                }
            }

            var devices = new List<DeviceSnapshot>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM devices WHERE serial = $serial ORDER BY address";
                command.Parameters.AddWithValue("$serial", serialNumber);
                using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    devices.Add(new DeviceSnapshot(
                        reader.GetString(reader.GetOrdinal("address")),
                        (DeviceType)reader.GetInt32(reader.GetOrdinal("type")),
                        reader.GetInt32(reader.GetOrdinal("numeric_type")),
                        reader.GetString(reader.GetOrdinal("serial_number")),
                        reader.GetString(reader.GetOrdinal("name")),
                        reader.GetInt32(reader.GetOrdinal("room_id")),
                        (DeviceStateInfo)reader.GetInt32(reader.GetOrdinal("state_info")),
                        (RadioState)reader.GetInt32(reader.GetOrdinal("radio_state"))));
                }
            }

            return new CubeSnapshot(
                serialNumber,
                int.Parse(firmware),
                rfAddress,
                daylight,
                DateTimeOffset.Parse(captured),
                rooms,
                devices);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task ReplaceRowsAsync(
        SqliteTransaction transaction, string table, string serial, CancellationToken cancellationToken)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {table} WHERE serial = $serial";
        command.Parameters.AddWithValue("$serial", serial);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddRoomParameters(SqliteCommand command, string serial, RoomSnapshot room)
    {
        command.Parameters.AddWithValue("$serial", serial);
        command.Parameters.AddWithValue("$id", room.Id);
        command.Parameters.AddWithValue("$name", room.Name);
        command.Parameters.AddWithValue("$master", room.MasterAddress);
        command.Parameters.AddWithValue("$setPoint", room.SetPoint);
        command.Parameters.AddWithValue("$actual", room.Actual);
        command.Parameters.AddWithValue("$control", (int)room.ControlMode);
        command.Parameters.AddWithValue("$tempMode", (int)room.TemperatureMode);
        command.Parameters.AddWithValue("$comfort", room.Comfort);
        command.Parameters.AddWithValue("$eco", room.Eco);
        command.Parameters.AddWithValue("$maximum", room.Maximum);
        command.Parameters.AddWithValue("$minimum", room.Minimum);
        command.Parameters.AddWithValue("$windowOpen", room.WindowOpen);
        command.Parameters.AddWithValue("$windowMinutes", room.WindowOpenMinutes);
        command.Parameters.AddWithValue("$offset", room.Offset);
        command.Parameters.AddWithValue("$boostMinutes", room.BoostMinutes);
        command.Parameters.AddWithValue("$boostValve", room.BoostValve);
        command.Parameters.AddWithValue("$decalcDay", room.DecalcificationDay);
        command.Parameters.AddWithValue("$decalcHour", room.DecalcificationHour);
        command.Parameters.AddWithValue("$weekProfile", (object?)room.WeekProfile ?? DBNull.Value);
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _connection.Dispose();
        }
        finally
        {
            _gate.Release();
        }

        _gate.Dispose();
    }
}
