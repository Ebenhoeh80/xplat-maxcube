> English translation of docs/protokoll-referenzen.md. Machine-created; DE/EN reviewed by the user on 27.11.2026; FR/IT/NL/PL feedback expected via issues/PRs.

# Internet references: eQ-3 MAX! Cube LAN Gateway – protocol

> Purpose: sources and findings for quickly and safely completing the
> C#/.NET 10/Avalonia reimplementation in `app-maxcube`.
>
> Created: 2026-11-24. All statements are marked `EVIDENCED` (source read directly)
> or `UNKNOWN` (not verified). Links were taken only from sources actually found
> and read.

---

## 1. Executive summary / decision aid

| Question | Answer |
|---|---|
| Is there an official, authorized protocol specification from eQ-3? | `EVIDENCED`: None found. Only a datasheet and a user manual. |
| Is there a C# library? | `EVIDENCED`: Yes, but outdated: `bietiekay/MAXSharp` (2010–2012, AGPL v3, last updated 2015). No current NuGet package. |
| Best, most up-to-date reference? | `EVIDENCED`: Bouni `max-cube-protocol` (MIT, continuously maintained) + Python `maxcube-api` 0.4.3 (2021) as reference implementation. |
| Recommended strategy | Own C# protocol module, Bouni as spec, Python port as behavioral oracle, Java REA as fallback/cross-check. No third-party C# library as foundation. |

---

## 2. Source matrix

| No. | Source (URL) | License | Last known activity | Reliability |
|---|---|---|---|---|
| Q1 | https://github.com/Bouni/max-cube-protocol | MIT (`EVIDENCED`, license file in repo; previously also identified as MIT) | 137 commits, created 2013-12-10; last updated 2025-02-13 according to GitHub search (`EVIDENCED`); ~120 stars, ~35 forks (`EVIDENCED`) | Reverse engineering, very detailed, mostly well evidenced |
| Q2 | https://www.eq-3.com/downloads/download/max/pdb/en/MAX_Cube-LAN-Gateway_99004_datasheet_V3.0.pdf | Manufacturer PDF (usage rights not checked in the document → `UNKNOWN`) | Version V3.0 (`EVIDENCED`) | official, but only a product sheet |
| Q3 | https://www.eq-3.com/Downloads/eq3/downloads_produktkatalog/max/bda/en/BC-LGW-O-TW_UM_EN.pdf | Manufacturer PDF (`UNKNOWN`) | published 2017-05-17 (`EVIDENCED`) | official, operation, no protocol |
| Q4 | https://pypi.org/project/maxcube-api/ | PyPI metadata unclear: Libraries.io says `OpenSSL`, GitHub metadata MIT (`EVIDENCED`); exact LICENSE file not read → `UNKNOWN` | Version 0.4.3, 2021-05-02 (`EVIDENCED`) | community reference implementation, very useful |
| Q5 | https://github.com/uebelack/python-maxcube-api | MIT per GitHub metadata (`EVIDENCED`) | Repo metadata read; release 0.4.3 (2021-05-02) (`EVIDENCED`) | community, code read |
| Q6 | https://github.com/bietiekay/MAXSharp | GNU AGPL v3, `license.txt` read (`EVIDENCED`) | Copyright 2010–2012; GitHub last updated 2015-06-24 (`EVIDENCED`); ~11 stars (`EVIDENCED`) | community, C#, heavily outdated |
| Q7 | https://github.com/bietiekay/hacs/tree/master/tools/MAXDebug | `UNKNOWN` (not checked) | `UNKNOWN`; only listed in Q1 | community hint, not read |
| Q8 | https://github.com/ababilone/maxmanager | `UNKNOWN` (not checked) | Repo metadata read, date `UNKNOWN` (repository 2016, listed by Q1) | community hint, CS/UWP, not read in detail |
| Q9 | https://www.home-assistant.io/integrations/maxcube | Apache-2.0 typical for HA, not checked here → `UNKNOWN` | Legacy/community-maintained (`EVIDENCED`), documentation state read in 2026 | community, well documented |
| Q10 | https://github.com/home-assistant/core/blob/dev/homeassistant/components/maxcube/climate.py | Apache-2.0 (HA core, `UNKNOWN` whether checked individually) | `dev` branch, read in 2026 (`EVIDENCED`) | community reference implementation |
| Q11 | http://www.openhab.org/addons/bindings/max | `UNKNOWN` | Addon documentation read, date `UNKNOWN` | community/project, read |
| Q12 | https://github.com/openhab/openhab1-addons/wiki/MAX!Cube-Binding | `UNKNOWN` | Wiki read, date `UNKNOWN` | community, read |
| Q13 | https://insinuator.net/2016/04/discover-the-unknown-analyzing-an-iot-device/ | Blog, `UNKNOWN` | 2016-04 (`EVIDENCED`) | security RE, very valuable, partly speculative |
| Q14 | https://github.com/ernw/insinuator-snippets/tree/master/maxcube/firmware/parser | `UNKNOWN` | Repo contents read, date `UNKNOWN` | RE artifacts, read |
| Q15 | http://www.eq-3.de/produkte/max.html | Manufacturer page (`UNKNOWN`) | Cloud shutdown 2023-07-01 mentioned (`EVIDENCED`) | official |
| Q16 | https://azuresearch-usnc.nuget.org/query?q=maxcube&prerelease=true | nuget.org API, public | Query 2026-11-24 (`EVIDENCED`) | official package index result |

