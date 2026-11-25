# Gremium für die „beste App mit allen Features“

Stand 24.11.2026. Auftrag: Die MAX!-Local-App autonom über mehrere Runden zur besten
erreichbaren lokalen App machen. Entscheidungen trifft **nur das Gremium**; die
Implementierung richtet sich nach dessen Beschlüssen.

## Randbedingungen (gelten für alle Runden)

- Cube ist Source of Truth; SQLite nur Cache/Offline-Anzeige.
- Keine Cloud-, Jetty-, Browser-, Getdown- oder AGPL-Abhängigkeit.
- Firmware-Schreiben bleibt deaktiviert (`UNBEKANNT` ohne Golden-Capture).
- Keine Hardware-, Cube-, HA- oder Netzwerk-Schreibzugriffe; keine Commits/Pushes.
- Jede Aussage ist `FAKT`, `BEOBACHTUNG`, `INFERENZ` oder `UNBEKANNT`.

## Mitglieder

1. **Produkt/UX** – Vollständigkeit des Funktionsumfangs, Bedienbarkeit, sichtbare Lücken.
2. **Technik/Protokoll** – Architektur, Protokolltreue, Robustheit, Testbarkeit.
3. **DeepSeek V4.1 Flash Free** – unabhängige Gesamtprüfung, Risiken, Einordnung der
   historischen Sonnet-Freigaben. **Modellwechsel 24.11.2026:** Sonnet 5.5 wurde auf
   Wunsch des Nutzers wegen fehlenden Guthabens durch dieses kostenlose Modell ersetzt.
   Die früheren Sonnet-Prüfungen und die Freigabe der lokalen ersten Version bleiben
   als historische `FAKT`-Belege erhalten, werden aber nicht erneut abgerufen.

**Konkrete Modelle ab Runde 2 (alle kostenlos):** Produkt/UX
`opencode/tidepool-alpha-free`, Technik/Protokoll `opencode/nemotron-3.5-lightning-free`,
unabhängige Prüfung `opencode/deepseek-v4.1-flash-free`.

## Runde 4 – Erweiterung (24.11.2026)

**Neuer Nutzerauftrag:** App-Versionierung und Autodiscovery nachrüsten; die
noch fehlenden Funktionen gegenüber dem Original mit **Priorität leicht/mittel**
(P1/P2) angehen; alles autark bis zur fertigen Umsetzung.

### Stimmen (Kurzfassung)

- **Produkt/UX** (`tidepool-alpha-free`): bestätigt Version und Auto-Discovery als
  Lücken; empfiehlt als P1/P2-Paket App-Version, **Haussteuerung** und
  **Wochenprogramm kopieren**. i18n/Inclusion bleiben zurückgestellt.
- **Technik/Protokoll** (`nemotron-3.5-lightning-free`): Discovery-Details
  (`maxcube-protocol.md:47-67`) sind belastbar; keine UDP-Implementierung im Code.
  Empfiehlt `IDiscoveryService`, Fake-Tests, zentrale Assembly-Version und
  Anzeige in der UI. Aufwand geschätzt ~5 PT.
- **Prüfung** (`deepseek-v4.1-flash-free`): 251/251 selbst nachgemessen; Doku-
  Drift `INDEX.md:20` (78 statt 61 Feature-Zeilen) und fehlende Status-Spalte in
  `features.md`. Fordert zusätzlich SQLite-Schemaversion und Persistenz des
  letzten Endpoints; Broadcast nur benutzerinitiiert, kein Cloud-Update-Check.

### Bindender Beschluss (3/3)

1. **App-Versionierung:** zentrale `<Version>` in `Directory.Build.props`;
   Fenstertitel und Status-Tab zeigen die Assembly-Version als einzige Wahrheit.
   SQLite erhält `PRAGMA user_version` plus `app.version`/`schema.version` in
   `settings`. Kein HTTP-/Cloud-Update-Check, kein Firmware-Schreiben.
