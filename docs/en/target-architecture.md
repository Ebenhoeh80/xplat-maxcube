> English translation of docs/target-architecture.md. Machine-created; native-speaker review pending.

# Target architecture – MAX! Cube reimplementation

> Status: **draft** (phase 6). All statements about the existing system are evidenced by source locations or REA evidence. Statements about the target architecture are **design decisions**, not observations of the original.
>
> Language: German. Goal: complete reconstruction of the **functional behavior**, not a port of the Java code.

---

## 1. Target vision and design decisions

| No. | Decision | Rationale (evidenced by) | Consequence |
|---|---|---|---|
| A1 | Native Avalonia UI instead of embedded Jetty + browser | The original starts Jetty 6.1.26 and opens `http://localhost:<Port>` in the default browser (`decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/Application.java:190-227,275-277`), UI shipped as `root.war` with DWR/jQuery (REA evidence `ev_198593f77a5b718fcd92173c539c0b5f7d78752738b9e66ae993003f805d2e67`). Legacy: browser compatibility matrix, DWR servlet, 5 min session timeout (`extracted/rootwar/WEB-INF/web.xml:66-68`). | One process, no local HTTP attack surface, no browser plugin, no DWR serialization. The operating concept is rebuilt **functionally**, not technically. |
| A2 | .NET 10 / C# / Avalonia / CommunityToolkit.MVVM | Requirement of the client. | `net10.0` as target framework, `Avalonia` for UI, `CommunityToolkit.Mvvm` for `ObservableObject`, `[RelayCommand]`, `WeakReferenceMessenger`. |
| A3 | SQLite for persistence | The original stores **no** technical data in a DB: `Settings extends Properties` (`decompiled/localbackend/.../core/settings/Settings.java:8`), `SettingsProvider.saveSettings()` writes to `~/.MAX/<name>.settings` (`SettingsProvider.java:35-76`); search for `jdbc:/hsql/derby/sqlite/@Entity` in decompiled code yielded **no** hits. The cube is the authoritative state source (`CubeConnection.receiveDeviceAndMetaData()`, `CubeConnection.java:157-183`). SQLite is therefore a **new development**, not a migration. | SQLite stores: settings, rendered cube snapshot (cache), room/device metadata (names), time schedules as drafts, audit/log. The cube remains the source of truth; SQLite is cache + configuration. |
| A4 | Clean Architecture, explicit DI | The original uses Spring 3.0 with static `SettingsProvider` and `@Value` injection (`extracted/rootwar/WEB-INF/max-service.xml:15-25`, `LocalConfiguration.java:15-73`). Static globals hinder testing. | Interfaces for everything; `Microsoft.Extensions.DependencyInjection`; no static service locator. |
| A5 | Protocol layer first, testable without hardware | `CubeConnection` couples socket, parser, command queue, and state (`CubeConnection.java:24-74,139-155,185-197`). | Separate `MaxCube.Protocol` assembly with `Stream`-based implementation; tests with `MemoryStream`/`NetworkStream` fakes. No real device in unit tests. |
| A6 | No adoption of legacy baggage | Getdown updater (`extracted/getdown.txt:1-62`), Swing splash, Selenium test libs, AspectJ/Dozer, DWR, `javax.persistence` without DB, browser detection. | No auto-update in first release; no browser dependency; no Java interop; no DWR equivalence. |

---

## 2. Technology stack

| Level | Technology | Note |
|---|---|---|
| Runtime | .NET 10 (`net10.0`) | cross-platform |
| UI | Avalonia 11.x | Windows, Linux, macOS (Intel + Apple Silicon) |
| MVVM | CommunityToolkit.MVVM | `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`, `IMessenger` |
| DI | `Microsoft.Extensions.DependencyInjection`, `...Configuration`, `...Logging`, `...Host` | generic host for lifecycle |
| Persistence | `Microsoft.Data.Sqlite` (SQLite) | code-first schema, migrations via own `IDbMigrator` |
| Protocol | `System.IO.Pipelines` / `StreamReader`/`StreamWriter`, `System.Net.Sockets.TcpClient` | line-based text protocol (original: `BufferedReader.readLine()`, `CubeConnection.java:53,160`) |
| Validation | `DataAnnotations` + own `IValidator<T>` | mirrors `ConfigureDeviceValidator` etc. |
| Tests | xUnit, FluentAssertions, NSubstitute, Testcontainers (optional) | no hardware required |
| Logging | `ILogger<T>` + file sink | replaces Log4j (`extracted/localbackend/log4j.xml`) |
| Localization | Avalonia `ResourceManager` / `.resx` | original: 6 languages DE/EN/FR/IT/NL/PL (`extracted/maxlocalapp/MaxLocalAppStarter_*.properties`) – adopt functionally, not the file format |

