# Reverse-Engineering-Wiki – MAX! Local Application 1.4.6

> Ziel: Vollständige fachliche Rekonstruktion für eine Neuimplementierung in C#/.NET 10 + Avalonia.
> **Kein** Fork, **keine** 1:1-Portierung. Der Java-Code ist ausschließlich Informationsquelle.
>
> **Hinweis zum öffentlichen Repo:** Die RE-Eingaben (`decompiled/`, `extracted/`, `evidence/`) sind aus Lizenzgründen nicht Bestandteil von `xplat-maxcube`. Die nachfolgenden `Datei:Zeile`-Belege verweisen auf diese lokalen Eingaben; veröffentlicht sind ausschließlich unsere eigenen Auswertungen unter `docs/`.

**Englische Fassung:** Alle 25 Dokumente unter `docs/` haben eine englische Übersetzung unter [`docs/en/`](en/INDEX.md). Die Übersetzungen sind maschinell erstellt und mit `native-speaker review pending` gekennzeichnet; maßgeblich ist die deutsche Quelle.

---

## 1. Dokumentationsindex

| Phase | Dokument | Inhalt | Zeilen | Status |
|---|---|---|---:|---|
| 1 | [architecture-rea.md](architecture-rea.md) | REA-Bestandsarchitektur der MAX Local Application 1.4.6: Schichten, Laufzeit, Ports. Ursprünglich `architecture.md`; am 25.11.2026 umbenannt, damit die neue Entwickler-Doku unter `ARCHITECTURE.md` liegen kann. Inhalt deckt sich mit `SecondBrain/raw/app-maxcube-rea/2026-11-24/architecture.md` (319 Zeilen). | 319 | ✅ geprüft |
| 1 | [modules.md](modules.md) | Module/JARs, Klassen nach Paket, Rollen, Lebenszyklen | 348 | ✅ geprüft |
| 1 | [dependencies.md](dependencies.md) | Bibliotheken, Spring/DWR-Wiring, externe Dienste | 222 | ✅ geprüft |
| 1 | [dataflow.md](dataflow.md) | 9 Datenflüsse: Cube ↔ UI, Persistenz, Update, Remote, Shutdown | 367 | ✅ geprüft |
| 2 | [maxcube-protocol.md](maxcube-protocol.md) | Vollständiges MAX!-Cube-Protokoll, 28 Befehle, Radiogramme, UDP | 687 | ✅ geprüft |
| Quervergleich | [byte-abgleich-python-java.md](byte-abgleich-python-java.md) | Wochenprofil-Kodierung Python↔Java ist byte-identisch; CODE-BELEGT, Sonnet-Gegenprüfung, drei Doku-Korrekturen | 51 | ✅ geprüft |
| 3 | [domain-model.md](domain-model.md) | Fachobjekte, Properties, Kardinalitäten, Validierung, DTO↔AL | 530 | ✅ geprüft |
| 4 | [gui-specification.md](gui-specification.md) | Swing-Shell/Tray + Browser-UI, alle Ansichten und Dialoge | 536 | ✅ geprüft |
| 5 | [features.md](features.md) | 61 Feature-Zeilen mit Priorität P0–P2, Umsetzungsstatus (49 umgesetzt, 1 teilweise, 1 ersetzt, 10 verworfen) und Aufwand S–XL | 160 | ✅ geprüft |
| 6 | [target-architecture.md](target-architecture.md) | C#/.NET 10 + Avalonia Zielentwurf, Clean Architecture, SQLite | 263 | ✅ geprüft |
| 7 | [roadmap.md](roadmap.md) | 5 Phasen, 20 Arbeitspakete, Risiken, Akzeptanzkriterien | 307 | ✅ geprüft |
| 8 | [ha-maxcube-erkenntnisse.md](ha-maxcube-erkenntnisse.md) | Live-bewährter Code-Abgleich: 13 Slots/Tag, Modus-Kodierung, FrameTypes, 26 Übernahmeregeln | 493 | ✅ geprüft |
| 9 | [protokoll-referenzen.md](protokoll-referenzen.md) | 16 Internetquellen: Bouni-Spec, Python-Referenz, C#/NuGet-Lage, Lizenzen, Strategie | 495 | ✅ geprüft |

**Gesamt:** 12 Phasendokumente + 1 Quervergleich + dieses Wiki.

### Entwickler-, Betriebs- und Wiki-Dokumentation (neu, Stand 25.11.2026)

