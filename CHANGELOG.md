# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- Trennen liest nach `q:` jetzt bis zum echten EOF statt nur bis zur ersten Zeile. Der Cube
  sendet eine Abschlusszeile (z. B. `M:…`) und erst danach EOF; die frühere Implementierung
  schloss den Socket nach der Abschlusszeile und konnte die einzige TCP-Session am Cube
  hängen lassen. `CubeGateway.AwaitDisconnectEofAsync` nutzt nun ein gemeinsames 200-ms-Budget
  für alle Zeilen bis EOF (`CubeGateway.cs:453-474`).

### Verification

- Build 0 Warnungen/0 Fehler; Debian 13 (WSL2) **514/514** Tests grün. Neuer Test
  `Disposal_drains_the_final_cube_line_before_eof` in `CubeGatewayTests.cs` bildet die
  `M:`-Zeile plus EOF nach `q:` ab.

## [0.6.7] - 2026-11-26

### Fixed

- Sprachwechsel aktualisiert jetzt auch die von Avalonia gerenderten Überschriften, Reiter und
  Gerätetexte. Ursache war nicht der Textkatalog, sondern die XAML-Indexerbindung: Ein
  `PropertyChanged("Item[]")` auf dem `Localizer` lässt Avalonia `{Binding Loc[…]}` nicht neu
  auswerten. Die Ansicht bindet nun an ein austauschbares `Labels`-Objekt
  (`MainWindow.axaml`, `MainWindowViewModel.cs`, `MainWindowViewModel.Admin.cs`).
- Der interne `Localizer` bleibt stabil, damit Tray-Menü und abonnierende Kind-Viewmodels den
  Sprachwechsel weiterhin erhalten; nur die XAML-Bindungsquelle wird ersetzt.

### Verification

- Build 0 Warnungen/0 Fehler; Debian 13 (WSL2) **513/513** Tests grün;
  `SELFCHECK OK … 0.6.7 … SQLite ok`.
- Neue Headless-Render-Tests mit echtem `MainWindow` und Fluent-Theme: DE→EN für Verbinden,
  Trennen, Reiter „Geräte“, Überschrift „Gerät“ und Gerätetyp sowie DE→EN/FR/IT/NL/PL für
  dieselben Gerätetexte (`HeadlessLanguageTests.cs`).