### Explicitly empty searches

- `site:nuget.org "maxcube" OR "eq3" MAX Cube library C#` → `EVIDENCED`: no
  MAX Cube-specific package found; hits were only `Cube.Pdf` (a completely different
  product). The direct NuGet API query `q=maxcube` returned `totalHits: 0`
  (`EVIDENCED`, Q16). **There is no suitable NuGet package.**
- Search for an official "MAX! Cube LAN Gateway Protocol" PDF →
  `EVIDENCED`: only datasheet and user manual found, no protocol document.
- Search for a complete UDP firmware frame format → `EVIDENCED`: not found.
  See section 7.

---

## 3. Official eQ-3 documents (Q2, Q3, Q15)

`EVIDENCED` from Q2 (datasheet V3.0):

- 868.3 MHz bidirectional radio (MAX! protocol).
- Up to 50 devices and 10 rooms.
- Duty cycle < 1% per hour.
- AES-128 encryption is mentioned – **without** specifying key, IV, mode,
  or message format. The mention refers to the product, not to a documented API.
- Ethernet interface, IP configuration, NTP, portal connection.

`EVIDENCED` from Q3 (user manual): only mounting, commissioning, LEDs,
factory reset, and inclusion. No port, no command list, no byte fields.

`EVIDENCED` from Q15: MAX! cloud was shut down on 01.07.2023; local operation
and programming via PC remain permanently possible. **Consequence:** All
portal functions (`u:`/portal URL, `e:`/`d:` cloud crypto) are of no practical
value for the reimplementation; the local TCP path is the only relevant one.

---

## 4. Bouni `max-cube-protocol` (Q1) – the most important specification

### 4.1 Project status

`EVIDENCED`: MIT license, 137 commits, created 2013-12-10, ~120 stars, ~35 forks,
last updated 2025-02-13. The `protocol.md` is explicitly written for firmware
1.1.3 (`EVIDENCED`). The only freely available protocol documentation known to me
that is continuously maintained. Only limitation: many fields are named
"unknown"; nothing may be invented there.

### 4.2 Transport and discovery (`protocol.md`, `Cube_Discovery.md`)

`EVIDENCED`:

- TCP data port **62910**. A cube accepts only **one** TCP connection
  at a time (also confirmed in Q13).
- UDP port **23272** for discovery.
- Discovery request: 19 bytes `eQ3Max*\0**********I`.
- Discovery response: 26 bytes `eQ3MaxAp` + 10 bytes serial number + `>` + `I` +
  3 bytes RF address + 2 bytes firmware.
- Request types: `I` (info), `N`, `h`, `c`, `R` (reboot). Responses arrive on
  port 23272.
- Reboot via UDP is **unauthenticated** (Q13).

### 4.3 Message types (read in full)

`EVIDENCED`, incoming: `H: F: L: C: M: N: A: E: D: b: g: j: p: o: v: w: S:`
`EVIDENCED`, outgoing: `u: i: s: m: n: x: g: q: e: d: B: G: J: P: O: V: W: a: r: t: w: l: c: v: f: z:`