2. **UDP-Autodiscovery:** `ICubeDiscovery` + UDP-Adapter nach
   `maxcube-protocol.md:47-67` (23272, `eQ3Max*\0**********I`, Broadcast +
   Multicast, 3 Pakete/50 ms, Antwort `^eQ3Max(Ap|Bl)`, Serial 8..17,
   Firmware 24..25 big-endian). Benutzerinitiierter „Cube suchen"-Button,
   Trefferliste mit Seriennummer/IP/Firmware/Bootloader, Auswahl füllt Host/Port.
   Letzter Endpoint wird persistiert; manuelle IPv4-Eingabe bleibt und wird
   oktettweise validiert. Alle Tests laufen über Fake-Socket/Parser, ohne Broadcast.
3. **P1 leicht/mittel als Mitnahmen:** **Wochenprogramm kopieren** (Tag auf alle
   Tage bzw. Raumprofil übernehmen) und **Haussteuerung** (mehrere Räume
   gemeinsam Auto/ECO/COMFORT/Temporary). Doku: Status-Spalte in `features.md`
   und Korrektur der Feature-Zahl in `INDEX.md`.

**Zurückgestellt (ausdrücklich, kein Defekt):** i18n, Inclusion/Wizard,
Strukturpflege, Netzwerk/Zeit/Firmware, Airtime/Duty-Cycle, gebündelte Kommandos,
Tray/Instanzschutz, App-Update-Check. Hardware bleibt `UNBEKANNT`.

**Abschlussfeststellung Runde 4 (FAKT):** Alle drei Beschlusspunkte sind umgesetzt.
Die Version 0.2.0 steht zentral in `Directory.Build.props`, wird über
`IAppVersionProvider` in Fenstertitel/Status angezeigt und als `PRAGMA user_version = 1`
sowie `app.version`/`schema.version` im Cache gespeichert. `ICubeDiscovery` mit
`CubeDiscoveryService` und `UdpDiscoveryTransport` sendet den 19-Byte-Request an
Broadcast/Multicast (3×/50 ms), dekodiert Seriennummer/Firmware/Bootloader und bietet
„Cube suchen", Trefferliste und Host/Port-Übernahme; der letzte Endpoint liegt in
`settings` (`cube.endpoint`). Die manuelle Host-Eingabe verlangt genau vier
IPv4-Oktette (0..255; keine .NET-Kurzformen). Haussteuerung und
„Tag auf geladene Tage kopieren“ sind verdrahtet. Build 0/0, **268/268** Tests grün (Domain 55, Application 25, Protocol 149,
Infrastructure 11, UI 28). Hardwarenutzung bleibt `UNBEKANNT`; der UDP-Adapter wurde
nur mit Fake-Transport/Parser getestet, nicht gegen einen echten Cube.

## Runde 5 – Vollständige Feature-Abbildung und Windows/Linux (24.11.2026)

**Neuer Nutzerauftrag (wörtlich):** „mach weiter. ende erst wenn du alle features der
originalapp abgebildet hast. veruche alles das ziel zu erreichen. wichtig. die app muss
unter windows und linux lauffähig sein“.

**Verfahren (FAKT):** Alle vier Gremiumsaufrufe (Produkt, Technik, Prüfung, Code-Inventar)
brachen mit „Free usage limit reached“ ab, bevor sie Ergebnisse lieferten. Es gibt deshalb
**keine Modellstimmen** für Runde 5. Der Auftrag wurde autark nach den weiterhin
bindenden Beschlüssen der Runden 1–4 und den Randbedingungen oben umgesetzt; es wurden
keine weiteren kostenpflichtigen Modelle abgerufen. Diese Lücke ist bewusst nicht durch
erfundene Stimmen gefüllt. Der Rest der Prüfung leistet die Test-/Verifikationskette.

**Beschluss (autark, mit Begründung):**

