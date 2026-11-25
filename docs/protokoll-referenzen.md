# Internetreferenzen: eQ-3 MAX! Cube LAN Gateway – Protokoll

> Zweck: Fundstellen und Erkenntnisse zur schnellen, sicheren Fertigstellung der
> C#/.NET-10/Avalonia-Neuimplementierung in `app-maxcube`.
>
> Erstellt: 2026-11-24. Alle Aussagen sind mit `BELEGT` (Quelle direkt gelesen)
> oder `UNBEKANNT` (nicht verifiziert) markiert. Links wurden ausschließlich
> aus tatsächlich gefundenen bzw. gelesenen Quellen übernommen.

---

## 1. Kurzfassung / Entscheidungshilfe

| Frage | Antwort |
|---|---|
| Gibt es eine offizielle, autorisierte Protokollspezifikation von eQ-3? | `BELEGT`: Nein gefunden. Nur Datenblatt und Bedienungsanleitung. |
| Gibt es eine C#-Bibliothek? | `BELEGT`: Ja, aber veraltet: `bietiekay/MAXSharp` (2010–2012, AGPL v3, zuletzt 2015). Kein aktuelles NuGet-Paket. |
| Beste, aktuellste Referenz? | `BELEGT`: Bouni `max-cube-protocol` (MIT, kontinuierlich gepflegt) + Python `maxcube-api` 0.4.3 (2021) als Referenzimplementierung. |
| Empfohlene Strategie | Eigenes C#-Protokollmodul, Bouni als Spec, Python-Port als Verhaltensoracle, Java-REA als Fallback/Quervergleich. Keine C#-Fremdbibliothek als Fundament. |

---

## 2. Quellenmatrix

| Nr. | Quelle (URL) | Lizenz | letzter Aktivitätsstand | Verlässlichkeit |
|---|---|---|---|---|
| Q1 | https://github.com/Bouni/max-cube-protocol | MIT (`BELEGT`, Lizenzdatei im Repo; zuvor auch als MIT identifiziert) | 137 Commits, erstellt 2013-12-10; laut GitHub-Suche zuletzt 2025-02-13 aktualisiert (`BELEGT`); ~120 Sterne, ~35 Forks (`BELEGT`) | Reverse-Engineering, sehr detailliert, meist gut belegt |
| Q2 | https://www.eq-3.com/downloads/download/max/pdb/en/MAX_Cube-LAN-Gateway_99004_datasheet_V3.0.pdf | Hersteller-PDF (Nutzungsrechte nicht im Dokument geprüft → `UNBEKANNT`) | Version V3.0 (`BELEGT`) | offiziell, aber nur Produktblatt |
| Q3 | https://www.eq-3.com/Downloads/eq3/downloads_produktkatalog/max/bda/en/BC-LGW-O-TW_UM_EN.pdf | Hersteller-PDF (`UNBEKANNT`) | veröffentlicht 2017-05-17 (`BELEGT`) | offiziell, Bedienung, kein Protokoll |
| Q4 | https://pypi.org/project/maxcube-api/ | PyPI-Metadaten unscharf: Libraries.io nennt `OpenSSL`, GitHub-Meta MIT (`BELEGT`); genaue LICENSE-Datei nicht gelesen → `UNBEKANNT` | Version 0.4.3, 2021-05-02 (`BELEGT`) | Community-Referenzimplementierung, sehr brauchbar |
| Q5 | https://github.com/uebelack/python-maxcube-api | MIT laut GitHub-Metadaten (`BELEGT`) | Repo-Meta gelesen; Release 0.4.3 (2021-05-02) (`BELEGT`) | Community, Code gelesen |
| Q6 | https://github.com/bietiekay/MAXSharp | GNU AGPL v3, `license.txt` gelesen (`BELEGT`) | Urheberrecht 2010–2012; GitHub zuletzt 2015-06-24 (`BELEGT`); ~11 Sterne (`BELEGT`) | Community, C#, stark veraltet |
| Q7 | https://github.com/bietiekay/hacs/tree/master/tools/MAXDebug | `UNBEKANNT` (nicht geprüft) | `UNBEKANNT`; nur in Q1 gelistet | Community-Hinweis, nicht gelesen |
| Q8 | https://github.com/ababilone/maxmanager | `UNBEKANNT` (nicht geprüft) | Repo-Meta gelesen, Datum `UNBEKANNT` (Repository 2016, von Q1 gelistet) | Community-Hinweis, CS/UWP, nicht im Detail gelesen |
| Q9 | https://www.home-assistant.io/integrations/maxcube | Apache-2.0-typisch für HA, hier nicht geprüft → `UNBEKANNT` | Legacy/Community-maintained (`BELEGT`), Stand der Doku 2026 gelesen | Community, gut dokumentiert |
| Q10 | https://github.com/home-assistant/core/blob/dev/homeassistant/components/maxcube/climate.py | Apache-2.0 (HA-Core, `UNBEKANNT` im Einzelnen geprüft) | `dev`-Zweig, 2026 gelesen (`BELEGT`) | Community-Referenzimplementierung |
| Q11 | http://www.openhab.org/addons/bindings/max | `UNBEKANNT` | Addon-Doku gelesen, Datum `UNBEKANNT` | Community/Projekt, gelesen |
| Q12 | https://github.com/openhab/openhab1-addons/wiki/MAX!Cube-Binding | `UNBEKANNT` | Wiki gelesen, Datum `UNBEKANNT` | Community, gelesen |
| Q13 | https://insinuator.net/2016/04/discover-the-unknown-analyzing-an-iot-device/ | Blog, `UNBEKANNT` | 2016-04 (`BELEGT`) | Security-RE, sehr wertvoll, aber teils spekulativ |
| Q14 | https://github.com/ernw/insinuator-snippets/tree/master/maxcube/firmware/parser | `UNBEKANNT` | Repo-Inhalt gelesen, Datum `UNBEKANNT` | RE-Artefakte, gelesen |
| Q15 | http://www.eq-3.de/produkte/max.html | Herstellerseite (`UNBEKANNT`) | Cloud-Abschaltung 2023-07-01 genannt (`BELEGT`) | offiziell |
| Q16 | https://azuresearch-usnc.nuget.org/query?q=maxcube&prerelease=true | nuget.org API, öffentlich | Abfrage 2026-11-24 (`BELEGT`) | offizielles Package-Index-Result |