- Hardwaresonde am Original-Cube: nach `q:` EOF in 9–16 ms, anschließend TCP-Connect mit
  `H:`-Handshake nach 2 s erfolgreich; auch ein harter Socket-Close wurde nach 2 s sofort
  wieder angenommen. Der Cube hält die Session also nicht lange nach dem Beenden.
  Details in [HARDWARE-VERIFY.md](docs/HARDWARE-VERIFY.md#25-sprachwechsel-und-session-release-067).
- **FAKT (Nutzeraussage 26.11.2026):** Der native Linux-Desktop funktioniert. Damit ist der
  frühere Punkt „außerhalb WSLg nicht geprüft“ geklärt.
- **UNBEKANNT:** Eine Muttersprachlerprüfung der sechs Sprachen wurde nicht durchgeführt;
  Vollständigkeit und Platzhalter bleiben weiterhin maschinell geprüft.

### Release

- PR [#25](https://github.com/Ebenhoeh80/xplat-maxcube/pull/25) wurde als Squash
  `d452d82` in `main` gemergt; alle sieben PR-Checks waren grün. Der annotierte Tag
  `v0.6.7` zeigt auf diesen Merge-Commit und wurde erst danach erstellt. Der
  Release-Lauf `45853761096` war grün; [v0.6.7](https://github.com/Ebenhoeh80/xplat-maxcube/releases/tag/v0.6.7)
  ist veröffentlicht.
- Geprüfte Assets: `xplat-maxcube-0.6.7-win-x64.zip` (SHA-256
  `8e9fdec601730e4c6231c5b26c092aa1e0f339009e66ef7e9756728096c7a977`) und
  `xplat-maxcube-0.6.7-linux-x64.zip` (SHA-256
  `4d8ae381e054b1d5343a9a598905ea47b97c1cf0e0d4bf75b09d187681c358fd`). Die lokal
  berechneten Hashes stimmen mit den GitHub-Digests überein; beide Pakete liefen
  fensterlos mit `SELFCHECK OK … 0.6.7 … SQLite ok` unter Windows 10.0.26300 und
  Debian GNU/Linux 13 (trixie).
- Der Doku-Nachtrag der Release-Belege erfolgt ohne neues Tag; `v0.6.7` bleibt auf
  `d452d82`.

## [0.6.6] - 2026-11-26

### Fixed

- Verbindungsfehler zeigen jetzt einen vollständig lokalisierten Ursachenhinweis im Dialog
  statt nur eines Generic-Texts: Timeout, nicht erreichbarer Endpoint, fehlender bzw.
  ungültiger Handshake, nicht unterstützte Firmware und bereits bestehende Verbindung.
  Alle sechs Sprachen folgen dem Sprachwechsel ohne Neustart; die rohe Ausnahme bleibt im
  Protokoll und im Datenstatus.
- Ein `nmap`-Portscan wurde bewusst **nicht** in den Verbindungs- oder Reconnect-Pfad
  eingebaut. Der vorhandene TCP-Connect mit 30-s-Grenze plus `H:`/`L:`/`C:`/`M:`-Handshake
  ist die maßgebliche Prüfung; nmap würde nur „Port offen“ melden und eine externe,
  rechtebehaftete Abhängigkeit einführen.

### Verification

- Build 0 Warnungen/0 Fehler; Windows und Debian 13 (WSL2) **507/507** Tests grün;
  `SELFCHECK OK … 0.6.6 … SQLite ok` auf beiden Systemen.
- Neue Regressionstests: sechs Verbindungsursachen auf Deutsch und nach Sprachwechsel
  (EN/FR/PL/NL/IT) – jeweils ohne englischen Rohtext im sichtbaren Dialog.
- **UNBEKANNT:** Hardware-EOF-Reaktion am Original-Cube, Avalonia-Sichtprüfung und
  Muttersprachlerprüfung bleiben offen; die Reaktion des Original-Cube auf einen nmap-Scan
  wurde nicht gemessen.

### Release

- PR [#23](https://github.com/Ebenhoeh80/xplat-maxcube/pull/23) wurde als Squash
  `05fa4c6` in `main` gemergt; alle sieben PR-Checks waren grün. Der annotierte Tag
  `v0.6.6` zeigt auf diesen Merge-Commit und wurde erst danach erstellt. Der
  Release-Lauf `45845522904` war grün; [v0.6.6](https://github.com/Ebenhoeh80/xplat-maxcube/releases/tag/v0.6.6)
  ist veröffentlicht.
- Geprüfte Assets: `xplat-maxcube-0.6.6-win-x64.zip` (SHA-256
  `8565d463dadfb3f1768ed4fff62a15ee52f9d05f32a40e953f7f7bb785e01ffd`) und
  `xplat-maxcube-0.6.6-linux-x64.zip` (SHA-256
  `aefb701ee6074873bc1bb898da20d3dd73b526447ebb5881af33dd2ad68a9e80`). Die lokal
  berechneten Hashes stimmen mit den GitHub-Digests überein; beide Pakete liefen
  fensterlos mit `SELFCHECK OK … 0.6.6 … SQLite ok` unter Windows 10.0.26300 und
  Debian GNU/Linux 13 (trixie).
- Der Doku-Nachtrag der Release-Belege erfolgt ohne neues Tag; `v0.6.6` bleibt auf
  `05fa4c6`.

## [0.6.5] - 2026-11-26

### Fixed

- Beenden trennt jetzt in allen normalen Pfaden sauber vom Cube: Tray → „Beenden“ ruft
  `desktop.TryShutdown()` statt des forcierten `desktop.Shutdown()`. Damit läuft der
  Cleanup (Reconnect stoppen, `q:` senden, Socket/DB schließen, Lock freigeben) auch über
  das Tray. Ein zweites `ShutdownRequested` bricht das laufende Cleanup nicht mehr ab.
- Nach `q:` wartet das Gateway begrenzt auf EOF, bevor es den Socket schließt – wie der
  Herstellerclient in `DisconnectCubeCommand.java:15-19`. Der Socket wird auch bei einer
  hängenden Queue geschlossen; ein Reconnect nach dem Dispose kann keinen neuen Socket
  mehr öffnen.
- `CubeApplicationService` ist jetzt `IAsyncDisposable`: beim App-Ende werden
  Reconnect-Loop und Gateway-Event-Abos beendet, ohne ein zweites `q:` zu senden.
- Sprachwechsel: Formatargumente vom Typ `LocalizedValue` werden beim Re-Render in der
  neuen Sprache ausgewertet. Fehlerdialoge mit Code bleiben nach de→en→fr vollständig
  lokalisiert. Wochenprofil-Fehler und mehrere Protokollzeilen erscheinen nicht mehr als
  fest deutscher Rohtext.
- Zahlen- und Datumsformate folgen jetzt tatsächlich der UI-Sprache. Da die App mit
  `InvariantGlobalization=true` baut, trägt eine beschreibbare Kopie der invarianten
  Kultur die Trennzeichen und Kurzdatumsmuster pro Sprache – ohne ICU-Voraussetzung auf
  Linux. Protokoll- und Berechnungspfade bleiben weiterhin explizit invariant.

### Verification

- Build 0 Warnungen/0 Fehler; Windows und Debian 13 (WSL2) **501/501** Tests grün;
  `--selfcheck OK … 0.6.5 … SQLite ok` auf beiden Systemen.
- Neue Regressionstests: sauberer Tray-Exit (Quellprüfung), `q:` + EOF, `q:` genau einmal,
  kein Connect nach Dispose, Service löst Event-Abos, Fehlerdialog de→en→fr,
  Zahlenformate für alle sechs Sprachen.
- **UNBEKANNT:** Hardwarewirkung des EOF-Wartens am Original-Cube nicht gemessen;
  Avalonia-Sichtprüfung und Muttersprachlerprüfung weiterhin offen. Audit- und
  Diagnosetexte bleiben historische Rohtexte und werden nicht nachträglich übersetzt.

### Release

- PR [#20](https://github.com/Ebenhoeh80/xplat-maxcube/pull/20) wurde als Squash
  `5af4edd` in `main` gemergt; alle sieben PR-Checks waren grün. Der annotierte Tag
  `v0.6.5` zeigt auf diesen Merge-Commit und wurde erst danach erstellt. Der
  Release-Lauf `45803317941` war grün; [v0.6.5](https://github.com/Ebenhoeh80/xplat-maxcube/releases/tag/v0.6.5)
  ist veröffentlicht.
- Geprüfte Assets: `xplat-maxcube-0.6.5-win-x64.zip` (SHA-256
  `6163152c75eea9b83a4508d022be9959242aefe4d19708b418d52a901115e36b`) und
  `xplat-maxcube-0.6.5-linux-x64.zip` (SHA-256
  `2e16bb6df70d35fe571db8af39bd5fdaebf6cdef10bc3a99c529b820fa98b36b`). Beide
  liefen fensterlos mit `SELFCHECK OK … 0.6.5 … SQLite ok` unter Windows 10.0.26300
  und Debian GNU/Linux 13 (trixie).
- Der Doku-Nachtrag PR [#21](https://github.com/Ebenhoeh80/xplat-maxcube/pull/21)
  (`fb4bb8d`) folgte ohne neues Tag; `v0.6.5` bleibt auf `5af4edd`.

## [0.6.4] - 2026-11-26

### Fixed

- Sprachwechsel funktioniert jetzt auch beim zweiten und weiteren Wechsel: `LocalizedValue.In`
  behält die Renderfunktion, damit zwischengespeicherte Erfolgs-, Fehler-, Warn-, Vorschau-,
  Status- und Dirty-Texte nicht in der Sprache des ersten Wechsels erstarren.
- Enum-Werte in lokalisierten Sätzen kommen nicht mehr aus `Enum.ToString()`: Wochenprofil,
  Modusmeldungen und Gerätefund nutzen `EnumLabels`. Der für den Cube bestimmte vorbelegte
  Gerätename bleibt sprachneutral (Seriennummer).
- Service-Warnungen werden als Code + Parameter geliefert und in allen sechs Sprachen
  gerendert; die rohe Meldung bleibt nur im Log. Fehlerdialoge zeigen keinen unübersetzten
  Ausnahmetext mehr, sondern einen lokalisierten Code-Hinweis.
- Numerische Anzeigen und Schaltpunkt-Temperaturen werden nach dem Sprachwechsel neu
  formatiert; `DefaultThreadCurrentCulture` gilt auch für Hintergrundtasks.
- „Netzwerkinfos“ zeigen „Konfigurierte IP“ und „Aktive IP (verbinden)“ getrennt. Bei DHCP
  ist die gespeicherte IP nicht die erreichbare Adresse. Netzwerklesen wird gesperrt, wenn
  die angezeigte Host-Zeile nicht mehr zum tatsächlich verbundenen Endpoint passt. Die
  Byte-Reihenfolge des belegten `c`-Formats bleibt unverändert.

### Added

- Wartungsseite mit „Cube neu starten“ (UDP-Opcode `R`, genau einmal per Unicast) und
  „Werksreset des Cubes“ (TCP-Opcode `a:` über die bestehende Command-Queue). Beide sind
  nur mit eigener Checkbox, sichtbarer Vorschau und der separaten Bestätigung der
  Schreibvorschau ausführbar; der Werksreset hat eine zusätzliche Warnung. Beide laufen
  über Operation Gate und Audit. Es gibt keine automatische Ausführung.

### Verification

- Build 0 Warnungen/0 Fehler; Windows und Debian 13 (WSL2) 495/495 Tests grün;
  `--selfcheck OK … 0.6.4 … SQLite ok` auf beiden Systemen und in beiden heruntergeladenen
  Release-Paketen. Release-Lauf `45792581012` und Post-Merge-CI/CodeQL grün.
- Assets: `xplat-maxcube-0.6.4-win-x64.zip` SHA-256
  `da40f485e3d070a61e99f9bc35217812013364d7f480e3fe3817f7aa27c00104`;
  `xplat-maxcube-0.6.4-linux-x64.zip` SHA-256
  `3a514e0058fd2d715cb35895e207b2132c9371e5a2ff748cbe1cfdf5792e8ba3`.
- Hardwarewirkung von `a:`/`R` bleibt `UNBEKANNT`, weil bewusst kein zerstörender Test am
  Produktiv-Cube ausgeführt wurde. Eine Avalonia-Sichtprüfung aller sechs Sprachen steht aus.

## [0.6.3] - 2026-11-26

### Fixed

- Sprachwechsel aktualisiert jetzt alle sichtbaren Bereiche, nicht nur Titel, Hilfe und
  Räume: Statusleiste, Erfolgs-/Fehler-/Warntexte, Zeitstempel des Fehlers, Dirty-Hinweis,
  Beschäftigt-Overlay, Schreibvorschau inklusive noch nicht bestätigter Aktion, Funkauslastung,
  Zeitzone, Diagnose, Fundmeldung und Profilraumbezeichnung werden in der neuen Sprache
  neu erzeugt (`MainWindowViewModel.OnLanguageChanged`, `RefreshLocalizedBindings`).
- Wochentage, Raum- und Taster-Modi, Gerätetyp/-status, Funkstatus, Entkalkungstag und
  Zeitzonen-Presets kommen aus dem Katalog statt aus `Enum.ToString()`; Auswahl und
  Domänenwerte bleiben unverändert (`EnumLabels`, `EnumOption`, `MainWindow.axaml`).
- Fehlerhinweise für FR/IT/NL/PL bleiben nicht mehr englisch: Bekannte Codes erhalten einen
  lokalisierten Hinweis, `Command discarded` einen eigenen sechssprachigen Text
  (`ErrorCatalog.FindFirstCode`, `ErrorCatalog.IsCommandDiscarded`).
- Zahlen- und Zeitformate folgen der gewählten UI-Sprache; Protokoll- und Berechnungspfade
  bleiben bewusst invariant (`Localizer.ApplyCulture`).

### Changed

- Der Selfcheck prüft jetzt in allen sechs Sprachen jeden Katalogschlüssel, nicht nur `Connect`.
- Version auf `0.6.3` (`Directory.Build.props:3`); Build 0 Warnungen/0 Fehler,
  488/488 hardwarefreie Tests grün, `--selfcheck OK` unter Windows und Debian 13 (WSL2).

## [0.6.2] - 2026-11-26

### Added

- Vorschau vor riskanten Schreibbefehlen: Wochenprofil senden/kopieren und
  Konfiguration senden/kopieren zeigen zuerst die konkreten Werte; erst nach
  „Jetzt senden“ wird ein Telegramm erzeugt. „Abbrechen“ verwirft die Vorschau
  ohne Cube-Zugriff (`MainWindowViewModel.RunAsync`, `ConfirmWriteCommand`,
  `CancelWriteCommand`, `MainWindow.axaml`).
- Sichtbarer Dirty-Hinweis für geänderte Wochenprofile und/oder Raumkonfiguration
  in der Statusleiste; erfolgreiches Senden setzt die jeweilige Basis zurück.
- Verständliche, lokalisierte Begründung, warum eine Schreibaktion gesperrt ist
  (offline, veraltet, nicht gelesen, kein Raum, kein geladenes Wochenprofil,
  keine Administration, laufende Aktion, keine Räume ausgewählt).
- Modales „Beschäftigt“-Overlay zeigt jetzt den konkreten Vorgang; alle neuen
  Texte existieren in DE/EN/FR/IT/NL/PL und die Bedienelemente haben Namen für
  Screenreader.

### Fixed

- Linux-Start: Fehlten `libfontconfig.so.1` bzw. die X11-Bibliotheken, brach
  Avalonia mit einer `TypeInitializationException` ab (`SkiaSharp.SKImageInfo`).
  `Program.cs` prüft alle elf von SkiaSharp/Avalonia.X11/Skia benötigten
  Systembibliotheken (fontconfig, sechs X11, ICE/SM, GL/Vulkan) vor dem GUI-Start
  und nennt den konkreten Installationsbefehl;
  `start.sh` prüft zusätzlich vor dem Build. `--check-linux-deps` macht die Prüfung
  auch im veröffentlichten ZIP verfügbar. Das Paket bleibt self-contained für .NET;
  Systembibliotheken müssen auf dem Zielsystem vorhanden sein (READMEs,
  `docs/HARDWARE-VERIFY.md` §14).
- Externe Links beenden die App nicht mehr: Der Klick auf den Projekt-Link rief
  `Process.Start` mit `UseShellExecute` direkt auf; ohne `xdg-open`/`gio`/`wslview`
  (in schlanken WSL-Installationen üblich) endete das in einer ungehandelten
  `Win32Exception`. Neuer `IUrlOpener`/`DesktopUrlOpener` versucht Windows-Shell,
  `xdg-open`, `gio open`, `wslview` bzw. macOS `open`; bei Fehlern erscheint die
  lokalisierte Meldung „Öffnen fehlgeschlagen – bitte manuell öffnen: {0}" mit dem
  Ziel als Statusleistentext. Auch „Protokollordner öffnen“ nutzt jetzt diesen
  Pfad; `start.sh --install-deps` installiert zusätzlich `xdg-utils`
  (`docs/HARDWARE-VERIFY.md` §15). Vom Nutzer am 26.11.2026 in WSLg bestätigt.

### Changed

- Version auf `0.6.2` (`Directory.Build.props:3`); Build 0 Warnungen/0 Fehler,
  486/486 Tests grün, `--selfcheck OK` unter Windows und Debian 13 (WSL2).
  Veröffentlicht am 26.11.2026: Tag `v0.6.2` auf `7f45d5e`, Merge in `main`
  als `04e5c63` via PR #13; Release-Workflow und alle vier Pflicht-Checks grün,
  ZIPs für `win-x64` und `linux-x64` angehängt.

## [0.6.1] - 2026-11-25

Auswertung des Kundenprotokolls vom 25.11.2026: „Call from invalid thread" beim
Cube-Reiter, bei Netzwerk-/Cube-Einstellungen und widersprüchliche Protokollzeilen.

### Fixed

- Alle UI-Mutationen laufen über `IUiDispatcher`; `ConfigureAwait(false)` wurde aus
  dem ViewModel entfernt. Damit aktualisieren Status, Audit und Lesewerte nicht mehr
  vom Threadpool (`Call from invalid thread`, 09:41/09:45 im Kundenlog).
- Erfolgreiche Aktionen werden mit dem Erfolgstext protokolliert, nicht mit dem
  Fehlerprefix (`Temperatur konnte nicht gesendet werden: Temperatur gesendet.`).
- Das Ausschalten des Protokolls wird protokolliert, bevor der Store schweigestellt
  wird; Ein/Aus/Ein erzeugt jetzt drei nachvollziehbare Zeilen.
- `Command discarded` erhält einen konkreten Hinweis (MAX1008, Funkauslastung).

### Changed

- Logzeilen tragen Thread-ID und UI-Marker, damit Supportfälle zuordenbar sind.
- `--selfcheck` prüft den Ein/Aus-Schalter des Protokolls.
- Version auf `0.6.1`.

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

[Unreleased]: https://github.com/Ebenhoeh80/xplat-maxcube/compare/v0.6.1...HEAD
[0.6.1]: https://github.com/Ebenhoeh80/xplat-maxcube/releases/tag/v0.6.1
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

