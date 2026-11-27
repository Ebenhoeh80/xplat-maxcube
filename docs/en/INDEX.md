> English translation of docs/INDEX.md. Machine-created; DE/EN reviewed by the user on 27.11.2026; FR/IT/NL/PL feedback expected via issues/PRs.

# Reverse-engineering wiki – MAX! Local Application 1.4.6

> Goal: complete technical reconstruction for a reimplementation in C#/.NET 10 + Avalonia.
> **No** fork, **no** 1:1 port. The Java code serves exclusively as a source of information.
>
> **Note on the public repo:** The RE inputs (`decompiled/`, `extracted/`, `evidence/`) are not part of `xplat-maxcube` for licensing reasons. The `file:line` references below point to these local inputs; only our own analyses under `docs/` are published.

**German original:** The authoritative source is [`docs/INDEX.md`](../INDEX.md). All 25 documents under `docs/` have an English translation here in `docs/en/`; the translations are machine-created; DE/EN were reviewed and approved by the user on 27.11.2026, FR/IT/NL/PL await feedback via issues/PRs.

---

## 1. Documentation index

| Phase | Document | Content | Lines | Status |
|---|---|---|---:|---|
| 1 | [architecture-rea.md](architecture-rea.md) | REA as-built architecture of MAX Local Application 1.4.6: layers, runtime, ports. Originally `architecture.md`; renamed on 25.11.2026 so the new developer documentation could be placed under `ARCHITECTURE.md`. Content matches `SecondBrain/raw/app-maxcube-rea/2026-11-24/architecture.md` (319 lines). | 319 | ✅ checked |
| 1 | [modules.md](modules.md) | Modules/JARs, classes by package, roles, lifecycles | 348 | ✅ checked |
| 1 | [dependencies.md](dependencies.md) | Libraries, Spring/DWR wiring, external services | 222 | ✅ checked |
| 1 | [dataflow.md](dataflow.md) | 9 data flows: cube ↔ UI, persistence, update, remote, shutdown | 367 | ✅ checked |
| 2 | [maxcube-protocol.md](maxcube-protocol.md) | Complete MAX! Cube protocol, 28 commands, radiograms, UDP | 687 | ✅ checked |
| Cross-check | [byte-abgleich-python-java.md](byte-abgleich-python-java.md) | Weekly schedule encoding Python↔Java is byte-identical; CODE-VERIFIED, Sonnet cross-check, three documentation corrections | 51 | ✅ checked |
| 3 | [domain-model.md](domain-model.md) | Technical objects, properties, cardinalities, validation, DTO↔AL | 530 | ✅ checked |
| 4 | [gui-specification.md](gui-specification.md) | Swing shell/tray + browser UI, all views and dialogs | 536 | ✅ checked |
| 5 | [features.md](features.md) | 61 feature rows with priority P0–P2, implementation status (49 implemented, 1 partial, 1 replaced, 10 discarded), and effort S–XL | 160 | ✅ checked |
| 6 | [target-architecture.md](target-architecture.md) | C#/.NET 10 + Avalonia target design, Clean Architecture, SQLite | 263 | ✅ checked |
| 7 | [roadmap.md](roadmap.md) | 5 phases, 20 work packages, risks, acceptance criteria | 307 | ✅ checked |
| 8 | [ha-maxcube-erkenntnisse.md](ha-maxcube-erkenntnisse.md) | Comparison with live-proven code: 13 slots/day, mode encoding, frame types, 26 adoption rules | 493 | ✅ checked |
| 9 | [protokoll-referenzen.md](protokoll-referenzen.md) | 16 internet sources: Bouni spec, Python reference, C#/NuGet situation, licenses, strategy | 495 | ✅ checked |

**Total:** 12 phase documents + 1 cross-check + this wiki.

### Developer, operations, and wiki documentation (new, as of 25.11.2026)

Ten target files of the assignment – one root file and nine files in `docs/`. They describe the **as-built state of the C# app**, not the Java REA.

| Target | Document | Content |
|---|---|---|
| Quick start | [README.md](../../README.md) | start Windows/Linux, self-test, limits, verification status |
| Architecture | [ARCHITECTURE.md](ARCHITECTURE.md) | senior architect view: context, layers, ADRs, risks, improvements |
| Development | [DEVELOPMENT.md](DEVELOPMENT.md) | setup, conventions, 553/553 tests, extension recipes, versioning |
| API | [API.md](API.md) | local C# interfaces + cube wire protocol; **no HTTP API** |
| Database | [DATABASE.md](DATABASE.md) | SQLite schema, migration, settings, audit |
| Operations | [OPERATIONS.md](OPERATIONS.md) | network, paths, CI, packages, backup; no server/Kubernetes |
| Security | [SECURITY.md](SECURITY.md) | threat model, privacy, local logs, no secrets |
| Troubleshooting | [TROUBLESHOOTING.md](TROUBLESHOOTING.md) | self-test exit codes, MAX codes, logs, write symptoms |
| Runbook | [RUNBOOK.md](RUNBOOK.md) | start/stop, backup/restore, configuration sending, hardware test |
| Wiki integration | [SECOND_BRAIN.md](SECOND_BRAIN.md) | SecondBrain rules, lessons learned, feedback after hardware test |
| Hardware evidence | [HARDWARE-VERIFY.md](HARDWARE-VERIFY.md) | original cube `192.168.10.60`: discovery correction, `0x11` comfort/eco, reset, capture |

