# Erkenntnisse aus `ha-maxcube` für die C#/.NET-10-Neuimplementierung

Stand: 24.11.2026. Zweck: Aus dem live-bewährten Projekt `ha-maxcube` und der statischen
Reverse Engineering (REA) der Original-JARs konkrete Protokoll- und Fachregeln ziehen,
die für die MAX!-App in C#/.NET 10 übernommen werden sollen. Es werden **keine** Quelltexte
kopiert, sondern Regeln formuliert. Jede Aussage trägt einen Beleg in der Form
`Datei.py:Zeile` bzw. `Datei.java:Zeile` (oder einen als solchen gekennzeichneten Wiki-Beleg).
Nicht Belegtes ist ausdrücklich `UNBEKANNT`.

## 1. Methodik und Evidenzklassen

Folgende Klassen werden strikt getrennt; sie dürfen nicht vermischt werden:

- **LIVE-BEWÄHRT** – am echten Cube bzw. in der laufenden Home-Assistant-Instanz beobachtet.
  Belege liegen in `SecondBrain/wiki/…`, nicht in Python-/Java-Quelltexten.
- **CODE-BELEGT** – direkt aus einer `.py`- oder `.java`-Datei mit Zeilennummer lesbar.
- **INFERENZ** – aus mehreren CODE-BELEGTEN Fakten abgeleitet; ausdrücklich als Ableitung
  gekennzeichnet und niemals als Fakt ausgegeben.
- **UNBEKANNT** – trotz Suche nicht belegt. Darf nicht durch Plausibilität ersetzt werden.

### 1.1 Pfadlegende der Belege

| Kurzname | Ort |
| --- | --- |
| `schedule.py`, `climate.py`, `const.py`, `button.py`, `display.py`, `coordinator.py`, `cube.py` (HA) | `C:\Users\ebenh\OneDrive\Coding\ha-maxcube\custom_components\maxcube_local\` |
| `cube.py` (Lib), `commander.py`, `device.py` (Lib) | `C:\Users\ebenh\OneDrive\Coding\welock-bluetooth\.venv\Lib\site-packages\maxcube\` (maxcube-api 0.4.3) |
| `fake_cube.py`, `test_fake_cube.py` | `C:\Users\ebenh\OneDrive\Coding\ha-maxcube\tests\` |
| `WeekTemperatureProfile.java`, `DayTemperatureProfile.java`, `TemperatureProfileSwitchPoint.java`, `SetRoomWeekTemperatureProfile.java`, `FrameType.java`, `RadiogramSpecification.java`, `Constants.java`, `DayOfWeek.java`, `LocalConfiguration.java`, `ServerSettings.java` | `C:\Users\ebenh\OneDrive\Coding\app-maxcube\decompiled\…\sources\de\eq3\max\…` |
| `PortFactory.java`, `Application.java` | `C:\Users\ebenh\OneDrive\Coding\app-maxcube\decompiled\maxlocalapp\sources\de\eq3\max\MaxLocalAppStarter\` |
| `maxcube-protocol.md` | `C:\Users\ebenh\OneDrive\Coding\app-maxcube\docs\maxcube-protocol.md` (eigene statische REA) |
| `max-cube-protokoll.md`, `ha-maxcube.md`, `max-local-application.md` | `C:\Users\ebenh\OneDrive\Coding\SecondBrain\wiki\…` |

Hinweis zur Belegform: Bei REA-Dateien und Wiki-Seiten wird die Zeilennummer mit `:` angegeben,
z. B. `maxcube-protocol.md:424` bzw. `ha-maxcube.md:39`. Die Einordnung erfolgt über die
Kennzeichnung im Text (LIVE-BEWÄHRT / CODE-BELEGT / INFERENZ).

## 2. Quellenlage

### 2.1 Live-bewährte Quellen (LIVE-BEWÄHRT)

- ha-maxcube v1.8.3 ist seit 24.11.2026 ~07:39 live installiert und `loaded`
  (`ha-maxcube.md:15`).
- Der Modusfix wurde am 24.11.2026 live geprüft: 07:50:28 +01:00 `off` / 4,5 °C /
  `hvac_action=off`; 07:51:07 +01:00 nach `HEAT` `heat` / 19,5 °C; 07:56:47 +01:00 wieder
  `off` / 4,5 °C (`ha-maxcube.md:39`).
- Die beiden Anzeige-Knöpfe (0x82) wurden am 22.11.2026 am echten Wandthermostat bestätigt
  (`ha-maxcube.md:27`, `max-cube-protokoll.md:25`).
- Discovery/Reboot per UDP 23272 und die Antwort `eQ3MaxAp` mit 10-stelliger Seriennummer
  wurden am 22.11.2026 live beobachtet (`max-cube-protokoll.md:28`).
- Die Web-Oberfläche der MAX!-Software lief auf `http://localhost:63121/`
  (`max-local-application.md:16`, Rohquelle `raw/sitzungen/2026-11-22-…:44`).
- Der TCP-Port 62910 des Cube war mehrfach Gegenstand live beobachteter Blockaden; Ping und
  UDP funktionierten dabei weiterhin (`max-cube-protokoll.md:21`, `ha-maxcube.md:76`).

### 2.2 Code-bevorzugte Quellen (CODE-BELEGT)

- Komplette Integrationslogik in Python (`schedule.py`, `climate.py`, `const.py`, `button.py`,
  `display.py`, `coordinator.py`, `cube.py`, `linkstatus.py`, `health.py`).
- Bibliothek `maxcube-api` 0.4.3 mit `cube.py`, `commander.py`, `device.py`.
- Dekompilierte Hersteller-JARs (MaxEssentialsBackend 1.4.6, MaxLocalBackend 1.4.6,
  MaxLocalAppStarter) als `FrameType.java`, `SetRoomWeekTemperatureProfile.java`,
  `LocalConfiguration.java`, `PortFactory.java`, `Application.java` usw.
