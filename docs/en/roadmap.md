> English translation of docs/roadmap.md. Machine-created; DE/EN reviewed by the user on 27.11.2026; FR/IT/NL/PL feedback expected via issues/PRs.

# Implementation plan – MAX! Cube reimplementation

> Status: **Implementation up to the first working version** (24.11.2026). Basis: `docs/architecture-rea.md`, `docs/maxcube-protocol.md`, `docs/domain-model.md`, `docs/gui-specification.md`, `docs/features.md`, `docs/target-architecture.md`. For interim status see section 8.
>
> Effort in person-days (PT), including tests and review. Assumption: 1 senior developer with .NET/Avalonia experience.
>
> Prioritization specified by the client: **1. Protocol → 2. Domain model → 3. Persistence → 4. Business logic → 5. UI**.

---

## 0. Overhead & prerequisites (not included in any work package)

| ID | Measure | Effort |
| --- | --- | --- |
| V1 | Solution skeleton, CI (4 RIDs), style/analyzers, test projects | 3 PT |
| V2 | Complete reverse-engineering documentation (phases 1–6), maintain the unknown list | 1 PT |
| V3 | (optional) Capture of a real cube / virtualized cube (62910) for golden tests | 2 PT |

**Prerequisite for AP-1.4 and AP-1.5:** at least `V3` or a documented fake cube with recorded `H:/L:/C:/M:/A:` responses.

---

## Phase 1 – Protocol layer (highest priority)

### AP-1.1 – Transport & handshake
- **Description (implemented 24.11.2026):** `ICubeTransport`, `TcpCubeTransport`, line-based reading/writing (**CRLF**-terminated, own `LineReader`; LF `\n` is accepted when reading), `ConnectAsync()` configured via `CubeConnectionOptions` (host/port instead of the `ConnectAsync(ip, port)` signature), `soTimeout` 30 s hello-first-read / 70 s socket (Java, `CubeConnection.java:40-74`) plus 30 s connect as a C# addition (`max.local.properties:17-24`), hello parser `H:serial,addrHex,fwHex` and firmware guard 273–275. Async timeouts are enforced via `CancellationTokenSource.CancelAfter` because `NetworkStream.ReadTimeout` only affects synchronous I/O.
- **Effort:** 4 PT
- **Risks:** Wrong line encoding/CRLF; cube replies in a different order; firmware guard too strict for real devices.
- **Dependencies:** V1, `docs/maxcube-protocol.md` (handshake chapter).
- **Acceptance criteria:**
  1. `H:001234567890,12345678,0111` → serial `001234567890`, radio address `0x12345678`, firmware `273` (0x0111).
  2. Firmware 272 and 276 are rejected with `CubeCommunicationFailedException`.
  3. If `H:` is missing, `NoIncomingHello` is reported.
  4. Unit tests with `MemoryStream` without a socket.

### AP-1.2 – State receiver (L / C / M)
- **Description:** Initial receipt of `L:` (device list), `C:<addrHex>,<config>` (device configuration), `M:<index>,<count>,<data>` (metadata, 1900-byte blocks) until `L:` has arrived (`CubeConnection.receiveDeviceAndMetaData()`, `CubeConnection.java:157-183`; `max.metadataBlockSize=1900`, `max.local.properties:8`).
- **Effort:** 5 PT
- **Risks:** `M:` blocks may arrive multiple times/interleaved; `C:` before `L:`; large blocks → memory.
- **Dependencies:** AP-1.1.
- **Acceptance criteria:**
  1. A sequence to be recorded `C:…,M:…,L:…` ends after `L:` and delivers a complete `RawCubeState`.
  2. Each `C:` is assigned to its radio address; each `M:` to its index/count.
  3. Timeout/maximum block size aborts in a controlled manner (no endless-loop test).

### AP-1.3 – Command queue & session logic
- **Description:** `Channel<ICubeCommand>` (capacity 100), **one** worker, synchronous response correlation, `CancellationToken` instead of `waitForResult()` (`CubeConnection.java:36,139-155,185-197`), inclusion-mode guard (only `FetchNewDevices`, `CancelInclusionMode`, `Disconnect` allowed, `CubeConnection.java:264-275`), watchdog 5 s and idle 30 s / 900 s (`LocalApp-al.xml:10-13`, `max.local.properties:4-5`).
- **Effort:** 5 PT
- **Risks:** Deadlock on response timeout; inclusion mode remains stuck (timeout 60 s, `max.local.properties:9`); queue overflow.
- **Dependencies:** AP-1.1, AP-1.2.
- **Acceptance criteria:**
  1. 100 parallel commands are processed serially in receive order.
  2. In inclusion mode, `SetRoomPermanentMode` is rejected with `CubeIsInInclusionMode`.
  3. A socket error marks all waiting commands as failed (original: `purgeCommandQueue()`, `CubeConnection.java:249-261`).
  4. Idle activity and watchdog run deterministically with `FakeClock`.

**Interim status 24.11.2026 (AP-1.2/1.3):** `StateReceiver`, `RawCubeState`, `CubeCommandQueue`, `CubeSessionMonitor` and 21 typed commands are implemented; solution build 0 warnings/0 errors, 116/116 tests green (repeated several times). The end-to-end test runs via a loopback fake cube, **not** a golden capture (see `tests/MaxCube.Protocol.Tests/LoopbackFakeCube.cs:10-12`). This is an `OBSERVATION` status, not hardware evidence.

Deliberate, documented deviations from the Java original:

- `StateReceiver` aborts on an incorrect argument count in a `C:`/`M:` line; Java ignores such lines (`CubeConnection.java:167,175`). Rationale: `UNKNOWN` must not be passed on as a valid state.
- `C:` radio addresses are limited to `0..0xFFFFFF` (`NumberStyles.HexNumber` alone accepts negative 32-bit values). `L:` returns `""` instead of `null` for an empty body; for Base64 this is equivalent.
- `RawCubeState` requires all `M:` blocks and throws `CubeCommunicationFailedException` on a differing `count`; Java silently fills missing blocks with empty arrays (`Metadata.java:43-46`).
- Parse/prefix errors of a response reject only that one command (`UnknownCubeOpcodeException`, `CubeCommandRejectedException`) without faulting the connection. Transport, EOF and timeout errors fault the queue and purge all waiting commands.
- The inclusion guard applies both when enqueuing **and** when executing; the flag is set before the `n:` command is completed. `CubeIsInInclusionModeException` is therefore not a connection error. Unlike the Java original, a second `n:` is rejected (there `FetchNewDevices` merely waits for `N:` in inclusion mode) so that a pairing window is not accidentally restarted.
- `n:` uses a command-specific timeout of `max(70 s, inclusionSeconds + 10 s)` instead of the global 30 s, because the cube sends the `N:` response only when a new device appears or at the end of the inclusion window (`FetchNewDevicesCubeCommand.java:39-65`). An empty `N:` also ends inclusion mode in C# (`CubeConnection.java:59-62`). `x:` ignores response lines without an `N:` prefix as in the original (`CancelInclusionModeCubeCommand.java:37-49`).
- `CubeSessionMonitor` does not yet know an `Unreachable` callback and does not automatically couple `LastActivity` to the queue; `onWatchdog` is no longer called from the keepalive threshold onward (exclusivity is `UNKNOWN` according to `docs/maxcube-protocol.md`). The 60 s inclusion monitoring from `max.local.properties:9` is not yet implemented and remains noted as an open task for AP-1.6/AP-3.