This [INDEX.md](INDEX.md) remains the directory of **analysis sources**; the ten files above are the **operations/development documentation**. File names are written case-sensitively; `docs/ARCHITECTURE.md` and `docs/architecture-rea.md` are two different documents.

**Strategy decision (24.11.2026): Own C# protocol layer; Bouni (MIT) as spec, Python `maxcube-api` 0.4.3 as behavior oracle, Java REA and `ha-maxcube` as cross-check. No NuGet package available; `MAXSharp` is AGPL v3 and frozen. Details in `protokoll-referenzen.md` section 9 and `ha-maxcube-erkenntnisse.md` section 10.

**Cloud status (user notice 25.11.2026, `FACT`, not externally verified):** eQ-3 has shut down MAX! cloud control and removed the cloud app from the Android and iOS app stores. The `discarded` rows in [features.md](features.md) are therefore permanently excluded.

**Important cross-references:**
- Default value conflict (properties vs. `Room` constructor): `docs/domain-model.md` section 3.2 → `docs/target-architecture.md` section 6.
- Protocol is the authoritative reference for all opcodes: `docs/maxcube-protocol.md` sections 5–6 → `docs/roadmap.md` AP-1.4.
- Port rule (`127.0.0.1`/host IP → 54321, otherwise 62910): `architecture-rea.md`, `modules.md`, `INDEX.md` §4.
- Persistence: no database in the original; SQLite in the target is a new development: `architecture-rea.md`, `dependencies.md`, `target-architecture.md` §7.
- Switching points: 13 slots per day, 7 + 6 across two write telegrams – corrects the older inference "6 per day": `ha-maxcube-erkenntnisse.md` §3.
- Mode: `off` = `MANUAL` + 4.5 °C, `HEAT` = `MANUAL` + last real target value; never send a target value without a mode: `ha-maxcube-erkenntnisse.md` §5.
- Ports: 62910 cube TCP, 54321 virtual cube, 63121 dynamic web UI port (no contradiction): `ha-maxcube-erkenntnisse.md` §4.

---

## 2. Method and tool status

| Aspect | Status |
|---|---|
| REA CLI | `rea 6.10.1`, Node `v26.13.1` – installed and used |
| REA MCP in OpenCode | **not registered** (`rea doctor --client opencode`: `registration:opencode` = `config_drift` / `missing`). Therefore CLI fallback used according to the REA recovery guide. |
| Java decompiler | `jadx-headless-mcp 0.7.1-all.jar`, SHA-256 verified (`6e5eacf500b64292bfb73c49797c1958f6ee44646e43e868039ae7feb573ff75`); jadx API run locally. **Not** REA evidence. |
| JDK | Temurin 21.0.13 (`jar tf`, `javap`) for inventories/source verification |
| Artifacts modified? | **No.** Original paths only read. Extracts are under `extracted/`, decompilations under `decompiled/`. |
| Network access to manufacturer services | **None.** URLs documented only from configuration/code. |

**Marking convention in all documents:**
- `OBSERVATION` / `FACT` / source location `file:line` → directly evidenced in the artifact.
- `REA Evidence ev_…` → finding generated by REA CLI with artifact digest.
- `INFERENCE` → derived from code, not directly observed.
- `UNKNOWN` → not evidenced; must not serve as a basis for implementation without validation.

**Quality check:** All `UNKNOWN` markers were rechecked against the materialized files. Facts falsely marked as unknown but actually evidenced (port rule, property contents, metadata block size, command inventory, state order, byte order of TCP commands) were converted to `OBSERVATION` and provided with source locations.

---

## 3. Artifact and evidence register

### 3.1 Bootstrapper (installation directory)
`C:\Program Files (x86)\Max Local Application\` – version `1.4.6` (`version.txt`), language `de` (`Setup.config.txt`).

| Artifact | SHA-256 (excerpt) | REA evidence |
|---|---|---|
| `MAXRuntimeStarter.jar` | `553d54219c649e49…` | `ev_81a48cb5c63ace439b913812ff1d104c79c9a1179387732f5682a5545799e809` (summary `ev_7c6b299ce5a2e8199add769013f8f812eecb2a45d6c2d00055c52f1829170a0d`) |
| `lib/commons-io-2.1.jar` | `c4e99ee90946a3a9…` | – (hash only via PowerShell) |
| `lib/WinFoldersJava-1.1.jar` | `e6a8978a0b92e1e9…` | – |
| `lib/WinFoldersJava_x64-1.1.dll` | `97cab4ef75280969…` | – |
| `lib/WinFoldersJava-1.1.dll` | `8fad091f1f92f0a2…` | – |

Extraction: REA `ev_49ca970502d6fa52ea44268706a0d11f67eaf1e7d3fc6ee865d31521930f2209`.

### 3.2 Main installation
`C:\ProgramData\MAX\` – version `1.4.6` (`version.txt`), Getdown base `http://max.eq-3.de/max/` (`getdown.txt:1`).

