> English translation of docs/ha-maxcube-erkenntnisse.md. Machine-created; DE/EN reviewed by the user on 27.11.2026; FR/IT/NL/PL feedback expected via issues/PRs.

# Findings from `ha-maxcube` for the C#/.NET 10 reimplementation

As of 24.11.2026. Purpose: to extract concrete protocol and domain rules from the live-proven project `ha-maxcube` and the static reverse engineering (REA) of the original JARs, to be adopted for the MAX! app in C#/.NET 10. **No** source code is copied; instead, rules are formulated. Every statement carries evidence in the form `File.py:line` or `File.java:line` (or a wiki reference marked as such). Anything not evidenced is explicitly `UNKNOWN`.

## 1. Methodology and evidence classes

The following classes are strictly separated; they must not be mixed:

- **LIVE-VERIFIED** – observed on the real cube or in the running Home Assistant instance. Evidence lies in `SecondBrain/wiki/…`, not in Python/Java source code.
- **CODE-VERIFIED** – directly readable from a `.py` or `.java` file with a line number.
- **INFERENCE** – derived from several CODE-VERIFIED facts; explicitly marked as a derivation and never presented as a fact.
- **UNKNOWN** – not evidenced despite searching. Must not be replaced by plausibility.

### 1.1 Legend for evidence paths

| Short name | Location |
| --- | --- |
| `schedule.py`, `climate.py`, `const.py`, `button.py`, `display.py`, `coordinator.py`, `cube.py` (HA) | `C:\Users\ebenh\OneDrive\Coding\ha-maxcube\custom_components\maxcube_local\` |
| `cube.py` (lib), `commander.py`, `device.py` (lib) | `C:\Users\ebenh\OneDrive\Coding\welock-bluetooth\.venv\Lib\site-packages\maxcube\` (maxcube-api 0.4.3) |
| `fake_cube.py`, `test_fake_cube.py` | `C:\Users\ebenh\OneDrive\Coding\ha-maxcube\tests\` |
| `WeekTemperatureProfile.java`, `DayTemperatureProfile.java`, `TemperatureProfileSwitchPoint.java`, `SetRoomWeekTemperatureProfile.java`, `FrameType.java`, `RadiogramSpecification.java`, `Constants.java`, `DayOfWeek.java`, `LocalConfiguration.java`, `ServerSettings.java` | `C:\Users\ebenh\OneDrive\Coding\app-maxcube\decompiled\…\sources\de\eq3\max\…` |
| `PortFactory.java`, `Application.java` | `C:\Users\ebenh\OneDrive\Coding\app-maxcube\decompiled\maxlocalapp\sources\de\eq3\max\MaxLocalAppStarter\` |
| `maxcube-protocol.md` | `C:\Users\ebenh\OneDrive\Coding\app-maxcube\docs\maxcube-protocol.md` (own static REA) |
| `max-cube-protokoll.md`, `ha-maxcube.md`, `max-local-application.md` | `C:\Users\ebenh\OneDrive\Coding\SecondBrain\wiki\…` |

Note on the evidence form: For REA files and wiki pages the line number is given with `:`, e.g. `maxcube-protocol.md:424` or `ha-maxcube.md:39`. Classification is via the labeling in the text (LIVE-VERIFIED / CODE-VERIFIED / INFERENCE).

## 2. Source situation

### 2.1 Live-verified sources (LIVE-VERIFIED)

- ha-maxcube v1.8.3 has been live-installed and `loaded` since 24.11.2026 ~07:39
  (`ha-maxcube.md:15`).
- The mode fix was checked live on 24.11.2026: 07:50:28 +01:00 `off` / 4.5 °C /
  `hvac_action=off`; 07:51:07 +01:00 after `HEAT` `heat` / 19.5 °C; 07:56:47 +01:00 again
  `off` / 4.5 °C (`ha-maxcube.md:39`).
- The two display buttons (0x82) were confirmed on a real wall thermostat on 22.11.2026
  (`ha-maxcube.md:27`, `max-cube-protokoll.md:25`).
- Discovery/reboot via UDP 23272 and the `eQ3MaxAp` response with a 10-digit serial number
  were observed live on 22.11.2026 (`max-cube-protokoll.md:28`).
- The web UI of the MAX! software ran at `http://localhost:63121/`
  (`max-local-application.md:16`, raw source `raw/sitzungen/2026-11-22-…:44`).
- The cube's TCP port 62910 was repeatedly the subject of live-observed blockages; ping and
  UDP continued to work (`max-cube-protokoll.md:21`, `ha-maxcube.md:76`).

### 2.2 Code-preferred sources (CODE-VERIFIED)

- Complete integration logic in Python (`schedule.py`, `climate.py`, `const.py`, `button.py`,
  `display.py`, `coordinator.py`, `cube.py`, `linkstatus.py`, `health.py`).