### AP-1.4 – Command catalogue (App → Cube)
- **Description:** All outgoing opcodes from `IOperationCodes.java:32-59` as typed commands: `u:` URL, `i:` interval, `s:` send/radiogram, `m:` metadata, `n:`/`x:` inclusion, `g:` more-data, `q:` quit, `e:`/`d:` AES, `a:` gateway reset, `r:` radiogram reset, `t:` delete devices, `w:` push-button config, `l:` device list, `c:` configuration, `v:` timezone, `f:` NTP, `z:` wakeup. Each with syntax, example and example response from `docs/maxcube-protocol.md`. **Correction 24.11.2026:** `B:/G:/J:/P:/O:/V:/W:` are **not** cube TCP opcodes but belong to the HTTP remote proxy of the shut-down cloud (section 6.1 of the protocol document); they are deliberately not modelled as commands. The same applies to `g:` "more-data": the operative `g:` response belongs to the proxy's `G:` request, `OUTGOING_MORE_DATA` is a dead constant.
- **Effort:** 8 PT
- **Risks:** Multiply assigned prefixes (`s:`, `u:`, `v:`, `w:`, `g:`) depend on context; unknown responses.
- **Dependencies:** AP-1.3, complete protocol documentation.
- **Acceptance criteria:**
  1. For every listed opcode there is a test with the expected request string.
  2. `A:` (acknowledge) is interpreted as success, an `r:` response as an error.
  3. Unknown response prefixes produce `UnknownOpcode` errors, never silent successes.

**Interim status 24.11.2026:** 21 typed commands for all **evidenced** cube TCP opcodes exist (`src/MaxCube.Protocol/Commands/CubeCommands.cs`) and are tested with expected wire strings in `tests/MaxCube.Protocol.Tests/CubeCommandsTests.cs`. **Resolved 24.11.2026:** The seven opcodes `B:/G:/J:/P:/O:/V:/W:` and the operative `g:` are **clarified** – they belong to the HTTP remote proxy (device app) of the cloud shut down since 01.07.2023, not to local cube TCP. They are deliberately **not** implemented: no cloud interface, no access data/product keys in the app. `CODE-EVIDENCED` by `decompiled/*/sources/de/eq3/max/al/local/remoteproxy/` (seven request classes, `MaxRemoteProxy.java:67-175`). Only the dead constant `OUTGOING_MORE_DATA` (`g:`, `IOperationCodes.java:38`) remains `UNKNOWN`. An unexpected `b:/g:/j:/p:/o:/v:/w:` **as a response line** on port 62910 is rejected as `UnknownCubeOpcodeException`, not as success (`CubeCommands.cs:58-60`); `v:`/`w:` remain valid requests (`CubeCommands.cs:309,338`). Proof: `SecondBrain/wiki/fragen/2026-11-24-maxcube-remote-proxy-opcodes.md`, section 6.1 of the protocol document. Response prefixes of the implemented commands follow the Java sources (`l:→L:`, `c:→C:`, `m:/u:/i:/t:/w:/v:/z:→A:`, `n:/x:→N:`, `s:/r:→S:`, `e:→E:`, `d:→D:`, `f:→F:`).

### AP-1.5 – Radiogram builder
- **Description:** Bit-exact generation of device telegrams from `RadiogramSpecification.java` / `TransmissionRadiogramBuilder.java` / `ResetRadiogramBuilder.java` (including switch points, temperature encoding, addresses, parity/CRC if evidenced).
- **Effort:** 8 PT
- **Risks:** **Highest technical risk.** Bit fields are reconstructed from bytecode; not verifiable without a golden capture.
- **Dependencies:** AP-1.4, V3 (golden capture) strongly recommended.
- **Acceptance criteria:**
  1. Known telegram patterns from the documentation are reproduced byte-for-byte.
  2. Temperature 20.0 °C → evidenced encoding value; 0.5 °C steps checked.
  3. Every builder has a test against `RadiogramSpecification` (reference value pairs).

### AP-1.6 – UDP firmware update (detection first)
- **Description:** Rebuild the `CubeUdpOpcode` table and request/response from `CubeUdpProxy.java:105`, `CubeUdpRequest/Response`, `CubeUpdater.java`. Phase 1 only **detection + status**; writing/flashing firmware only after capture validation.
- **Effort:** 4 PT (detection) + 6 PT (flash, later)
- **Risks:** Device bricked by a wrong frame; update URL no longer reachable (`max.eq-3.de`).
- **Dependencies:** AP-1.4, V3.
- **Acceptance criteria:**
  1. Detection delivers current firmware and available version without write access.
  2. The flash path is **not implemented** and remains disabled until a golden test exists.

**Total effort phase 1: 34 PT (+ 6 PT optional)**

---

## Phase 2 – Domain model

### AP-2.1 – Value objects & enums
- **Description:** `Temperature` (0.5 °C steps, limits from `max.properties`), `RadioAddress` (hex), `SerialNumber` (error codes `errorcodes.properties:42-43`, pattern `UNKNOWN`), `DeviceType`, `RoomControlMode`, `RoomTemperatureMode`, `DayOfWeek` (Monday=1…Sunday=7), `DeviceStateInfo`, `RadioState`, `PushButtonKeyMode` (`essentials/.../cubestate/*.java`, `cl/dto/*.java`).
- **Effort:** 4 PT
- **Risks:** Wrong temperature unit/limits; enum values incomplete.
- **Dependencies:** V1, `docs/domain-model.md`.
- **Acceptance criteria:**
  1. All enums are derived exhaustively from the Java sources and listed in the documentation.
  2. `Temperature` rejects values outside the evidenced limits (`DomainValidationException` with `MAX1050`, `errorcodes.properties:52`; `@Range(0..40)` from `Temperature.java:13`).
  3. Value objects are immutable and have value comparison.