1. **Was „alle Features“ bedeutet.** Die Feature-Matrix (`docs/features.md`) hat 61 Zeilen.
   Abgebildet werden alle, die lokal möglich sind. Zehn Zeilen sind **nicht abbildbar**:
   die Cloud ist seit 01.07.2023 abgeschaltet (Fernzugriff, Produktkonto, Kontoinfo,
   Ablaufwarnung, Passwortstärke), Proxy/App-Update/Update-Splash gehören zu Cloud und
   Update-Server, die Browserwarnung zum Browser, und **Cube-Firmware schreiben** bleibt
   deaktiviert (Gerätedefekt-Risiko, keine Firmwaredatei, kein Golden-Capture). Der
   Fehlerbericht ist durch einen **lokalen Diagnosebericht ohne Versand ersetzt**.
   **Nutzerhinweis 25.11.2026 (FAKT, vom Nutzer mitgeteilt, nicht extern verifiziert):**
   eQ-3 hat die MAX!-Cloud-Steuerung abgeschaltet und nach Angabe des Nutzers auch die
   Cloud-App aus dem Android- und iOS-App-Store entfernt. Beschluss 1 ist damit zusätzlich
   gestützt: Es gibt keinen herstellerseitigen Fernzugriffsweg mehr, den die lokale App
   nachrüsten könnte.
2. **Schreibformate ohne Hardwarebeweis.** Boost/Entkalkung (0x12), Gruppen-/Linkzuordnung
   (0x20–0x23), Taster (`w:`), Zeitzone (`v:`) und Netzwerk (UDP) folgen dem Lesepfad bzw.
   der Doku (`INFERENZ`). Schutzmaßnahmen: ausdrückliche Bestätigung für jede verändernde
   Strukturaktion; Schreiben nur mit zuvor vom Cube gelesenen Rohwerten; **Rundreise-
   Sicherheitsprüfung** für Metadaten (der unveränderte Zustand muss byte-identisch
   reproduzierbar sein, sonst wird nichts gesendet); Entwurfsprinzip (Validierung, Funk,
   Metadaten, erst dann das echte Modell); Teilfehler werden auditiert und zeigen
   „Zustand neu laden“.
3. **Windows und Linux.** Ein Code, zwei Systeme: plattformneutrale Datenpfade
   (`%LOCALAPPDATA%` bzw. `$XDG_DATA_HOME`), prozessübergreifender Instanzschutz per
   exklusiver Datei, Systemsprache ohne ICU (die App läuft mit `InvariantGlobalization`),
   fensterloser `--selfcheck`, CI-Matrix Windows+Linux, Startskripte `start.cmd`/`start.sh`.
4. **Abnahme-Gate.** „Fertig“ heißt: Build 0/0, alle Tests grün unter Windows **und**
   unter echtem Linux (Debian WSL2), Selbsttest auf beiden, GUI-Start unter Windows.

## Protokoll

| Runde | Fokus | Beschluss | Umsetzung |
| --- | --- | --- | --- |
| 1 | Bestandsaufnahme + Zielbild | **Beschluss 3/3:** M-F (alle auf Platz 1), N7 (alle in Top 3), Sitzungsüberwachung (alle in Top 3) als Paket. Feedback/Gerätefilter/start.cmd als Mitnahme. | umgesetzt (243/243 Tests grün) |
| 2 | Prüfung der Runde-1-Umsetzung | **Beschluss 3/3:** Session-/Reconnect-Pfad, M-F-Ende-zu-Ende-Härtung, StatusText/Config-Mitnahmen | umgesetzt (249/249 Tests grün) |
| 3 | Abschlussprüfung | **Beschluss 3/3:** Reconnect in der UI sichtbar/verbindbar machen, Geräteliste nach Raumwechsel aktualisieren, Cancel-Race und Wording schließen | umgesetzt (251/251 Tests grün), **FREIGEGEBEN** |
| 4 | Erweiterung (Nutzerwunsch) | **Beschluss 3/3:** App-Versionierung, UDP-Autodiscovery, Haussteuerung und Wochenprogramm-Kopieren (leicht/mittel) | umgesetzt (268/268 Tests grün), hardwarefrei **FREIGEGEBEN** |
| 5 | Alle Features + Windows/Linux (Nutzerwunsch) | **Autark, ohne Modellstimmen** (alle Aufrufe am Free-Limit gescheitert): 49 von 61 Zeilen umgesetzt, 1 teilweise, 1 ersetzt, 10 nicht abbildbar; Windows+Linux belegt | umgesetzt (442/442 Tests grün unter Windows **und** Debian 13), Selbsttest beider Systeme; Linux-GUI nicht gesehen |

### Bindender Gremiumsbeschluss Runde 1 (24.11.2026)

