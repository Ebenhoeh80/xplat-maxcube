# Zielarchitektur – MAX! Cube Neuimplementierung

> Status: **Entwurf** (Phase 6). Alle Aussagen zum Bestandssystem sind durch Quellstellen oder REA-Evidence belegt. Aussagen zur Zielarchitektur sind **Entwurfsentscheidungen** und keine Beobachtungen am Original.
>
> Sprache: Deutsch. Ziel: vollständige Rekonstruktion der **Fachlichkeit**, nicht Portierung des Java-Codes.

---

## 1. Zielbild und Designentscheidungen

| Nr. | Entscheidung | Begründung (belegt durch) | Konsequenz |
|---|---|---|---|
| A1 | Native Avalonia-UI statt embedded Jetty + Browser | Original startet Jetty 6.1.26 und öffnet `http://localhost:<Port>` im Standardbrowser (`decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/Application.java:190-227,275-277`), UI liegt als `root.war` mit DWR/jQuery bei (REA Evidence `ev_198593f77a5b718fcd92173c539c0b5f7d78752738b9e66ae993003f805d2e67`). Altlast: Browser-Kompatibilitätsmatrix, DWR-Servlet, Session-Timeout 5 min (`extracted/rootwar/WEB-INF/web.xml:66-68`). | Ein Prozess, keine lokale HTTP-Angriffsoberfläche, kein Browser-Plugin, keine DWR-Serialisierung. Bedienkonzept wird **fachlich** nachgebaut, nicht technisch. |
| A2 | .NET 10 / C# / Avalonia / CommunityToolkit.MVVM | Vorgabe des Auftraggebers. | `net10.0` als Target Framework, `Avalonia` für UI, `CommunityToolkit.Mvvm` für `ObservableObject`, `[RelayCommand]`, `WeakReferenceMessenger`. |
| A3 | SQLite als Persistenz | Original speichert **keine** Fachdaten in einer DB: `Settings extends Properties` (`decompiled/localbackend/.../core/settings/Settings.java:8`), `SettingsProvider.saveSettings()` schreibt nach `~/.MAX/<name>.settings` (`SettingsProvider.java:35-76`); Suche nach `jdbc:/hsql/derby/sqlite/@Entity` im dekompilierten Code ergab **keine** Treffer. Der Cube ist die authoritative State-Quelle (`CubeConnection.receiveDeviceAndMetaData()`, `CubeConnection.java:157-183`). SQLite ist daher ein **Neubau**, keine Migration. | SQLite speichert: Einstellungen, gerenderter Cube-Snapshot (Cache), Raum-/Gerätemetadaten (Namen), Zeitprofile als Entwürfe, Audit/Log. Der Cube bleibt Source of Truth; SQLite ist Cache + Konfiguration. |
| A4 | Clean Architecture, explizite DI | Original nutzt Spring 3.0 mit statischem `SettingsProvider` und `@Value`-Injektion (`extracted/rootwar/WEB-INF/max-service.xml:15-25`, `LocalConfiguration.java:15-73`). Statische Globals erschweren Tests. | Interfaces für alles; `Microsoft.Extensions.DependencyInjection`; keine statischen Service-Locator. |
| A5 | Protokollschicht zuerst, hardwarefern testbar | `CubeConnection` koppelt Socket, Parser, Command-Queue und Zustand (`CubeConnection.java:24-74,139-155,185-197`). | Eigene `MaxCube.Protocol`-Assembly mit `Stream`-basierter Implementierung; Tests mit `MemoryStream`/`NetworkStream`-Fake. Kein echtes Gerät in Unit-Tests. |
| A6 | Keine Übernahme von Altlasten | Getdown-Updater (`extracted/getdown.txt:1-62`), Swing-Splash, Selenium-Testlibs, AspectJ/Dozer, DWR, `javax.persistence` ohne DB, Browser-Erkennung. | Kein Auto-Update im ersten Release; keine Browser-Abhängigkeit; keine Java-Interop; keine DOI/DWR-Äquivalenz. |

---

## 2. Technologie-Stack