- Fake-Cube als struktur invariant: `fake_cube.py:1-10` sagt selbst, dass er das Layout
  nachbildet, das Bibliothek und `linkstatus.py` lesen – **kein** Beweis für echtes Hardwareverhalten.

### 2.3 Ausdrücklich nicht vorhanden

- Ein direkter Code-Beleg für den konkreten Zahlenwert `63121` in IRGENDEINER `.py`-,
  `.java`- oder Konfigurationsdatei: `UNBEKANNT` (gezielte Suche über den gesamten Workspace
  mit den Endungen `py,md,java,properties,json,yaml,txt,settings,cfg,conf,xml` fand keinen Treffer;
  nur eine `firmware.enc`-Binärdatei enthält die Ziffernfolge, die nicht als Beleg taugt).
- Ein Beleg dafür, dass der Cube `63121` selbst verwendet: `UNBEKANNT`.
- Die Ursache der zeitweisen TCP-Port-Blockade 62910: `UNBEKANNT` (`ha-maxcube.md:76`).

## 3. Konflikt 1: Schaltpunkte je Tag

### 3.1 Was die Quellen sagen

- `schedule.py:22` definiert `CUBE_DAY_ORDER` beginnend mit **Samstag**, dann Sonntag, Montag …
  Freitag. Das ist die Reihenfolge der Wochenprogramm-Blöcke.
- `schedule.py:27-28` setzt `BLOCK_BYTES = 26` und `SLOTS_PER_DAY = 13`: ein Tag belegt 26 Byte,
  also genau 13 Zwei-Byte-Wörter.
- `schedule.py:55-63` liest pro Block 13 Wörter und dekodiert
  `minutes = (word & 0x1FF) * 5` (`schedule.py:62`) sowie `temp = (word >> 9) / 2`
  (`schedule.py:63`). Die unteren 9 Bit sind die Zeit in 5-Minuten-Schritten, die oberen 7 Bit
  die Temperatur in Halbgraden (`schedule.py:3-7`).
- `schedule.py:89-91` formuliert die Schreibregel ausdrücklich: **ein Funktelegramm** trägt
  höchstens 7 Sollpunkte; der Cube speichert beim Auslesen bis zu 13.
- `schedule.py:107-118` füllt kurze Tage mit Kopien des letzten Slots auf, sodass **genau 7**
  Wörter gesendet werden; `schedule.py:116` kodiert den Tag als Index in `CUBE_DAY_ORDER`
  (Samstag = 0).
- `schedule.py:153-155` verlangt steigende Zeiten, 5-Minuten-Raster, letzten Slot `24:00` und
  Temperaturen von 4,5 bis 30,5 °C. `tests/test_schedule.py:214-219` bestätigt `24:00` mit 4,5
  bzw. 30,5 °C als gültige Grenzen.
- Bibliotheksseite: `CMD_SET_PROG = "10"` (`cube.py:27`), `DAYS` ab Samstag (`cube.py:33`),
  Pad auf 7 (`cube.py:328-329`) und `get_programme` mit `int(int(word[:7], 2) / 2)`
  (`cube.py:387`), also Verlust der halben Grade beim Auslesen.
- Java: `DayTemperatureProfile.java:17-18` erlaubt `@Size(min = 1, max = 13)` Schaltpunkte je
  Tag. `WeekTemperatureProfile.java:14` verlangt genau 7 Tagesprofile
  (`Constants.COOKIE_AGE_DAYS`). `DayOfWeek.java:7-13` nummeriert Montag=1 … Sonntag=7;
  `RadiogramSpecification.java:281-285` ergibt `(value + 1) % 7` den Cube-Index – Samstag wird
  damit 0, Sonntag 1. `DayOfWeek.java:26-27,33-34` betrifft nur die `Calendar`-Umrechnung.
- Java-Schreibregel: `SetRoomWeekTemperatureProfile.java:81` erzeugt pro geändertem Tag ein
  erstes Telegramm mit `createDayPartSpec(day, 0, 7)`; `:82-84` erzeugt **nur dann** ein zweites
  Telegramm mit `createDayPartSpec(day, 1, 6)`, wenn `getSwitchPointsNormalized().size() > 7`.
  `createDayPartSpec` (`:89-100`) setzt bei Byte 10 Bit 0..2 (3 Bit) den Wochentag, Bit 4 die
  Telegrammnummer 1 (`:90`), den Offset `7 * telegram` (`:91`) und füllt fehlende Punkte mit
  dem letzten bekannten Punkt auf (`:96-97`).

### 3.2 Auflösung

**ENTSCHIEDEN (CODE-BELEGT):** Der Cube speichert **13** Schaltpunkte je Tag
(`schedule.py:28`, `DayTemperatureProfile.java:17-18`). Ein **Schreib-Telegramm** enthält
**7** Punkte (`schedule.py:89-91`, `SetRoomWeekTemperatureProfile.java:81`). Python schreibt
ausschließlich dieses eine Telegramm (`schedule.py:97-118,159-160`). Bei mehr als 7 Punkten
folgt laut Hersteller-Java ein **zweites Telegramm mit 6 weiteren Punkten**
(`SetRoomWeekTemperatureProfile.java:82-84`) – nicht „6 je Tag“. Das zweite Telegramm ist
allein Java-belegt; ein Livebeleg am Cube liegt nicht vor.

