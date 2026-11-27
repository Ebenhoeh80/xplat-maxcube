> English translation of docs/gremium.md. Machine-created; DE/EN reviewed by the user on 27.11.2026; FR/IT/NL/PL feedback expected via issues/PRs.

# Committee for the "best app with all features"

As of 24.11.2026. Assignment: autonomously make the MAX! local app the best achievable local app over several rounds. Decisions are made **only by the committee**; implementation follows its resolutions.

## Boundary conditions (apply to all rounds)

- Cube is the source of truth; SQLite is only a cache/offline display.
- No cloud, Jetty, browser, Getdown, or AGPL dependency.
- Firmware writing remains disabled (`UNKNOWN` without a golden capture).
- No hardware, cube, HA, or network write access; no commits/pushes.
- Every statement is `FACT`, `OBSERVATION`, `INFERENCE`, or `UNKNOWN`.

## Members

1. **Product/UX** – completeness of the feature set, usability, visible gaps.
2. **Technology/protocol** – architecture, protocol fidelity, robustness, testability.
3. **DeepSeek V4.1 Flash Free** – independent overall review, risks, classification of the historical Sonnet approvals. **Model change 24.11.2026:** At the user's request, Sonnet 5.5 was replaced by this free model due to lack of credit. The earlier Sonnet reviews and the approval of the local first version remain as historical `FACT` evidence but are not queried again.

**Concrete models from round 2 onward (all free):** Product/UX `opencode/tidepool-alpha-free`, technology/protocol `opencode/nemotron-3.5-lightning-free`, independent review `opencode/deepseek-v4.1-flash-free`.

## Round 4 – extension (24.11.2026)

**New user assignment:** add app versioning and autodiscovery; address the still missing functions relative to the original with **low/medium priority** (P1/P2); complete everything autonomously through to finished implementation.

### Votes (summary)

- **Product/UX** (`tidepool-alpha-free`): confirms version and auto-discovery as gaps; recommends as a P1/P2 package app version, **house control**, and **copy weekly program**. i18n/inclusion remain deferred.
- **Technology/protocol** (`nemotron-3.5-lightning-free`): discovery details (`maxcube-protocol.md:47-67`) are sound; no UDP implementation in the code. Recommends `IDiscoveryService`, fake tests, central assembly version, and display in the UI. Effort estimated at ~5 person-days.
- **Review** (`deepseek-v4.1-flash-free`): re-measured 251/251; documentation drift `INDEX.md:20` (78 instead of 61 feature rows) and missing status column in `features.md`. Additionally requires SQLite schema version and persistence of the last endpoint; broadcast only user-initiated, no cloud update check.

### Binding decision (3/3)

1. **App versioning:** central `<Version>` in `Directory.Build.props`; window title and status tab show the assembly version as the single source of truth. SQLite gets `PRAGMA user_version` plus `app.version`/`schema.version` in `settings`. No HTTP/cloud update check, no firmware writing.
2. **UDP auto-discovery:** `ICubeDiscovery` + UDP adapter per `maxcube-protocol.md:47-67` (23272, `eQ3Max*\0**********I`, broadcast + multicast, 3 packets/50 ms, response `^eQ3Max(Ap|Bl)`, serial 8..17, firmware 24..25 big-endian). User-initiated "Search for cube" button, result list with serial number/IP/firmware/bootloader, selection fills host/port. Last endpoint is persisted; manual IPv4 entry remains and is validated octet by octet. All tests run via fake socket/parser without broadcast.
3. **P1 low/medium as add-ons:** **copy weekly program** (day to all days or adopt room profile) and **house control** (several rooms jointly auto/ECO/COMFORT/temporary). Docs: status column in `features.md` and correction of the feature count in `INDEX.md`.

**Deferred (explicitly, not a defect):** i18n, inclusion/wizard, structure maintenance, network/time/firmware, airtime/duty cycle, bundled commands, tray/instance protection, app update check. Hardware remains `UNKNOWN`.