**Interim status 24.11.2026:** `Temperature`, `RadioAddress`, `SerialNumber`, all **seven** domain enums (`DeviceType`, `RoomControlMode`, `RoomTemperatureMode`, `DeviceStateInfo`, `RadioState`, `DayOfWeek`, `PushButtonKeyMode`) and `DomainValidationException` are implemented in `src/MaxCube.Domain/`; 43 new tests green. Sonnet cross-check: first run not approved (DayOfWeek, PushButtonKeyMode, documentation numbers/source lines, `INFERENCE` marking), all findings resolved. Enum source lines: `DeviceType.java:4-30`, `RoomControlMode.java:4-9`, `RoomTemperatureMode.java:4-9`, `DeviceStateInfo.java:4-8`, `RadioState.java:4-11`, `DayOfWeek.java:6-45`, `PushButtonKeyMode.java:4-10`; fully listed in `docs/domain-model.md` section 16. Deliberate deviations: `Temperature` uses exact value comparison as a `readonly record struct` instead of the Java tolerance `< 0.1 °C` (`Temperature.java:82-93`); the constructor additionally checks the interval 0..40 evidenced from `@Range`, which the Java constructor only checks during bean validation. No `SerialNumber` pattern is invented (`UNKNOWN`, `errorcodes.properties:43`).

### AP-2.2 – Aggregate root Cube / House / Room / Device
- **Description:** Entities and relationships (max. 20 rooms, 140 devices, names ≤ 32; `max.local.properties:1-3`), state adopter logic (`HeatingThermostatDeviceRoomStateAdopter`, `WallMountedThermostatDeviceRoomStateAdopter`, `RoomStateAdopter`).
- **Effort:** 6 PT
- **Risks:** Wrong cardinality; state adopter special cases (plus variant, wall thermostat) missed.
- **Dependencies:** AP-2.1.
- **Acceptance criteria:**
  1. A `RawCubeState` can be fully transferred into the aggregate (roundtrip test).
  2. 21st room / 141st device / 33rd character are rejected.
  3. Domain events for `RoomStateChanged`, `DeviceStateChanged`.

**Interim status 24.11.2026 (preparation):** `MaxCube.Protocol.State.MetadataDecoder` decodes the metadata payload of the `M:` blocks into rooms/devices (magic 86 + version 2 or version 1 without the fields from version 2, `MetaDataSerializer.java:38-43`, `VersionedObjectSerializer.java:26-38`, `ObjectSerializer.java:38-58`); twelve tests with artificially constructed payloads. Sonnet 5.5 approved the decoder after a MEDIUM finding was fixed: missing count bytes at the end of the stream now yield empty lists (`ListSerializer.java:30-34`), a missing daylight byte yields `false` (`BooleanSerializer.java:23-24`) and an empty payload yields a V1 snapshot without entries – Java parity. Within an entry, truncated payloads deliberately remain an error. `RawCubeState.GetMetadataBase64()` still throws on missing `M:` blocks (stricter than Java, deliberate AP-1.2 decision); the decoder itself accepts empty payloads. This prepares the `M:` side of the roundtrip acceptance criterion; the `C:`/`L:` adopters and cardinality checks are still missing. The test payloads come from Java code, not from a golden capture (`UNKNOWN`).

### AP-2.3 – Time profiles & operating modes
- **Description:** `WeekTemperatureProfile`, `DayTemperatureProfile`, `TemperatureProfileSwitchPoint` (**13 switch points/day**, split 7 + 6 across two telegrams; **correction 24.11.2026:** an older version stated 6), `RoomControlMode`, boost/descaling/window-open rules (`max.properties:2-12`, `WeekTemperatureProfileBuilder.java`; `domain-model.md:359`, `byte-abgleich-python-java.md`).
- **Effort:** 5 PT
- **Risks:** Wrong switch-point count/time order; daylight saving time/changeover (`DaylightSavingConfiguration.java`).
- **Dependencies:** AP-2.1.
- **Acceptance criteria:**
  1. 7 days × up to 13 points can be created, validated and serialized (`DayTemperatureProfile.java:16-18`).
  2. Overlapping/unsorted switch points are rejected.
  3. DST change causes no data loss (FakeClock test).

### AP-2.4 – Domain validators
- **Description:** Business rules from `ConfigureDeviceValidator`, `RemoteCredentialsValidator`, `SoftwareInformationValidator` and the `validators/exceptions/*` (names, radio address, device type per room, room with devices cannot be deleted).
- **Effort:** 4 PT
- **Risks:** Rules reconstructed incompletely from bytecode.
- **Dependencies:** AP-2.2.
- **Acceptance criteria:**
  1. Every `MAXxxxx` validation exception has a test case.
  2. `RemovedRoomStillHasDevicesAssociated` is raised before deletion.
  3. Validators are pure functions without IO.

**Total effort phase 2: 19 PT**

---

## Phase 3 – Persistence

### AP-3.1 – SQLite schema & migrator
- **Description:** Tables `settings`, `cubes`, `rooms`, `devices`, `device_states`, `week_profiles`, `switch_points`, `metadata_cache`, `audit_log` (see `docs/target-architecture.md` §7), code-first migrator, `Data Source=:memory:` for tests.
- **Effort:** 4 PT
- **Risks:** Schema instability; parallel writers.
- **Dependencies:** V1, AP-2.2.
- **Acceptance criteria:**
  1. `dotnet run -- migrate` creates the schema on an empty DB and is idempotent.
  2. All tables have PK/FK; `Serial`/`RadioAddress` indexed.
  3. No business logic in SQL migrations.

### AP-3.2 – Repositories & SettingsStore
- **Description:** `ICubeStateStore`, `IRoomRepository`, `ISettingsStore`; upsert by foreign key; lazy loading not needed (small data volume); adopt settings from `Default.settings` in terms of content (`extracted/localbackend/Default.settings:1-9`).
- **Effort:** 4 PT
- **Risks:** Cache divergence; wrong paths per OS.
- **Dependencies:** AP-3.1.
- **Acceptance criteria:**
  1. Reconnect overwrites the cache consistently (upsert, no duplicates).
  2. Settings are stored platform-appropriately under `%LOCALAPPDATA%/MaxCube` or `~/.config/MaxCube` (not `~/.MAX`).
  3. `ISettingsStore` has no static methods (difference from the original `SettingsProvider.java:35-76`).

### AP-3.3 – Audit & error-code persistence
- **Description:** Commands and `MAXxxxx` codes in `audit_log`, structured logging (replaces Log4j, `extracted/localbackend/log4j.xml`); configurable log retention (original `SwipeLogsOlderThanDays=7`, `Default.settings:3`; `Application.deleteLogsOlderThan():287-295`).
- **Effort:** 2 PT
- **Risks:** Log volume spikes on connection problems.
- **Dependencies:** AP-3.1.
- **Acceptance criteria:**
  1. Every failed command produces exactly one audit entry with a code.
  2. Retention deletes entries > 7 days (configurable), tested with FakeClock.

**Total effort phase 3: 10 PT**

---

## Phase 4 – Business logic (application layer)