| Ebene | Technologie | Anmerkung |
|---|---|---|
| Runtime | .NET 10 (`net10.0`) | Cross-Platform |
| UI | Avalonia 11.x | Windows, Linux, macOS (Intel + Apple Silicon) |
| MVVM | CommunityToolkit.MVVM | `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`, `IMessenger` |
| DI | `Microsoft.Extensions.DependencyInjection`, `...Configuration`, `...Logging`, `...Host` | Generischer Host für Lifecycle |
| Persistenz | `Microsoft.Data.Sqlite` (SQLite) | Code-First Schema, Migrationen via eigener `IDbMigrator` |
| Protokoll | `System.IO.Pipelines` / `StreamReader`/`StreamWriter`, `System.Net.Sockets.TcpClient` | Zeilenbasiertes Textprotokoll (Original: `BufferedReader.readLine()`, `CubeConnection.java:53,160`) |
| Validierung | `DataAnnotations` + eigene `IValidator<T>` | Spiegelt `ConfigureDeviceValidator` etc. |
| Tests | xUnit, FluentAssertions, NSubstitute, Testcontainers (optional) | Kein Hardware-Zwang |
| Logging | `ILogger<T>` + Datei-Sink | Ersetzt Log4j (`extracted/localbackend/log4j.xml`) |
| Lokalisierung | Avalonia `ResourceManager` / `.resx` | Original: 6 Sprachen DE/EN/FR/IT/NL/PL (`extracted/maxlocalapp/MaxLocalAppStarter_*.properties`) – fachlich übernehmen, nicht Dateiformat |

**Explizit nicht enthalten (bewusst weggelassen):**
- Eingebetteter HTTP-Server, DWR, Browser-Autostart, Session-Handling.
- Getdown/basierter Selbstupdater und `~/.MAX_UPDATE`, `~/.MAX_BACKUP` (Bootstrapper-Logik, `evidence/javap-starter.txt:15-25`).
- ~~Remote-Portal-Proxy (`IMaxRemoteProxy`, `UrlRetriever`) – nur als optionales Future Feature (siehe `docs/features.md`).~~ **Verworfen 24.11.2026:** Der Remote-Proxy ist HTTP-Cloud-Protokoll der abgeschalteten Cloud; die Opcodes sind vollständig `CODE-BELEGT` (`docs/maxcube-protocol.md` Abschnitt 6.1) und werden bewusst nicht implementiert (AP-4.4 gestrichen).

---

## 3. Solution-Struktur (Clean Architecture)

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

**Abhängigkeitsregel:** `Domain` → keine Abhängigkeit. `Application` → `Domain` (nur Interfaces). `Protocol` → `Domain` (nur Value Objects; eigenes Modell der Wire-Typen). `Infrastructure` → `Application` + `Protocol`. `UI` → `Application` + `Domain`. `UI.Desktop` → alle (Composition Root).

---

## 4. Laufzeitmodell

### 4.1 Prozess / Lifecycle
1. `Program.cs` baut Host mit DI, lädt SQLite, startet `CubeConnectionHostedService`.
2. `SettingsStore` lädt Einstellungen (Pfad: `%LOCALAPPDATA%/MaxCube` bzw. `~/.config/MaxCube` auf Linux/macOS). **Nicht** `~/.MAX` (Altlast, `SettingsProvider.java:70`).
3. `DiscoveryService` findet Cube (IPv4-Broadcast/UDP, konfigurierbarer Scan-Intervall; original `max.cubeScanTime=1800`, `extracted/settings/max.local.properties:11`).
4. `CubeTcpClient` verbindet TCP 62910; Handshake `H:` und Firmware-Prüfung (273–275, `max.local.properties:17-18`).
5. Initialer State: `L:` (Geräteliste), `C:` (Konfigurationen), `M:` (Metadaten, 1900-Byte-Blöcke) → `RawCubeState` → Domänenmodell.
6. `CommandQueue` (Kapazität 100, `max.connection.commandQueueCapacity=100`) serialisiert alle Schreibzugriffe. **Ein** Worker, wie im Original (`CubeConnection.talkToCube()`, `CubeConnection.java:185-197`).
7. `ConnectionWatchDog` (5 s, `LocalApp-al.xml:11`) und `IdleService` (30 s Inaktivität, 900 s Keepalive, `max.local.properties:4-5`) halten die Verbindung.
8. UI abonniert State-Änderungen via `WeakReferenceMessenger`; keine Poll-Schleife in der View.