> **Classification 24.11.2026:** This list comes from Q1 (Bouni) and mixes two protocols. `B:/G:/J:/P:/O:/V:/W:` together with `b:/g:/j:/p:/o:/v:/w:` belong, according to manufacturer bytecode (`decompiled/*/remoteproxy/`, `MaxRemoteProxy.java:67-175`), to the **HTTP remote proxy** of the shut-down cloud, not to cube TCP. `g:` is used twice (there `G:` response, here `OUTGOING_MORE_DATA` as a dead constant). Deliberately excluded from the C# integration; evidence in `SecondBrain/wiki/fragen/2026-11-24-maxcube-remote-proxy-opcodes.md` and `docs/maxcube-protocol.md` section 6.1.

| Command | Meaning | Evidence |
|---|---|---|
| `l:` / `L:` | Request/respond with device list | Q1, Q5 |
| `m:` / `M:` | Read/write metadata (rooms/devices) | Q1, Q5 |
| `s:` / `S:` | Send radio command / acknowledgement | Q1, Q5 |
| `c:` / `C:` | Configuration of a device | Q1, Q5 |
| `n:` / `N:` | Inclusion (pairing), with/without timeout | Q1 |
| `t:` | Delete device | Q1 |
| `z:` | Wake-up to devices/group | Q1 |
| `f:` / `F:` | Set/read NTP server | Q1 |
| `v:` / `A:` | Set date/time / ACK | Q1 |
| `a:` | Factory reset (devices lose pairing) | Q1 |
| `q:` | End connection | Q1, Q5 |
| `u:` | Set portal URL | Q1 |
| `e:` / `d:` | Encrypt/decrypt | Q1 |
| `x:` | End pairing | Q1 |

### 4.4 L-message (device states)

`EVIDENCED` (Bouni `L-Message.md`, Q5 `cube.py`): Base64-encoded. Per submessage:
length (1 byte), 3 bytes RF address, 1 unknown byte, 2 bytes flags,
valve position, target temperature (7 bits, `& 0x7F`, /2), date/time until, optional
actual temperature (2 bytes, /10). Terminated by `ce 00`.

From Q5 `cube.py` `parse_l_message` `EVIDENCED`:

- Mode = `bits2 & 3` (0 auto, 1 boost?, 2 manual, 3 vacation – mode values
  `EVIDENCED` from Q10/Q5 `device.py`).
- Battery = `bits2 >> 7`.
- Window contact: `status = data[pos+6] & 3`, `> 0` ⇒ open.
- Wall thermostat actual temperature: `(((data[pos+8] & 0x80) << 1) + data[pos+12]) / 10.0`.
- Thermostat actual temperature only in auto/manual; in boost/vacation → `None`.
- Pointer advance: `pos += length + 1`.

⚠️ `EVIDENCED`: Q5 uses `pos += length + 1`, while Bouni describes the length as the first byte.
For the C# implementation, this must be checked against a real frame.

### 4.5 C-message (configuration)

`EVIDENCED` (Q1 `C-Message.md`, Q5 `cube.py`): 18-byte header (length, RF address,
device type, room ID, firmware, test flag, serial number), followed by
device-specific data. Offset 18–21: comfort/eco/max/min temperature, each
`/2.0`. From offset 29: weekly program.

`EVIDENCED`: 182 bytes = 7 × 26 bytes, starting **Saturday** (Q5 `DAYS`). Per day 13
words of 2 bytes each; bit layout: 7 bits temperature (×2) + 9 bits time in
5-minute steps. `24:00` marks the end of valid switching points.

### 4.6 M-message (metadata)

`EVIDENCED` (Q1 `M-Message.md` and Q5 `cube.py`):

- Format: `M:<index>,<count>,<base64>`. Blocks of ~1900 bytes; `m:00,...`,
  `m:01,...` write. **`UNKNOWN`**: whether the `m:` write function is identical in the
  German/firmware-1.1.3 variant (Q1: "Input is not validated").
- Decoded: 2 unknown bytes `56 02`, then number of rooms; per room:
  room ID, name length, name (UTF-8), 3 bytes group RF address.
- Then number of devices; per device: type (0 cube, 1 radiator thermostat,
  2 thermostat plus, 3 wall thermostat, 4 window contact, 5 switching actuator),
  3 bytes RF address, 10 bytes serial number, name length, name, room ID.

### 4.7 H-, V-, U-, E/D-, A-, N-, F-, T-, Z-, Q-message

`EVIDENCED` (Q1):

