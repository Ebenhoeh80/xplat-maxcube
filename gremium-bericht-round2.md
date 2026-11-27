# Gremiumsbericht: Technik/Protokoll – Runde 2

**Sitzung:** Runde 2 – Prüfung der Runde-1-Umsetzung  
**Rolle:** Technik/Protokoll  
**Bestandskatalog:** `docs/gremium.md`, `docs/target-architecture.md`, `docs/maxcube-protocol.md`, `docs/roadmap.md`  
**Quellverzeichnis:** `src/` und `tests/`  
**Datum:** 24.11.2026  

> **Historisches Artefakt:** Dieser Bericht dokumentiert die Bewertung zu Beginn von
> Runde 2. Die damaligen Zahlen (232/232) sind überholt; maßgeblich sind der
> Abschlussstand der Runden 1–3 war **251/251**, später **442/442**; maßgeblich
> ist der aktuelle Stand **553/553** in `README.md` und `CHANGELOG.md` (Abschnitt 0.8.0). Die darin
> beschriebenen Funde wurden in den Runden 2 und 3 abgearbeitet.

> **Randbedingungen (aus `docs/gremium.md`):** Cube ist Source of Truth; SQLite nur Cache. Keine Cloud/, Jetty/, Browser-, AGPL-Abhängigkeit. Firmware-Schreiben deaktiviert. Keine Hardware-/Netzwerk-Schreibzugriffe. Keine Commits/Pushes. Ohne echte Hardware sind alle `Hardware`-Fragen `UNBEKANNT`.

---
## 1. Aussagen als FAKT / BEOBACHTUNG / INFERENZ / UNBEKANNT

### WeekProfileDecoder und ApplyWeekProfile (M-F)

- **FAKT**: `WeekProfileDecoder.Decode()` decodiert das Wochenprofil aus dem C:-Konfigurationsblock. Offset 11, 13 Einträge pro Tag zu je 2 Bytes, Java-lesen Samstag-first, C#-Mapping Montag-first. Liefert `null`, wenn Block zu kurz ist. Unplausible Einzelfälle werden verworfen, ohne Connect zu faulten.  
  *Beleg:* `src/MaxCube.Protocol/State/WeekProfileDecoder.cs:36-89`

- **FAKT**: `ApplyWeekProfile` wird in `CubeStateAssembler.ApplyConfigurations()` aufgerufen (Zeile 137–141), nachdem das C:-Block analysiert wurde. Wochenprofil wird nur dann an den Cube weitergegeben, wenn `weekProfile is not null`.  
  *Beleg:* `src/MaxCube.Protocol/State/CubeStateAssembler.cs:137-141`

- **FAKT**: Wochenprofil-Decoder testbar: 5 Tests in `WeekProfileDecoderTests.cs` decken Formate mit 2 Schaltpunkten, Ignorieren nach Mitternacht, Off/On-Wire-Werte und inkomplette Blöcke ab.  
  *Beleg:* `tests/MaxCube.Protocol.Tests/WeekProfileDecoderTests.cs:1-101`

- **BEOBACHTUNG**: Java `RawCubeState` füllt fehlende Blöcke still mit leeren Arrays (`Metadata.java:43-46`), während die C#-Implementation `RawCubeState` bei abweichendem `count` `CubeCommunicationFailedException` wirft. Dies ist eine bewusste Entscheidung von Runde 1 (AP-1.2-Entscheidung), um "UNBEKANNT" nicht stillschweigend durchzureichen.

- **INFERENZ**: Ob `ApplyWeekProfile` nach Reconnect korrekt neu aufgebaut wird: Nach Reconnect wird der Cube-Zustand aus `L:/C:/M:` neu assembled (Quelle: Target Architecture §4.1 Zeile 96). Das Wochenprofil liegt dann im `RawCubeState.Metadata` und wird von `MetadataDecoder.Decode()` in Domänenobjekte überführt. Ob der `WeekProfileDecoder` dabei identisch konsistente Ergebnisse liefert wie beim ersten Connect, ist **nicht explizit geprüft** – es wird jedoch angenommen, dass gleiche C:-Daten gleiche Decoder-Ergebnisse liefern.

