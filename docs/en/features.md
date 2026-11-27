> English translation of docs/features.md. Machine-created; DE/EN reviewed by the user on 27.11.2026; FR/IT/NL/PL feedback expected via issues/PRs.

# Feature catalog – MAX! Cube Local Application

Prioritized feature list as a basis for a reimplementation. Sources are exclusively the locally available decompiled Java classes, browser JavaScript files, HTML templates, and properties files. This is not a 1:1 port: the matrix describes *functional capabilities*, not the existing technical realization.

## 1. Evaluation scheme

| Dimension | Values | Meaning |
|---|---|---|
| Priority | `P0` | Core function without which the product is not operational (evidenced by main/start view) |
| | `P1` | Important configuration and comfort function |
| | `P2` | Extension, comfort, compatibility, or diagnostics |
| Effort | `S` | < 1 person-day, isolated function |
| | `M` | 1–3 person-days, several views/APIs |
| | `L` | 3–10 person-days, state management and hardware protocol affected |
| | `XL` | > 10 person-days, cross-cutting or close to hardware |
| Complexity | `low` / `medium` / `high` / `very high` | Technical difficulty independent of scope |

**INFERENCE:** Effort and complexity are estimates derived from the structure of the source material (e.g. file sizes, number of involved controllers, validation rules), not measured values. Prioritization follows visibility in the start view and the number of dependent functions.

---

## 2. Feature matrix

Status (as of 24.11.2026, committee round 5): `implemented` = locally usable, `partial` = core function with limitation, `replaced` = mapped by a local alternative, `discarded` = cannot be mapped or deliberately excluded (cloud shut down, browser/Jetty, firmware writing), `missing` = not wired (currently no row). The matrix still describes the original; the status evaluates the C# app.

**Evidence depth of implemented rows (honest classification, no hardware proof):**

| Evidence level | Rows |
| --- | --- |
| Source-/live-evidenced (format from Java or a real device) | Room control (`0x40`), weekly profile (`0x10`, proven live), temperature configuration/window/offset (`0x11`, Java bit fields, byte test), wall thermostat display (`0x82`, confirmed on a real device) |
| Derived (`INFERENCE`) and only tested against fake cube/codec | Boost/descaling/valve offset (`0x12`, mirror of the read path), group/link assignment (`0x20`–`0x23`), metadata writing (`m:`, with round-trip safety check), push button (`w:`), time zone/time (`v:`), NTP (`f:`), network (UDP `c`/`C`) |
| Purely local (no cube communication) | i18n, language selection, error codes, status dialogs, help system, log cleanup, instance protection, tray, bundled commands, small displays, diagnostic report |

**Not mappable / deliberately omitted (`discarded`):** Internet control, product account, account information, remote expiry warning, password strength (cloud shut down since 01.07.2023, remote proxy is cloud HTTP); proxy configuration (only needed for cloud/update); app software update and update splash (no update server, no HTTP check); cube firmware update (firmware writing remains disabled for safety: risk of device damage, no firmware file available); browser warning (no browser). `Send error report` is `replaced` by the local diagnostic report without transmission. `Weekly program` remains `partial`: day bar and quick editing (±5 min, ±0.5 °C) are present, but no drag-and-drop editor.

**FACT (user notice 25.11.2026, not externally verified in this session):** eQ-3 has shut down MAX! cloud control and, according to the user, also removed the associated cloud app from the Android and iOS app stores. This supplements, rather than replaces, the documented shutdown since 01.07.2023. Remote access, product account, proxy, and app update therefore remain permanently `discarded`; the C# app stays local.