- `H:KEQ0523864,097f2c,0113,00000000,477719c0,00,32,0d0c09,1404,03,0000\r\n`
  = serial, RF address, firmware, unknown, HTTP connection ID, duty cycle,
  free slots, date `YYMMDD`, time `HHMM`, state, NTP counter.
- `v:<Base64 timezone>,<UTC seconds since 2000-01-01 as HEX>\r\n`; cube replies `A:`.
- `u:http://...:80` sets the portal URL (obsolete since cloud shutdown).
- `e:`/`E:` encrypt, `d:`/`D:` decrypt; responses are padded with null bytes.
  **Algorithm and purpose described as `UNKNOWN` in Q1.**
- `a:` factory reset; `A:` also acknowledges `t:`, `z:`, `v:`.
- `n:` without argument = pairing without timeout; `n:003c` = 60 s (hex). End pairing with
  `x:`. Important (`EVIDENCED`): read the response only after timeout/`x:`, otherwise
  responses to commands get mixed up.
- `N:` = device type, 3 bytes RF address, 10 bytes serial, 1 byte `ff`.
- `F:ntp.homematic.com,ntp.homematic.com` – default NTP `EVIDENCED`.
- `t:01,1,Dx1U` – count, force flag, Base64 list of room RF addresses.
- `z:1e,G,01` – wake-up time (hex, 1e = 30 s?), scope `G`/`A`/`D`, target.
  ⚠️ Q1 writes "1e=30sec", 0x1e = 30 decimal. For `z:` time please verify on the device;
  `UNKNOWN` whether minutes or seconds are meant.
- `q:` = `q:\r\n` end connection.

### 4.8 S-message / radio commands

`EVIDENCED` (Q1 `S-Message.md`, Q5 `cube.py`):

- Structure: `00` (unknown), `04` (RF flag = group/room; `00` = device), `40`
  (set temperature), 3 bytes `from` (`000000`), 3 bytes target RF address, room ID,
  then payload.
- Commands: `40` temperature, `10` program, `11` eco temperature, `12`
  valve functions, `20` add link, `21` remove link, `22` set group address,
  `23` remove group address, `82` display of actual temperature.
- Q1 note: send wake-up (`z:`) before commands if necessary.
- Response `S:` contains duty cycle, status code, free slots. Status code `0` =
  success (Q5 `commander.py`).

---

## 5. Python `maxcube-api` 0.4.3 (Q4, Q5) – behavioral oracle

`EVIDENCED` read: `cube.py`, `commander.py`, `connection.py`, `thermostat.py`
(earlier), D-message-related metadata.

### 5.1 Connection / timing (`commander.py`, `connection.py`)

`EVIDENCED`:

- Default port 62910.
- Time limits: `update=3.0 s`, `connect=3.0 s`, `flush-input=0 s`,
  `send-radio-msg=30.0 s`, `cmd-reply=2.0 s`.
- `use_persistent_connection = True` by default; with `False`, disconnect after every
  command.
- Messages are buffered until `\r\n` (4096-byte blocks); a 0-byte read
  means end of connection.
- For `s:` commands, the response is checked for status code `0`; with
  duty cycle `100` and free slots `0`, wait up to 10 s and retry
  (retry loop up to 30 s).
- Error ⇒ close socket, reconnect once, repeat command.
- When connecting, an unsolicited `L:` message is stored as "unsolicited";
  `update()` fetches `L:` and delivers all buffered messages.

**Important for C#:** These timeouts/retry rules are the practically proven
reference. A naive `TcpClient` without `\r\n` framing and without
duty-cycle retry will hang in operation.

### 5.2 `set_temperature_mode` (`cube.py`)

`EVIDENCED`, exactly:

```python
target_temperature = int(temperature * 2) + (mode << 6)
byte_cmd = "000440000000" + rf_address + room + to_hex(target_temperature)
# → s:<base64(byte_cmd)>
```

- Mode `0` = auto ⇒ set temperature `0`; then derive target temperature from
  the weekly program.
- Auto/manual/boost/vacation = 0/1/2/3 (`EVIDENCED` from `device.py`/Q10).
- After success, local `mode`/`target_temperature` are updated.

### 5.3 `set_programme` (`cube.py`)

`EVIDENCED`:

- `CMD_SET_PROG = "10"`, `UNKNOWN = "00"`, `RF_FLAG_IS_ROOM = "04"`,
  `RF_FLAG_IS_DEVICE = "00"`, `RF_NULL_ADDRESS = "000000"`.