**Round 4 closing finding (FACT):** All three decision points are implemented. Version 0.2.0 is central in `Directory.Build.props`, shown in window title/status via `IAppVersionProvider`, and stored as `PRAGMA user_version = 1` plus `app.version`/`schema.version` in the cache. `ICubeDiscovery` with `CubeDiscoveryService` and `UdpDiscoveryTransport` sends the 19-byte request to broadcast/multicast (3×/50 ms), decodes serial number/firmware/bootloader, and offers "Search for cube", result list, and host/port adoption; the last endpoint is in `settings` (`cube.endpoint`). Manual host entry requires exactly four IPv4 octets (0..255; no .NET short forms). House control and "copy day to loaded days" are wired. Build 0/0, **268/268** tests green (Domain 55, Application 25, Protocol 149, Infrastructure 11, UI 28). Hardware use remains `UNKNOWN`; the UDP adapter was tested only with fake transport/parser, not against a real cube.

## Round 5 – complete feature mapping and Windows/Linux (24.11.2026)

**New user assignment (verbatim):** „mach weiter. ende erst wenn du alle features der originalapp abgebildet hast. veruche alles das ziel zu erreichen. wichtig. die app muss unter windows und linux lauffähig sein“.

**Procedure (FACT):** All four committee calls (product, technology, review, code inventory) aborted with "Free usage limit reached" before delivering results. There are therefore **no model votes** for round 5. The assignment was implemented autonomously under the still binding decisions of rounds 1–4 and the boundary conditions above; no further paid models were queried. This gap was deliberately not filled with invented votes. The remaining review is provided by the test/verification chain.

**Decision (autonomous, with rationale):**

1. **What "all features" means.** The feature matrix (`docs/features.md`) has 61 rows. All that are locally possible are mapped. Ten rows are **not mappable**: the cloud has been shut down since 01.07.2023 (remote access, product account, account info, expiry warning, password strength), proxy/app update/update splash belong to cloud and update server, the browser warning belongs to the browser, and **writing cube firmware** remains disabled (risk of device damage, no firmware file, no golden capture). The error report is **replaced by a local diagnostic report without transmission**.
   **User notice 25.11.2026 (FACT, reported by the user, not externally verified):** eQ-3 has shut down MAX! cloud control and, according to the user, also removed the cloud app from the Android and iOS app stores. Decision 1 is thereby additionally supported: there is no longer any manufacturer-side remote access path that the local app could add.
2. **Write formats without hardware proof.** Boost/descaling (`0x12`), group/link assignment (`0x20`–`0x23`), push button (`w:`), time zone (`v:`), and network (UDP) follow the read path or documentation (`INFERENCE`). Protective measures: explicit confirmation for every changing structure action; write only with raw values previously read from the cube; **round-trip safety check** for metadata (the unchanged state must be reproducible byte-identically, otherwise nothing is sent); design principle (validation, radio, metadata, then only the real model); partial failures are audited and show "Reload state".
3. **Windows and Linux.** One code base, two systems: platform-neutral data paths (`%LOCALAPPDATA%` or `$XDG_DATA_HOME`), cross-process instance protection via exclusive file, system language without ICU (the app runs with `InvariantGlobalization`), headless `--selfcheck`, CI matrix Windows + Linux, start scripts `start.cmd`/`start.sh`.
4. **Acceptance gate.** "Finished" means: build 0/0, all tests green under Windows **and** under real Linux (Debian WSL2), self-test on both, GUI start under Windows.

## Log

| Round | Focus | Decision | Implementation |
|---|---|---|---|
| 1 | Inventory + target vision | **Decision 3/3:** M-F (all rank 1), N7 (all in top 3), session monitoring (all in top 3) as a package. Feedback/device filter/`start.cmd` as add-ons. | implemented (243/243 tests green) |
| 2 | Review of round-1 implementation | **Decision 3/3:** session/reconnect path, M-F end-to-end hardening, status text/config add-ons | implemented (249/249 tests green) |
| 3 | Final review | **Decision 3/3:** make reconnect visible/connectable in UI, update device list after room change, close cancel race and wording | implemented (251/251 tests green), **APPROVED** |
| 4 | Extension (user request) | **Decision 3/3:** app versioning, UDP auto-discovery, house control and weekly program copying (low/medium) | implemented (268/268 tests green), hardware-free **APPROVED** |
| 5 | All features + Windows/Linux (user request) | **Autonomous, without model votes** (all calls failed at the free limit): 49 of 61 rows implemented, 1 partial, 1 replaced, 10 not mappable; Windows + Linux evidenced | implemented (442/442 tests green under Windows **and** Debian 13), self-test on both systems; Linux GUI not seen |