| Feature | Description | Priority | Status | Effort | Rationale | Complexity | Dependencies | Source |
|---|---|---|---|---|---|---|---|---|
| Establish cube connection | Establish and disconnect a connection to the MAX! Cube via IP | P0 | implemented | L | Without a connection there are no state data | high | Network, instance protection | `MaxLocalApi.java:112`, `:116`; `APICalls.js:1268`, `:1294` |
| Auto-discovery | Detect available cubes on the network and offer them for selection | P0 | implemented | L | First-time setup and installations with unknown IP require detection | high | Cube connection, UDP 23272 | `CubeFinder.java:22-28,47-51,84-106`; `docs/maxcube-protocol.md:47-67`; `max.local.properties:26-27` is only the hidden cloud fallback |
| Manual IP entry | Enter cube IP via four octet fields with 0–255 validation | P0 | implemented | M | Fallback if detection fails | medium | Cube connection | `index.html:936-975`; `indexStructureController.js:552-636` |
| State retrieval | Load rooms, devices, and states via `getMaxCubeState` | P0 | implemented | M | Data basis of the entire UI | medium | DTO model, DWR bridge | `MaxLocalApi.java:47`; `maxCubeModel.js:85-100` |
| Room overview | Display all rooms with actual/target temperature and mode | P0 | implemented | M | Main view of the application | medium | State retrieval | `indexView.js:1-953`; `index.html:361-457` |
| Room control Auto | Set room to automatic profile control | P0 | implemented | S | Default mode according to `IRoom` | low | State retrieval, commands | `IRoom.java:20`; `SetRoomAutoMode.java:1`; `APICalls.js:2133` |
| Room control Permanently | Set a fixed target temperature over a period | P0 | implemented | S | Central heating function | low | Room control | `roomControlController.js:618-639`; `APICalls.js:2162` |
| Room control Temporary | Set target temperature with end date/time | P0 | implemented | M | Time-limited control; date/time dialog required | medium | Room control, cube time | `roomControlController.js:640-662`, `:757-766`; `APICalls.js:2225` |
| House control | Select and control several rooms at once | P1 | implemented | M | Comfort function for the whole apartment | medium | Room control | `roomControlController.js:265-394`, `:837-866`; `index.html:843-865` |
| Change target temperature via +/- | Incremental temperature control on room tiles and in the dialog | P0 | implemented | S | Direct interaction in the main view | low | Room control | `index.html:433-444`; `roomControlController.js:179-264` |
| Weekly program | Graphically edit seven day profiles with up to 13 switching points | P0 | partial | XL | Core of heating automation; largest UI controller | very high | Weekly profile model, validation, drag/resize | `weeklyProgramController.js:96-1842`; `WeekTemperatureProfile.java:13-15`; `DayTemperatureProfile.java:16-18` |
| Copy weekly program | Transfer day profiles/room configuration to other rooms | P1 | implemented | M | Multiple configuration saves effort | medium | Weekly program | `weeklyProgramController.js:122-153`, `:1716-1785`; `index.html:1676-1702` |
| Quick editing of weekly program | +/- buttons and time-window tooltip in the diagram | P2 | implemented | L | Comfort, not needed for basic function | high | Weekly program | `weeklyProgramController.js:643-823`, `:1198-1270` |
| Temperature configuration | Set eco, comfort, maximum, and window-open temperature per room | P1 | implemented | M | Affects all automatic modes | medium | Room configuration | `temperatureConfController.js:60-201`; `index.html:1429-1492` |
| Boost configuration | Set boost duration and valve angle | P1 | implemented | S | Special function for fast heating | low | Room configuration | `boostConfController.js:56-146`; `index.html:1493-1530` |
| Configure descaling | Set weekday and time of descaling | P1 | implemented | S | Device maintenance | low | Room configuration | `decalcificationConfController.js:56-146`; `index.html:1531-1563` |
| Window-open logic | Configure temperature and duration when a window is open | P1 | implemented | S | Energy-saving function | low | Temperature configuration, ShutterContact | `Room.java:48-49`, `:283-311`; `APICalls.js:2338` |
| Temperature offset | Correct sensor/valve offset per device | P1 | implemented | M | Accuracy of control | medium | Device configuration | `deviceOffsetConfController.js:58-210`; `APICalls.js:2440` |
| Wall thermostat display | Switch display between target vs. actual temperature on the device | P2 | implemented | S | Pure comfort feature | low | Device configuration | `setWallThermostatDisplayConfigController.js:55-109`; `APICalls.js:2458` |
| Push button configuration | Configure upper/lower button of a PushButton | P1 | implemented | M | Device-specific function | medium | House-specific devices | `pushButtonConfController.js:51-118`; `index.html:2106-2149` |
| Search for new devices (inclusion) | Start detection mode and display found devices | P0 | implemented | L | Devices can only be added this way | high | Radio protocol, room assignment | `MaxLocalApi.java:54`, `:62`; `addNewDeviceController.js:195-264`; `max.local.properties:9` |
| Assign device to room | Give a found device a name and room | P0 | implemented | M | Completes inclusion | medium | New devices, validation | `MaxLocalApi.java:79`; `ConfigureDeviceValidator.java:60-77`; `addNewDeviceController.js:359-403` |
| House devices without room | Assign devices to `House` instead of a room | P1 | implemented | M | Push buttons/wall thermostats may be roomless | medium | New devices, house model | `MaxLocalApi.java:75`, `:161`; `addNewDeviceController.js:727` |
| Check device limits | Max. 8 heating/window contacts, 1 wall thermostat per room | P0 | implemented | S | Prevents invalid configurations | low | Validation | `ConfigureDeviceValidator.java:80-101`; `IRoom.java:17-19` |
| Name validation | Name not null, ≤ 32 characters, case-insensitively unique | P0 | implemented | S | Data consistency and error avoidance | low | Validation, error codes | `ConfigureDeviceValidator.java:148-154`, `:123-138`; `max.local.properties:1` |
| Rename room | Change a room's name | P1 | implemented | S | Structure maintenance | low | Structure management | `changeRoomStructureController.js:190-311`; `MaxLocalApi.java:83` |
| Delete room | Remove room including devices, optionally with factory reset | P1 | implemented | M | Structure maintenance, data-loss risk | medium | Structure management | `changeRoomStructureController.js:312-391`; `MaxLocalApi.java:95` |
| Rename device | Change a device's name | P1 | implemented | S | Structure maintenance | low | Structure management | `changeRoomStructureController.js:410-538`; `MaxLocalApi.java:87` |
| Delete device | Remove device, deletion only when permitted | P1 | implemented | M | Structure maintenance | medium | Structure management | `changeRoomStructureController.js:539-769`, `:623-661` |
| Move device | Transfer device to another room, including mode notice | P1 | implemented | L | Complex state change with warning | high | Structure management, mode | `changeRoomStructureController.js:770-1031`; `index.html:1893-1894` |
| Change room order | Drag-and-drop sorting of rooms | P2 | implemented | M | Pure display configuration | medium | Structure management | `changeRoomStructureController.js:1105-1192`; `MaxLocalApi.java:165` |
| Display/change network configuration | Cube IP, subnet, gateway, DNS, DHCP | P1 | implemented | L | Hardware-near configuration with error consequences | high | Cube connection | `cubeNetworkSettingController.js:46-269`; `NetworkConfiguration.java:17-24`; `index.html:976-1100` |
| DHCP toggle | Switch DHCP on/off and show/hide dependent fields | P1 | implemented | S | Part of network configuration | low | Network configuration | `cubeNetworkSettingController.js:210-225`; `index.html:1015` |
| Time zone / daylight saving | Set time zone and automatic summer/winter time switching | P1 | implemented | M | Affects weekly program and cube time | medium | Global settings, cube time | `globalSettingsMenuController.js:134-251`; `MaxLocalApi.java:197-205` |
| Set cube time | Show browser/cube time difference and synchronize cube time | P1 | implemented | M | Time basis for time control | medium | Time zone | `setCubeTimeController.js:57-168`; `MaxLocalApi.java:209` |
| Check firmware version | Compare current and recommended cube firmware | P1 | implemented | M | Update decision | medium | Update service | `indexStructureController.js:313-400`; `max.local.properties:17-18` |
| Update cube firmware | Firmware update with progress and completion status | P1 | discarded | XL | Close to hardware, risky | very high | Firmware check, download | `indexStructureController.js:401-551`; `APICalls.js:1157-1225` |
| App software update | Detect and download new client/server version | P1 | discarded | L | Operational security and currency | high | Update URL, shell | `InteractiveSplash.java:297-320`; `VersionChecker.java:1-160`; `max.local.properties:13-14` |
| Internet control / remote access | ~~Enable remote access and set credentials~~ **discarded 24.11.2026** (cloud shut down, no credentials in the app) | ~~P1~~ | discarded | M | Remote control via MAX! portal | medium | Product account, validation | `settingsInternetController.js:74-330`; `MaxLocalApi.java:120-124` |
| Product account activation | ~~Link cube to account via product key~~ **discarded 24.11.2026** (remote proxy is cloud HTTP, deliberately excluded) | ~~P1~~ | discarded | M | Prerequisite for remote access | medium | Internet control | `indexController.js:157-163`, `:658-745`; `MaxLocalApi.java:221-225` |
| Show account information | Display activation and expiry information | P2 | discarded | S | Informational function | low | Product account | `indexController.js:705-720`; `showProductAccountInfoView.js:1-73` |
| Remote expiry warning | Notice before remote access authorization expires | P2 | discarded | S | Preventive information | low | Product account | `indexController.js:688-704`; `remoteSoonInvalidView.js:1-137` |
| Password strength indicator | Visual rating of password during entry | P2 | discarded | S | Comfort when setting credentials | low | Internet control | `settingsInternetController.js:293-330` |
| Configure proxy | Enable and parameterize HTTP proxy in the desktop shell | P1 | discarded | M | Networks with a proxy require this | medium | Shell, settings | `ProxySettingsWindow.java:25-142`; `MaxLocalAppStarter_de.properties:1-7` |
| System tray control | Open browser, proxy, error report, exit via tray | P1 | implemented | M | Continuous operation without a visible window | medium | Shell, settings | `MaxSystemTrayHandler.java:26-160`; `MaxLocalAppStarter_de.properties:7-14` |
| Send error report | Send system profile, problem description, and log file by email | P2 | replaced | M | Support and diagnostics | medium | Shell, SystemProfiler | `BugReportWizard.java:19-128`; `TextRessources.java:53-83` |
| Update splash | Welcome window with update check and language selection at startup | P2 | discarded | M | First impression and update notice | medium | Shell, update service | `InteractiveSplash.java:61-169`, `:290-478` |
| Instance protection and port selection | Prevent second instance, find free port, conflict dialog | P1 | implemented | M | Prevents data conflicts | medium | Shell, Jetty | `Application.java:75-91`, `:228-229`; `PortFactory.java:1-36`; `InstanceLock.java:1-78` |
| i18n DE/EN/FR/IT/NL/PL | Six languages for browser UI and shell | P0 | implemented | L | Product shipped multilingually | medium | All views | `jquery.translate.lang_{de,en,fr,it,nl,pl}.js`; `indexController.js:125-134`; `TextRessources.java:5-83` |
| Language selection (cookie/browser/setting) | Select language automatically or switch manually | P1 | implemented | M | User comfort | medium | i18n | `indexController.js:323-479`; `maxCubeModel.js:233-252` |
| Error codes MAX0001–MAX1051 | Central error-code table and display in dialogs | P1 | implemented | M | Understandable error messages | low | All APIs, status dialogs | `errorcodes.properties:1-53`; `APICalls.js:2514-2546` |
| Status dialogs (waiting/success/error) | Reusable dialog for all write operations | P0 | implemented | M | Consistent feedback | low | All actions | `generalSendView.js:28-77`; `index.html:2254-2300` |
| Airtime/connection monitoring | `stillAlivePing`, connection loss, and duty-cycle notice | P1 | implemented | M | Operational safety with radio problems | medium | Cube connection | `indexStructureController.js:723-815`; `index.html:515-527`, `:2213-2213` |
| Bundled commands | Collect temperature changes for 3 s and send them together | P1 | implemented | M | Reduces radio traffic and keyboard lag | medium | Command model | `indexController.js:119-121`, `:187-217`; `maxCubeModel.js:178-200` |
| Help system | Multilingual context-sensitive help | P2 | implemented | M | Onboarding and support | medium | i18n | `helpController.js:1-570`; `jquery.translateHelp.lang.js:1-834` |
| Log cleanup | Remove old logs after a configurable number of days | P2 | implemented | S | Data housekeeping | low | Shell settings | `ApplicationSettings.java:7`, `:67-78` |
| Offline/demo mode | Switch in the DWR wrapper for working without a cube | P2 | implemented | S | Development and testing | low | DWR bridge | `APICalls.js:30-32`, `:2497-2513` |
| Browser warning | Show notice for unsupported browsers | P2 | discarded | S | Compatibility communication | low | Start controller | `indexController.js:307-318`; `noSupportedBrowserView.js:16-34` |
| Mobile/small displays | Hide navigation on small viewports | P1 | implemented | S | Usability on small devices | low | Main view | `index.html:20`; `indexController.js:136-154`; `jquery.mobileDetect.js:26-90` |
| Separate two default worlds | Model hardware defaults (max.properties) and runtime defaults (Java) separately | P0 | implemented | M | Prevents wrong target temperatures | medium | Domain model | `max.properties:2-13`; `Room.java:43-64`; `IRoom.java:9-22` |
| Preserve DTO/AL separation | Never write transport objects directly into the AL aggregate | P0 | implemented | M | Clean architecture, fewer errors | medium | Domain model, APIs | `MaxLocalApi.java:99`; `command/ClientCommand.java:1`; `domain-model.md` section 13 |