### AP-4.1 – Commands & queries
- **Description:** Use cases derived from `essentials/.../core/command/*.java`: `SetRoomAutoMode`, `SetRoomPermanentMode`, `SetRoomTemporaryMode`, `SetRoomComfortTemperature`, `SetRoomEcoTemperature`, `SetRoomWindowOpenTemperature`, `SetRoomMaximumTemperature`, `SetRoomTemperatureOffset`, `SetRoomConfigBoost`, `SetRoomConfigDecalcification`, `SetRoomConfigValveOffset`, `SetRoomWeekTemperatureProfile`, `SetDeviceDisplaySetPointActual`, `SetDeviceDaylightSavingMode`, `AssociateNewDeviceToRoom`, `DisassociateDeviceFromRoom`, `ConfigureDevice`.
- **Effort:** 8 PT
- **Risks:** Order/dependencies of several sub-commands (e.g. mode + temperature); rollback on partial failure.
- **Dependencies:** AP-1.4, AP-2.2, AP-3.2.
- **Acceptance criteria:**
  1. Every use case has exactly one handler with `ICommandHandler<T>` and tests.
  2. A failed cube command produces no persisted partial change (transaction boundary).
  3. NAME convention and DTO fields match `max-dwr.xml:53-67` (functional completeness).

### AP-4.2 – State synchronization & events
- **Description:** Mapping `RawCubeState` → domain → SQLite → UI events; `IMessenger` messages; airtime/last ping (`MaxCubeAirtimeStatus`, `MaxCubeLastPing`); duty-cycle warning (`js/com_structure/views/dutyCycleFullView.js`).
- **Effort:** 5 PT
- **Risks:** Event storm with the 5 s watchdog; UI lags behind.
- **Dependencies:** AP-1.2, AP-2.2, AP-3.2.
- **Acceptance criteria:**
  1. At most one `CubeStateChanged` event per aggregate per state update.
  2. UI test: an event changes a ViewModel property without a poll loop.
  3. Duty-cycle warning appears at the evidenced threshold (source in the documentation).

### AP-4.3 – Discovery, watchdog, reconnect
- **Description:** Cube finder (subnet scan, `cubeScanTime=1800`), `ConnectionWatchDog` (5 s), `idleTimeBeforeLocalAppDisconnect=30000`, `cubeIdleTimeToSend=900000`, automatic reconnect with backoff.
- **Effort:** 5 PT
- **Risks:** Broadcast unavailable in IPv6/containers; overly aggressive reconnects.
- **Dependencies:** AP-1.1, AP-1.3, AP-3.2.
- **Acceptance criteria:**
  1. Discovery finds a fake cube in the local subnet (integration test with loopback).
  2. After a socket abort the service reconnects within a configurable window.
  3. Backoff is testable with FakeClock; no busy loops.

### AP-4.4 – (discarded) Remote-proxy adapter
- **Description:** ~~Functional counterpart to `IMaxRemoteProxy`, `RemoteCredentials`, `ActivateProductRequest`, `CheckProductActivationRequest` (`localbackend/.../remoteproxy/*`). Feature flag, off by default.~~ **Decision 24.11.2026: discarded.** The opcode family is now fully `CODE-EVIDENCED` (HTTP cloud protocol, section 6.1 of the protocol document); the MAX! cloud has been shut down since 01.07.2023. A local integration needs neither remote access nor product activation nor access data. The 6 PT effort is dropped; `Phase-4-Gesamtaufwand` thus falls from 30 PT to 24 PT.
- **Effort:** 6 PT
- **Risks:** External services (`http://max.eq-3.de/cube`, `Default.settings:1-9`) no longer reachable; data protection.
- **Dependencies:** AP-4.1, AP-3.2.
- **Acceptance criteria:**
  1. Disabled: no outbound HTTP connection at startup.
  2. Enabled: credentials encrypted in SQLite, never in the log.
  3. All error codes `MAX1037/MAX10038/MAX1039/MAX1046/MAX1047` covered (errorcodes.properties).

**Total effort phase 4: 24 PT (AP-4.4 discarded, 24.11.2026)**

---

## Phase 5 – User interface

### AP-5.1 – Shell, navigation, tray, styles
- **Description:** Avalonia `MainWindow`, `INavigationService`, `IDialogService`, system tray (original: `MaxSystemTrayHandler.java`), theme, 6 languages (`MaxLocalAppStarter_*.properties` as a text source in terms of content), license notice (`License.txt`).
- **Effort:** 6 PT
- **Risks:** Tray differences on Linux/macOS; fonts/translations.
- **Dependencies:** V1, AP-4.2, `docs/gui-specification.md`.
- **Acceptance criteria:**
  1. App starts without a browser; navigation works by mouse and keyboard.
  2. Language change at runtime updates all texts.
  3. Tray: start/status/exit as specified in terms of content.

### AP-5.2 – Room control & device view
- **Description:** `RoomControlView`, temperature selection (comfort/eco/offset/boost), mode (auto/manual/permanent/temporary), window-open status, device list, airtime/status icons. Sources: `roomControlController.js`, `roomTemperatureView.js`, `roomDeviceView.js`.
- **Effort:** 8 PT
- **Risks:** Many small states; wrong order of mechanisms (room vs. device); 0.5 °C grid.
- **Dependencies:** AP-4.1, AP-4.2, AP-5.1.
- **Acceptance criteria:**
  1. Every mode from `RoomControlMode.java` is reachable and sends the correct use case.
  2. Temperatures are displayed and validated on the 0.5 °C grid.
  3. UI tests without a real cube (mock `ICubeGateway`).

### AP-5.3 – Weekly program & device configuration
- **Description:** `WeeklyProgramView` (7 days, switch points, copying days), device configuration (radiator thermostat, wall thermostat display, shutter contact time, push-button configuration, descaling, offset). Sources: `weeklyProgramController.js` (108 KB!), `com_configuration/*`.
- **Effort:** 10 PT
- **Risks:** Largest UI scope; copy/undo semantics; 13 switch points per day (7 + 6 in two telegrams).
- **Dependencies:** AP-2.3, AP-4.1, AP-5.1.
- **Acceptance criteria:**
  1. All 7 days and switch points are editable; copy-day works.
  2. Invalid profiles are rejected before sending (validator).
  3. Every configuration option from `max-dwr.xml:53-67` has a UI field.

### AP-5.4 – Structure, discovery, network, time, update
- **Description:** Wizard "Neues Gerät" (inclusion mode 60 s), rename/delete/move room/device, cube network/IP, time setting/NTP, firmware update view. Sources: `js/com_structure/**`, `cubeNetworkSettingController.js`, `setCubeTimeController.js`, `localCubeUpdateController.js`.
- **Effort:** 10 PT
- **Risks:** Inclusion-mode timeouts; states without a way out; firmware approval (AP-1.6).
- **Dependencies:** AP-1.3, AP-1.6, AP-4.3, AP-5.1.
- **Acceptance criteria:**
  1. Wizard shows clear progress/error states (original views as a catalogue: `foundNoDeviceView.js`, `deviceLimitReachedView.js`, `roomLimitReachedView.js`, `noMoreDevicesPossibleView.js`).
  2. Deleting a room with devices is blocked and explained.
  3. Firmware write cannot be executed without a feature flag.
  4. States without a way out (e.g. aborted wizard) are reachable via `Abbrechen`/`Zurück`.