Die Wiki-Formulierung „maximal 7 Abschnitte je Tag und Schreib-Telegramm“
(`max-cube-protokoll.md:64`) ist daher **pro Telegramm** zu lesen. Die REA-Aussage
„bei >7 Punkten ein zweites Telegramm mit 6 Punkten“ (`maxcube-protocol.md:424`) ist korrekt;
die ältere Inferenz „6 Schaltpunkte je Tag“ war falsch.

**INFERENZ:** Die C#-Implementierung sollte intern 13 Slots je Tag halten, beim Schreiben aber
7 + ggf. 6 in zwei Telegramme aufteilen und unbenutzte Slots mit dem letzten Slot auffüllen.
Diese Ableitung folgt aus `schedule.py:28,89-91,107` und
`SetRoomWeekTemperatureProfile.java:81-97`. Der zweite Teil (7+6) ist Java-only; Python
unterstützt nur bis zu 7 Punkte pro Tag und darf nicht als Beleg dafür zitiert werden.

## 4. Konflikt 2: Port 63121 gegenüber 62910/54321

### 4.1 Drei klar getrennte Port-Begriffe

1. **Cube-TCP 62910** – CODE-BELEGT: `const.py:7` (`DEFAULT_PORT = 62910`), Bibliothek
   `cube.py:32` (`DEFAULT_PORT = 62910`), Hersteller-Java `LocalConfiguration.java:12`
   (`DEFAULT_CUBE_PORT = 62910`). Das ist der Port, den die C#-App für `H:/M:/C:/L:` und
   `q:/l:/s:` verwenden muss.
2. **Virtueller Cube 54321** – CODE-BELEGT: `max.local.properties:22`
   (`max.connection.virtualCubePort=54321`). `LocalConfiguration.java:81-89` liefert diesen
   Port nur, wenn die IP `127.0.0.1` oder die eigene Host-IP ist; sonst 62910.
   Für die C#-App ist das **kein** Zielport, sondern ein Test-/Simulationspfad des Herstellers.
3. **Web-UI der MAX!-Software 63121** – LIVE-BEWÄHRT: `max-local-application.md:16` nennt
   `http://localhost:63121/`. Der **Mechanismus** ist CODE-BELEGT: `ServerSettings.java:6-14`
   speichert einen `ServerPort` in `max.settings`; `PortFactory.java:23-29` wählt bei Bedarf
   einen zufälligen freien Port zwischen 4711 und 65535; `Application.java:136-157` holt ihn,
   wenn der gespeicherte Port fehlt oder belegt ist; `Application.java:160-162` speichert ihn;
   `Application.java:190-192` startet Jetty mit genau diesem Wert; `Application.java:220`
   öffnet den Browser auf diese Adresse.

### 4.2 Auflösung

**ENTSCHIEDEN (LIVE-BEWÄHRT + CODE-BELEGT):** 63121 ist der **lokale HTTP-Port der
Hersteller-Weboberfläche**, nicht der Cube-Protokollport. Der Wert selbst ist nur live
beobachtet (`max-local-application.md:16`); dass der Port dynamisch vergeben und in
`max.settings` gespeichert wird, ist CODE-BELEGT (`PortFactory.java:23-29`,
`Application.java:136-162`, `ServerSettings.java:6-18`). Warum ausgerechnet 63121 gewählt
wurde, ist `UNBEKANNT` (kein Code-Treffer, keine Config-Datei mit diesem Wert).

Die Wiki-Zeile `max-cube-protokoll.md:65` („63121 = zufälliger Web-UI-Port …“) ist damit
im Kern bestätigt, aber der konkrete Zahlenwert bleibt live-basiert.

**Konsequenz für C#:** Kein HTTP-Server, kein Jetty, kein Browser-Plugin. Die
Zielarchitektur sieht eine native Avalonia-UI vor (`target-architecture.md:13`). Die
Ports 63121 und 54321 gehören nicht in die C#-Verbindungskonfiguration.

### 4.3 Weitere Ports (nicht verwechseln)

- UDP 23272 = Discovery **und** Reboot (`discovery.py:17-20`, `button.py:3-8,36-41`).
  CODE-BELEGT und LIVE-BEWÄHRT (`max-cube-protokoll.md:28`).
- `maxcube-protocol.md:537` beschreibt eine `GetUrl`-Antwort mit Port aus `data[0]<<8|data[1]`;
  das betrifft den Proxy-URL-Abruf, nicht den Cube-TCP-Port.

## 5. Konflikt 3: Modus- und Temperaturkodierung

### 5.1 Die Zahlen

- `climate.py:71-74`: `ON_TEMPERATURE = 30.5`, `OFF_TEMPERATURE = 4.5`,
  `MIN_TEMPERATURE = 5.0`, `MAX_TEMPERATURE = 30.0`. Die 4,5/30,5 sind also Grenzwerte im
  Funkprotokoll, 5,0/30,0 die UI-Grenzen.
- `schedule.py:92-94` bestätigt `MIN_TEMPERATURE = 4.5` („off“) und
  `MAX_TEMPERATURE = 30.5` („on“), Raster 5 Minuten.
- Bibliothek `device.py:10-13`: Modi `AUTOMATIC=0`, `MANUAL=1`, `VACATION=2`, `BOOST=3`.
- Bibliothek `cube.py:284-309`: `set_temperature_mode(thermostat, temperature, mode)`;
  `target_temperature = int(temperature * 2) + (mode << 6)` (`cube.py:307`); daraus wird der
  Frame `000440000000` + RF-Adresse + Raum + Byte (`cube.py:309`). Die unteren 6 Bit tragen
  die Temperatur in Halbgraden, die oberen 2 Bit den Modus.
- `cube.py:279,282` zeigen die Falle: `set_target_temperature` und `set_mode` delegieren zwar an
  `set_temperature_mode`, aber mit `None` für den jeweils anderen Parameter – der alte Modus
  bleibt damit erhalten.

