# Implementierungsplan – MAX! Cube Neuimplementierung

> Status: **Umsetzung bis erste funktionierende Version** (24.11.2026). Grundlage: `docs/architecture-rea.md`, `docs/maxcube-protocol.md`, `docs/domain-model.md`, `docs/gui-specification.md`, `docs/features.md`, `docs/target-architecture.md`. Zwischenstand siehe Abschnitt 8.
>
> Aufwände in Personentagen (PT), inkl. Tests und Review. Annahme: 1 Senior-Entwickler mit .NET/Avalonia-Erfahrung.
>
> Priorisierungsvorgabe des Auftraggebers: **1. Protokoll → 2. Domänenmodell → 3. Persistenz → 4. Businesslogik → 5. UI**.

---

## 0. Overhead & Vorbedingungen (in keinem Arbeitspaket enthalten)

| ID | Maßnahme | Aufwand |
|---|---|---|
| V1 | Solution-Skeleton, CI (4 RIDs), Style/Analyzers, Testprojekte | 3 PT |
| V2 | Reverse-Engineering-Doku abschließen (Phasen 1–6), Unknown-Liste pflegen | 1 PT |
| V3 | (optional) Capture eines echten Cubes / virtualisierten Cube (62910) für Golden-Tests | 2 PT |

**Voraussetzung für AP-1.4 und AP-1.5:** mindestens `V3` oder ein dokumentierter Fake-Cube mit aufgezeichneten `H:/L:/C:/M:/A:`-Antworten.

---

## Phase 1 – Protokollschicht (höchste Priorität)

### AP-1.1 – Transport & Handshake
- **Beschreibung (umgesetzt 24.11.2026):** `ICubeTransport`, `TcpCubeTransport`, zeilenbasiertes Lesen/Schreiben (**CRLF**-terminiert, eigenes `LineReader`; LF `\n` wird beim Lesen akzeptiert), `ConnectAsync()` mit Konfiguration über `CubeConnectionOptions` (Host/Port statt Signatur `ConnectAsync(ip, port)`), `soTimeout` 30 s Hello-First-Read / 70 s Socket (Java, `CubeConnection.java:40-74`) plus 30 s Connect als C#-Ergänzung (`max.local.properties:17-24`), Hello-Parser `H:serial,addrHex,fwHex` und Firmware-Guard 273–275. Asynchrone Timeouts werden per `CancellationTokenSource.CancelAfter` erzwungen, weil `NetworkStream.ReadTimeout` nur synchrone I/O betrifft.
- **Aufwand:** 4 PT
- **Risiken:** Falsche Zeilenkodierung/CRLF; Cube antwortet mit anderer Reihenfolge; Firmware-Guard zu streng für reale Geräte.
- **Abhängigkeiten:** V1, `docs/maxcube-protocol.md` (Handshake-Kapitel).
- **Akzeptanzkriterien:**
  1. `H:001234567890,12345678,0111` → Serial `001234567890`, RadioAddress `0x12345678`, Firmware `273` (0x0111).
  2. Firmware 272 und 276 werden mit `CubeCommunicationFailedException` abgelehnt.
  3. Fehlt `H:`, wird `NoIncomingHello` gemeldet.
  4. Unit-Tests mit `MemoryStream` ohne Socket.

### AP-1.2 – State-Empfänger (L / C / M)
- **Beschreibung:** Initialer Empfang von `L:` (Geräteliste), `C:<addrHex>,<config>` (Gerätekonfiguration), `M:<index>,<count>,<data>` (Metadaten, 1900-Byte-Blöcke), bis `L:` eingegangen ist (`CubeConnection.receiveDeviceAndMetaData()`, `CubeConnection.java:157-183`; `max.metadataBlockSize=1900`, `max.local.properties:8`).
- **Aufwand:** 5 PT
- **Risiken:** `M:`-Blöcke können mehrfach/interleaved kommen; `C:` vor `L:`; große Blöcke → Speicher.
- **Abhängigkeiten:** AP-1.1.
- **Akzeptanzkriterien:**
  1. Eine aufzuzeichnende Sequenz `C:…,M:…,L:…` endet nach `L:` und liefert einen vollständigen `RawCubeState`.
  2. Jede `C:` wird ihrer Radio-Adresse zugeordnet; jede `M:` ihrem Index/Count.
  3. Timeout/max. Blockgröße bricht kontrolliert ab (kein Endlosschleifen-Test).

### AP-1.3 – Kommando-Queue & Sitzungslogik
- **Beschreibung:** `Channel<ICubeCommand>` (Kapazität 100), **ein** Worker, synchrone Antwort-Zuordnung, `CancellationToken` statt `waitForResult()` (`CubeConnection.java:36,139-155,185-197`), Inclusion-Mode-Guard (nur `FetchNewDevices`, `CancelInclusionMode`, `Disconnect` erlaubt, `CubeConnection.java:264-275`), Watchdog 5 s und Idle 30 s / 900 s (`LocalApp-al.xml:10-13`, `max.local.properties:4-5`).
- **Aufwand:** 5 PT
- **Risiken:** Deadlock bei Antwort-Timeout; Inclusion-Mode bleibt hängen (Timeout 60 s, `max.local.properties:9`); Queue-Überlauf.
- **Abhängigkeiten:** AP-1.1, AP-1.2.
- **Akzeptanzkriterien:**
  1. 100 parallele Commands werden in Empfangsreihenfolge seriell abgearbeitet.
  2. Im Inclusion-Mode wird `SetRoomPermanentMode` mit `CubeIsInInclusionMode` abgelehnt.
  3. Socket-Fehler markiert alle wartenden Commands als fehlgeschlagen (Original: `purgeCommandQueue()`, `CubeConnection.java:249-261`).
  4. Idle-Aktivität und Watchdog laufen mit `FakeClock` deterministisch.

**Zwischenstand 24.11.2026 (AP-1.2/1.3):** `StateReceiver`, `RawCubeState`, `CubeCommandQueue`, `CubeSessionMonitor` und 21 typisierte Kommandos sind implementiert; Solution-Build 0 Warnungen/0 Fehler, 116/116 Tests grün (mehrfach wiederholt). Der End-to-End-Test läuft über einen Loopback-Fake-Cube, **keinen** Golden-Capture (siehe `tests/MaxCube.Protocol.Tests/LoopbackFakeCube.cs:10-12`). Das ist ein `BEOBACHTUNG`-Status, kein Hardwarebeweis.

Bewusste, dokumentierte Abweichungen vom Java-Original:

- `StateReceiver` bricht bei falscher Argumentzahl einer `C:`/`M:`-Zeile ab; Java ignoriert solche Zeilen (`CubeConnection.java:167,175`). Begründung: `UNBEKANNT` darf nicht als gültiger Zustand durchgereicht werden.
- `C:`-Radioadressen werden auf `0..0xFFFFFF` begrenzt (`NumberStyles.HexNumber` allein akzeptiert negative 32-Bit-Werte). `L:` liefert bei leerem Body `""` statt `null`; für Base64 ist das gleichwertig.
- `RawCubeState` verlangt alle `M:`-Blöcke und wirft bei abweichendem `count` mit `CubeCommunicationFailedException`; Java füllt fehlende Blöcke still mit leeren Arrays (`Metadata.java:43-46`).
- Parse-/Präfix-Fehler einer Antwort weisen nur diesen einen Command ab (`UnknownCubeOpcodeException`, `CubeCommandRejectedException`), ohne die Verbindung zu faulten. Transport-, EOF- und Timeout-Fehler faulten die Queue und purgen alle Wartenden.
- Der Inclusion-Guard greift beim Einreihen **und** beim Ausführen; das Flag wird vor dem Abschließen des `n:`-Commands gesetzt. `CubeIsInInclusionModeException` ist daher kein Verbindungsfehler. Ein erneutes `n:` wird im Gegensatz zum Java-Original abgelehnt (dort wartet `FetchNewDevices` im Inclusion-Mode nur auf `N:`), damit ein Paarungsfenster nicht versehentlich neu gestartet wird.
- `n:` nutzt einen kommando-spezifischen Timeout von `max(70 s, inclusionSeconds + 10 s)` statt der globalen 30 s, weil der Cube die `N:`-Antwort erst bei einem neuen Gerät oder am Ende des Inclusion-Fensters sendet (`FetchNewDevicesCubeCommand.java:39-65`). Ein leeres `N:` beendet den Inclusion-Mode auch in C# (`CubeConnection.java:59-62`). `x:` ignoriert Antwortzeilen ohne `N:`-Präfix wie das Original (`CancelInclusionModeCubeCommand.java:37-49`).
- `CubeSessionMonitor` kennt noch keine `Unreachable`-Callback und koppelt `LastActivity` nicht automatisch an die Queue; `onWatchdog` wird ab dem KeepAlive-Schwellwert nicht mehr gerufen (Exklusivität ist laut `docs/maxcube-protocol.md` `UNBEKANNT`). Die 60-s-Inclusion-Überwachung aus `max.local.properties:9` ist noch nicht umgesetzt und bleibt als offene Aufgabe für AP-1.6/AP-3 vermerkt.