### Binding committee decision round 1 (24.11.2026)

All three members name **M-F, N7, and session monitoring** in their top 3; M-F is unanimously rank 1. DeepSeek puts session monitoring ahead of N7; Product/UX and Technology put N7 at rank 2. The committee decision therefore implements **all three as a package** and prioritizes within the package by dependency: M-F → N7 → session (N7 is the prerequisite for clean reconnect). No member put inclusion or i18n in the top 3; these remain deferred to rounds 2/3.

**Add-on items (unanimously uncontested, low effort):** filter device list to the selected room, success message after sending, `start.cmd` rebuilds after code changes, correct documentation inaccuracy "audit limited to 500".

### Round 3 – Technology/protocol vote (`opencode/nemotron-3.5-lightning-free`)

**APPROVED.** Confirmed: N3 (`Fault()` before `Fail()`) in code and in the new test, `RefreshInterval` removed, full 7-day decoding evidenced. Risks from this member's perspective: (1) reconnect after `SessionUnreachable` (HIGH), (2) monitor task race after `MarkUnreachableAsync` (MEDIUM), (3) inconsistent `_monitor` reset in the gate-timeout path (MEDIUM). Recommendation: max retry limit in the reconnect loop.

**Counter-check of the implementation (FACT):**
- The test state seen, 243/243, is outdated; currently **249/249** green, build 0/0.
- The reconnect test `An_unreachable_session_can_reconnect_without_an_explicit_disconnect` was **first run red in this session**: `InvalidOperationException: Already connected. Disconnect first.` at `CubeGateway.cs:74`. After the round-2 fix it is green (Infrastructure 10/10). The finding is therefore resolved and **evidenced**, not `UNKNOWN`.
- The endless retry is a **deliberate round-1 decision** (backoff 1…30 s): the app should go online again automatically when the cube returns. A max retry limit would prevent exactly that; not a defect.
- The monitor "race" is bounded by `StopMonitorAsync` with `CleanupTimeout` (500 ms) and exception-safe (`CubeGateway.cs:340-346`); `ConnectAsync` waits for it before restarting (`:81`). The reviewer himself concedes that the path is technically correct. No further fix needed.

### Round 3 – Binding decision (3/3)

Two votes say `APPROVED`, the independent review says `NOT APPROVED`. After counter-checking against the code, the reviewer's finding is classified as a **real, hardware-free blocker**: after automatic reconnect the UI stays "disconnected" because `OnConnectionChanged` only handles `Faulted`, while the service also reports `Connected` via `OnGatewayStateChanged` (`CubeApplicationService.cs:84-85`, `MainWindowViewModel.cs:417-427`). `IsConnected` therefore stays false, all action buttons are disabled, and a new connect runs into "Already connected" (`CubeGateway.cs:73-75`).

**Binding decision (to be implemented before final approval):**
1. Make `OnConnectionChanged` react to `Connected`: `IsConnected = true`, status "Connection restored" and no longer set `StatusText` to "Disconnected". Regression test through the real service with a fake gateway that fires `Connected`.
2. `OnSelectedRoomChanged` re-filters `Devices`; regression test for device change on room change.
3. Close cancel race (set status before cancel) and align stale wording in XAML with `StatusText`.

Deferred remains explicitly the max retry limit (deliberate round-1 decision: endless backoff 1…30 s), i18n, inclusion, and audit retention. Hardware remains `UNKNOWN`.

**Closing finding (FACT):** All three points are implemented. `OnConnectionChanged` now sets `IsConnected = true` and "Connection restored" on `Connected` (`MainWindowViewModel.cs:420-447`); `OnSelectedRoomChanged` calls `RefreshDevices` (`:539-553`); the cancel notice precedes the cancellation (`:378-390`) and the XAML wording is aligned (`MainWindow.axaml:139`). New regression tests: `Automatic_reconnect_is_visible_in_the_status` and `Selecting_another_room_refreshes_the_device_list` (`tests/MaxCube.UI.Tests/ViewModelTests.cs:200-234`). Build 0/0, **251/251** green (Domain 55, Application 21, Protocol 149, Infrastructure 10, UI 16). The final assessment is therefore **APPROVED** — for the hardware-free, local feature set. Hardware-near statements remain `UNKNOWN`.