- Library `maxcube-api` 0.4.3 with `cube.py`, `commander.py`, `device.py`.
- Decompiled manufacturer JARs (MaxEssentialsBackend 1.4.6, MaxLocalBackend 1.4.6,
  MaxLocalAppStarter) as `FrameType.java`, `SetRoomWeekTemperatureProfile.java`,
  `LocalConfiguration.java`, `PortFactory.java`, `Application.java`, etc.
- Fake cube as a structurally invariant aid: `fake_cube.py:1-10` itself says it emulates the layout
  read by the library and `linkstatus.py` – **not** proof of real hardware behavior.

### 2.3 Explicitly absent

- A direct code reference for the concrete numeric value `63121` in ANY `.py`,
  `.java`, or configuration file: `UNKNOWN` (targeted search across the entire workspace
  with extensions `py,md,java,properties,json,yaml,txt,settings,cfg,conf,xml` found no hit;
  only a `firmware.enc` binary contains the digit sequence, which is not valid evidence).
- Evidence that the cube itself uses `63121`: `UNKNOWN`.
- Cause of the temporary TCP port 62910 blockage: `UNKNOWN` (`ha-maxcube.md:76`).

## 3. Conflict 1: Switching points per day

### 3.1 What the sources say

- `schedule.py:22` defines `CUBE_DAY_ORDER` starting with **Saturday**, then Sunday, Monday …
  Friday. This is the order of the weekly program blocks.
- `schedule.py:27-28` sets `BLOCK_BYTES = 26` and `SLOTS_PER_DAY = 13`: one day occupies 26 bytes,
  i.e. exactly 13 two-byte words.
- `schedule.py:55-63` reads 13 words per block and decodes
  `minutes = (word & 0x1FF) * 5` (`schedule.py:62`) and `temp = (word >> 9) / 2`
  (`schedule.py:63`). The lower 9 bits are the time in 5-minute steps, the upper 7 bits
  are the temperature in half degrees (`schedule.py:3-7`).
- `schedule.py:89-91` explicitly formulates the write rule: **one radio telegram** carries
  at most 7 target points; when read out, the cube stores up to 13.
- `schedule.py:107-118` pads short days with copies of the last slot so that **exactly 7**
  words are sent; `schedule.py:116` encodes the day as an index in `CUBE_DAY_ORDER`
  (Saturday = 0).
- `schedule.py:153-155` requires ascending times, a 5-minute grid, last slot `24:00`, and
  temperatures from 4.5 to 30.5 °C. `tests/test_schedule.py:214-219` confirms `24:00` with 4.5
  and 30.5 °C as valid boundaries.
- Library side: `CMD_SET_PROG = "10"` (`cube.py:27`), `DAYS` starting at Saturday (`cube.py:33`),
  padding to 7 (`cube.py:328-329`) and `get_programme` with `int(int(word[:7], 2) / 2)`
  (`cube.py:387`), i.e. loss of half degrees on read-out.
- Java: `DayTemperatureProfile.java:17-18` allows `@Size(min = 1, max = 13)` switching points per
  day. `WeekTemperatureProfile.java:14` requires exactly 7 day profiles
  (`Constants.COOKIE_AGE_DAYS`). `DayOfWeek.java:7-13` numbers Monday=1 … Sunday=7;
  `RadiogramSpecification.java:281-285` yields `(value + 1) % 7` as the cube index –
  Saturday thus becomes 0, Sunday 1. `DayOfWeek.java:26-27,33-34` only concerns `Calendar` conversion.
- Java write rule: `SetRoomWeekTemperatureProfile.java:81` creates a first telegram per changed day with
  `createDayPartSpec(day, 0, 7)`; `:82-84` creates a **second** telegram with `createDayPartSpec(day, 1, 6)` **only** if `getSwitchPointsNormalized().size() > 7`.
  `createDayPartSpec` (`:89-100`) sets the weekday in byte 10 bits 0..2 (3 bits), bit 4 the
  telegram number 1 (`:90`), the offset `7 * telegram` (`:91`), and fills missing points with
  the last known point (`:96-97`).

### 3.2 Resolution

**DECIDED (CODE-VERIFIED):** The cube stores **13** switching points per day
(`schedule.py:28`, `DayTemperatureProfile.java:17-18`). A **write telegram** contains
**7** points (`schedule.py:89-91`, `SetRoomWeekTemperatureProfile.java:81`). Python writes
only this one telegram (`schedule.py:97-118,159-160`). With more than 7 points,
according to manufacturer Java a **second telegram with 6 further points** follows
(`SetRoomWeekTemperatureProfile.java:82-84`) – not "6 per day". The second telegram is
evidenced only in Java; no live evidence from the cube exists.