- Pad arguments to 7 `(temp, until)` tuples with `(0, "00:00")`.
- Command = `00` + RF flag + `10` + `000000`, then per device RF address +
  room ID + weekday (Saturday = 0, `DAYS` list), then 7× `temp_and_time`.
- `temp_and_time`: `assert temp <= 32`, `temp % 0.5 == 0`, minutes % 5 == 0;
  7 bits temperature×2 + 9 bits `minuten/5`, as 2 bytes big-endian.
- `set_programmes_from_config()` processes JSON config per device/day.

### 5.4 `send_metadata`

`EVIDENCED`: In the read 0.4.3 codebase of `cube.py`/`commander.py` there is
**no** function named `send_metadata`. There is only `parse_m_message()` and
the `m:` command from Q1. The open question noted in the working draft is thus
resolved: **no API call of that name**; if needed, build `s:`/`m:` directly.

---

## 6. C# references

### 6.1 `bietiekay/MAXSharp` (Q6) – `EVIDENCED`

- README: "MAXSharp (ELV MAX! Cube C# control library)", (C) 2010–2012 Daniel
  Kirstenpfad.
- `license.txt`: **GNU AGPL v3** (with parts by sones GmbH 2008–2010).
  AGPL is problematic for a closed app; usable as inspiration,
  not for copy-paste.
- Files `EVIDENCED`: `AssemblyInfo.cs`, `DiffSet/`, `ELVMAX_Data/`,
  `MAXEncodeDecode.cs`, `MAXMonitoringThread.cs`, `MAXSharp.csproj`.
- `MAXMonitoringThread.cs` `EVIDENCED`: `TcpClient`/`NetworkStream`,
  `UpdateTime=10000`, `ELVMAXSensorReStoringSec=360`,
  `ELVMAXReconnectTimeMsec=60000`, `ConcurrentQueue<IDeviceDiffSet>` for
  events, `keepRunning` flag. A C# skeleton for polling,
  reconnect, and diff events therefore exists – but no .NET 10, no async/await,
  no tests, frozen in 2015.

### 6.2 Other C# projects (Q7, Q8)

- `EVIDENCED` (only listed in Q1 README): `MAXDebug` (CS) and `MaxManager` (CS).
- `EVIDENCED`: `ababilone/maxmanager` is a "Universal Windows Application that
  allows to manage a Max! / eQ-3 cube". Source code not read in detail →
  concrete protocol knowledge from it `UNKNOWN`.
- `EVIDENCED`: `bietiekay/hacs/.../MAXDebug` known only as a path, not read.

### 6.3 NuGet (Q16)

`EVIDENCED`: API query `q=maxcube&prerelease=true` → `totalHits: 0`. Web search
returned only `Cube.Pdf` (unrelated). **Conclusion: no installable,
maintained NuGet package.** For .NET 10 only a self-implementation or
a local port of old sources remains.

---

## 7. Security / crypto / firmware (Q13, Q14)

`EVIDENCED` from Q13 (Insinuator, 2016):

- Reboot unauthenticated: `eQ3Max*\0KMD1016788R` (UDP).
- Only one TCP connection at a time.
- Firmware can be updated via UDP without authentication.
- `firmware.enc` is encrypted; origin/content undocumented.

`EVIDENCED` from Q14: snippets contain `decrypt.py`, `firmware.enc` (346,352 bytes)
and `parser/`. `decrypt.py` uses **AES-128-CBC** with

```
KEY = 7815696ecbf1c96e6894b779456d330e
IV  = 368aea7612c9ab9163daea7612c9ac93
```

PKCS#7-like padding, Base64 input/output. `EVIDENCED`: origin and
cross-device validity of this key/IV are **not** documented;
Q13 speculates about derivation from serial number/QR code. I tested several
obvious MD5 candidates (`maxcube`, `eQ3Max`, …) – none produced
this key (`EVIDENCED`, local check). Therefore:

- Concrete AES-CBC key from RE snippet: `EVIDENCED`.
- Derivation rule / production validity: `UNKNOWN`.
- UDP firmware frame (header, sequence, CRC, image format): `UNKNOWN`.
- For the app: do **not** implement firmware update; cloud is shut down (Q15). The TCP commands `e:`/`d:`, by contrast, are implemented as `EncryptCommand`/`DecryptCommand` (`CubeCommands.cs:260-271`) and also serve locally as idle keep-alive; **not** adopted is their use as cloud transport (correction 24.11.2026).