### 4.2 Threads / Async
- Protokoll-I/O: eine `Channel<ICubeCommand>` + ein `Async`-Worker (`Task`), kein `BlockingQueue` mit blockierendem `waitForResult()` (`CubeConnection.java:141-142`).
- UI: Avalonia UI-Thread; State-Updates via `Dispatcher.UIThread.Post`.
- SQLite: eine `SemaphoreSlim` pro Connection oder `SqliteConnection`-Factory; keine parallelen Schreiber auf eine Datei.
- Timeouts: `CancellationToken` statt `SocketException`-Fang (`CubeConnection.java:236-242`).

---

## 5. Protokoll-Architektur (Kurzfassung, Details in `docs/maxcube-protocol.md`)

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

**Wichtig:** Die Protokoll-Assembly kennt **keine** Avalonia-Typen und keine SQLite-Typen. Sie ist mit `Stream` testbar.

---

## 6. Domänenmodell (Überblick, Details in `docs/domain-model.md`)

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

**Zwei belegte Default-Ebenen – nicht gleichsetzen:**

| Ebene | Werte | Quelle |
|---|---|---|
| Properties (Hardware-/Initialisierungsdefaults) | `SetPoint 20.0 °C`, `Eco 17.0`, `Comfort 21.0`, `WindowOpen 17.0`, `Boost 15 min`, `ValveAngle 100`, `Decalcification Tag 6 / Stunde 12`, `WindowOpenTime 15 min`, `offset 0` | `extracted/essentials/max.properties:2-12` |
| Java-Konstruktor (Laufzeit-Fachmodell `Room`) | `Eco 16.5`, `Comfort 21.5`, `WindowOpen 12.0`, `ControlMode Auto`, `TemperatureMode NotAvailable`, `Boost 5`, `ValveAngle 80`, `Decalcification Saturday` | `decompiled/essentials/sources/de/eq3/max/al/core/cubestate/Room.java:45-48` (verifiziert); Details in `docs/domain-model.md` Abschnitt 3.2 |

Die Neuimplementierung muss **beide Ebenen bewusst modellieren** (z. B. `HardwareDefaults` vs. `RuntimeDefaults`) und darf sie nicht stillschweigend vereinheitlichen. Dies ist eine Produktentscheidung, keine mechanische Portierung.

Validierung (aus `localbackend/.../validators/`): Namenslänge ≤ 32, Räume ≤ 20, Geräte ≤ 140, keine doppelten Namen, gültige Radio-Adresse (Hex), kein Entfernen eines Raums mit Geräten.

---

## 7. Persistenzdesign (SQLite)

| Tabelle | Zweck | Quelle der Fachlichkeit |
|---|---|---|
| `settings` (Key/Value) | Locale, Proxy, ServerPort, Tray-Optionen, SwipeDays | `Default.settings:1-9`, `ServerSettings.java:6` |
| `cubes` | Serial, IP, Firmware, letzter Kontakt | `CubeConnection.parseHeader():297-302` |
| `rooms` | Name, Position, Modus, Temperaturen, Boost/Decalcification | `Room.java`, `max.properties` |
| `devices` | Serial, Typ, RadioAddress, Name, RoomId, Konfiguration | `Device.java`, `DeviceType.java` |
| `device_states` | Latest State (JSON oder typisierte Spalten), Timestamp | `HeatingThermostatDeviceState.java` etc. |
| `week_profiles` / `switch_points` | Zeitprofile als lokale Entwürfe | `WeekTemperatureProfile.java` |
| `metadata_cache` | Index/Count/Blob aus `M:` | `RawCubeState.setMetaData():172-180` |
| `audit_log` | Befehle, Fehlercodes (MAXxxxx) | `errorcodes.properties:1-53` |