The wiki formulation "at most 7 sections per day and write telegram"
(`max-cube-protokoll.md:64`) is therefore to be read **per telegram**. The REA statement
"with >7 points a second telegram with 6 points" (`maxcube-protocol.md:424`) is correct;
the older inference "6 switching points per day" was wrong.

**INFERENCE:** The C# implementation should hold 13 slots per day internally, but when writing
split 7 + possibly 6 into two telegrams and pad unused slots with the last slot.
This derivation follows from `schedule.py:28,89-91,107` and
`SetRoomWeekTemperatureProfile.java:81-97`. The second part (7+6) is Java-only; Python
supports at most 7 points per day and must not be cited as evidence for it.

## 4. Conflict 2: Port 63121 versus 62910/54321

### 4.1 Three clearly separated port concepts

1. **Cube TCP 62910** – CODE-VERIFIED: `const.py:7` (`DEFAULT_PORT = 62910`), library
   `cube.py:32` (`DEFAULT_PORT = 62910`), manufacturer Java `LocalConfiguration.java:12`
   (`DEFAULT_CUBE_PORT = 62910`). This is the port the C# app must use for `H:/M:/C:/L:` and
   `q:/l:/s:`.
2. **Virtual cube 54321** – CODE-VERIFIED: `max.local.properties:22`
   (`max.connection.virtualCubePort=54321`). `LocalConfiguration.java:81-89` returns this
   port only if the IP is `127.0.0.1` or the host's own IP; otherwise 62910.
   For the C# app this is **not** a target port, but a manufacturer test/simulation path.
3. **Web UI of the MAX! software 63121** – LIVE-VERIFIED: `max-local-application.md:16` names
   `http://localhost:63121/`. The **mechanism** is CODE-VERIFIED: `ServerSettings.java:6-14`
   stores a `ServerPort` in `max.settings`; `PortFactory.java:23-29` selects a random free port between 4711 and 65535 if needed; `Application.java:136-157` fetches it
   when the stored port is missing or occupied; `Application.java:160-162` saves it;
   `Application.java:190-192` starts Jetty with exactly this value; `Application.java:220`
   opens the browser at that address.

### 4.2 Resolution

**DECIDED (LIVE-VERIFIED + CODE-VERIFIED):** 63121 is the **local HTTP port of the
manufacturer's web UI**, not the cube protocol port. The value itself is only
observed live (`max-local-application.md:16`); that the port is assigned dynamically and stored in
`max.settings` is CODE-VERIFIED (`PortFactory.java:23-29`,
`Application.java:136-162`, `ServerSettings.java:6-18`). Why precisely 63121 was chosen
is `UNKNOWN` (no code hit, no config file with this value).

The wiki line `max-cube-protokoll.md:65` ("63121 = random web UI port …") is thus
essentially confirmed, but the concrete numeric value remains live-based.

**Consequence for C#:** No HTTP server, no Jetty, no browser plugin. The
target architecture provides a native Avalonia UI (`target-architecture.md:13`). The
ports 63121 and 54321 do not belong in the C# connection configuration.

### 4.3 Other ports (do not confuse)

- UDP 23272 = discovery **and** reboot (`discovery.py:17-20`, `button.py:3-8,36-41`).
  CODE-VERIFIED and LIVE-VERIFIED (`max-cube-protokoll.md:28`).
- `maxcube-protocol.md:537` describes a `GetUrl` response with a port from `data[0]<<8|data[1]`;
  that concerns proxy URL retrieval, not the cube TCP port.

## 5. Conflict 3: Mode and temperature encoding

### 5.1 The numbers

- `climate.py:71-74`: `ON_TEMPERATURE = 30.5`, `OFF_TEMPERATURE = 4.5`,
  `MIN_TEMPERATURE = 5.0`, `MAX_TEMPERATURE = 30.0`. So 4.5/30.5 are boundary values in the
  radio protocol, 5.0/30.0 the UI boundaries.
- `schedule.py:92-94` confirms `MIN_TEMPERATURE = 4.5` ("off") and
  `MAX_TEMPERATURE = 30.5` ("on"), 5-minute grid.
- Library `device.py:10-13`: modes `AUTOMATIC=0`, `MANUAL=1`, `VACATION=2`, `BOOST=3`.
- Library `cube.py:284-309`: `set_temperature_mode(thermostat, temperature, mode)`;
  `target_temperature = int(temperature * 2) + (mode << 6)` (`cube.py:307`); from this the
  frame `000440000000` + RF address + room + byte (`cube.py:309`). The lower 6 bits carry
  the temperature in half degrees, the upper 2 bits the mode.
- `cube.py:279,282` show the trap: `set_target_temperature` and `set_mode` do delegate to
  `set_temperature_mode`, but with `None` for the respective other parameter – the old mode
  is thereby preserved.