### OperationGate / CancelActive / Timeouts und asynchrones Shutdown (N7)

- **FAKT**: `OperationGate` ( `src/Application/Abstractions/OperationGate.cs:1-73` ) ist ein SemaphoreSlim-basiertes Tor mit `EnterAsync`, `CancelActive` und `Release`. `AcquireTimeout` = 2 s, `ShutdownAcquireTimeout` = 250 ms. App- und Gateway-Instanz sind jeweils separat, damit ein blockiertes Gateway-Gate das App-Gate nicht mitblockiert.

- **FAKT**: `CubeGateway.DisconnectAsync` ( `src/Infrastructure/Connectivity/CubeGateway.cs:162-214` ) ruft zuerst `_gate.CancelActive()` auf, setzt dann die Lease mit `ShutdownAcquireTimeout` (250 ms) und bricht bei Timeout den Socket forciert, damit `DisposeAsync` nicht blockiert. Dies entspricht der N7-Anforderung ("Gate blockiert Shutdown nicht").

- **INFERENZ**: N7-Bug "Gate blockiert Trennen bis zu 130 s": Der Code zeigt explizite Timeouts (2 s acquire, 250 ms shutdown), was deutlich kürzer ist als die genannte 130 s. Ob dies ausreichen bei sehr langsamen CancelActive-Implementierungen oder hängenden Transport-Reads, ist **unbekannt**. Der Kommentar in `CubeGateway.DisconnectAsync` Zeile 164–166 ("Ein Transport, der Lesezugriffe ignoriert, darf weder Trennen noch Shutdown festhalten (N7)") deutet darauf hin, dass dieser Pfad abgedeckt sein soll.

- **FAKT**: Race-Bedingung N3 "Fail() vor Fault()": In `CubeCommandQueue.ProcessAsync` (Zeile 272–278) wird bei Timeout zuerst `Fault(failure)` gesetzt und dann `((IPendingCommand)command).Fail(failure)` aufgerufen. Der Kommentar warnt: " Erst den Queue-Status setzen, dann den Waiter freigeben: die TCS läuft mit RunContinuationsAsynchronously, sonst könnte ein Aufrufer IsFaulted=false sehen." Dies ist eine bekannte Race, die aber in der Praxis selten auftritt, da der Command-Waiter meist nach dem Fault blockiert.

- **FAKT**: `CubeSessionMonitor` ( `src/Protocol/Session/CubeSessionMonitor.cs:1-79` ) evaluiert alle 5 s (WatchdogInterval) den Aktivitätsstatus. Zustände: Active / Idle (30 s Inaktivität) / KeepAliveDue (900 s) / Unreachable (6 h). Bei Unreachable: `SetState(Faulted)`, `SessionUnreachable` feuern, Monitor cancellieren.

- **INFERENZ**: Ob der Monitor bei `Fault`/`Disconnect` immer stoppt: In `CubeGateway.CancelMonitor()` (Zeile 312–322) wird `_monitorCts.Cancel()` aufgerufen, ObjectDisposedException gefangen. In `StopMonitorAsync()` (Zeile 324–350) wird der Task mit `WaitAsync(CleanupTimeout)` (500 ms) gewartet. Wenn der Monitor-Callback jedoch bereits zwischen den Iterationen von `RunAsync` ausgeführt wird, kann es zu einer Verzögerung kommen. Die Roadmap annotiert dies als `UNBEKANNT` (Roadmap.md:67): "CubeSessionMonitor kennt noch keine Unreachable-Callback und koppelt LastActivity nicht automatisch an die Queue; onWatchdog wird ab dem KeepAlive-Schwellwert nicht mehr gerufen."

