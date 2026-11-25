# Byte-Abgleich Wochenprofil: Python `schedule.py` ↔ Java `SetRoomWeekTemperatureProfile`

> Status: CODE-BELEGT, 24.11.2026. Rein statischer Abgleich der dekompilierten Hersteller-JAR gegen `ha-maxcube`. Kein Hardware-Mitschnitt, kein Schreibzugriff auf einen Cube. Gegenprüfung: Claude Sonnet 5.5.

## Ergebnis
Für gültige Schaltpunkte (4,5–30,5 °C in 0,5-°C-Schritten, 5-Minuten-Raster bis 24:00, höchstens 7 Punkte je Telegramm) erzeugen beide Implementierungen **identische Bytes**. Der in `ha-maxcube-erkenntnisse.md` Abschnitt 9.3 als `UNBEKANNT` geführte Abgleich kann geschlossen werden.

## Ableitung

### Python
`ha-maxcube/custom_components/maxcube_local/schedule.py:108-110` schreibt je Punkt ein 16-Bit-Wort:

```python
(int(temp * 2) << 9) | (minutes // 5)
```

- Bits 0..8: Zeit in 5-Minuten-Schritten (0..288).
- Bits 9..15: Temperatur in Halbgraden (9..61 bei 4,5..30,5 °C).
- Der Wert wird als Big-Endian-Wort in zwei Bytes zerlegt: High-Byte = `temp*2 << 1`, Low-Byte = Zeit.

### Java
- `RadiogramSpecification.addTime5MinResolution()` (`:207-219`) legt 8 Zeitbits in Byte `12+2k` und das 9. Bit als Überlauf in Byte `11+2k` Bit 0; 24:00 wird als 288 kodiert.
- `RadiogramSpecification.addTemperature()` (`:272-279`, Renderer `:96-111`) legt 6 Temperatur-Bits in Byte `11+2k` Bit 1..6. Maximalwert 61 < 64, daher bleibt Bit 7 immer 0.
- `createPayload()` (`:114-125`) und `ByteUtil.fromInt()` (`:8-20`) schreiben Bits LSB-first im Byte.
- `addWeekDay()` (`:281-285`) schreibt 3 Bit `(value+1)%7` ab Byte 10 Bit 0: Samstag = 0, Sonntag = 1, Montag = 2 (vgl. `DayOfWeek.java:7-13`). Das entspricht `CUBE_DAY_ORDER` in `schedule.py:22`.
- Das Telegramm-1-Flag in Byte 10 Bit 4 wird nur für das zweite Telegramm gesetzt (`SetRoomWeekTemperatureProfile.java:90`). Python schreibt 0, weil es nur ein Telegramm mit höchstens 7 Punkten erzeugt.

### Äquivalenz
| Python-Wort | Byte 11 (Java) | Byte 12 (Java) | Beispiel |
|---|---|---|---|
| `(temp_half << 9) | time` | `temp_half << 1`, ggf. `| 0x01` bei `time > 255` | 20,5 °C / 06:00 → `52 48` |
| `time & 0x1FF` | Überlaufbit in Bit 0 | `time & 0xFF` | 4,5 °C / 24:00 → `13 20` |
| `temp_half & 0x7F` | Bits 1..6 | – | 30,5 °C / 20:00 → `7A F0` |

## Konsequenz für die C#-Implementierung
Die Python-Formel kann direkt als Radiogramm-Builder für das erste Telegramm übernommen werden; eine zweite Bitfeld-Implementierung ist nicht nötig. Für mehr als 7 Punkte bleibt Java die einzige Belegquelle: zweites Telegramm mit 6 Punkten, Offset 7 und Flag in Byte 10 Bit 4 (`SetRoomWeekTemperatureProfile.java:81-84,90-98`). Python lehnt mehr als 7 Punkte ab (`schedule.py:159-160`) und darf deshalb **nicht** als Beleg für das zweite Telegramm zitiert werden.

## Doku-Korrekturen
- `docs/ha-maxcube-erkenntnisse.md:106,277-278`: Wochentag sind **3 Bit (Byte 10 Bit 0..2)**, nicht Bit 0..3.
- `docs/ha-maxcube-erkenntnisse.md:280`: Die Bitfelder sind **aneinandergrenzend**, nicht überlappend; nur das Zeit-Überlaufbit liegt im selben Byte wie die Temperatur.
- `docs/maxcube-protocol.md:424`: Die Zeit ist vollständig **9 Bit** (8 Bit + Überlauf), nicht nur 8 Bit; vgl. `:404`.
- Die im REA-Auftrag genannte `0x1F`-Maske ist im Workspace nicht belegt. Belegt ist `0x1FF` für die 9-Bit-Zeit (`schedule.py:62`); 6 bzw. 7 Temperatur-Bits entsprächen `0x3F` bzw. `0x7F`. Die `0x1F`-Spur wird daher nicht übernommen.

## Grenzen
Kein Hardware-Mitschnitt eines echten `0x10`-Telegramms. Wie der Cube auf ein Java-artiges 7+6-Schreiben reagiert, bleibt `UNBEKANNT`. Keine Geheimnisse, IPs oder Seriennummern in diesem Dokument.

## Quellen
- `ha-maxcube/custom_components/maxcube_local/schedule.py:22,61-63,97-118,159-160`
- `app-maxcube/decompiled/essentials/sources/de/eq3/max/al/core/command/SetRoomWeekTemperatureProfile.java:81-98`
- `app-maxcube/decompiled/essentials/sources/de/eq3/max/al/core/radiogram/specification/RadiogramSpecification.java:96-111,207-219,272-285`
- `app-maxcube/decompiled/essentials/sources/de/eq3/max/al/core/radiogram/specification/ByteUtil.java:8-20`
- `docs/ha-maxcube-erkenntnisse.md`, `docs/maxcube-protocol.md`