### 5.2 The live correction v1.8.3

- LIVE-VERIFIED: Before the fix, `Off` only set 4.5 °C and kept `AUTO`; the entity showed
  `auto` instead of `off`, and the target value was overwritten by the weekly program
  (`ha-maxcube.md:34`). Diagnosis: `state=auto`, `temperature=4,5`,
  `scheduled_temperature=17`, `valve_position=0` (`ha-maxcube.md:34`).
- CODE-VERIFIED: `climate.py:315-322` delivers `off` **only** for `MANUAL` **and** 4.5 °C;
  otherwise `HEAT` for `MANUAL`, `AUTO` in all other cases.
- CODE-VERIFIED: `climate.py:318-321` and `:358-367`: `OFF` → 4.5 °C; `HEAT` → last real
  target value (clamped to `min_temp..max_temp`); `AUTO` → `set_mode(AUTOMATIC)`.
- CODE-VERIFIED: `climate.py:170-180` remembers the last heating target only for `MANUAL`,
  not 4.5/30.5 °C, and only within the UI boundaries. `climate.py:182-193` falls back to
  program target → comfort target → `min_temp`.
- CODE-VERIFIED: `climate.py:389-407` – **all** target-value paths call
  `cube.set_temperature_mode(device, temperature, MAX_DEVICE_MODE_MANUAL)`; afterwards the
  real value is remembered and a refresh is requested.
- CODE-VERIFIED: `CHANGELOG.md:13-15` describes exactly the same fix and the dashboard toggle;
  `ha-maxcube.md:30` links it to PR #41 / tag `v1.8.3`.
- Tests as support: `tests_ha/test_integration.py:615-619` requires that a target value
  leaves `AUTO` and sends `MANUAL` along with it; `:621-630` checks `OFF` from `AUTO`.
  `fake_cube.py:24-27` models the four modes.

### 5.3 Resolution

**DECIDED (LIVE-VERIFIED + CODE-VERIFIED):** A target value must never be sent without a mode. `OFF` is not "a low target value", but `MANUAL + 4.5 °C`. `HEAT` is
`MANUAL + last real target value`. `AUTO` is mode 0 without its own target value.
30.5 °C is the "on" boundary value and, like 4.5 °C, is never remembered as a heating target
(`climate.py:177,403`).

Open: Whether the cube encodes modes internally identically (Java `RoomControlMode` vs. Python
`device.py:10-13`) is **INFERENCE** from `cube.py:307` and `FrameType.java:21-25` and was not
evidenced by a direct Java bit field. For the C# implementation the Python encoding suffices,
because it is live-verified.

## 6. Conflict 4: Radiogram bit fields

### 6.1 Frame structure (CODE-VERIFIED)

- `maxcube-protocol.md:339-340`: `addByte(2, 0, frameTypeCode)` → FrameType in **byte 2**;
  `addDeviceAddress(6, 0, receiverAddr)` → receiver address in **bytes 6–8** (24 bits).
- `maxcube-protocol.md:351-354`: `RadiogramSpecification.createPayload()` writes bits
  **LSB-first** within a byte (`bytes[i/8] |= (1 << (i%8))`); integers via
  `ByteUtil.fromInt(value, bitLength)`; byte order big-endian in the
  `reorderInt(..., LITTLE_ENDIAN)` path (`maxcube-protocol.md:658`).
- Directly evidenced in Java: `FrameType.java:8-34` lists the codes, `FrameType.java:40-43`
  the registration, `FrameType.java:46-50` the lookup.
- `display.py:25-30` encodes the display telegram as
  `00 00 82 000000 <RF 3 bytes> 00 <04|00>`: byte 2 = 0x82, byte 10 = 0x04 (actual) or 0x00
  (target). This matches the Java side `SwitchDisplaySetPointActual((byte) -126, …)`
  (`FrameType.java:31`, -126 = 0x82 as a signed byte).

### 6.2 FrameType table (CODE-VERIFIED, `FrameType.java:9-34`)

