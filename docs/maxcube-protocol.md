# MAX! Cube – Kommunikationsprotokoll-Spezifikation (Phase 2 der REA-Analyse)

> **Status:** Technische Rekonstruktion aus statischen Artefakten. Diese Dokumentation beschreibt das Verhalten der MAX!-Local-Backend-Version `1.4.6`, wie es im dekompilierten Java-Code und den extrahierten Konfigurationsdateien vorliegt. Sie ist als Grundlage für eine Neuimplementierung ohne Originalcode gedacht.
>
> **Wichtige Kennzeichnung:** Alle Quellstellenangaben (`pfad/Datei.java:Zeile`) beziehen sich auf **JADX-dekompilierten Code** (Marker `/* JADX INFO: loaded from: ... */` in jeder Datei). Dekompilierter Code ist **keine** REA Evidence. REA Evidence-IDs werden ausschließlich dort zitiert, wo Artefakt-Identität bzw. Hash aus der REA-CLI belegt ist.

---

## 1. Quellen, Artefakt-Identität und Evidenz

| Artefakt | REA Evidence | Belegte Identität |
|---|---|---|
| `MaxLocalBackend-1.4.6.jar` | **REA Evidence `ev_b01b22f5d2b54d783aa1cb8dbe567111e03a6fe5029e1641318be232168a43a7`** | SHA-256 `693de979b66038ab5359e75564f03e0eacd526d486e5dd3393b64c2872cce8a2`, Pfad `C:\ProgramData\MAX\lib\MaxLocalBackend-1.4.6.jar`, Coverage 434/434 Container-Entries (siehe `evidence/localbackend-rea.json`) |
| `MaxEssentialsBackend-1.4.6.jar` | **REA Evidence `ev_cd71f066e83568d1ab3c71c8f78b5ea143ceb6dcf3b32ee7b5329970380de677`** | siehe `evidence/essentials-rea.json` / `essentials-evidence.json` |
| Root-WAR / JS-Client | **REA Evidence `ev_198593f77a5b718fcd92173c539c0b5f7d78752738b9e66ae993003f805d2e67`** | siehe `evidence/rootwar-rea.json` / `rootwar-evidence.json` |

Weitere lokale Evidenz-Bundles (nicht für Protokolldetails, sondern für Extraktionsnachweis): `evidence/localbackend-extract.json`, `evidence/essentials-extract.json`, `evidence/settings-evidence.json`.

**Verwendete dekompilierte Quellpfade (JADX, keine REA Evidence):**

- `app-maxcube/decompiled/localbackend/sources/de/eq3/max/il/local/` – `CubeConnection.java`, `CubeGateway.java`, `CubeConnectionCommand.java`, `ICubeConnection*.java`, alle 19 Klassen unter `commands/`, `ConnectionWatchDog.java`
- `app-maxcube/decompiled/localbackend/sources/de/eq3/max/al/local/` – `core/LocalConfiguration.java`, `finder/CubeFinder.java`, `finder/CubeConnectionInfo.java`, `update/*.java`
- `app-maxcube/decompiled/essentials/sources/de/eq3/max/al/core/` – `IOperationCodes.java`, `Constants.java`, `Conversion.java`, `radiogram/*.java`, `radiogram/specification/*.java`, `cubestate/*.java`, `cubestate/convert/*.java`, `command/*.java`
- `app-maxcube/decompiled/controller/sources/de/eq3/max/al/local/remoteproxy/` – vollständig implementiertes HTTP-Remote-Proxy-Protokoll (sieben Request-/Antwortpaare, Abschnitt 6.1); belegt auch die AES-Verwendung (`RemoteProxyRequest.java:40`, `RemoteProxyResponse.java:50-57`)
- Konfiguration: `app-maxcube/extracted/settings/max.local.properties`, `extracted/localbackend/Default.settings`, `extracted/essentials/errorcodes.properties`
- JS-Client: `app-maxcube/extracted/rootwar/js/lib/APICalls.js`, `extracted/rootwar/js/models/maxCubeModel.js`

**Einschränkung:** Es liegt kein Netzwerk-PCAP und keine Firmware-Binary vor. Alle Aussagen über das *Cube-seitige* Verhalten sind aus den Antwort-Erwartungen des Clients abgeleitet (**INFERENZ**, sofern nicht explizit im Code als Antwort verarbeitet).

---

## 2. Transport, Ports und Discovery

### 2.1 TCP-Datenkanal

- Pro Verbindung genau ein `java.net.Socket` mit `BufferedReader` (InputStreamReader, Plattform-Encoding) und `BufferedWriter` (OutputStreamWriter, Plattform-Encoding). Jede Zeile endet mit `\r\n`; der Client liest mit `readLine()`.
  Quelle: `decompiled/localbackend/sources/de/eq3/max/il/local/CubeConnection.java:46-50`.
- Portwahl (`LocalConfiguration.getConnectionPort`, `decompiled/localbackend/sources/de/eq3/max/al/local/core/LocalConfiguration.java:81-90`):
  - `62910` (`DEFAULT_CUBE_PORT`, Zeile 12) für echte Cubes.
  - `max.connection.virtualCubePort` = `54321` (`extracted/settings/max.local.properties:22`), wenn die IP gleich `127.0.0.1` oder der eigenen Host-IP (`InetAddress.getLocalHost().getHostAddress()`) entspricht. Bei `UnknownHostException` fällt der Code auf `62910` zurück.
- Timeouts (`CubeConnection.java:48,60`; `max.local.properties:21-24`):
  - Erstes Lese-Timeout nach Socket-Aufbau: `max.connection.connectionTimeout` = **30000 ms**.
  - Nach erfolgreichem Hello: `max.connection.socketTimeout` = **70000 ms**.
  - `max.connection.commandQueueCapacity` = **100**.
- Nur **eine** Verbindung gleichzeitig: `CubeGateway.connections` ist `ArrayBlockingQueue<ICubeConnection>(1)` (`CubeGateway.java:52`); ein zweiter `connect(ip)` wirft `AlreadyConnectedToCubeException("already connected")` (`CubeGateway.java:110-119`).

### 2.2 UDP-Discovery (CubeFinder)

Quelle: `decompiled/localbackend/sources/de/eq3/max/al/local/finder/CubeFinder.java`.

| Parameter | Wert | Quelle |
|---|---|---|
| Port | `23272` | `CubeFinder.java:24` |
| Multicast-Gruppe | `224.0.0.1` | `CubeFinder.java:22` |
| Broadcast | `255.255.255.255` | `CubeFinder.java:23` |
| TTL | `5` | `CubeFinder.java:28,42` |
| Signatur (Request) | `65 51 51 4D 61 78 2A 00` = `eQ3Max*\0` | `CubeFinder.java:48` (Java-Literal `"eQ3Max*\u0000**********I"`) |
| Resends | 2 Wiederholungen im Abstand von 50 ms (3 Pakete gesamt) | `CubeFinder.java:26-27,157-167` |
| Antwort-Signatur | Regex `^eQ3Max(Ap|Bl)` in den ersten 8 Bytes | `CubeFinder.java:25,89-90` |
| Seriennummer in Antwort | Bytes 8..17 (10 Byte) | `CubeFinder.java:91` |
| Firmware in Antwort | Bytes 24..25, big-endian 16-Bit (`readInt(data,24,2)`) | `CubeFinder.java:92,100-106` |
| Bootloader-Flag | `signature == "eQ3MaxBl"` | `CubeFinder.java:21,93` |
| Empfangspuffer | 128 Byte, `setSoTimeout(800)`, `waitTime` aus Aufrufer | `CubeFinder.java:61,86` |

Der Client sendet das Discovery-Paket **sowohl** an die Broadcast- als auch an die Multicast-Adresse (`CubeFinder.java:47-51`) und wiederholt den Zyklus pro aktiver Netzwerkschnittstelle (`CubeFinder.java:112-131`). `CubeUdpProxy` ruft `CubeFinder.listCubes(1800)` auf (`CubeUdpProxy.java:59`).

**Unterschied zur TCP-Hello-Firmwarecodierung:** Discovery liefert die Firmware als **dezimale Ganzzahl aus 2 Bytes** (`readInt`), TCP-Hello als **hexadezimalen String** (`Integer.parseInt(...,16)`). Ein Wert `0x0111` im TCP-Hello entspricht z. B. `273` dezimal; der Discovery-Wert `261` in `APICalls.js:1243` ist ein Mockbeispiel.

---

## 3. Verbindungsaufbau und Hello (H:)

### 3.1 Ablauf

1. `CubeGateway.connect(ip)` erzeugt `new CubeConnection(ip, localConfiguration)` und legt sie in die Connection-Queue (`CubeGateway.java:110-119`).
2. `new Socket(ip, port)`; `setSoTimeout(30000)`; Reader/Writer anlegen; `ArrayBlockingQueue(100)` anlegen; `lastAccessTimestamp = now` (`CubeConnection.java:43-52`).
3. **Erste Zeile muss mit `H:` beginnen**, sonst `CubeCommunicationFailedException("No Incoming Hello")` (`CubeConnection.java:53-54,66`).
4. Body wird mit `StringTokenizer(command, ":")` gelesen (Token nach dem ersten `:`). `parseHeader` zerlegt kommasepariert: `serial=splitted[0]`, `radioAddress=parseInt(splitted[1],16)`, `firmwareVersion=parseInt(splitted[2],16)` (`CubeConnection.java:55-56,297-302`).
5. Firmwareprüfung: `leastCompatibleFirmwareVersion <= fw <= latestCubeFirmwareVersion`, konfiguriert als **273..275** (`CubeConnection.java:57`; `max.local.properties:17-18`). Bei Verstoß: `CubeCommunicationFailedException("incompatible firmware version")` (`CubeConnection.java:64`).
6. Bei Erfolg: `rawCubeState.setHeader(header)`, `connected=true`, `setSoTimeout(70000)`, Log `"Cube Connection ready."` (`CubeConnection.java:58-62`).