---

## 8. Further ecosystem sources

### 8.1 Home Assistant (Q9, Q10)

`EVIDENCED`:

- Integration `maxcube`: legacy, community-maintained, **local polling**.
- Default port 62910, `scan_interval: 300` s.
- `use_persistent_connection = scan_interval <= 300` – consistent with Q5.
- **No weekly schedules** in the HA integration → the reimplementation needs its own path
  (`s:...10...`, section 5.3).
- Modes in `climate.py`: `MAX_DEVICE_MODE_AUTOMATIC`, `_BOOST`, `_MANUAL`,
  `_VACATION`; `OFF_TEMPERATURE = 4.5`; `ON` is 30.5 °C according to Bouni (`EVIDENCED`).

### 8.2 openHAB (Q11, Q12)

`EVIDENCED`:

- MAX! binding: discovery via UDP, bridge with IP + serial number, reboot action.
- Wiki: default IP 192.168.0.222, port 62910, `refreshInterval=10000`,
  `maxRequestsPerConnection`, **exclusive mode** (one client only).
- Confirms the 10-second polling frequency from MAXSharp (section 6.1).

### 8.3 FHEM (Q1 README, `00_MAXLAN.pm` read)

`EVIDENCED`: `fhem/FHEM/00_MAXLAN.pm` (Matthias Gehre, 2012–2013) uses
`MIME::Base64`, `MaxCommon`. Constants: `$reconnect_interval = 60`,
`$roundtriptime = 3`, `$read_timeout = 3`, `$metadata_magic = 0x56`,
`$metadata_version = 2`, `$defaultPollInterval = 60`. The `magic 0x56`
matches the first byte of the M payload from Q1 (`56 02 …`) – a nice
cross-check (`EVIDENCED`).

### 8.4 CLI/other implementations (only listed in Q1 README)

`EVIDENCED` as links present, contents not read → details `UNKNOWN`:

- https://github.com/ivesdebruycker/maxcube-cli (JavaScript)
- https://github.com/pacostiro/MAX-cube-ctl (C)
- https://github.com/spinscale/maxcube-java/ (Java)
- https://github.com/KnuthLohse/MAXCPP (C)
- https://github.com/ercpe/pymax (Python)
- https://github.com/Juerd/eq3-max (Perl)

They are suitable as additional cross-checks, but not necessary as long as
Q1 + Q5 + Q10 agree.

---

## 9. Strategy recommendation (trade-off)

### Option (a): Use a C# library

- **Pro:** C# idiomatic, `MAXSharp` provides TCP loop, diff queue, device model.
- **Con:** `EVIDENCED` AGPL v3, last state 2015, no NuGet, no .NET 10,
  no async, no tests. Maintenance and license risk.
- **Verdict:** **Not as a foundation.** At most `MAXMonitoringThread.cs` and
  `MAXEncodeDecode.cs` as a source of ideas (without copying code).

### Option (b): Port the Python implementation to C#

- **Pro:** `EVIDENCED` 0.4.3 is complete, modern (type annotations), active until 2021,
  MIT-like, timeouts/retry/weekly program exactly traceable. Directly
  portable: `Message`, `Connection`, `Commander`, `MaxCube`, `Thermostat`.
- **Con:** Python dynamics (dicts, `struct`, `bytearray`) must be typed explicitly;
  some fields remain "unknown".
- **Verdict:** **Primary blueprint.** Rebuild the 1:1 structure in C#, with
  `CancellationToken`, `Channel<T>` instead of `ConcurrentQueue`, and
  `System.Text.Json` instead of `json`.

### Option (c): Java REA + Bouni

- **Pro:** existing `docs/maxcube-protocol.md` (687 lines from Java 1.4.6 REA)
  is already in the project; Bouni has more message types and more current maintenance.
- **Con:** Java code is very old, probably incomplete; decompiled code hard to verify.
- **Verdict:** **Cross-check/fallback.** For ambiguities (e.g. `L:` offset,
  `z:` time unit), compare Java REA and Bouni against each other.

### Option (d): Combination – **recommended**

1. **Spec:** Bouni (Q1) as primary reference; annotate every line of the C# parser with
   the Bouni field name.