### AP-1.4 – Befehlskatalog (App → Cube)
- **Beschreibung:** Alle outgoing Opcodes aus `IOperationCodes.java:32-59` als typisierte Commands: `u:` URL, `i:` Interval, `s:` Senden/Radiogramm, `m:` Metadaten, `n:`/`x:` Inclusion, `g:` More-Data, `q:` Quit, `e:`/`d:` AES, `a:` Reset-Gateway, `r:` Reset-Radiogramm, `t:` DeleteDevices, `w:` PushButton-Config, `l:` DeviceList, `c:` Configuration, `v:` Timezone, `f:` NTP, `z:` Wakeup. Jeder mit Syntax, Beispiel und Beispielantwort aus `docs/maxcube-protocol.md`. **Korrektur 24.11.2026:** `B:/G:/J:/P:/O:/V:/W:` sind **keine** Cube-TCP-Opcodes, sondern gehören zum HTTP-Remote-Proxy der abgeschalteten Cloud (Abschnitt 6.1 der Protokolldoku); sie werden bewusst nicht als Commands modelliert. Das gilt auch für das `g:`-„More-Data": die operative `g:`-Antwort gehört zur `G:`-Anfrage des Proxys, `OUTGOING_MORE_DATA` ist eine tote Konstante.
- **Aufwand:** 8 PT
- **Risiken:** Mehrfach belegte Präfixe (`s:`, `u:`, `v:`, `w:`, `g:`) kontextabhängig; unbekannte Antworten.
- **Abhängigkeiten:** AP-1.3, vollständige Protokolldoku.
- **Akzeptanzkriterien:**
  1. Für jeden gelisteten Opcode existiert ein Test mit erwartetem Request-String.
  2. `A:` (Acknowledge) wird als Erfolg, `r:`-Antwort als Fehler interpretiert.
  3. Unbekannte Antwort-Präfixe erzeugen `UnknownOpcode`-Fehler, keine stillen Erfolge.

**Zwischenstand 24.11.2026:** 21 typisierte Commands für alle **belegten** Cube-TCP-Opcodes existieren (`src/MaxCube.Protocol/Commands/CubeCommands.cs`) und sind in `tests/MaxCube.Protocol.Tests/CubeCommandsTests.cs` mit erwartetem Wire-String getestet. **Aufgelöst 24.11.2026:** Die sieben Opcodes `B:/G:/J:/P:/O:/V:/W:` und das operative `g:` sind **geklärt** – sie gehören zum HTTP-Remote-Proxy (Device-App) der seit 01.07.2023 abgeschalteten Cloud, nicht zum lokalen Cube-TCP. Sie werden bewusst **nicht** implementiert: keine Cloud-Schnittstelle, keine Zugangsdaten/Produktkeys in der App. `CODE-BELEGT` durch `decompiled/*/sources/de/eq3/max/al/local/remoteproxy/` (sieben Request-Klassen, `MaxRemoteProxy.java:67-175`). Nur die tote Konstante `OUTGOING_MORE_DATA` (`g:`, `IOperationCodes.java:38`) bleibt `UNBEKANNT`. Ein unerwartetes `b:/g:/j:/p:/o:/v:/w:` **als Antwortzeile** auf Port 62910 wird als `UnknownCubeOpcodeException` abgelehnt, nicht als Erfolg (`CubeCommands.cs:58-60`); `v:`/`w:` bleiben gültige Requests (`CubeCommands.cs:309,338`). Nachweis: `SecondBrain/wiki/fragen/2026-11-24-maxcube-remote-proxy-opcodes.md`, Abschnitt 6.1 der Protokolldoku. Antwort-Prefixe der implementierten Commands folgen den Java-Quellen (`l:→L:`, `c:→C:`, `m:/u:/i:/t:/w:/v:/z:→A:`, `n:/x:→N:`, `s:/r:→S:`, `e:→E:`, `d:→D:`, `f:→F:`).

### AP-1.5 – Radiogramm-Builder
- **Beschreibung:** Bitgenaue Erzeugung der Geräte-Telegramme aus `RadiogramSpecification.java` / `TransmissionRadiogramBuilder.java` / `ResetRadiogramBuilder.java` (inkl. Switchpoints, Temperaturcodierung, Adressen, Parity/CRC falls belegt).
- **Aufwand:** 8 PT
- **Risiken:** **Höchstes technisches Risiko.** Bitfelder sind aus Bytecode rekonstruiert; ohne Golden-Capture nicht verifizierbar.
- **Abhängigkeiten:** AP-1.4, V3 (Golden-Capture) dringend empfohlen.
- **Akzeptanzkriterien:**
  1. Bekannte Telegrasmmuster aus der Doku werden bytegleich reproduziert.
  2. Temperatur 20,0 °C → belegter Codierungswert; 0,5-°C-Schritte geprüft.
  3. Jeder Builder hat einen Test gegen `RadiogramSpecification` (Referenzwertepaare).

### AP-1.6 – UDP-Firmware-Update (Erkennung zuerst)
- **Beschreibung:** `CubeUdpOpcode`-Tabelle und Request/Response aus `CubeUdpProxy.java:105`, `CubeUdpRequest/Response`, `CubeUpdater.java` nachbauen. Phase 1 nur **Erkennung + Status**; Schreiben/Firmware-Flash erst nach Capture-Validierung.
- **Aufwand:** 4 PT (Erkennung) + 6 PT (Flash, später)
- **Risiken:** Gerätebrick bei falschem Frame; Update-URL nicht mehr erreichbar (`max.eq-3.de`).
- **Abhängigkeiten:** AP-1.4, V3.
- **Akzeptanzkriterien:**
  1. Erkennung liefert aktuelle Firmware und verfügbare Version ohne Schreibzugriff.
  2. Flash-Pfad ist **nicht implementiert** und bleibt deaktiviert, bis ein Golden-Test existiert.

**Phase-1-Gesamtaufwand: 34 PT (+ 6 PT optional)**

---

## Phase 2 – Domänenmodell

### AP-2.1 – Value Objects & Enums
- **Beschreibung:** `Temperature` (0,5 °C-Schritte, Grenzen aus `max.properties`), `RadioAddress` (Hex), `SerialNumber` (Fehlercodes `errorcodes.properties:42-43`, Pattern `UNBEKANNT`), `DeviceType`, `RoomControlMode`, `RoomTemperatureMode`, `DayOfWeek` (Monday=1…Sunday=7), `DeviceStateInfo`, `RadioState`, `PushButtonKeyMode` (`essentials/.../cubestate/*.java`, `cl/dto/*.java`).
- **Aufwand:** 4 PT
- **Risiken:** Falsche Temperatureinheit/Grenzen; Enum-Werte unvollständig.
- **Abhängigkeiten:** V1, `docs/domain-model.md`.
- **Akzeptanzkriterien:**
  1. Alle Enums sind exhaustiv aus Java-Quellen abgeleitet und in der Doku gelistet.
  2. `Temperature` lehnt Werte außerhalb der belegten Grenzen ab (`DomainValidationException` mit `MAX1050`, `errorcodes.properties:52`; `@Range(0..40)` aus `Temperature.java:13`).
  3. Value Objects sind immutable und haben Wertvergleich.

**Zwischenstand 24.11.2026:** `Temperature`, `RadioAddress`, `SerialNumber`, alle **sieben** Fach-Enums (`DeviceType`, `RoomControlMode`, `RoomTemperatureMode`, `DeviceStateInfo`, `RadioState`, `DayOfWeek`, `PushButtonKeyMode`) und `DomainValidationException` sind in `src/MaxCube.Domain/` implementiert; 43 neue Tests grün. Sonnet-Gegenprüfung: erster Lauf nicht freigegeben (DayOfWeek, PushButtonKeyMode, Doku-Zahlen/Quellzeilen, INFERENZ-Kennzeichnung), alle Funde behoben. Quellzeilen der Enums: `DeviceType.java:4-30`, `RoomControlMode.java:4-9`, `RoomTemperatureMode.java:4-9`, `DeviceStateInfo.java:4-8`, `RadioState.java:4-11`, `DayOfWeek.java:6-45`, `PushButtonKeyMode.java:4-10`; vollständig gelistet in `docs/domain-model.md` Abschnitt 16. Bewusste Abweichungen: `Temperature` nutzt als `readonly record struct` einen exakten Wertvergleich statt der Java-Toleranz `< 0,1 °C` (`Temperature.java:82-93`); der Konstruktor prüft zusätzlich das aus `@Range` belegte Intervall 0..40, das der Java-Konstruktor erst bei der Bean-Validierung prüft. Ein `SerialNumber`-Pattern wird nicht erfunden (`UNBEKANNT`, `errorcodes.properties:43`).

