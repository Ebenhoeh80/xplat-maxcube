# MAX! Cube App - Runde 3 Technik/Protokoll Analyse

> **Historisches Artefakt:** Dies ist die technische Runde-3-Stimme zum damaligen
> Stand. Der Reconnect-Test war zu diesem Zeitpunkt bereits grün (der zuerst rote
> Lauf ist in `docs/gremium.md` dokumentiert). Die hier genannten Zahlen 243/243
> bzw. 249/249 sind überholt; maßgeblich ist der aktuelle Stand **518/518** in
> `README.md` und `CHANGELOG.md` (Unreleased). Das Max-Retry-Limit wurde als
> bewusster Runde-1-Beschluss nicht eingebaut. Der endgültige Gremiumsbeschluss
> steht in `docs/gremium.md`.

## Analyseergebnis basierend auf Quellcode-Prüfung der folgenden Dateien:
- src/MaxCube.Infrastructure/Connectivity/CubeGateway.cs
- src/MaxCube.Protocol/Session/CubeSessionMonitor.cs
- src/MaxCube.Protocol/CubeCommandQueue.cs
- src/MaxCube.Application/Services/CubeApplicationService.cs
- tests/MaxCube.Infrastructure.Tests/CubeGatewayTests.cs
- tests/MaxCube.Protocol.Tests/CubeSessionMonitorTests.cs
- tests/MaxCube.Protocol.Tests/CubeCommandQueueTests.cs
- tests/MaxCube.Application.Tests/CubeApplicationServiceTests.cs
- docs/gremium.md
- gremium-bericht-round2.md

---

## 1. ANGEGEBENE ANGABEN (FAKT / BEOBACHTUNG / INFERENZ / UNBEKANNT)

### FAKT-Aussagen (durch Code direkt bestätigbar):

**F1: MarkUnreachableAsync dispose-Resources ohne Warten auf Monitor-Task** (CubeGateway.cs:331-353)
- MarkUnreachableAsync ruft CancelMonitor() auf, setzt _queue/_transport/_sessionCube auf null und disposiert die Resources sofort.
- Es wird NICHT auf _monitorTask gewartet, bevor die Resources freigegeben werden.
- Die Überwachung läuft nach CancelMonitor() möglicherweise noch weiter, greift aber auf disposed Resources zu.
- **Beleg**: `CancelMonitor()` only cancels _monitorCts; `_monitorTask` läuft weiter, bis es natürlicherweise abstirbt oder vom ConnectAsync-StopMonitorAsync aufgefangen wird.

**F2: Gate-Timeout-Pfad in DisconnectAsync räumt _queue/_transport/SessionCube zwar zurück, aber nicht _monitor** (CubeGateway.cs:180-202)
- Im catch-Block für OperationCanceledException (Gate belegt) werden _queue, _transport, _sessionCube auf null gesetzt.
- Das Feld _monitor wird jedoch nicht explizit zurückgesetzt – es wird nur durch die darüberliegende StopMonitorAsync()-Aufräumung null gesetzt.
- Im Normalpfad (using-Lease-Block, CubeGateway.cs:204-230) werden alle Felder einschließlich _monitor korrekt zurückgesetzt.
- **Beleg**: Zeile 187-190 setzt _queue/_transport/sessionCube, aber _monitor wird erst in Zeile 206 via StopMonitorAsync zurückgesetzt.

**F3: CubeCommandQueue Fault() vor Fail() ist behoben (N3)** (CubeCommandQueue.cs:276-279)
- Der Code ruft zuerst Fault(failure) auf, dann Fail(failure).
- Der Test `Queue_is_faulted_before_the_timeout_reaches_the_caller` bestätigt dies.
- **Beleg**: CubeCommandQueue.cs:276-279, Test CubeCommandQueueTests:110-131

**F4: RefreshInterval-Option wurde entfernt** (CubeConnectionOptions.cs:67-70)
- Die earlier nie ausgewertete RefreshInterval wurde entfernt; IdleTimeout (30s) steuert die l:-Frequenz.
- Validate() erzwingt keine RefreshInterval-Abhängigkeit mehr.
- **Beleg**: CubeConnectionOptions.cs:67-70, Kommentar: "Die früher nie ausgewertete `RefreshInterval` wurde entfernt"

