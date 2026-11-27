> Deutsche Fassung. Muttersprachlerprüfung DE/EN am 27.11.2026 durch den Nutzer bestätigt; FR/IT/NL/PL: Rückmeldung über Issues/PRs erwartet.

# xplat-maxcube

> Cross-platform MAX! Cube desktop application for Windows and Linux.

[![CI](https://github.com/Ebenhoeh80/xplat-maxcube/actions/workflows/ci.yml/badge.svg)](https://github.com/Ebenhoeh80/xplat-maxcube/actions/workflows/ci.yml)
[![Release](https://github.com/Ebenhoeh80/xplat-maxcube/actions/workflows/release.yml/badge.svg)](https://github.com/Ebenhoeh80/xplat-maxcube/actions/workflows/release.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Auf GitHub sponsern](https://img.shields.io/badge/%E2%9D%A4%20Auf%20GitHub%20sponsern-EA4AAA?style=for-the-badge&logo=githubsponsors&logoColor=white)](https://github.com/sponsors/Ebenhoeh80)

[English](README.md) | [Deutsch](README.de.md)

---

Lokale C#/.NET-10-Desktopanwendung für den eQ-3 MAX! Cube. Sie ersetzt die lokale Steuerung der ehemaligen MAX! Local Application – **ohne Java, Browser, Jetty, Cloud, HTTPS-Update-Check oder Administratorrechte**. Der Cube bleibt Source of Truth; SQLite ist nur Cache, Einstellungsspeicher und Audit-Log (`src/MaxCube.Infrastructure/Persistence/SqliteStateStore.cs:8-13`).

Version **0.8.0** (`Directory.Build.props:3`), vorgesehen am **27.11.2026** als `v0.8.0` (davor veröffentlicht: `v0.7.0`). Siehe [CHANGELOG.md](CHANGELOG.md).

> **FAKT (Nutzerhinweis 25.11.2026, nicht extern verifiziert):** eQ-3 hat die MAX!-Cloud-Steuerung abgeschaltet und die Cloud-App aus den App-Stores entfernt. Fernzugriff, Produktkonto, Proxy und App-Update bleiben daher bewusst ausgeschlossen (`docs/features.md:34-36`).

## Inhalt

- [Funktionen](#funktionen)
- [Voraussetzungen](#voraussetzungen)
- [Installation](#installation)
- [Verwendung](#verwendung)
- [Datenherkunft und Schreibschutz](#datenherkunft-und-schreibschutz)
- [Konfiguration und Datenordner](#konfiguration-und-datenordner)
- [Unterstützte Funktionen](#unterstützte-funktionen)
- [Screenshots](#screenshots)
- [Entwicklung](#entwicklung)
- [Architektur](#architektur)
- [Grenzen und bewusste Ausschlüsse](#grenzen-und-bewusste-ausschüsse)
- [Roadmap und Ideen](#roadmap-und-ideen)
- [Datenschutz und Sicherheit](#datenschutz-und-sicherheit)
- [Verifikationsstand](#verifikationsstand)
- [Dokumentation](#dokumentation)
- [Projekt unterstützen](#projekt-unterstützen)
- [Mitmachen und Lizenz](#mitmachen-und-lizenz)

## Funktionen

- **Räume und Geräte:** Soll-/Isttemperatur, Ventilstellung, Fensterstatus, Gerätestatus; Modi Auto / Permanent / Temporary sowie ECO / COMFORT.
- **Wochenprogramm:** bis zu 13 Schaltpunkte pro Tag, grafische Tagesleiste, Schnellbearbeitung in 0,5-°C-/5-Minuten-Schritten, Drag-and-drop eines Punktes auf die Tagesleiste (5-Minuten-Raster, Reihenfolge- und 13-Punkte-Prüfung) und Kopieren auf andere Räume. Wochenprofil-, Konfigurations- und Geräte-Reiter behalten jeweils ihre eigene Raumauswahl. Ungelesene Platzhalterprofile können nicht gesendet werden.
- **Temporary:** freie Endzeit in Räume- und Haus-Reiter; im Protokoll bleibt die Rundung auf 30 Minuten.
- **Administration:** Raumkonfiguration, Boost / Entkalkung / Ventileinstellungen, Wandthermostat-Anzeige, Tasterkonfiguration, Anlernen, Gerätezuordnung, Strukturpflege, Uhrzeit / Zeitzone / NTP und UDP-Netzwerkkonfiguration. Wake-up (`z:`) nur als ausdrücklich bestätigte Funkaktion; `A:` quittiert nur die Annahme durch den Cube.
- **Diagnose:** Funkauslastung aus dem `S:`-Sendestatus, lokaler Fehlerbericht und Fehlercode-Katalog `MAX0001`–`MAX1051`. Es wird nichts nach außen gesendet.
- **Anwendungsprotokoll:** opt-in (`log.enabled`, Standard aus), INFO/WARN/ERROR, Viewer in der App und lokaler ZIP-Export nur auf Klick (`src/MaxCube.UI/ViewModels/MainWindowViewModel.StatusBar.cs`).
- **Statusleiste:** Verbindung, Host/Port, Datenherkunft mit Lesezeitpunkt, letzte Aktion mit Uhrzeit und Protokollstatus immer sichtbar – mit Symbol **und** Text.
- **Datenherkunft:** einheitlicher Status für jeden Bereich mit Lesezeitpunkt in DE / EN / FR / IT / NL / PL (`src/MaxCube.UI/ViewModels/DataStatusViewModel.cs:13`).
- **Über-Seite:** Autor `Ebenhoeh80`, MIT-Lizenz, Projektlink und ausdrücklicher Hinweis *inoffiziell, nicht von eQ-3*. Der Link öffnet erst nach Benutzerklick.
- **Self-contained-Pakete** für `win-x64` und `linux-x64`; keine installierte .NET-Runtime nötig.

## Voraussetzungen

- Ein **eQ-3 MAX! Cube LAN Gateway**, im lokalen Netz erreichbar (Standardport `62910`). Kein Flashen, kein CUL/CUN, keine Cloud.
- Windows 10+ oder Linux mit X11/Wayland für die Desktop-Oberfläche. Auf Debian/Ubuntu benötigen SkiaSharp/Avalonia Systembibliotheken, die nicht im ZIP enthalten sind:
  `sudo apt-get update && sudo apt-get install -y libfontconfig1 libx11-6 libxcursor1 libxext6 libxfixes3 libxi6 libxrandr2 libice6 libsm6 libgl1 libvulkan1 xdg-utils` (`xdg-utils` nur für externe Links).
  Prüfen mit `./MaxCube.UI.Desktop --check-linux-deps`. Zum Öffnen des Projekt-Links werden `xdg-open`, `gio` oder `wslview` benötigt; ohne sie zeigt die App die URL in der Statusleiste an, statt sich zu beenden.
- Zum Bauen aus Quellen: **.NET 10 SDK**. Die veröffentlichten ZIPs benötigen es nicht.

## Installation

### Fertige Pakete (empfohlen)

Unter [Releases](https://github.com/Ebenhoeh80/xplat-maxcube/releases) liegen selbstständige Pakete für `win-x64` und `linux-x64` (self-contained, kein .NET-SDK nötig). Die Archive werden bei jedem `v*`-[Tag](https://github.com/Ebenhoeh80/xplat-maxcube/tags) automatisch von GitHub Actions gebaut, mit dem fensterlosen Selbsttest geprüft und zusammen mit `SHA256SUMS.txt` an das Release angehängt. Die Datei enthält reine Prüfsummen, keine Signatur.

- **Windows:** ZIP entpacken, `MaxCube.UI.Desktop.exe` starten.
- **Linux:** zuerst die Systembibliotheken installieren (`sudo apt-get update && sudo apt-get install -y libfontconfig1 libx11-6 libxcursor1 libxext6 libxfixes3 libxi6 libxrandr2 libice6 libsm6 libgl1 libvulkan1 xdg-utils` (xdg-utils nur für externe Links); das ZIP enthält keine Systembibliotheken), dann ZIP entpacken, `chmod +x MaxCube.UI.Desktop` und `./MaxCube.UI.Desktop`. Für die Oberfläche sind X11 oder Wayland nötig. Mit `./MaxCube.UI.Desktop --check-linux-deps` lässt sich die Voraussetzung vorab prüfen; fehlen Bibliotheken, erscheint ein konkreter Hinweis statt eines Stacktraces. Im Quellcheckout installiert `./start.sh --install-deps` die Pakete nach Eingabe des sudo-Passworts. Zum Öffnen des Projekt-Links wird `xdg-open`, `gio` oder `wslview` benötigt; ohne einen solchen Opener zeigt die App die URL in der Statusleiste an, statt sich zu beenden.

### Aus Quellen bauen

Voraussetzung: **.NET 10 SDK**.

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

`start.cmd` und `start.sh` suchen ein .NET-10-SDK, bauen inkrementell und starten die Desktop-App (`start.cmd:8-43`, `start.sh:8-35`). `start.sh` setzt bei fehlendem `libicuuc` automatisch `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.

### Fensterloser Selbsttest

```powershell
dotnet test MaxCube.slnx
dotnet src/MaxCube.UI.Desktop/bin/Debug/net10.0/MaxCube.UI.Desktop.dll --selfcheck
```

Der Selbsttest baut Pfade, SQLite, Gateway, Service, ViewModel und Lokalisierung, prüft Instanzschutz und die sechs Sprachen. Er kontaktiert **keinen** Cube und sendet **keinen** Broadcast (`src/MaxCube.UI.Desktop/Program.cs:26-92`).

Der Datenordner ist `%LOCALAPPDATA%\MaxCube` unter Windows bzw. `$XDG_DATA_HOME/MaxCube` (Standard `~/.local/share/MaxCube`) unter Linux. `MAXCUBE_DATA_DIR` verschiebt ihn für portablen Betrieb (`AppPaths.cs:3-10`).

## Verwendung

1. **Cube verbinden:** Entweder den gespeicherten Endpoint verwenden oder **Cube suchen**. Der UDP-Broadcast wird ausschließlich auf diese Benutzeraktion hin gesendet, niemals beim App-Start (`UdpDiscoveryTransport.cs`). Der lokale UDP-Port ist `23272`; die eigentliche Steuerungsverbindung läuft über TCP `host:62910`.
2. **Räume und Geräte:** Räume, Ist-/Solltemperaturen und Gerätestatus werden aus dem Cube gelesen. Modi Auto/Permanent/Temporary sowie ECO/COMFORT und Haussteuerung sind verfügbar.
3. **Wochenprofile:** Bis zu 13 Schaltpunkte pro Tag, grafische Tagesleiste, Schnellbearbeitung im 0,5-°C-/5-Minuten-Raster, Drag-and-drop eines Punktes auf die Tagesleiste (5-Minuten-Raster, Reihenfolge- und 13-Punkte-Prüfung) und Kopieren auf andere Räume. Wochenprofil-, Konfigurations- und Geräte-Reiter behalten jeweils ihre eigene Raumauswahl.
4. **Temporary:** Freie Endzeit in Räume- und Haus-Reiter; im Protokoll bleibt die Rundung auf 30 Minuten.
5. **Administration:** Raumkonfiguration, Boost/Entkalkung/Ventil, Wandthermostat-Anzeige, Tasterkonfiguration, Anlernen, Gerätezuordnung, Strukturpflege, Uhrzeit/Zeitzone/NTP und UDP-Netzwerkkonfiguration. Wake-up (`z:`) nur als ausdrücklich bestätigte Funkaktion; `A:` quittiert nur die Annahme durch den Cube.
6. **Diagnose:** Funkauslastung aus dem `S:`-Sendestatus, lokaler Fehlerbericht und Fehlercode-Katalog `MAX0001`–`MAX1051`. Es wird nichts nach außen gesendet.
7. **Datenherkunft:** Jeder Bereich zeigt „nicht vom Cube gelesen“, „Cache vom …“, „Live …“ oder „veraltet“ mit Uhrzeit. Der Cube-Reiter liest beim Aktivieren automatisch, wenn eine Verbindung besteht. Ungelesene Platzhalter (z. B. im Wochenprofil) sind nicht sendbar.
8. **Statusleiste:** Verbindung, Host/Port, Datenherkunft, letzte Aktion mit Uhrzeit und Protokollstatus bleiben dauerhaft sichtbar – mit Symbol **und** Text.
9. **Diagnoseprotokoll:** In den Einstellungen oder der Statusleiste opt-in einschalten (Standard aus). Der Viewer im Reiter „Status“ zeigt die letzten Einträge; der ZIP-Export entsteht nur auf Klick und bleibt im Datenordner. Er kann IP-Adressen, Seriennummern und Raumwerte enthalten – vor der Weitergabe prüfen.
10. **Über:** Autor, MIT-Lizenz und GitHub-Link; ausdrücklich inoffiziell und nicht von eQ-3. Der Link öffnet erst nach Benutzerklick.

Alle Cube-Schreibzugriffe erfordern eine ausdrückliche Bestätigung in der Oberfläche **und** eine tatsächlich gelesene Ausgangsbasis; `Stale` und Offline sperren das Senden. Cache-Werte sind nur offline schreibbar: Bei bestehender Verbindung werden sie nie auf den Cube zurückgeschrieben (`src/MaxCube.UI/ViewModels/MainWindowViewModel.cs:1074-1106,1210-1225`). Der Normalbetrieb ist lesend; die SQLite-Datenbank ist ausschließlich Cache und Audit-Log.

## Datenherkunft und Schreibschutz

| Zustand | Bedeutung | Schreiben erlaubt? |
| :--- | :--- | :--- |
| `NotRead` | Noch kein Lesen vom Cube – Platzhalter oder leer | Nein |
| `Loading` | Ein Lesevorgang läuft | Nein |
| `Live` | In dieser Sitzung vom Cube gelesen | Ja, mit Bestätigung |
| `Cached` | Aus dem lokalen SQLite-Cache gelesen, mit Zeitstempel | Nur offline, mit Bestätigung |
| `Stale` | Gelesene Daten sind zu alt | Nein |
| `Failed` | Lesen fehlgeschlagen | Nein |

`CubeSnapshot.CapturedAt` liefert den Cache-Zeitstempel. Trennen setzt alle Bereichszustände auf *nicht gelesen* zurück. Es wird nie ohne vorheriges Lesen, Bestätigung und einen nicht veralteten, online-Zustand auf den Cube geschrieben.

## Konfiguration und Datenordner

Der Datenordner ist `%LOCALAPPDATA%\MaxCube` unter Windows und `$XDG_DATA_HOME/MaxCube` (Standard `~/.local/share/MaxCube`) unter Linux. `MAXCUBE_DATA_DIR` verschiebt ihn für portablen Betrieb (`AppPaths.cs:3-10`). Host und Port des gespeicherten Endpoints werden lokal aufbewahrt und beim Start wiederhergestellt. Tägliche Protokolldateien liegen im Unterordner `logs/`; der ZIP-Export wird daneben im Datenordner geschrieben.

## Unterstützte Funktionen

Die meisten lokalen Funktionen der ehemaligen MAX! Local Application sind umgesetzt; 49 von 61 katalogisierten Funktionen sind vollständig, 1 teilweise, 1 ersetzt und 10 nicht abbildbar (Cloud/Update/Firmware-Schreiben) (`docs/features.md`).

| Bereich | Lesen | Schreiben |
| :--- | :--- | :--- |
| Räume, Temperaturen, Modi | ja | ja, bestätigt |
| Wochenprogramme (Thermostate) | ja | ja, bestätigt |
| Wandthermostat-Anzeige, Boost, Ventil, Entkalkung | ja | ja, bestätigt |
| Anlernen, Gerätezuordnung, Struktur | ja | ja, bestätigt |
| Uhrzeit, Zeitzone, NTP | ja | ja, bestätigt |
| UDP-Netzwerkkonfiguration | ja | ja, bestätigt |
| Funkauslastung (`S:`) | ja | entfällt |
| Firmware-Version | ja (nur lesend) | nein, bewusst deaktiviert |

## Screenshots

Noch keine Screenshots veröffentlicht. Die Oberfläche ist eine native Avalonia-Desktopanwendung mit Hauptfenster (Räume/Geräte), Wochenprofil-Editor und Admin-Dialogen; die geplante Gestaltung ist in [`docs/gui-specification.md`](docs/gui-specification.md) beschrieben. Screenshots eines echten Cubes werden in einem Folge-Release ergänzt.

## Entwicklung

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

Die Solution enthält die Projekte `Domain`, `Application`, `Protocol`, `Infrastructure`, `UI`, `UI.Desktop` und die zugehörigen Testprojekte. Der CI-Workflow [`.github/workflows/ci.yml`](.github/workflows/ci.yml) baut und testet auf `ubuntu-latest` und `windows-latest` und fährt zusätzlich den fensterlosen Selbsttest.

**Wichtige Konventionen:**

- Aussagen über das Protokoll werden als `FAKT`, `BEOBACHTUNG`, `INFERENZ` oder `UNBEKANNT` mit Beleg `Datei:Zeile` markiert. Hardwarebelege stehen in [`docs/HARDWARE-VERIFY.md`](docs/HARDWARE-VERIFY.md).
- Der Cube ist Source of Truth. Neue Schreibformate nie ohne kontrollierte Rücksetzung testen; echte Hardwaretests nur mit freigegebener Gegenstelle.
- Die Reverse-Engineering-Eingaben (`decompiled/`, `extracted/`, `evidence/`) sind **nicht** Teil dieses Repos. Nur unsere eigenen Auswertungen unter `docs/` sind quelloffen. Details: [`docs/INDEX.md`](docs/INDEX.md).
- Keine Cloud-, HTTP- oder Update-Abhängigkeiten ergänzen.

Weitere Hinweise: [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md) und [CONTRIBUTING.md](CONTRIBUTING.md).

## Architektur

```mermaid
flowchart LR
  User[Benutzer] --> UI[Avalonia UI / MVVM]
  UI --> App[CubeApplicationService]
  App --> GW[ICubeGateway / CubeGateway]
  App --> Store[ICubeStateStore / SQLite]
  App --> Disc[ICubeDiscovery / UDP]
  GW --> Proto[MaxCube.Protocol]
  Proto --> TCP[(MAX! Cube TCP 62910)]
  Disc --> UDP[(UDP 23272)]
  Store --> DB[(maxcube.db)]
  UI --> Log[(logs/maxcube-*.log)]
```

Details: [Architektur](docs/ARCHITECTURE.md), [API](docs/API.md), [Datenbank](docs/DATABASE.md), [Module](docs/modules.md).

## Grenzen und bewusste Ausschlüsse

| Thema | Status |
|---|---|
| Cloud, Fernzugriff, Produktkonto, Proxy, App-Update | bewusst verworfen (Cloud abgeschaltet) |
| Firmware schreiben | bewusst deaktiviert; nur read-only Versionsprüfung |
| Browser-/Jetty-/AGPL-Abhängigkeit | keine |
| WebSocket-/HTTP-API | keine; `docs/API.md` beschreibt C#-Schnittstellen und das Cube-Wire-Protokoll |
| Golden-Capture der Schreibformate `m:`, `w:`, `v:`, `z:` | `FAKT` (Wire-Format und `A:`-Quittung am Original-Cube), fachliche Gerätewirkung teils `UNBEKANNT` |
| `S:`-Sendestatus | `FAKT`: `S:<dutyHex>,<discarded>,<freeHex>`; Airtime und freie Slots, keine Fehlercodes |
| UDP-Adressreihenfolge | `FAKT`: IP, Gateway, Maske, DNS 1, DNS 2; durch Java-Code und echte `c`-Antwort belegt |
| Drag-and-drop im Wochenprogramm | Verschieben eines Punktes umgesetzt; Größenänderung eines Intervallendes weiterhin offen |
| Linux-Oberfläche auf echtem Desktop, Tray-Verhalten | Optik vom Nutzer am 27.11.2026 als „augenscheinlich ok“ bestätigt; Tray-Verhalten weiterhin `UNBEKANNT` (CI prüft fensterlosen Selbsttest) |

## Roadmap und Ideen

Geplant und in Betrachtung – **reagiere auf das, was dir wichtig ist**, und bringe eigene Ideen ein in [Discussions → Ideas](https://github.com/Ebenhoeh80/xplat-maxcube/discussions/categories/ideas):

- Fein abgestufte Protokollstufen, Filter und ein geschwärzter Support-Export.
- Nur-lesende Firmware-Abfragen (`latest` / `least compatible`).
- Größenänderung von Wochenprogramm-Intervallen durch Ziehen der Intervallenden (Verschieben ist umgesetzt).
- `autoSetupTimeZoneConfiguration` und automatisches Vorwecken (bewusst nicht verdrahtet: Schreiben bleibt explizit).
- Echte Read-backs für abgeleitete Schreibformate, UDP-`C:`-Schreiben, `F:`-NTP-Format, Golden-Captures mit 8–13 Schaltpunkten.
- Screenshots und ein Desktop-Smoke-Test unter Linux.

## Datenschutz und Sicherheit

- Vollständig lokal. **Keine Cloud, keine Telemetrie, kein HTTP, kein Update-Check, keine Browser-Abhängigkeit, kein AGPL-Code.** Externe Links öffnen erst nach Benutzerklick.
- TCP-Schreibbefehle und der UDP-Broadcast erfordern eine Benutzeraktion; beim Start wird nie automatisch Discovery durchgeführt.
- Es werden keine Passwörter, Tokens oder Secrets im Repository oder im Wiki gespeichert.
- Das optionale Protokoll kann IP-Adressen, Seriennummern und Raumwerte enthalten; einen ZIP-Export vor der Weitergabe prüfen. Es werden keine Nutzdaten, Zugangsdaten oder Tokens protokolliert (`src/MaxCube.Application/Abstractions/IAppLog.cs:1-5`).
- Die Reverse-Engineering-Eingaben (`decompiled/`, `extracted/`, `evidence/`) sind **nicht** Teil dieses Repositories; nur unsere eigene Dokumentation unter `docs/` ist quelloffen (siehe [docs/INDEX.md](docs/INDEX.md)).
- Die dekompilierten und extrahierten Artefakte der eQ-3 MAX! Local Application unterliegen weiterhin ihren ursprünglichen Lizenzen.

## Verifikationsstand

Build **0 Warnungen / 0 Fehler** und **553/553** hardwarefreie Tests grün unter Windows 10 und Debian 13 (WSL2) - Domain 64, Application 90, Protocol 200, Infrastructure 69, UI 130. `dotnet format --verify-no-changes`, die NuGet-Schwachstellenprüfung und das YAML-Parsing beider Workflows sind grün; `--selfcheck` meldet `OK` mit 0.8.0 auf beiden Systemen und in beiden selbstständigen Publish-Paketen. Für Wake-up wurde kein Hardware-Schreibtest hinzugefügt (`docs/HARDWARE-VERIFY.md` §30). Diese Ergebnisse sind `BEOBACHTUNG`.

Ein Wechsel der Oberflächensprache aktualisiert ohne Neustart und ohne Cube-Zugriff alle sichtbaren Bereiche: zwischengespeicherte Status-/Ergebnistexte, Schreibvorschau, Dirty-Hinweis, Wochentags- und Modusbezeichnungen, Gerätestatus und Fehlerhinweise (`CHANGELOG.md`, `docs/HARDWARE-VERIFY.md` §17).

**FAKT (25.11.2026, freigegebene Hardwareprüfung):** Discovery, `0x11` Komfort/Eco, `0x12`, `0x20`–`0x23`, `m:`, `w:`, `v:` und `z:` wurden am Original-Cube `192.168.10.60` gesendet und quittiert; der Cube-Zustand wurde auf die Ursprungswerte 21,5 °C / 16,5 °C zurückgesetzt und nur-lesend bestätigt (`docs/HARDWARE-VERIFY.md`). Die fachliche Annahme durch Thermostate ist nur teilweise per Read-back belegt.

**FAKT (Nutzer, 27.11.2026):** Die Optik der Desktop-App unter Linux wurde geprüft und „augenscheinlich ok“ befunden; die Muttersprachlerprüfung ist für Deutsch und Englisch abgeschlossen. Französisch, Italienisch, Niederländisch und Polnisch erwarten Rückmeldung über Issues oder PRs (`docs/HARDWARE-VERIFY.md` §28).

## Dokumentation

| Ziel | Dokument |
|---|---|
| Systemverständnis, Entscheidungen, Risiken | [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) |
| Setup, Build, Tests, Konventionen | [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) |
| C#-Schnittstellen und Cube-Wire-Protokoll | [docs/API.md](docs/API.md) |
| SQLite-Schema, Migration, Datenflüsse | [docs/DATABASE.md](docs/DATABASE.md) |
| Netzwerk, CI, Pakete | [docs/OPERATIONS.md](docs/OPERATIONS.md) |
| Auth, Secrets, Datenschutz | [docs/SECURITY.md](docs/SECURITY.md) |
| Fehlerbilder und Diagnose | [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) |
| Start/Stopp/Update/Wartung | [docs/RUNBOOK.md](docs/RUNBOOK.md) |
| Wiki-Auszug, FAQ, Lessons Learned, ADRs | [docs/SECOND_BRAIN.md](docs/SECOND_BRAIN.md) |
| Hardwareprüfung am Original-Cube | [docs/HARDWARE-VERIFY.md](docs/HARDWARE-VERIFY.md) |
| Reverse-Engineering-Quellen und Gremium | [docs/INDEX.md](docs/INDEX.md), [docs/features.md](docs/features.md), [docs/gremium.md](docs/gremium.md) |

## ❤️ Support this project

<div align="center">

**Ich entwickle und pflege dieses Projekt in meiner Freizeit und trage die laufenden Kosten selbst – meine Zeit, Strom, Werkzeuge und IT-Ausstattung.**

Sponsoring ist freiwillig und begründet keinen Anspruch auf Support, Funktionen oder Antwortzeiten. Jeder Beitrag, egal wie klein, hilft. Vielen Dank! 🙏

<a href="https://github.com/sponsors/Ebenhoeh80">
  <img src="https://img.shields.io/badge/%E2%9D%A4%20Auf%20GitHub%20sponsern-EA4AAA?style=for-the-badge&logo=githubsponsors&logoColor=white" alt="GitHub Sponsors" height="42">
</a>
<a href="https://www.paypal.com/pool/9p84qTHCFr?sr=wccr">
  <img src="https://img.shields.io/badge/%E2%9D%A4%20Spenden%20via%20PayPal-PayPal-00457C?style=for-the-badge&logo=paypal&logoColor=white" alt="Spenden via PayPal" height="42">
</a>
<a href="https://buymeacoffee.com/daneben">
  <img src="https://img.shields.io/badge/%E2%98%95%20Spendier%20mir%20einen%20Kaffee-FFDD00?style=for-the-badge&logo=buymeacoffee&logoColor=black" alt="Buy Me a Coffee" height="42">
</a>

</div>

## Mitmachen und Lizenz

Beiträge sind willkommen – siehe [CONTRIBUTING.md](CONTRIBUTING.md). Bitte keine Cloud-Funktionen, kein Firmware-Schreiben und keine unbestätigten Cube-Schreibzugriffe ergänzen.

Veröffentlicht unter der [MIT-Lizenz](LICENSE). Die dekompilierten und extrahierten Artefakte der eQ-3 MAX! Local Application sind ausdrücklich **nicht** Bestandteil dieses Repos und unterliegen weiterhin ihren ursprünglichen Lizenzbedingungen.