| FrameType | Code | Category | Evidence |
| --- | --- | --- | --- |
| SystemInformation | 0x00 | None | `FrameType.java:9` |
| Inclusion | 0x01 | None | `FrameType.java:10` |
| Answer | 0x02 | None | `FrameType.java:11` |
| TimeInformation | 0x03 | None | `FrameType.java:12` |
| ConfigWeekTemperatureProfile | 0x10 | Configuration | `FrameType.java:13` |
| ConfigTemperatures | 0x11 | Configuration | `FrameType.java:14` |
| ConfigValveFunctions | 0x12 | Configuration | `FrameType.java:15` |
| AddLinkPartner | 0x20 | Assignment | `FrameType.java:16` |
| RemoveLinkPartner | 0x21 | Assignment | `FrameType.java:17` |
| SetGroupAddress | 0x22 | Assignment | `FrameType.java:18` |
| RemoveGroupAddress | 0x23 | Assignment | `FrameType.java:19` |
| ShutterConactState | 0x30 | None | `FrameType.java:20` |
| ControlMode | 0x40 | Control | `FrameType.java:21` |
| SetPointTemperature | 0x41 | Control | `FrameType.java:22` |
| SetPointAndCurrentTemperature | 0x42 | None | `FrameType.java:23` |
| ComfortTemperature | 0x43 | Control | `FrameType.java:24` |
| EcoTemperature | 0x44 | Control | `FrameType.java:25` |
| CurrentTemperatureAndHumidity | 0x45 | None | `FrameType.java:26` |
| PushButtonState | 0x50 | None | `FrameType.java:27` |
| HeatungThermostatState | 0x60 | None | `FrameType.java:28` |
| LockManualControls | 0x80 | Control | `FrameType.java:29` |
| DaylightSavingTimeMode | 0x81 | None | `FrameType.java:30` |
| SwitchDisplaySetPointActual | 0x82 | Configuration | `FrameType.java:31` |
| Reset | 0xF0 | Remove | `FrameType.java:32` |
| WakeUp | 0xF1 | None | `FrameType.java:33` |
| Test | 0xFF | None | `FrameType.java:34` |

The typos `ShutterConactState` and `HeatungThermostatState` stand like this in the original
(`FrameType.java:20,28`; cf. `maxcube-protocol.md:682`) and were deliberately adopted.

### 6.3 Weekly profile in the radiogram (CODE-VERIFIED)

- `SetRoomWeekTemperatureProfile.java:90`: header with `FrameType 0x10`, weekday in
  byte 10 bits 0..2 (3 bits), telegram-1 flag at byte 10 bit 4.
- `SetRoomWeekTemperatureProfile.java:98`: per point `addTime5MinResolution(12 + 2*count, 0, …)`
  and `addTemperature(11 + 2*count, 1, …)` – time and temperature lie in **adjacent
  bit fields** (byte 11 from bit 1 = temperature, byte 12 from bit 0 = time; the 9th time bit as
  overflow into byte 11 bit 0).
- `RadiogramSpecification.java:193-203` (`addTime30MinResolution`) evidences the pattern for
  30-minute fields; the 5-minute counterpart is used in `SetRoomWeekTemperatureProfile.java:98`.
- The Python side, by contrast, encodes compact 16-bit words with 7 bits temperature + 9 bits time
  (`schedule.py:62-63,108-110`). **CORRECTION 24.11.2026 (CODE-VERIFIED):** Both descriptions
  are two views of the same 2-byte pair. The direct byte-for-byte comparison is derivable from the
  code and positive; full derivation in `byte-abgleich-python-java.md`.
  The 6 Java temperature bits and the 9th time bit yield exactly the Python word.
- `SetRoomWeekTemperatureProfile.java:103-115` sends only **changed** days unless
  `overwrite` is set. For C# this is an optional optimization, not a requirement.

### 6.4 Status bit fields of the L-message (LIVE-VERIFIED + CODE-VERIFIED)

- `linkstatus.py:31-40` describes the layout per submessage: byte +0 length (counted from offset 1),
  +1..+3 RF address, +4 unknown, +5 `bits1` (bit 4 set, bit 3 `error`,
  bit 2, bit 1 `initialized`), +6 `bits2` (bit 7 battery empty, bit 6 link error, bit 5
  panel locked, bit 4 gateway known, bit 3 DST, bits 1–0 mode). For 6-byte submessages
  (window contacts) byte +6 is missing (`linkstatus.py:40`).
- LIVE-VERIFIED: `Link_Error` (bits2 bit 6) remained 0 on all 16 devices, even on a
  clearly dead thermostat; `Error` (bits1 bit 3) was set exactly on this one device
  (`linkstatus.py:9-17`). `Initialized` was clear on four window contacts that nevertheless
  worked correctly in pairs (`linkstatus.py:18-21`).
- `linkstatus.py:26-29` derives the rule: no single bit flag is authoritative;
  instead provide raw bits **and** a "stale" detection based on last reception.
- `linkstatus.py:65-74` defines `BIT_INITIALIZED = 1 << 1`, `BIT_ERROR = 1 << 3`,
  `BIT_LINK_ERROR = 1 << 6`, minimum length 6.
- `cube.py:237-243` (lib) reads `bits1, bits2` from `data[pos+5:pos+7]` and the target temperature
  from `data[pos+8] & 0x7F`; `fake_cube.py:34-38` mirrors bits 0x10, 0x02, 0x08, 0x40, 0x80.

**DECIDED:** For C#, use `Error` (bits1 bit 3) as the primary "device not responding" signal,
do not use `Link_Error` as a reachability criterion, and additionally use a time-based
`stale`. It remains `UNKNOWN` whether other cube firmware versions do set `Link_Error`.

