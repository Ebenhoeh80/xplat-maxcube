> English translation of docs/byte-abgleich-python-java.md. Machine-created; DE/EN reviewed by the user on 27.11.2026; FR/IT/NL/PL feedback expected via issues/PRs.

# Byte comparison of weekly schedule: Python `schedule.py` ↔ Java `SetRoomWeekTemperatureProfile`

> Status: CODE-VERIFIED, 24.11.2026. Purely static comparison of the decompiled manufacturer JAR against `ha-maxcube`. No hardware capture, no write access to a cube. Cross-check: Claude Sonnet 5.5.

## Result
For valid switching points (4.5–30.5 °C in 0.5 °C steps, 5-minute grid up to 24:00, at most 7 points per telegram), both implementations produce **identical bytes**. The comparison listed as `UNKNOWN` in section 9.3 of `ha-maxcube-erkenntnisse.md` can be closed.

## Derivation

### Python
`ha-maxcube/custom_components/maxcube_local/schedule.py:108-110` writes one 16-bit word per point:

```python
(int(temp * 2) << 9) | (minutes // 5)
```

- Bits 0..8: time in 5-minute steps (0..288).
- Bits 9..15: temperature in half degrees (9..61 for 4.5..30.5 °C).
- The value is split into two bytes as a big-endian word: high byte = `temp*2 << 1`, low byte = time.

### Java
- `RadiogramSpecification.addTime5MinResolution()` (`:207-219`) puts 8 time bits into byte `12+2k` and the 9th bit as overflow into byte `11+2k` bit 0; 24:00 is encoded as 288.
- `RadiogramSpecification.addTemperature()` (`:272-279`, renderer `:96-111`) puts 6 temperature bits into byte `11+2k` bits 1..6. Maximum value 61 < 64, so bit 7 always remains 0.
- `createPayload()` (`:114-125`) and `ByteUtil.fromInt()` (`:8-20`) write bits LSB-first within the byte.
- `addWeekDay()` (`:281-285`) writes 3 bits `(value+1)%7` starting at byte 10 bit 0: Saturday = 0, Sunday = 1, Monday = 2 (cf. `DayOfWeek.java:7-13`). This corresponds to `CUBE_DAY_ORDER` in `schedule.py:22`.
- The telegram-1 flag in byte 10 bit 4 is set only for the second telegram (`SetRoomWeekTemperatureProfile.java:90`). Python writes 0 because it generates only one telegram with at most 7 points.

### Equivalence
| Python word | Byte 11 (Java) | Byte 12 (Java) | Example |
|---|---|---|---|
| `(temp_half << 9) | time` | `temp_half << 1`, optionally `| 0x01` if `time > 255` | 20.5 °C / 06:00 → `52 48` |
| `time & 0x1FF` | overflow bit in bit 0 | `time & 0xFF` | 4.5 °C / 24:00 → `13 20` |
| `temp_half & 0x7F` | bits 1..6 | – | 30.5 °C / 20:00 → `7A F0` |

## Consequence for the C# implementation
The Python formula can be adopted directly as a radiogram builder for the first telegram; a second bit-field implementation is not needed. For more than 7 points, Java remains the only source of evidence: second telegram with 6 points, offset 7, and flag in byte 10 bit 4 (`SetRoomWeekTemperatureProfile.java:81-84,90-98`). Python rejects more than 7 points (`schedule.py:159-160`) and therefore **must not** be cited as evidence for the second telegram.

## Documentation corrections
- `docs/ha-maxcube-erkenntnisse.md:106,277-278`: the day of the week is **3 bits (byte 10 bits 0..2)**, not bits 0..3.
- `docs/ha-maxcube-erkenntnisse.md:280`: the bit fields are **adjacent**, not overlapping; only the time overflow bit is in the same byte as the temperature.
- `docs/maxcube-protocol.md:424`: the time is fully **9 bits** (8 bits + overflow), not only 8 bits; cf. `:404`.
- The `0x1F` mask mentioned in the REA brief is not evidenced in the workspace. What is evidenced is `0x1FF` for the 9-bit time (`schedule.py:62`); 6 or 7 temperature bits would correspond to `0x3F` or `0x7F`. The `0x1F` trace is therefore not adopted.

## Limits
No hardware capture of a real `0x10` telegram. How the cube reacts to a Java-like 7+6 write remains `UNKNOWN`. No secrets, IPs, or serial numbers in this document.

## Sources
- `ha-maxcube/custom_components/maxcube_local/schedule.py:22,61-63,97-118,159-160`
- `app-maxcube/decompiled/essentials/sources/de/eq3/max/al/core/command/SetRoomWeekTemperatureProfile.java:81-98`
- `app-maxcube/decompiled/essentials/sources/de/eq3/max/al/core/radiogram/specification/RadiogramSpecification.java:96-111,207-219,272-285`
- `app-maxcube/decompiled/essentials/sources/de/eq3/max/al/core/radiogram/specification/ByteUtil.java:8-20`
- `docs/ha-maxcube-erkenntnisse.md`, `docs/maxcube-protocol.md`