### 3.2 H:-Format (erweitert)

`H:<serial>,<radioAddressHex>,<firmwareHex>[,<f3>,<f4>,<dutyHex>,<freeSlotsHex>,<dateHex>,<timeHex>,<stateCubeTime>,<ntpCounterHex>]`

Die ersten drei Felder sind für den Verbindungsaufbau verbindlich. `MaxCubeStateFactory.parseHeader` (`decompiled/essentials/sources/de/eq3/max/al/core/cubestate/MaxCubeStateFactory.java:112-145`) interpretiert zusätzlich:

| Index | Bedeutung | Kodierung | Quelle |
|---|---|---|---|
| 0 | Seriennummer | String | `MaxCubeStateFactory.java:114` |
| 1 | RF-Adresse des Cube | 6 Hex-Ziffern (24 Bit) | `:115` |
| 2 | Firmware | Hex (z. B. `111` = 273) | `:116` |
| 3, 4 | **UNBEKANNT** (vom Client nicht ausgewertet) | – | – |
| 5 | Duty Cycle | Hex, `parseInt(...,16)` | `:120-122` |
| 6 | Freie Memory-Slots | Hex | `:123-125` |
| 7 | Datum (NTP-Zeit des Cube) | Hex `YYMMDD`, je 2 Hex-Ziffern → `hexToBytes` | `:126-127,147-156` |
| 8 | Uhrzeit | Hex `HHMM` | `:127,152-153` |
| 9 | `stateCubeTime` | Dezimal | `:135-138` |
| 10 | NTP-Zähler | Hex | `:139-142` |

Fehlen Index 7/8, wird `cubeDate = null` gesetzt (`MaxCubeStateFactory.java:131-134`). Das century wird aus dem aktuellen Kalender übernommen (`int century = cubeDate.get(1)/100`, `:154`), d. h. `YY` wird relativ zum aktuellen Jahrhundert interpretiert.

**Authentisches Beispiel (INFERENZ bezüglich konkreter Werte, Format quellbelegt); korrigiert 24.11.2026: Feld 7 ist hexadezimal `1a0b18` = 26.11.24, nicht dezimal `261124`:**

```text
H:JEQ0001234,1d2a3c,111,00,00,05,1f,1a0b18,0c1e,3,0002
```

### 3.3 Zustand / Lebenszyklus

- `CubeConnection.process()` führt **zuerst** `receiveDeviceAndMetaData()` aus und danach `talkToCube()` (`CubeConnection.java:107-110`). `talkToCube()` blockiert in `commandQueue.take()`, solange `connected == true` (`CubeConnection.java:185-197`). Deshalb wird der State-Import praktisch **einmal pro Verbindung** ausgeführt, bevor der erste Befehl verarbeitet wird.
- `receiveDeviceAndMetaData()` liest Zeilen, bis eine `L:`-Zeile eintrifft; `C:`- und `M:`-Zeilen werden vorher in den `RawCubeState` einsortiert (`CubeConnection.java:157-183`). Andere Präfixe werden in dieser Phase ignoriert; `null` oder unbekannte Zeilen führen zu `NullPointerException` (UNBEKANNT, wie der Cube das vermeidet).
- Worker-Thread: `CubeGateway` startet einen Daemon-Thread `CubeGateway` (`CubeGateway.java:54,56-59`); dekompilierte `run()`-Methode ist JADX-inkonsistent (`CubeGateway.java:63-106`), aus dem Byte-Code-Kommentar geht hervor: `getNextConnection()` → `process()` → bei Exception `log.error("Error with connection")` → `close()`, dann Schleife. **INFERENZ:** nach `close()` wird die Queue geleert und die nächste Verbindung erwartet.
- Befehlsausführung: `execute()` legt Befehl in Queue, wartet auf Semaphore, wirft Fehler weiter (`CubeConnection.java:139-155`; `CubeConnectionCommand.java:30-49`). `IOException` und `RuntimeException` werden unverändert weitergereicht, alle anderen `Throwable` als `CubeCommunicationFailedException("Unknown Error", cause)` verpackt.
- `SocketException` / `SocketTimeoutException` während eines Befehls setzen den Befehlsfehler und rufen `close()` (`CubeConnection.java:236-241`); `CubeGateway.execute()` ruft zusätzlich `dropConnection()` (`CubeGateway.java:304-314`).
- `close()` setzt `connected=false`, markiert den aktuellen Befehl mit `CubeCommunicationFailedException()` und benachrichtigt ihn; danach werden alle wartenden Befehle mit Exception `"command ignored"` abgewiesen (`CubeConnection.java:123-135,249-261`).
- `DisconnectCubeCommand` ruft `context.shutdown()` (nur `connected=false`, **kein** `socket.close()` im Befehl; `CubeConnection.java:77-79`). Der Socket wird erst beim nächsten `CubeGateway.run()`-Durchlauf geschlossen (INFERENZ aus `CubeGateway.java:79-82`).
- Inclusion-Mode-Sperre: Solange `isInclusionMode == true`, sind nur `FetchNewDevicesCubeCommand`, `CancelInclusionModeCubeCommand` und `DisconnectCubeCommand` erlaubt; alle anderen Befehle scheitern mit `CubeIsInInclusionModeException` (`CubeConnection.java:264-275`). `SendMetadataCubeCommand` prüft zusätzlich selbst (`SendMetadataCubeCommand.java:28-30`).
- Watchdog / Idle (vom Auftraggeber verifiziert, implementierungsseitig belegt):
  - `ConnectionWatchDog.checkConnection()` meldet nach `max.idleTimeBeforeLocalAppDisconnect` = **30000 ms** Inaktivität an `IConnectionTimeoutHandler.onConnectionTimeout()` (`ConnectionWatchDog.java:16-17,34-40`; `max.local.properties:4`).
  - `CubeGateway.idleCall()` sendet nach `max.cubeIdleTimeToSend` = **900000 ms** ohne Zugriff `encrypt("something")` → `e:something\r\n`, aber **nur**, wenn nicht Inclusion-Mode aktiv ist (`CubeGateway.java:291-301`; `max.local.properties:5`).
  - `connectionWatchDog.checkConnection` und `cubeGateway.idleCall` werden alle **5000 ms** aufgerufen (vom Auftraggeber verifiziert; Scheduler-Konfiguration liegt nicht in den gelesenen Dateien → **INFERENZ** bezüglich konkreter Bean/Annotation).
  - **Hinweis zur Reimplementierung:** 900000 ms > 30000 ms. Ob der Watchdog durch die reguläre API-Aktivität zurückgesetzt wird, ist aus den vorliegenden Dateien **UNBEKANNT** (`ConnectionWatchDog.reset()` existiert, seine Aufrufer wurden nicht analysiert: `ConnectionWatchDog.java:30-32`).

---

## 4. Allgemeine Nachrichtenformate und Kodierungen

| Funktion | Regel | Quelle |
|---|---|---|
| Opcode | 1 Buchstabe + `:` am Zeilenanfang | `IOperationCodes.java:15-59` |
| Body (einfach) | `StringTokenizer(cmd, ":")` → Token nach erstem `:` | `CubeConnection.java:199-206`, `AESEncryptCubeCommand.java:31-38` |
| Body (Argumente) | `cmd.substring(2).split(",")` | `CubeConnection.java:208-213` |
| Hex | lowercase, 2 Ziffern pro Byte: `toHex8` → `toHex16` → `toHex24` → `toHex32` | `Conversion.java:11-44` |
| Base64 | `Base64.encodeBase64(bytes,false)` ohne Zeilenumbrüche | `Conversion.java:73-83` |
| Zeilenende | `\r\n` für **alle** ausgehenden Befehle | z. B. `SetUrlCommand.java:17` |
| ACK | `A:\r\n`-Zeile; gelesen nur bei `firmwareVersion > 22` (dezimal!) | `CubeConnectionCommand.java:52-59` |

`readAcknowledge()` erwartet lediglich `startsWith("A:")`; der restliche Inhalt wird nicht geprüft. Da die erlaubte Firmware 273..275 und damit immer > 22 ist, ist das ACK bei allen Befehlen, die `readAcknowledge()` aufrufen, **obligatorisch**. Ausnahmen: `AESEncryptCubeCommand`, `AESDecryptCubeCommand`, `FetchNewDevicesCubeCommand`, `ResetGatewayCubeCommand`, `GetConfigurationCubeCommand`, `ListDevicesCubeCommand`, `QueryNTPServerCommand`, `SetNTPServerCommand` (eigene Antwortlogik) und `DeviceSendCommand` (`S:` statt `A:`).

---

## 5. Opcode-Universum und Auflösung mehrfach belegter Präfixe

Quelle: `decompiled/essentials/sources/de/eq3/max/al/core/IOperationCodes.java:5-59`.