### 5.2 Die Live-Korrektur v1.8.3

- LIVE-BEWÄHRT: Vor dem Fix setzte `Aus` nur 4,5 °C und behielt `AUTO`; die Entität zeigte
  `auto` statt `off`, der Sollwert wurde vom Wochenprogramm überschrieben
  (`ha-maxcube.md:34`). Diagnose: `state=auto`, `temperature=4,5`,
  `scheduled_temperature=17`, `valve_position=0` (`ha-maxcube.md:34`).
- CODE-BELEGT: `climate.py:315-322` liefert `off` **nur** bei `MANUAL` **und** 4,5 °C;
  sonst `HEAT` bei `MANUAL`, `AUTO` in allen anderen Fällen.
- CODE-BELEGT: `climate.py:318-321` und `:358-367`: `OFF` → 4,5 °C; `HEAT` → letzter echter
  Sollwert (geklemmt auf `min_temp..max_temp`); `AUTO` → `set_mode(AUTOMATIC)`.
- CODE-BELEGT: `climate.py:170-180` merkt sich den letzten Heizsollwert nur bei `MANUAL`,
  nicht 4,5/30,5 °C und nur innerhalb der UI-Grenzen. `climate.py:182-193` fällt zurück auf
  Programmsollwert → Komfortsollwert → `min_temp`.
- CODE-BELEGT: `climate.py:389-407` – **alle** Sollwertwege rufen
  `cube.set_temperature_mode(device, temperature, MAX_DEVICE_MODE_MANUAL)`; danach wird der
  echte Wert gemerkt und ein Refresh angefordert.
- CODE-BELEGT: `CHANGELOG.md:13-15` beschreibt exakt denselben Fix und den Dashboard-Umschalter;
  `ha-maxcube.md:30` verknüpft ihn mit PR #41 / Tag `v1.8.3`.
- Tests als Stütze: `tests_ha/test_integration.py:615-619` fordert, dass ein Sollwert
  `AUTO` verlässt und `MANUAL` mitsendet; `:621-630` prüft `OFF` aus `AUTO`.
  `fake_cube.py:24-27` bildet die vier Modi nach.

### 5.3 Auflösung

**ENTSCHIEDEN (LIVE-BEWÄHRT + CODE-BELEGT):** Ein Sollwert darf niemals ohne Modus gesendet
werden. `AUS` ist nicht „ein niedriger Sollwert“, sondern `MANUAL + 4,5 °C`. `HEAT` ist
`MANUAL + letzter echter Sollwert`. `AUTO` ist der Modus 0 ohne eigenen Sollwert.
30,5 °C ist der „On“-Grenzwert und wird wie 4,5 °C nicht als Heizsollwert gemerkt
(`climate.py:177,403`).

Offen: Ob der Cube die Modi intern identisch codiert (Java `RoomControlMode` vs. Python
`device.py:10-13`) ist **INFERENZ** aus `cube.py:307` und `FrameType.java:21-25` und wurde nicht
durch ein direktes Java-Bitfeld belegt. Für die C#-Implementierung genügt die Python-Kodierung,
weil sie live verifiziert ist.

## 6. Konflikt 4: Radiogramm-Bitfelder

### 6.1 Rahmenaufbau (CODE-BELEGT)

- `maxcube-protocol.md:339-340`: `addByte(2, 0, frameTypeCode)` → FrameType in **Byte 2**;
  `addDeviceAddress(6, 0, receiverAddr)` → Empfängeradresse in **Byte 6–8** (24 Bit).
- `maxcube-protocol.md:351-354`: `RadiogramSpecification.createPayload()` schreibt Bits
  **LSB-first** innerhalb eines Bytes (`bytes[i/8] |= (1 << (i%8))`); Ganzzahlen über
  `ByteUtil.fromInt(value, bitLength)`; Byte-Reihenfolge big-endian im
  `reorderInt(..., LITTLE_ENDIAN)`-Pfad (`maxcube-protocol.md:658`).
- Direkt belegt in Java: `FrameType.java:8-34` listet die Codes, `FrameType.java:40-43`
  die Registrierung, `FrameType.java:46-50` den Lookup.
- `display.py:25-30` kodiert das Anzeige-Telegramm als
  `00 00 82 000000 <RF 3 Byte> 00 <04|00>`: Byte 2 = 0x82, Byte 10 = 0x04 (Ist) oder 0x00
  (Soll). Das deckt sich mit der Java-Seite `SwitchDisplaySetPointActual((byte) -126, …)`
  (`FrameType.java:31`, -126 = 0x82 als vorzeichenbehaftetes Byte).

### 6.2 FrameType-Tabelle (CODE-BELEGT, `FrameType.java:9-34`)