---

## 3. Prioritization logic

### 3.1 P0 – operational core

P0 covers everything needed for the basic function "control heating via cube": connection, discovery, state retrieval, room overview, the three modes, weekly program, inclusion with validation, status dialogs, i18n, and separation of the default worlds. Rationale: without these functions no controllable installation is reachable; they are anchored in the start view or the first-installation flow (`indexController.js:86-123`; `globalSettingsMenuController.js:243-248`).

### 3.2 P1 – configuration and operational safety

P1 covers structure maintenance, temperature and special functions, network, time, updates, remote access, proxy, tray, instance protection, error codes, and mobile displays. These features are not needed for the first heating action, but for continuous operation and maintenance (`indexConfController.js:68-609`; `Application.java:66-91`).

### 3.3 P2 – comfort and diagnostics

P2 covers quick editing, account information, expiry warnings, password strength, bug report, splash, offline mode, browser warning, help, and log cleanup. They increase quality and supportability but do not block any core function.

---

## 4. Complexity drivers

| Driver | Affected features | Rationale / source |
|---|---|---|
| Graphical weekly program editor | Weekly program, quick editing, copying | 1842 lines of controller logic, drag/resize, 13-point limit, 0.5 °C rounding (`weeklyProgramController.js:195-1125`) |
| Hardware-near radio operations | Inclusion, firmware, network, connection | Time limits and timeouts configured: `inclusionModeTimeoutSeconds=60`, `connectionTimeout=30000`, `socketTimeout=70000` (`max.local.properties:9`, `:23-24`) |
| State consistency | Move, delete, mode changes, bundled commands | Flags `stateDirty`/`stateChanged` and mode notices (`Room.java:40-41`, `:136-158`; `index.html:1893-1894`) |
| Two default sources | Temperature configuration, offline operation, domain model | `max.properties` deviates from `IRoom.java` (see section 2, last row) |
| Six languages × two shells | i18n, language selection, splash, help | Browser files ~520 lines per language, shell resource bundles, and `TextRessources.java` (`jquery.translate.lang_*.js`; `TextRessources.java:5-83`) |
| Three representation levels | DTO/AL separation, state retrieval, commands | AL, DTO, and browser model have differing types and defaults (`domain-model.md` section 13) |