| Opcode | Richtung laut Konstante | Bedeutung | Implementiert in localbackend? |
|---|---|---|---|
| `H:` | Cube→App | Incoming Hello | ja (`CubeConnection.java:54`) |
| `F:` | Cube→App | NTP-Server-Antwort | ja (`QueryNTPServerCommand.java:30`) |
| `L:` | Cube→App | Device-Liste (Base64) | ja (`CubeConnection.java:161`) |
| `C:` | Cube→App | Device-/Cube-Konfiguration | ja (`CubeConnection.java:165`) |
| `M:` | Cube→App | Metadata-Block | ja (`CubeConnection.java:172`) |
| `N:` | Cube→App | Neues Device (Inclusion) | ja (`FetchNewDevicesCubeCommand.java:54`) |
| `A:` | Cube→App | ACK | ja (`CubeConnectionCommand.java:55`) |
| `E:` | Cube→App | Verschlüsselungsergebnis | ja (`AESEncryptCubeCommand.java:26`) |
| `D:` | Cube→App | Entschlüsselungsergebnis | ja (`AESDecryptCubeCommand.java:26`) |
| `S:` | Cube→App | Airtime-Status nach Radiogramm | ja (`DeviceSendCommand.java:34`) |
| `b:` / `B:` | Proxy→App / App→Proxy | Remote-Zugangsdaten setzen | **ja, aber nicht auf TCP**: `SetRemoteCredentialsRequest.java:10`, `MaxRemoteProxy.java:74` |
| `g:` / `G:` | Proxy→App / App→Proxy | Zugangsdaten lesen | **ja, aber nicht auf TCP**: `GetRemoteCredentialsRequest.java:6`, `MaxRemoteProxy.java:89` |
| `j:` / `J:` | Proxy→App / App→Proxy | Fernzugriff ein/aus | **ja, aber nicht auf TCP**: `SetRemoteAccessRequest.java:6`, `MaxRemoteProxy.java:310` |
| `p:` / `P:` und `o:` / `O:` | Proxy→App / App→Proxy | Remote-User-Data setzen/lesen | **ja, aber nicht auf TCP**: `SetRemoteUserDataRequest.java:14`, `GetRemoteUserDataRequest.java:12`, `MaxRemoteProxy.java:115` |
| `v:` / `V:` | Proxy→App / App→Proxy | Produktaktivierung prüfen | **ja, aber nicht auf TCP**: `CheckProductActivationRequest.java:6`, `MaxRemoteProxy.java:134` |
| `w:` / `W:` | Proxy→App / App→Proxy | Produkt aktivieren | **ja, aber nicht auf TCP**: `ActivateProductRequest.java:12`, `MaxRemoteProxy.java:167` |
| `u:` | App→Cube | URL setzen | ja (`SetUrlCommand.java:17`) |
| `i:` | App→Cube | Intervall setzen | ja (`IntervalCubeCommand.java:23`) |
| `s:` | App→Cube | Geräte-Radiogramm senden | ja (`ClientCommand.java:53`, `DeviceSendCommand.java:30`) |
| `m:` | App→Cube | Metadata-Block senden | ja (`SendMetadataCubeCommand.java:42`) |
| `n:` | App→Cube | Inclusion-Mode starten | ja (`FetchNewDevicesCubeCommand.java:45`) |
| `x:` | App→Cube | Inclusion-Mode abbrechen | ja (`CancelInclusionModeCubeCommand.java:35`, `CubeGateway.java:140`) |
| `q:` | App→Cube | Quit / Disconnect | ja (`DisconnectCubeCommand.java:13`) |
| `e:` | App→Cube | Verschlüsseln | ja (`AESEncryptCubeCommand.java:23`) |
| `d:` | App→Cube | Entschlüsseln | ja (`AESDecryptCubeCommand.java:23`) |
| `a:` | App→Cube | Reset des Gateway | ja (`ResetGatewayCubeCommand.java:11`) |
| `r:` | App→Cube | Reset-Radiogramm | ja (`ResetDeviceRadiogram.java:21`) |
| `t:` | App→Cube | Geräte löschen | ja (`DeleteDevicesCubeCommand.java:34`) |
| `c:` | App→Cube | Konfiguration abfragen | ja (`GetConfigurationCubeCommand.java:26`) |
| `l:` | App→Cube | Device-Liste abfragen | ja (`ListDevicesCubeCommand.java:16`) |
| `f:` | App→Cube | NTP-Server setzen/abfragen | ja (`SetNTPServerCommand.java:22`, `QueryNTPServerCommand.java:20`) |
| `z:` | App→Cube | Wakeup | ja (`WakeupCommand.java:23`) |

### 5.1 Aufgelöste Präfix-Kollisionen

| Präfix | Kontext 1 | Kontext 2 | Auflösungsregel |
|---|---|---|---|
| `g:` | `INCOMING_GET_CREDENTIALS` (`IOperationCodes.java:25`) | `OUTGOING_MORE_DATA` (`:38`) | Erstes ist die echte `G:`-Antwort des Remote-Proxys (`MaxRemoteProxy.java:89`). Zweites ist eine **tote Konstante** ohne Produzenten im Java-Baum (`UNBEKANNT`), kein Cube-TCP-Befehl. |
| `w:` | `INCOMING_ACTIVATE_PRODUCT` (`:30`) | `OUTGOING_SET_PUSHBUTTON_CONFIG` (`:53`) | Zwei getrennte Protokolle: `w:` vom **Proxy** = `W:`-Antwort (`MaxRemoteProxy.java:167`); `w:` an den **Cube** = Push-Button-Konfiguration (`PushButtonConfigurationCubeCommand.java:27`). Nicht verwechseln. |
| `s:` | `OUTGOING_SEND` (`:34`) | `OUTGOING_SEND_DEVICE_CMD` (`:49`) | Beide Konstanten identisch; `s:` ist immer App→Cube und transportiert ein Radiogramm. `S:` (groß) ist die zugehörige Antwort. |
| `u:` | `OUTGOING_URL` (`:32`) | `OUTGOING_SET_URL` (`:55`) | Beide identisch; `u:` ist immer App→Cube (`SetUrlCommand.java:17`). |
| `v:` | `INCOMING_CHECK_PRODUCT_ACTIVATION` (`:29`) | `OUTGOING_TIME_CONFIG` (`:57`) | Zwei getrennte Protokolle: `v:` vom **Proxy** = `V:`-Antwort (`MaxRemoteProxy.java:134`); `v:` an den **Cube** = Zeit-/Zeitzonen-Konfiguration (`AdjustTimezoneConfigurationCommand.java:24`). Nicht verwechseln. |
| `f:` vs `F:` | `f:` = Request (App→Cube), `F:` = Response (Cube→App) | – | Groß-/Kleinschreibung unterscheidet Richtung. |
| `e:/E:`, `d:/D:` | klein = Request, groß = Response | – | Wie bei `f:/F:`. |
| `l:/L:`, `c:/C:`, `m:/M:` | klein = Request, groß = Response | – | Wie oben; `M:` ist zusätzlich unaufgeforderter State-Stream. |