### AP-2.2 – Aggregate Root Cube / House / Room / Device
- **Beschreibung:** Entitäten und Beziehungen (max. 20 Räume, 140 Geräte, Namen ≤ 32; `max.local.properties:1-3`), State-Adopter-Logik (`HeatingThermostatDeviceRoomStateAdopter`, `WallMountedThermostatDeviceRoomStateAdopter`, `RoomStateAdopter`).
- **Aufwand:** 6 PT
- **Risiken:** Kardinalität falsch; State-Adopter-Sonderfälle (Plus-Variante, Wandthermostat) übersehen.
- **Abhängigkeiten:** AP-2.1.
- **Akzeptanzkriterien:**
  1. Ein `RawCubeState` lässt sich vollständig in das Aggregat überführen (Roundtrip-Test).
  2. 21. Raum / 141. Gerät / 33. Zeichen werden abgelehnt.
  3. Domain Events für `RoomStateChanged`, `DeviceStateChanged`.

**Zwischenstand 24.11.2026 (Vorbereitung):** `MaxCube.Protocol.State.MetadataDecoder` dekodiert die Metadaten-Nutzlast der `M:`-Blöcke in Räume/Geräte (Magic 86 + Version 2 bzw. Version 1 ohne die Felder ab Version 2, `MetaDataSerializer.java:38-43`, `VersionedObjectSerializer.java:26-38`, `ObjectSerializer.java:38-58`); zwölf Tests mit künstlich gebauten Payloads. Sonnet 5.5 gab den Decoder nach Behebung eines MITTEL-Funds frei: fehlende Count-Bytes am Stream-Ende ergeben jetzt leere Listen (`ListSerializer.java:30-34`), ein fehlendes Daylight-Byte `false` (`BooleanSerializer.java:23-24`) und ein leerer Payload einen V1-Snapshot ohne Einträge – Java-Parität. Innerhalb eines Eintrags bleibt abgeschnittene Nutzlast bewusst ein Fehler. `RawCubeState.GetMetadataBase64()` wirft weiterhin bei fehlenden `M:`-Blöcken (strenger als Java, bewusste AP-1.2-Entscheidung); der Decoder selbst akzeptiert leere Payloads. Damit ist die `M:`-Seite des Roundtrip-Akzeptanzkriteriums vorbereitet; die `C:`/`L:`-Adopter und die Kardinalitätsprüfungen fehlen noch. Die Test-Payloads stammen aus Java-Code, nicht aus einem Golden-Capture (`UNBEKANNT`).

### AP-2.3 – Zeitprofile & Betriebsmodi
- **Beschreibung:** `WeekTemperatureProfile`, `DayTemperatureProfile`, `TemperatureProfileSwitchPoint` (**13 Schaltpunkte/Tag**, aufgeteilt 7 + 6 in zwei Telegrammen; **Korrektur 24.11.2026:** eine ältere Fassung nannte 6), `RoomControlMode`, Boost/Decalcification/WindowOpen-Regeln (`max.properties:2-12`, `WeekTemperatureProfileBuilder.java`; `domain-model.md:359`, `byte-abgleich-python-java.md`).
- **Aufwand:** 5 PT
- **Risiken:** Schaltpunkt-Anzahl/Zeitordnung falsch; Sommerzeit/Umschaltung (`DaylightSavingConfiguration.java`).
- **Abhängigkeiten:** AP-2.1.
- **Akzeptanzkriterien:**
  1. 7 Tage × bis zu 13 Punkte können erstellt, validiert und serialisiert werden (`DayTemperatureProfile.java:16-18`).
  2. Überlappende/unsortierte Schaltpunkte werden abgelehnt.
  3. DST-Wechsel erzeugt keinen Datenverlust (FakeClock-Test).

### AP-2.4 – Domänen-Validatoren
- **Beschreibung:** Fachregeln aus `ConfigureDeviceValidator`, `RemoteCredentialsValidator`, `SoftwareInformationValidator` und den `validators/exceptions/*` (Namen, Radio-Adresse, Gerätetyp pro Raum, Raum mit Geräten nicht löschbar).
- **Aufwand:** 4 PT
- **Risiken:** Regeln aus Bytecode unvollständig rekonstruiert.
- **Abhängigkeiten:** AP-2.2.
- **Akzeptanzkriterien:**
  1. Jede `MAXxxxx`-Validierungsausnahme hat einen Testfall.
  2. `RemovedRoomStillHasDevicesAssociated` wird vor dem Löschen ausgelöst.
  3. Validatoren sind Pure Functions ohne IO.

**Phase-2-Gesamtaufwand: 19 PT**

---

## Phase 3 – Persistenz

### AP-3.1 – SQLite-Schema & Migrator
- **Beschreibung:** Tabellen `settings`, `cubes`, `rooms`, `devices`, `device_states`, `week_profiles`, `switch_points`, `metadata_cache`, `audit_log` (siehe `docs/target-architecture.md` §7), Code-First-Migrator, `Data Source=:memory:` für Tests.
- **Aufwand:** 4 PT
- **Risiken:** Schema-Instabilität; parallele Schreiber.
- **Abhängigkeiten:** V1, AP-2.2.
- **Akzeptanzkriterien:**
  1. `dotnet run -- migrate` erzeugt Schema auf leerer DB und ist idempotent.
  2. Alle Tabellen haben PK/FK; `Serial`/`RadioAddress` indiziert.
  3. Keine Fachlogik in SQL-Migrationen.

### AP-3.2 – Repositories & SettingsStore
- **Beschreibung:** `ICubeStateStore`, `IRoomRepository`, `ISettingsStore`; Upsert nach Fremdschlüssel; Lazy-Load nicht nötig (kleine Datenmenge); Einstellungen aus `Default.settings` fachlich übernehmen (`extracted/localbackend/Default.settings:1-9`).
- **Aufwand:** 4 PT
- **Risiken:** Cache-Divergenz; falsche Pfade pro OS.
- **Abhängigkeiten:** AP-3.1.
- **Akzeptanzkriterien:**
  1. Reconnect überschreibt Cache konsistent (Upsert, keine Duplikate).
  2. Einstellungen werden plattformgerecht unter `%LOCALAPPDATA%/MaxCube` bzw. `~/.config/MaxCube` gespeichert (nicht `~/.MAX`).
  3. `ISettingsStore` hat keine statischen Methoden (Unterschied zum Original `SettingsProvider.java:35-76`).

### AP-3.3 – Audit & Fehlercode-Persistenz
- **Beschreibung:** Befehle und `MAXxxxx`-Codes in `audit_log`, strukturiertes Logging (ersetzt Log4j, `extracted/localbackend/log4j.xml`); Log-Retention konfigurierbar (Original `SwipeLogsOlderThanDays=7`, `Default.settings:3`; `Application.deleteLogsOlderThan():287-295`).
- **Aufwand:** 2 PT
- **Risiken:** Log-Volumenspitzen bei Verbindungsproblemen.
- **Abhängigkeiten:** AP-3.1.
- **Akzeptanzkriterien:**
  1. Jeder fehlgeschlagene Befehl erzeugt genau einen Audit-Eintrag mit Code.
  2. Retention löscht Einträge > 7 Tage (konfigurierbar), getestet mit FakeClock.

**Phase-3-Gesamtaufwand: 10 PT**

---

## Phase 4 – Businesslogik (Application Layer)

### AP-4.1 – Commands & Queries
- **Beschreibung:** Use Cases fachlich aus `essentials/.../core/command/*.java`: `SetRoomAutoMode`, `SetRoomPermanentMode`, `SetRoomTemporaryMode`, `SetRoomComfortTemperature`, `SetRoomEcoTemperature`, `SetRoomWindowOpenTemperature`, `SetRoomMaximumTemperature`, `SetRoomTemperatureOffset`, `SetRoomConfigBoost`, `SetRoomConfigDecalcification`, `SetRoomConfigValveOffset`, `SetRoomWeekTemperatureProfile`, `SetDeviceDisplaySetPointActual`, `SetDeviceDaylightSavingMode`, `AssociateNewDeviceToRoom`, `DisassociateDeviceFromRoom`, `ConfigureDevice`.
- **Aufwand:** 8 PT
- **Risiken:** Reihenfolge/Abhängigkeiten mehrerer Teilbefehle (z. B. Modus + Temperatur); Rollback bei Teilfehler.
- **Abhängigkeiten:** AP-1.4, AP-2.2, AP-3.2.
- **Akzeptanzkriterien:**
  1. Jeder Use Case hat genau einen Handler mit `ICommandHandler<T>` und Tests.
  2. Ein fehlgeschlagener Cube-Befehl erzeugt keine persistierte Teiländerung (Transaktionsgrenze).
  3. NAME-Konvention und DTO-Felder decken sich mit `max-dwr.xml:53-67` (fachliche Vollständigkeit).