### Explizit leere Suchen

- `site:nuget.org "maxcube" OR "eq3" MAX Cube library C#` → `BELEGT`: kein
  MAX-Cube-spezifisches Paket gefunden; Treffer waren nur `Cube.Pdf` (völlig anderes
  Produkt). Die direkte NuGet-API-Abfrage `q=maxcube` ergab `totalHits: 0`
  (`BELEGT`, Q16). **Es gibt kein passendes NuGet-Paket.**
- Suche nach einem offiziellen „MAX! Cube LAN Gateway Protocol"-PDF →
  `BELEGT`: nur Datenblatt und Bedienungsanleitung gefunden, kein Protokolldokument.
- Suche nach vollständigem UDP-Firmware-Frame-Format → `BELEGT`: nicht gefunden.
  Siehe Abschnitt 7.

---

## 3. Offizielle eQ-3-Unterlagen (Q2, Q3, Q15)

`BELEGT` aus Q2 (Datenblatt V3.0):

- 868,3 MHz Bidirektional-Funk (MAX!-Protokoll).
- Bis zu 50 Geräte und 10 Räume.
- Duty-Cycle < 1 % pro Stunde.
- AES-128-Verschlüsselung wird erwähnt – **ohne** Angabe von Schlüssel, IV, Modus
  oder Nachrichtenformat. Die Erwähnung bezieht sich auf das Produkt, nicht auf
  eine dokumentierte API.
- Ethernet-Schnittstelle, IP-Konfiguration, NTP, Portal-Anbindung.

`BELEGT` aus Q3 (Bedienungsanleitung): nur Montage, Inbetriebnahme, LEDs,
Werkseinstellungen, Anlernen. Kein Port, keine Kommandoliste, keine Bytefelder.

`BELEGT` aus Q15: MAX!-Cloud wurde zum 01.07.2023 abgeschaltet; lokaler Betrieb
und Programmierung über PC bleiben dauerhaft möglich. **Konsequenz:** Alle
Portalfunktionen (`u:`/Portal-URL, `e:`/`d:`-Cloud-Krypto) sind für die
Neuimplementierung ohne praktischen Wert; der lokale TCP-Pfad ist der einzig
relevante.

---

## 4. Bouni `max-cube-protocol` (Q1) – die wichtigste Spezifikation

### 4.1 Projektstatus

`BELEGT`: MIT-Lizenz, 137 Commits, erstellt 2013-12-10, ~120 Sterne, ~35 Forks,
zuletzt 2025-02-13 aktualisiert. Die `protocol.md` ist ausdrücklich für Firmware
1.1.3 geschrieben (`BELEGT`). Einzige mir bekannte durchgehend gepflegte,
freie Protokolldokumentation. Einzige Einschränkung: viele Felder heißen
„unknown"; dort darf nicht erfunden werden.