| FrameType | Code | Kategorie | Beleg |
| --- | --- | --- | --- |
| SystemInformation | 0x00 | None | `FrameType.java:9` |
| Inclusion | 0x01 | None | `FrameType.java:10` |
| Answer | 0x02 | None | `FrameType.java:11` |
| TimeInformation | 0x03 | None | `FrameType.java:12` |
| ConfigWeekTemperatureProfile | 0x10 | Configuration | `FrameType.java:13` |
| ConfigTemperatures | 0x11 | Configuration | `FrameType.java:14` |
| ConfigValveFunctions | 0x12 | Configuration | `FrameType.java:15` |
| AddLinkPartner | 0x20 | Assignment | `FrameType.java:16` |
| RemoveLinkPartner | 0x21 | Assignment | `FrameType.java:17` |
| SetGroupAddress | 0x22 | Assignment | `FrameType.java:18` |
| RemoveGroupAddress | 0x23 | Assignment | `FrameType.java:19` |
| ShutterConactState | 0x30 | None | `FrameType.java:20` |
| ControlMode | 0x40 | Control | `FrameType.java:21` |
| SetPointTemperature | 0x41 | Control | `FrameType.java:22` |
| SetPointAndCurrentTemperature | 0x42 | None | `FrameType.java:23` |
| ComfortTemperature | 0x43 | Control | `FrameType.java:24` |
| EcoTemperature | 0x44 | Control | `FrameType.java:25` |
| CurrentTemperatureAndHumidity | 0x45 | None | `FrameType.java:26` |
| PushButtonState | 0x50 | None | `FrameType.java:27` |
| HeatungThermostatState | 0x60 | None | `FrameType.java:28` |
| LockManualControls | 0x80 | Control | `FrameType.java:29` |
| DaylightSavingTimeMode | 0x81 | None | `FrameType.java:30` |
| SwitchDisplaySetPointActual | 0x82 | Configuration | `FrameType.java:31` |
| Reset | 0xF0 | Remove | `FrameType.java:32` |
| WakeUp | 0xF1 | None | `FrameType.java:33` |
| Test | 0xFF | None | `FrameType.java:34` |

Die Tippfehler `ShutterConactState` und `HeatungThermostatState` stehen so im Original
(`FrameType.java:20,28`; vgl. `maxcube-protocol.md:682`) und sind absichtlich übernommen.

### 6.3 Wochenprofil im Radiogramm (CODE-BELEGT)

- `SetRoomWeekTemperatureProfile.java:90`: Header mit `FrameType 0x10`, Wochentag bei
  Byte 10 Bit 0..2 (3 Bit), Telegramm-1-Flag bei Byte 10 Bit 4.
- `SetRoomWeekTemperatureProfile.java:98`: pro Punkt `addTime5MinResolution(12 + 2*count, 0, …)`
  und `addTemperature(11 + 2*count, 1, …)` – Zeit und Temperatur liegen in **aneinandergrenzenden
  Bitfeldern** (Byte 11 ab Bit 1 = Temperatur, Byte 12 ab Bit 0 = Zeit; das 9. Zeitbit als
  Überlauf in Byte 11 Bit 0).
- `RadiogramSpecification.java:193-203` (`addTime30MinResolution`) belegt das Muster für
  30-Minuten-Felder; das 5-Minuten-Pendant wird in `SetRoomWeekTemperatureProfile.java:98`
  verwendet.
- Die Python-Seite kodiert dagegen kompakte 16-Bit-Wörter mit 7 Bit Temperatur + 9 Bit Zeit
  (`schedule.py:62-63,108-110`). **KORREKTUR 24.11.2026 (CODE-BELEGT):** Beide Beschreibungen
  sind zwei Sichten auf dasselbe 2-Byte-Paar. Der direkte Byte-für-Byte-Abgleich ist aus dem
  Code ableitbar und positiv; vollständige Ableitung in `byte-abgleich-python-java.md`.
  Die 6 Java-Temperatur-Bits und das 9. Zeitbit ergeben genau das Python-Wort.
- `SetRoomWeekTemperatureProfile.java:103-115` sendet nur **geänderte** Tage, sofern nicht
  `overwrite` gesetzt ist. Für C# ist das eine optionale Optimierung, keine Pflicht.

### 6.4 Status-Bitfelder der L-Nachricht (LIVE-BEWÄHRT + CODE-BELEGT)

- `linkstatus.py:31-40` beschreibt das Layout pro Submessage: Byte +0 Länge (ab Offset 1
  gezählt), +1..+3 RF-Adresse, +4 unbekannt, +5 `bits1` (Bit 4 gesetzt, Bit 3 `error`,
  Bit 2, Bit 1 `initialized`), +6 `bits2` (Bit 7 Batterie leer, Bit 6 Linkfehler, Bit 5
  Panel gesperrt, Bit 4 Gateway bekannt, Bit 3 DST, Bit 1–0 Modus). Bei 6-Byte-Submessages
  (Fensterkontakte) fehlt Byte +6 (`linkstatus.py:40`).
- LIVE-BEWÄHRT: `Link_Error` (bits2 Bit 6) blieb bei allen 16 Geräten auf 0, auch bei einem
  erkennbar totes Thermostat; `Error` (bits1 Bit 3) war genau bei diesem einen Gerät gesetzt
  (`linkstatus.py:9-17`). `Initialized` war bei vier Fensterkontakten klar, die trotzdem
  paarweise korrekt funktionierten (`linkstatus.py:18-21`).
- `linkstatus.py:26-29` zieht daraus die Regel: keine einzelne Bitflag ist autoritativ;
  stattdessen Rohbits **und** eine „Stale“-Erkennung über letzten Empfang anbieten.
- `linkstatus.py:65-74` definiert `BIT_INITIALIZED = 1 << 1`, `BIT_ERROR = 1 << 3`,
  `BIT_LINK_ERROR = 1 << 6`, Mindestlänge 6.
- `cube.py:237-243` (Lib) liest `bits1, bits2` aus `data[pos+5:pos+7]` und die Solltemperatur
  aus `data[pos+8] & 0x7F`; `fake_cube.py:34-38` spiegelt die Bits 0x10, 0x02, 0x08, 0x40, 0x80.

**ENTSCHIEDEN:** Für C# `Error` (bits1 Bit 3) als primäres „Gerät antwortet nicht“-Signal
verwenden, `Link_Error` nicht als Erreichbarkeitskriterium, und zusätzlich ein Zeit-basiertes
`stale`. `UNBEKANNT` bleibt, ob andere Cube-Firmwareversionen `Link_Error` doch setzen.

## 7. Weitere Protokoll- und Fachregeln

### 7.1 Verbindung und Polling