### AP-5.5 – Settings, status, error dialogs, acceptance
- **Description:** Proxy, remote, language, log retention, status/errors (`MAXxxxx` as readable messages), duty cycle, connection loss, product activation. Acceptance test against `docs/gui-specification.md` and `docs/features.md`.
- **Effort:** 6 PT
- **Risks:** Missing legacy functions only become visible during acceptance.
- **Dependencies:** AP-5.1 through AP-5.4, AP-4.4 (if active).
- **Acceptance criteria:**
  1. Every view from `docs/gui-specification.md` is present or explicitly documented as "not implemented (P2)".
  2. All 53 error codes deliver a defined UI message (fallback `MAX0001`, `errorcodes.properties:1`).
  3. Cross-platform smoke test on win-x64, linux-x64, osx-x64, osx-arm64.

**Total effort phase 5: 40 PT**

---

## 6. Overall overview & order

| Phase | Work packages | Effort | Blockers |
| --- | --- | --- | --- |
| 0 | V1–V3 | 3–6 PT | — |
| 1 | AP-1.1 … AP-1.6 | 34 PT (+6) | Golden capture for 1.5/1.6 |
| 2 | AP-2.1 … AP-2.4 | 19 PT | Protocol documentation (AP-1.4) |
| 3 | AP-3.1 … AP-3.3 | 10 PT | Domain model (AP-2.2) |
| 4 | AP-4.1 … AP-4.4 | 24 PT | Protocol + domain + persistence |
| 5 | AP-5.1 … AP-5.5 | 40 PT | Business logic + GUI specification |
| **Total** | | **130 PT (+12 optional)** | ≈ 6–7 months 1 person |

**Critical path:** `V3 → AP-1.1 → AP-1.2 → AP-1.3 → AP-1.4 → AP-1.5 → AP-4.1 → AP-5.3`
(AP-1.5 radiograms are the highest risk in derivation theory; therefore complete before any UI work.)

**Recommended milestones:**
- M1 (after AP-1.3): fake-cube test suite green, handshake + state receipt verified.
- M2 (after AP-1.5): all opcodes and radiograms byte-identical against the golden capture.
- M3 (after AP-3.2): domain state survives reconnect and app restart.
- M4 (after AP-4.2): first functional end-to-end use case (set room temperature) without UI.
- M5 (after AP-5.5): feature matrix P0 complete, cross-platform smoke green.

---

## 7. Explicit open points (not facts, but open questions)

1. **Radiogram bit fields** fully verified? → **partially clarified 24.11.2026:** the first `0x10` telegram (7 points) is `LIVE-PROVEN` for encoding and sending, but not for acceptance by the cube (`sent_not_confirmed`, no `A:` acknowledgement evidenced). The second telegram (7 + 6) remains `UNKNOWN`; still no golden capture with > 7 points. Until then inference.
2. **UDP firmware frame** exact? → only opcode table and URL construction evidenced (`CubeUdpProxy.java:105`).
3. **Duty-cycle threshold** numeric? → UI view exists, value not yet found in sources.
4. ~~**Maximum switch points per day** = 6?~~ **Clarified 24.11.2026:** 13 per day, 7 + 6 in two `0x10` telegrams. The first telegram (7 points) is `LIVE-PROVEN` for encoding and sending via the Python integration; the second telegram with flag 1 / offset 7 remains `UNKNOWN` (no capture with more than 7 points). See `docs/ha-maxcube-erkenntnisse.md`.
5. **Remote portal endpoints** still reachable? → not tested (no network access to `max.eq-3.de` within this analysis).
6. **Behaviour with several cubes** in the same network? → CubeFinder exists, selection UI not conclusively evidenced.

These points are marked `UNKNOWN` in `docs/maxcube-protocol.md` and `docs/features.md` and may only be transferred into the implementation after evidence is obtained.

---

## 8. Interim status 24.11.2026 – local first version after four Sonnet reviews

Phases 2 to 5 are implemented for the local core to the extent that the app starts without hardware and offers a complete functional flow (connect → show rooms → send temperature/mode/weekly profile → cache/audit). **OBSERVATION**, not hardware evidence: the end-to-end test runs via `FakeGateway` and `FakeStore`, `CubeGateway` is now tested with a replaceable transport (EOF fault, `q:` on shutdown), but not against a physical cube. The Avalonia tests are also code-based (CanExecuteChanged, warning, input), not runtime click tests.

| Work package | Status 24.11.2026 |
| --- | --- |
| AP-1.5 Radiogram builder | `RadiogramBuilder` (bit fields LSB-first, 17-byte standard telegrams, `0x10/0x11/0x40`) and `WeekProfileSerializer` (7 + 6 points, Java lengths 25/23 bytes, `ToBytes(minimumLength: 0)`) implemented. Byte pairs tested against `docs/byte-abgleich-python-java.md` (20.5 °C/06:00 → `52 48`; 4.5 °C/24:00 → `13 20`; 30.5 °C/20:00 → `7A F0`). The 24-bit radio address is now written byte-wise big-endian (`ByteUtil.java:8-20`); 10:45 is rounded to 11:00 as in Java. First `0x10` telegram live-proven for encoding/sending, second `UNKNOWN`. |
| AP-2.2 Aggregate & state adopters | `CubeStateAssembler` builds `Cube` from `H:`/`C:`/`M:`/`L:` including configuration and telemetry; `Cube`/`Room`/`Device`, `DomainLimits` (20/140/32, `INFERENCE`) and `DomainEvents`. Nine aggregate tests. Corrected after a Sonnet finding: `L:` offsets (mode `state[0]`, setpoint `state[2]`, actual low `state[6]`), window/boost duration via the Java VALUES table, too-short blocks are skipped individually, orphaned devices do not fault, `Cube.AddDevice` mutates only after all checks. After Sonnet round 2: device parameters only when `isValid` (`DeviceStateSerializer.java:64-69`), per-device `try/catch`, room values from the master rather than the last device (`:73-98`), actual temperature as `MeasuredTemperature` without OFF/ON normalization; values outside 0..40 °C are discarded ("no measurement") instead of inventing clamps (round 3, N4). |
| AP-2.3 Time profiles | `WeekTemperatureProfile` in the domain (max. 13 points/day); `SwitchPointViewModel` text-based for XAML binding; `DayProfile` is passed unchanged to the gateway. `DayProfile.Validate` requires ascending times in the 5-minute grid, 0.5 °C steps and 00:00 (= 24:00) as the last point; the UI generates sendable default days. |
| AP-3.1/3.2 SQLite & store | `SqliteStateStore` with `settings`, `cubes`, `rooms`, `devices`, `audit_log`; upsert/replace per serial prevents duplicates on reconnect; `Data Source=:memory:` tests. Audit is stored without a limit; `ReadAuditAsync` returns the last 500 entries, the UI shows the last 100 of them; `DisposeAsync` waits on the gate. |
| AP-4.1/4.2 Application layer | `CubeApplicationService` as the only write location: `Connect`/`LoadCached`/`SetRoomTemperature`/`SetRoomControlMode`/eco/comfort/`SetWeekDayProfile`/`Disconnect`, one `SemaphoreSlim` gate per operation, cache + audit + `StateChanged` event. Cached offline state is read-only; cache write errors after connect close the session; domain events are drained after operations. After round 2: cache/audit errors **after** sending report `Warning` instead of "not sent"; temporary end time must be in the future (`SetRoomTemporaryMode.java:19-21`); `LoadCached` also works after `Faulted`; partial weekly-profile failures are audited. Round 3: setpoints are quantized to the 0.5 °C wire grid before sending and caching (N5), `Fault()` runs before `Fail()` (N3). |
| AP-5.1/5.2/5.3 UI (core) | Avalonia `MainWindow` with rooms / weekly profile / status; ViewModels without controls (twelve UI tests); DI in `App.axaml.cs`, native start in `Program.cs`. Round 1: `DataContext` set in the composition root, `StateChanged`/`ConnectionChanged` marshalled to the UI thread via `IUiDispatcher`, port as `NumericUpDown`, `WinExe`. Round 2: `[NotifyCanExecuteChangedFor]` on `IsConnected`/`SelectedRoom` (buttons become active), temporary time from `DateTimeOffset.Now` (local time as `Calendar.getInstance()`, `RadiogramSpecification.java:187-191`), input field does not jump back, `CubeGateway` is `IAsyncDisposable` and sends `q:` on shutdown. Round 3/4: own `WarningText` (warning is not overwritten by "Disconnected" and is cleared after the next action), state refresh with the same room selection does not overwrite the input (N6/N8); on initial load and reconnect, however, the room setpoint is adopted (N9). |

