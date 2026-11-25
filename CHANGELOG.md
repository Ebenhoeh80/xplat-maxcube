# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.6.0] - 2026-11-25

Kritischer Cognitive Walkthrough mit den Gremien UX, Architektur und QA: das
Diagnoseprotokoll ist opt-in, lokal sichtbar und exportierbar; die Statusleiste
zeigt Verbindung, Datenherkunft, letzte Aktion und Protokollstatus dauerhaft.
Gleichzeitig werden die gefährlichsten Erstnutzer-Fallen geschlossen.

### Added

- Opt-in-Protokoll (`log.enabled`, Standard aus): Schalter in „Einstellungen“
  und in der Statusleiste, wirkt ohne Neustart. Einträge mit INFO/WARN/ERROR
  zu Start, Verbinden/Trennen, Reconnect/Stale, Aktionen und Export
  (`src/MaxCube.UI/ViewModels/MainWindowViewModel.StatusBar.cs`).
- Lokaler Protokoll-Viewer im Status-Reiter mit Aktualisieren, Ordner öffnen
  und ZIP-Export ausschließlich auf Benutzerklick; kein Versand, kein Dialog,
  keine Funk-nutzlasten (`src/MaxCube.Infrastructure/Platform/FileAppLog.cs`).
- Echte Statusleiste: Verbindung mit Symbol und Text, Host/Port, Datenherkunft
  mit Leszeitpunkt, letzte Aktion mit Uhrzeit, Protokollstatus und Exportbutton.
  Stale/Reconnect erscheint nur noch einmal (`src/MaxCube.UI/Views/MainWindow.axaml`).
- Fehlerdialog mit Zeitstempel und Sprung zum Protokoll.
- Beim Start wird der zuletzt verbundene Endpoint aus dem lokalen Cache
  übernommen; die README-Behauptung „saved endpoint is used“ stimmt jetzt.

### Changed

- Schreibzugriffe aus dem Cache sind nur offline erlaubt. Bei bestehender
  Verbindung blockiert `Cached` das Senden, damit keine veralteten Werte auf
  den Cube zurückgeschrieben werden (`src/MaxCube.UI/ViewModels/MainWindowViewModel.cs:280`).
- Der Cube-Reiter übernimmt beim automatischen Lesen weiterhin Lese- und
  Schreibstatus gemeinsam; ein zwischenzeitlicher Cache-Zustand bleibt sichtbar.
- Version auf `0.6.0`; 468/468 hardwarefreie Tests grün.

### Security / scope

- Der Export bleibt eine lokale ZIP-Datei im Datenordner. Er kann IP-Adressen,
  Seriennummern und Raumwerte enthalten und ist vor der Weitergabe zu prüfen;
  ein Hinweis steht direkt im Viewer. Keine Cloud, kein HTTP, keine Telemetrie.

[Unreleased]: https://github.com/Ebenhoeh80/xplat-maxcube/compare/v0.6.0...HEAD
[0.6.0]: https://github.com/Ebenhoeh80/xplat-maxcube/releases/tag/v0.6.0

Eindeutige Datenherkunft: Der Cube-Reiter zeigt „nicht vom Cube gelesen“ oder liest
beim Aktivieren automatisch. Nie wird ein Default- oder Cache-Wert als Live ausgegeben.

### Added

- Einheitlicher Datenstatus pro Bereich (`NotRead`, `Loading`, `Live`, `Cached`,
  `Stale`, `Failed`) mit Symbol **und** lokalisiertem Text plus Leszeitpunkt in
  allen sechs Sprachen (DE/EN/FR/IT/NL/PL).
- Auto-Read des Cube-Reiters bei Aktivierung, sofern verbunden; leise ohne
  modales „Beschäftigt“. Einzelne Abschnitte (Zeitzone, NTP, Netzwerk) können
  unabhängig voneinander scheitern.
- Reiter „Über“ mit Autor `Ebenhoeh80`, MIT-Lizenz, GitHub-Projekt und dem
  ausdrücklichen Hinweis „inoffiziell, nicht von eQ-3“. Der Link öffnet nur nach
  Benutzerklick; kein Update-Download.
- Wochenprofil-Platzhalter sind als „nicht gelesen“ markiert und nicht sendbar.

### Changed

- Schreibbefehle für Räume, Haus, Struktur, Konfiguration, Zeitzone, NTP und
  Netzwerk setzen eine echte Lesung voraus; `Stale` und Offline sperren zusätzlich.
- `CubeSnapshot.CapturedAt` wird als Cache-Zeitpunkt in der Anzeige verwendet.

### Fixed

- Verbindungsabbruch setzt alle Bereichsstatus auf „nicht gelesen“ und leert
  Firmware-, Funkauslastungs- und Diagnosetext.
- Lesende Hardwareverifikation gegen `192.168.10.60:62910` bestätigt unveränderte
  Baseline (Serial `JEQ0355273`, FW `275`, 6 Räume, 9 Geräte).

### Security / scope

- Weiterhin keine Cloud, kein Jetty, kein Browser, kein HTTP-Update-Check und
  keine Firmware-Schreibfunktion. UDP-Broadcast nur über „Cube suchen“.

[0.5.0]: https://github.com/Ebenhoeh80/xplat-maxcube/releases/tag/v0.5.0
[0.4.0]: https://github.com/Ebenhoeh80/xplat-maxcube/releases/tag/v0.4.0

First public release under the name **xplat-maxcube**.

### Added

- Golden-capture protocol tests built from real hardware traffic of the original
  Cube (`192.168.10.60`, serial `JEQ0355273`, firmware `275`).
- Wire-format test for the Java-style group-header variant of `0x20`/`0x21`.
- Real 42-byte UDP `c` response fixture with decoded gateway and subnet mask.
- Repository documentation: `README.md`, `LICENSE` (MIT), `CHANGELOG.md`,
  `CONTRIBUTING.md` and an automated release workflow.

### Changed

- **Breaking (protocol):** `SetRoomValveFunctionsCommand` (`0x12`) now emits the
  14-byte Java-compatible frame instead of the previous 17-byte frame.
- **Breaking (protocol):** `RemoveGroupAddressCommand` (`0x23`) now emits 11 bytes
  with the room ID in byte 10, matching the Java client.
- Link/unlink flows now also emit the Java group-header radiogram with room ID and
  anchor thermostat, while keeping the direct pairwise links.
- UDP network configuration now decodes and re-encodes the Java field order
  IP, gateway, subnet mask, DNS 1, DNS 2, DHCP, encryption.
- Version bumped from `0.3.0` to `0.4.0`.

### Fixed

- Discovery and UDP configuration now bind the local socket to port `23272`
  instead of an ephemeral port; discovery returns real Cube answers again.
- `S:` send status is documented and decoded correctly as
  `S:<dutyHex>,<discarded>,<freeHex>`; values above zero are airtime/free slots,
  not error codes.

### Security / scope

- No cloud, remote access, Jetty, browser, HTTP update check or firmware writing.
  No Cube or network write access without an explicit user action.
  UDP broadcast is only sent when the user chooses "Cube suchen".