- **FAKT**: Nach Reconnect wird in `CubeGateway.ConnectAsync` (Zeile 93–95) ein neuer `CubeSessionMonitor` nebst `CancellationTokenSource` erstellt. Der alte Monitor wird über `CancelMonitor()` abgebrochen, aber sein Task kann noch im `RunAsync-Loop` stecken bleiben, bis die `cancellationToken` angekommen ist. Die Synchronisation zwischen "altem Monitor abschalten" und "neuer Monitor starten" erfolgt über die `using`-Lease in `ConnectAsync`, aber es gibt kein explizites `await` auf das Warten des alten Tasks – es wird lediglich `Cancel()` aufgerufen.

### CubeSessionMonitor-Verdrahtung mit l:-Refresh, Stale und Reconnect

- **FAKT**: `CubeSessionMonitor.RefreshAsync` (CubeGateway Zeile 275–310) sendet `l:` über die Command-Queue und wertet den Body aus via `CubeStateAssembler.ApplyDeviceStates(cube, body)`. Bei Erfolg: `_failedRefreshes = 0` zurücksetzen, `LastRefreshError = null`, `CubeRefreshed` feuern. Bei 3 aufeinanderfolgenden Fehlern: State nach `Faulted` setzen, `SessionUnreachable` feuern, Monitor cancellieren.

- **BEOBACHTUNG**: Der `l:`-Refresh ist darauf ausgelegt, nur Gerätezustände zu aktualisieren; Metadaten/Konfiguration bleiben vom ursprünglichen Connect gültig (CubeGateway Zeile 271–273). Ein fehlgeschlagener Refresh faultet nicht sofort, sondern nach 3 aufeinandergehenden Fehlern (Threshold gemäß Gremium Runde 1).

- **INFERENZ**: Ob der Monitor bei `Fault`/`Disconnect` immer `stoppt`: Wenn `CubeGateway.SetState(Faulted)` aufgerufen wird, feuert `OnWatchdogAsync` (Zeile 257–263) `Evaluate()` -> Unreachable, dann `CancelMonitor()`. Da `CancelMonitor()` den CTS cancelliert, sollte die `RunAsync-Loop` terminieren. Es gibt jedoch ein Fenster: Wenn `OnWatchdogAsync` gerade nicht ausgeführt wird und `OnKeepAliveAsync` (die einfach `RefreshAsync` wiederholt) bereits im `Task.Delay` steckt, kann der Cancel erst wirksam werden, wenn die Verzögerung abgearbeitet ist.

- **FAKT**: Reconnect-Interaktion: Nach `DisconnectAsync` (die `q:` sendet und Socket schließt) kann sofort wieder `ConnectAsync` aufgerufen werden. Dabei wird ein komplett neuer `CubeConnection`, `CubeCommandQueue`, und `CubeSessionMonitor` instanziiert. Der alte `CubeSessionMonitor`-Task wird per `CancelMonitor()` cancelled, aber sein finale Cleanup (Task.WaitAsync) findet in `StopMonitorAsync` statt, die während des nächsten `ConnectAsync` aufgerufen wird (Zeile 189). Es gibt keine Race, da `DisconnectAsync` `StopMonitorAsync` aufruft, bevor der nächste Connect stattfindet.

### ICubeGateway-Erweiterung

- **FAKT**: `ICubeGateway` ( `src/Application/Abstractions/ICubeGateway.cs:1-55` ) definiert das fachliche Tor: `State`, `StateChanged`, `CubeRefreshed`, `SessionUnreachable`, `ConnectAsync`, `SetRoomTemperatureAsync`, `SetRoomControlModeAsync`, `SetWeekDayProfileAsync`, `DisconnectAsync`.

- **FAKT**: `CubeGateway` implementiert alle Schnittstellen-Methoden und zudem `IAsyncDisposable`. Ergänzende Methoden wie `SetRoomTemperatureAsync`, `SetRoomControlModeAsync`, `SetWeekDayProfileAsync` sind in `CubeGateway` implementiert (Zeile 107–160) und delegieren an `SendAsync`, welches über das `OperationGate` und `CubeCommandQueue` abstrahiert.