### AP-4.2 – State-Synchronisation & Events
- **Beschreibung:** Mapping `RawCubeState` → Domain → SQLite → UI-Events; `IMessenger`-Nachrichten; Airtime/LastPing (`MaxCubeAirtimeStatus`, `MaxCubeLastPing`); Duty-Cycle-Warnung (`js/com_structure/views/dutyCycleFullView.js`).
- **Aufwand:** 5 PT
- **Risiken:** Event-Storm bei 5-s-Watchdog; UI hinkt hinterher.
- **Abhängigkeiten:** AP-1.2, AP-2.2, AP-3.2.
- **Akzeptanzkriterien:**
  1. Pro State-Update maximal ein `CubeStateChanged`-Event pro Aggregate.
  2. UI-Test: Event ändert ViewModel-Property ohne Poll-Schleife.
  3. Duty-Cycle-Warnung erscheint bei belegtem Schwellwert (Quelle in Doku).

### AP-4.3 – Discovery, Watchdog, Reconnect
- **Beschreibung:** Cube-Finder (Subnet-Scan, `cubeScanTime=1800`), `ConnectionWatchDog` (5 s), `idleTimeBeforeLocalAppDisconnect=30000`, `cubeIdleTimeToSend=900000`, automatischer Reconnect mit Backoff.
- **Aufwand:** 5 PT
- **Risiken:** Broadcast in IPv6/Container nicht verfügbar; zu aggressive Reconnects.
- **Abhängigkeiten:** AP-1.1, AP-1.3, AP-3.2.
- **Akzeptanzkriterien:**
  1. discovery findet einen Fake-Cube im lokalen Subnetz (Integrationstest mit Loopback).
  2. Nach Socket-Abbruch reconnectet der Dienst within konfigurierbarem Fenster.
  3. Backoff ist mit FakeClock testbar; keine Busy-Loops.

### AP-4.4 – (verworfen) Remote-Proxy-Adapter
- **Beschreibung:** ~~Fachliche Entsprechung zu `IMaxRemoteProxy`, `RemoteCredentials`, `ActivateProductRequest`, `CheckProductActivationRequest` (`localbackend/.../remoteproxy/*`). Feature-Flag, standardmäßig aus.~~ **Entscheidung 24.11.2026: verworfen.** Die Opcode-Familie ist inzwischen vollständig `CODE-BELEGT` (HTTP-Cloud-Protokoll, Abschnitt 6.1 der Protokolldoku); die MAX!-Cloud ist seit 01.07.2023 abgeschaltet. Eine lokale Integration braucht weder Fernzugriff noch Produktaktivierung noch Zugangsdaten. Der Aufwand von 6 PT entfällt; `Phase-4-Gesamtaufwand` sinkt damit von 30 PT auf 24 PT.
- **Aufwand:** 6 PT
- **Risiken:** Externe Dienste (`http://max.eq-3.de/cube`, `Default.settings:1-9`) nicht mehr erreichbar; Datenschutz.
- **Abhängigkeiten:** AP-4.1, AP-3.2.
- **Akzeptanzkriterien:**
  1. Deaktiviert: keine outbound HTTP-Verbindung beim Start.
  2. Aktiviert: Credentials verschlüsselt in SQLite, nie im Log.
  3. Alle Fehlercodes `MAX1037/MAX10038/MAX1039/MAX1046/MAX1047` abgedeckt (errorcodes.properties).

**Phase-4-Gesamtaufwand: 24 PT (AP-4.4 verworfen, 24.11.2026)**

---

## Phase 5 – Benutzeroberfläche

### AP-5.1 – Shell, Navigation, Tray, Styles
- **Beschreibung:** Avalonia `MainWindow`, `INavigationService`, `IDialogService`, Systemtray (Original: `MaxSystemTrayHandler.java`), Theme, 6 Sprachen (`MaxLocalAppStarter_*.properties` fachlich als Textquelle), Lizenzhinweis (`License.txt`).
- **Aufwand:** 6 PT
- **Risiken:** Tray-Unterschiede Linux/macOS; Fonts/Übersetzungen.
- **Abhängigkeiten:** V1, AP-4.2, `docs/gui-specification.md`.
- **Akzeptanzkriterien:**
  1. App startet ohne Browser; Navigation funktioniert per Maus und Tastatur.
  2. Sprachwechsel zur Laufzeit aktualisiert alle Texte.
  3. Tray: Start/Status/Beenden wie fachlich spezifiziert.

### AP-5.2 – Raumsteuerung & Geräteansicht
- **Beschreibung:** `RoomControlView`, Temperaturwahl (comfort/eco/offset/boost), Modus (Auto/Manuell/Permanent/Temporär), Fenster-offen-Status, Geräteliste, Airtime/Status-Icons. Quellen: `roomControlController.js`, `roomTemperatureView.js`, `roomDeviceView.js`.
- **Aufwand:** 8 PT
- **Risiken:** Many small states; falsche Reihenfolge Mechanismen (Raum vs. Gerät); 0,5-°C-Raster.
- **Abhängigkeiten:** AP-4.1, AP-4.2, AP-5.1.
- **Akzeptanzkriterien:**
  1. Jeder Modus aus `RoomControlMode.java` ist erreichbar und sendet den richtigen Use Case.
  2. Temperaturen werden im 0,5-°C-Raster angezeigt und validiert.
  3. UI-Tests ohne echten Cube (Mock `ICubeGateway`).

### AP-5.3 – Wochenprogramm & Gerätekonfiguration
- **Beschreibung:** `WeeklyProgramView` (7 Tage, Schaltpunkte, Kopieren von Tagen), Geräte-Konfiguration (Heizkörperthermostat, Wandthermostat-Display, Fensterkontakt-Zeit, Taster-Konfiguration, Entkalkung, Offset). Quellen: `weeklyProgramController.js` (108 KB!), `com_configuration/*`.
- **Aufwand:** 10 PT
- **Risiken:** Großter UI-Umfang; Copy-/Undo-Semantik; 13 Schaltpunkte pro Tag (7 + 6 in zwei Telegrammen).
- **Abhängigkeiten:** AP-2.3, AP-4.1, AP-5.1.
- **Akzeptanzkriterien:**
  1. Alle 7 Tage und Schaltpunkte bearbeitbar; Copy-Day funktioniert.
  2. Ungültige Profile werden vor dem Senden abgelehnt (Validator).
  3. Jede Konfigurationsoption aus `max-dwr.xml:53-67` hat ein UI-Feld.

### AP-5.4 – Struktur, Discovery, Netzwerk, Zeit, Update
- **Beschreibung:** Wizard "Neues Gerät" (Inclusion-Mode 60 s), Raum/Gerät umbenennen/löschen/verschieben, Cube-Netzwerk/IP, Zeiteinstellung/NTP, Firmware-Update-Ansicht. Quellen: `js/com_structure/**`, `cubeNetworkSettingController.js`, `setCubeTimeController.js`, `localCubeUpdateController.js`.
- **Aufwand:** 10 PT
- **Risiken:** Inclusion-Mode-Timeouts; Zustände ohne Ausweg; Firmware-Freigabe (AP-1.6).
- **Abhängigkeiten:** AP-1.3, AP-1.6, AP-4.3, AP-5.1.
- **Akzeptanzkriterien:**
  1. Wizard zeigt klare Fortschritts-/Fehlerzustände (Original-Ansichten als Katalog: `foundNoDeviceView.js`, `deviceLimitReachedView.js`, `roomLimitReachedView.js`, `noMoreDevicesPossibleView.js`).
  2. Löschen eines Raums mit Geräten ist blockiert und begründet.
  3. Firmware-Write ist ohne Feature-Flag nicht ausführbar.
  4. Zustände ohne Ausweg (z. B. abgebrochener Wizard) sind mit `Abbrechen`/`Zurück` erreichbar.