- Der Cube akzeptiert nur **eine** Verbindung gleichzeitig; `coordinator.py:3-6` öffnet daher
  pro Poll eine neue Verbindung und schließt sie danach. Jeder Poll beginnt mit einer frischen
  `H:`-Zeile mit Duty Cycle und freien Speicherplätzen (`coordinator.py:4-6`).
- `commander.py:31-40` sendet `q:` und schließt anschließend immer (`finally`).
- `commander.py:47-60`: `update()` fragt `l:` ab, reconnectet bei Fehler und trennt bei
  `use_persistent_connection = False` nach jedem Aufruf.
- `cube.py` (HA):1-7 hält die zusätzlichen `H:`-Felder fest, die die Bibliothek verwirft
  (nur Serial, RF-Adresse, Firmware werden dort gelesen). `_DUTY_CYCLE_FIELD = 5`
  (`cube.py` (HA):25).
- `cube.py` (Lib):129-130 schluckt Parsefehler – C# sollte stattdessen explizit protokollieren.
- `cube.py` (Lib):165-223 erzeugt Geräte nur aus der `M:`-Zeile. LIVE-BEWÄHRT: funktechnisch
  bekannte Geräte ohne M-Eintrag erscheinen nicht als Entitäten (`ha-maxcube.md:67`).
- `cube.py` (Lib):241-259 verwirft die Ist-Temperatur bei Boost/Uralaub, obwohl sie im Frame
  steht (`max-cube-protokoll.md:34`). `max-cube-protokoll.md:63` bleibt die offene Messung dazu.

### 7.2 Funkbudget (Duty Cycle)

- `const.py:20-24`: `MAX_DUTY_CYCLE_FOR_WRITING = 90` Prozent, `SCHEDULE_VERIFY_DELAY = 300`
  Sekunden.
- `schedule.py:180-182` rechnet ca. 2,8 % Funkaufwand pro Telegramm.
- `schedule.py:236-246` lehnt einen Schreibvorgang ab, wenn der prognostizierte Duty ≥ 90 %
  wäre.
- `commander.py:62-70` sendet `s:` mit Base64 der Hex-Nutzlast und wiederholt bis zum Timeout.
- `commander.py:72-86` wertet die `S:`-Antwort `duty,status,free` aus: `status_code == "0"`
  bedeutet Erfolg (`:76-77`); bei `duty == 100` und `free == 0` wird gewartet (`:82-83`).
- `health.py:20-25`: `DUTY_HIGH = 90`, `DUTY_LOW = 80`, `DUTY_SUSTAIN = 600` s (Hysterese),
  `UNREACHABLE_AFTER = 6 * 3600` s, `BATTERY_HOLD = 15 * 60` s (`health.py:14-17`).
- LIVE-BEWÄHRT: 99 % Funkauslastung führten am 24.11. zum Hinweis `radio_busy_cube`
  (`ha-maxcube.md:39`); am 22.11. wurden 6 % und 49 freie Plätze gemessen (`ha-maxcube.md:62`).

### 7.3 Discovery und Reboot (UDP 23272)

- `discovery.py:17-24`: Port 23272, Request `eQ3Max*\0**********I`, Antwort-Präfix
  `eQ3MaxAp`, Seriennummer `slice(8,18)`, Format `[A-Z0-9]{10}`.
- `button.py:36-42`: gleicher Port, Präfix `eQ3Max*\0`, Opcode `R`, Ziel
  `255.255.255.255:23272` (`button.py:8`).
- LIVE-BEWÄHRT: `R` bedeutet **Reboot**, nicht Werksreset; der Werksreset ist das TCP-Kommando
  `a:` (`max-cube-protokoll.md:28`). Der Button wurde am 22.11. nicht gedrückt
  (`max-cube-protokoll.md:28`).

### 7.4 Anzeige am Wandthermostat (0x82)

- `display.py:4-10` dokumentiert die Herkunft (Bouni S-Message.md, Command 82; offizielle
  Software 1.4.6) und die wichtige Einschränkung: Der aktuelle Zustand **kann nicht**
  zurückgelesen werden; deshalb zwei Buttons statt eines Switches.
- `display.py:16-19`: `FRAME_TYPE_DISPLAY = "82"`, `04` = Isttemperatur, `00` = Solltemperatur.
- `display.py:22-30`: exaktes Hex-Layout (siehe 6.1).
- `button.py:63-76`: Buttons nur für Wandthermostate (`show_room_temperature=True`,
  `show_setpoint=False`), nicht für Heizkörperthermostate.
- `button.py:130-150` → `coordinator.async_set_display(rf, show_actual)`, mit vorheriger
  Funkbudgetprüfung.
- Java-Gegenstück: `SetDeviceDisplaySetPointActual.java:12-19` und
  `WallMountedThermostatDisplayConfigCommand.java:13-23` ( Gerätetyp Wandthermostat,
 RF-Adresse, `displayActual`).
- LIVE-BEWÄHRT: beide Knöpfe am Gerät bestätigt (`ha-maxcube.md:27`,
  `max-cube-protokoll.md:25`).

### 7.5 Snapshots, Health, Diagnostics (Architekturregeln)

- `snapshot.py:1-7`: unveränderlicher Snapshot am Ende jedes Polls, weil die Bibliotheksobjekte
  im Executor-Thread verändert werden. Das ist eine C#-relevante Regel: UI nie direkt auf
  veränderliche Treiberobjekte zeigen lassen.
- `snapshot.py:26-28`: Wochenprogramm als Tupel `(until "HH:MM", temperature)` pro Tag.
- `coordinator.py:1-7`: sämtliche synchronen Bibliotheksaufrufe in den Executor; ein
  dedizierter Worker, der die einzige Cube-Verbindung serialisiert.