**Konsistenzregel:** SQLite ist **Cache**. Nach Reconnect wird der Zustand aus `L:/C:/M:` neu aufgebaut und der Cache aktualisiert (Upsert nach Serial/RadioAddress). Ein "Offline"-Modus darf nur gecachte Werte anzeigen und muss dies explizit kennzeichnen (Original erlaubt `allow_offline = true`, `extracted/getdown.txt:62` – hier aber nur für den Updater belegt).

**Migration:** Es gibt keine Datenbank im Original; daher kein Datenimport. `~/.MAX/*.settings` können optional als Initialwerte für `settings` gelesen werden (EXPLIZIT OPTIONAL, nicht automatisch – Altlast).

---

## 8. UI-Architektur (Avalonia MVVM)

- **Shell**: `MainWindow` mit Navigation (`INavigationService`), Systemtray (`MaxSystemTrayHandler` fachlich nachgebaut, `decompiled/maxlocalapp/.../MaxSystemTrayHandler.java`).
- **Ansichten** (fachliche Entsprechung zu `root.war/js/views/*`):
  | Neu | Original-Quelle |
  |---|---|
  | `RoomControlView` | `js/views/roomControl/roomTemperatureView.js`, `js/controllers/roomControlController.js` |
  | `RoomDeviceView` | `js/views/roomDeviceView.js` |
  | `WeeklyProgramView` | `js/com_configuration/views/weeklyProgramConfView.js` |
  | `AddDeviceView` (Wizard) | `js/com_structure/views/addNewDevice/*` |
  | `RoomStructureView` | `js/com_structure/views/changeRoomStructure/*` |
  | `CubeNetworkView` | `js/com_structure/views/cubeConnect/*`, `cubeNetworkSettingController.js` |
  | `CubeTimeView` | `js/com_structure/controllers/setCubeTimeController.js` |
  | `FirmwareUpdateView` | `js/com_structure/controllers/localCubeUpdateController.js` |
  | `SettingsView` (Proxy/Remote/Sprache) | `js/com_structure/controllers/settingsInternetController.js`, `ProxySettingsWindow.java` |
  | `StatusView` (Airtime, Duty-Cycle, Fehler) | `js/com_structure/views/dutyCycleFullView.js`, `cubeLostConnectionView.js` |
- **Bindings**: `[ObservableProperty]`, `[RelayCommand]`; keine Code-Behind-Logik außer Platform-Code.
- **Dialoge**: `IDialogService.Show(...)` mit Avalonia `Window`/`ContentDialog`; keine `JOptionPane`-Äquivalenz im ViewModel.
- **Design**: eigenes Theme, keine Kopie der jQuery-UI-CSS. Icons neu erstellt (Original `/images/new/*` nur alssemantische Referenz).
- **Fehler**: `MAXxxxx`-Codes aus `errorcodes.properties` als `ErrorInfo` mit Resource-Key; keine Exception-Message direkt an UI.

---

## 9. Testbarkeits- und Qualitätsregeln

1. Jeder Command aus `docs/maxcube-protocol.md` bekommt einen Test mit `FakeCubeTransport` (Request-Bytes und Antwort-Bytes explizit).
2. Domänenregeln sind Pure Functions (Temperatur in 0,5-°C-Schritten, Grenzen aus `max.properties`); Test ohne Cube.
3. `ICubeTransport` ist das einzige Socket-abhängige Interface; Integrationstests gegen den virtualisierten Cube (Port 54321) oder einen eigenen Testserver.
4. Kein `Thread.Sleep` in Tests; `FakeClock` für Watchdog/Idle.
5. SQLite-Tests mit `Data Source=:memory:`.
6. UI-ViewModels testbar ohne Avalonia-Control (`CommunityToolkit` + `IMessenger`).
7. Abdeckungsziel: Protokoll ≥ 90 %, Domäne ≥ 85 %, Applikation ≥ 80 %.

---