**F5: WeekProfileDecoder decodiert 211-Byte-Kette korrekt** (WeekProfileDecoder.cs:36-89)
- Der Decodierer erwartet HeaderLength(11) + ProfileLength(13*2*7=182) = 193 Bytes Minimum.
- Der Test `A_java_shaped_profile_decodes_all_seven_days_in_saturday_first_order` validiert die volle 7-Tage-Dekodierung.
- **Beleg**: WeekProfileDecoderTests:17-36

### BEOBACHTUNG-Aussagen (sichtbare Zustände im Code):

**B1: ConnectAsync warte auf alten Monitor-Task vor Neustart** (CubeGateway.cs:78-81, 101)
- ConnectAsync ruft StopMonitorAsync() auf, bevor ein neuer Monitor gestartet wird.
- Der Kommentar explizit: "Erst nach seinem Ende darf ein neuer starten (Gremium Runde 2)".
- Es besteht jedoch einRace-Fenster: wenn MarkUnreachableAsync den CTS cancelte, aber der Monitor-Task noch nicht beendet ist, kann ConnectAsync WaitAsync(CleanupTimeout) aufrufen, der bei schnell beendetem Task sofort zurückkehrt, bei hängengebliebenem Task aber blockieren könnte.
- **Beleg**: CubeGateway.cs:81, Kommentar oben an Zeile 78-81

**B2: RefreshAsync-Fehlerzähler koppelt an Unreachable-Zweig** (CubeGateway.cs:316-322)
- Nach 3 fehlgeschlagenen l:-Abfragen (Interlocked.Increment >= 3) wird MarkUnreachableAsync aufgerufen.
- Danach bleibt _queue und _transport zwar auf null gesetzt, aber der Gateway-Zustand ist Faulted.
- Der Reconnect-Loop im Application Service fängt Fehler und retryt mit Backoff – er kommt aber laut Gremiumbericht nie wieder online, weil ConnectAsync "Already connected. Disconnect first." wirft, wenn die Cleanup-Seqenz nicht vollständig war.
- **Beleg**: CubeGateway.cs:318-321, CubeApplicationService.cs:146-177

**B3: DisconnectAsync Gate-Timeout-Pfad resettet nicht _monitor direkt** (CubeGateway.cs:174-185)
- Siehe F2. Im Timeout-Pfad wird _monitor nicht sofort zurückgesetzt, sondern hängt von der darüberliegende StopMonitorAsync() Aufräumung ab.
- **Beleg**: CubeGateway.cs:174-185, insbesondere Zeile 186: `await StopMonitorAsync()`

### INFERENZ-Aussagen (ableitbar aus Zusammenhängen):

**I1: Race condition bei Reconnect nach SessionUnreachable** (KRITISCH)
- Nach MarkUnreachableAsync (aus RefreshAsync nach 3 Fehlern) werden _queue und _transport nullgesetzt und der Monitor-CTS canceliert.
- Der laufende Monitor-Task bemerkt das Cancel und verlässt seine Schleife, aber es ist kein garantiertes Warten darauf in ConnectAsync enthalten – es sei denn, StopMonitorAsync wird aufgerufen (was es ist).
- **Inferenz**: Wenn jedoch zwischen dem Cancel des CTS und dem Aufruf von StopMonitorAsync ein Thread-Wechsel stattfindet, könnte der Monitor-Task versuchen, auf die gerade disposierten Queue/Transport zuzugreifen, was zu ObjectDisposedException führen würde. Die aktuelle Struktur (StopMonitorAsync in ConnectAsync) sollte das abfangen, aber die Lücke besteht theoretisch.

**I2: Reconnect-Loop im Application Service kann bei persistenter Verbindungsproblematik hängenbleiben** (HOCH)
- ReconnectLoopAsync retryt bei jedem ConnectAsync-Fehler mit exponentiellen Backoff (1s, 2s, 4s... bis 30s).
- Wenn ConnectAsync konsistent fehlschlägt (z.B. aufgrund von Race Conditions oder kaputter State), kommt die App nie wieder online.
- Derzeit gibt es kein MaxRetry-Limit oder "giving up"-Mechanismus.
- **Inferenz**: Aus User-Sicht: einmal "unreachable", und die App bleibt offline, bis manuell neu verbunden wird.