---

## 5. Dependencies (INFERENCE)

```
Cube-Verbindung / Auto-Discovery
  ├── Zustandsabruf ──> Raumübersicht ──> Raumsteuerung (Auto/Permanent/Temporary)
  │                                      └──> Haussteuerung
  │                                      └──> gebündelte Kommandos
  ├── Inclusion ──> Namensvalidierung ──> Gerätelimits ──> Raumzuordnung
  │                                      └──> Hausgeräte ohne Raum
  ├── Strukturverwaltung ──> Verschieben ──> Modus-Hinweis
  ├── Netzwerkkonfiguration ──> DHCP-Umschaltung
  ├── Zeitzone ──> Cube-Zeit ──> Wochenprogramm ──> Wochenprogramm kopieren
  ├── Firmware-Prüfung ──> Cube-Firmware-Update
  └── Airtime-Überwachung ──> Statusdialoge

Shell: Instanzschutz ──> Update-Splash ──> Systemtray ──> Proxy / Fehlerbericht
Querschnitt: i18n ──> alle Ansichten; Fehlercodes ──> Statusdialoge; DTO/AL-Trennung ──> alle APIs
```

**Evidence limit:** The dependency graph shown is derived from call chains (e.g. `globalSettingsMenuController.js:245-247` calls `AddNewDeviceController.openOverlay()` after the time zone), not from an explicit architecture diagram. It must therefore be read as **INFERENCE**.