**Explicitly not included (deliberately omitted):**
- Embedded HTTP server, DWR, browser autostart, session handling.
- Getdown-based self-updater and `~/.MAX_UPDATE`, `~/.MAX_BACKUP` (bootstrapper logic, `evidence/javap-starter.txt:15-25`).
- ~~Remote portal proxy (`IMaxRemoteProxy`, `UrlRetriever`) – only as an optional future feature (see `docs/features.md`).~~ **Discarded 24.11.2026:** The remote proxy is the HTTP cloud protocol of the shut-down cloud; the opcodes are fully `CODE-VERIFIED` (`docs/maxcube-protocol.md` section 6.1) and are deliberately not implemented (AP-4.4 removed).

---

## 3. Solution structure (Clean Architecture)

```
app-maxcube/
├── src/
│   ├── MaxCube.Domain/                 # Enterprise Rules, keine externen Abhängigkeiten
│   │   ├── Common/                     # Value Objects (Temperature, RadioAddress, SerialNumber)
│   │   ├── Cubes/                      # Cube, CubeInfo, FirmwareInfo, CubeState
│   │   ├── Houses/                     # House, Room, RoomControlMode, RoomTemperatureMode
│   │   ├── Devices/                    # Device, HeatingThermostat, WallThermostat, ShutterContact, PushButton
│   │   ├── Schedules/                  # WeekTemperatureProfile, DayProfile, SwitchPoint
│   │   └── Services/                   # Domain Services (IDomainEvent, IRoomStateAdopter)
│   ├── MaxCube.Application/            # Anwendungsfälle ( Commands / Queries )
│   │   ├── Abstractions/               # ICubeGateway, ICubeStateStore, IUnitOfWork, IClock
│   │   ├── Commands/                   # SetRoomPermanentMode, SetRoomWeekProfile, ... (1:1 fachlich zu AL-Commands)
│   │   ├── Queries/                    # GetCubeState, GetRooms, GetDeviceDetails
│   │   └── Behaviours/                 # Validation, Logging, Transaction
│   ├── MaxCube.Protocol/               # MAX! Cube Textprotokoll (Phase 2 Spezifikation)
│   │   ├── Framing/                    # OpCodeParser, Antwort-Leser, Zeilenprotokoll
│   │   ├── Handshake/                  # HelloParser (H:), Firmware-Kompatibilität
│   │   ├── Commands/                   # L:,C:,M:,s:,m:,a:,r:,t:,w:,l:,q:,n:,x:,u:,c:,v:,f:,z: ...
│   │   ├── Radiograms/                 # Radiogramm-Builder (aus RadiogramSpecification.java)
│   │   ├── Updates/                    # UDP-Firmware-Update (CubeUdpOpcode)
│   │   └── CubeTcpClient.cs            # TcpClient, Queue, Timeout, Reconnect
│   ├── MaxCube.Infrastructure/         # Adapter
│   │   ├── Persistence/                # SQLite Repositories, Migrations, SettingsStore
│   │   ├── Discovery/                  # UDP/Subnet-Scan (Original: CubeFinder, cubeScanTime=1800)
│   │   ├── Connectivity/               # TcpClient-Adapter, WatchDog (5 s), Idle (30 s/900 s)
│   │   └── Remote/                     # (optional) Portal-Client
│   ├── MaxCube.UI/                     # Avalonia Views + ViewModels + Styles + Resources
│   │   ├── Views/, ViewModels/, Converters/, Services/ (INavigationService, IDialogService)
│   └── MaxCube.UI.Desktop/             # Program.cs, App.axaml, DI-Composition, Tray-Icon
├── tests/
│   ├── MaxCube.Protocol.Tests/
│   ├── MaxCube.Domain.Tests/
│   ├── MaxCube.Application.Tests/
│   ├── MaxCube.Infrastructure.Tests/
│   └── MaxCube.UI.Tests/
└── docs/                               # diese Reverse-Engineering-Dokumentation
```

**Dependency rule:** `Domain` → no dependencies. `Application` → `Domain` (interfaces only). `Protocol` → `Domain` (value objects only; own model of wire types). `Infrastructure` → `Application` + `Protocol`. `UI` → `Application` + `Domain`. `UI.Desktop` → all (composition root).