### AP-5.5 – Einstellungen, Status, Fehlerdialoge, Abnahme
- **Beschreibung:** Proxy, Remote, Sprache, Log-Retention, Status/Fehler (`MAXxxxx` als lesbare Meldung), Duty-Cycle, Verbindungsverlust, Produktaktivierung. Abnahmetest gegen `docs/gui-specification.md` und `docs/features.md`.
- **Aufwand:** 6 PT
- **Risiken:** Fehlende Alt-Funktionen werden erst in Abnahme sichtbar.
- **Abhängigkeiten:** AP-5.1 bis AP-5.4, AP-4.4 (wenn aktiv).
- **Akzeptanzkriterien:**
  1. Jede Ansicht aus `docs/gui-specification.md` ist vorhanden oder explizit als "nicht implementiert (P2)" dokumentiert.
  2. Alle 53 Fehlercodes liefern eine definierte UI-Meldung (Fallback `MAX0001`, `errorcodes.properties:1`).
  3. Cross-Platform-Smoke-Test auf win-x64, linux-x64, osx-x64, osx-arm64.

**Phase-5-Gesamtaufwand: 40 PT**

---

## 6. Gesamtübersicht & Reihenfolge

| Phase | Arbeitspakete | Aufwand | Blocker |
|---|---|---|---|
| 0 | V1–V3 | 3–6 PT | — |
| 1 | AP-1.1 … AP-1.6 | 34 PT (+6) | Golden-Capture für 1.5/1.6 |
| 2 | AP-2.1 … AP-2.4 | 19 PT | Protokoll-Doku (AP-1.4) |
| 3 | AP-3.1 … AP-3.3 | 10 PT | Domänenmodell (AP-2.2) |
| 4 | AP-4.1 … AP-4.4 | 24 PT | Protokoll + Domäne + Persistenz |
| 5 | AP-5.1 … AP-5.5 | 40 PT | Businesslogik + GUI-Spezifikation |
| **Summe** | | **130 PT (+12 optional)** | ≈ 6–7 Monate 1 Person |

**Kritischer Pfad:** `V3 → AP-1.1 → AP-1.2 → AP-1.3 → AP-1.4 → AP-1.5 → AP-4.1 → AP-5.3`
(AP-1.5 Radiogramm ist derivationstheoretisch das höchste Risiko; deshalb vor jeder UI-Arbeit abschließen.)

**Empfohlene Meilensteine:**
- M1 (nach AP-1.3): Fake-Cube-Test suite grün, Handshake + State-Empfang verifiziert.
- M2 (nach AP-1.5): Alle Opcodes und Radiogramme bytegleich gegen Golden-Capture.
- M3 (nach AP-3.2): Domänenzustand überlebt Reconnect und App-Neustart.
- M4 (nach AP-4.2): Erster fachlicher End-to-End-Use-Case (Raumtemperatur setzen) ohne UI.
- M5 (nach AP-5.5): Feature-Matrix P0 vollständig, Cross-Platform-Smoke grün.

---

## 7. Explizit offene Punkte (keine Fakten, sondern offene Fragen)

1. **Radiogramm-Bitfelder** vollständig verifiziert? → **teilweise geklärt 24.11.2026:** der erste `0x10`-Telegramm (7 Punkte) ist `LIVE-BEWÄHRT` für Kodierung und Versand, nicht aber für die Annahme durch den Cube (`sent_not_confirmed`, keine `A:`-Quittung belegt). Das zweite Telegramm (7 + 6) bleibt `UNBEKANNT`; weiterhin kein Golden-Capture mit > 7 Punkten. Bis dahin INFERENZ.
2. **UDP-Firmware-Frame** exakt? → nur Opcode-Tabelle und URL-Konstruktion belegt (`CubeUdpProxy.java:105`).
3. **Duty-Cycle-Schwellwert** numerisch? → UI-Ansicht vorhanden, Wert bisher nicht in Quellen gefunden.
4. ~~**Maximale Schaltpunkte pro Tag** = 6?~~ **Geklärt 24.11.2026:** 13 pro Tag, 7 + 6 in zwei `0x10`-Telegrammen. Der erste Telegramm (7 Punkte) ist per Python-Integration `LIVE-BEWÄHRT` für Kodierung und Versand; das zweite Telegramm mit Flag 1 / Offset 7 bleibt `UNBEKANNT` (kein Capture mit mehr als 7 Punkten). Siehe `docs/ha-maxcube-erkenntnisse.md`.
5. **Remote-Portal-Endpunkte** noch erreichbar? → nicht getestet (keine Netzwerkzugriffe auf `max.eq-3.de` im Rahmen dieser Analyse).
6. **Verhalten bei mehreren Cubes** im selben Netz? → CubeFinder vorhanden, Auswahl-UI nicht abschließend belegt.

Diese Punkte sind in `docs/maxcube-protocol.md` und `docs/features.md` mit `UNBEKANNT` markiert und dürfen erst nach Beleg in Implementierung überführt werden.

---

## 8. Zwischenstand 24.11.2026 – lokale erste Version nach vier Sonnet-Prüfungen

Die Phasen 2 bis 5 sind für den lokalen Kern soweit umgesetzt, dass die App ohne Hardware startet und einen vollständigen fachlichen Ablauf (Verbinden → Räume anzeigen → Temperatur/Modus/Wochenprofil senden → Cache/Audit) bietet. **BEOBACHTUNG**, kein Hardwarebeweis: der End-to-End-Test läuft über `FakeGateway` und `FakeStore`, `CubeGateway` ist inzwischen mit ersetzbarem Transport getestet (EOF-Fault, `q:` beim Shutdown), aber nicht gegen einen physischen Cube. Auch die Avalonia-Tests sind codebasiert (CanExecuteChanged, Warning, Eingabe), keine Laufzeit-Klicktests.

| Arbeitspaket | Status 24.11.2026 |
| --- | --- |
| AP-1.5 Radiogramm-Builder | `RadiogramBuilder` (Bitfelder LSB-first, 17-Byte-Standardtelegramme, `0x10/0x11/0x40`) und `WeekProfileSerializer` (7 + 6 Punkte, Java-Längen 25/23 Byte, `ToBytes(minimumLength: 0)`) umgesetzt. Byte-Paare gegen `docs/byte-abgleich-python-java.md` getestet (20,5 °C/06:00 → `52 48`; 4,5 °C/24:00 → `13 20`; 30,5 °C/20:00 → `7A F0`). Die 24-Bit-Funkadresse wird jetzt byteweise big-endian geschrieben (`ByteUtil.java:8-20`); 10:45 wird wie Java auf 11:00 gerundet. Erstes `0x10`-Telegramm live für Kodierung/Versand bewährt, zweites `UNBEKANNT`. |
| AP-2.2 Aggregat & State-Adopter | `CubeStateAssembler` baut `Cube` aus `H:`/`C:`/`M:`/`L:` inklusive Konfiguration und Telemetrie; `Cube`/`Room`/`Device`, `DomainLimits` (20/140/32, INFERENZ) und `DomainEvents`. Neun Aggregate-Tests. Nach Sonnet-Fund korrigiert: `L:`-Offsets (Modus `state[0]`, Sollwert `state[2]`, Ist low `state[6]`), Fenster-/Boost-Dauer über Java-VALUES-Tabelle, zu kurze Blöcke werden einzeln übersprungen, verwaiste Geräte faulten nicht, `Cube.AddDevice` mutiert erst nach allen Prüfungen. Nach Sonnet-Runde 2: Geräteparameter nur bei `isValid` (`DeviceStateSerializer.java:64-69`), pro Gerät `try/catch`, Raumwerte vom Master statt vom letzten Gerät (`:73-98`), Ist-Temperatur als `MeasuredTemperature` ohne AUS/AN-Normalisierung; Werte außerhalb 0..40 °C werden verworfen („keine Messung“) statt erfunden geklemmt (Runde 3, N4). |
| AP-2.3 Zeitprofile | `WeekTemperatureProfile` in der Domäne (maximal 13 Punkte/Tag); `SwitchPointViewModel` textbasiert für XAML-Binding; `DayProfile` wird unverändert an das Gateway gereicht. `DayProfile.Validate` verlangt aufsteigende Zeiten im 5-Minuten-Raster, 0,5-°C-Schritte und 00:00 (= 24:00) als letzten Punkt; die UI erzeugt sendbare Standardtage. |
| AP-3.1/3.2 SQLite & Store | `SqliteStateStore` mit `settings`, `cubes`, `rooms`, `devices`, `audit_log`; Upsert/Replace pro Serial verhindert Duplikate bei Reconnect; `Data Source=:memory:`-Tests. Audit wird unbegrenzt gespeichert; `ReadAuditAsync` liefert die letzten 500 Einträge, die UI zeigt davon die letzten 100; `DisposeAsync` wartet auf das Gate. |
| AP-4.1/4.2 Application Layer | `CubeApplicationService` als einzige Schreibstelle: `Connect`/`LoadCached`/`SetRoomTemperature`/`SetRoomControlMode`/Eco/Comfort/`SetWeekDayProfile`/`Disconnect`, ein `SemaphoreSlim`-Gate pro Operation, Cache + Audit + `StateChanged`-Event. Der gecachte Offline-Zustand ist schreibgeschützt; Fehler beim Cache-Schreiben nach Connect schließen die Session; Domain-Events werden nach Operationen gedraint. Nach Runde 2: Cache-/Audit-Fehler **nach** dem Senden melden `Warning` statt „nicht gesendet“; Temporary-Endzeit muss in der Zukunft liegen (`SetRoomTemporaryMode.java:19-21`); `LoadCached` funktioniert auch nach `Faulted`; Teilfehler des Wochenprofils werden auditiert. Runde 3: Sollwerte werden vor Senden und Cache auf das 0,5-°C-Drahtgitter quantisiert (N5), `Fault()` läuft vor `Fail()` (N3). |
| AP-5.1/5.2/5.3 UI (Kern) | Avalonia `MainWindow` mit Räume / Wochenprofil / Status; ViewModels ohne Controls (zwölf UI-Tests); DI in `App.axaml.cs`, nativer Start in `Program.cs`. Runde 1: `DataContext` im Composition Root gesetzt, `StateChanged`/`ConnectionChanged` über `IUiDispatcher` auf den UI-Thread gemarshallt, Port als `NumericUpDown`, `WinExe`. Runde 2: `[NotifyCanExecuteChangedFor]` an `IsConnected`/`SelectedRoom` (Buttons aktivieren sich), Temporary-Zeit aus `DateTimeOffset.Now` (Ortszeit wie `Calendar.getInstance()`, `RadiogramSpecification.java:187-191`), Eingabefeld springt nicht zurück, `CubeGateway` ist `IAsyncDisposable` und sendet beim Beenden `q:`. Runde 3/4: eigener `WarningText` (Warnung wird nicht von „Getrennt“ überschrieben und nach der nächsten Aktion geleert), State-Refresh mit gleicher Raumauswahl überschreibt die Eingabe nicht (N6/N8); beim Erstladen und Reconnect wird der Raum-Sollwert dagegen übernommen (N9). |