## 10. Plattform- und Verteilungsstrategie

| Plattform | Target | Besonderheit |
|---|---|---|
| Windows | `win-x64` (auch `win-arm64` optional) | Tray-Icon, Auto-Start optional (Original: Tray + Splash, `Application.java:100-133`) |
| Linux | `linux-x64` | X11/Wayland; Settings in `~/.config/MaxCube` |
| macOS Intel | `osx-x64` | `.app`-Bundle, Quarantine-Hinweis |
| macOS Apple Silicon | `osx-arm64` | nativer Build, kein Rosetta |

Ein Solution-Build mit `dotnet publish -r <rid> --self-contained true` je Ziel. Keine Java-Laufzeit, kein Browser, kein Administratorrecht nötig (Original installiert nach `C:\ProgramData\MAX`, `Executor.java:196-209` – Altlast).

---

## 11. Risiken der Zielarchitektur

| Risiko | Bewertung | Gegenmaßnahme |
|---|---|---|
| Protokoll-Details unvollständig (z. B. Radiogramm-Bitfelder) | hoch | Phase 2 vor Implementierung abschließen; Tests gegen echten Cube/Capture; `docs/maxcube-protocol.md` Unknowns explizit halten |
| Firmware-Update (UDP) schwer reproduzierbar | hoch | Update zuerst als "Erkennung + Hinweis", Schreibzugriff erst nach Capture-Validierung |
| Avalonia-Verhalten auf macOS ARM | mittel | CI-Matrix mit allen vier RIDs, Smoke-Tests |
| State-Divergenz Cache ↔ Cube | mittel | Cube immer authoritative; Cache bei jedem Reconnet aggressively überschreiben |
| Fachliche UI-Lücken durch Wegfall des Browsers | mittel | `docs/gui-specification.md` als Abnahmekatalog verwenden |
| Keine echte DB im Original → unklare Datenmenge | niedrig | SQLite-Schema konservativ, Indizes auf Serial/RadioAddress |

---

## 12. Akzeptanzkriterien für die Zielarchitektur

1. Die Solution baut auf allen vier RIDs ohne Java, Browser oder Admin-Rechte.
2. `MaxCube.Domain` hat **keine** `using`-Abhängigkeit auf `System.Net.*`, `Avalonia.*`, `Microsoft.Data.Sqlite.*`.
3. Protokoll-Tests laufen ohne physischen Cube und decken alle in `docs/maxcube-protocol.md` gelisteten Opcodes ab.
4. Ein Reconnect nach `L:/C:/M:` erzeugt denselben Domänenzustand wie ein Kaltstart.
5. Alle `MAXxxxx`-Fehlercodes sind als typsichere `ErrorInfo` in UI und Log auffindbar.
6. Kein Port 8080/54321 im Normalbetrieb erforderlich; Port 54321 nur für den optionalen Virtual-Cube-Adapter.
7. Die sechs Originalsprachen sind in der UI auswählbar (Übersetzungen fachlich, nicht durch Kopie der JS-Resource-Dateien).

---

## Quellenverzeichnis (Auswahl)

- REA-CLI Evidence: `evidence/maxlocalapp-rea.json`, `evidence/essentials-rea.json`, `evidence/localbackend-rea.json`, `evidence/rootwar-rea.json`, `evidence/controller-rea.json`, `evidence/maxruntime-starter-inventory.json`.
- Jadx-Dekompilation: `decompiled/{maxlocalapp,essentials,localbackend,controller}/sources/**/*.java` (nicht REA-Evidence, eigenständig hashverifiziertes jadx 1.5.6).
- Konfiguration: `extracted/**` (von REA `extract-artifact` materialisiert, Evidence `ev_49ca970502d6fa52ea44268706a0d11f67eaf1e7d3fc6ee865d31521930f2209` für den Bootstrapper; die übrigen Extraktionen siehe `evidence/*-extract.json`).
- Originalinstallation: `C:\ProgramData\MAX\**`, `C:\Program Files (x86)\Max Local Application\**` (lokal begutachtet, nicht verändert).