- `health.py:9-12`: Issue-Typen `battery_low`, `device_error`, `cube_unreachable`, `radio_busy`.
- `diagnostics.py:14-16`: Redaktion von Host, Seriennummer, Namen, Räumen, Title, unique_id.
  C# sollte gleich-sensitive Daten ebenfalls redaktieren.
- `config_flow.py:10-28`: Host/Port aus Konfiguration, DHCP- und UDP-Discovery, Seriennummer
  als stabile Identität (`identity.py` via `config_flow.py:28`).

## 8. Radio-/Protokoll-Referenz kompakt

| Ebene | Regel | Beleg |
| --- | --- | --- |
| Transport | TCP 62910, zeilenorientiert | `const.py:7`, `cube.py` (Lib):32 |
| Begrüßung | `H:` mit Serie, RF, Firmware, Duty, Slots, Datum | `cube.py` (HA):1-7, `:25` |
| Zustand | `M:` Räume/Geräte, `C:` Konfiguration, `L:` Status | `fake_cube.py:3-6`, `cube.py` (Lib):165-223 |
| Schreiben | `s:` + Base64(Hex), Antwort `S:` = `duty,status,free` | `commander.py:62-86` |
| Beenden | `q:` dann Close | `commander.py:31-40` |
| Frame | FrameType Byte 2, Empfänger Byte 6–8, LSB-first | `maxcube-protocol.md:339-354` |
| Woche | 13 Slots/Tag, Python 7 je Telegramm; 7 + 6 nur Java, am 24.11. live versandt | `schedule.py:28,89-91` (7er-Grenze), `SetRoomWeekTemperatureProfile.java:81-84` |
| Temperatur | Halbgrade, 4,5/30,5 Grenzen | `schedule.py:62-63,92-93`, `cube.py` (Lib):307 |
| Modus | 0 Auto, 1 Manual, 2 Vacation, 3 Boost | `device.py:10-13` |
| Display | 0x82, Byte 10 = 04/00, nur Wandthermostat | `display.py:16-30`, `FrameType.java:31` |
| UDP | 23272, `I` discover, `R` reboot | `discovery.py:17-24`, `button.py:36-41` |

## 9. Offene Punkte / UNBEKANNT

1. `UNBEKANNT`: Herkunft des konkreten Web-UI-Ports 63121 im Code; nur live beobachtet
   (`max-local-application.md:16`). Mechanismus dynamisch belegt (`PortFactory.java:23-29`).
2. `UNBEKANNT`: Warum der Cube zeitweise den TCP-Listener 62910 nicht mehr annimmt
   (`ha-maxcube.md:76`, `max-cube-protokoll.md:21`).
3. ~~`UNBEKANNT`: Direkter Byte-für-Byte-Abgleich zwischen Python-Wortkodierung
   (`schedule.py:108-110`) und Java-Bitfeld-Kodierung
   (`SetRoomWeekTemperatureProfile.java:98`)~~ **Erledigt 24.11.2026 (CODE-BELEGT):** Die
   Kodierung ist byte-identisch; Ableitung und Sonnet-Gegenprüfung in
   `byte-abgleich-python-java.md`. **LIVE-BEWÄHRT 24.11.2026:** Kodierung und Versand eines echten
   `0x10`-Telegramms der Python-Integration wurden über das DEBUG-Log der Bibliothek erfasst
   (25 Byte, 7 Wörter, Byte 10 Bit 4 = 0; Nachweis im SecondBrain-Wiki unter
   `wiki/anleitungen/maxcube-0x10-mitschnitt.md`). Die Annahme durch den Cube ist nur indirekt
   belegt (`sent_not_confirmed`, keine `A:`-Quittung). Ein Paketmitschnitt bleibt offen; der
   Versand eines 13-Punkte-Programms durch die Original-App ist am 24.11.2026 um 10:32 +01:00
   live belegt (zwei Telegramme, Byte 10 `0x01` mit 7 Wörtern, dann `0x11` mit 6 Wörtern ab
   Offset 7). Kodierung und Versand der Java-Regel sind damit `LIVE-BEWÄHRT`, die fachliche
   Annahme durch den Cube weiterhin nur indirekt.
4. `UNBEKANNT`: Ist-Temperatur im Boost-Modus wurde nie am echten Gerät gemessen
   (`max-cube-protokoll.md:63`, `cube.py` (Lib):241-259).
5. `UNBEKANNT`: Ob die Java-Kategorie `FrameTypeCategory` exakt auf die Python-RadioState-
   Abbildung passt; `maxcube-protocol.md:390` nennt die Regel, ein Python-Gegensück fehlt.
6. `UNBEKANNT`: Verhalten bei mehr als 13 Schaltpunkten je Tag; Java begrenzt auf 13
   (`DayTemperatureProfile.java:17-18`), ein Testfall darüber hinaus liegt nicht vor.
7. `UNBEKANNT`: Ob der Cube die Telegramm-1-Flagge (`SetRoomWeekTemperatureProfile.java:90`)
   fachlich korrekt übernimmt; die Python-Seite sendet nur 7 Wörter und kein Flag
   (`schedule.py:112-118`). **INFERENZ:** Bei ≤7 Punkten ist das zweite Telegramm nicht nötig,
   daher ist das Auffüllen in `schedule.py` mit der Java-Regel vereinbar. Der Live-Telegramm-
   Mitschnitt vom 24.11.2026 zeigt genau dieses eine Telegramm ohne Flag und bestätigt damit die
   Python-Seite. Der Live-Versand mit Flag 1 durch die Original-App ist `LIVE-BEWÄHRT`
   (24.11.2026, 10:32 +01:00; Nachweis im SecondBrain-Wiki unter
   `wiki/anleitungen/maxcube-0x10-mitschnitt.md`); ob der Cube ihn fachlich korrekt übernimmt,
   bleibt `UNBEKANNT`.