### 4.2 Transport und Discovery (`protocol.md`, `Cube_Discovery.md`)

`BELEGT`:

- TCP-Datenport **62910**. Ein Cube akzeptiert nur **eine** TCP-Verbindung
  gleichzeitig (auch in Q13 bestätigt).
- UDP-Port **23272** für Discovery.
- Discovery-Request: 19 Bytes `eQ3Max*\0**********I`.
- Discovery-Response: 26 Bytes `eQ3MaxAp` + 10 Byte Seriennummer + `>` + `I` +
  3 Byte RF-Adresse + 2 Byte Firmware.
- Request-Typen: `I` (Info), `N`, `h`, `c`, `R` (Reboot). Antworten kommen auf
  Port 23272 zurück.
- Reboot via UDP ist **unauthentifiziert** (Q13).

### 4.3 Nachrichtentypen (vollständig gelesen)

`BELEGT`, eingehend: `H: F: L: C: M: N: A: E: D: b: g: j: p: o: v: w: S:`
`BELEGT`, ausgehend: `u: i: s: m: n: x: g: q: e: d: B: G: J: P: O: V: W: a: r: t: w: l: c: v: f: z:`

> **Einordnung 24.11.2026:** Diese Liste stammt aus Q1 (Bouni) und mischt zwei Protokolle. `B:/G:/J:/P:/O:/V:/W:` samt `b:/g:/j:/p:/o:/v:/w:` gehören laut Hersteller-Bytecode (`decompiled/*/remoteproxy/`, `MaxRemoteProxy.java:67-175`) zum **HTTP-Remote-Proxy** der abgeschalteten Cloud, nicht zum Cube-TCP. `g:` ist doppelt belegt (dort `G:`-Antwort, hier `OUTGOING_MORE_DATA` als tote Konstante). Für die C#-Integration bewusst ausgeschlossen; Nachweis in `SecondBrain/wiki/fragen/2026-11-24-maxcube-remote-proxy-opcodes.md` und `docs/maxcube-protocol.md` Abschnitt 6.1.

| Kommando | Bedeutung | Beleg |
|---|---|---|
| `l:` / `L:` | Geräteliste anfordern/antworten | Q1, Q5 |
| `m:` / `M:` | Metadaten (Räume/Geräte) lesen/schreiben | Q1, Q5 |
| `s:` / `S:` | Funkbefehl senden / Bestätigung | Q1, Q5 |
| `c:` / `C:` | Konfiguration eines Geräts | Q1, Q5 |
| `n:` / `N:` | Anlernen (Pairing), mit/ohne Timeout | Q1 |
| `t:` | Gerät löschen | Q1 |
| `z:` | Wake-up an Geräte/Gruppe | Q1 |
| `f:` / `F:` | NTP-Server setzen/lesen | Q1 |
| `v:` / `A:` | Datum/Uhrzeit setzen / ACK | Q1 |
| `a:` | Factory Reset (Geräte verlieren Kopplung) | Q1 |
| `q:` | Verbindung beenden | Q1, Q5 |
| `u:` | Portal-URL setzen | Q1 |
| `e:` / `d:` | Verschlüsseln/Entschlüsseln | Q1 |
| `x:` | Pairing beenden | Q1 |

### 4.4 L-Message (Gerätezustände)

`BELEGT` (Bouni `L-Message.md`, Q5 `cube.py`): Base64-kodiert. Pro Submessage:
Länge (1 Byte), 3 Byte RF-Adresse, 1 unbekanntes Byte, 2 Byte Flags,
Ventilposition, Solltemperatur (7 Bit, `& 0x7F`, /2), Datum/Uhrzeit bis, optional
Isttemperatur (2 Byte, /10). Abschluss `ce 00`.

Aus Q5 `cube.py` `parse_l_message` `BELEGT`:

- Mode = `bits2 & 3` (0 Auto, 1 Boost?, 2 Manual, 3 Vacation – Moduswerte
  `BELEGT` aus Q10/Q5 `device.py`).
- Battery = `bits2 >> 7`.
- Fensterkontakt: `status = data[pos+6] & 3`, `> 0` ⇒ offen.
- Wandthermostat Isttemperatur: `(((data[pos+8] & 0x80) << 1) + data[pos+12]) / 10.0`.
- Thermostat Isttemperatur nur in Auto/Manual; in Boost/Vacation → `None`.
- Pointer-Vorschub: `pos += length + 1`.

⚠️ `BELEGT`: Q5 nutzt `pos += length + 1`, während Bouni die Länge als erstes Byte
beschreibt. Für die C#-Implementierung unbedingt mit echtem Frame gegenprüfen.