## 7. Further protocol and domain rules

### 7.1 Connection and polling

- The cube accepts only **one** connection at a time; `coordinator.py:3-6` therefore opens
  a new connection per poll and closes it afterwards. Every poll begins with a fresh
  `H:` line with duty cycle and free memory slots (`coordinator.py:4-6`).
- `commander.py:31-40` sends `q:` and then always closes (`finally`).
- `commander.py:47-60`: `update()` queries `l:`, reconnects on error, and disconnects with
  `use_persistent_connection = False` after every call.
- `cube.py` (HA):1-7 retains the additional `H:` fields discarded by the library
  (only serial, RF address, firmware are read there). `_DUTY_CYCLE_FIELD = 5`
  (`cube.py` (HA):25).
- `cube.py` (lib):129-130 swallows parse errors – C# should instead log them explicitly.
- `cube.py` (lib):165-223 creates devices only from the `M:` line. LIVE-VERIFIED: radio-known
  devices without an M entry do not appear as entities (`ha-maxcube.md:67`).
- `cube.py` (lib):241-259 discards the actual temperature in boost/vacation even though it is in the frame
  (`max-cube-protokoll.md:34`). `max-cube-protokoll.md:63` remains the open measurement for it.

### 7.2 Radio budget (duty cycle)

- `const.py:20-24`: `MAX_DUTY_CYCLE_FOR_WRITING = 90` percent, `SCHEDULE_VERIFY_DELAY = 300`
  seconds.
- `schedule.py:180-182` calculates approx. 2.8% radio effort per telegram.
- `schedule.py:236-246` rejects a write if the predicted duty ≥ 90%.
- `commander.py:62-70` sends `s:` with Base64 of the hex payload and repeats until timeout.
- `commander.py:72-86` evaluates the `S:` response `duty,status,free`: `status_code == "0"`
  means success (`:76-77`); with `duty == 100` and `free == 0` it waits (`:82-83`).
- `health.py:20-25`: `DUTY_HIGH = 90`, `DUTY_LOW = 80`, `DUTY_SUSTAIN = 600` s (hysteresis),
  `UNREACHABLE_AFTER = 6 * 3600` s, `BATTERY_HOLD = 15 * 60` s (`health.py:14-17`).
- LIVE-VERIFIED: 99% radio utilization led to the `radio_busy_cube` notice on 24.11.
  (`ha-maxcube.md:39`); on 22.11. 6% and 49 free slots were measured (`ha-maxcube.md:62`).

### 7.3 Discovery and reboot (UDP 23272)

- `discovery.py:17-24`: port 23272, request `eQ3Max*\0**********I`, response prefix
  `eQ3MaxAp`, serial number `slice(8,18)`, format `[A-Z0-9]{10}`.
- `button.py:36-42`: same port, prefix `eQ3Max*\0`, opcode `R`, target
  `255.255.255.255:23272` (`button.py:8`).
- LIVE-VERIFIED: `R` means **reboot**, not factory reset; factory reset is the TCP command
  `a:` (`max-cube-protokoll.md:28`). The button was not pressed on 22.11.
  (`max-cube-protokoll.md:28`).

### 7.4 Display on the wall thermostat (0x82)

- `display.py:4-10` documents the origin (Bouni S-Message.md, command 82; official
  software 1.4.6) and the important limitation: the current state **cannot**
  be read back; hence two buttons instead of one switch.
- `display.py:16-19`: `FRAME_TYPE_DISPLAY = "82"`, `04` = actual temperature, `00` = target temperature.
- `display.py:22-30`: exact hex layout (see 6.1).
- `button.py:63-76`: buttons only for wall thermostats (`show_room_temperature=True`,
  `show_setpoint=False`), not for radiator thermostats.
- `button.py:130-150` → `coordinator.async_set_display(rf, show_actual)`, with a prior
  radio budget check.
- Java counterpart: `SetDeviceDisplaySetPointActual.java:12-19` and
  `WallMountedThermostatDisplayConfigCommand.java:13-23` (device type wall thermostat,
  RF address, `displayActual`).
- LIVE-VERIFIED: both buttons confirmed on the device (`ha-maxcube.md:27`,
  `max-cube-protokoll.md:25`).

### 7.5 Snapshots, health, diagnostics (architecture rules)

- `snapshot.py:1-7`: immutable snapshot at the end of every poll, because library objects
  are modified in the executor thread. This is a C#-relevant rule: never let the UI point directly at
  mutable driver objects.
- `snapshot.py:26-28`: weekly program as a tuple `(until "HH:MM", temperature)` per day.
- `coordinator.py:1-7`: all synchronous library calls into the executor; one
  dedicated worker serializing the single cube connection.