Alle drei Mitglieder nennen **M-F, N7 und Sitzungsüberwachung** in ihren Top 3; M-F ist
einstimmig Platz 1. DeepSeek schiebt die Sitzungsüberwachung vor N7, Produkt/UX und
Technik haben N7 auf Platz 2. Der Gremiumsbeschluss implementiert deshalb **alle drei
im Paket** und priorisiert innerhalb des Pakets nach Abhängigkeit: M-F → N7 →
Sitzung (N7 ist Voraussetzung für sauberes Reconnect). Kein Mitglied hat Inclusion
oder i18n in die Top 3 gesetzt; diese bleiben für Runde 2/3 zurückgestellt.

**Mitnahmepunkte (einstimmig unstrittig, geringer Aufwand):**
Geräteliste auf den gewählten Raum filtern, Erfolgsmeldung nach Senden, `start.cmd`
baut nach Codeänderungen neu, Doku-Ungenauigkeit „Audit auf 500 begrenzt“ korrigieren.

### Runde 3 – Stimme Technik/Protokoll (`opencode/nemotron-3.5-lightning-free`)

**FREIGEGEBEN.** Bestätigt: N3 (`Fault()` vor `Fail()`) im Code und im neuen Test,
`RefreshInterval` entfernt, volle 7-Tage-Dekodierung belegt. Risiken aus Sicht dieses
Mitglieds: (1) Reconnect nach `SessionUnreachable` (HIGH), (2) Monitor-Task-Race
nach `MarkUnreachableAsync` (MEDIUM), (3) inkonsistenter `_monitor`-Reset im
Gate-Timeout-Pfad (MEDIUM). Empfehlung: Max-Retry-Limit im Reconnect-Loop.

**Gegenprüfung der Implementierung (FAKT):**
- Der gesehene Teststand 243/243 ist veraltet; aktuell **249/249** grün, Build 0/0.
- Der Reconnect-Test `An_unreachable_session_can_reconnect_without_an_explicit_disconnect`
  wurde in dieser Sitzung **zuerst rot ausgeführt**: `InvalidOperationException:
  Already connected. Disconnect first.` an `CubeGateway.cs:74`. Nach dem Runde-2-Fix
  ist er grün (Infrastructure 10/10). Der Befund ist damit behoben und **belegt**,
  nicht `UNBEKANNT`.
- Das endlose Retry ist **bewusster Runde-1-Beschluss** (Backoff 1…30 s): die App
  soll nach Rückkehr des Cubes automatisch wieder online gehen. Ein Max-Retry-Limit
  würde genau das verhindern; kein Defekt.
- Der Monitor-"Race" ist durch `StopMonitorAsync` mit `CleanupTimeout` (500 ms)
  begrenzt und exception-fest (`CubeGateway.cs:340-346`); `ConnectAsync` wartet
  vor dem Neustart darauf (`:81`). Der Prüfer räumt selbst ein, dass der Pfad
  technisch korrekt ist. Kein weiterer Fix nötig.

### Runde 3 – Bindender Beschluss (3/3)

Zwei Stimmen sagen `FREIGEGEBEN`, die unabhängige Prüfung `NICHT FREIGEGEBEN`.
Nach Gegenprüfung am Code wird der Prüferfund als **echter, hardwarefreier
Blocker** eingestuft: Nach automatischem Reconnect bleibt die UI „getrennt\",
weil `OnConnectionChanged` nur `Faulted` verarbeitet, während der Service über
`OnGatewayStateChanged` auch `Connected` meldet (`CubeApplicationService.cs:84-85`,
`MainWindowViewModel.cs:417-427`). Damit bleibt `IsConnected` false, alle
Aktionsbuttons sind gesperrt und ein erneuter Connect läuft in „Already
connected" (`CubeGateway.cs:73-75`).

**Bindender Beschluss (vor endgültiger Freigabe umzusetzen):**
1. `OnConnectionChanged` auf `Connected` reagieren lassen: `IsConnected = true`,
   Status „Verbindung wiederhergestellt" und `StatusText` nicht mehr als
   „Getrennt" setzen. Regressionstest über den echten Service und ein Fake-Gateway,
   das `Connected` feuert.
2. `OnSelectedRoomChanged` filtert `Devices` neu; Regressionstest für den
   Gerätewechsel beim Raumwechsel.