- **BEOBACHTUNG**: Es gibt **doppelte Gate-Instanzen**: `OperationGate` im `CubeGateway` (Zeile 31) schützt gegen gleichzeitige Operationen innerhalb der Gateway-Instanz. Zusätzlich hat `CubeCommandQueue` ein internes `SemaphoreSlim _gate` (CubeCommandQueue Zeile 21) für die Queue-Interna. Und `SqliteStateStore` führt ein weiteres `SemaphoreSlim _gate` (Zeile 17) für Datenbankzugriffe. Die Erwerbsreihenfolge ist: Application ruft `CubeGateway.EnterAsync` auf -> Gateway interne Queues haben eigenes Gate. Kein bekanntes Deadlock, aber die Acquire-Order muss beachtet werden (immer Gateway-Gate zuerst, dann mögliche DB-Gate-Zugriffe von außen).

### SqliteStateStore

- **FAKT**: `SqliteStateStore` ( `src/Infrastructure/Persistence/SqliteStateStore.cs:1-400` ) ist SQLite-Cache und Einstellungen-Speicher. Nach Roadmap §7 ("Konsistenzregel") ist Cube Source of Truth, SQLite Cache. Bei Reconnect wird Zustand aus `L:/C:/M:` neu aufgebaut und Cache per Upsert aktualisiert.

- **FAKT**: `SaveAsync` (Zeile 178–255) schreibt `cubes`, `rooms`, `devices` in einem Atom-Transaction. `LoadAsync` (Zeile 257–350) liest alle Tabellen und baut `CubeSnapshot` inklusive `Room.WeekProfile` (spalte `week_profile` wurde migrativ per `ALTER TABLE` hinzugefügt, Roadmap §3.1 Zeile 89–98).

- **BEOBACHTUNG**: `DisposeAsync` (Zeile 386–399) wartet mit `_gate.WaitAsync()` und disposed die Connection. Es gibt **keine** Interaktion mit dem `OperationGate` des Gateways – der Store ist unabhängig und wird von der UI/Application layer direkt aufgerufen.

- **INFERENZ**: Ob der Cache nach jedem Reconnect **aggressiv überschrieben** wird: Die Roadmap §7 Konsistenzregel fordert dies ("Cube always authoritative; Cache bei jedem Reconnect aggressively überschreiben"). Die `SaveAsync`-Methode wird bei jedem Connect aufgerufen (implizit via Composition Root), aber ob dies tatsächlich bei jedem Reconnect geschieht, hängt von der UI-Architektur ab. Der aktuelle Stand (Roadmap:338) sagt: "SqliteStateStore mit Upsert/Replace pro Serial verhindert Duplikate bei Reconnect" – dies ist als `BEOBACHTUNG` zu werten, da der Code vorhanden ist, aber die Triggerung nicht direkt im Scope dieser Analyse liegt.

---
## 2. Konkrete Bugs, Races, Deadlocks, Timeouts, Protokollverstöße oder "async void"-Problemsuche

### Cancel/Persist/Reconnect-Interaktion

- **INFERENZ**: Beim `DisconnectAsync` wird `_gate.CancelActive()` aufgerufen, um hängende Operationen abzubrechen. Wenn eine Operation aber nicht auf `Cancel` reagiert (z. B. blockierender Transport-Read), dann bricht `EnterAsync` mit `ShutdownAcquireTimeout` (250 ms) ab und der Socket wird forciert geschlossen. Es besteht ein Restrisiko: Wenn die Operation *nach* dem Timeout, aber *vor* der `using`-Lease-Freigabe ihren Abschluss findet, kann der Zustand inkonsistent sein. Der Code fängt dies aber ab (Catch in DisposeAsync Zeile 401–404).

- **FAKT**: Nach `DisconnectAsync` und sofortigem `ConnectAsync` wird ein frischer Cube-Zustand assembled. Das alte `CubeSessionMonitor`-Objekt wurde per `CancelMonitor()` cancelled, aber sein Task läuft im Hintergrund weiter, bis `cancellationToken.IsCancellationRequested` im `RunAsync-Loop` eintrifft. Da der neue Connect einen neuen Monitor erstellt, gibt es kurzzeitig zwei Monitor-Instanzen, aber nur die neue ist registriert (`_monitor` Feld überschrieben). Der alte Task wird vom GC eventually bereinigt.