- `health.py:9-12`: issue types `battery_low`, `device_error`, `cube_unreachable`, `radio_busy`.
- `diagnostics.py:14-16`: redaction of host, serial number, names, rooms, title, unique_id.
  C# should redact equally sensitive data.
- `config_flow.py:10-28`: host/port from configuration, DHCP and UDP discovery, serial number
  as stable identity (`identity.py` via `config_flow.py:28`).

## 8. Compact radio/protocol reference

| Level | Rule | Evidence |
| --- | --- | --- |
| Transport | TCP 62910, line-oriented | `const.py:7`, `cube.py` (lib):32 |
| Greeting | `H:` with serial, RF, firmware, duty, slots, date | `cube.py` (HA):1-7, `:25` |
| State | `M:` rooms/devices, `C:` configuration, `L:` status | `fake_cube.py:3-6`, `cube.py` (lib):165-223 |
| Writing | `s:` + Base64(hex), response `S:` = `duty,status,free` | `commander.py:62-86` |
| Ending | `q:` then close | `commander.py:31-40` |
| Frame | FrameType byte 2, receiver bytes 6–8, LSB-first | `maxcube-protocol.md:339-354` |
| Week | 13 slots/day, Python 7 per telegram; 7 + 6 Java-only, sent live on 24.11. | `schedule.py:28,89-91` (7 limit), `SetRoomWeekTemperatureProfile.java:81-84` |
| Temperature | Half degrees, 4.5/30.5 boundaries | `schedule.py:62-63,92-93`, `cube.py` (lib):307 |
| Mode | 0 auto, 1 manual, 2 vacation, 3 boost | `device.py:10-13` |
| Display | 0x82, byte 10 = 04/00, wall thermostat only | `display.py:16-30`, `FrameType.java:31` |
| UDP | 23272, `I` discover, `R` reboot | `discovery.py:17-24`, `button.py:36-41` |

## 9. Open points / UNKNOWN

1. `UNKNOWN`: Origin of the concrete web UI port 63121 in code; only observed live
   (`max-local-application.md:16`). Dynamic mechanism evidenced (`PortFactory.java:23-29`).
2. `UNKNOWN`: Why the cube temporarily stops accepting the TCP listener 62910
   (`ha-maxcube.md:76`, `max-cube-protokoll.md:21`).
3. ~~`UNKNOWN`: Direct byte-for-byte comparison between Python word encoding
   (`schedule.py:108-110`) and Java bit-field encoding
   (`SetRoomWeekTemperatureProfile.java:98`)~~ **Done 24.11.2026 (CODE-VERIFIED):** The
   encoding is byte-identical; derivation and Sonnet cross-check in
   `byte-abgleich-python-java.md`. **LIVE-VERIFIED 24.11.2026:** Encoding and transmission of a real
   `0x10` telegram from the Python integration were captured via the library's DEBUG log
   (25 bytes, 7 words, byte 10 bit 4 = 0; evidence in the SecondBrain wiki at
   `wiki/anleitungen/maxcube-0x10-mitschnitt.md`). Acceptance by the cube is only indirectly
   evidenced (`sent_not_confirmed`, no `A:` acknowledgement). A packet capture remains open; the
   transmission of a 13-point program by the original app is live-evidenced on 24.11.2026 at 10:32 +01:00
   (two telegrams, byte 10 `0x01` with 7 words, then `0x11` with 6 words from
   offset 7). Encoding and transmission of the Java rule are thereby `LIVE-VERIFIED`, acceptance by the cube still only indirect.
4. `UNKNOWN`: Actual temperature in boost mode was never measured on a real device
   (`max-cube-protokoll.md:63`, `cube.py` (lib):241-259).
5. `UNKNOWN`: Whether the Java category `FrameTypeCategory` maps exactly to the Python RadioState
   mapping; `maxcube-protocol.md:390` names the rule, a Python counterpart is missing.
6. `UNKNOWN`: Behavior with more than 13 switching points per day; Java limits to 13
   (`DayTemperatureProfile.java:17-18`), no test case beyond that exists.
7. `UNKNOWN`: Whether the cube adopts the telegram-1 flag (`SetRoomWeekTemperatureProfile.java:90`)
   correctly at the domain level; the Python side sends only 7 words and no flag
   (`schedule.py:112-118`). **INFERENCE:** With ≤7 points the second telegram is not needed,
   so padding in `schedule.py` is compatible with the Java rule. The live telegram
   capture from 24.11.2026 shows exactly this one telegram without a flag and thereby confirms the
   Python side. Live transmission with flag 1 by the original app is `LIVE-VERIFIED`
   (24.11.2026, 10:32 +01:00; evidence in the SecondBrain wiki at
   `wiki/anleitungen/maxcube-0x10-mitschnitt.md`); whether the cube adopts it correctly remains `UNKNOWN`.