3. Cancel-Race schließen (Status vor dem Cancel setzen) und Stale-Wortlaut in
   XAML an den StatusText angleichen.

Zurückgestellt bleibt ausdrücklich das Max-Retry-Limit (bewusster Runde-1-Beschluss:
endloses Backoff 1…30 s), i18n, Inclusion und Audit-Retention. Hardware bleibt
`UNBEKANNT`.

**Abschlussfeststellung (FAKT):** Alle drei Punkte sind umgesetzt. `OnConnectionChanged`
setzt bei `Connected` nun `IsConnected = true` und „Verbindung wiederhergestellt"
(`MainWindowViewModel.cs:420-447`); `OnSelectedRoomChanged` ruft `RefreshDevices`
(`:539-553`); der Cancel-Hinweis steht vor dem Abbruch (`:378-390`) und das XAML-Wortlaut
ist angeglichen (`MainWindow.axaml:139`). Neue Regressionstests:
`Automatic_reconnect_is_visible_in_the_status` und
`Selecting_another_room_refreshes_the_device_list`
(`tests/MaxCube.UI.Tests/ViewModelTests.cs:200-234`). Build 0/0, **251/251** grün
(Domain 55, Application 21, Protocol 149, Infrastructure 10, UI 16). Damit ist die
Abschlussbewertung **FREIGEGEBEN** – für den hardwarefreien, lokalen Funktionsumfang.
Hardwarenahe Aussagen bleiben `UNBEKANNT`.

### Runde 3 – Stimme Prüfung (`opencode/deepseek-v4.1-flash-free`)