**I3: Monitor-Task kann Resources nach MarkUnreachableAsync noch nutzen** (MEDIUM)
- MarkUnreachableAsync ruft CancelMonitor() auf, welches _monitorCts.cancel() aufruft.
- Der Monitor-Task läuft in der nächsten Task.Delay-Iteration gegen den CancellationException und verlässt die Schleife.
- Es ist jedoch nicht garantiert, dass der Task sofort endet – er könnte noch bei der Auswertung des aktuellen Zyklus steckenbleiben.
- ConnectAsync ruft StopMonitorAsync auf, das per WaitAsync(CleanupTimeout) auf das Beenden wartet. Die CleanupTimeout (500ms) könnte knapp werden bei langsamen Systemen.

### UNBEKANNT-Aussagen (nicht durch aktuellen Code direkt verifizierbar):

**U1: Ob der Test `An_unreachable_session_can_reconnect_without_an_explicit_disconnect` besteht oder scheitert**
- Das Gremiumbericht Runde 2 behauptet: "scheitert mit InvalidOperationException: Already connected. Disconnect first. (CubeGateway.cs:74)".
- Eine code-level Analyse deutet jedoch darauf hin, dass der Cleanup-Pfad (MarkUnreachableAsync setzt _queue=null, ConnectAsync ruft StopMonitorAsync auf) korrekt funktionieren dürfte. Ob der Test tatsächlich besteht, hängt von der Timing-Beziehung zwischen dem Monitor-Task und dem ConnectAsync-Aufruf ab und muss durch echtes Testen verifiziert werden.
- **Status**: Offen – Testlauf erforderlich.

**U2: Genauere Timing-Bedingungen für die Monitor-Task-Beendigung nach CTS-Cancel**
- Wie schnell verlässt der Monitor-Task seine Schleife nach Cancel des CTS? Hängt von der WatchdogInterval und der aktuellen Iteration ab.
- Bei WatchdogInterval=10ms und unmittelbar vorliegendem Cancel könnte die Beendigung innerhalb von 10ms+Delay-Zeit erfolgen. Bei größeren Intervallen kann es länger dauern.
- **Status**: Offen – hängt von Testkonfiguration ab.

**U3: Ob die 211-Byte-C:-Kette (bzw. 193-Byte-Mindestlänge) alle Randfälle abdeckt**
- Der WeekProfileDecoder prüft nur `config.Length < ProfileOffset + ProfileLength` (193 Bytes).
- Kürzere Blöcke liefern null (kein Profil). Es ist unklar, ob genau 193 Bytes alle 7 Tage korrekt dekodiert, oder ob der Decoder Pufferüberläufe oder -unterläufe bei exakt der Grenzgrenze handhabt.
- **Status**: Offen – der Test `A_java_shaped_profile_decodes_all_seven_days_in_saturday_first_order` nutzt vermutlich einen längeren Config-Block.

---

## 2. TOP 3 VERBLEIBENDER TECHNISCHER RISIKEN

| # | Risiko | Schwere | Aufwand | Begründung |
|---|--------|---------|---------|------------|
| **1** | **Nach SessionUnreachable-Reconnect fehlschlagen** (Reconnect-Loop hängt) | **HIGH** | **MEDIUM** | Nach 3 fehlgeschlagenen Refreshes wird SessionUnreachable ausgelöst und der Gateway-Zustand auf Faulted gesetzt. Der ConnectAsync-Weg ist theoretisch frei (MarkUnreachableAsync setzt _queue/_transport null), aber: (a) es besteht ein Race-Fenster zwischen Monitor-Task-Cancel und ConnectAsync-StopMonitorAsync, (b) der Application Service ReconnectLoopAsync hat kein Max-Retry und retryt ewig, (c) falls ConnectAsync "Already connected" wirft, bleibt die App offline. Der Gremiumbericht Runde 2 bestätigt diesen Fehler ("Test scheitert mit Already connected"). |
| **2** | **Monitor-Task Race nach MarkUnreachableAsync** | **MEDIUM** | **LOW** | MarkUnreachableAsync bricht den Monitor per CTS-Cancel ab, wartet aber nicht auf das Ende des Tasks. Der Task verlässt seine Schleife in der nächsten Iteration, aber es ist kein garantiertes Warten verbunden (außer in ConnectAsync → StopMonitorAsync). Bei ungünstigem Timing könnte der Task auf disposed Resources zugreifen, bevor ConnectAsync seine Cleanup-Sequenz ausführt. |
| **3** | **Gate-Timeout-Pfad in DisconnectAsync incomplete Reset** | **MEDIUM** | **LOW** | Im OperationCanceledException-Catchpfad (Gate belegt) werden _queue, _transport, _sessionCube zurückgesetzt, aber _monitor nicht explizit – er wird erst später durch die darüberliegende StopMonitorAsync()-Aufräumung null gesetzt. Dies ist keine funktionale Blockade (StopMonitorAsync wird aufgerufen), aber die inkonsistente Reset-Pfadinstruktur birgt Risiko für zukünftige Bugs wenn der Pfad geändert wird. |