### 4.5 C-Message (Konfiguration)

`BELEGT` (Q1 `C-Message.md`, Q5 `cube.py`): 18 Byte Header (Länge, RF-Adresse,
Gerätetyp, Raum-ID, Firmware, Test-Flag, Seriennummer), danach
gerätespezifische Daten. Offset 18–21: Comfort/Eco/Max/Min-Temperatur, jeweils
`/2.0`. Ab Offset 29: Wochenprogramm.

`BELEGT`: 182 Byte = 7 × 26 Byte, Start **Samstag** (Q5 `DAYS`). Pro Tag 13
Worte à 2 Byte; Bitlayout: 7 Bit Temperatur (×2) + 9 Bit Uhrzeit in
5-Minuten-Schritten. `24:00` markiert das Ende gültiger Schaltpunkte.

### 4.6 M-Message (Metadaten)

`BELEGT` (Q1 `M-Message.md` und Q5 `cube.py`):

- Format: `M:<index>,<count>,<base64>`. Blöcke à ~1900 Byte; `m:00,...`,
  `m:01,...` schreiben. **`UNBEKANNT`**: ob die `m:`-Schreibfunktion in der
  deutschen/firmware-1.1.3-Variante identisch ist (Q1: „Input is not validated").
- Dekodiert: 2 unbekannte Bytes `56 02`, dann Anzahl Räume; pro Raum:
  Raum-ID, Namenslänge, Name (UTF-8), 3 Byte Gruppen-RF-Adresse.
- Danach Anzahl Geräte; pro Gerät: Typ (0 Cube, 1 Heizkörperthermostat,
  2 Thermostat Plus, 3 Wandthermostat, 4 Fensterkontakt, 5 Schaltaktor),
  3 Byte RF-Adresse, 10 Byte Seriennummer, Namenslänge, Name, Raum-ID.

### 4.7 H-, V-, U-, E/D-, A-, N-, F-, T-, Z-, Q-Message

`BELEGT` (Q1):

- `H:KEQ0523864,097f2c,0113,00000000,477719c0,00,32,0d0c09,1404,03,0000\r\n`
  = Serie, RF-Adresse, Firmware, unbekannt, HTTP-Connection-ID, Duty-Cycle,
  freie Slots, Datum `YYMMDD`, Zeit `HHMM`, State, NTP-Counter.
- `v:<Base64-Zeitzonen>,<UTC-Sekunden seit 2000-01-01 als HEX>\r\n`; Cube antwortet `A:`.
- `u:http://...:80` setzt die Portal-URL (obsolet seit Cloud-Abschaltung).
- `e:`/`E:` verschlüsseln, `d:`/`D:` entschlüsseln; Antworten sind mit Nullbytes
  gepaddet. **Algorithmus und Zweck in Q1 als `UNBEKANNT` beschrieben.**
- `a:` Factory Reset; `A:` bestätigt auch `t:`, `z:`, `v:`.
- `n:` ohne Argument = Pairing ohne Timeout; `n:003c` = 60 s (HEX). Pairing mit
  `x:` beenden. Wichtig (`BELEGT`): Antwort erst nach Timeout/`x:` lesen, sonst
  Durcheinanderbringen der Antworten auf die Kommandos.
- `N:` = Gerätetyp, 3 Byte RF-Adresse, 10 Byte Serie, 1 Byte `ff`.
- `F:ntp.homematic.com,ntp.homematic.com` – Default-NTP `BELEGT`.
- `t:01,1,Dx1U` – Anzahl, Force-Flag, Base64-Liste von Raum-RF-Adressen.
- `z:1e,G,01` – Wakeup-Zeit (HEX, 1e = 30 s?), Scope `G`/`A`/`D`, Ziel.
  ⚠️ Q1 schreibt „1e=30sec", 0x1e = 30 dezimal. Für `z:`-Zeit bitte am Gerät
  verifizieren; `UNBEKANNT`, ob Minuten oder Sekunden gemeint sind.
- `q:` = `q:\r\n` Verbindung beenden.

### 4.8 S-Message / Funkbefehle

`BELEGT` (Q1 `S-Message.md`, Q5 `cube.py`):

- Aufbau: `00` (unknown), `04` (RF-Flag = Gruppe/Raum; `00` = Gerät), `40`
  (Temperatur setzen), 3 Byte `from` (`000000`), 3 Byte Ziel-RF-Adresse, Raum-ID,
  dann Nutzdaten.
- Befehle: `40` Temperatur, `10` Programm, `11` Eco-Temperatur, `12`
  Ventilfunktionen, `20` Link hinzufügen, `21` Link entfernen, `22` Gruppenadresse
  setzen, `23` Gruppenadresse entfernen, `82` Anzeige der Isttemperatur.
- Q1-Hinweis: vor Befehlen ggf. Wake-up (`z:`) senden.
- Antwort `S:` enthält Duty-Cycle, Statuscode, freie Slots. Statuscode `0` =
  Erfolg (Q5 `commander.py`).

---

## 5. Python `maxcube-api` 0.4.3 (Q4, Q5) – Verhaltensoracle

`BELEGT` gelesen: `cube.py`, `commander.py`, `connection.py`, `thermostat.py`
(früher), `D-Message`-bezogene Metadaten.

### 5.1 Verbindung / Timing (`commander.py`, `connection.py`)

`BELEGT`:

- Default-Port 62910.
- Zeitlimits: `update=3.0 s`, `connect=3.0 s`, `flush-input=0 s`,
  `send-radio-msg=30.0 s`, `cmd-reply=2.0 s`.
- `use_persistent_connection = True` default; bei `False` wird nach jedem
  Kommando getrennt.
- Nachrichten werden bis `\r\n` gepuffert (4096-Byte-Blöcke); ein 0-Byte-Read
  bedeutet Verbindungsende.
- Bei `s:`-Befehlen wird die Antwort auf Statuscode `0` geprüft; bei
  Duty-Cycle `100` und freien Slots `0` wird bis zu 10 s gewartet und erneut
  versucht (Retry-Schleife bis 30 s).
- Fehler ⇒ Socket schließen, einmalig neu verbinden, Kommando wiederholen.
- Beim Verbinden wird eine unaufgeforderte `L:`-Nachricht als „unsolicited"
  gespeichert; `update()` holt `L:` und liefert alle gepufferten Nachrichten.

**Wichtig für C#:** diese Timeouts/Retry-Logik ist die praktisch erprobte
Referenz. Ein naiver `TcpClient` ohne `\r\n`-Framing und ohne
Duty-Cycle-Retry wird im Betrieb hängen bleiben.

### 5.2 `set_temperature_mode` (`cube.py`)

`BELEGT`, exakt:

```python
target_temperature = int(temperature * 2) + (mode << 6)
byte_cmd = "000440000000" + rf_address + room + to_hex(target_temperature)
# → s:<base64(byte_cmd)>
```

- Modus `0` = Auto ⇒ Temperatur `0` setzen; danach Solltemperatur aus
  Wochenprogramm ableiten.
- Auto/Manual/Boost/Vacation = 0/1/2/3 (`BELEGT` aus `device.py`/Q10).
- Nach Erfolg werden lokale `mode`/`target_temperature` aktualisiert.

### 5.3 `set_programme` (`cube.py`)

`BELEGT`:

- `CMD_SET_PROG = "10"`, `UNKNOWN = "00"`, `RF_FLAG_IS_ROOM = "04"`,
  `RF_FLAG_IS_DEVICE = "00"`, `RF_NULL_ADDRESS = "000000"`.
- Argumente auf 7 `(temp, until)`-Tupel auffüllen mit `(0, "00:00")`.
- Befehl = `00` + RF-Flag + `10` + `000000`, dann pro Gerät RF-Adresse +
  Raum-ID + Wochentag (Samstag = 0, `DAYS`-Liste), dann 7× `temp_and_time`.
- `temp_and_time`: `assert temp <= 32`, `temp % 0.5 == 0`, Minuten % 5 == 0;
  7 Bit Temperatur×2 + 9 Bit `minuten/5`, als 2 Byte big-endian.
- `set_programmes_from_config()` verarbeitet JSON-Config pro Gerät/Tag.

### 5.4 `send_metadata`

`BELEGT`: In der gelesenen 0.4.3-Codebasis von `cube.py`/`commander.py` gibt es
**keine** Funktion namens `send_metadata`. Es gibt nur `parse_m_message()` und
den `m:`-Befehl aus Q1. Die im Arbeitstand notierte offene Frage ist damit
geklärt: **kein API-Aufruf dieses Namens**; bei Bedarf `s:`/`m:` direkt bauen.

---

## 6. C#-Referenzen

### 6.1 `bietiekay/MAXSharp` (Q6) – `BELEGT`

- README: „MAXSharp (ELV MAX! Cube C# control library)", (C) 2010–2012 Daniel
  Kirstenpfad.
- `license.txt`: **GNU AGPL v3** (mit Teilen von sones GmbH 2008–2010).
  Für eine geschlossene App ist AGPL problematisch; als Inspiration nutzbar,
  nicht als Copy-Paste.
- Dateien `BELEGT`: `AssemblyInfo.cs`, `DiffSet/`, `ELVMAX_Data/`,
  `MAXEncodeDecode.cs`, `MAXMonitoringThread.cs`, `MAXSharp.csproj`.
- `MAXMonitoringThread.cs` `BELEGT`: `TcpClient`/`NetworkStream`,
  `UpdateTime=10000`, `ELVMAXSensorReStoringSec=360`,
  `ELVMAXReconnectTimeMsec=60000`, `ConcurrentQueue<IDeviceDiffSet>` für
  Events, `keepRunning`-Flag. Damit existiert ein C#-Gerüst für Polling,
  Reconnect und Diff-Events – aber kein .NET 10, kein async/await, keine
  Tests, 2015 eingefroren.

### 6.2 Weitere C#-Projekte (Q7, Q8)

- `BELEGT` (nur aus Q1-README gelistet): `MAXDebug` (CS) und `MaxManager` (CS).
- `BELEGT`: `ababilone/maxmanager` ist eine „Universal Windows Application that
  allows to manage a Max! / eQ-3 cube". Quellcode nicht im Detail gelesen →
  Konkrete Protokollkenntnisse daraus `UNBEKANNT`.
- `BELEGT`: `bietiekay/hacs/.../MAXDebug` nur als Pfad bekannt, nicht gelesen.

### 6.3 NuGet (Q16)

`BELEGT`: API-Abfrage `q=maxcube&prerelease=true` → `totalHits: 0`. Websearch
lieferte nur `Cube.Pdf` (unverwandt). **Fazit: kein installierbares,
gepflegtes NuGet-Paket.** Für .NET 10 bleibt nur Eigenimplementierung oder
ein lokales Portieren alter Quellen.

---

## 7. Security / Krypto / Firmware (Q13, Q14)

`BELEGT` aus Q13 (Insinuator, 2016):

- Reboot unauthentifiziert: `eQ3Max*\0KMD1016788R` (UDP).
- Nur eine TCP-Verbindung gleichzeitig.
- Firmware kann per UDP unauthentifiziert aktualisiert werden.
- `firmware.enc` ist verschlüsselt; Herkunft/Inhalt nicht dokumentiert.

`BELEGT` aus Q14: Snippets enthalten `decrypt.py`, `firmware.enc` (346.352 Byte)
und `parser/`. `decrypt.py` verwendet **AES-128-CBC** mit

```
KEY = 7815696ecbf1c96e6894b779456d330e
IV  = 368aea7612c9ab9163daea7612c9ac93
```

PKCS#7-ähnliches Padding, Base64-Ein/Ausgabe. `BELEGT`: Herkunft und
Geräteübergreifende Gültigkeit dieses Key/IV sind **nicht** dokumentiert;
Q13 spekuliert über eine Ableitung aus Seriennummer/QR-Code. Ich habe mehrere
naheliegende MD5-Kandidaten geprüft (`maxcube`, `eQ3Max`, …) – keiner ergab
diesen Key (`BELEGT`, lokale Prüfung). Deshalb:

- Konkreter AES-CBC-Schlüssel aus RE-Snippet: `BELEGT`.
- Ableitungsregel / Produktionsgültigkeit: `UNBEKANNT`.
- UDP-Firmware-Frame (Header, Sequence, CRC, Image-Format): `UNBEKANNT`.
- Für die App: Firmware-Update **nicht** implementieren; Cloud ist abgeschaltet (Q15). Die TCP-Befehle `e:`/`d:` sind dagegen als `EncryptCommand`/`DecryptCommand` implementiert (`CubeCommands.cs:260-271`) und dienen lokal auch als Idle-Keepalive; **nicht** übernommen ist ihre Verwendung als Cloud-Transport (Korrektur 24.11.2026).

---

## 8. Weitere Ökosystem-Quellen

### 8.1 Home Assistant (Q9, Q10)

`BELEGT`:

- Integration `maxcube`: Legacy, community-maintained, **Local Polling**.
- Default-Port 62910, `scan_interval: 300` s.
- `use_persistent_connection = scan_interval <= 300` – deckt sich mit Q5.
- **Keine Wochenpläne** in der HA-Integration → die Neuimplementierung braucht einen eigenen Pfad
  (`s:...10...`, Abschnitt 5.3).
- Modi in `climate.py`: `MAX_DEVICE_MODE_AUTOMATIC`, `_BOOST`, `_MANUAL`,
  `_VACATION`; `OFF_TEMPERATURE = 4.5`; `ON` laut Bouni 30,5 °C (`BELEGT`).

### 8.2 openHAB (Q11, Q12)

`BELEGT`:

- MAX!-Binding: Discovery per UDP, Bridge mit IP + Seriennummer, Reboot-Action.
- Wiki: Default-IP 192.168.0.222, Port 62910, `refreshInterval=10000`,
  `maxRequestsPerConnection`, **Exclusive-Mode** (nur ein Client).
- Bestätigt die 10-s-Poll-Frequenz aus MAXSharp (Abschnitt 6.1).

### 8.3 FHEM (Q1-README, `00_MAXLAN.pm` gelesen)

`BELEGT`: `fhem/FHEM/00_MAXLAN.pm` (Matthias Gehre, 2012–2013) nutzt
`MIME::Base64`, `MaxCommon`. Konstanten: `$reconnect_interval = 60`,
`$roundtriptime = 3`, `$read_timeout = 3`, `$metadata_magic = 0x56`,
`$metadata_version = 2`, `$defaultPollInterval = 60`. Der `Magic 0x56`
entspricht dem ersten Byte der M-Nutzdaten aus Q1 (`56 02 …`) – schöner
Quervergleich (`BELEGT`).

### 8.4 CLI-/andere Implementierungen (nur Q1-README gelistet)

`BELEGT` als Links vorhanden, Inhalte nicht gelesen → Details `UNBEKANNT`:

- https://github.com/ivesdebruycker/maxcube-cli (JavaScript)
- https://github.com/pacostiro/MAX-cube-ctl (C)
- https://github.com/spinscale/maxcube-java/ (Java)
- https://github.com/KnuthLohse/MAXCPP (C)
- https://github.com/ercpe/pymax (Python)
- https://github.com/Juerd/eq3-max (Perl)

Sie sind als zusätzliche Quervergleiche geeignet, aber nicht nötig, solange
Q1 + Q5 + Q10 übereinstimmen.

---

## 9. Strategie-Empfehlung (Abwägung)

### Variante (a): C#-Bibliothek nutzen

- **Pro:** C#-Idiomatik, `MAXSharp` liefert TCP-Loop, Diff-Queue, Gerätemodell.
- **Contra:** `BELEGT` AGPL v3, letzter Stand 2015, kein NuGet, kein .NET 10,
  kein async, keine Tests. Wartungsrisiko und Lizenzrisiko.
- **Urteil:** **Nicht als Fundament.** Höchstens `MAXMonitoringThread.cs` und
  `MAXEncodeDecode.cs` als Ideenquelle (ohne Codeübernahme).

### Variante (b): Python-Port nach C#

- **Pro:** `BELEGT` 0.4.3 ist komplett, modern (Typannotations), aktiv bis 2021,
  MIT-ähnlich, Timeouts/Retry/Wochenprogramm exakt nachvollziehbar. Direkt
  portierbar: `Message`, `Connection`, `Commander`, `MaxCube`, `Thermostat`.
- **Contra:** Python-Dynamik (Dicts, `struct`, `bytearray`) muss explizit
  getippt werden; einige Felder bleiben „unknown".
- **Urteil:** **Primäre Blaupause.** 1:1-Struktur in C# nachbauen, mit
  `CancellationToken`, `Channel<T>` statt `ConcurrentQueue` und
  `System.Text.Json` statt `json`.

### Variante (c): Java-REA + Bouni

- **Pro:** vorhandene `docs/maxcube-protocol.md` (687 Zeilen aus Java-1.4.6-REA)
  ist bereits im Projekt; Bouni hat mehr Nachrichtentypen und aktuellere Pflege.
- **Contra:** Java-Code ist sehr alt, vermutlich unvollständig; Decompilat
  schwer verifizierbar.
- **Urteil:** **Quervergleich/Fallback.** Bei Unklarheiten (z. B. `L:`-Offset,
  `z:`-Zeiteinheit) Java-REA und Bouni gegeneinanderhalten.

### Variante (d): Kombination – **empfohlen**

1. **Spec:** Bouni (Q1) als primäre Referenz; jede Zeile C#-Parser mit
   Bouni-Feldname kommentieren.
2. **Orchestrierung:** Python 0.4.3 (Q5) portieren – Connection Framing,
   Commander Timeouts/Retry, `set_temperature_mode`, `set_programme`.
3. **Quervergleich:** `docs/maxcube-protocol.md` (Java-REA) + FHEM-`00_MAXLAN.pm`
   + HA `climate.py` (Q10) für Gerätetypen/Modi.
4. **Nicht implementieren:** Cloud, UDP-Firmware, Reboot (Q13/Q15). Die TCP-Befehle `e:`/`d:` sind implementiert (`CubeCommands.cs:260-271`); nur der Cloud-Transport darüber bleibt ausgeschlossen (Korrektur 24.11.2026).
5. **Tests:** Gegen einen echten Cube: `l:` → Gerätezahl, `m:` → Räume,
   `s:...40...` → Ventil reagiert; `H:`-Duty-Cycle beobachten.
6. **Lizenz:** AGPL-Code (Q6) nicht übernehmen; MIT/Apache-Referenzen (Q1, Q5,
   Q10) können als Ideenquelle dienen, aber eigenen Code schreiben.

**Begründung:** Nur (d) liefert gleichzeitig aktuelle Felddefinitionen,
erprobtes Verbindungsverhalten und eine kontrollierbare Lizenzbasis. (a)
scheitert an AGPL und Alter, (b) allein an unbelegten Feldern, (c) allein an
fehlender Pflege.

---

## 10. Offene Punkte (`UNBEKANNT`) – nicht erraten

1. Bedeutung der „unknown"-Bytes in `L:`, `H:`, `M:` und `S:`.
2. `z:`-Zeiteinheit (Q1 widersprüchlich: `1e` = 30 s vs. „minutes").
3. Herkunft/Gültigkeit des AES-Key/IV aus Q14; keine Produktionsableitung
   gefunden.
4. Vollständiges UDP-Firmware-Update-Frame-Format.
5. `m:`-Schreibsemantik auf Firmware 1.1.3 / deutscher Variante.
6. Ob `N:`-Antwort nach `n:` bei allen Firmwares identisch gepuffert wird
   (Q1 warnt vor Timeout-Reihenfolge).
7. Exakte Bedeutung von `S:`-Statuscodes außer `0` (Q5 prüft nur `0`).

---

## 11. Quellenliste zum Nachschlagen (nur tatsächlich gesehene Links)

- Q1 https://github.com/Bouni/max-cube-protocol
  - https://github.com/Bouni/max-cube-protocol/blob/master/protocol.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/Cube_Discovery.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/L-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/C-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/H-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/M-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/S-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/A-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/N-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/Q-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/F-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/T-Command.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/Z-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/U-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/V-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/E-Message.md
  - https://github.com/Bouni/max-cube-protocol/blob/master/D-Message.md
- Q2 https://www.eq-3.com/downloads/download/max/pdb/en/MAX_Cube-LAN-Gateway_99004_datasheet_V3.0.pdf
- Q3 https://www.eq-3.com/Downloads/eq3/downloads_produktkatalog/max/bda/en/BC-LGW-O-TW_UM_EN.pdf
- Q4 https://pypi.org/project/maxcube-api/
- Q5 https://github.com/uebelack/python-maxcube-api
  - https://github.com/uebelack/python-maxcube-api/blob/master/maxcube/cube.py
  - https://github.com/uebelack/python-maxcube-api/blob/master/maxcube/commander.py
  - https://github.com/uebelack/python-maxcube-api/blob/master/maxcube/connection.py
  - https://github.com/uebelack/python-maxcube-api/blob/master/maxcube/thermostat.py
- Q6 https://github.com/bietiekay/MAXSharp
  - https://github.com/bietiekay/MAXSharp/blob/master/MAXSharp/MAXMonitoringThread.cs
  - https://github.com/bietiekay/MAXSharp/blob/master/MAXSharp/MAXEncodeDecode.cs
  - https://github.com/bietiekay/MAXSharp/blob/master/license.txt
- Q7 https://github.com/bietiekay/hacs/tree/master/tools/MAXDebug
- Q8 https://github.com/ababilone/maxmanager
- Q9 https://www.home-assistant.io/integrations/maxcube
- Q10 https://github.com/home-assistant/core/blob/dev/homeassistant/components/maxcube/climate.py
- Q11 http://www.openhab.org/addons/bindings/max
- Q12 https://github.com/openhab/openhab1-addons/wiki/MAX!Cube-Binding
- Q13 https://insinuator.net/2016/04/discover-the-unknown-analyzing-an-iot-device/
- Q14 https://github.com/ernw/insinuator-snippets/tree/master/maxcube/firmware/parser
  - https://github.com/ernw/insinuator-snippets/blob/master/maxcube/decrypt.py
- Q15 http://www.eq-3.de/produkte/max.html
- Q16 https://azuresearch-usnc.nuget.org/query?q=maxcube&prerelease=true
- Projekt-intern: `docs/maxcube-protocol.md` (687 Zeilen, Java-1.4.6-REA, `BELEGT`)