**Verifikation:** `dotnet test MaxCube.slnx` → **232/232** grün (Domain 55, Application 15, Protocol 143, Infrastructure 7, UI 12); `dotnet build` 0 Warnungen/0 Fehler; Start mit lokalem .NET-10-Host stabil. `raw/` unverändert, keine Geheimnisse ergänzt.

**Sonnet-Gesamtprüfung, Runde 1 (24.11.2026):** `anthropic/claude-sonnet-5-5` bestätigte die Build-/Testzahlen, gab die Version aber **nicht frei**. Bestätigte Funde: fehlende `DataContext`-Verdrahtung (K1), little-endian Funkadresse (K2), verschobene `L:`-Offsets (H1), fehlendes UI-Thread-Marshalling (H2), falsche 10:45-Rundung (H3), fehlende Wochenprofil-Validierung (H4) sowie M1–M8. Alle Punkte sind behoben und mit Tests unterlegt.

**Sonnet-Gesamtprüfung, Runde 2 (24.11.2026):** erneut **nicht freigegeben**, aber bestätigt: K1/K2, H1–H4 und M1–M8 sind wirklich behoben, Java-Quellen vorhanden (480 `*.java`), 7+6-Serialisierung und Byte-Paare Java/Python-konform. Neue Funde: H-A (`CanExecuteChanged` wurde nie ausgelöst → Buttons blieben deaktiviert), H-B (Temporary-Endzeit in UTC statt Ortszeit), H-C (ein Gerät mit 45,6 °C Ist brach den Connect ab; `isValid` wurde nicht geprüft), M-A (Cache-Fehler nach Senden), M-B (nur `CubeTransportException` setzte `Faulted`), M-C (Ist-Temperatur wurde als Sollwert normalisiert), M-D (Eingabefeld sprang zurück), M-E (Raumwerte vom falschen Gerät), M-F (Profil-Editor lädt kein Cube-Profil), M-G (kein `q:` beim Shutdown). H-A/B/C und M-A/B/C/D/E/G sind behoben; M-F ist als bekannte Lücke in der UI und in `README.md` ausgewiesen.

**Sonnet-Gesamtprüfung, Runde 3 (24.11.2026):** erneut **nicht freigegeben**, aber ohne kritische oder hohe Funde. Bestätigt: alle Runde-2-Korrekturen im Code vorhanden, kein Deadlock in `SendAsync`, `DisposeAsync` doppelaufrufsicher, Warning-Threading korrekt. Funde: N1 (Doku sagte „alle mit Tests unterlegt“, obwohl Gateway/Clock/M-D ungetestet waren), N2 (Master-Test bewies die Master-Regel nicht), N3 (Race: `Fail()` vor `Fault()`), N4 (Klemmen erfand 40,0 °C), N5 (Sollwert wurde nicht quantisiert), N6 (Warnung ging beim Trennen verloren), N7 (Gate blockiert Trennen bis zu 130 s), N8 (Eingabe wurde bei StateChanged zurückgesetzt). N1–N6 und N8 sind behoben und mit Tests unterlegt; N7 bleibt als NIEDRIG ausgewiesen.

**Sonnet-Diff-Nachprüfung, Runde 4 (24.11.2026):** erneut **nicht freigegeben**, aber bestätigt: N1–N6 sind im Code und in den Tests wirklich behoben, keine neue Master-/Gateway-/Quantisierungsregression. Neuer Fund N9 (MITTEL): Die N8-Unterdrückung galt auch beim Erstladen und nach `Disconnect` (`previousId == null`), sodass der Raum-Sollwert nicht ins Eingabefeld übernommen wurde. Behoben durch Unterdrückung nur bei gleicher, bereits vorhandener Auswahl; Tests mit 19,5 °C für Connect und Reconnect. N10 (NIEDRIG): `WarningText` wurde nie geleert; wird jetzt zu Beginn jeder Aktion zurückgesetzt. Zusätzlich Tests für Permanent-/Temporary-Quantisierung und ein veralteter Testname korrigiert.

**Abschluss-Diff-Sichtung (24.11.2026):** `anthropic/claude-sonnet-5-5` hat die lokale erste Version **`FREIGEGEBEN`**. Bestätigt: N9 und N10 sind im Code und in den Tests behoben; 232/232 grün, Build sauber. Hardwarevalidierung bleibt `UNBEKANNT`; N7 und M-F sind als NIEDRIG/Lücke dokumentiert. Kosmetisch bleibt ein fehlender Einzeltest für den echten Raumwechsel (im Code eindeutig über `OnSelectedRoomChanged`).

**Weiter offen (keine Blocker für die lokale erste Version):** Golden-Capture mit > 7 Punkten je Tag, fachlich ausgewertete `S:`-Antwort (Java quittiert `s:` mit `S:`, nicht `A:`; `A:`-Quittung bleibt `UNBEKANNT`), ~~Laden des Cube-Wochenprofils in den Editor (M-F)~~ **erledigt, siehe Abschnitt 9**, ~~Entkopplung des Gateway-Semaphors (N7)~~ **erledigt, siehe Abschnitt 9**, 60-s-Inclusion-Überwachung, ~~Sitzungsüberwachung in der Orchestrierung~~ **erledigt, siehe Abschnitt 9**, Gerätekonfiguration/Discovery/Wizard, Cross-Platform-Smoke, sowie alle Punkte aus Abschnitt 7.

---

## 9. Gremium 24.11.2026 – Runde 1 umgesetzt

Auftrag des Nutzers: ein dreiköpfiges Gremium arbeitet autark, mindestens drei Runden;
erst nach vollständiger Abarbeitung ist Schluss. Sonnet wurde auf Wunsch (kein Guthaben)
durch das kostenlose `opencode/deepseek-v4.1-flash-free` ersetzt. Beschluss und Mitnahmen
stehen in `docs/gremium.md`.