Zehn Zieldateien des Auftrags – eine Root-Datei und neun Dateien in `docs/`. Sie beschreiben den **Ist-Zustand der C#-App**, nicht die Java-REA.

| Ziel | Dokument | Inhalt |
|---|---|---|
| Schnelleinstieg | [README.md](../README.md) | Start Windows/Linux, Selbsttest, Grenzen, Verifikationsstand |
| Architektur | [ARCHITECTURE.md](ARCHITECTURE.md) | Senior-Architekt-Sicht: Kontext, Schichten, ADRs, Risiken, Verbesserungen |
| Entwicklung | [DEVELOPMENT.md](DEVELOPMENT.md) | Setup, Konventionen, 519/519 Tests, Erweiterungsrezepte, Versionierung |
| API | [API.md](API.md) | lokale C#-Interfaces + Cube-Wire-Protokoll; **keine HTTP-API** |
| Datenbank | [DATABASE.md](DATABASE.md) | SQLite-Schema, Migration, Einstellungen, Audit |
| Betrieb | [OPERATIONS.md](OPERATIONS.md) | Netz, Pfade, CI, Pakete, Backup; keine Server/Kubernetes |
| Sicherheit | [SECURITY.md](SECURITY.md) | Bedrohungsmodell, Datenschutz, lokale Logs, keine Secrets |
| Fehlerbehebung | [TROUBLESHOOTING.md](TROUBLESHOOTING.md) | Selbsttest-Exitcodes, MAX-Codes, Logs, Schreibsymptome |
| Runbook | [RUNBOOK.md](RUNBOOK.md) | Start/Stopp, Backup/Restore, Konfigurationssendung, Hardwaretest |
| Wiki-Anbindung | [SECOND_BRAIN.md](SECOND_BRAIN.md) | SecondBrain-Regeln, Lessons Learned, Rückkopplung nach Hardwaretest |
| Hardwarebeleg | [HARDWARE-VERIFY.md](HARDWARE-VERIFY.md) | Original-Cube `192.168.10.60`: Discovery-Korrektur, `0x11` Komfort/Eco, Rücksetzung, Mitschnitt |

Dieses [INDEX.md](INDEX.md) bleibt das Verzeichnis der **Analysequellen**; die zehn Dateien oben sind die **Betriebs-/Entwicklungsdokumentation**. Dateinamen werden case-sensitiv geschrieben; `docs/ARCHITECTURE.md` und `docs/architecture-rea.md` sind zwei verschiedene Dokumente.

**Strategie-Entscheidung (24.11.2026): Eigene C#-Protokollschicht; Bouni (MIT) als Spec, Python `maxcube-api` 0.4.3 als Verhaltensoracle, Java-REA und `ha-maxcube` als Quervergleich. Kein NuGet-Paket vorhanden; `MAXSharp` ist AGPL v3 und eingefroren. Details in `protokoll-referenzen.md` Abschnitt 9 und `ha-maxcube-erkenntnisse.md` Abschnitt 10.

**Cloud-Status (Nutzerhinweis 25.11.2026, `FAKT`, nicht extern verifiziert):** eQ-3 hat die MAX!-Cloud-Steuerung abgeschaltet und die Cloud-App aus dem Android- und iOS-App-Store entfernt. Die `verworfen`-Zeilen in [features.md](features.md) bleiben damit dauerhaft ausgeschlossen.

**Wichtige Querverweise:**
- Default-Wert-Konflikt (Properties vs. `Room`-Konstruktor): `docs/domain-model.md` Abschnitt 3.2 → `docs/target-architecture.md` Abschnitt 6.
- Protokoll ist die authoritative Referenz für alle Opcodes: `docs/maxcube-protocol.md` Abschnitte 5–6 → `docs/roadmap.md` AP-1.4.
- Port-Regel (`127.0.0.1`/Host-IP → 54321, sonst 62910): `architecture-rea.md`, `modules.md`, `INDEX.md` §4.
- Persistenz: keine Datenbank im Original; SQLite im Ziel ist Neubau: `architecture-rea.md`, `dependencies.md`, `target-architecture.md` §7.
- Schaltpunkte: 13 Slots je Tag, 7 + 6 je zwei Schreibtelegramme – korrigiert die ältere Inferenz „6 je Tag": `ha-maxcube-erkenntnisse.md` §3.
- Modus: `off` = `MANUAL` + 4,5 °C, `HEAT` = `MANUAL` + letzter echter Sollwert; Sollwert nie ohne Modus senden: `ha-maxcube-erkenntnisse.md` §5.
- Ports: 62910 Cube-TCP, 54321 virtueller Cube, 63121 dynamischer Web-UI-Port (kein Widerspruch): `ha-maxcube-erkenntnisse.md` §4.