---

## 4. Runtime model

### 4.1 Process / lifecycle
1. `Program.cs` builds the host with DI, loads SQLite, starts `CubeConnectionHostedService`.
2. `SettingsStore` loads settings (path: `%LOCALAPPDATA%/MaxCube` or `~/.config/MaxCube` on Linux/macOS). **Not** `~/.MAX` (legacy, `SettingsProvider.java:70`).
3. `DiscoveryService` finds the cube (IPv4 broadcast/UDP, configurable scan interval; original `max.cubeScanTime=1800`, `extracted/settings/max.local.properties:11`).
4. `CubeTcpClient` connects to TCP 62910; handshake `H:` and firmware check (273–275, `max.local.properties:17-18`).
5. Initial state: `L:` (device list), `C:` (configurations), `M:` (metadata, 1900-byte blocks) → `RawCubeState` → domain model.
6. `CommandQueue` (capacity 100, `max.connection.commandQueueCapacity=100`) serializes all write access. **One** worker, as in the original (`CubeConnection.talkToCube()`, `CubeConnection.java:185-197`).
7. `ConnectionWatchDog` (5 s, `LocalApp-al.xml:11`) and `IdleService` (30 s inactivity, 900 s keepalive, `max.local.properties:4-5`) keep the connection alive.
8. UI subscribes to state changes via `WeakReferenceMessenger`; no polling loop in the view.

### 4.2 Threads / async
- Protocol I/O: one `Channel<ICubeCommand>` + one async worker (`Task`), no `BlockingQueue` with blocking `waitForResult()` (`CubeConnection.java:141-142`).
- UI: Avalonia UI thread; state updates via `Dispatcher.UIThread.Post`.
- SQLite: one `SemaphoreSlim` per connection or `SqliteConnection` factory; no parallel writers to one file.
- Timeouts: `CancellationToken` instead of catching `SocketException` (`CubeConnection.java:236-242`).

---

## 5. Protocol architecture (summary, details in `docs/maxcube-protocol.md`)

```
ICubeTransport (Stream, ConnectAsync, ReadLineAsync, WriteLineAsync)
   └── TcpCubeTransport        -> 62910 (physisch), 54321 (virtueller Cube, nur Kompatibilität)
   └── FakeCubeTransport       -> Tests

ICubeProtocol (SendAsync(command) -> Answer)
   └── CubeProtocol
        ├── HelloParser        H:serial,addrHex,fwHex
        ├── StateReceiver      L:, C:, M:
        ├── CommandWriter      s:, m:, u:, i:, c:, l:, v:, f:, z:, n:, x:, q:, e:, d:, a:, r:, t:, w:, ...
        ├── Acknowledger       A: (sowie S:/E:/D:/F:/C:/L:/M:/N: als Antwort-/State-Zeilen)
        └── RadiogramBuilder   (Bitfelder/Switchpoints aus RadiogramSpecification)

ICubeGateway (fachlich)
   └── CubeGateway
        ├── Queue + Inclusion-Mode-Guard (nur FetchNew/Cancel/Disconnect erlaubt, CubeConnection.java:270-275)
        ├── State-Mapping RawCubeState -> Domain
        └── Events: CubeConnected, CubeStateChanged, CubeDisconnected, Error(MAXxxxx)
```

**Important:** The protocol assembly knows **no** Avalonia types and no SQLite types. It is testable with `Stream`.

---

## 6. Domain model (overview, details in `docs/domain-model.md`)

```
Cube (Serial, RadioAddress, FirmwareVersion)
 ├── House
 │    ├── Room[] (max. 20, extracted/settings/max.local.properties:2)
 │    │    ├── Devices[] (max. 140, max.local.properties:3)
 │    │    │    ├── HeatingThermostat (SetPoint, Eco, Comfort, WindowOpen, Offset, ValveAngle, Boost, Decalcification)
 │    │    │    ├── WallMountedThermostat (+ DisplayConfig, ActualTemperature, Humidity)
 │    │    │    ├── ShutterContact (Open/Closed, WindowOpenTime)
 │    │    │    └── PushButton (KeyConfiguration)
 │    │    ├── RoomControlMode (Auto/Manual/Permanent/Temporary)  [RoomControlMode.java]
 │    │    ├── RoomTemperatureMode (Normal/Comfort/Eco/...)       [RoomTemperatureMode.java]
 │    │    └── WeekTemperatureProfile (7 × DayProfile × 6 SwitchPoints) [WeekTemperatureProfile.java]
 │    └── Metadata / ProductInformation / FirmwareInfo
 └── Connection (Ip, Port, LastAccess, Watchdog)
```