**Verification:** `dotnet test MaxCube.slnx` → **232/232** green (Domain 55, Application 15, Protocol 143, Infrastructure 7, UI 12); `dotnet build` 0 warnings/0 errors; startup with a local .NET 10 host stable. `raw/` unchanged, no secrets added.

**Sonnet overall review, round 1 (24.11.2026):** `anthropic/claude-sonnet-5-5` confirmed the build/test figures but did **not** approve the version. Confirmed findings: missing `DataContext` wiring (K1), little-endian radio address (K2), shifted `L:` offsets (H1), missing UI-thread marshalling (H2), wrong 10:45 rounding (H3), missing weekly-profile validation (H4) plus M1–M8. All points were fixed and covered by tests.

**Sonnet overall review, round 2 (24.11.2026):** again **not approved**, but confirmed: K1/K2, H1–H4 and M1–M8 are genuinely fixed, Java sources present (480 `*.java`), 7+6 serialization and byte pairs conform to Java/Python. New findings: H-A (`CanExecuteChanged` was never raised → buttons stayed disabled), H-B (temporary end time in UTC instead of local time), H-C (one device with 45.6 °C actual aborted the connect; `isValid` was not checked), M-A (cache error after sending), M-B (only `CubeTransportException` set `Faulted`), M-C (actual temperature normalized as setpoint), M-D (input field jumped back), M-E (room values from the wrong device), M-F (profile editor did not load a cube profile), M-G (no `q:` on shutdown). H-A/B/C and M-A/B/C/D/E/G are fixed; M-F is documented as a known gap in the UI and in `README.md`.

**Sonnet overall review, round 3 (24.11.2026):** again **not approved**, but without critical or high findings. Confirmed: all round-2 corrections present in the code, no deadlock in `SendAsync`, `DisposeAsync` safe against double calls, warning threading correct. Findings: N1 (documentation said "all covered by tests" although gateway/clock/M-D were untested), N2 (master test did not prove the master rule), N3 (race: `Fail()` before `Fault()`), N4 (clamping invented 40.0 °C), N5 (setpoint not quantized), N6 (warning lost on disconnect), N7 (gate blocks disconnect for up to 130 s), N8 (input reset on StateChanged). N1–N6 and N8 are fixed and covered by tests; N7 remains documented as LOW.

**Sonnet diff re-check, round 4 (24.11.2026):** again **not approved**, but confirmed: N1–N6 are genuinely fixed in code and tests, no new master/gateway/quantization regression. New finding N9 (MEDIUM): the N8 suppression also applied on initial load and after `Disconnect` (`previousId == null`), so the room setpoint was not adopted into the input field. Fixed by suppressing only for an identical, already existing selection; tests with 19.5 °C for connect and reconnect. N10 (LOW): `WarningText` was never cleared; it is now reset at the start of every action. Additional tests for permanent/temporary quantization and one outdated test name corrected.

**Final diff review (24.11.2026):** `anthropic/claude-sonnet-5-5` **`APPROVED`** the local first version. Confirmed: N9 and N10 are fixed in code and tests; 232/232 green, clean build. Hardware validation remains `UNKNOWN`; N7 and M-F are documented as LOW/gap. Cosmetically, a missing individual test for an actual room change remains (in the code clearly via `OnSelectedRoomChanged`).

**Still open (no blockers for the local first version):** golden capture with > 7 points per day, functionally evaluated `S:` response (Java acknowledges `s:` with `S:`, not `A:`; `A:` acknowledgement remains `UNKNOWN`), ~~loading the cube weekly profile into the editor (M-F)~~ **done, see section 9**, ~~decoupling the gateway semaphore (N7)~~ **done, see section 9**, 60 s inclusion monitoring, ~~session monitoring in orchestration~~ **done, see section 9**, device configuration/discovery/wizard, cross-platform smoke, plus all points from section 7.

---

## 9. Panel 24.11.2026 – round 1 implemented

User instruction: a three-member panel works autonomously, at least three rounds;
only after complete processing is the work finished. At the user's request (no credit)
Sonnet was replaced by the free `opencode/deepseek-v4.1-flash-free`. The binding
decision and follow-ups are in `docs/gremium.md`.