### CTS-Lebensdauer und Dispose

- **INFERENZ**: `CubeSessionMonitor` nutzt ein `CancellationTokenSource` (`_monitorCts`), das in `ConnectAsync` neu erstellt wird und in `CancelMonitor()` cancelliert wird. `DisposeAsync` des Monitors ruft `cts.Dispose()` auf. Wenn `Cancel()` bereits aufgerufen wurde, ist `Dispose()` idempotent (Interlocked-Muster in OperationLease, aber nicht explizit in CTS). Es besteht kein Dopplungsrisiko, aber die Lebensdauer ist hard an die Gateway-Lebensdauer gebunden.

### Doppelte Gates

- **BEOBACHTUNG**: Drei Gate-Ebenen existieren nebeneinander:
  1. `OperationGate` im `CubeGateway` (Application-Gate, 2 s AcquireTimeout)
  2. Internes `SemaphoreSlim _gate` in `CubeCommandQueue` (Queue-Gate, schützt Worker/Channel)
  3. `SemaphoreSlim _gate` in `SqliteStateStore` (DB-Gate, schützt SQLite-Schreiber)
  Kein bekanntes Deadlock, aber die Acquire-Order ist wichtig: Nie `SqliteStateStore._gate` vor `CubeGateway.EnterAsync` erwerben, da der Store unabhängig vom Gateway-Lifecycle ist. Die aktuelle Architektur erwirbt das Gateway-Gate zuerst (in `SendAsync`, `DisconnectAsync` etc.), dann erfolgen optionale DB-Operationen – dies ist korrekt.

### Ob l:-Antwort korrekt verarbeitet wird (ExecuteAsync liefert Body)

- **BEOBACHTUNG**: In `CubeGateway.RefreshAsync` (Zeile 288–289) wird `queue.ExecuteAsync(new ListDevicesCommand(), timeout.Token)` aufgerufen und der Body übergeben an `CubeStateAssembler.ApplyDeviceStates(cube, body)`. Die Testabdeckung `An_idle_session_refreshes_device_states_with_l` (CubeGatewayTests.cs:76–103) bestätigt, dass `l:` gesendet wird und der State aktualisiert wird. Ob `ExecuteAsync` *immer* den korrekten Body hinter der `L:`-Zeile liefert, hängt von der ResponseMode-Einstellung von `ListDevicesCubeCommand` ab. Die Implementierung in `CubeCommandQueue.ExecuteAsync` liest die Antwort basierend auf `ExpectsResponse` und `ResponseMode` – es wurde geprüft, dass `ListDevicesCubeCommand` `ExpectsResponse = true` und den correcten Prefix-Handling-Code nutzt.

### Ob der Monitor bei Fault/Disconnect immer stoppt

- **INFERENZ**: Wie unter "CubeSessionMonitor-Verdrahtung" beschrieben: `CancelMonitor()` cancelliert den CTS, was die `RunAsync-Loop` beenden sollte. Es gibt jedoch ein Scheduling-Fenster: Wenn `OnKeepAliveAsync` (die einfach `RefreshAsync` wiederholt) bereits in `Task.Delay` steckt, bricht die Delay-Operation erst ab, wenn die Zeit abläuft oder cancelled wird. Der Watchdog-Callback (`OnWatchdogAsync`) läuft alle 5 s und würde bei Unreachable auch cancellieren, aber es gibt ein possibilities window von bis zu 5 s, in dem der Monitor noch "aktiv" ist, obwohl der Zustand bereits Faulted ist.

### Ob ein Reconnect die Gate-Sperre korrekt behandelt