**Two evidenced default levels – do not equate them:**

| Level | Values | Source |
|---|---|---|
| Properties (hardware/initialization defaults) | `SetPoint 20.0 °C`, `Eco 17.0`, `Comfort 21.0`, `WindowOpen 17.0`, `Boost 15 min`, `ValveAngle 100`, `Decalcification day 6 / hour 12`, `WindowOpenTime 15 min`, `offset 0` | `extracted/essentials/max.properties:2-12` |
| Java constructor (runtime business model `Room`) | `Eco 16.5`, `Comfort 21.5`, `WindowOpen 12.0`, `ControlMode Auto`, `TemperatureMode NotAvailable`, `Boost 5`, `ValveAngle 80`, `Decalcification Saturday` | `decompiled/essentials/sources/de/eq3/max/al/core/cubestate/Room.java:45-48` (verified); details in `docs/domain-model.md` section 3.2 |

The reimplementation must **deliberately model both levels** (e.g. `HardwareDefaults` vs. `RuntimeDefaults`) and must not silently unify them. This is a product decision, not a mechanical port.

Validation (from `localbackend/.../validators/`): name length ≤ 32, rooms ≤ 20, devices ≤ 140, no duplicate names, valid radio address (hex), no removal of a room with devices.

---

## 7. Persistence design (SQLite)

| Table | Purpose | Source of functionality |
|---|---|---|
| `settings` (key/value) | locale, proxy, server port, tray options, swipe days | `Default.settings:1-9`, `ServerSettings.java:6` |
| `cubes` | serial, IP, firmware, last contact | `CubeConnection.parseHeader():297-302` |
| `rooms` | name, position, mode, temperatures, boost/descaling | `Room.java`, `max.properties` |
| `devices` | serial, type, radio address, name, room ID, configuration | `Device.java`, `DeviceType.java` |
| `device_states` | latest state (JSON or typed columns), timestamp | `HeatingThermostatDeviceState.java` etc. |
| `week_profiles` / `switch_points` | time schedules as local drafts | `WeekTemperatureProfile.java` |
| `metadata_cache` | index/count/blob from `M:` | `RawCubeState.setMetaData():172-180` |
| `audit_log` | commands, error codes (MAXxxxx) | `errorcodes.properties:1-53` |

**Consistency rule:** SQLite is a **cache**. After reconnect, the state is rebuilt from `L:/C:/M:` and the cache updated (upsert by serial/radio address). An "offline" mode may only display cached values and must mark this explicitly (the original allows `allow_offline = true`, `extracted/getdown.txt:62` – evidenced here only for the updater).

**Migration:** There is no database in the original; therefore no data import. `~/.MAX/*.settings` may optionally be read as initial values for `settings` (EXPLICITLY OPTIONAL, not automatic – legacy).

---

## 8. UI architecture (Avalonia MVVM)

- **Shell**: `MainWindow` with navigation (`INavigationService`), system tray (`MaxSystemTrayHandler` rebuilt functionally, `decompiled/maxlocalapp/.../MaxSystemTrayHandler.java`).
- **Views** (functional counterparts to `root.war/js/views/*`):
  | New | Original source |
  |---|---|
  | `RoomControlView` | `js/views/roomControl/roomTemperatureView.js`, `js/controllers/roomControlController.js` |
  | `RoomDeviceView` | `js/views/roomDeviceView.js` |
  | `WeeklyProgramView` | `js/com_configuration/views/weeklyProgramConfView.js` |
  | `AddDeviceView` (wizard) | `js/com_structure/views/addNewDevice/*` |
  | `RoomStructureView` | `js/com_structure/views/changeRoomStructure/*` |
  | `CubeNetworkView` | `js/com_structure/views/cubeConnect/*`, `cubeNetworkSettingController.js` |
  | `CubeTimeView` | `js/com_structure/controllers/setCubeTimeController.js` |
  | `FirmwareUpdateView` | `js/com_structure/controllers/localCubeUpdateController.js` |
  | `SettingsView` (proxy/remote/language) | `js/com_structure/controllers/settingsInternetController.js`, `ProxySettingsWindow.java` |
  | `StatusView` (airtime, duty cycle, errors) | `js/com_structure/views/dutyCycleFullView.js`, `cubeLostConnectionView.js` |