| Point | Implementation | Evidence |
| --- | --- | --- |
| M-F load weekly profile | `WeekProfileDecoder` reads `config[11..]`: 7 days Saturday→Friday, 13 entries × 2 bytes, `(value & 511)*5` minutes, `((value>>9)&63)/2` °C, first `00:00` ends the day. `Cube.ApplyWeekProfile`, `Room.WeekProfile`, snapshot/migration, send lock without a loaded profile; UI loads on room change/`StateChanged`. | `src/MaxCube.Protocol/State/WeekProfileDecoder.cs`, `src/MaxCube.UI/ViewModels/MainWindowViewModel.cs` |
| N7 cancel/timeouts | `OperationGate` with limited gate acquisition and `CancelActive()`; `CubeGateway`/`CubeApplicationService` switched to leases and tokens; `q:`/cleanup/shutdown with time budgets (250/700/500 ms, outer 2 s); UI `CancelCommand` with its own CTS, `OperationCanceledException` → "Abgebrochen"; asynchronous shutdown without blocking `GetResult()`. | `src/MaxCube.Application/Abstractions/OperationGate.cs`, `src/MaxCube.Infrastructure/Connectivity/CubeGateway.cs`, `src/MaxCube.UI.Desktop/App.axaml.cs` |
| Session monitoring | `CubeSessionMonitor` wired into the gateway; `l:` refresh on inactivity (default 30 s, configurable), device values are transferred into the existing aggregate via `CubeStateAssembler.ApplyDeviceStates` and written to the cache; `StaleChanged`/reconnect with exponential backoff (1…30 s). | `src/MaxCube.Protocol/Session/CubeSessionMonitor.cs`, `src/MaxCube.Infrastructure/Connectivity/CubeGateway.cs`, `src/MaxCube.Application/Services/CubeApplicationService.cs` |
| Follow-ups | Device list filtered by `RoomId`, `SuccessText`/`WarningText` visible, `start.cmd` looks for a .NET 10 SDK and always rebuilds incrementally, audit documentation corrected (stored without limit, 500 read, 100 displayed). | `src/MaxCube.UI/ViewModels/MainWindowViewModel.cs`, `start.cmd`, `README.md` |

**Verification after round 1:** `dotnet build` 0 warnings/0 errors, `dotnet test MaxCube.slnx`
**243/243** green (Domain 55, Application 20, Protocol 147, Infrastructure 9, UI 12).
`start.cmd` smoke: SDK discovery, incremental build and app start observed; process
then terminated in a controlled manner. No hardware validation – `UNKNOWN`.

---

## 10. Panel 24.11.2026 – round 2 implemented

All three members put the session/reconnect path in first place. The binding
decision and the cross-check of every vote are in `docs/gremium.md`.

| Point | Implementation | Evidence |
| --- | --- | --- |
| Session/reconnect path | New `MarkUnreachableAsync()` cleans up queue/transport/session after `Unreachable` and three `l:` errors and fires `SessionUnreachable` only once. `ConnectAsync` waits for the old monitor task before restarting; the gate timeout of `DisconnectAsync` also resets all fields. | `src/MaxCube.Infrastructure/Connectivity/CubeGateway.cs`, `tests/MaxCube.Infrastructure.Tests/CubeGatewayTests.cs` |
| M-F end-to-end | Send lock now per day of week (`HasWeekProfile(roomId, day)`) so a discarded day is not overwritten with 21.0 °C. New assembler test with a complete 211-byte `C:` block; UI test for "profile loaded → sending locked/allowed". | `src/MaxCube.Application/Services/CubeApplicationService.cs`, `src/MaxCube.UI/ViewModels/MainWindowViewModel.cs`, `tests/MaxCube.Protocol.Tests/CubeStateAssemblerTests.cs`, `tests/MaxCube.UI.Tests/ViewModelTests.cs` |
| Follow-ups | `StatusText` restored after cancel and visible on stale/reconnect; the never-evaluated option `RefreshInterval` removed (`IdleTimeout` controls the 30 s `l:` refresh); N3 test (`Fault()` before `Fail()`) added. | `src/MaxCube.UI/ViewModels/MainWindowViewModel.cs`, `src/MaxCube.Protocol/CubeConnectionOptions.cs`, `tests/MaxCube.Protocol.Tests/CubeCommandQueueTests.cs` |

**Verification after round 2:** `dotnet build` 0 warnings/0 errors,
`dotnet test MaxCube.slnx` **249/249** green (Domain 55, Application 21, Protocol 149,
Infrastructure 10, UI 14). The previously failing reconnect test is green. Hardware remains
`UNKNOWN`. i18n (only one of three votes) and inclusion are explicitly
postponed; audit retention remains a documented limitation.

---

## 11. Panel 24.11.2026 – round 3 completed

Two votes (`tidepool-alpha-free`, `nemotron-3.5-lightning-free`) gave
`APPROVED`, the independent review (`deepseek-v4.1-flash-free`) gave
`NOT APPROVED` because of a UI blocker. After cross-checking against the code, the
blocker was adopted as a binding round-3 decision and fully implemented.

| Point | Implementation | Evidence |
| --- | --- | --- |
| Reconnect in the UI | `OnConnectionChanged` now also handles `Connected`: `IsConnected = true`, status "Verbindung wiederhergestellt". The previously locked action path is usable again after auto-reconnect; a new connect no longer runs into "Already connected". Regression test via the real service. | `src/MaxCube.UI/ViewModels/MainWindowViewModel.cs`, `tests/MaxCube.UI.Tests/ViewModelTests.cs` |
| Device list after room change | `OnSelectedRoomChanged` calls `RefreshDevices`; the list immediately shows the new room's devices. Regression test with two rooms/devices. | `src/MaxCube.UI/ViewModels/MainWindowViewModel.cs`, `tests/MaxCube.UI.Tests/ViewModelTests.cs` |
| Follow-ups | Cancel notice is set before the abort (no lingering "Abbrechen …"); XAML stale text aligned with StatusText. | `src/MaxCube.UI/ViewModels/MainWindowViewModel.cs`, `src/MaxCube.UI/Views/MainWindow.axaml` |

**Final verification:** `dotnet build` 0 warnings/0 errors,
`dotnet test MaxCube.slnx` **251/251** green (Domain 55, Application 21, Protocol 149,
Infrastructure 10, UI 16). The panel is thus completed after three rounds and
the hardware-free, local functionality is **`APPROVED`**. Hardware remains
`UNKNOWN`. The max-retry limit was deliberately not introduced (round-1 decision:
endless backoff 1…30 s). i18n, inclusion and audit retention remain
postponed. Full votes and cross-checks: `docs/gremium.md`.

---

## 12. Panel round 4 – versioning, autodiscovery, light/medium (24.11.2026)

**FACT – initial situation:** The app had no own version display (only the cube
firmware in StatusText) and no UDP discovery. `docs/features.md` had
no status column; `docs/INDEX.md:20` stated 78 instead of the actual 61 feature rows.

**Votes (summary, all free):**
- Product/UX (`tidepool-alpha-free`): version + discovery confirmed; P1/P2
  house control and weekly-program copying recommended.
- Technology (`nemotron-3.5-lightning-free`): protocol evidenced
  (`maxcube-protocol.md:47-67`), no UDP infrastructure in the code; central
  assembly version and fake-transport tests recommended.
- Review (`deepseek-v4.1-flash-free`): measured 251/251 itself; found documentation
  drift; required SQLite schema version and last endpoint as follow-ups;
  broadcast only user-initiated, no cloud update check.

**Binding decision and implementation (3/3):**