## 10. Übernahmeliste für die C#-Implementierung

### 10.1 Fachregeln aus der Python-Logik (LIVE-BEWÄHRT + CODE-BELEGT)

1. **Verbindung** pro Poll neu öffnen, `H:` auslesen, `q:` senden, schließen; nie zwei
   Clients gleichzeitig (`coordinator.py:3-6`, `commander.py:31-40`).
2. **Port** 62910 als Default; 54321 und 63121 nicht verwenden (`const.py:7`,
   `cube.py` (Lib):32, `LocalConfiguration.java:12`, `max-local-application.md:16`).
3. **Modus+Temperatur immer zusammen** senden; `AUS = MANUAL + 4,5 °C`, `HEAT = MANUAL +
   letzter echter Sollwert`, `AUTO = Modus 0` (`climate.py:315-322,358-367,389-401`,
   `ha-maxcube.md:34-39`).
4. **Halbgrade** intern als `int(temperatur * 2)`; obere 2 Bit Modus, untere 6 Bit Temperatur
   (`cube.py` (Lib):307, `schedule.py:62-63`).
5. **Grenzwerte** 4,5/30,5 °C im Protokoll, 5,0/30,0 °C in der UI; 4,5 und 30,5 nie als
   Heizsollwert merken (`climate.py:71-74,177,403`).
6. **Wochenprogramm** 13 Slots je Tag, Start Samstag, 5-Minuten-Raster, letzter Slot 24:00,
   Temperatur 4,5–30,5 °C (`schedule.py:22,27-28,62-63,153-155`).
7. **Schreiben** maximal 7 Punkte je Telegramm, kurze Tage mit letztem Slot auffüllen,
   Tag als Index ab Samstag (`schedule.py:89-91,107-118`).
8. **Funkbudget** vor jedem Write prüfen (90 % Grenze), ~2,8 % pro Telegramm, `S:`-Status `"0"`
   als Erfolg, Wartezeit bei 100 %/0 Slots (`const.py:21,24`, `schedule.py:180-182,236-246`,
   `commander.py:72-86`).
9. **Schreibverifikation** nach 300 s (`const.py:24`).
10. **Status** `Error` (bits1 Bit 3) als Hauptindikator, `Link_Error` ignorieren, zusätzlich
    Stale nach 6 h (`linkstatus.py:9-29,65-74`, `health.py:20`).
11. **Batterie** 15 Minuten halten, Funk-Hinweis mit 90/80 % Hysterese über 600 s
    (`health.py:14-25`).
12. **Discovery/Reboot** UDP 23272, `I` bzw. `R`, Seriennummer `[A-Z0-9]{10}`
    (`discovery.py:17-24`, `button.py:36-41`).
13. **Display** 0x82 nur für Wandthermostate, `04` Ist / `00` Soll, Zustand nicht rücklesbar
    (`display.py:4-10,16-30`, `button.py:63-76`).
14. **Snapshot** pro Poll unveränderlich für die UI; Treiberzugriff serialisieren
    (`snapshot.py:1-7`, `coordinator.py:1-7`).
15. **Diagnose** Seriennummer/Host/Namen redaktieren (`diagnostics.py:14-16`).

### 10.2 Fachregeln aus der Java-REA (CODE-BELEGT)

16. FrameType-Codes und -Kategorien aus `FrameType.java:9-34` übernehmen (Tabelle in 6.2).
17. Bitreihenfolge LSB-first im Byte, Big-Endian zwischen Bytes
    (`maxcube-protocol.md:351-354,658`, `RadiogramSpecification.java:193-203`).
18. Wochenprofil: FrameType 0x10, Byte 10 Bit 0..2 (3 Bit) Wochentag, Bit 4 Telegramm-1-Flag,
    Punkte ab Byte 11/12; >7 Punkte → zweites Telegramm mit 6 Punkten
    (`SetRoomWeekTemperatureProfile.java:90-100,81-84`; 24.11.2026 10:32 +01:00 live versandt).
19. Nur geänderte Tage senden (optional); 7 Tagesprofile und maximal 13 Punkte je Tag
    validieren (`SetRoomWeekTemperatureProfile.java:103-115`,
    `WeekTemperatureProfile.java:14`, `DayTemperatureProfile.java:17-18`).
20. Samstag = Index 0, Sonntag = 1; Montag = 1 … Sonntag = 7 im Java-Enum
    (`RadiogramSpecification.java:281-285`, `DayOfWeek.java:7-13`, `schedule.py:22`).
21. Web-UI nicht nachbauen (kein Jetty/HTTP), sondern native UI; Hersteller nutzt dynamischen
    Port + Browser (`Application.java:190-220`, `target-architecture.md:13`).

### 10.3 Offene Punkte für die C#-Arbeit

22. ~~Byte-für-Byte-Abgleich Python ↔ Java Wochenprofil nachholen (siehe 9.3).~~
    **Erledigt 24.11.2026:** CODE-BELEGT, siehe `byte-abgleich-python-java.md`.
23. Boost-Ist-Temperatur am echten Cube messen, bevor `cube.py` (Lib):241-259 ungeprüft
    übernommen wird.
24. Port-Blockade 62910 beobachten und im C#-Client mit Timeout/Reconnect robuster machen
    (`ha-maxcube.md:76`, `coordinator.py:47-60`).
25. Keinen Wert aus `fake_cube.py` als Hardwarefakt übernehmen (`fake_cube.py:8-10`).
26. Die vier Konflikte sind damit für die Implementierung entschieden; verbleibende
    `UNBEKANNT`-Punkte dürfen erst nach neuer Evidenz in Fakten umgewandelt werden.