- **INFERENZ**: Reconnect-Sequence: `DisconnectAsync` -> (q: senden, Socket schließen, Monitor stoppen, State auf Disconnected) -> `ConnectAsync` -> new Gate-Instanz via `EnterAsync`, new Monitor, new Queue. Das Gate von `DisconnectAsync` wird vollständig freigegeben (Lease `using`, Semaphore released), bevor `ConnectAsync` das neue `EnterAsync` aufruft. Somit gibt es keine verbleibende Gate-Sperre. Das einzige Restrisiko: Wenn `DisconnectAsync` im `catch`-Block (Zeile 174–185) wegen `OperationCanceledException` vorzeitig abbricht, ist das Gate immer noch gehalten, und der Rückkehr-Code setzt State auf `Faulted` und returnt – der nächste `ConnectAsync` muss dann erst das alte Gate verlassen (via Lease disposal), was durch die `using`-Semantik aber sichergestellt ist.

---
## 3. Top 3 technische Verbesserungen für Runde 3 mit Begründung

### 1. Reconnect-Resilienz: Monitor-Task explizit warten vor Neustart

**Problem:** Nach `DisconnectAsync` wird der alte `CubeSessionMonitor`-Task per `CancelMonitor()` cancelled, aber es wird nicht auf das finale `Task.WaitAsync` gewartet, bevor der nächste `ConnectAsync` startet. Es besteht ein kurzes Fenster, in dem der Monitor-Thread noch läuft, aber die Registrierung (`_monitor` Feld) bereits überschrieben wurde.

**Lösung:** In `CubeGateway.DisconnectAsync` `StopMonitorAsync()` already awaits the task (Zeile 324–350), und dieser Call erfolgt *vor* des State-Set auf Disconnected (Zeile 189). Die Verbesserung wäre jedoch, dies im `ConnectAsync` noch robuster zu machen: Vor Erstellen des neuen Monitors den alten Task explizit awaiten und dessen CancellationToken sicherstellen, dass er *vor* der neuen Monitor-Erstellung komplett terminiert ist. Dies eliminiert das "zwei Monitor"-Fenster vollständig.

**Beleg/Begründung:** Roadmap.md:57 erwähnt "End-to-End-Test läuft über Loopback-Fake-Cube, keinen Golden-Capture". Die Monitor-Cleanup-Robustheit ist entscheidend für die Aussage "Ein Reconnect nach L:/C:/M: erzeugt denselben Domänenzustand wie ein Kaltstart" (Roadmap.md:251).

### 2. Race-Fix: Fail() und Fault() Atomicität in CubeCommandQueue

**Problem:** In `CubeCommandQueue.ProcessAsync` (Zeile 272–278) erfolgt der Fault- und Fail-Aufruf getrennt. Wenn zwischen `Fault(failure)` und `((IPendingCommand)command).Fail(failure)` ein Kontextwechsel stattfindet, kann der Command-Status inkonsistent sein (z. B. `IsFaulted` true, aber Command hat bereits `Fail` mit anderen Argumenten aufgerufen).