### Round 3 – Review vote (`opencode/deepseek-v4.1-flash-free`)

**NOT APPROVED.** Re-measured: build 0/0, **249/249** tests green (Domain 55, Application 21, Protocol 149, Infrastructure 10, UI 14); documentation numbers match. Round-2 delta at gateway/domain/test level fully evidenced. New finding: auto-reconnect does not reach the UI (`IsConnected` stays false, buttons disabled, Connect → "Already connected"). Device list after room change wrong. Low: cancel race, wording discrepancy, `UnreachableTimeout` effectively unreachable via monitor loop (already documented in `gremium.md:120-121`). Recommendation: fix plus one regression test each, then approval without another architecture review.

### Round 3 – Product/UX vote (`opencode/tidepool-alpha-free`)

**APPROVED.** The round-2 add-ons are evidenced in code (`Cancel` restore `MainWindowViewModel.cs:358-386`, test `ViewModelTests.cs:177-198`; stale text `MainWindowViewModel.cs:402-415`; day lock `:151`). Three remaining points:

1. **Device list on room change** (INFERENCE): `OnSelectedRoomChanged` (`MainWindowViewModel.cs:515-525`) updates setpoint and weekly profile but not `Devices`; the list is filled only in `UpdateRooms` (`:475-480`).
2. **Cancel race** (INFERENCE): `StatusText = "Abbrechen …"` is set only after `_actionCts.Cancel()`/`CancelActiveOperation()` (`:383-385`). If the action finishes in between, the `finally` restore (`:359`) runs into emptiness and the text remains.
3. **Wording** (OBSERVATION): status "Connection lost – reconnect running …" vs. XAML "State stale – reconnect running …" (`MainWindow.axaml:139-140`).

**Counter-check (FACT):** All three points are traceable in the current code; they are small, hardware-free, and should be completed before final closure. i18n/inclusion remain deferred as decided.

### Round 2 – Product/UX vote (`opencode/tidepool-alpha-free`)

Key points: M-F/N7/session are largely implemented from a usability perspective. Top 3: (1) actively use `RefreshInterval`, (2) reset `StatusText` after cancellation (stays "Abbrechen …"), (3) make stale/reconnect visible in the status text as well.

**Counter-check of the implementation (FACT):**
- The status-text finding is justified: `Cancel()` sets "Abbrechen …" at `MainWindowViewModel.cs:371`; `RunAsync` does not restore it in `finally` (`:354-358`).
- The rationale for (1) is **not** correct: `CubeSessionMonitor.RunAsync` (`CubeSessionMonitor.cs:66-77`) calls `onWatchdog` on `Idle` (default 30 s), and `OnWatchdogAsync` refreshes (`CubeGateway.cs:265`). The refresh therefore does not happen only after 900 s. What is true: the newly introduced option `RefreshInterval` (`CubeConnectionOptions.cs:67-71`) is not evaluated anywhere — it is a misleading config and must either be wired or removed.

### Round 2 – Technology/protocol vote (`opencode/nemotron-3.5-lightning-free`)

Full report: `gremium-bericht-round2.md`. Key points: M-F/N7/session are technically implemented soundly; top 3 from this member's perspective: (1) explicitly wait for the old monitor task before a new connect, (2) make `Fault()`/`Fail()` atomic (N3), (3) make the monitor's `Unreachable` coupling explicit.