- **Bindings**: `[ObservableProperty]`, `[RelayCommand]`; no code-behind logic except platform code.
- **Dialogs**: `IDialogService.Show(...)` with Avalonia `Window`/`ContentDialog`; no `JOptionPane` equivalent in the view model.
- **Design**: own theme, no copy of jQuery UI CSS. Icons newly created (original `/images/new/*` only as semantic reference).
- **Errors**: `MAXxxxx` codes from `errorcodes.properties` as `ErrorInfo` with resource key; no exception message directly to UI.

---

## 9. Testability and quality rules

1. Every command from `docs/maxcube-protocol.md` gets a test with `FakeCubeTransport` (request bytes and response bytes explicit).
2. Domain rules are pure functions (temperature in 0.5 °C steps, limits from `max.properties`); test without a cube.
3. `ICubeTransport` is the only socket-dependent interface; integration tests against the virtualized cube (port 54321) or a custom test server.
4. No `Thread.Sleep` in tests; `FakeClock` for watchdog/idle.
5. SQLite tests with `Data Source=:memory:`.
6. UI view models testable without an Avalonia control (`CommunityToolkit` + `IMessenger`).
7. Coverage target: protocol ≥ 90 %, domain ≥ 85 %, application ≥ 80 %.

---

## 10. Platform and distribution strategy

| Platform | Target | Special feature |
|---|---|---|
| Windows | `win-x64` (`win-arm64` optional) | tray icon, auto-start optional (original: tray + splash, `Application.java:100-133`) |
| Linux | `linux-x64` | X11/Wayland; settings in `~/.config/MaxCube` |
| macOS Intel | `osx-x64` | `.app` bundle, quarantine notice |
| macOS Apple Silicon | `osx-arm64` | native build, no Rosetta |

One solution build with `dotnet publish -r <rid> --self-contained true` per target. No Java runtime, no browser, no administrator rights needed (original installs to `C:\ProgramData\MAX`, `Executor.java:196-209` – legacy).

---

## 11. Risks of the target architecture

| Risk | Assessment | Countermeasure |
|---|---|---|
| Protocol details incomplete (e.g. radiogram bit fields) | high | complete phase 2 before implementation; tests against real cube/capture; keep unknowns in `docs/maxcube-protocol.md` explicit |
| Firmware update (UDP) difficult to reproduce | high | first implement "detection + notice", write access only after capture validation |
| Avalonia behavior on macOS ARM | medium | CI matrix with all four RIDs, smoke tests |
| State divergence cache ↔ cube | medium | cube always authoritative; overwrite cache aggressively on every reconnect |
| Functional UI gaps from dropping the browser | medium | use `docs/gui-specification.md` as acceptance catalog |
| No real DB in original → unclear data volume | low | conservative SQLite schema, indexes on serial/radio address |

---

## 12. Acceptance criteria for the target architecture

1. The solution builds on all four RIDs without Java, browser, or admin rights.
2. `MaxCube.Domain` has **no** `using` dependency on `System.Net.*`, `Avalonia.*`, `Microsoft.Data.Sqlite.*`.
3. Protocol tests run without a physical cube and cover all opcodes listed in `docs/maxcube-protocol.md`.
4. A reconnect after `L:/C:/M:` produces the same domain state as a cold start.
5. All `MAXxxxx` error codes are findable as type-safe `ErrorInfo` in UI and log.
6. No port 8080/54321 required in normal operation; port 54321 only for the optional virtual-cube adapter.
7. The six original languages are selectable in the UI (translations functional, not by copying JS resource files).

---

## Source list (selection)

- REA CLI evidence: `evidence/maxlocalapp-rea.json`, `evidence/essentials-rea.json`, `evidence/localbackend-rea.json`, `evidence/rootwar-rea.json`, `evidence/controller-rea.json`, `evidence/maxruntime-starter-inventory.json`.
- Jadx decompilation: `decompiled/{maxlocalapp,essentials,localbackend,controller}/sources/**/*.java` (not REA evidence, independently hash-verified jadx 1.5.6).
- Configuration: `extracted/**` (materialized by REA `extract-artifact`, evidence `ev_49ca970502d6fa52ea44268706a0d11f67eaf1e7d3fc6ee865d31521930f2209` for the bootstrapper; other extractions see `evidence/*-extract.json`).
- Original installation: `C:\ProgramData\MAX\**`, `C:\Program Files (x86)\Max Local Application\**` (locally inspected, not modified).