---

## 2. Methode & Werkzeugstatus

| Aspekt | Status |
|---|---|
| REA-CLI | `rea 6.10.1`, Node `v26.13.1` – installiert und verwendet |
| REA-MCP in OpenCode | **nicht registriert** (`rea doctor --client opencode`: `registration:opencode` = `config_drift` / `missing`). Deshalb CLI-Fallback gemäß REA-Recovery-Guide genutzt. |
| Java-Dekompiler | `jadx-headless-mcp 0.7.1-all.jar`, SHA-256 verifiziert (`6e5eacf500b64292bfb73c49797c1958f6ee44646e43e868039ae7feb573ff75`); jadx-API lokal ausgeführt. **Nicht** REA-Evidence. |
| JDK | Temurin 21.0.13 (`jar tf`, `javap`) für Inventare/Quellverifikation |
| Artefakte verändert? | **Nein.** Originalpfade nur gelesen. Extrakte liegen unter `extracted/`, Dekompilate unter `decompiled/`. |
| Netzwerkzugriff auf Herstellerdienste | **Nicht** erfolgt. URLs nur aus Konfiguration/Code dokumentiert. |

**Kennzeichnungskonvention in allen Dokumenten:**
- `BEOBACHTUNG` / `FAKT` / Quellstelle `Datei:Zeile` → direkt im Artefakt belegt.
- `REA Evidence ev_…` → von REA-CLI erzeugter Befund mit Artefakt-Digest.
- `INFERENZ` → aus Code abgeleitet, nicht direkt beobachtet.
- `UNBEKANNT` → nicht belegbar; darf nicht als Implementierungsgrundlage ohne Validierung dienen.

**Qualitätsprüfung:** Alle `UNBEKANNT`-Markierungen wurden gegen die materialisierten Dateien nachgeprüft. Fälschlich als unbekannt markierte, tatsächlich belegte Sachverhalte (Port-Regel, Property-Inhalte, Metadaten-Blockgröße, Kommando-Inventar, State-Reihenfolge, Byte-Reihenfolge der TCP-Kommandos) wurden in `BEOBACHTUNG` umgewandelt und mit Quellstellen versehen.

---

## 3. Artefakt- & Evidence-Register

### 3.1 Bootstrapper (Installationsverzeichnis)
`C:\Program Files (x86)\Max Local Application\` – Version `1.4.6` (`version.txt`), Sprache `de` (`Setup.config.txt`).

| Artefakt | SHA-256 (Auszug) | REA Evidence |
|---|---|---|
| `MAXRuntimeStarter.jar` | `553d54219c649e49…` | `ev_81a48cb5c63ace439b913812ff1d104c79c9a1179387732f5682a5545799e809` (Summary `ev_7c6b299ce5a2e8199add769013f8f812eecb2a45d6c2d00055c52f1829170a0d`) |
| `lib/commons-io-2.1.jar` | `c4e99ee90946a3a9…` | – (nur Hash via PowerShell) |
| `lib/WinFoldersJava-1.1.jar` | `e6a8978a0b92e1e9…` | – |
| `lib/WinFoldersJava_x64-1.1.dll` | `97cab4ef75280969…` | – |
| `lib/WinFoldersJava-1.1.dll` | `8fad091f1f92f0a2…` | – |

Extraktion: REA `ev_49ca970502d6fa52ea44268706a0d11f67eaf1e7d3fc6ee865d31521930f2209`.

### 3.2 Hauptinstallation
`C:\ProgramData\MAX\` – Version `1.4.6` (`version.txt`), Getdown-Basis `http://max.eq-3.de/max/` (`getdown.txt:1`).