**Counter-check of the implementation (FACT):**
- The test state seen, 232/232, is outdated; currently **243/243** green (Domain 55, Application 20, Protocol 147, Infrastructure 9, UI 12).
- N3 is fixed: `CubeCommandQueue.cs:276-279` calls `Fault(failure)` first and only then `Fail(failure)`. The reviewer's concern is refuted by the code; a dedicated test for it is indeed still missing.
- The hint about a monitor window is **partly justified**: `DisconnectAsync` waits for `StopMonitorAsync` in the normal path (`CubeGateway.cs:189`), but in the gate-timeout path (`CubeGateway.cs:174-185`) `_queue`, `_transport`, and `_monitor` are not reset. A subsequent `ConnectAsync` fails there at `CubeGateway.cs:72-75` ("Already connected"). That is a real round-3 candidate.
- **Additional implementation check (FACT, not committee evidence):** The more relevant path is the live refresh: after three failed `l:` queries, `RefreshAsync` sets the state to `Faulted` and fires `SessionUnreachable` (`CubeGateway.cs:300-308`), but leaves `_queue`/`_transport` in place. The subsequent reconnect calls `CubeGateway.ConnectAsync`, which fails because of `CubeGateway.cs:72-75` ("Already connected. Disconnect first."). The reconnect loop (`CubeApplicationService.cs`, `ReconnectLoopAsync`) catches the error and retries with backoff — it never comes back online. This must be fixed in round 3 and evidenced with a test.

### Round 2 – Independent review vote (`opencode/deepseek-v4.1-flash-free`)

Key points: 243/243 green and 0/0 build re-measured; `README.md:8` still says 232/232 (documentation drift). Top 3: (1) close gateway session/reconnect path, (2) harden M-F end to end, (3) i18n.

**Counter-check of the implementation (FACT):**
- The strongest finding is test-evidenced: `CubeGatewayTests.An_unreachable_session_can_reconnect_without_an_explicit_disconnect` fails with `InvalidOperationException: Already connected. Disconnect first.` (`CubeGateway.cs:74`). After `SessionUnreachable`, `_queue` remains; the service's reconnect loop never comes back online.
- The monitor's `Unreachable` branch is dead code (`CubeSessionMonitor.cs:69-77` has no such case); detection depends solely on three failed `l:`.
- `HasWeekProfile` applies per room, not per day (`Room.cs:68`). A day discarded by the decoder (`WeekProfileDecoder.cs:78-85`) is simply absent from the profile; a sendable default day program would overwrite the real heating program.
- The audit retention acceptance criterion (`roadmap.md:177`) is still unmet; this remains a documented limitation, not a round-2 decision.

### Binding committee decision round 2 (24.11.2026)

All three votes put the **session/reconnect path** in first place (Product/UX: "reconnect in status text", Technology: "wait for monitor task", Review: "close the path"). The resulting committee decision, in this order:

1. **Close the session/reconnect path.** In the `Unreachable` branch and after three failed `l:` queries, release queue, transport, monitor, and monitor task with bounds; `ConnectAsync` waits for the old task before restarting. The gate-timeout path of `DisconnectAsync` also cleans up. A regression test with a real `CubeGateway` and `ScriptedTransport` is part of the decision.
2. **Harden M-F end to end.** Send lock on the concrete weekday, not only on the room: a day discarded by the decoder must not be sent. Additionally an assembler test with a complete 211-byte C: block and an N3 test (`Fault()` before `Fail()`).
3. **Add-ons from Product/UX and Review.** Restore `StatusText` after cancellation; remove `RefreshInterval` instead of wiring it (`FACT`: `Validate` enforces `IdleTimeout <= RefreshInterval`, so the idle watchdog already refreshes earlier — a second timer would only duplicate the same `l:`); make stale/reconnect visible in `StatusText`; correct the outdated test count in `README.md`.

**Deferred (explicitly not decided):** i18n received only one of three votes (Review, rank 3); Product/UX and Technology put other topics first. Inclusion remains rank 4. Correctness of the local connection and protection against overwriting the heating program take priority over localization. Audit retention remains as a documented limitation.

**Implementation status (24.11.2026, FACT):** Build 0/0, **249/249** tests green (Domain 55, Application 21, Protocol 149, Infrastructure 10, UI 14). The formerly red reconnect test is now green; new tests also cover the day lock, the complete C: chain, the N3 ordering finding, and `StatusText` after cancellation.

**Not a committee vote:** The still-running Sonnet response returned after the model change and does not count. Two of its pieces of evidence were verified independently and incorporated as `FACT` into the add-on items: device list without room filter (`MainWindowViewModel.cs:369-373`) and blocking `DisposeAsync().GetResult()` on exit (`App.axaml.cs:61`).