**NICHT FREIGEGEBEN.** Selbst nachgemessen: Build 0/0, **249/249** Tests grün
(Domain 55, Application 21, Protocol 149, Infrastructure 10, UI 14); Doku-Zahlen
stimmen. Delta Runde 2 auf Gateway-/Domänen-/Testebene vollständig belegt.
Neuer Fund: Auto-Reconnect kommt in der UI nicht an (`IsConnected` bleibt false,
Buttons gesperrt, Connect → „Already connected"). Geräteliste nach Raumwechsel
falsch. Niedrig: Cancel-Race, Wording-Diskrepanz, `UnreachableTimeout` über
Monitor-Loop faktisch unerreichbar (schon in `gremium.md:120-121` dokumentiert).
Empfehlung: Fix plus je einen Regressionstest, dann Freigabe ohne erneute
Architekturprüfung.

### Runde 3 – Stimme Produkt/UX (`opencode/tidepool-alpha-free`)

**FREIGEGEBEN.** Die Runde-2-Mitnahmen sind im Code belegt (`Cancel`-Restore
`MainWindowViewModel.cs:358-386`, Test `ViewModelTests.cs:177-198`; Stale-Text
`MainWindowViewModel.cs:402-415`; Tagessperre `:151`). Drei restliche Punkte:

1. **Geräteliste beim Raumwechsel** (INFERENZ): `OnSelectedRoomChanged`
   (`MainWindowViewModel.cs:515-525`) aktualisiert Sollwert und Wochenprofil, aber
   nicht `Devices`; die Liste wird nur in `UpdateRooms` (`:475-480`) gefüllt.
2. **Cancel-Race** (INFERENZ): `StatusText = "Abbrechen …"` steht erst nach
   `_actionCts.Cancel()`/`CancelActiveOperation()` (`:383-385`). Beendet die Aktion
   dazwischen, läuft der `finally`-Restore (`:359`) ins Leere und der Text bleibt.
3. **Wording** (BEOBACHTUNG): Status „Verbindung verloren – Reconnect läuft …"
   vs. XAML „Zustand veraltet – reconnect läuft …" (`MainWindow.axaml:139-140`).

**Gegenprüfung (FAKT):** Alle drei Punkte sind am aktuellen Code nachvollziehbar;
sie sind klein, hardwarefrei und sollten vor dem endgültigen Abschluss mit erledigt
werden. i18n/Inclusion bleiben wie beschlossen zurückgestellt.

### Runde 2 – Stimme Produkt/UX (`opencode/tidepool-alpha-free`)

Kernpunkte: M-F/N7/Sitzung sind aus Sicht der Bedienbarkeit weitgehend umgesetzt.
Top 3: (1) `RefreshInterval` aktiv nutzen, (2) `StatusText` nach Abbruch zurücksetzen
(bleibt „Abbrechen …“), (3) Stale/Reconnect auch im Statustext sichtbar machen.

**Gegenprüfung der Implementierung (FAKT):**
- Der Status-Text-Fund ist berechtigt: `Cancel()` setzt `MainWindowViewModel.cs:371`
  „Abbrechen …“, `RunAsync` stellt es im `finally` (`:354-358`) nicht wieder her.
- Die Begründung zu (1) ist **nicht** zutreffend: `CubeSessionMonitor.RunAsync`
  (`CubeSessionMonitor.cs:66-77`) ruft `onWatchdog` bei `Idle` (Standard 30 s) auf,
  und `OnWatchdogAsync` refreshmt (`CubeGateway.cs:265`). Der Refresh kommt also nicht
  erst nach 900 s. Richtig ist aber: die neu eingeführte Option `RefreshInterval`
  (`CubeConnectionOptions.cs:67-71`) wird nirgends ausgewertet – sie ist eine
  irreführende Config und muss entweder verdrahtet oder entfernt werden.

### Runde 2 – Stimme Technik/Protokoll (`opencode/nemotron-3.5-lightning-free`)

Vollbericht: `gremium-bericht-round2.md`. Kernpunkte: M-F/N7/Sitzung sind technisch
solide umgesetzt; Top 3 aus Sicht dieses Mitglieds: (1) alten Monitor-Task vor neuem
Connect explizit abwarten, (2) `Fault()`/`Fail()` atomar machen (N3), (3)
`Unreachable`-Kopplung des Monitors explizit machen.

**Gegenprüfung der Implementierung (FAKT):**
- Der gesehene Teststand 232/232 ist veraltet; aktuell sind **243/243** grün
  (Domain 55, Application 20, Protocol 147, Infrastructure 9, UI 12).
- N3 ist behoben: `CubeCommandQueue.cs:276-279` ruft zuerst `Fault(failure)` und erst
  danach `Fail(failure)`. Die Befürchtung des Prüfers ist durch den Code widerlegt;
  ein eigener Test dafür fehlt allerdings tatsächlich.
- Der Hinweis auf ein Monitor-Fenster ist **teilweise berechtigt**: `DisconnectAsync`
  wartet im Normalpfad auf `StopMonitorAsync` (`CubeGateway.cs:189`), aber im
  Gate-Timeout-Pfad (`CubeGateway.cs:174-185`) werden `_queue`, `_transport` und
  `_monitor` nicht zurückgesetzt. Ein anschließendes `ConnectAsync` scheitert dort an
  `CubeGateway.cs:72-75` („Already connected“). Das ist ein echter Runde-3-Kandidat.
- **Zusätzliche Implementierungsprüfung (FAKT, kein Gremiumsbeleg):** Der relevantere
  Pfad ist der Live-Refresh: Nach drei fehlgeschlagenen `l:`-Abfragen setzt
  `RefreshAsync` den Zustand auf `Faulted` und feuert `SessionUnreachable`
  (`CubeGateway.cs:300-308`), lässt aber `_queue`/`_transport` stehen. Der
  anschließende Reconnect ruft `CubeGateway.ConnectAsync` auf, das wegen
  `CubeGateway.cs:72-75` („Already connected. Disconnect first.“) scheitert. Der
  Reconnect-Loop (`CubeApplicationService.cs`, `ReconnectLoopAsync`) fängt den Fehler
  und versucht es mit Backoff erneut – er kommt so nie wieder online. Das muss in
  Runde 3 behoben und mit einem Test belegt werden.

### Runde 2 – Stimme unabhängige Prüfung (`opencode/deepseek-v4.1-flash-free`)

Kernpunkte: 243/243 grün und 0/0 Build sind selbst nachgemessen; `README.md:8` nennt
noch 232/232 (Doku-Drift). Top 3: (1) Gateway-Session-/Reconnect-Pfad schließen,
(2) M-F Ende-zu-Ende härten, (3) i18n.

**Gegenprüfung der Implementierung (FAKT):**
- Der stärkste Fund ist testbelegt: `CubeGatewayTests.An_unreachable_session_can_reconnect_without_an_explicit_disconnect`
  scheitert mit `InvalidOperationException: Already connected. Disconnect first.`
  (`CubeGateway.cs:74`). Nach `SessionUnreachable` bleibt `_queue` stehen; der
  Reconnect-Loop im Service kommt nie wieder online.
- Der `Unreachable`-Zweig des Monitors ist toter Code (`CubeSessionMonitor.cs:69-77`
  kennt keinen solchen Fall), die Erkennung hängt allein an drei fehlgeschlagenen `l:`.
- `HasWeekProfile` gilt pro Raum, nicht pro Tag (`Room.cs:68`). Ein vom Decoder
  verworfener Tag (`WeekProfileDecoder.cs:78-85`) ist im Profil einfach abwesend; ein
  sendbares Default-Tagesprogramm würde das echte Heizprogramm überschreiben.
- Der Audit-Retention-AC (`roadmap.md:177`) ist weiterhin unerfüllt; das bleibt
  dokumentierte Einschränkung, kein Runde-2-Beschluss.

### Bindender Gremiumsbeschluss Runde 2 (24.11.2026)

Alle drei Stimmen setzen den **Session-/Reconnect-Pfad** auf Platz 1 (Produkt/UX:
„Reconnect im Statustext", Technik: „Monitor-Task abwarten", Prüfung: „Pfad schließen").
Der Gremiumsbeschluss daraus, in dieser Reihenfolge:

1. **Session-/Reconnect-Pfad schließen.** Beim `Unreachable`-Zweig und nach drei
   fehlgeschlagenen `l:`-Abfragen werden Queue, Transport, Monitor und Monitor-Task
   begrenzt freigegeben; `ConnectAsync` wartet vor dem Neustart auf den alten Task.
   Auch der Gate-Timeout-Pfad von `DisconnectAsync` räumt auf. Regressionstest mit
   echtem `CubeGateway` und `ScriptedTransport` ist Bestandteil des Beschlusses.
2. **M-F Ende-zu-Ende härten.** Sendesperre auf den konkreten Wochentag, nicht nur auf
   den Raum: ein Tag, den der Decoder verworfen hat, darf nicht gesendet werden.
   Zusätzlich Assembler-Test mit vollständigem 211-Byte-C:-Block und N3-Test
   (`Fault()` vor `Fail()`).
3. **Mitnahmen aus Produkt/UX und Prüfung.** `StatusText` nach Abbruch wiederherstellen;
   `RefreshInterval` entfernen statt verdrahten (`FAKT`: `Validate` erzwingt
   `IdleTimeout <= RefreshInterval`, der Idle-Watchdog refreshed also bereits früher –
   ein zweiter Timer würde denselben `l:` nur duplizieren); Stale/Reconnect im
   StatusText sichtbar machen; veraltete Testzahl in `README.md` korrigieren.

**Zurückgestellt (ausdrücklich nicht beschlossen):** i18n erhielt nur eine von drei
Stimmen (Prüfung, Platz 3); Produkt/UX und Technik setzten andere Themen vor.
Inclusion bleibt Platz 4. Korrektheit der lokalen Verbindung und Schutz vor dem
Überschreiben des Heizprogramms gehen vor Lokalisierung. Audit-Retention bleibt
als dokumentierte Einschränkung bestehen.

**Umsetzungsstand (24.11.2026, FAKT):** Build 0/0, **249/249** Tests grün
(Domain 55, Application 21, Protocol 149, Infrastructure 10, UI 14). Der ehemals
rote Reconnect-Test ist jetzt grün; neu sind außerdem Tests für die Tagessperre,
die vollständige C:-Kette, den N3-Reihenfolge-Fund und den StatusText nach Abbruch.

**Nicht Gremiumsstimme:** Die noch laufende Sonnet-Antwort kam nach dem Modellwechsel
zurück und zählt nicht. Zwei ihrer Belege wurden selbst verifiziert und sind als
`FAKT` in die Mitnahmepunkte eingegangen: Geräteliste ohne Raumfilter
(`MainWindowViewModel.cs:369-373`) und blockierendes `DisposeAsync().GetResult()`
beim Beenden (`App.axaml.cs:61`).