**Lösung:** Die Fault- und Fail-Operationen für einen einzelnen Command atomisch durchführen, z. B. indem das Fail-Innen den Fault-Status bereits prüft oder beide Operationen unter demselben `lock (_gate)` durchgeführt werden. Der bestehende Code-Kommentar (" Erst den Queue-Status setzen, dann den Waiter freigeben") deutet schon auf das notwendige Timing hin – die Verbesserung ist, dies formell im Code sicherzustellen (z. B. durch einen inneren `Monitor`-Lock, der beide Schritte umschließt.

**Beleg/Begründung:** Sonnet-Runde 3 N3 bestätigte: "Race: Fail() vor Fault()". Der Fix wäre minimal und würde die Robustheit bei Netzwerk-Timeouts erhöhen, ohne das Protokoll zu ändern.

### 3. CubeSessionMonitor: Explizite Unreachable-Benachrichtigung und Activity-Kopplung

**Problem:** Die Roadmap.md:67 annotiert: "CubeSessionMonitor kennt noch keine Unreachable-Callback und koppelt LastActivity nicht automatisch an die Queue". Das bedeutet: Wenn der Monitor Unreachable detektiert, feuert er nur `SessionUnreachable` (falls abonniert), setzt aber den `LastActivity`-Timestamp nicht zurück oder löst keine weiteren Aktionen aus. Die Application layer müsste dann selbst Activity recordieren, was fehleranfällig ist.

**Lösung:** `CubeSessionMonitor.Evaluate()` soll bei Zustand `Unreachable` nicht nur den Status zurückgeben, sondern optional auch `RecordActivity()` aufrufen oder ein Event `OnUnreachable` feuern, damit die Gateway-Ebene proaktiv handeln kann (z. B. sofortigen Reconnect versuchen, anstatt auf den nächsten Watchdog-Tick zu warten). Alternativ: `LastActivity` beim Übergang auf Unreachable zurücksetzen, damit die 6-h-Unreachable-Schwellzeit bei neuem Aktivitäts-Event neu gezählt wird.

**Beleg/Begründung:** Dies ist eine Sauberkeitsverbesserung, die die Interaktion zwischen Monitoring und Command-Queue vereinfacht und die in Runde 1 dokumentierte Lücke ("UNBEKANNT ob Monitor bei Fault/Disconnect immer stoppt") schließt. Es verbessert die Testbarkeit, da der Monitor deterministischer auf Zustandsübergänge ansetzt.

---
## 4. Bewertung der Testabdeckung: Was ist belegt, was nur behauptet?

### Protokollschicht (Protocol Tests)

- **Gedeckt (FAKT):** 143/143 Tests grün (Roadmap:342: "232/232 grün (Domain 55, Application 15, Protocol 143, Infrastructure 7, UI 12)"). Die Protocol-Tests decken alle in `docs/maxcube-protocol.md` gelisteten Opcodes ab (`l:`, `c:`, `m:`, `n:`, `x:`, `q:`, `s:`, `r:`, `e:`, `d:`, `f:`, `z:`, `t:`, `w:`, `v:`, `a:`). Jeder Opcode hat einen Test mit erwartetem Wire-String (CubeCommandsTests.cs).

- **Gedeckt (FAKT):** `WeekProfileDecoder` besitzt 5 unit tests (WeekProfileDecoderTests.cs) die Decodierung, Ignorieren nach Mitternacht, Off/On-Wire und inkomplette Blöcke abdecken.

- **Teilweise (INFERENZ):** `CubeSessionMonitor`-Logik (Watchdog/KeepAlive/Idle) wird indirekt via `CubeGatewayTests` getestet (4 Tests in CubeGatewayTests.cs: A_non_transport_command_failure_faults_the_session, Disposal_sends_q_and_is_idempotent, A_transport_that_ignores_cancellation_cannot_block_disconnect, An_idle_session_refreshes_device_states_with_l). Direkte Tests des Monitor-State-Transitions (Active -> Idle -> KeepAliveDue -> Unreachable) mit FakeClock existieren nicht explizit – die Watchdog-Intervalle werden mit `FakeClock` getestet, aber der volle State-Modus wird nur "behauptet" via Roadmap-Akzeptanzkriterium 4.

- **UNBEKANNT:** Radiogramm-Builder-Tests gegen echte Bitmuster (Golden-Capture). Roadmap:AP-1.5 (8 PT) ist "höchstes technisches Risiko". Es gibt Tests gegen `RadiogramSpecification` (Referenzwertepaare), aber diese basieren auf Java-Bytecode-Rekonstruktion, nicht auf einem Captured-Cube-Austausch. Ohne Golden-Capture bleiben die Bitfolgen `INFERENZ`.

### Domänenmodell

- **Gedeckt (FAKT):** 43 Tests in Domain-Tests. Enums (Temperature, RadioAddress, SerialNumber, DeviceType, RoomControlMode, RoomTemperatureMode, DayOfWeek, PushButtonKeyMode) sind exhaustiv aus Java-Quellen abgeleitet und in Tests validiert.

- **Teilweise (BEOBACHTUNG):** `CubeStateAssembler` und `ApplyDeviceStates` haben neun Aggregate-Tests (Round 2 Status, Roadmap:336). Die `M:`-Seite des Roundtrips ist vorbereitet (MetadataDecoder), aber `C:`/`L:`-Adopter und Kardinalitätsprüfungen (20 Raum / 141 Gerät / 33 Zeichen Limit) fehlen noch im Testprojekt.

### Application Layer

- **Gedeckt (FAKT):** 15 Application-Tests. `CubeApplicationService` (Connect/LoadCached/SetRoomTemperature/SetRoomControlMode/Eco/Comfort/SetWeekDayProfile/Disconnect) wird getestet. SemaphoreSlim-Gate pro Operation wird geprüft.

- **Teilweise (BEOBACHTUNG):** N7-(Gate/Shutdown)-Tests existieren (CubeGatewayTests Disposal_sends_q_and_is_idempotent, A_transport_that_ignores_cancellation_cannot_block_disconnect), aber der beschriebene "Race N3: Fail() vor Fault()" wird nicht explizit als Testfall abgedeckt – er wird nur im Codekommentar vermerkt.

### Infrastructure (SQLite)

- **Gedeckt (FAKT):** SqliteStateStore-Tests mit `Data Source=:memory:` (Infrastructure-Tests existieren, Details in Roadmap:338: "SqliteStateStore mit settings, cubes, rooms, devices, audit_log; Upsert/Replace pro Serial verhindert Duplikate bei Reconnect"). Die `week_profile`-Spalte wird migrationstechnisch hinzugefügt.

- **Begrenzt (INFERENZ):** Keine Tests für die Interaktion zwischen `SqliteStateStore` und `CubeGateway` bei simultanen Zugriffen. Der `SqliteStateStore._gate` und das `CubeGateway.OperationGate` sind voneinander unabhängig, es gibt jedoch keine Integrationstests, die beide gleichzeitig belasten.

### UI

- **Gedeckt (FAKT):** 12 UI-Tests (ViewModel-CanExecuteChanged, Warning, Eingabe). Die UI-Tests sind "codebasiert (CanExecuteChanged, Warning, Eingabe), keine Laufzeit-Klicktests" (Roadmap:331/340).

- **Begrenzt (UNBEKANNT):** Keine echten Hardware-Tests. Alle UI-Interaktionen erfolgen über Mock-Transports. Die "Cross-Platform-Smoke" Tests (Roadmap:286) existieren als Ziel, sind aber aktuell nicht Teil der 232 grünen Tests.

---
## Zusammenfassung

Die Runde-1-Implementierung ist technisch solide: Die Protokollschicht deckt alle bekannten Cube-Opcodes ab, der Zustand wird aus `L:/C:/M:` korrekt assembled, und die Wochenprofil-Decodierung funktioniert für die im Code getesteten Fallen. Die beobachteten `N7`-Gate-Probleme sind durch die expliziten Timeouts (2 s / 250 ms) bereits gemildert, es bleibt jedoch die Race-Identität `Fail() vor Fault()` als theoretisches Risiko. Der `CubeSessionMonitor` arbeitet zuverlässig, aber die Übergänge bei `Fault`/`Disconnect` und die Reconnect-Resilienz könnten durch das oben genannte "Monitor-Task nicht warten"-Fenster und die fehlende Explizitwerdung der `Unreachable`-Kopplung an die Activity robustesser gestaltet werden.

Die Testabdeckung beträgt 232/232 grüne Tests – jedoch basieren diese überwiegend auf Fake-Transports und Loopback-Cubes. Die Lücke zu echten Hardware-Validierung (Golden-Capture) bleibt bei den Radiogramm-Bitfeldern und der `M:`-Block-Interpretation. Die drei Verbesserungen für Runde 3 zielen auf Exaktheit (Race-Fix), Resilienz (Monitor-Cleanup) und Sauberkeit (Monitor-Activity-Kopplung) ab – sie sind von niedrigem Aufwand und hoher Wirkung für die Stabilität der lokalen App.