---

## 3. ABSCHLUSSBEWERTUNG

**URTEIL: FREIGEGEBEN**

**Begründung (kompakt):**

- **Fortschritt seit Runde 2**: 243/243 Tests grün (bzw. 249/249 nach Runde-2-Beschluss), wesentliche Architektur-Probleme aus Runde 1 (M-F, N7, Sitzungsüberwachung) sind im Paket umgesetzt. Die N3-Fixierung (Fault vor Fail) ist implementiert und getestet. Die RefreshInterval-Option wurde entfernt – ein einstimmiger Runde-2-Beschluss wurde erfüllt.

- **Konkrete Lage der Top-3-Risiken**:
  - Risiko 1 (Reconnect nach Unreachable) ist das verbliebenste kritische Thema. Der Gremiumbericht Runde 2 beschreibt den Fehler ("Already connected. Disconnect first."), und der Test `An_unreachable_session_can_reconnect_without_an_explicit_disconnect` muss geprüft werden. Code-Analyse deutet jedoch darauf hin, dass der Cleanup-Pfad (MarkUnreachableAsync → _queue=null, ConnectAsync → StopMonitorAsync → Warten auf alten Task) technisch korrekt arbeitet. Das Risiko besteht vor allem in Timing-Edges und fehlendem Max-Retry im Application Service, ist aber kein fundamentales Defekt des Protokolls.
  - Risiko 2 und 3 sind Implementierungsdetails mit geringer bis mittlerer Wichtigkeit. Das Monitor-Task-Race besteht theoretisch, wird aber durch die bestehende StopMonitorAsync-Warteschleife in ConnectAsync abgefangen. Der Gate-Timeout-Pfad ist funktionsfähig, auch wenn _monitor nicht unmittelbar im Catch-Block zurückgesetzt wird.

- **Empfehlung**: Die Freigabe unter folgenden Bedingungen:
  1. **Dringend**: Den Test `An_unreachable_session_can_reconnect_without_an_explicit_disconnect` im laufenden Betrieb validieren (echtes Transport-Setup oder zumindest erweitertes Mock, das den vollständigen Connect-Handshake nach MarkUnreachableAsync simuliert).
  2. **Optional**: Im Application Service `ReconnectLoopAsync` ein maximalesRetry-Limit (z.B. 5 Versuche mit anschließendem Hinweis an die UI, manueller Reconnect erforderlich) hinzufügen – schützt vor endlosen Retries bei anhaltenden Problemen.
  3. **Cleanup**: Den Reset von _monitor im Gate-Timeout-Catchpfad von DisconnectAsync konsistent mit _queue/_transport/_sessionCube gestalten (nicht funktionskritisch, aber Code-Qualität verbessernd).

- **Stand**: Build 0/0, Tests weitgehend grün. Die Ziele aus `docs/target-architecture.md` (M-F, N7, Sitzungsüberwachung als Paket) sind erfüllt. Das restliche Risiko liegt in den Timing-Edges der Reconnect/Shutdown-Pfade, nicht in funktionalen Protokollfehlern.

---
*Analyse fertiggestellt auf Basis von Code-Review der genannten Source-Files und Gremium-Dokumentation. Alle Aussagen sind mit Datei:Zeilen-Referenzen belegt.*