## 10. Adoption list for the C# implementation

### 10.1 Domain rules from Python logic (LIVE-VERIFIED + CODE-VERIFIED)

1. **Connection** open anew per poll, read `H:`, send `q:`, close; never two
   clients at once (`coordinator.py:3-6`, `commander.py:31-40`).
2. **Port** 62910 as default; do not use 54321 or 63121 (`const.py:7`,
   `cube.py` (lib):32, `LocalConfiguration.java:12`, `max-local-application.md:16`).
3. **Always send mode + temperature together**; `OFF = MANUAL + 4.5 °C`, `HEAT = MANUAL +
   last real target value`, `AUTO = mode 0` (`climate.py:315-322,358-367,389-401`,
   `ha-maxcube.md:34-39`).
4. **Half degrees** internally as `int(temperature * 2)`; upper 2 bits mode, lower 6 bits temperature
   (`cube.py` (lib):307, `schedule.py:62-63`).
5. **Boundaries** 4.5/30.5 °C in the protocol, 5.0/30.0 °C in the UI; never remember 4.5 or 30.5 as
   a heating target (`climate.py:71-74,177,403`).
6. **Weekly program** 13 slots per day, starting Saturday, 5-minute grid, last slot 24:00,
   temperature 4.5–30.5 °C (`schedule.py:22,27-28,62-63,153-155`).
7. **Writing** at most 7 points per telegram, pad short days with the last slot,
   day as index starting at Saturday (`schedule.py:89-91,107-118`).
8. **Radio budget** check before every write (90% limit), ~2.8% per telegram, `S:` status `"0"`
   as success, wait at 100%/0 slots (`const.py:21,24`, `schedule.py:180-182,236-246`,
   `commander.py:72-86`).
9. **Write verification** after 300 s (`const.py:24`).
10. **Status** `Error` (bits1 bit 3) as main indicator, ignore `Link_Error`, additionally
    stale after 6 h (`linkstatus.py:9-29,65-74`, `health.py:20`).
11. **Battery** hold for 15 minutes, radio notice with 90/80% hysteresis over 600 s
    (`health.py:14-25`).
12. **Discovery/reboot** UDP 23272, `I` or `R`, serial number `[A-Z0-9]{10}`
    (`discovery.py:17-24`, `button.py:36-41`).
13. **Display** 0x82 only for wall thermostats, `04` actual / `00` target, state not readable back
    (`display.py:4-10,16-30`, `button.py:63-76`).
14. **Snapshot** immutable per poll for the UI; serialize driver access
    (`snapshot.py:1-7`, `coordinator.py:1-7`).
15. **Diagnostics** redact serial number/host/names (`diagnostics.py:14-16`).

### 10.2 Domain rules from Java REA (CODE-VERIFIED)

16. Adopt FrameType codes and categories from `FrameType.java:9-34` (table in 6.2).
17. Bit order LSB-first within the byte, big-endian between bytes
    (`maxcube-protocol.md:351-354,658`, `RadiogramSpecification.java:193-203`).
18. Weekly profile: FrameType 0x10, byte 10 bits 0..2 (3 bits) weekday, bit 4 telegram-1 flag,
    points from byte 11/12; >7 points → second telegram with 6 points
    (`SetRoomWeekTemperatureProfile.java:90-100,81-84`; sent live 24.11.2026 10:32 +01:00).
19. Send only changed days (optional); validate 7 day profiles and at most 13 points per day
    (`SetRoomWeekTemperatureProfile.java:103-115`,
    `WeekTemperatureProfile.java:14`, `DayTemperatureProfile.java:17-18`).
20. Saturday = index 0, Sunday = 1; Monday = 1 … Sunday = 7 in the Java enum
    (`RadiogramSpecification.java:281-285`, `DayOfWeek.java:7-13`, `schedule.py:22`).
21. Do not rebuild the web UI (no Jetty/HTTP), but native UI; the manufacturer uses a dynamic
    port + browser (`Application.java:190-220`, `target-architecture.md:13`).

### 10.3 Open points for the C# work

22. ~~Complete byte-for-byte comparison Python ↔ Java weekly profile (see 9.3).~~
    **Done 24.11.2026:** CODE-VERIFIED, see `byte-abgleich-python-java.md`.
23. Measure boost actual temperature on a real cube before adopting `cube.py` (lib):241-259 unchecked.
24. Observe port blockage 62910 and make the C# client more robust with timeout/reconnect
    (`ha-maxcube.md:76`, `coordinator.py:47-60`).
25. Do not adopt any value from `fake_cube.py` as a hardware fact (`fake_cube.py:8-10`).
26. The four conflicts are thereby decided for implementation; remaining
    `UNKNOWN` points may only be converted into facts after new evidence.