| Artifact | REA evidence | Entries (`jar tf`) | Decompiled |
|---|---|---|---|
| `MaxLocalApp.jar` | `ev_0a83b0f4dfeaa8dc02b4b7e5168c4e25586ca4f78ee9bb199f263cf7971f0f67` | 85 (47 `.class`) | `decompiled/maxlocalapp` (29 `.java`) |
| `lib/MaxEssentialsBackend-1.4.6.jar` | `ev_cd71f066e83568d1ab3c71c8f78b5ea143ceb6dcf3b32ee7b5329970380de677` | 285 (254 `.class`) | `decompiled/essentials` (232 `.java`) |
| `lib/MaxLocalBackend-1.4.6.jar` | `ev_b01b22f5d2b54d783aa1cb8dbe567111e03a6fe5029e1641318be232168a43a7` | 144 (114 `.class`) | `decompiled/localbackend` (109 `.java`) |
| `lib/MaxSettings.jar` | `ev_31f2c19749714a95e3e7fb36dfc7552969f422676dd7ea8282172d359855089f` | 5 (0 `.class`) | – (properties only) |
| `webapp/root.war` | `ev_198593f77a5b718fcd92173c539c0b5f7d78752738b9e66ae993003f805d2e67` | 286 (0 `.class`) | `extracted/rootwar` (JS/CSS/HTML) |
| `tools/MaxControllerApplication.jar` | `ev_42e4885a961f14d51aae2e5f880199599ca787a822b9dc66cd85ce2d46a9c91a` | 156 (115 `.class`) | `decompiled/controller` (110 `.java`) |

Complete REA results: `evidence/{maxlocalapp,essentials,localbackend,settings,rootwar,controller}-rea.json` and `*-evidence.json`; extraction evidence: `*-extract.json`; class inventories: `*-entries.txt`; bootstrapper bytecode: `evidence/javap-starter.txt`. 25 evidence/working files verified in total.

---

## 4. Central key figures (quick reference)

| Topic | Value | Source |
|---|---|---|
| Cube port (physical) | TCP **62910** | `LocalConfiguration.java:12,81-92` |
| Cube port (virtual/local) | TCP **54321** (only for IP `127.0.0.1` or host IP) | `max.local.properties:22`, `LocalConfiguration.java:81-92` |
| Web UI | `http://localhost:<ServerPort>` | `Application.java:275-277`; port dynamic (`PortFactory`) |
| Servlets | `/dwr/*`, `/controller/*`, `/*.html` | `extracted/rootwar/WEB-INF/web.xml:14-63` |
| Session timeout | 5 min | `web.xml:66-68` |
| Firmware window | 273–275 | `max.local.properties:17-18` |
| Connection timeouts | 30 s connect / 70 s socket | `max.local.properties:23-24` |
| Command queue | 100 | `max.local.properties:21` |
| Watchdog / idle | 5 s / 30 s / 900 s | `LocalApp-al.xml:11-12`, `max.local.properties:4-5` |
| Inclusion mode | 60 s | `max.local.properties:9` |
| Metadata block | 1900 | `max.local.properties:8`, `SendMetadataCubeCommand.java:14-17,33,44` |
| Limits | 32 characters, 20 rooms, 140 devices | `max.local.properties:1-3` |
| Default temperatures | **two levels:** properties 20.0/17.0/21.0/17.0; `Room` constructor 16.5/21.5/12.0 | `max.properties:2-5`; `Room.java:45-48`; `domain-model.md` §3.2 |
| Error codes | MAX0001, MAX1001–MAX1051 | `extracted/essentials/errorcodes.properties` |
| Persistence original | `~/.MAX/*.settings` (java.util.Properties), **no DB** | `SettingsProvider.java:35-76`; search for jdbc/sqlite/@Entity without hits |
| Languages | de, en, fr, it, nl, pl | `extracted/maxlocalapp/MaxLocalAppStarter_*.properties` |

---

## 5. Folder structure of this workspace

```
app-maxcube/
├── docs/          → this wiki + 10 phase documents
├── evidence/      → 25 REA-CLI JSONs, javap output, JAR inventories
├── extracted/     → REA-extracted JAR/WAR contents (configuration, JS, properties)
└── decompiled/    → jadx-decompiled Java source (480 .java files, for analysis)
```

**Note:** `decompiled/` is an intermediate stage of the analysis. The target implementation must **not** copy any code from it; it follows exclusively the technical specifications in `docs/`.