---

## 6. Non-goals / deliberately excluded

1. No 1:1 adoption of the DWR API, the `j_*` selectors, or the `@@token@@` platform; these are implementation details of the legacy solution (`APICalls.js:8-18`; `index.html:22-25`).
2. No adoption of evidenced bugs in the old code: wrong error selectors in the status dialog (`generalSendView.js:53-55`), typo "Port Einabe" (`MaxLocalAppStarter_de.properties:18`), regex `\d+.\d+.\d+` without escaped dots (`SoftwareInformationValidator.java:18`), inconsistent error-code number `MAX10038` (`errorcodes.properties:40`).
3. No support for obsolete browsers via `$.browser` detection as a target state; documented only as historical reference (`indexController.js:308`).
4. No assumption that `HeatingThermostatPlus` is considered in `Room.isTemperatureControllable()`; the old code does not do so (`Room.java:176-178`).

---

## 7. Explicit open items

1. **UNKNOWN** – Functional rules behind `deviceFactoryReset` (`MaxLocalApi.java:91`, `:95`).
2. **UNKNOWN** – Exact update protocol and file format of the software update (`VersionChecker.java:1-160`, `DownloadManager.java:1-156`).
3. **UNKNOWN** – Meaning of the `encryptionEnabled` flag of the AL network configuration for the UI (`NetworkConfiguration.java:15` vs. `cl/dto/NetworkConfiguration.java:5-10`).
4. **UNKNOWN** – The text of the browser warning view `#j_notSupportedBrowsers` and of the `@@h1dutyCycleFull@@` notice in the evaluated DE excerpt.
5. **INFERENCE** – Effort classes are orders of magnitude; a separate feasibility study is needed for the weekly program and firmware update.
6. **INFERENCE** – Prioritization assumes a local cube; for a pure cloud/remote product, internet control and product account would have to be promoted to P0.