| Artefakt | REA Evidence | Einträge (`jar tf`) | Dekompiliert |
|---|---|---|---|
| `MaxLocalApp.jar` | `ev_0a83b0f4dfeaa8dc02b4b7e5168c4e25586ca4f78ee9bb199f263cf7971f0f67` | 85 (47 `.class`) | `decompiled/maxlocalapp` (29 `.java`) |
| `lib/MaxEssentialsBackend-1.4.6.jar` | `ev_cd71f066e83568d1ab3c71c8f78b5ea143ceb6dcf3b32ee7b5329970380de677` | 285 (254 `.class`) | `decompiled/essentials` (232 `.java`) |
| `lib/MaxLocalBackend-1.4.6.jar` | `ev_b01b22f5d2b54d783aa1cb8dbe567111e03a6fe5029e1641318be232168a43a7` | 144 (114 `.class`) | `decompiled/localbackend` (109 `.java`) |
| `lib/MaxSettings.jar` | `ev_31f2c19749714a95e3e7fb36dfc7552969f422676dd7ea8282172d359855089f` | 5 (0 `.class`) | – (nur Properties) |
| `webapp/root.war` | `ev_198593f77a5b718fcd92173c539c0b5f7d78752738b9e66ae993003f805d2e67` | 286 (0 `.class`) | `extracted/rootwar` (JS/CSS/HTML) |
| `tools/MaxControllerApplication.jar` | `ev_42e4885a961f14d51aae2e5f880199599ca787a822b9dc66cd85ce2d46a9c91a` | 156 (115 `.class`) | `decompiled/controller` (110 `.java`) |

Vollständige REA-Ergebnisse: `evidence/{maxlocalapp,essentials,localbackend,settings,rootwar,controller}-rea.json` und `*-evidence.json`; Extraktions-Evidence: `*-extract.json`; Klasseninventare: `*-entries.txt`; Bootstrapper-Bytecode: `evidence/javap-starter.txt`. Insgesamt 25 Evidence-/Arbeitsdateien verifiziert.

---

## 4. Zentrale Eckdaten (schnell nachschlagen)

| Thema | Wert | Quelle |
|---|---|---|
| Cube-Port (physisch) | TCP **62910** | `LocalConfiguration.java:12,81-92` |
| Cube-Port (virtuell/lokal) | TCP **54321** (nur bei IP `127.0.0.1` oder Host-IP) | `max.local.properties:22`, `LocalConfiguration.java:81-92` |
| Web-UI | `http://localhost:<ServerPort>` | `Application.java:275-277`; Port dynamisch (`PortFactory`) |
| Servlets | `/dwr/*`, `/controller/*`, `/*.html` | `extracted/rootwar/WEB-INF/web.xml:14-63` |
| Session-Timeout | 5 min | `web.xml:66-68` |
| Firmware-Fenster | 273–275 | `max.local.properties:17-18` |
| Verbindungs-Timeouts | 30 s connect / 70 s socket | `max.local.properties:23-24` |
| Command-Queue | 100 | `max.local.properties:21` |
| Watchdog / Idle | 5 s / 30 s / 900 s | `LocalApp-al.xml:11-12`, `max.local.properties:4-5` |
| Inclusion-Mode | 60 s | `max.local.properties:9` |
| Metadata-Block | 1900 | `max.local.properties:8`, `SendMetadataCubeCommand.java:14-17,33,44` |
| Grenzen | 32 Zeichen, 20 Räume, 140 Geräte | `max.local.properties:1-3` |
| Default-Temperaturen | **zwei Ebenen:** Properties 20.0/17.0/21.0/17.0; `Room`-Konstruktor 16.5/21.5/12.0 | `max.properties:2-5`; `Room.java:45-48`; `domain-model.md` §3.2 |
| Fehlercodes | MAX0001, MAX1001–MAX1051 | `extracted/essentials/errorcodes.properties` |
| Persistenz Original | `~/.MAX/*.settings` (java.util.Properties), **keine DB** | `SettingsProvider.java:35-76`; Suche nach jdbc/sqlite/@Entity ohne Treffer |
| Sprachen | de, en, fr, it, nl, pl | `extracted/maxlocalapp/MaxLocalAppStarter_*.properties` |

---

## 5. Ordnerstruktur dieses Workspace

```
app-maxcube/
├── docs/          → dieses Wiki + 10 Phasendokumente
├── evidence/      → 25 REA-CLI-JSONs, javap-Ausgabe, Jar-Inventare
├── extracted/     → REA-extrahierte JAR/WAR-Inhalte (Konfiguration, JS, Properties)
└── decompiled/    → jadx-dekompilierter Java-Quellcode (480 .java, Analysezwecke)
```

**Hinweis:** `decompiled/` ist eine Zwischenstufe der Analyse. Die Zielimplementierung darf daraus **keinen** Code kopieren; sie folgt ausschließlich den technischen Spezifikationen in `docs/`.