| Punkt | Umsetzung | Beleg |
| --- | --- | --- |
| M-F Wochenprofil laden | `WeekProfileDecoder` liest `config[11..]`: 7 Tage Samstag→Freitag, 13 Einträge à 2 Byte, `(value & 511)*5` Minuten, `((value>>9)&63)/2` °C, erstes `00:00` beendet den Tag. `Cube.ApplyWeekProfile`, `Room.WeekProfile`, Snapshot/Migration, Sendesperre ohne geladenes Profil; UI lädt bei Raumwechsel/`StateChanged`. | `src/MaxCube.Protocol/State/WeekProfileDecoder.cs`, `src/MaxCube.UI/ViewModels/MainWindowViewModel.cs` |
| N7 Abbrechen/Timeouts | `OperationGate` mit begrenztem Gate-Erwerb und `CancelActive()`; `CubeGateway`/`CubeApplicationService` auf Leases und Token umgestellt; `q:`/Cleanup/Shutdown mit Zeitbudgets (250/700/500 ms, äußere 2 s); UI-`CancelCommand` mit eigenem CTS, `OperationCanceledException` → „Abgebrochen“; asynchrones Shutdown ohne blockierendes `GetResult()`. | `src/MaxCube.Application/Abstractions/OperationGate.cs`, `src/MaxCube.Infrastructure/Connectivity/CubeGateway.cs`, `src/MaxCube.UI.Desktop/App.axaml.cs` |
| Sitzungsüberwachung | `CubeSessionMonitor` im Gateway verdrahtet; `l:`-Refresh bei Inaktivität (Standard 30 s, konfigurierbar), Gerätewerte werden per `CubeStateAssembler.ApplyDeviceStates` in das bestehende Aggregat übernommen und in den Cache geschrieben; `StaleChanged`/Reconnect mit exponentiellem Backoff (1…30 s). | `src/MaxCube.Protocol/Session/CubeSessionMonitor.cs`, `src/MaxCube.Infrastructure/Connectivity/CubeGateway.cs`, `src/MaxCube.Application/Services/CubeApplicationService.cs` |
| Mitnahmen | Geräteliste nach `RoomId` gefiltert, `SuccessText`/`WarningText` sichtbar, `start.cmd` sucht ein .NET-10-SDK und baut immer inkrementell neu, Audit-Doku korrigiert (unbegrenzt gespeichert, 500 gelesen, 100 angezeigt). | `src/MaxCube.UI/ViewModels/MainWindowViewModel.cs`, `start.cmd`, `README.md` |

**Verifikation nach Runde 1:** `dotnet build` 0 Warnungen/0 Fehler, `dotnet test MaxCube.slnx`
**243/243** grün (Domain 55, Application 20, Protocol 147, Infrastructure 9, UI 12).
`start.cmd`-Smoke: SDK-Fund, inkrementeller Build und App-Start beobachtet; Prozess
anschließend kontrolliert beendet. Keine Hardwarevalidierung - `UNBEKANNT`.

---

## 10. Gremium 24.11.2026 - Runde 2 umgesetzt

Alle drei Mitglieder setzten den Session-/Reconnect-Pfad auf Platz 1. Der bindende
Beschluss und die Gegenprüfung jeder Stimme stehen in `docs/gremium.md`.

| Punkt | Umsetzung | Beleg |
| --- | --- | --- |
| Session-/Reconnect-Pfad | Neue `MarkUnreachableAsync()` räumt Queue/Transport/Session nach `Unreachable` und drei `l:`-Fehlern auf und feuert `SessionUnreachable` nur einmal. `ConnectAsync` wartet vor dem Neustart auf den alten Monitor-Task; auch der Gate-Timeout von `DisconnectAsync` setzt alle Felder zurück. | `src/MaxCube.Infrastructure/Connectivity/CubeGateway.cs`, `tests/MaxCube.Infrastructure.Tests/CubeGatewayTests.cs` |
| M-F Ende-zu-Ende | Sendesperre jetzt pro Wochentag (`HasWeekProfile(roomId, day)`), damit ein verworfener Tag nicht mit 21,0 °C überschrieben wird. Neuer Assembler-Test mit vollständigem 211-Byte-C:-Block; UI-Test für „Profil geladen → Senden gesperrt/erlaubt“. | `src/MaxCube.Application/Services/CubeApplicationService.cs`, `src/MaxCube.UI/ViewModels/MainWindowViewModel.cs`, `tests/MaxCube.Protocol.Tests/CubeStateAssemblerTests.cs`, `tests/MaxCube.UI.Tests/ViewModelTests.cs` |
| Mitnahmen | `StatusText` nach Abbruch wiederhergestellt und bei Stale/Reconnect sichtbar; die nie ausgewertete Option `RefreshInterval` entfernt (`IdleTimeout` steuert den 30-s-`l:`-Refresh); N3-Test (`Fault()` vor `Fail()`) ergänzt. | `src/MaxCube.UI/ViewModels/MainWindowViewModel.cs`, `src/MaxCube.Protocol/CubeConnectionOptions.cs`, `tests/MaxCube.Protocol.Tests/CubeCommandQueueTests.cs` |

**Verifikation nach Runde 2:** `dotnet build` 0 Warnungen/0 Fehler,
`dotnet test MaxCube.slnx` **249/249** grün (Domain 55, Application 21, Protocol 149,
Infrastructure 10, UI 14). Der vorher rote Reconnect-Test ist grün. Hardware bleibt
`UNBEKANNT`. i18n (nur eine von drei Stimmen) und Inclusion sind ausdrücklich
zurückgestellt; Audit-Retention bleibt eine dokumentierte Einschränkung.

---

## 11. Gremium 24.11.2026 - Runde 3 abgeschlossen

Zwei Stimmen (`tidepool-alpha-free`, `nemotron-3.5-lightning-free`) gaben
`FREIGEGEBEN`, die unabhängige Prüfung (`deepseek-v4.1-flash-free`) wegen eines
UI-Blockers `NICHT FREIGEGEBEN`. Nach Gegenprüfung am Code wurde der Block als
bindender Runde-3-Beschluss übernommen und vollständig umgesetzt.

| Punkt | Umsetzung | Beleg |
| --- | --- | --- |
| Reconnect in der UI | `OnConnectionChanged` behandelt jetzt auch `Connected`: `IsConnected = true`, Status „Verbindung wiederhergestellt“. Der zuvor gesperrte Aktionspfad ist nach Auto-Reconnect wieder bedienbar; ein erneuter Connect läuft nicht mehr in „Already connected“. Regressionstest über den echten Service. | `src/MaxCube.UI/ViewModels/MainWindowViewModel.cs`, `tests/MaxCube.UI.Tests/ViewModelTests.cs` |
| Geräteliste nach Raumwechsel | `OnSelectedRoomChanged` ruft `RefreshDevices` auf; die Liste zeigt sofort die Geräte des neuen Raums. Regressionstest mit zwei Räumen/Geräten. | `src/MaxCube.UI/ViewModels/MainWindowViewModel.cs`, `tests/MaxCube.UI.Tests/ViewModelTests.cs` |
| Mitnahmen | Cancel-Hinweis wird vor dem Abbruch gesetzt (kein stehenbleibendes „Abbrechen …“ mehr); XAML-Stale-Text an den StatusText angepasst. | `src/MaxCube.UI/ViewModels/MainWindowViewModel.cs`, `src/MaxCube.UI/Views/MainWindow.axaml` |

**Abschlussverifikation:** `dotnet build` 0 Warnungen/0 Fehler,
`dotnet test MaxCube.slnx` **251/251** grün (Domain 55, Application 21, Protocol 149,
Infrastructure 10, UI 16). Damit ist das Gremium nach drei Runden abgeschlossen und
der hardwarefreie, lokale Funktionsumfang **FREIGEGEBEN**. Hardware bleibt
`UNBEKANNT`. Das Max-Retry-Limit wurde bewusst nicht eingebaut (Runde-1-Beschluss:
endloses Backoff 1…30 s). i18n, Inclusion und Audit-Retention bleiben
zurückgestellt. Vollständige Stimmen und Gegenprüfungen: `docs/gremium.md`.

---

## 12. Gremium Runde 4 – Versionierung, Autodiscovery, leicht/mittel (24.11.2026)

**FAKT – Ausgangslage:** Die App hatte keine eigene Versionsanzeige (nur die
Cube-Firmware im StatusText) und keine UDP-Discovery. `docs/features.md` hatte
keine Statusspalte; `docs/INDEX.md:20` nannte 78 statt tatsächlich 61 Feature-Zeilen.

**Stimmen (Kurzfassung, alle kostenlos):**
- Produkt/UX (`tidepool-alpha-free`): Version + Discovery bestätigt; P1/P2
  Haussteuerung und Wochenprogramm-Kopieren empfohlen.
- Technik (`nemotron-3.5-lightning-free`): Protokoll belegt
  (`maxcube-protocol.md:47-67`), keine UDP-Infrastruktur im Code; zentrale
  Assembly-Version und Fake-Transport-Tests empfohlen.
- Prüfung (`deepseek-v4.1-flash-free`): 251/251 selbst gemessen; Doku-Drift
  gefunden; SQLite-Schemaversion und letzten Endpoint als Mitnahmen gefordert;
  Broadcast nur benutzerinitiiert, kein Cloud-Update-Check.

**Bindender Beschluss und Umsetzung (3/3):**