| Point | Implementation | Evidence |
| --- | --- | --- |
| App versioning | `<Version>0.2.0</Version>` centrally in `Directory.Build.props`; `IAppVersionProvider`/`AppVersionProvider` reads the assembly information version; window title and status tab show it. SQLite: `PRAGMA user_version = 1`, `app.version`, `schema.version`, both idempotent. | `Directory.Build.props`, `src/MaxCube.UI/AppVersionProvider.cs`, `src/MaxCube.Infrastructure/Persistence/SqliteStateStore.cs` |
| UDP autodiscovery | `IDiscoveryTransport` + `UdpDiscoveryTransport` (23272, broadcast 224.0.0.1/255.255.255.255, TTL 5, 3 packets/50 ms), `CubeDiscoveryProtocol` (request 19 bytes, response `eQ3MaxAp`/`eQ3MaxBl`, serial number 8..17, firmware 24..25 big-endian), `CubeDiscoveryService` deduplicates hits. UI: "Cube suchen", hit list, "Übernehmen" fills host/port 62910; last endpoint in `settings`, manual input checked against exactly four IPv4 octets (0..255). | `src/MaxCube.Application/Services/CubeDiscoveryService.cs`, `src/MaxCube.Infrastructure/Connectivity/UdpDiscoveryTransport.cs`, `MainWindowViewModel.cs`, `MainWindow.axaml` |
| P1 light/medium | House control (selected rooms jointly auto/eco/comfort/temporary) and "copy day to loaded days" (per-weekday send lock remains). | `MainWindowViewModel.cs`, `RoomViewModel.cs`, `ViewModelTests.cs` |
| Documentation | Status column in `features.md` (61 rows: implemented/partial/missing/discarded), feature count in `INDEX.md` corrected, autodiscovery source corrected to the local UDP finder. | `docs/features.md`, `docs/INDEX.md` |

**Verification:** `dotnet build MaxCube.slnx` 0 warnings/0 errors,
`dotnet test MaxCube.slnx` **268/268** green (Domain 55, Application 25, Protocol 149,
Infrastructure 11, UI 28). Discovery tests use only fake transport and
parsed example bytes; there is **no** broadcast, hardware or
firmware write access.

~~**Open functions compared with the original (status round 4):** inclusion, structure
management, configuration, network/time, i18n, tray/instance protection and others.~~ **Completed in round 5, see
section 13.** Deliberately discarded remain cloud/remote access, product account, proxy,
browser/Jetty shell, app update check via HTTP and cube firmware writing.
Hardware receipt of the UDP response remains `UNKNOWN`.

---

## 13. Round 5 – all locally mappable features, Windows and Linux (24.11.2026)

**Procedure (FACT):** All panel calls failed due to the free limit; there are no
model votes. Implementation follows the binding decisions of rounds 1–4 and the
user instruction (`docs/gremium.md`, round 5). Details of the decisions there.

**User addition 25.11.2026 (FACT, reported by the user, not externally verified):**
eQ-3 has shut down MAX! cloud control and removed the cloud app from the Android and
iOS app stores. This supports the exclusion of remote access, product account, proxy and
app update in decision 1; a local app must work without the manufacturer's cloud.

| Package | Implementation | Evidence |
| --- | --- | --- |
| Protocol | New telegrams `SetRoomValveFunctions` (0x12), `SetDisplayMode` (0x82), `SetGroupAddress`/`RemoveGroupAddress` (0x22/0x23), `LinkPartner` (0x20/0x21); `MetadataEncoder` (counterpart to the decoder) with roundtrip safety check and 1900-byte blocks; `NewDeviceDecoder` (`N:`); `CubeConfigurationCodec` (push buttons, timezone); `CubeUdpCodec` (network `c`/`C`, no firmware opcode). Descaling weekday corrected (code 2 = Monday). | `src/MaxCube.Protocol/Commands/RoomCommands.cs`, `State/MetadataEncoder.cs`, `State/CubeConfigurationCodec.cs`, `State/CubeUdpCodec.cs`, `tests/MaxCube.Protocol.Tests/AdminProtocolTests.cs` |
| Domain | `Cube.RenameRoom/RenameDevice/ReorderRooms/RemoveDevice/RemoveRoom/MoveDevice/EnsureRoomMaster/NextFreeRoomId`, raw valve values (`HasValveConfiguration`), atomic move with limit check. | `src/MaxCube.Domain/Aggregates/Cube.cs`, `tests/MaxCube.Domain.Tests/StructureTests.cs` |
| Application | `ICubeAdminGateway`, `ICubeNetworkConfigurator`; `CubeApplicationService` (partial) with room configuration, boost/valve, display, inclusion (60 s in 10 s windows), assignment, rename/delete/move/sort according to the **design principle**, push buttons, timezone/cube time/NTP, network, bundled temperatures (3 s), airtime notice, diagnostic report, settings; `ErrorCatalog` (52 codes). | `src/MaxCube.Application/Services/CubeApplicationService.Admin.cs`, `Models/ErrorCatalog.cs`, `tests/MaxCube.Application.Tests/AdminServiceTests.cs` |
| Infrastructure | `CubeGateway.Admin` via the same queue (one socket, one worker), `UdpNetworkConfigurator`, `AppPaths`, `InstanceLock`, `FileAppLog`, `SystemLanguage`. | `src/MaxCube.Infrastructure/Connectivity/CubeGateway.Admin.cs`, `Platform/*`, `tests/MaxCube.Infrastructure.Tests/CubeGatewayAdminTests.cs`, `PlatformTests.cs`, `UdpNetworkConfiguratorTests.cs`, `SystemLanguageTests.cs` |
| UI | Nine tabs, graphical day bar, quick editing, modal wait/error dialogs, six languages (`Localizer`, completeness test against XAML and code), multilingual help with context buttons, settings, tray, instance-protection notice window, wrapping layout. | `src/MaxCube.UI/Views/MainWindow.axaml`, `Localization/*`, `ViewModels/MainWindowViewModel.*.cs`, `tests/MaxCube.UI.Tests/AdminViewModelTests.cs` |
| Platform | `Composition.cs`, `--selfcheck`, CI matrix Windows+Linux (including self-contained packages), `start.sh`, `.gitattributes`. Self-test findings: container must be disposed asynchronously; `InvariantGlobalization` makes .NET culture detection ineffective → own system language detection. | `.github/workflows/ci.yml`, `src/MaxCube.UI.Desktop/*`, `start.sh` |

**Verification:** Build 0 warnings/0 errors; `dotnet test MaxCube.slnx` **519/519** green under
Windows and – in a Debian 13 WSL2 instance with Linux SDK 10.0.404 – also **519/519**
(Domain 64, Application 84, Protocol 198, Infrastructure 66, UI 107); `--selfcheck` under
both systems `OK`; Windows GUI starts stably. **Not evidenced:** real hardware for the
inferred write formats, real UDP response, Linux UI on a desktop.

**What remains (see README "Next steps"):** hardware test and packet capture of the
`INFERENCE` formats, golden capture for `S:`/UDP, Linux desktop visual check, optional
drag-and-drop in the weekly-program editor.