**Aufgelöst 24.11.2026:** Die Familie `B/G/J/P/O/V/W` samt Kleinschreibung gehört zum **Remote-Proxy** über HTTP, nicht zum Cube-TCP. Vollständig CODE-BELEGT durch `decompiled/*/sources/de/eq3/max/al/local/remoteproxy/` (sieben Request-Klassen, `MaxRemoteProxy.java:67-175,303-315`, `RemoteProxyRequest.java:27-56`, `RemoteProxyResponse.java:29-58`). Der Request wird als `H:` + Kommando Base64-kodiert, über die Cube-Krypto (`e:`) verschlüsselt und per HTTP-Request an die Device-App-URL gesendet („POST" aus `setDoOutput(true)` ohne `setRequestMethod` gefolgert, nicht wörtlich im Code); die Antwort läuft über `d:` zurück. `b:OK`, `g:<user>,<enabled>`, `o:<value>`, `v:<…>`, `w:<…>` sind die aus dem Bytecode belegten Antwortformate, keine Live-Messung. Nur `OUTGOING_MORE_DATA` (`g:`, `:38`) bleibt eine tote Konstante (`UNBEKANNT`). Die Cloud ist seit 01.07.2023 abgeschaltet; die Familie wird bewusst **nicht** in die lokale Integration übernommen. Nachweis in `SecondBrain/wiki/fragen/2026-11-24-maxcube-remote-proxy-opcodes.md` (dort auch die Korrekturen der Sonnet-Gegenprüfung).

---

## 6. Vollständige Befehlstabelle (App → Cube)

Alle Beispiele zeigen den exakten Wire-String ohne darstellbares `\r\n` (am Ende jeweils CRLF). „Antwort“ ist die vom Client gelesene Zeile.

| Nr. | Befehl | Syntax | Bedeutung | Beispiel | Antwort | Quellstelle |
|---|---|---|---|---|---|---|
| 1 | Identify/Hello | `H:`-Zeile vom Cube | Seriennummer, RF-Adresse, Firmware, ggf. Duty/Datum | `H:JEQ0001234,1d2a3c,111` | – (Initial-Frame) | `CubeConnection.java:53-62` |
| 2 | Device-Liste abfragen | `l:\r\n` | Liefert komplette Geräteliste | `l:` | `L:<base64>` | `ListDevicesCubeCommand.java:16-24` |
| 3 | Konfiguration abfragen | `c:<adrHex24>\r\n` | Konfiguration eines Geräts/Cube | `c:1d2a3c` | `C:1d2a3c,<base64>` | `GetConfigurationCubeCommand.java:26-35` |
| 4 | Metadata senden | `m:<index2>,<utf8Bytes>\r\n` | Blockweise Metadata (1900 Byte/Block) | `m:00,{"rooms":[...]}` | `A:` (pro Block) | `SendMetadataCubeCommand.java:42-51` |
| 5 | Inclusion starten | `n:<timeoutHex16>\r\n` | Anlernen für 60 s | `n:003c` | `N:<base64>` | `FetchNewDevicesCubeCommand.java:44-49` |
| 6 | Inclusion abbrechen | `x:\r\n` | Beendet Anlernmodus | `x:` | `N:` (letzte Zeile mit leerem Body) oder `A:`-unabhängig | `CancelInclusionModeCubeCommand.java:35-48` |
| 7 | Quit/Disconnect | `q:\r\n` | Trennt Verbindung; Client erwartet **keine** Zeile | `q:` | *(keine; `readLine()==null`)* | `DisconnectCubeCommand.java:13-19` |
| 8 | URL setzen | `u:<url>\r\n` | App-URL des Cube | `u:http://www.eq3.de` | `A:` | `SetUrlCommand.java:9-20` |
| 9 | Intervall setzen | `i:<intervalHex32>,<00000000>\r\n` | Update-Intervall; 2. Argument hart `0` | `i:000003e8,00000000` | `A:` | `IntervalCubeCommand.java:23-26` |
| 10 | Radiogramm senden | `s:<base64(payload)>\r\n` | Gerätebefehl ( CONTROL/CONFIG/ASSIGN/REMOVE ) | `s:/wDcAA...` | `S:<dutyHex>,<discarded>,<freeHex>` | `DeviceSendCommand.java:30-38`, `ClientCommand.java:53` |
| 11 | Reset-Radiogramm | `r:01,<base64(adrHigh,adrMid,adrLow)>\r\n` | Setzt ein Gerät zurück | `r:01,HSo8` (Adr `1d2a3c`) | `S:...` | `ResetDeviceRadiogram.java:20-38` |
| 12 | Verschlüsseln | `e:<plaintext>\r\n` | Cube führt AES aus | `e:something` | `E:<base64-cipher>` | `AESEncryptCubeCommand.java:23-28` |
| 13 | Entschlüsseln | `d:<base64-cipher>\r\n` | Cube führt AES aus | `d:4b9m...` | `D:<base64-plain>` | `AESDecryptCubeCommand.java:23-28` |
| 14–20 | Remote-Proxy (**kein** Cube-TCP-Befehl) | `B:/G:/J:/P:/O:/V:/W:` | HTTP-Request an die Device-App-URL, nicht an Port 62910 („POST" nur gefolgert) | – | `b:/g:/j:/p:/o:/v:/w:` | Abschnitt 6.1; `MaxRemoteProxy.java:67-175` |
| 21 | Reset Gateway | `a:\r\n` | Hardware-Reset; Client liest **kein** ACK | `a:` | – | `ResetGatewayCubeCommand.java:11-12` |
| 22 | Geräte löschen | `t:<countHex8>,<force>,<base64(3N Adressbytes)>\r\n` | Löscht Geräte, optional Factory-Reset | `t:01,1,HSo8` | `A:` | `DeleteDevicesCubeCommand.java:33-39,52-66` |
| 23 | Push-Button-Konfig | `w:<base64(66 Byte)>\r\n` | Zwei je 33 Byte-Templates | `w:...` | `A:` | `PushButtonConfigurationCubeCommand.java:18,25-29` |
| 24 | Zeit-/Zeitzonen-Konfig | `v:<base64(24 Byte)>[,<ntpHex>]\r\n` | Winter + Sommer + optional NTP-Offset | `v:...`,`v:...,1c` | `A:` | `AdjustTimezoneConfigurationCommand.java:24-31` |
| 25 | NTP-Server setzen | `f:[server1][,server2]\r\n` | Bis zu zwei Server | `f:pool.ntp.org,time.eq-3.de` | `F:...` | `SetNTPServerCommand.java:22-31,36-41` |
| 26 | NTP-Server abfragen | `f:\r\n` | Liest aktuelle Server | `f:` | `F:<roh>` | `QueryNTPServerCommand.java:20-34` |
| 27 | Wakeup | `z:<minHex8>,<A\|D\|G>[,<ziel>]\r\n` | Gerät/Gruppe/Alle aufwecken | `z:1e,D,1d2a3c` | `A:` | `WakeupCommand.java:23-32` |
| 28 | State abfragen (lokal) | *(kein Wire-Befehl)* | `FetchGatewayStateCubeCommand` kopiert nur `RawCubeState` in `MaxCubeState` | – | – | `FetchGatewayStateCubeCommand.java:19-20` |

### 6.1 Remote-Proxy-Protokoll (CODE-BELEGT, nicht implementiert)

Die sieben Großbuchstaben-Opcodes und `g:` gehören zu einem **zweiten Protokoll**: HTTP-Request an die Device-App-URL der (abgeschalteten) MAX!-Cloud („POST" aus `setDoOutput(true)` gefolgert). Die Großbuchstabenzeile wird nie direkt an Port 62910 gesendet; ihre Base64-Nutzload läuft aber als `e:`-Argument über TCP.

**Transport:** `H:<serial>,<rfHex>,<fwHex>\r\n` + Kommandozeile → Base64 → Cube-Krypto `e:` → Base64-dekodiert als HTTP-Body; Header `Opt` (Hersteller-Namensraum, Wert hier nicht wiederholt) und `MAX-Serial`. Antwort: Base64 → Cube-Krypto `d:` → Base64 → Zeilenparser `line[0]` / `line.substring(2).split(",")` (`RemoteProxyRequest.java:27-56`, `RemoteProxyResponse.java:29-58`, `MaxRemoteProxy.java:182-190`). **Präzisierung:** `e:`/`d:` sind echte TCP-Befehle an den Cube; die Base64-Nutzlast läuft als `e:`-Argument über Port 62910, die Großbuchstabenzeile selbst tut es nicht. „POST" ist aus `setDoOutput(true)` ohne `setRequestMethod` gefolgert.

| Opcode | Nutzlast | Antwort | Quelle |
|---|---|---|---|
| `B:` | `<user>,<MD5(passwort+serial)>,<'0'\|'1'>,<passwort>` | `b:OK` sonst `RemoteUserAlreadyExists` | `SetRemoteCredentialsRequest.java:10,13-21`, `MaxRemoteProxy.java:74-77` |
| `G:` | – (`G:\r\n`) | `g:<user>,<true\|false>` | `GetRemoteCredentialsRequest.java:6`, `MaxRemoteProxy.java:89-94` |
| `J:` | `<1\|0>` | `j:OK` (Prüfung leerer if-Block) | `SetRemoteAccessRequest.java:6`, `MaxRemoteProxy.java:310-313` |
| `P:` | `<key>,<value>`, Key ≤ 64 `[_a-zA-Z0-9]*`, Value ≤ 1024 ohne CR/LF/Komma | keine Auswertung | `SetRemoteUserDataRequest.java:7-14,18-28` |
| `O:` | `<key>` (validiert wie oben) | `o:<value>` oder `""` | `GetRemoteUserDataRequest.java:7-17`, `MaxRemoteProxy.java:115-119` |
| `V:` | – (`V:\r\n`) | `v:` mit 7 Feldern + optional Base64-InfoTexten | `CheckProductActivationRequest.java:6`, `MaxRemoteProxy.java:134-150` |
| `W:` | `<productKey>`, `[A-Z0-9-]*` ≤ 40 | `w:<activated>,<expiry>,<days>,<criteriaKeyViolation>` | `ActivateProductRequest.java:7-17`, `MaxRemoteProxy.java:167-173` |
| `g:` | operative `G:`-Antwort (siehe oben); `OUTGOING_MORE_DATA` (`:38`) ist **tot** | – | `IOperationCodes.java:25,38` |

**Entscheidung für die Integration:** bewusst **nicht** übernehmen – Cloud seit 01.07.2023 abgeschaltet, keine Zugangsdaten/Produktkeys in der App, keine Remote-Schnittstelle. Ein unerwartetes `b:/g:/j:/p:/o:/v:/w:` **als Antwortzeile** auf Port 62910 ist ein unbekanntes Opcode und führt zu `UnknownCubeOpcodeException` (`CubeCommands.cs:58-60`), nicht zu einem Erfolg. `v:` und `w:` bleiben gültige Requests (`CubeCommands.cs:309,338`). Vollständiger Nachweis: `SecondBrain/wiki/fragen/2026-11-24-maxcube-remote-proxy-opcodes.md`.

**Reihenfolgengarantie:** Es gibt genau einen Worker (`CubeGateway`-Thread) und eine Queue; Befehle werden in FIFO-Reihenfolge abgearbeitet (`CubeConnection.java:215-222`, `CubeGateway.java:52-59`). `execute()` ist synchron (Semaphore, `CubeConnectionCommand.java:30-49`).

---

## 7. Detailbeschreibung ausgewählter Befehle

### 7.1 `l:` / `L:` – Device-Liste

- Request `l:\r\n`, Antwort muss mit `L:` beginnen, sonst `CubeCommunicationFailedException("device list expected")` (`ListDevicesCubeCommand.java:19-24`).
- Der Body wird als Base64 dekodiert (`DeviceStateSerializer.java:40`). Struktur pro Gerät:
  - 1 Byte `size` (Länge des folgenden Geräteblocks)
  - 3 Byte RF-Adresse, big-endian (`Conversion.parseInteger(...,3)`, `:51`)
  - 1 Byte `erroneousFrameType` (`:52`)
  - 1 Byte `stateInformation` (`:53`): Bit 4 = valid, Bit 1 = initialized, Bit 3 = RF-Fehler (`:31-33,61-63`)
  - `size - 5` Byte gerätespezifischer State (`:54,181-188`)
- `createRadioState()` bildet aus RF-Fehler + FrameType-Code einen `RadioState` über `FrameType.getRadioState()` (`DeviceStateSerializer.java:204-213`).

### 7.2 `c:` / `C:` – Konfiguration

- Request `c:<6 Hex-Ziffern>\r\n`; Antwort `C:<adrHex>,<base64>`; der Client nimmt `line.substring(2).split(",")[1]` (`GetConfigurationCubeCommand.java:30-33`).
- Dekodierung (`DeviceConfigurationSerializer.java:46-71`): 1 Byte (ungedeutet) → 3 Byte Adresse → 1 Byte Gerätetyp → 1 Byte Gruppenadresse → 1 Byte Firmware → 1 Byte Testergebnis → 10 Byte Seriennummer (ISO-8859-1, `:97-101`) → Rest = gerätespezifische Konfiguration.
- Gerätetypen (`DeviceType.java:5-10`): `1` HeatingThermostat, `2` HeatingThermostatPlus (wird intern zu HeatingThermostat normalisiert, `DeviceConfigurationSerializer.java:54-56`), `3` WallMountedThermostat, `4` ShutterContact, `5` PushButton, `256` Invalid.
- Für den **Cube selbst** wird dieselbe `C:`-Zeile mit der RF-Adresse aus dem Header verwendet; `CubeConfigurationSerializer.java:69-73` dekodiert sie und liest feste Offsets:
  - Offset 19 / 52: Push-Button-Konfiguration (`KEY_CONFIG_LENGTH=11`, `:23-29`),
  - Offset 85..128: App-URL, endet bei Byte ≤ 32 (`:34-35,148-159`),
  - Offset 214 / 226: Winter-/Sommerzeit-Konfiguration (`:26-27,41-43,50-62`).

### 7.3 `m:` / `M:` – Metadata

**App → Cube (`m:`):**

- `metadataBlockSize` = **1900 Byte** (`max.local.properties:8`).
- `index` ist zweistellig **dezimal** (`"0"+packages`, `SendMetadataCubeCommand.java:37-41`), Payload sind die rohen UTF-8-Bytes (kein Base64!), danach `\r\n`; **nach jedem Block** wird ein `A:` gelesen (`:49-51`).
- Blockzahl = `ceil(byteLength / 1900)` (`:32-35`).

**Cube → App (`M:`):**

- `M:<indexHex>,<countHex>,<payload>`; Index und Count werden mit `parseInt(...,16)` gelesen (`CubeConnection.java:172-180`).
- `Metadata` hält maximal **16 Blöcke** (`MAX_BLOCK_COUNT=16`, `Metadata.java:10-18`); `setBlock(index,count,data)` setzt Gesamt-Count und ersetzt den Block (`:43-46`); `getBytes()` konkateniert die ersten `count` Blöcke (`:28-37`).
- Die konkatenierten Bytes werden als Base64 interpretiert und in `rooms`, `devicesNotAssociated`, `daylightSaving` deserialisiert (`MetaDataSerializer.java:40-43,47-53`).

**Blockierung:** Während Inclusion-Mode ist `m:` verboten – doppelte Prüfung in `CubeConnection.throwExceptionIfNotAllowedToExecute()` (`CubeConnection.java:264-275`) und in `SendMetadataCubeCommand.execute()` (`SendMetadataCubeCommand.java:28-30`).

### 7.4 `n:` / `x:` / `N:` – Inclusion-Mode

- Start: `n:<Conversion.toHex16(inclusionModeTimeoutSeconds)>\r\n` = `n:003c` bei 60 s (`FetchNewDevicesCubeCommand.java:45`; `max.local.properties:9`); danach `setInclusionMode(true)` (`:48`).
- Warten: genau **eine** Zeile wird gelesen; muss `N:` sein, sonst `InvalidArgumentsException("N: expected, but found ...")` (`FetchNewDevicesCubeCommand.java:52-65`).
- `N:<base64>` dekodiert zu: 1 Byte DeviceType, 3 Byte RF-Adresse, 10 Byte Seriennummer (`NewDeviceSerializer.java:29-49`). `HeatingThermostatPlus` → `HeatingThermostat` (`:37-39`).
- Leerer Body (`N:` ohne Token) beendet den Modus (`setInclusionMode(false)`), `FetchNewDevicesCubeCommand.java:59-62`.
- Abbruch-Sonderfall: Ist gerade `FetchNewDevicesCubeCommand` der aktive Befehl, schreibt `CubeGateway.cancelInclusionMode()` das `x:` **direkt** in den Writer, ohne über die Queue zu gehen (`CubeGateway.java:137-145`). Andernfalls wird `CancelInclusionModeCubeCommand` in die Queue gestellt; dieser schreibt `x:` und liest `N:`-Zeilen, bis der Modus false wird (`CancelInclusionModeCubeCommand.java:31-53`).
- Nach `Disconnect` wird die New-Device-Liste zurückgesetzt (`CubeGateway.java:187-192`).

### 7.5 `s:` / `S:` – Radiogramme und Duty-Cycle

- Request: `deviceRadiogram.getOpCode() + Conversion.encodeBase64(payload) + "\r\n"` (`DeviceSendCommand.java:30`).
- Antwort: `S:<dutyCycleHex>,<commandDiscarded>,<freeMemorySlotHex>`; **drei** Argumente, sonst `InvalidArgumentsException("invalid arguments")` (`:42-49`).
  - `dutyCycle = parseInt(args[0],16)`
  - `commandDiscarded = args[1].charAt(0) == '1'`
  - `freeMemorySlot = parseInt(args[2],16)`
  - Bei `commandDiscarded == true`: `CubeCommunicationFailedException("Command discarded")` (`:36-38`).
- Danach wird `MaxCubeAirtimeStatus(dutyCycle, discarded, freeSlots, null)` erzeugt und erst in `CubeGateway.commitDeviceRadiogram()` mit `cubeState.getCubeDate()` versehen (`DeviceSendCommand.java:49,66-69`; `CubeGateway.java:165-170`).
- Der Duty-Cycle wird zusätzlich aus dem `H:`-Header (Index 5) gelesen (`MaxCubeStateFactory.java:120-122,143-144`).

### 7.6 `w:` – Push-Button-Konfiguration

- Template: 33 Byte, `DATA_OFFSET = 11` (`PushButtonConfigurationCubeCommand.java:17-18`). Zwei Templates (upper, lower key) → 66 Byte Base64.
- Byte 11 = `(mode << 6) | temperature` (`:42-49`).
- Mode-Mapping (`PushButtonKeyMode`): `Auto`/`AutoWithTemperature` → 0; `Manual` → 1; `Eco` → 1; `Comfort` → 1 (`:58-76,80-88`).
- Temperatur-Kodierung (`:90-102`): `AutoWithTemperature`/`Manual` → `(int)(temp*2) & 63`; `Eco` → 1; `Comfort` → 2; `Auto` → 0. Decodierung spiegelverkehrt in `CubeConfigurationSerializer.java:75-101`.

### 7.7 `v:` – Zeit-/Zeitzonen-Konfiguration

- Payload pro Zone 12 Byte (`AdjustTimezoneConfigurationCommand.java:41-53`): Name 5 Byte (mit `0` aufgefüllt), `month`, `dayOfWeek.getValue() % 7`, `hourOfDay`, `offsetToUTC` als 4 Byte big-endian (`>>24,>>16,>>8,>>0`).
- Reihenfolge: **zuerst Winter, dann Sommer** (`:34-39`).
- Optional `,` + `Integer.toHexString(ntpUTCSeconds)`, nur wenn `ntpUTCSeconds > 0` (`:26-28`).
- Antwort `A:` via `readAcknowledge()` (`:31`).

### 7.8 `f:` / `F:` – NTP

- Setzen: `f:<server1>[,<server2>]\r\n`; Server werden nur geschrieben, wenn `length() > 1` (`SetNTPServerCommand.java:23-28`). Antwort muss mit `F:` beginnen, sonst `CubeCommunicationFailedException("ACK of NTP server settings expected")` (`:36-41`).
- Abfragen: `f:\r\n`; Antwort wird **inklusive** `F:`-Präfix in `ntpServers` gespeichert (`QueryNTPServerCommand.java:28-34`). Die Parsing-Logik für den Inhalt liegt nicht in den gelesenen Dateien → **UNBEKANNT**.
- NTP-Zeit im Header: siehe Abschnitt 3.2 (Index 7/8/9/10).

### 7.9 `z:` – Wakeup

- `z:<hex8(duration)>,<context>[,<destination>]\r\n` (`WakeupCommand.java:23-29`).
- Kontext-Codes (`:48-64`): `A` = alle (`destination=0`, kein drittes Feld), `D` = Device (`hex24`), `G` = Gruppe/Raum (`hex8`).
- Default-Dauer `DEFAULT_WAKE_UP_TIME = 30` Minuten → `1e` (`:10,35-45`). `wakeupAllDevices()` ist implementiert, aber nicht über `CubeGateway` verfügbar gemacht (`CubeGateway.java:155-162`).
- Antwort `A:` (`:32` → `readAcknowledge`).

### 7.10 `t:` – Geräte löschen

- `t:<hex8(count)>,<force 0|1>,<base64(count*3 Adressbytes)>\r\n` (`DeleteDevicesCubeCommand.java:33-38,42-67`).
- Adressbytes big-endian: `(a>>16)&255, (a>>8)&255, a&255` (`:56-61`).
- Antwort `A:` (`:39`).

---

## 8. Radiogramm-Binary-Format (`s:` / `r:`)

### 8.1 Header-Spezifikation

Quelle: `ClientCommand.java:39-54`, `AbstractRoomClientCommand.java:45-50`, `AbstractRoomDeviceClientCommand.java:77-79`.

```text
addByte(2, 0, frameTypeCode)          // Byte 2, FrameType.getCode()
addDeviceAddress(6, 0, receiverAddr)  // Byte 6..8, 24 Bit
```

Gruppenbefehle ergänzen:

```text
addBoolean(1, 2, true)   // Broadcast-Flag
addBoolean(1, 1, false)
addByte(9, 0, roomId)
```

`RadiogramSpecification.createPayload()` (`RadiogramSpecification.java:114-126`) schreibt Bits in ein LSB-first-Bitfeld: Bit `i` → `bytes[i/8] |= (1 << (i%8))`. Ganzzahlen werden über `ByteUtil.fromInt(value, bitLength)` gerendert (`ByteUtil.java:8-21`):

- `reorderInt(value, byteLength, LITTLE_ENDIAN)` erzeugt **big-endian Byte-Reihenfolge im Byte-Array** (`ByteUtil.java:23-33`: `b[i] = (value >>> ((len-1-i)*8)) & 255`; Reverse nur bei `BIG_ENDIAN`).
- Innerhalb jedes Bytes werden die Bits LSB→MSB in das BitSet übernommen (`ByteUtil.java:11-19`).
- 24-Bit-Adressen: `DEVICEADDRESS_RENDERER` → `fromInt(value,24)` (`RadiogramSpecification.java:66-71`).

### 8.2 FrameType-Tabelle

Quelle: `decompiled/essentials/sources/de/eq3/max/al/core/radiogram/FrameType.java:9-34`.

| Name | Code (hex) | Kategorie |
|---|---|---|
| SystemInformation | `0x00` | None |
| Inclusion | `0x01` | None |
| Answer | `0x02` | None |
| TimeInformation | `0x03` | None |
| ConfigWeekTemperatureProfile | `0x10` | Configuration |
| ConfigTemperatures | `0x11` | Configuration |
| ConfigValveFunctions | `0x12` | Configuration |
| AddLinkPartner | `0x20` | Assignment |
| RemoveLinkPartner | `0x21` | Assignment |
| SetGroupAddress | `0x22` | Assignment |
| RemoveGroupAddress | `0x23` | Assignment |
| ShutterConactState | `0x30` | None |
| ControlMode | `0x40` | Control |
| SetPointTemperature | `0x41` | Control |
| SetPointAndCurrentTemperature | `0x42` | None |
| ComfortTemperature | `0x43` | Control |
| EcoTemperature | `0x44` | Control |
| CurrentTemperatureAndHumidity | `0x45` | None |
| PushButtonState | `0x50` | None |
| HeatungThermostatState | `0x60` | None |
| LockManualControls | `0x80` | Control |
| DaylightSavingTimeMode | `0x81` | None |
| SwitchDisplaySetPointActual | `0x82` | Configuration |
| Reset | `0xF0` | Remove |
| WakeUp | `0xF1` | None |
| Test | `0xFF` | None |

`FrameTypeCategory.toRadioState()` (`FrameTypeCategory.java:44-59`): None→`Error`, Control/Configuration→`ConfigFailed`, Assignment→`AssignFailed`, Remove→`DeleteFailed`.

### 8.3 Renderer und Feldkodierung

|Renderer|Regel|Quelle|
|---|---|---|
|Boolean|1 Bit, `BitSet.set(0,value)`|`RadiogramSpecification.java:20-27`|
|Byte|8 Bit, LSB-first|`:28-33`|
|Int|`length` Bit, LSB-first pro Byte|`:34-39`|
|String|Bytes, LSB-first pro Byte, Länge `length` oder `strlen`|`:40-65`|
|DeviceAddress|24 Bit|`:66-71`|
|RoomControlMode|2 Bit: Auto=`00`, Permanently=`10`, Temporary=`01`|`:72-94`, `RoomControlMode.java:5-8`|
|Temperature|6 Bit: `off`→9, `on`→61, gültig→`(int)(value*2)`|`:96-111`|
|Time (30 min)|6 Bit: `hour*2 + minCount`; `min>=45`→hour++, `15<min<45`→minCount=1|`:193-205`|
|Time (5 min)|8 Bit: `hour*12 + minute/5`; `0`→`288`; `>255`→Bit am `bytePos-1` setzen und 256 subtrahieren|`:207-219`|
|Offset-Temperatur|4 Bit: `(int)((value+3.5)*2)`|`:222-226`|
|Window-Open-Time|4 Bit: `value/5`|`:228-230`|
|Day / Month / Year|5 Bit `Calendar.DAY_OF_MONTH`; 4 Bit `month+1`; 7 Bit `year-2000`|`:233-249`|
|WeekDay|3 Bit `(value+1)%7`|`:281-285`|
|BoostTime|3 Bit: `value<=30 ? value/5 : 7`|`:287-290`|

**Beispiel `SetRoomTemporaryMode`** (`SetRoomTemporaryMode.java:46-48`):

```text
header(0x40, masterThermostatAddress)
  .addControlMode(10, 6, Temporary)   // Byte 10 Bit 6..7
  .addTemperature(10, 0, temp)        // Byte 10 Bit 0..5
  .addMonth(11, 4, date)              // Byte 11 Bit 4..7
  .addMonth(12, 7, date)              // Byte 12 Bit 7..10
  .addDay(11, 0, date)                // Byte 11 Bit 0..4
  .addYear(12, 0, date)               // Byte 12 Bit 0..6
  .addTime30MinResolution(13, 0, date)
```

**Beispiel Wochenprofil** (`SetRoomWeekTemperatureProfile.java:89-100`): pro Tag ein Telegramm mit `FrameType 0x10`, `weekDay` bei Byte 10 Bit 0..2, `telegram==1` bei Byte 10 Bit 4, Schaltpunkte ab Byte 11/12 (Paare aus 6-Bit-Temperatur ab Byte 11 Bit 1 und **9-Bit-Zeit**: 8 Bit in Byte 12, 9. Bit als Überlauf in Byte 11 Bit 0, vgl. `:404`); bei >7 Schaltpunkten ein zweites Telegramm mit 6 Punkten (`:81-84`). Der Byte-Abgleich gegen die Python-Kodierung ist in `byte-abgleich-python-java.md` belegt.

**Reset-Radiogramm `r:`** (`ResetDeviceRadiogram.java:20-38`, `DeleteDevicesCubeCommand.java:52-66`): Opcode ist wörtlich `r:01,`, Payload = drei Adressbytes big-endian, Base64-kodiert. Für `0x1d2a3c` → `r:01,HSo8\r\n` (Bytes `1d 2a 3c`; **Korrektur 24.11.2026:** eine ältere Fassung dieser Tabelle nannte `Ha0/PA==`, das vier Bytes kodiert und damit nicht zur Spezifikation passt). Danach Antwort `S:...` (gleicher Pfad wie `s:`).

**Zuordnung von Client-Befehlen zu Radiogrammen:** `TransmissionRadiogramBuilder.createDeviceRadiograms()` validiert den Befehl und erzeugt die Liste (`TransmissionRadiogramBuilder.java:12-16`); `ResetRadiogramBuilder` stellt bei Bedarf `r:01,…`-Reset-Telegramme **voran** (`ResetRadiogramBuilder.java:26-33,43-63`). Jeder Client-Befehl wird über `@ClientCommandInfo(scope=...)` einer Kategorie zugeordnet (HouseConfiguration, RoomControl, RoomConfiguration, DeviceConfiguration, DeviceAssociation, DeviceRemoval; `ResetRadiogramBuilder.java:142-167`).

---

## 9. AES-Verschlüsselung

- Der Client implementiert **keine** eigene AES-Verschlüsselung; er delegiert an den Cube:
  - `e:<plaintext>\r\n` → `E:<body>\r\n` (`AESEncryptCubeCommand.java:23-28`)
  - `d:<ciphertext>\r\n` → `D:<body>\r\n` (`AESDecryptCubeCommand.java:23-28`)
  - `getOpCodeBody()` trennt bei `:` **nicht** bei `,` – ein Base64-Inhalt mit Kommas würde abgeschnitten (INFERENZ aus `StringTokenizer(cmd,":")`; Base64 enthält normalerweise keine Kommas).
- Verwendungszweck: Remote-Proxy. `RemoteProxyRequest.java:40` verschlüsselt das Base64 des Request-Body; `RemoteProxyResponse.java:50-57` entschlüsselt die Antwort und dekodiert danach Base64.
- Idle-Keepalive: `CubeGateway.idleCall()` sendet `e:something\r\n` nach 900000 ms (`CubeGateway.java:291-301`).
- Fehlercodes aus `errorcodes.properties`: `AESKeyInvalidLength=MAX1001`, `AESKeyIsNotHex=MAX1002`, `EncryptionFailed=MAX1017`. Die Validierung des AES-Schlüssels liegt außerhalb der hier gelesenen Dateien → **UNBEKANNT**, ob diese Fehler vom Cube oder vom Client erzeugt werden.
- **UNBEKANNT:** Verfahren, Modus, Padding, Schlüssellänge und Schlüsselquelle sind aus dem lokalen Code nicht ersichtlich. Der Cube verarbeitet die Bytes intern.

---

## 10. Zeit, NTP und Sommer/Winterzeit

1. **Cube-Zeit im Header:** `H:`-Felder 7/8 (`YYMMDD` / `HHMM`, hex-kodierte Bytes) → `Calendar(Locale.GERMANY)`, Jahrhundert aus aktuellem Datum (`MaxCubeStateFactory.java:147-156`).
2. **State-Zeit:** Feld 9 (`stateCubeTime`, dezimal) und Feld 10 (`ntpCounter`, hex) (`:135-142`).
3. **Zeitzone setzen:** `v:`-Befehl (Abschnitt 7.7); Winter- und Sommerzeit werden getrennt übertragen, `maxCubeState.setDaylightSaving(!winterConfig.equals(summerConfig))` (`CubeConfigurationSerializer.java:41-43`).
4. **NTP-Server:** `f:` setzen/abfragen (Abschnitt 7.8).
5. **Client-API:** `APICalls.prototype.adjustTimeZoneConfiguration` (`APICalls.js:1914-1934`), `setTimeZoneConfiguration` (`:1942-1962`), `autoSetupTimeZoneConfiguration` (`:1968-1988`), `setCubeTime` (`:1990-2010`).
6. **Duty-/Free-Slots** aus `H:` fließen in `MaxCubeAirtimeStatus` ein (Abschnitt 7.5).
7. **UNBEKANNT:** Die genaue Bedeutung von `stateCubeTime` und `ntpCounter` sowie das Format der `F:`-Antwort sind nicht belegt.

---

## 11. Firmware-Update über UDP

### 11.1 Opcode-Tabelle

Quelle: `decompiled/localbackend/sources/de/eq3/max/al/local/update/CubeUdpOpcode.java:5-14`.

| Opcode | Name | Richtung | Payload (aus `CubeUdpRequestFactory.java`) | Quelle |
|---|---|---|---|---|
| `I` | Identify | App→Cube | – | `CubeUdpRequestFactory.java:21-27` |
| `N` | GetNetworkAddress | App→Cube / Broadcast/Multicast | – | `:17-19` |
| `B` | EnterBootloader | App→Cube | – | `:29-31` |
| `U` | InitUpdate | App→Cube | – | `:33-35` |
| `W` | WriteFrame | App→Cube | `indexHigh,indexLow,lengthHigh,lengthLow,data` | `:49-56` |
| `R` | Reboot | App→Cube | – | `:37-39` |
| `c` | GetNetworkConfiguration | App→Cube | – | `:41-43` |
| `C` | SetNetworkConfiguration | App→Cube | 5×4 IP-Bytes + DHCP-Flag + Encryption-Flag | `:45-47` |
| `h` | GetUrl | App→Cube | – | `:58-60` |
| `H` | SetUrl | App→Cube | `portHigh,portLow,hostBytes,0x2C(','),pathBytes` | `:62-65` |

**Wichtig:** `CubeUdpOpcode.H` ist **nicht** der TCP-Hello (`H:`), sondern der UDP-Opcode zum Setzen der URL. Kontext (UDP vs. TCP-Zeilenprotokoll) unterscheidet die Bedeutungen.

### 11.2 Datagramm-Format

**Request** (`CubeUdpRequest.java:16-28,93-104`):

```text
8 Byte  Signatur: 65 51 33 4D 61 78 2A 00  -> "eQ3Max*\0"
10 Byte Seriennummer (mit '*' aufgefüllt)
1 Byte  Opcode (ASCII)
n Byte  opcode-spezifische Payload
```

Hinweis: Der Java-Quelltext zeigt `new byte[]{101, 81, 51, 77, 97, 120, 42, 0}` (Zeile 94); das sind `e Q 3 M a x * NUL`. Im obigen Hex ist `4D`=77 (`M`) korrekt; die Discovery-Signatur in `CubeFinder.java:48` lautet `eQ3Max*`.

**Response** (`CubeUdpResponse.java:10-33,59-94`):

```text
8 Byte  Signatur (nur Bytes > 32 werden als Zeichen übernommen)
10 Byte Seriennummer
1 Byte  ID '>'   (0x3E)
1 Byte  Opcode (muss zum Request passen)
n Byte  Daten
```

Validierung (`CubeUdpResponse.java:96-110,129-166`): ID == `'>'`; Seriennummer leer oder gleich der erwarteten; Opcode gleich; bei `N` mindestens 4 Datenbytes und nicht alle null; bei `W` drei Byte Status/Index:

- `data[0] == 0` → `ConnectionLostUpdateException("write frame: failed")`
- `data[0] == 1` → OK
- `data[0] == 2` → OK (Restart)
- `data[1]<<8 | data[2]` muss dem angefragten Frame-Index entsprechen (`:141-161`).

### 11.3 Transportparameter und Ablauf

| Parameter | Wert | Quelle |
|---|---|---|
| Port | `23272` | `CubeUdpProxy.java:25` |
| Puffer | `1024` Byte | `:24` |
| Timeout | `2000 ms` | `:26,51` |
| Max. Versuche | `5` (mit je 10 ms Vorlauf) | `:23,32,211-225` |
| Reboot-Timeout | `20000 ms` | `:27,131-139` |
| Multicast / Broadcast | `224.0.0.1` / `255.255.255.255`, TTL 5 | `:28-31,45-50` |
| Fallback | Unicast↔Broadcast und Multicast↔Broadcast bei Fehler | `:187-208` |

Ablauf (`CubeUpdater.java:27-66`):

```text
beginUpdate
 -> CubeUdpProxy(serialNumber) + getNetworkAddress()      // IP ermitteln
 -> FirmwareFile(stream).getFrames()                       // hex-Datei parsen
 -> getMode() (I); wenn != Bootloader: B + waitForReboot + N
 -> U (InitUpdate)
 -> für jedes Frame: W(index, bytes)
 -> waitForReboot (20 s)
 -> endUpdate
```

`CubeMode.parse(signature)` (`CubeMode.java:9-17`): `eQ3MaxAp` → Application, `eQ3MaxBl` → Bootloader, sonst Offline.

**Firmware-Datei:** ASCII-Hex. `readLength()` liest zwei Hex-Ziffernpaare als `high<<8|low`; dann `length` Bytes, jeweils als zwei Hex-Ziffern (`FirmwareFile.java:57-82`). Bei `length <= 0` endet die Frame-Liste (`:49-55`). Parsing-Fehler → `InvalidFirmwareFileUpdateException` (`:27-32`).

**GetUrl-Antwort:** `data[0]<<8|data[1]` = Port; ab Byte 2 `host,path` (durch Komma getrennt) → `http://<host>:<port><path>` (`CubeUdpProxy.java:94-108`).

---

## 12. Fehlercodes und Java-Exceptions

### 12.1 `errorcodes.properties` (`extracted/essentials/errorcodes.properties:1-53`)

| Code | Symbol | Code | Symbol |
|---|---|---|---|
| MAX0001 | DefaultErrorCode / GeneralTechnicalFailure | MAX1027 | LocalValidation |
| MAX1001 | AESKeyInvalidLength | MAX1028 | Monitoring |
| MAX1002 | AESKeyIsNotHex | MAX1029 | NameIsNull |
| MAX1003 | AdminValidator | MAX1030 | NameTooLong |
| MAX1004 | AlreadyConnectedToCube | MAX1031 | NoUserName |
| MAX1005 | Application | MAX1032 | NotConnectedToCube |
| MAX1006 | CanNotUpdateState | MAX1033 | NotImplemented |
| MAX1007 | ConnectionLostUpdate | MAX1034 | RadioAddressHasInvalidLength |
| MAX1008 | CubeCommunicationFailed | MAX1035 | RadioAddressIsNotHex |
| MAX1009 | CubeIsInInclusionMode | MAX1036 | RadiogramCreationFailed |
| MAX1010 | CubeNotInventoried | MAX1037 | RemoteAccessDisabled |
| MAX1011 | DeserializationFailed | **MAX10038** | RemoteServiceUnavailable (auffällige 5-stellige Zahl, quellbelegt) |
| MAX1012 | Device | MAX1039 | SecurityViolation |
| MAX1013 | DeviceClientCommand | MAX1040 | SerialNumberInvalidLength |
| MAX1014 | DeviceIsNoNewDevice | MAX1041 | SerialNumberInvalidPattern |
| MAX1015 | DeviceNameAlreadyExists | MAX1042 | Serialization |
| MAX1016 | DownloadURLIsNull | MAX1043 | SoftwareInformationHasInvalidCountOfArguments |
| MAX1017 | EncryptionFailed | MAX1044 | SoftwareVersionIsInvalid |
| MAX1018 | FileWithoutContent | MAX1045 | SoftwareVersionIsNull |
| MAX1019 | FirmwareVersionIsInvalid | MAX1046 | SubjectNotAuthorized |
| MAX1020 | FirmwareVersionIsNull | MAX1047 | UnknownCredentials |
| MAX1021 | InvalidArguments | MAX1048 | Update |
| MAX1022 | InvalidDeviceRadioAddress | MAX1049 | UpdateAlreadyInProgress |
| MAX1023 | InvalidFirmwareFileUpdate | MAX1050 | Validation |
| MAX1024 | InvalidInventoryListEntry | MAX1051 | WrongSize |
| MAX1025 | InvalidResponseUpdate | | |
| MAX1026 | InvalidUserName | | |

### 12.2 Java-Exceptions im Protokollpfad

| Exception | Auslöser | Meldung | Quelle |
|---|---|---|---|
| `CubeCommunicationFailedException` | Falsche erste Zeile | `"No Incoming Hello"` | `CubeConnection.java:66` |
| `CubeCommunicationFailedException` | Firmware außerhalb 273..275 | `"incompatible firmware version"` | `:64` |
| `CubeCommunicationFailedException` | Fehler ohne IOException/RuntimeException | `"Unknown Error"` | `:150` |
| `CubeCommunicationFailedException` | Queue geleert bei close | `"command ignored"` | `:258` |
| `CubeCommunicationFailedException` | RX `l:` erwartet | `"device list expected"` | `ListDevicesCubeCommand.java:24` |
| `CubeCommunicationFailedException` | RX `c:` erwartet | `"configuration expected"` | `GetConfigurationCubeCommand.java:35` |
| `CubeCommunicationFailedException` | RX `A:` erwartet | `"ACK expected"` | `CubeConnectionCommand.java:56` |
| `CubeCommunicationFailedException` | RX `F:` erwartet | `"ACK of NTP server settings expected"` | `SetNTPServerCommand.java:39`, `QueryNTPServerCommand.java:31` |
| `CubeCommunicationFailedException` | RX `S:` erwartet | `"S: expected"` | `DeviceSendCommand.java:34` |
| `CubeCommunicationFailedException` | Radiogramm verworfen | `"Command discarded"` | `DeviceSendCommand.java:37` |
| `CubeCommunicationFailedException` | RX bei `q:` unerwartet vorhanden | `"disconnect expected"` | `DisconnectCubeCommand.java:17` |
| `InvalidArgumentsException` | RX `N:` erwartet | `"N: expected, but found ..."` | `FetchNewDevicesCubeCommand.java:64` |
| `InvalidArgumentsException` | `S:` hat != 3 Argumente | `"invalid arguments"` | `DeviceSendCommand.java:44,62` |
| `NotConnectedToCubeException` | Befehl ohne Verbindung | – | `CubeConnection.java:154`, `CubeGateway.java:222` |
| `AlreadyConnectedToCubeException` | Zweiter `connect()` | `"already connected"` | `CubeGateway.java:116` |
| `CubeIsInInclusionModeException` | Befehl während Inclusion | `"cube is in inclusion mode"` | `CubeConnection.java:266`; `CubeIsInInclusionModeException.java:10` |
| `SocketTimeoutException`, `SocketException` | Socket-Zeitüberschreitung/Fehler | – | `CubeConnection.java:236-241`, `CubeGateway.java:307-313` |
| `ConnectionLostUpdateException` | UDP-Update fehlgeschlagen | `"write frame: failed"` / `"write frame: unknown error %d"` | `CubeUdpResponse.java:154,160` |

Alle `Cube*Exception`-Klassen erben von `CubeException` → `ApplicationException` (`CubeException.java:5`).

---

## 13. Authentische Sequenz (Connect → L:/C:/M: → Befehl → A:)

Die folgende Sequenz ist aus den Empfangs- und Sende-Pfaden rekonstruiert. Konkrete Payload-Werte sind **Beispiele (INFERENZ)**, Reihenfolge und Opcode-Abfolge sind quellbelegt.

```text
[App]  TCP connect 192.168.1.42:62910
[Cube] H:JEQ0001234,1d2a3c,111,00,00,05,1f,1a0b18,0c1e,3,0002
[App]  socket.setSoTimeout(70000)
[Cube] C:1d2a3c,AQABHQoD...          (Cube-Konfiguration; in RawCubeState gemerkt)
[Cube] C:1d2a01,AQABHQoD...          (Geräte-Konfiguration)
[Cube] M:00,05,eyJyb29tcyI6W119      (Metadata-Block 0 von 5)
[Cube] M:01,05,...
[Cube] M:02,05,...
[Cube] M:03,05,...
[Cube] M:04,05,...
[Cube] L:CgEdKgEcATU...              (Device-Liste; beendet receiveDeviceAndMetaData)

[App]  s:<base64 Radiogramm frameType=0x40, receiver=1d2a3c, mode=Temporary, temp=21.0>
[Cube] S:05,0,1f
       -> dutyCycle=5, discarded=false, freeMemorySlots=31

[App]  c:1d2a01
[Cube] C:1d2a01,AQABHQoD...

[App]  f:pool.ntp.org
[Cube] F:pool.ntp.org

[App]  q:
       -> connect=false, keine weitere Antwort; CubeGateway.dropConnection()
```

**Variante Inclusion:**

```text
[App]  n:003c
[Cube] N:<base64: type=4, address, serial>
[App]  x:                     (direkt oder als CancelInclusionModeCubeCommand)
[Cube] N:                     (leerer Body -> Inclusion-Mode aus)
```

**Variante Metadata-Send:**

```text
[App]  m:00,<1900 Bytes JSON>     -> [Cube] A:
[App]  m:01,<rest>                -> [Cube] A:
```

---

## 14. Implementierungshinweise für eine Neuimplementierung

1. **Parser:** Zeilenorientiert, CRLF, `startsWith(opcode)`; niemals `String.split(":")` für den Body verwenden (Base64 kann `=` enthalten, aber keine `:`; `A:`-Body wird ohnehin ignoriert). Für `C:`/`M:`/`S:` `substring(2).split(",")`.
2. **Reihenfolge:** Nach dem Hello zwingend `C:`/`M:`-Zeilen sammeln, bis `L:` eintrifft; erst dann Befehle senden.
3. **Serialisierung:** Alle Hex-Werte lowercase, feste Breite (8/16/24/32 Bit) gemäß `Conversion.toHexXX`; Base64 ohne_newline.
4. **ACK-Pflicht:** Nur bei Befehlen, die `readAcknowledge()` aufrufen (Abschnitt 4). `q:`, `a:`, `c:`, `l:`, `e:`, `d:`, `n:`, `f:` haben eigene Antwortregeln.
5. **Nebenläufigkeit:** Ein Socket, ein Writer, eine Queue, ein Worker; niemals zwei Befehle gleichzeitig schreiben.
6. **Radiogramme:** Bitreihenfolge LSB-first innerhalb der Bytes, Byte-Reihenfolge big-endian im `reorderInt(..., LITTLE_ENDIAN)`-Pfad (counterintuitiv, aber quellbelegt, `ByteUtil.java:23-33`).
7. **Firmware-Grenze:** 273..275 (`0x111`..`0x113`). Für Versionsvergleiche im ACK-Pfad wird `> 22` (dezimal) geprüft; das ist bei erlaubten Firmwares immer wahr.
8. **Portwahl:**_IP `127.0.0.1` oder eigene Host-IP → 54321, sonst 62910.
9. **UDP-Update:** getrennt vom TCP-Kanal, Port 23272, 5 Retries à 2 s, Frame-Index in `W`-Antwort validieren.

---

## 15. Explizite Unbekannten und INFERENZEN

| Thema | Status | Bemerkung |
|---|---|---|
| Cube-seitige Implementierung des TCP-Servers | UNBEKANNT | Keine Firmware/kein PCAP; alles aus Client-Erwartungen abgeleitet |
| `H:`-Felder 3 und 4 | UNBEKANNT | Vom Client nicht ausgewertet (`MaxCubeStateFactory.java:120` beginnt bei Index 5) |
| `stateCubeTime`, `ntpCounter` | UNBEKANNT | Nur gespeichert, nicht interpretiert (`MaxCubeStateFactory.java:135-142`) |
| `F:`-Antwortformat | UNBEKANNT | Nur `startsWith("F:")` geprüft; Parsing nicht in gelesenen Dateien |
| `OUTGOING_MORE_DATA` (`g:`) | UNBEKANNT | Tote Konstante (`IOperationCodes.java:38`); kein Produzent im Java-Baum. Die operative `g:`-Antwort ist geklärt (Abschnitt 6.1) |
| AES-Verfahren/Schlüssel | UNBEKANNT | Delegation an Cube; kein Schlüssel im lokalen Code gefunden |
| Remote-Proxy `B/G/J/P/O/V/W` samt Antworten | CODE-BELEGT, bewusst unübernommen | Vollständig implementiert in `decompiled/*/remoteproxy/`; aber HTTP-Cloud-Protokoll, nicht Cube-TCP. Cloud seit 01.07.2023 abgeschaltet (Abschnitt 6.1) |
| 5000-ms-Scheduler für Watchdog/idleCall | INFERENZ | Vom Auftraggeber verifiziert; Scheduler-Bean nicht in analysierten Dateien |
| `CubeGateway.run()`-Schleife | INFERENZ | JADX-Dekompilierung inkonsistent (`CubeGateway.java:63-106`); Ablauf aus Byte-Code-Kommentar abgeleitet |
| `SetUrlCommand`-Default | quellbelegt | `http://www.eq3.de` (`SetUrlCommand.java:9`) |
| `infiniteInterval` (-1) | UNBEKANNT | Konfiguriert (`max.local.properties:7`), aber kein Aufrufer in `IntervalCubeCommand` ersichtlich |
| Konkrete Werte in Beispielen | INFERENZ | Formate quellbelegt, Adressen/Seriennummern frei gewählt |
| `RemoteServiceUnavailable=MAX10038` | quellbelegt | Vermutlich Tippfehler im Original, nicht korrigiert (`errorcodes.properties:40`) |
| `ShutterConactState`, `HeatungThermostatState` | quellbelegt | Tippfehler in `FrameType.java:20,28` absichtlich übernommen |
| REA-Scan-Limitation | quellbelegt | REA-Bericht weist darauf hin, dass ZIP-Nicht-Entry-Bytes nicht erfasst werden (`evidence/localbackend-rea.json`, `limitations`) |

---

*Ende der Spezifikation. Alle Zeilennummern beziehen sich auf die JADX-dekompilierten Quellen unter `app-maxcube/decompiled/` bzw. die extrahierten Konfigurationsdateien unter `app-maxcube/extracted/`; Artefakt-Hashes und Evidence-IDs stammen aus den REA-CLI-Bundles unter `app-maxcube/evidence/`.*