| Punkt | Umsetzung | Beleg |
| --- | --- | --- |
| App-Versionierung | `<Version>0.2.0</Version>` zentral in `Directory.Build.props`; `IAppVersionProvider`/`AppVersionProvider` liest die Assembly-Informationsversion; Fenstertitel und Status-Reiter zeigen sie. SQLite: `PRAGMA user_version = 1`, `app.version`, `schema.version`, beides idempotent. | `Directory.Build.props`, `src/MaxCube.UI/AppVersionProvider.cs`, `src/MaxCube.Infrastructure/Persistence/SqliteStateStore.cs` |
| UDP-Autodiscovery | `IDiscoveryTransport` + `UdpDiscoveryTransport` (23272, Broadcast 224.0.0.1/255.255.255.255, TTL 5, 3 Pakete/50 ms), `CubeDiscoveryProtocol` (Request 19 Byte, Antwort `eQ3MaxAp`/`eQ3MaxBl`, Seriennummer 8..17, Firmware 24..25 big-endian), `CubeDiscoveryService` entdoppelt Treffer. UI: „Cube suchen", Trefferliste, „Übernehmen" füllt Host/Port 62910; letzter Endpoint in `settings`, manuelle Eingabe auf genau vier IPv4-Oktette (0..255) geprüft. | `src/MaxCube.Application/Services/CubeDiscoveryService.cs`, `src/MaxCube.Infrastructure/Connectivity/UdpDiscoveryTransport.cs`, `MainWindowViewModel.cs`, `MainWindow.axaml` |
| P1 leicht/mittel | Haussteuerung (ausgewählte Räume gemeinsam Auto/ECO/Comfort/Temporary) und „Tag auf geladene Tage kopieren" (Sendesperre pro Wochentag bleibt). | `MainWindowViewModel.cs`, `RoomViewModel.cs`, `ViewModelTests.cs` |
| Doku | Statusspalte in `features.md` (61 Zeilen: umgesetzt/teilweise/fehlt/verworfen), Feature-Zahl in `INDEX.md` korrigiert, Auto-Discovery-Quelle auf lokalen UDP-Finder korrigiert. | `docs/features.md`, `docs/INDEX.md` |

**Verifikation:** `dotnet build MaxCube.slnx` 0 Warnungen/0 Fehler,
`dotnet test MaxCube.slnx` **268/268** grün (Domain 55, Application 25, Protocol 149,
Infrastructure 11, UI 28). Discovery-Tests nutzen ausschließlich Fake-Transport und
geparste Beispielbytes; es gibt **keinen** Broadcast-, Hardware- oder
Firmware-Schreibzugriff.

~~**Offene Funktionen gegenüber dem Original (Stand Runde 4):** Inclusion, Strukturpflege,
Konfiguration, Netzwerk/Zeit, i18n, Tray/Instanzschutz u. a.~~ **Erledigt in Runde 5, siehe
Abschnitt 13.** Bewusst verworfen blieben Cloud-/Fernzugriff, Produktkonto, Proxy,
Browser-/Jetty-Shell, App-Update-Check über HTTP und Cube-Firmware-Schreiben.
Hardware-Empfang der UDP-Antwort bleibt `UNBEKANNT`.

---

## 13. Runde 5 – Alle lokal abbildbaren Features, Windows und Linux (24.11.2026)

**Verfahren (FAKT):** Alle Gremiumsaufrufe scheiterten am Free-Limit; es gibt keine
Modellstimmen. Die Umsetzung folgt den bindenden Beschlüssen der Runden 1–4 und dem
Nutzerauftrag (`docs/gremium.md`, Runde 5). Details der Entscheidungen dort.

**Nutzerergänzung 25.11.2026 (FAKT, vom Nutzer mitgeteilt, nicht extern verifiziert):**
eQ-3 hat die MAX!-Cloud-Steuerung abgeschaltet und die Cloud-App aus dem Android- und
iOS-App-Store entfernt. Das stützt den Ausschluss von Fernzugriff, Produktkonto, Proxy und
App-Update in Beschluss 1; eine lokale App muss ohne Hersteller-Cloud auskommen.

| Paket | Umsetzung | Belege |
| --- | --- | --- |
| Protokoll | Neue Telegramme `SetRoomValveFunctions` (0x12), `SetDisplayMode` (0x82), `SetGroupAddress`/`RemoveGroupAddress` (0x22/0x23), `LinkPartner` (0x20/0x21); `MetadataEncoder` (Gegenstück zum Decoder) mit Rundreise-Sicherheitsprüfung und 1900-Byte-Blöcken; `NewDeviceDecoder` (`N:`); `CubeConfigurationCodec` (Taster, Zeitzone); `CubeUdpCodec` (Netzwerk `c`/`C`, kein Firmware-Opcode). Entkalkungs-Wochentag korrigiert (Code 2 = Montag). | `src/MaxCube.Protocol/Commands/RoomCommands.cs`, `State/MetadataEncoder.cs`, `State/CubeConfigurationCodec.cs`, `State/CubeUdpCodec.cs`, `tests/MaxCube.Protocol.Tests/AdminProtocolTests.cs` |
| Domäne | `Cube.RenameRoom/RenameDevice/ReorderRooms/RemoveDevice/RemoveRoom/MoveDevice/EnsureRoomMaster/NextFreeRoomId`, Ventil-Rohwerte (`HasValveConfiguration`), atomares Verschieben mit Limitprüfung. | `src/MaxCube.Domain/Aggregates/Cube.cs`, `tests/MaxCube.Domain.Tests/StructureTests.cs` |
| Application | `ICubeAdminGateway`, `ICubeNetworkConfigurator`; `CubeApplicationService` (partial) mit Raumkonfiguration, Boost/Ventil, Anzeige, Anlernen (60 s in 10-s-Fenstern), Zuordnen, Umbenennen/Löschen/Verschieben/Sortieren nach dem **Entwurfsprinzip**, Taster, Zeitzone/Cube-Zeit/NTP, Netzwerk, gebündelte Temperaturen (3 s), Airtime-Hinweis, Diagnosebericht, Einstellungen; `ErrorCatalog` (52 Codes). | `src/MaxCube.Application/Services/CubeApplicationService.Admin.cs`, `Models/ErrorCatalog.cs`, `tests/MaxCube.Application.Tests/AdminServiceTests.cs` |
| Infrastruktur | `CubeGateway.Admin` über dieselbe Queue (ein Socket, ein Worker), `UdpNetworkConfigurator`, `AppPaths`, `InstanceLock`, `FileAppLog`, `SystemLanguage`. | `src/MaxCube.Infrastructure/Connectivity/CubeGateway.Admin.cs`, `Platform/*`, `tests/MaxCube.Infrastructure.Tests/CubeGatewayAdminTests.cs`, `PlatformTests.cs`, `UdpNetworkConfiguratorTests.cs`, `SystemLanguageTests.cs` |
| Oberfläche | Neun Reiter, grafische Tagesleiste, Schnellbearbeitung, modale Warte-/Fehlerdialoge, sechs Sprachen (`Localizer`, Vollständigkeitstest gegen XAML und Code), mehrsprachige Hilfe mit Kontext-Schaltflächen, Einstellungen, Tray, Instanzschutz-Hinweisfenster, umbrechendes Layout. | `src/MaxCube.UI/Views/MainWindow.axaml`, `Localization/*`, `ViewModels/MainWindowViewModel.*.cs`, `tests/MaxCube.UI.Tests/AdminViewModelTests.cs` |
| Plattform | `Composition.cs`, `--selfcheck`, CI-Matrix Windows+Linux (inkl. self-contained Pakete), `start.sh`, `.gitattributes`. Funde des Selbsttests: Container muss asynchron entsorgt werden; `InvariantGlobalization` macht die .NET-Kulturerkennung wirkungslos → eigene Systemspracherkennung. | `.github/workflows/ci.yml`, `src/MaxCube.UI.Desktop/*`, `start.sh` |

**Verifikation:** Build 0 Warnungen/0 Fehler; `dotnet test MaxCube.slnx` **518/518** grün unter
Windows und - in einer Debian-13-WSL2-Instanz mit Linux-SDK 10.0.404 - ebenfalls **518/518**
(Domain 64, Application 83, Protocol 198, Infrastructure 66, UI 107); `--selfcheck` unter
beiden Systemen `OK`; Windows-GUI startet stabil. **Nicht belegt:** echte Hardware für die
inferierten Schreibformate, echte UDP-Antwort, Linux-Oberfläche auf einem Desktop.

**Was bleibt (siehe README „Nächste Schritte“):** Hardwaretest und Paketmitschnitt der
`INFERENZ`-Formate, Golden-Capture für `S:`/UDP, Linux-Desktop-Sichtprüfung, optional
Drag-and-drop im Wochenprogramm-Editor.