2. **Orchestration:** Port Python 0.4.3 (Q5) – connection framing,
   commander timeouts/retry, `set_temperature_mode`, `set_programme`.
3. **Cross-check:** `docs/maxcube-protocol.md` (Java REA) + FHEM `00_MAXLAN.pm`
   + HA `climate.py` (Q10) for device types/modes.
4. **Do not implement:** Cloud, UDP firmware, reboot (Q13/Q15). The TCP commands `e:`/`d:` are implemented (`CubeCommands.cs:260-271`); only their use as cloud transport remains excluded (correction 24.11.2026).
5. **Tests:** Against a real cube: `l:` → device count, `m:` → rooms,
   `s:...40...` → valve reacts; observe `H:` duty cycle.
6. **License:** Do not adopt AGPL code (Q6); MIT/Apache references (Q1, Q5,
   Q10) may serve as a source of ideas, but write your own code.

**Rationale:** Only (d) provides up-to-date field definitions,
proven connection behavior, and a controllable license basis at the same time. (a)
fails due to AGPL and age, (b) alone due to unevidenced fields, (c) alone due
to lack of maintenance.

---

## 10. Open points (`UNKNOWN`) – do not guess

1. Meaning of the "unknown" bytes in `L:`, `H:`, `M:`, and `S:`.
2. `z:` time unit (Q1 contradictory: `1e` = 30 s vs. "minutes").
3. Origin/validity of the AES key/IV from Q14; no production derivation
   found.
4. Complete UDP firmware update frame format.
5. `m:` write semantics on firmware 1.1.3 / German variant.
6. Whether the `N:` response after `n:` is buffered identically on all firmwares
   (Q1 warns about timeout ordering).
7. Exact meaning of `S:` status codes other than `0` (Q5 checks only `0`).

---

## 11. Source list for reference (only links actually seen)

- Q1 https://github.com/Bouni/max-cube-protocol
  - https://github.com/Bouni/max-cube-protocol/blob/master/protocol.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/Cube_Discovery.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/L-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/C-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/H-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/M-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/S-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/A-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/N-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/Q-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/F-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/T-Command.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/Z-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/U-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/V-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/E-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/D-Message.md
- Q2 https://www.eq-3.com/downloads/download/max/pdb/en/MAX_Cube-LAN-Gateway_99004_datasheet_V3.0.pdf
- Q3 https://www.eq-3.com/Downloads/eq3/downloads_produktkatalog/max/bda/en/BC-LGW-O-TW_UM_EN.pdf
- Q4 https://pypi.org/project/maxcube-api/
- Q5 https://github.com/uebelack/python-maxcube-api
  - https://github.com/uebelack/python-maxcube-api/blob/master/maxcube/cube.py
  - https://github.com/uebelack/python-maxcube-api/blob/master/maxcube/commander.py
  - https://github.com/uebelack/python-maxcube-api/blob/master/maxcube/connection.py
  - https://github.com/uebelack/python-maxcube-api/blob/master/maxcube/thermostat.py
- Q6 https://github.com/bietiekay/MAXSharp
  - https://github.com/bietiekay/MAXSharp/blob/master/MAXSharp/MAXMonitoringThread.cs
  - https://github.com/bietiekay/MAXSharp/blob/master/MAXSharp/MAXEncodeDecode.cs
  - https://github.com/bietiekay/MAXSharp/blob/master/license.txt
- Q7 https://github.com/bietiekay/hacs/tree/master/tools/MAXDebug
- Q8 https://github.com/ababilone/maxmanager
- Q9 https://www.home-assistant.io/integrations/maxcube
- Q10 https://github.com/home-assistant/core/blob/dev/homeassistant/components/maxcube/climate.py
- Q11 http://www.openhab.org/addons/bindings/max
- Q12 https://github.com/openhab/openhab1-addons/wiki/MAX!Cube-Binding
- Q13 https://insinuator.net/2016/04/discover-the-unknown-analyzing-an-iot-device/
- Q14 https://github.com/ernw/insinuator-snippets/tree/master/maxcube/firmware/parser
  - https://github.com/ernw/insinuator-snippets/blob/master/maxcube/decrypt.py
- Q15 http://www.eq-3.de/produkte/max.html
- Q16 https://azuresearch-usnc.nuget.org/query?q=maxcube&prerelease=true
- Project-internal: `docs/maxcube-protocol.md` (687 lines, Java 1.4.6 REA, `EVIDENCED`)
