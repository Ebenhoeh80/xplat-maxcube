# xplat-maxcube

> Cross-platform MAX! Cube desktop application for Windows and Linux.

[![CI](https://github.com/Ebenhoeh80/xplat-maxcube/actions/workflows/ci.yml/badge.svg)](https://github.com/Ebenhoeh80/xplat-maxcube/actions/workflows/ci.yml)
[![Release](https://github.com/Ebenhoeh80/xplat-maxcube/actions/workflows/release.yml/badge.svg)](https://github.com/Ebenhoeh80/xplat-maxcube/actions/workflows/release.yml)
[![GitHub Release](https://img.shields.io/github/v/release/Ebenhoeh80/xplat-maxcube)](https://github.com/Ebenhoeh80/xplat-maxcube/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Sponsor on GitHub](https://img.shields.io/badge/%E2%9D%A4%20Sponsor%20on%20GitHub-EA4AAA?style=for-the-badge&logo=githubsponsors&logoColor=white)](https://github.com/sponsors/Ebenhoeh80)

[English](README.md) | [Deutsch](README.de.md)

---

A local C#/.NET 10 desktop application for the eQ-3 MAX! Cube LAN gateway. It
replaces the local control of the former MAX! Local Application — **no Java,
browser, Jetty, cloud, HTTPS update check or administrator rights**. The Cube
remains the source of truth; SQLite is only a cache, settings store and audit log
(`src/MaxCube.Infrastructure/Persistence/SqliteStateStore.cs:8-13`).

Version **0.8.0** (`Directory.Build.props:3`), to be released on 2026-11-27 as
`v0.8.0` (previous published release: `v0.7.0`). See [CHANGELOG.md](CHANGELOG.md).

> **FACT (user note 2026-11-25, not independently verified):** eQ-3 has shut down
> MAX! cloud control and removed the cloud app from the app stores. Remote
> access, product account, proxy and app updates are therefore deliberately out
> of scope (`docs/features.md:34-36`).

## Contents

- [How it works](#how-it-works)
- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick start](#quick-start)
- [Data provenance and write protection](#data-provenance-and-write-protection)
- [Configuration and data folder](#configuration-and-data-folder)
- [Supported functions](#supported-functions)
- [Known limitations](#known-limitations)
- [Roadmap and ideas](#roadmap-and-ideas)
- [Privacy and security](#privacy-and-security)
- [Verification status](#verification-status)
- [Screenshots](#screenshots)
- [Documentation](#documentation)
- [Development](#development)
- [Support this project](#support-this-project)
- [Contributing and license](#contributing-and-license)

## How it works

1. The app connects to the Cube over **local TCP, port `62910`**. `Find Cube`
   sends a **UDP discovery broadcast only when you click it** (local port
   `23272`) — never at startup.
2. Rooms, devices, schedules and configuration are **read from the Cube**. The
   local SQLite cache is used for offline display and as an audit log; it never
   overrides the Cube.
3. Every section shows where its values came from: *not read from Cube*, *cache
   from …*, *live …*, *stale* or *failed* — with a symbol **and** localised text,
   never colour alone. Activating the Cube tab reads it automatically when
   connected (`src/MaxCube.UI/ViewModels/MainWindowViewModel.cs:567`).
4. Writing needs an explicit confirmation **and** a genuinely read basis.
   Unread placeholders, stale data and offline state block write commands;
   cached values are writable only while offline, never over an active
   connection (`src/MaxCube.UI/ViewModels/MainWindowViewModel.cs:1074-1106,1210-1225`).
5. A persistent status bar shows connection, host, data provenance, the last
   action with a timestamp and the log state. Diagnostics are opt-in and stay
   local; the viewer and ZIP export are in the **Status** tab.

**Important:** the Cube accepts only **one client connection at a time**. Close
the MAX! smartphone app and any other integration before connecting, otherwise
the connection fails.

## Features

- **Rooms and devices:** target/measured temperature, valve position, window
  state, device status; modes Auto / Permanent / Temporary and ECO / COMFORT.
- **Weekly schedule:** up to 13 switch points per day, a graphical day bar,
  quick editing in 0.5 °C / 5-minute steps, drag-and-drop of a point onto the
  day bar (snapped to 5 minutes, order and limit validated) and copying to
  other rooms. The weekly, configuration and device tabs keep their own room
  selection. Unread placeholder profiles cannot be sent.
- **Temporary mode:** a free date and time in the room and house tabs; the
  protocol still rounds to 30-minute boundaries.
- **Administration:** room configuration, boost / descaling / valve settings,
  wall thermostat display, button configuration, inclusion, device mapping,
  structure maintenance, time / time zone / NTP and UDP network configuration.
- **Wake-up (`z:`):** an explicit, confirmed radio wake-up for the selected
  room. It is never embedded in other write paths; `A:` only confirms that the
  Cube accepted the command, not its effect on the devices.
- **Diagnostics:** radio duty cycle from the `S:` send status, local error report
  and error-code catalogue `MAX0001`–`MAX1051`. Nothing is sent anywhere.
- **Application log:** opt-in (`log.enabled`, default off), INFO/WARN/ERROR,
  in-app viewer and local ZIP export on click only
  (`src/MaxCube.UI/ViewModels/MainWindowViewModel.StatusBar.cs`).
- **Status bar:** connection, host/port, data provenance with read time, last
  action with timestamp and log state always visible with symbol **and** text.
- **Data provenance:** consistent status for every section with read timestamp
  in DE / EN / FR / IT / NL / PL (`src/MaxCube.UI/ViewModels/DataStatusViewModel.cs:13`).
- **About page:** author `Ebenhoeh80`, MIT licence, project link and an explicit
  *unofficial, not from eQ-3* notice. The link opens only on user click.
- **Self-contained packages** for `win-x64` and `linux-x64`; no installed .NET
  runtime required.

## Requirements

- An **eQ-3 MAX! Cube LAN gateway** reachable on the local network (default port
  `62910`). No flashing, no CUL/CUN, no cloud.
- Windows 10+ or Linux with X11/Wayland for the desktop UI. On Debian/Ubuntu
  SkiaSharp/Avalonia need system libraries that are not part of the ZIP:
  `sudo apt-get update && sudo apt-get install -y libfontconfig1 libx11-6 libxcursor1 libxext6 libxfixes3 libxi6 libxrandr2 libice6 libsm6 libgl1 libvulkan1 xdg-utils` (`xdg-utils` only for external links).
  Check with `./MaxCube.UI.Desktop --check-linux-deps`. Opening the project link
  needs `xdg-open`, `gio` or `wslview`; without them the URL is shown in the
  status bar instead of terminating the app.
- For building from source: **.NET 10 SDK**. The released ZIPs do not need it.

## Installation

### Ready-to-run packages (recommended)

Download a self-contained archive from
[Releases](https://github.com/Ebenhoeh80/xplat-maxcube/releases). Archives are
built by GitHub Actions for every `v*` [tag](https://github.com/Ebenhoeh80/xplat-maxcube/tags),
checked with the headless self-test and attached to the release together with
`SHA256SUMS.txt` (plain checksums, no signature claim).

- **Windows:** unzip and run `MaxCube.UI.Desktop.exe`.
- **Linux:** install the system libraries first (see Requirements), unzip, run
  `chmod +x MaxCube.UI.Desktop`, then `./MaxCube.UI.Desktop`. An X11 or Wayland
  session is required. Missing system libraries produce a concrete hint instead
  of a stack trace. For a source checkout, `./start.sh --install-deps` installs
  them with your sudo password.

### Build from source

```powershell
git clone https://github.com/Ebenhoeh80/xplat-maxcube.git
cd xplat-maxcube
.\start.cmd
```

```bash
git clone https://github.com/Ebenhoeh80/xplat-maxcube.git
cd xplat-maxcube
bash start.sh
```

`start.cmd` / `start.sh` locate a .NET 10 SDK, build incrementally and start the
app (`start.cmd:8-43`, `start.sh:8-35`). `start.sh` sets
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` automatically when `libicuuc` is
missing.

### Headless self-test

```powershell
dotnet test MaxCube.slnx
dotnet src/MaxCube.UI.Desktop/bin/Debug/net10.0/MaxCube.UI.Desktop.dll --selfcheck
```

The self-test builds paths, SQLite, gateway, service, view model and
localisation, checks single-instance handling and all six languages. It contacts
**no** Cube and sends **no** broadcast (`src/MaxCube.UI.Desktop/Program.cs:26-92`).

## Quick start

1. Click **Connect** — the saved endpoint is used. If the Cube has a new IP,
   click **Find Cube** instead; the discovery broadcast is sent only then.
2. In **Rooms**, select a room. Values are marked *live* after the read. Change
   a setpoint and confirm — writes are disabled until a real read happened.
3. Open **Weekly profile** to edit a schedule; the placeholder shown before a
   read is deliberately not sendable.
4. Open the **Cube** tab: it reads the Cube automatically while connected.
   Time zone, NTP and network sections can fail independently.
5. Use the **Status** tab for the log viewer, export and error catalogue;
   **About** for author, licence and project link. Enable the log in
   **Settings** only if you need diagnostics; the export stays local.

## Data provenance and write protection

| State | Meaning | Write allowed? |
| :--- | :--- | :--- |
| `NotRead` | No read from the Cube yet — placeholder or empty | No |
| `Loading` | A read is in progress | No |
| `Live` | Read from the Cube in this session | Yes, with confirmation |
| `Cached` | Read from the local SQLite cache with timestamp | Only offline, with confirmation |
| `Stale` | Read data is too old | No |
| `Failed` | Read failed | No |

`CubeSnapshot.CapturedAt` supplies the cache timestamp. Disconnect resets all
section states to *not read*. The Cube is never written without a prior read,
confirmation and a non-stale, online state.

## Configuration and data folder

The data folder is `%LOCALAPPDATA%\MaxCube` on Windows and
`$XDG_DATA_HOME/MaxCube` (default `~/.local/share/MaxCube`) on Linux.
`MAXCUBE_DATA_DIR` relocates it for portable use (`AppPaths.cs:3-10`).
Host and port of the saved endpoint are kept locally and restored at startup.
Daily log files live in the `logs/` subfolder; the ZIP export is written next
to it in the data folder.

## Supported functions

Most local functions of the original MAX! Local Application are implemented;
49 of 61 catalogued features are complete, 1 partial, 1 replaced and 10 not
mappable (cloud/update/firmware-write) (`docs/features.md`).

| Area | Read | Write |
| :--- | :--- | :--- |
| Rooms, temperatures, modes | yes | yes, confirmed |
| Weekly schedules (thermostats) | yes | yes, confirmed |
| Wall thermostat display, boost, valve, descaling | yes | yes, confirmed |
| Inclusion, device mapping, structure | yes | yes, confirmed |
| Time, time zone, NTP | yes | yes, confirmed |
| UDP network configuration | yes | yes, confirmed |
| Radio duty cycle (`S:`) | yes | n/a |
| Firmware version | yes (read-only) | no, deliberately disabled |

## Known limitations

- **One Cube connection at a time.** Other clients must be closed.
- Only the original **MAX! Cube LAN gateway** is supported; no CUL/CUN, no
  newer proprietary protocol and no MAX! cloud.
- **Wall thermostats cannot be programmed** from this app; their display
  configuration can be read and written.
- A device paired with the Cube appears after a reload; removing a device is
  reflected after a reload/restart of the app.
- Drag-and-drop moves a switch point to a new time; it does not resize an
  existing interval edge. Use the `−5` / `+5` buttons for fine shifts.
- The radio duty cycle is limited by the Cube. At 100 % it pauses radio commands
  for about an hour; changing many settings at once takes time.
- The technical wire format of `0x12`, `0x20`–`0x23`, `m:`, `w:`, `v:` and `z:`
  is hardware-confirmed (`FACT`), but the **functional effect on the devices is
  only partially confirmed by read-back** (`UNKNOWN`).
- The `F:` NTP response format, the effect of a UDP `C:` write and golden
  captures with 8–13 schedule points remain `UNKNOWN`.
- The Linux UI look on a real desktop was confirmed by the user on 2026-11-27 as visually okay;
  tray behaviour remains `UNKNOWN` and CI covers the headless self-test only.
- No screenshots of the real Cube have been published yet.

Statements in this project are classified as `FACT`, `OBSERVATION`, `INFERENCE`
or `UNKNOWN` with a `file:line` reference. Hardware evidence is in
[docs/HARDWARE-VERIFY.md](docs/HARDWARE-VERIFY.md).

## Roadmap and ideas

Planned and under consideration — **give a reaction to what matters to you** and
bring your own ideas to
[Discussions → Ideas](https://github.com/Ebenhoeh80/xplat-maxcube/discussions/categories/ideas):

- Fine-grained log levels, filters and a redacted support export.
- Resizing of weekly schedule intervals by dragging their edges.
- Read-only firmware queries (`latest` / `least compatible`).
- `autoSetupTimeZoneConfiguration` and automatic pre-wake behaviour (deliberately
  not wired: writes must stay explicit).
- Real read-backs for derived write formats, UDP `C:` write, `F:` NTP format,
  golden captures with 8–13 schedule points.
- Screenshots and a Linux desktop smoke check.

## Privacy and security

- Fully local. **No cloud, no telemetry, no HTTP, no update check, no browser
  dependency, no AGPL code.** Outbound links open only after a user click.
- TCP write commands and the UDP broadcast require a user action; discovery is
  never automatic at startup.
- No passwords, tokens or secrets are stored in the repository or the wiki.
- The optional log can contain IP addresses, serial numbers and room values;
  inspect a ZIP export before sharing it. No payloads, credentials or tokens
  are logged (`src/MaxCube.Application/Abstractions/IAppLog.cs:1-5`).
- Reverse-engineering inputs (`decompiled/`, `extracted/`, `evidence/`) are
  **not** part of this repository; only our own documentation under `docs/` is
  open source (see [docs/INDEX.md](docs/INDEX.md)).
- The decompiled and extracted artefacts of the eQ-3 MAX! Local Application
  remain under their original licences.

## Verification status

Build **0 warnings / 0 errors** and **553/553** hardware-free tests green on Windows
10 and Debian 13 (WSL2) - Domain 64, Application 90, Protocol 200, Infrastructure
69, UI 130. `dotnet format --verify-no-changes`, the NuGet vulnerability audit and
YAML parsing of both workflows are green; `--selfcheck` reports `OK` at 0.8.0 on
both systems and in both self-contained publish outputs. No hardware write test
was added for wake-up (`docs/HARDWARE-VERIFY.md` §30). These results are
`OBSERVATION`.

Switching the UI language updates every visible area without a restart or Cube
access: cached status/result texts, confirmation preview, dirty hint, weekday and
mode labels, device states and error hints (`CHANGELOG.md`, §17 of
`docs/HARDWARE-VERIFY.md`).

**FACT (2026-11-25, authorised hardware check):** discovery, `0x11`
comfort/eco, `0x12`, `0x20`–`0x23`, `m:`, `w:`, `v:` and `z:` were sent to the
original Cube `192.168.10.60` and acknowledged; the Cube state was restored to
21.5 °C / 16.5 °C and confirmed read-only (`docs/HARDWARE-VERIFY.md`). The
functional acceptance by the thermostats is only partially read-back verified.

**FACT (user, 27.11.2026):** The desktop UI on Linux was checked and found visually okay; native-speaker review is complete for German and English. French, Italian, Dutch and Polish await feedback via issues or PRs (`docs/HARDWARE-VERIFY.md` §28).

## Screenshots

No screenshots have been published yet. The UI is a native Avalonia desktop
application with a main window (rooms/devices), weekly schedule editor and
administration views; the intended design is in
[docs/gui-specification.md](docs/gui-specification.md). Screenshots from a real
Cube are planned for a follow-up release.

## Documentation

| Purpose | Document |
| :--- | :--- |
| System understanding, decisions, risks | [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) |
| Setup, build, tests, conventions | [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) |
| C# interfaces and Cube wire protocol | [docs/API.md](docs/API.md) |
| SQLite schema, migration, data flows | [docs/DATABASE.md](docs/DATABASE.md) |
| Network, CI, packages | [docs/OPERATIONS.md](docs/OPERATIONS.md) |
| Auth, secrets, privacy | [docs/SECURITY.md](docs/SECURITY.md) |
| Error patterns and diagnostics | [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) |
| Start/stop/update/maintenance | [docs/RUNBOOK.md](docs/RUNBOOK.md) |
| Wiki extract, FAQ, lessons learned, ADRs | [docs/SECOND_BRAIN.md](docs/SECOND_BRAIN.md) |
| Hardware verification on the original Cube | [docs/HARDWARE-VERIFY.md](docs/HARDWARE-VERIFY.md) |
| Reverse-engineering sources and council | [docs/INDEX.md](docs/INDEX.md), [docs/features.md](docs/features.md), [docs/gremium.md](docs/gremium.md) |

## Development

```powershell
dotnet restore MaxCube.slnx
dotnet build MaxCube.slnx --nologo
dotnet test MaxCube.slnx --nologo
```

```bash
dotnet restore MaxCube.slnx
dotnet build MaxCube.slnx --nologo
dotnet test MaxCube.slnx --nologo
```

The solution contains `Domain`, `Application`, `Protocol`, `Infrastructure`,
`UI`, `UI.Desktop` and the matching test projects. CI
([.github/workflows/ci.yml](.github/workflows/ci.yml)) builds and tests on
`ubuntu-latest` and `windows-latest` and runs the headless self-test.

Conventions:

- Protocol statements are marked `FACT` / `OBSERVATION` / `INFERENCE` /
  `UNKNOWN` with a `file:line` reference.
- The Cube is the source of truth. Never test a new write format without a
  controlled reset; hardware tests only against an authorised device.
- Do not add cloud, HTTP or update dependencies.

## ❤️ Support this project

<div align="center">

**I build and maintain this project in my free time and cover the running costs myself — my time, electricity, tools and IT equipment.**

Sponsoring is voluntary and does not buy support, features or response times. Every contribution, however small, helps. Thank you! 🙏

<a href="https://github.com/sponsors/Ebenhoeh80">
  <img src="https://img.shields.io/badge/%E2%9D%A4%20Sponsor%20on%20GitHub-EA4AAA?style=for-the-badge&logo=githubsponsors&logoColor=white" alt="GitHub Sponsors" height="42">
</a>
<a href="https://www.paypal.com/pool/9p84qTHCFr?sr=wccr">
  <img src="https://img.shields.io/badge/%E2%9D%A4%20Donate%20via%20PayPal-PayPal-00457C?style=for-the-badge&logo=paypal&logoColor=white" alt="Donate via PayPal" height="42">
</a>
<a href="https://buymeacoffee.com/daneben">
  <img src="https://img.shields.io/badge/%E2%98%95%20Buy%20me%20a%20coffee-FFDD00?style=for-the-badge&logo=buymeacoffee&logoColor=black" alt="Buy Me a Coffee" height="42">
</a>

</div>

## Contributing and license

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). Please do
not add cloud functions, firmware writing or unconfirmed Cube write commands.

Released under the [MIT licence](LICENSE). This project is **unofficial and not
affiliated with eQ-3**. The decompiled and extracted artefacts of the eQ-3 MAX!
Local Application are expressly not part of this repository and remain subject
to their original licence terms.

**Author:** Ebenhoeh80 · **Project:** <https://github.com/Ebenhoeh80/xplat-maxcube>
