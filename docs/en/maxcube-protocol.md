> English translation of docs/maxcube-protocol.md. Machine-created; native-speaker review pending.

# MAX! Cube – Communication protocol specification (phase 2 of the REA analysis)

> **Status:** Technical reconstruction from static artifacts. This documentation describes the behavior of the MAX! local backend version `1.4.6` as present in the decompiled Java code and extracted configuration files. It is intended as a basis for a reimplementation without original code.
>
> **Important marking:** All source location references (`path/File.java:line`) refer to **JADX-decompiled code** (marker `/* JADX INFO: loaded from: ... */` in every file). Decompiled code is **not** REA evidence. REA evidence IDs are quoted only where artifact identity or hash is evidenced by the REA CLI.

---

## 1. Sources, artifact identity and evidence

| Artifact | REA evidence | Evidenced identity |
| --- | --- | --- |
| `MaxLocalBackend-1.4.6.jar` | **REA evidence `ev_b01b22f5d2b54d783aa1cb8dbe567111e03a6fe5029e1641318be232168a43a7`** | SHA-256 `693de979b66038ab5359e75564f03e0eacd526d486e5dd3393b64c2872cce8a2`, path `C:\ProgramData\MAX\lib\MaxLocalBackend-1.4.6.jar`, coverage 434/434 container entries (see `evidence/localbackend-rea.json`) |
| `MaxEssentialsBackend-1.4.6.jar` | **REA evidence `ev_cd71f066e83568d1ab3c71c8f78b5ea143ceb6dcf3b32ee7b5329970380de677`** | see `evidence/essentials-rea.json` / `essentials-evidence.json` |
| Root WAR / JS client | **REA evidence `ev_198593f77a5b718fcd92173c539c0b5f7d78752738b9e66ae993003f805d2e67`** | see `evidence/rootwar-rea.json` / `rootwar-evidence.json` |

Further local evidence bundles (not for protocol details, but for proof of extraction): `evidence/localbackend-extract.json`, `evidence/essentials-extract.json`, `evidence/settings-evidence.json`.

**Decompiled source paths used (JADX, not REA evidence):**

- `app-maxcube/decompiled/localbackend/sources/de/eq3/max/il/local/` – `CubeConnection.java`, `CubeGateway.java`, `CubeConnectionCommand.java`, `ICubeConnection*.java`, all 19 classes under `commands/`, `ConnectionWatchDog.java`
- `app-maxcube/decompiled/localbackend/sources/de/eq3/max/al/local/` – `core/LocalConfiguration.java`, `finder/CubeFinder.java`, `finder/CubeConnectionInfo.java`, `update/*.java`
- `app-maxcube/decompiled/essentials/sources/de/eq3/max/al/core/` – `IOperationCodes.java`, `Constants.java`, `Conversion.java`, `radiogram/*.java`, `radiogram/specification/*.java`, `cubestate/*.java`, `cubestate/convert/*.java`, `command/*.java`
- `app-maxcube/decompiled/controller/sources/de/eq3/max/al/local/remoteproxy/` – fully implemented HTTP remote proxy protocol (seven request/response pairs, section 6.1); also evidences AES use (`RemoteProxyRequest.java:40`, `RemoteProxyResponse.java:50-57`)
- Configuration: `app-maxcube/extracted/settings/max.local.properties`, `extracted/localbackend/Default.settings`, `extracted/essentials/errorcodes.properties`
- JS client: `app-maxcube/extracted/rootwar/js/lib/APICalls.js`, `extracted/rootwar/js/models/maxCubeModel.js`

**Limitation:** There is no network PCAP and no firmware binary. All statements about *cube-side* behavior are derived from the client's response expectations (**INFERENCE**, unless explicitly processed as a response in the code).

---

## 2. Transport, ports and discovery

### 2.1 TCP data channel

- Exactly one `java.net.Socket` per connection, with `BufferedReader` (InputStreamReader, platform encoding) and `BufferedWriter` (OutputStreamWriter, platform encoding). Every line ends with `\r\n`; the client reads with `readLine()`.
  Source: `decompiled/localbackend/sources/de/eq3/max/il/local/CubeConnection.java:46-50`.
- Port selection (`LocalConfiguration.getConnectionPort`, `decompiled/localbackend/sources/de/eq3/max/al/local/core/LocalConfiguration.java:81-90`):
  - `62910` (`DEFAULT_CUBE_PORT`, line 12) for real cubes.
  - `max.connection.virtualCubePort` = `54321` (`extracted/settings/max.local.properties:22`) when the IP equals `127.0.0.1` or the host's own IP (`InetAddress.getLocalHost().getHostAddress()`). On `UnknownHostException` the code falls back to `62910`.
- Timeouts (`CubeConnection.java:48,60`; `max.local.properties:21-24`):
  - First read timeout after socket establishment: `max.connection.connectionTimeout` = **30000 ms**.
  - After successful hello: `max.connection.socketTimeout` = **70000 ms**.
  - `max.connection.commandQueueCapacity` = **100**.
- Only **one** connection at a time: `CubeGateway.connections` is `ArrayBlockingQueue<ICubeConnection>(1)` (`CubeGateway.java:52`); a second `connect(ip)` throws `AlreadyConnectedToCubeException("already connected")` (`CubeGateway.java:110-119`).

### 2.2 UDP discovery (CubeFinder)

Source: `decompiled/localbackend/sources/de/eq3/max/al/local/finder/CubeFinder.java`.

| Parameter | Value | Source |
| --- | --- | --- |
| Port | `23272` | `CubeFinder.java:24` |
| Multicast group | `224.0.0.1` | `CubeFinder.java:22` |
| Broadcast | `255.255.255.255` | `CubeFinder.java:23` |
| TTL | `5` | `CubeFinder.java:28,42` |
| Signature (request) | `65 51 51 4D 61 78 2A 00` = `eQ3Max*\0` | `CubeFinder.java:48` (Java literal `"eQ3Max*\u0000**********I"`) |
| Resends | 2 repeats at 50 ms intervals (3 packets total) | `CubeFinder.java:26-27,157-167` |
| Response signature | Regex `^eQ3Max(Ap|Bl)` in the first 8 bytes | `CubeFinder.java:25,89-90` |
| Serial number in response | Bytes 8..17 (10 bytes) | `CubeFinder.java:91` |
| Firmware in response | Bytes 24..25, big-endian 16-bit (`readInt(data,24,2)`) | `CubeFinder.java:92,100-106` |
| Bootloader flag | `signature == "eQ3MaxBl"` | `CubeFinder.java:21,93` |
| Receive buffer | 128 bytes, `setSoTimeout(800)`, `waitTime` from caller | `CubeFinder.java:61,86` |

The client sends the discovery packet **both** to the broadcast and to the multicast address (`CubeFinder.java:47-51`) and repeats the cycle for every active network interface (`CubeFinder.java:112-131`). `CubeUdpProxy` calls `CubeFinder.listCubes(1800)` (`CubeUdpProxy.java:59`).

**Difference from TCP hello firmware encoding:** Discovery delivers firmware as a **decimal integer from 2 bytes** (`readInt`), TCP hello as a **hexadecimal string** (`Integer.parseInt(...,16)`). A value `0x0111` in the TCP hello corresponds e.g. to `273` decimal; the discovery value `261` in `APICalls.js:1243` is a mock example.

---

## 3. Connection establishment and hello (H:)

### 3.1 Flow

1. `CubeGateway.connect(ip)` creates `new CubeConnection(ip, localConfiguration)` and puts it into the connection queue (`CubeGateway.java:110-119`).
2. `new Socket(ip, port)`; `setSoTimeout(30000)`; create reader/writer; create `ArrayBlockingQueue(100)`; `lastAccessTimestamp = now` (`CubeConnection.java:43-52`).
3. **First line must begin with `H:`**, otherwise `CubeCommunicationFailedException("No Incoming Hello")` (`CubeConnection.java:53-54,66`).
4. Body is read with `StringTokenizer(command, ":")` (token after the first `:`). `parseHeader` splits comma-separated: `serial=splitted[0]`, `radioAddress=parseInt(splitted[1],16)`, `firmwareVersion=parseInt(splitted[2],16)` (`CubeConnection.java:55-56,297-302`).
5. Firmware check: `leastCompatibleFirmwareVersion <= fw <= latestCubeFirmwareVersion`, configured as **273..275** (`CubeConnection.java:57`; `max.local.properties:17-18`). On violation: `CubeCommunicationFailedException("incompatible firmware version")` (`CubeConnection.java:64`).
6. On success: `rawCubeState.setHeader(header)`, `connected=true`, `setSoTimeout(70000)`, log `"Cube Connection ready."` (`CubeConnection.java:58-62`).

### 3.2 H: format (extended)

`H:<serial>,<radioAddressHex>,<firmwareHex>[,<f3>,<f4>,<dutyHex>,<freeSlotsHex>,<dateHex>,<timeHex>,<stateCubeTime>,<ntpCounterHex>]`

The first three fields are mandatory for connection setup. `MaxCubeStateFactory.parseHeader` (`decompiled/essentials/sources/de/eq3/max/al/core/cubestate/MaxCubeStateFactory.java:112-145`) additionally interprets:

| Index | Meaning | Encoding | Source |
| --- | --- | --- | --- |
| 0 | Serial number | String | `MaxCubeStateFactory.java:114` |
| 1 | Cube RF address | 6 hex digits (24 bits) | `:115` |
| 2 | Firmware | Hex (e.g. `111` = 273) | `:116` |
| 3, 4 | **UNKNOWN** (not evaluated by the client) | – | – |
| 5 | Duty cycle | Hex, `parseInt(...,16)` | `:120-122` |
| 6 | Free memory slots | Hex | `:123-125` |
| 7 | Date (cube NTP time) | Hex `YYMMDD`, 2 hex digits each → `hexToBytes` | `:126-127,147-156` |
| 8 | Time | Hex `HHMM` | `:127,152-153` |
| 9 | `stateCubeTime` | Decimal | `:135-138` |
| 10 | NTP counter | Hex | `:139-142` |

If indices 7/8 are missing, `cubeDate = null` is set (`MaxCubeStateFactory.java:131-134`). The century is taken from the current calendar (`int century = cubeDate.get(1)/100`, `:154`), i.e. `YY` is interpreted relative to the current century.

**Authentic example (INFERENCE regarding concrete values, format source-evidenced); corrected 24.11.2026: field 7 is hexadecimal `1a0b18` = 26.11.24, not decimal `261124`:**

```text
H:JEQ0001234,1d2a3c,111,00,00,05,1f,1a0b18,0c1e,3,0002
```

### 3.3 State / lifecycle

- `CubeConnection.process()` executes **first** `receiveDeviceAndMetaData()` and then `talkToCube()` (`CubeConnection.java:107-110`). `talkToCube()` blocks in `commandQueue.take()` while `connected == true` (`CubeConnection.java:185-197`). Therefore state import is practically executed **once per connection**, before the first command is processed.
- `receiveDeviceAndMetaData()` reads lines until an `L:` line arrives; `C:` and `M:` lines are sorted into `RawCubeState` beforehand (`CubeConnection.java:157-183`). Other prefixes are ignored during this phase; `null` or unknown lines lead to `NullPointerException` (UNKNOWN how the cube avoids this).
- Worker thread: `CubeGateway` starts a daemon thread `CubeGateway` (`CubeGateway.java:54,56-59`); the decompiled `run()` method is JADX-inconsistent (`CubeGateway.java:63-106`), the bytecode comment shows: `getNextConnection()` → `process()` → on exception `log.error("Error with connection")` → `close()`, then loop. **INFERENCE:** after `close()` the queue is emptied and the next connection is expected.
- Command execution: `execute()` puts the command into the queue, waits on semaphore, forwards errors (`CubeConnection.java:139-155`; `CubeConnectionCommand.java:30-49`). `IOException` and `RuntimeException` are forwarded unchanged, all other `Throwable` are wrapped as `CubeCommunicationFailedException("Unknown Error", cause)`.
- `SocketException` / `SocketTimeoutException` during a command set the command error and call `close()` (`CubeConnection.java:236-241`); `CubeGateway.execute()` additionally calls `dropConnection()` (`CubeGateway.java:304-314`).
- `close()` sets `connected=false`, marks the current command with `CubeCommunicationFailedException()` and notifies it; afterwards all waiting commands are rejected with exception `"command ignored"` (`CubeConnection.java:123-135,249-261`).
- `DisconnectCubeCommand` calls `context.shutdown()` (only `connected=false`, **no** `socket.close()` in the command; `CubeConnection.java:77-79`). The socket is closed only in the next `CubeGateway.run()` pass (INFERENCE from `CubeGateway.java:79-82`).
- Inclusion mode lock: While `isInclusionMode == true`, only `FetchNewDevicesCubeCommand`, `CancelInclusionModeCubeCommand` and `DisconnectCubeCommand` are allowed; all other commands fail with `CubeIsInInclusionModeException` (`CubeConnection.java:264-275`). `SendMetadataCubeCommand` additionally checks itself (`SendMetadataCubeCommand.java:28-30`).
- Watchdog / idle (verified by the commissioner, evidenced on the implementation side):
  - `ConnectionWatchDog.checkConnection()` reports inactivity after `max.idleTimeBeforeLocalAppDisconnect` = **30000 ms** to `IConnectionTimeoutHandler.onConnectionTimeout()` (`ConnectionWatchDog.java:16-17,34-40`; `max.local.properties:4`).
  - `CubeGateway.idleCall()` sends `encrypt("something")` → `e:something\r\n` after `max.cubeIdleTimeToSend` = **900000 ms** without access, but **only** if inclusion mode is not active (`CubeGateway.java:291-301`; `max.local.properties:5`).
  - `connectionWatchDog.checkConnection` and `cubeGateway.idleCall` are called every **5000 ms** (verified by the commissioner; scheduler configuration is not in the read files → **INFERENCE** regarding the concrete bean/annotation).
  - **Note for reimplementation:** 900000 ms > 30000 ms. Whether the watchdog is reset by regular API activity is **UNKNOWN** from the available files (`ConnectionWatchDog.reset()` exists, its callers were not analyzed: `ConnectionWatchDog.java:30-32`).

---

## 4. General message formats and encodings

| Function | Rule | Source |
| --- | --- | --- |
| Opcode | 1 letter + `:` at start of line | `IOperationCodes.java:15-59` |
| Body (simple) | `StringTokenizer(cmd, ":")` → token after first `:` | `CubeConnection.java:199-206`, `AESEncryptCubeCommand.java:31-38` |
| Body (arguments) | `cmd.substring(2).split(",")` | `CubeConnection.java:208-213` |
| Hex | lowercase, 2 digits per byte: `toHex8` → `toHex16` → `toHex24` → `toHex32` | `Conversion.java:11-44` |
| Base64 | `Base64.encodeBase64(bytes,false)` without line breaks | `Conversion.java:73-83` |
| Line ending | `\r\n` for **all** outgoing commands | e.g. `SetUrlCommand.java:17` |
| ACK | `A:\r\n` line; read only when `firmwareVersion > 22` (decimal!) | `CubeConnectionCommand.java:52-59` |

`readAcknowledge()` only expects `startsWith("A:")`; the remaining content is not checked. Since allowed firmware is 273..275 and therefore always > 22, the ACK is **mandatory** for all commands that call `readAcknowledge()`. Exceptions: `AESEncryptCubeCommand`, `AESDecryptCubeCommand`, `FetchNewDevicesCubeCommand`, `ResetGatewayCubeCommand`, `GetConfigurationCubeCommand`, `ListDevicesCubeCommand`, `QueryNTPServerCommand`, `SetNTPServerCommand` (own response logic) and `DeviceSendCommand` (`S:` instead of `A:`).

---

## 5. Opcode universe and resolution of multiply assigned prefixes

Source: `decompiled/essentials/sources/de/eq3/max/al/core/IOperationCodes.java:5-59`.

| Opcode | Direction per constant | Meaning | Implemented in localbackend? |
| --- | --- | --- | --- |
| `H:` | Cube→App | Incoming hello | yes (`CubeConnection.java:54`) |
| `F:` | Cube→App | NTP server response | yes (`QueryNTPServerCommand.java:30`) |
| `L:` | Cube→App | Device list (Base64) | yes (`CubeConnection.java:161`) |
| `C:` | Cube→App | Device/cube configuration | yes (`CubeConnection.java:165`) |
| `M:` | Cube→App | Metadata block | yes (`CubeConnection.java:172`) |
| `N:` | Cube→App | New device (inclusion) | yes (`FetchNewDevicesCubeCommand.java:54`) |
| `A:` | Cube→App | ACK | yes (`CubeConnectionCommand.java:55`) |
| `E:` | Cube→App | Encryption result | yes (`AESEncryptCubeCommand.java:26`) |
| `D:` | Cube→App | Decryption result | yes (`AESDecryptCubeCommand.java:26`) |
| `S:` | Cube→App | Airtime status after radiogram | yes (`DeviceSendCommand.java:34`) |
| `b:` / `B:` | Proxy→App / App→Proxy | Set remote access credentials | **yes, but not on TCP**: `SetRemoteCredentialsRequest.java:10`, `MaxRemoteProxy.java:74` |
| `g:` / `G:` | Proxy→App / App→Proxy | Read credentials | **yes, but not on TCP**: `GetRemoteCredentialsRequest.java:6`, `MaxRemoteProxy.java:89` |
| `j:` / `J:` | Proxy→App / App→Proxy | Remote access on/off | **yes, but not on TCP**: `SetRemoteAccessRequest.java:6`, `MaxRemoteProxy.java:310` |
| `p:` / `P:` and `o:` / `O:` | Proxy→App / App→Proxy | Set/read remote user data | **yes, but not on TCP**: `SetRemoteUserDataRequest.java:14`, `GetRemoteUserDataRequest.java:12`, `MaxRemoteProxy.java:115` |
| `v:` / `V:` | Proxy→App / App→Proxy | Check product activation | **yes, but not on TCP**: `CheckProductActivationRequest.java:6`, `MaxRemoteProxy.java:134` |
| `w:` / `W:` | Proxy→App / App→Proxy | Activate product | **yes, but not on TCP**: `ActivateProductRequest.java:12`, `MaxRemoteProxy.java:167` |
| `u:` | App→Cube | Set URL | yes (`SetUrlCommand.java:17`) |
| `i:` | App→Cube | Set interval | yes (`IntervalCubeCommand.java:23`) |
| `s:` | App→Cube | Send device radiogram | yes (`ClientCommand.java:53`, `DeviceSendCommand.java:30`) |
| `m:` | App→Cube | Send metadata block | yes (`SendMetadataCubeCommand.java:42`) |
| `n:` | App→Cube | Start inclusion mode | yes (`FetchNewDevicesCubeCommand.java:45`) |
| `x:` | App→Cube | Abort inclusion mode | yes (`CancelInclusionModeCubeCommand.java:35`, `CubeGateway.java:140`) |
| `q:` | App→Cube | Quit / disconnect | yes (`DisconnectCubeCommand.java:13`) |
| `e:` | App→Cube | Encrypt | yes (`AESEncryptCubeCommand.java:23`) |
| `d:` | App→Cube | Decrypt | yes (`AESDecryptCubeCommand.java:23`) |
| `a:` | App→Cube | Gateway reset | yes (`ResetGatewayCubeCommand.java:11`) |
| `r:` | App→Cube | Reset radiogram | yes (`ResetDeviceRadiogram.java:21`) |
| `t:` | App→Cube | Delete devices | yes (`DeleteDevicesCubeCommand.java:34`) |
| `c:` | App→Cube | Query configuration | yes (`GetConfigurationCubeCommand.java:26`) |
| `l:` | App→Cube | Query device list | yes (`ListDevicesCubeCommand.java:16`) |
| `f:` | App→Cube | Set/query NTP server | yes (`SetNTPServerCommand.java:22`, `QueryNTPServerCommand.java:20`) |
| `z:` | App→Cube | Wakeup | yes (`WakeupCommand.java:23`) |

### 5.1 Resolved prefix collisions

| Prefix | Context 1 | Context 2 | Resolution rule |
| --- | --- | --- | --- |
| `g:` | `INCOMING_GET_CREDENTIALS` (`IOperationCodes.java:25`) | `OUTGOING_MORE_DATA` (`:38`) | The first is the real `G:` response of the remote proxy (`MaxRemoteProxy.java:89`). The second is a **dead constant** without producers in the Java tree (`UNKNOWN`), not a cube TCP command. |
| `w:` | `INCOMING_ACTIVATE_PRODUCT` (`:30`) | `OUTGOING_SET_PUSHBUTTON_CONFIG` (`:53`) | Two separate protocols: `w:` from the **proxy** = `W:` response (`MaxRemoteProxy.java:167`); `w:` to the **cube** = push-button configuration (`PushButtonConfigurationCubeCommand.java:27`). Do not confuse. |
| `s:` | `OUTGOING_SEND` (`:34`) | `OUTGOING_SEND_DEVICE_CMD` (`:49`) | Both constants identical; `s:` is always App→Cube and carries a radiogram. `S:` (uppercase) is the associated response. |
| `u:` | `OUTGOING_URL` (`:32`) | `OUTGOING_SET_URL` (`:55`) | Both identical; `u:` is always App→Cube (`SetUrlCommand.java:17`). |
| `v:` | `INCOMING_CHECK_PRODUCT_ACTIVATION` (`:29`) | `OUTGOING_TIME_CONFIG` (`:57`) | Two separate protocols: `v:` from the **proxy** = `V:` response (`MaxRemoteProxy.java:134`); `v:` to the **cube** = time/timezone configuration (`AdjustTimezoneConfigurationCommand.java:24`). Do not confuse. |
| `f:` vs `F:` | `f:` = request (App→Cube), `F:` = response (Cube→App) | – | Case distinguishes direction. |
| `e:/E:`, `d:/D:` | lowercase = request, uppercase = response | – | As with `f:/F:`. |
| `l:/L:`, `c:/C:`, `m:/M:` | lowercase = request, uppercase = response | – | As above; `M:` is additionally an unsolicited state stream. |

**Resolved 24.11.2026:** The family `B/G/J/P/O/V/W` including lowercase belongs to the **remote proxy** over HTTP, not to cube TCP. Fully CODE-VERIFIED by `decompiled/*/sources/de/eq3/max/al/local/remoteproxy/` (seven request classes, `MaxRemoteProxy.java:67-175,303-315`, `RemoteProxyRequest.java:27-56`, `RemoteProxyResponse.java:29-58`). The request is Base64-encoded as `H:` + command, encrypted via cube crypto (`e:`) and sent as an HTTP request to the device app URL ("POST" inferred from `setDoOutput(true)` without `setRequestMethod`, not literally in the code); the response returns via `d:`. `b:OK`, `g:<user>,<enabled>`, `o:<value>`, `v:<…>`, `w:<…>` are the response formats evidenced from bytecode, not live measurements. Only `OUTGOING_MORE_DATA` (`g:`, `:38`) remains a dead constant (`UNKNOWN`). The cloud has been shut down since 01.07.2023; the family is deliberately **not** adopted into the local integration. Proof in `SecondBrain/wiki/fragen/2026-11-24-maxcube-remote-proxy-opcodes.md` (including corrections from the Sonnet cross-check).

---

## 6. Complete command table (App → Cube)

All examples show the exact wire string without representable `\r\n` (CRLF is present at the end in each case). "Response" is the line read by the client.

| No. | Command | Syntax | Meaning | Example | Response | Source location |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Identify/hello | `H:` line from cube | Serial number, RF address, firmware, optionally duty/date | `H:JEQ0001234,1d2a3c,111` | – (initial frame) | `CubeConnection.java:53-62` |
| 2 | Query device list | `l:\r\n` | Delivers complete device list | `l:` | `L:<base64>` | `ListDevicesCubeCommand.java:16-24` |
| 3 | Query configuration | `c:<adrHex24>\r\n` | Configuration of a device/cube | `c:1d2a3c` | `C:1d2a3c,<base64>` | `GetConfigurationCubeCommand.java:26-35` |
| 4 | Send metadata | `m:<index2>,<utf8Bytes>\r\n` | Metadata in blocks (1900 bytes/block) | `m:00,{"rooms":[...]}` | `A:` (per block) | `SendMetadataCubeCommand.java:42-51` |
| 5 | Start inclusion | `n:<timeoutHex16>\r\n` | Inclusion for 60 s | `n:003c` | `N:<base64>` | `FetchNewDevicesCubeCommand.java:44-49` |
| 6 | Abort inclusion | `x:\r\n` | Ends inclusion mode | `x:` | `N:` (last line with empty body) or `A:`-independent | `CancelInclusionModeCubeCommand.java:35-48` |
| 7 | Quit/disconnect | `q:\r\n` | Disconnects; client expects **no** line | `q:` | *(none; `readLine()==null`)* | `DisconnectCubeCommand.java:13-19` |
| 8 | Set URL | `u:<url>\r\n` | App URL of the cube | `u:http://www.eq3.de` | `A:` | `SetUrlCommand.java:9-20` |
| 9 | Set interval | `i:<intervalHex32>,<00000000>\r\n` | Update interval; 2nd argument hard-coded `0` | `i:000003e8,00000000` | `A:` | `IntervalCubeCommand.java:23-26` |
| 10 | Send radiogram | `s:<base64(payload)>\r\n` | Device command (control/config/assign/remove) | `s:/wDcAA...` | `S:<dutyHex>,<discarded>,<freeHex>` | `DeviceSendCommand.java:30-38`, `ClientCommand.java:53` |
| 11 | Reset radiogram | `r:01,<base64(adrHigh,adrMid,adrLow)>\r\n` | Resets a device | `r:01,HSo8` (address `1d2a3c`) | `S:...` | `ResetDeviceRadiogram.java:20-38` |
| 12 | Encrypt | `e:<plaintext>\r\n` | Cube performs AES | `e:something` | `E:<base64-cipher>` | `AESEncryptCubeCommand.java:23-28` |
| 13 | Decrypt | `d:<base64-cipher>\r\n` | Cube performs AES | `d:4b9m...` | `D:<base64-plain>` | `AESDecryptCubeCommand.java:23-28` |
| 14–20 | Remote proxy (**not** cube TCP commands) | `B:/G:/J:/P:/O:/V:/W:` | HTTP request to the device app URL, not port 62910 ("POST" only inferred) | – | `b:/g:/j:/p:/o:/v:/w:` | Section 6.1; `MaxRemoteProxy.java:67-175` |
| 21 | Reset gateway | `a:\r\n` | Hardware reset; client reads **no** ACK | `a:` | – | `ResetGatewayCubeCommand.java:11-12` |
| 22 | Delete devices | `t:<countHex8>,<force>,<base64(3N address bytes)>\r\n` | Deletes devices, optionally factory reset | `t:01,1,HSo8` | `A:` | `DeleteDevicesCubeCommand.java:33-39,52-66` |
| 23 | Push-button config | `w:<base64(66 bytes)>\r\n` | Two 33-byte templates | `w:...` | `A:` | `PushButtonConfigurationCubeCommand.java:18,25-29` |
| 24 | Time/timezone config | `v:<base64(24 bytes)>[,<ntpHex>]\r\n` | Winter + summer + optional NTP offset | `v:...`,`v:...,1c` | `A:` | `AdjustTimezoneConfigurationCommand.java:24-31` |
| 25 | Set NTP server | `f:[server1][,server2]\r\n` | Up to two servers | `f:pool.ntp.org,time.eq-3.de` | `F:...` | `SetNTPServerCommand.java:22-31,36-41` |
| 26 | Query NTP server | `f:\r\n` | Reads current servers | `f:` | `F:<raw>` | `QueryNTPServerCommand.java:20-34` |
| 27 | Wakeup | `z:<minHex8>,<A\|D\|G>[,<target>]\r\n` | Wake device/group/all | `z:1e,D,1d2a3c` | `A:` | `WakeupCommand.java:23-32` |
| 28 | Query state (local) | *(no wire command)* | `FetchGatewayStateCubeCommand` only copies `RawCubeState` into `MaxCubeState` | – | – | `FetchGatewayStateCubeCommand.java:19-20` |

### 6.1 Remote proxy protocol (CODE-VERIFIED, not implemented)

The seven uppercase opcodes and `g:` belong to a **second protocol**: HTTP request to the device app URL of the (shut-down) MAX! cloud ("POST" inferred from `setDoOutput(true)`). The uppercase line is never sent directly to port 62910; its Base64 payload does, however, travel as an `e:` argument over TCP.

**Transport:** `H:<serial>,<rfHex>,<fwHex>\r\n` + command line → Base64 → cube crypto `e:` → Base64-decoded as HTTP body; headers `Opt` (manufacturer namespace, value not repeated here) and `MAX-Serial`. Response: Base64 → cube crypto `d:` → Base64 → line parser `line[0]` / `line.substring(2).split(",")` (`RemoteProxyRequest.java:27-56`, `RemoteProxyResponse.java:29-58`, `MaxRemoteProxy.java:182-190`). **Clarification:** `e:`/`d:` are real TCP commands to the cube; the Base64 payload travels as the `e:` argument over port 62910, the uppercase line itself does not. "POST" is inferred from `setDoOutput(true)` without `setRequestMethod`.

| Opcode | Payload | Response | Source |
| --- | --- | --- | --- |
| `B:` | `<user>,<MD5(password+serial)>,<'0'\|'1'>,<password>` | `b:OK` otherwise `RemoteUserAlreadyExists` | `SetRemoteCredentialsRequest.java:10,13-21`, `MaxRemoteProxy.java:74-77` |
| `G:` | – (`G:\r\n`) | `g:<user>,<true\|false>` | `GetRemoteCredentialsRequest.java:6`, `MaxRemoteProxy.java:89-94` |
| `J:` | `<1\|0>` | `j:OK` (check is empty if-block) | `SetRemoteAccessRequest.java:6`, `MaxRemoteProxy.java:310-313` |
| `P:` | `<key>,<value>`, key ≤ 64 `[_a-zA-Z0-9]*`, value ≤ 1024 without CR/LF/comma | no evaluation | `SetRemoteUserDataRequest.java:7-14,18-28` |
| `O:` | `<key>` (validated as above) | `o:<value>` or `""` | `GetRemoteUserDataRequest.java:7-17`, `MaxRemoteProxy.java:115-119` |
| `V:` | – (`V:\r\n`) | `v:` with 7 fields + optional Base64 info texts | `CheckProductActivationRequest.java:6`, `MaxRemoteProxy.java:134-150` |
| `W:` | `<productKey>`, `[A-Z0-9-]*` ≤ 40 | `w:<activated>,<expiry>,<days>,<criteriaKeyViolation>` | `ActivateProductRequest.java:7-17`, `MaxRemoteProxy.java:167-173` |
| `g:` | operative `G:` response (see above); `OUTGOING_MORE_DATA` (`:38`) is **dead** | – | `IOperationCodes.java:25,38` |

**Decision for the integration:** deliberately **not** adopted – cloud shut down since 01.07.2023, no access data/product keys in the app, no remote interface. An unexpected `b:/g:/j:/p:/o:/v:/w:` **as a response line** on port 62910 is an unknown opcode and leads to `UnknownCubeOpcodeException` (`CubeCommands.cs:58-60`), not to success. `v:` and `w:` remain valid requests (`CubeCommands.cs:309,338`). Full proof: `SecondBrain/wiki/fragen/2026-11-24-maxcube-remote-proxy-opcodes.md`.

**Ordering guarantee:** There is exactly one worker (`CubeGateway` thread) and one queue; commands are processed in FIFO order (`CubeConnection.java:215-222`, `CubeGateway.java:52-59`). `execute()` is synchronous (semaphore, `CubeConnectionCommand.java:30-49`).

---

## 7. Detailed description of selected commands

### 7.1 `l:` / `L:` – device list

- Request `l:\r\n`, response must start with `L:`, otherwise `CubeCommunicationFailedException("device list expected")` (`ListDevicesCubeCommand.java:19-24`).
- Body is decoded as Base64 (`DeviceStateSerializer.java:40`). Structure per device:
  - 1 byte `size` (length of the following device block)
  - 3 bytes RF address, big-endian (`Conversion.parseInteger(...,3)`, `:51`)
  - 1 byte `erroneousFrameType` (`:52`)
  - 1 byte `stateInformation` (`:53`): bit 4 = valid, bit 1 = initialized, bit 3 = RF error (`:31-33,61-63`)
  - `size - 5` bytes device-specific state (`:54,181-188`)
- `createRadioState()` maps RF error + frame type code to a `RadioState` via `FrameType.getRadioState()` (`DeviceStateSerializer.java:204-213`).

### 7.2 `c:` / `C:` – configuration

- Request `c:<6 hex digits>\r\n`; response `C:<adrHex>,<base64>`; the client takes `line.substring(2).split(",")[1]` (`GetConfigurationCubeCommand.java:30-33`).
- Decoding (`DeviceConfigurationSerializer.java:46-71`): 1 byte (uninterpreted) → 3 bytes address → 1 byte device type → 1 byte group address → 1 byte firmware → 1 byte test result → 10 bytes serial number (ISO-8859-1, `:97-101`) → remainder = device-specific configuration.
- Device types (`DeviceType.java:5-10`): `1` HeatingThermostat, `2` HeatingThermostatPlus (internally normalized to HeatingThermostat, `DeviceConfigurationSerializer.java:54-56`), `3` WallMountedThermostat, `4` ShutterContact, `5` PushButton, `256` Invalid.
- For the **cube itself**, the same `C:` line with the RF address from the header is used; `CubeConfigurationSerializer.java:69-73` decodes it and reads fixed offsets:
  - Offset 19 / 52: push-button configuration (`KEY_CONFIG_LENGTH=11`, `:23-29`),
  - Offset 85..128: app URL, ends at byte ≤ 32 (`:34-35,148-159`),
  - Offset 214 / 226: winter/summer time configuration (`:26-27,41-43,50-62`).

### 7.3 `m:` / `M:` – metadata

**App → Cube (`m:`):**

- `metadataBlockSize` = **1900 bytes** (`max.local.properties:8`).
- `index` is two-digit **decimal** (`"0"+packages`, `SendMetadataCubeCommand.java:37-41`), payload is raw UTF-8 bytes (no Base64!), followed by `\r\n`; an `A:` is read **after every block** (`:49-51`).
- Block count = `ceil(byteLength / 1900)` (`:32-35`).

**Cube → App (`M:`):**

- `M:<indexHex>,<countHex>,<payload>`; index and count are read with `parseInt(...,16)` (`CubeConnection.java:172-180`).
- `Metadata` holds at most **16 blocks** (`MAX_BLOCK_COUNT=16`, `Metadata.java:10-18`); `setBlock(index,count,data)` sets the total count and replaces the block (`:43-46`); `getBytes()` concatenates the first `count` blocks (`:28-37`).
- The concatenated bytes are interpreted as Base64 and deserialized into `rooms`, `devicesNotAssociated`, `daylightSaving` (`MetaDataSerializer.java:40-43,47-53`).

**Blocking:** During inclusion mode, `m:` is forbidden – double check in `CubeConnection.throwExceptionIfNotAllowedToExecute()` (`CubeConnection.java:264-275`) and in `SendMetadataCubeCommand.execute()` (`SendMetadataCubeCommand.java:28-30`).

### 7.4 `n:` / `x:` / `N:` – inclusion mode

- Start: `n:<Conversion.toHex16(inclusionModeTimeoutSeconds)>\r\n` = `n:003c` at 60 s (`FetchNewDevicesCubeCommand.java:45`; `max.local.properties:9`); then `setInclusionMode(true)` (`:48`).
- Waiting: exactly **one** line is read; it must be `N:`, otherwise `InvalidArgumentsException("N: expected, but found ...")` (`FetchNewDevicesCubeCommand.java:52-65`).
- `N:<base64>` decodes to: 1 byte device type, 3 bytes RF address, 10 bytes serial number (`NewDeviceSerializer.java:29-49`). `HeatingThermostatPlus` → `HeatingThermostat` (`:37-39`).
- Empty body (`N:` without token) ends the mode (`setInclusionMode(false)`), `FetchNewDevicesCubeCommand.java:59-62`.
- Abort special case: If `FetchNewDevicesCubeCommand` is currently the active command, `CubeGateway.cancelInclusionMode()` writes `x:` **directly** into the writer, bypassing the queue (`CubeGateway.java:137-145`). Otherwise `CancelInclusionModeCubeCommand` is queued; it writes `x:` and reads `N:` lines until the mode becomes false (`CancelInclusionModeCubeCommand.java:31-53`).
- After disconnect, the new-device list is reset (`CubeGateway.java:187-192`).

### 7.5 `s:` / `S:` – radiograms and duty cycle

- Request: `deviceRadiogram.getOpCode() + Conversion.encodeBase64(payload) + "\r\n"` (`DeviceSendCommand.java:30`).
- Response: `S:<dutyCycleHex>,<commandDiscarded>,<freeMemorySlotHex>`; **three** arguments, otherwise `InvalidArgumentsException("invalid arguments")` (`:42-49`).
  - `dutyCycle = parseInt(args[0],16)`
  - `commandDiscarded = args[1].charAt(0) == '1'`
  - `freeMemorySlot = parseInt(args[2],16)`
  - With `commandDiscarded == true`: `CubeCommunicationFailedException("Command discarded")` (`:36-38`).
- Afterwards `MaxCubeAirtimeStatus(dutyCycle, discarded, freeSlots, null)` is created and only in `CubeGateway.commitDeviceRadiogram()` provided with `cubeState.getCubeDate()` (`DeviceSendCommand.java:49,66-69`; `CubeGateway.java:165-170`).
- Duty cycle is additionally read from the `H:` header (index 5) (`MaxCubeStateFactory.java:120-122,143-144`).

### 7.6 `w:` – push-button configuration

- Template: 33 bytes, `DATA_OFFSET = 11` (`PushButtonConfigurationCubeCommand.java:17-18`). Two templates (upper, lower key) → 66 bytes Base64.
- Byte 11 = `(mode << 6) | temperature` (`:42-49`).
- Mode mapping (`PushButtonKeyMode`): `Auto`/`AutoWithTemperature` → 0; `Manual` → 1; `Eco` → 1; `Comfort` → 1 (`:58-76,80-88`).
- Temperature encoding (`:90-102`): `AutoWithTemperature`/`Manual` → `(int)(temp*2) & 63`; `Eco` → 1; `Comfort` → 2; `Auto` → 0. Decoding is mirrored in `CubeConfigurationSerializer.java:75-101`.

### 7.7 `v:` – time/timezone configuration

- Payload per zone 12 bytes (`AdjustTimezoneConfigurationCommand.java:41-53`): name 5 bytes (padded with `0`), `month`, `dayOfWeek.getValue() % 7`, `hourOfDay`, `offsetToUTC` as 4 bytes big-endian (`>>24,>>16,>>8,>>0`).
- Order: **winter first, then summer** (`:34-39`).
- Optional `,` + `Integer.toHexString(ntpUTCSeconds)`, only when `ntpUTCSeconds > 0` (`:26-28`).
- Response `A:` via `readAcknowledge()` (`:31`).

### 7.8 `f:` / `F:` – NTP

- Setting: `f:<server1>[,<server2>]\r\n`; servers are written only when `length() > 1` (`SetNTPServerCommand.java:23-28`). Response must start with `F:`, otherwise `CubeCommunicationFailedException("ACK of NTP server settings expected")` (`:36-41`).
- Query: `f:\r\n`; response is stored **including** the `F:` prefix in `ntpServers` (`QueryNTPServerCommand.java:28-34`). The parsing logic for the content is not in the read files → **UNKNOWN**.
- NTP time in header: see section 3.2 (indices 7/8/9/10).

### 7.9 `z:` – wakeup

- `z:<hex8(duration)>,<context>[,<destination>]\r\n` (`WakeupCommand.java:23-29`).
- Context codes (`:48-64`): `A` = all (`destination=0`, no third field), `D` = device (`hex24`), `G` = group/room (`hex8`).
- Default duration `DEFAULT_WAKE_UP_TIME = 30` minutes → `1e` (`:10,35-45`). `wakeupAllDevices()` is implemented but not made available via `CubeGateway` (`CubeGateway.java:155-162`).
- Response `A:` (`:32` → `readAcknowledge`).

### 7.10 `t:` – delete devices

- `t:<hex8(count)>,<force 0|1>,<base64(count*3 address bytes)>\r\n` (`DeleteDevicesCubeCommand.java:33-38,42-67`).
- Address bytes big-endian: `(a>>16)&255, (a>>8)&255, a&255` (`:56-61`).
- Response `A:` (`:39`).

---

## 8. Radiogram binary format (`s:` / `r:`)

### 8.1 Header specification

Source: `ClientCommand.java:39-54`, `AbstractRoomClientCommand.java:45-50`, `AbstractRoomDeviceClientCommand.java:77-79`.

```text
addByte(2, 0, frameTypeCode)          // Byte 2, FrameType.getCode()
addDeviceAddress(6, 0, receiverAddr)  // Byte 6..8, 24 Bit
```

Group commands add:

```text
addBoolean(1, 2, true)   // Broadcast-Flag
addBoolean(1, 1, false)
addByte(9, 0, roomId)
```

`RadiogramSpecification.createPayload()` (`RadiogramSpecification.java:114-126`) writes bits into an LSB-first bit field: bit `i` → `bytes[i/8] |= (1 << (i%8))`. Integers are rendered via `ByteUtil.fromInt(value, bitLength)` (`ByteUtil.java:8-21`):

- `reorderInt(value, byteLength, LITTLE_ENDIAN)` produces **big-endian byte order in the byte array** (`ByteUtil.java:23-33`: `b[i] = (value >>> ((len-1-i)*8)) & 255`; reverse only for `BIG_ENDIAN`).
- Within each byte, bits are transferred LSB→MSB into the BitSet (`ByteUtil.java:11-19`).
- 24-bit addresses: `DEVICEADDRESS_RENDERER` → `fromInt(value,24)` (`RadiogramSpecification.java:66-71`).

### 8.2 FrameType table

Source: `decompiled/essentials/sources/de/eq3/max/al/core/radiogram/FrameType.java:9-34`.

| Name | Code (hex) | Category |
| --- | --- | --- |
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

### 8.3 Renderer and field encoding

|Renderer|Rule|Source|
|---|---|---|
|Boolean|1 bit, `BitSet.set(0,value)`|`RadiogramSpecification.java:20-27`|
|Byte|8 bits, LSB-first|`:28-33`|
|Int|`length` bits, LSB-first per byte|`:34-39`|
|String|Bytes, LSB-first per byte, length `length` or `strlen`|`:40-65`|
|DeviceAddress|24 bits|`:66-71`|
|RoomControlMode|2 bits: Auto=`00`, Permanently=`10`, Temporary=`01`|`:72-94`, `RoomControlMode.java:5-8`|
|Temperature|6 bits: `off`→9, `on`→61, valid→`(int)(value*2)`|`:96-111`|
|Time (30 min)|6 bits: `hour*2 + minCount`; `min>=45`→hour++, `15<min<45`→minCount=1|`:193-205`|
|Time (5 min)|8 bits: `hour*12 + minute/5`; `0`→`288`; `>255`→set bit at `bytePos-1` and subtract 256|`:207-219`|
|Offset temperature|4 bits: `(int)((value+3.5)*2)`|`:222-226`|
|Window-Open-Time|4 bits: `value/5`|`:228-230`|
|Day / Month / Year|5 bits `Calendar.DAY_OF_MONTH`; 4 bits `month+1`; 7 bits `year-2000`|`:233-249`|
|WeekDay|3 bits `(value+1)%7`|`:281-285`|
|BoostTime|3 bits: `value<=30 ? value/5 : 7`|`:287-290`|

**Example `SetRoomTemporaryMode`** (`SetRoomTemporaryMode.java:46-48`):

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

**Weekly profile example** (`SetRoomWeekTemperatureProfile.java:89-100`): one telegram per day with `FrameType 0x10`, `weekDay` at byte 10 bits 0..2, `telegram==1` at byte 10 bit 4, switching points from byte 11/12 (pairs of 6-bit temperature starting at byte 11 bit 1 and **9-bit time**: 8 bits in byte 12, 9th bit as overflow into byte 11 bit 0, cf. `:404`); with >7 switching points a second telegram with 6 points (`:81-84`). The byte comparison against the Python encoding is evidenced in `byte-abgleich-python-java.md`.

**Reset radiogram `r:`** (`ResetDeviceRadiogram.java:20-38`, `DeleteDevicesCubeCommand.java:52-66`): opcode is literally `r:01,`, payload = three address bytes big-endian, Base64-encoded. For `0x1d2a3c` → `r:01,HSo8\r\n` (bytes `1d 2a 3c`; **correction 24.11.2026:** an older version of this table named `Ha0/PA==`, which encodes four bytes and thus does not match the specification). Afterwards response `S:...` (same path as `s:`).

**Mapping of client commands to radiograms:** `TransmissionRadiogramBuilder.createDeviceRadiograms()` validates the command and creates the list (`TransmissionRadiogramBuilder.java:12-16`); `ResetRadiogramBuilder` prepends `r:01,…` reset telegrams where needed (`ResetRadiogramBuilder.java:26-33,43-63`). Each client command is assigned to a category via `@ClientCommandInfo(scope=...)` (HouseConfiguration, RoomControl, RoomConfiguration, DeviceConfiguration, DeviceAssociation, DeviceRemoval; `ResetRadiogramBuilder.java:142-167`).

---

## 9. AES encryption

- The client implements **no** AES encryption of its own; it delegates to the cube:
  - `e:<plaintext>\r\n` → `E:<body>\r\n` (`AESEncryptCubeCommand.java:23-28`)
  - `d:<ciphertext>\r\n` → `D:<body>\r\n` (`AESDecryptCubeCommand.java:23-28`)
  - `getOpCodeBody()` splits at `:` **not** at `,` – Base64 content with commas would be truncated (INFERENCE from `StringTokenizer(cmd,":"`; Base64 normally contains no commas).
- Purpose: remote proxy. `RemoteProxyRequest.java:40` encrypts the Base64 of the request body; `RemoteProxyResponse.java:50-57` decrypts the response and then decodes Base64.
- Idle keepalive: `CubeGateway.idleCall()` sends `e:something\r\n` after 900000 ms (`CubeGateway.java:291-301`).
- Error codes from `errorcodes.properties`: `AESKeyInvalidLength=MAX1001`, `AESKeyIsNotHex=MAX1002`, `EncryptionFailed=MAX1017`. Validation of the AES key lies outside the files read here → **UNKNOWN** whether these errors are generated by the cube or the client.
- **UNKNOWN:** Algorithm, mode, padding, key length and key source are not apparent from the local code. The cube processes the bytes internally.

---

## 10. Time, NTP and summer/winter time

1. **Cube time in header:** `H:` fields 7/8 (`YYMMDD` / `HHMM`, hex-encoded bytes) → `Calendar(Locale.GERMANY)`, century from current date (`MaxCubeStateFactory.java:147-156`).
2. **State time:** field 9 (`stateCubeTime`, decimal) and field 10 (`ntpCounter`, hex) (`:135-142`).
3. **Set timezone:** `v:` command (section 7.7); winter and summer time are transmitted separately, `maxCubeState.setDaylightSaving(!winterConfig.equals(summerConfig))` (`CubeConfigurationSerializer.java:41-43`).
4. **NTP servers:** `f:` set/query (section 7.8).
5. **Client API:** `APICalls.prototype.adjustTimeZoneConfiguration` (`APICalls.js:1914-1934`), `setTimeZoneConfiguration` (`:1942-1962`), `autoSetupTimeZoneConfiguration` (`:1968-1988`), `setCubeTime` (`:1990-2010`).
6. **Duty/free slots** from `H:` feed into `MaxCubeAirtimeStatus` (section 7.5).
7. **UNKNOWN:** The exact meaning of `stateCubeTime` and `ntpCounter` and the format of the `F:` response are not evidenced.

---

## 11. Firmware update over UDP

### 11.1 Opcode table

Source: `decompiled/localbackend/sources/de/eq3/max/al/local/update/CubeUdpOpcode.java:5-14`.

| Opcode | Name | Direction | Payload (from `CubeUdpRequestFactory.java`) | Source |
| --- | --- | --- | --- | --- |
| `I` | Identify | App→Cube | – | `CubeUdpRequestFactory.java:21-27` |
| `N` | GetNetworkAddress | App→Cube / broadcast/multicast | – | `:17-19` |
| `B` | EnterBootloader | App→Cube | – | `:29-31` |
| `U` | InitUpdate | App→Cube | – | `:33-35` |
| `W` | WriteFrame | App→Cube | `indexHigh,indexLow,lengthHigh,lengthLow,data` | `:49-56` |
| `R` | Reboot | App→Cube | – | `:37-39` |
| `c` | GetNetworkConfiguration | App→Cube | – | `:41-43` |
| `C` | SetNetworkConfiguration | App→Cube | 5×4 IP bytes + DHCP flag + encryption flag | `:45-47` |
| `h` | GetUrl | App→Cube | – | `:58-60` |
| `H` | SetUrl | App→Cube | `portHigh,portLow,hostBytes,0x2C(','),pathBytes` | `:62-65` |

**Important:** `CubeUdpOpcode.H` is **not** the TCP hello (`H:`), but the UDP opcode for setting the URL. Context (UDP vs. TCP line protocol) distinguishes the meanings.

### 11.2 Datagram format

**Request** (`CubeUdpRequest.java:16-28,93-104`):

```text
8 Byte  Signatur: 65 51 33 4D 61 78 2A 00  -> "eQ3Max*\0"
10 Byte Seriennummer (mit '*' aufgefüllt)
1 Byte  Opcode (ASCII)
n Byte  opcode-spezifische Payload
```

Note: The Java source shows `new byte[]{101, 81, 51, 77, 97, 120, 42, 0}` (line 94); these are `e Q 3 M a x * NUL`. In the hex above `4D`=77 (`M`) is correct; the discovery signature in `CubeFinder.java:48` is `eQ3Max*`.

**Response** (`CubeUdpResponse.java:10-33,59-94`):

```text
8 Byte  Signatur (nur Bytes > 32 werden als Zeichen übernommen)
10 Byte Seriennummer
1 Byte  ID '>'   (0x3E)
1 Byte  Opcode (muss zum Request passen)
n Byte  Daten
```

Validation (`CubeUdpResponse.java:96-110,129-166`): ID == `'>'`; serial number empty or equal to the expected one; opcode equal; for `N` at least 4 data bytes and not all null; for `W` three bytes status/index:

- `data[0] == 0` → `ConnectionLostUpdateException("write frame: failed")`
- `data[0] == 1` → OK
- `data[0] == 2` → OK (restart)
- `data[1]<<8 | data[2]` must match the requested frame index (`:141-161`).

### 11.3 Transport parameters and flow

| Parameter | Value | Source |
| --- | --- | --- |
| Port | `23272` | `CubeUdpProxy.java:25` |
| Buffer | `1024` bytes | `:24` |
| Timeout | `2000 ms` | `:26,51` |
| Max. attempts | `5` (each with 10 ms lead-in) | `:23,32,211-225` |
| Reboot timeout | `20000 ms` | `:27,131-139` |
| Multicast / broadcast | `224.0.0.1` / `255.255.255.255`, TTL 5 | `:28-31,45-50` |
| Fallback | Unicast↔broadcast and multicast↔broadcast on error | `:187-208` |

Flow (`CubeUpdater.java:27-66`):

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

`CubeMode.parse(signature)` (`CubeMode.java:9-17`): `eQ3MaxAp` → application, `eQ3MaxBl` → bootloader, otherwise offline.

**Firmware file:** ASCII hex. `readLength()` reads two hex digit pairs as `high<<8|low`; then `length` bytes, each as two hex digits (`FirmwareFile.java:57-82`). At `length <= 0` the frame list ends (`:49-55`). Parsing errors → `InvalidFirmwareFileUpdateException` (`:27-32`).

**GetUrl response:** `data[0]<<8|data[1]` = port; from byte 2 `host,path` (comma-separated) → `http://<host>:<port><path>` (`CubeUdpProxy.java:94-108`).

---

## 12. Error codes and Java exceptions

### 12.1 `errorcodes.properties` (`extracted/essentials/errorcodes.properties:1-53`)

| Code | Symbol | Code | Symbol |
| --- | --- | --- | --- |
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
| MAX1011 | DeserializationFailed | **MAX10038** | RemoteServiceUnavailable (conspicuous 5-digit number, source-evidenced) |
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

### 12.2 Java exceptions in the protocol path

| Exception | Trigger | Message | Source |
| --- | --- | --- | --- |
| `CubeCommunicationFailedException` | Wrong first line | `"No Incoming Hello"` | `CubeConnection.java:66` |
| `CubeCommunicationFailedException` | Firmware outside 273..275 | `"incompatible firmware version"` | `:64` |
| `CubeCommunicationFailedException` | Error without IOException/RuntimeException | `"Unknown Error"` | `:150` |
| `CubeCommunicationFailedException` | Queue emptied on close | `"command ignored"` | `:258` |
| `CubeCommunicationFailedException` | RX `l:` expected | `"device list expected"` | `ListDevicesCubeCommand.java:24` |
| `CubeCommunicationFailedException` | RX `c:` expected | `"configuration expected"` | `GetConfigurationCubeCommand.java:35` |
| `CubeCommunicationFailedException` | RX `A:` expected | `"ACK expected"` | `CubeConnectionCommand.java:56` |
| `CubeCommunicationFailedException` | RX `F:` expected | `"ACK of NTP server settings expected"` | `SetNTPServerCommand.java:39`, `QueryNTPServerCommand.java:31` |
| `CubeCommunicationFailedException` | RX `S:` expected | `"S: expected"` | `DeviceSendCommand.java:34` |
| `CubeCommunicationFailedException` | Radiogram discarded | `"Command discarded"` | `DeviceSendCommand.java:37` |
| `CubeCommunicationFailedException` | RX unexpectedly present at `q:` | `"disconnect expected"` | `DisconnectCubeCommand.java:17` |
| `InvalidArgumentsException` | RX `N:` expected | `"N: expected, but found ..."` | `FetchNewDevicesCubeCommand.java:64` |
| `InvalidArgumentsException` | `S:` has != 3 arguments | `"invalid arguments"` | `DeviceSendCommand.java:44,62` |
| `NotConnectedToCubeException` | Command without connection | – | `CubeConnection.java:154`, `CubeGateway.java:222` |
| `AlreadyConnectedToCubeException` | Second `connect()` | `"already connected"` | `CubeGateway.java:116` |
| `CubeIsInInclusionModeException` | Command during inclusion | `"cube is in inclusion mode"` | `CubeConnection.java:266`; `CubeIsInInclusionModeException.java:10` |
| `SocketTimeoutException`, `SocketException` | Socket timeout/error | – | `CubeConnection.java:236-241`, `CubeGateway.java:307-313` |
| `ConnectionLostUpdateException` | UDP update failed | `"write frame: failed"` / `"write frame: unknown error %d"` | `CubeUdpResponse.java:154,160` |

All `Cube*Exception` classes inherit from `CubeException` → `ApplicationException` (`CubeException.java:5`).

---

## 13. Authentic sequence (connect → L:/C:/M: → command → A:)

The following sequence is reconstructed from the receive and send paths. Concrete payload values are **examples (INFERENCE)**; order and opcode sequence are source-evidenced.

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

**Inclusion variant:**

```text
[App]  n:003c
[Cube] N:<base64: type=4, address, serial>
[App]  x:                     (direkt oder als CancelInclusionModeCubeCommand)
[Cube] N:                     (leerer Body -> Inclusion-Mode aus)
```

**Metadata-send variant:**

```text
[App]  m:00,<1900 Bytes JSON>     -> [Cube] A:
[App]  m:01,<rest>                -> [Cube] A:
```

---

## 14. Implementation notes for a reimplementation

1. **Parser:** Line-oriented, CRLF, `startsWith(opcode)`; never use `String.split(":")` for the body (Base64 can contain `=`, but no `:`; `A:` body is ignored anyway). For `C:`/`M:`/`S:` use `substring(2).split(",")`.
2. **Order:** After the hello, mandatorily collect `C:`/`M:` lines until `L:` arrives; only then send commands.
3. **Serialization:** All hex values lowercase, fixed width (8/16/24/32 bits) according to `Conversion.toHexXX`; Base64 without newlines.
4. **ACK obligation:** Only for commands that call `readAcknowledge()` (section 4). `q:`, `a:`, `c:`, `l:`, `e:`, `d:`, `n:`, `f:` have their own response rules.
5. **Concurrency:** One socket, one writer, one queue, one worker; never write two commands simultaneously.
6. **Radiograms:** Bit order LSB-first within bytes, byte order big-endian in the `reorderInt(..., LITTLE_ENDIAN)` path (counterintuitive, but source-evidenced, `ByteUtil.java:23-33`).
7. **Firmware limit:** 273..275 (`0x111`..`0x113`). For version comparisons in the ACK path, `> 22` (decimal) is checked; this is always true for allowed firmware.
8. **Port selection:** IP `127.0.0.1` or own host IP → 54321, otherwise 62910.
9. **UDP update:** separate from the TCP channel, port 23272, 5 retries at 2 s each, validate frame index in `W` response.

---

## 15. Explicit unknowns and inferences

| Topic | Status | Note |
| --- | --- | --- |
| Cube-side implementation of the TCP server | UNKNOWN | No firmware/no PCAP; everything derived from client expectations |
| `H:` fields 3 and 4 | UNKNOWN | Not evaluated by the client (`MaxCubeStateFactory.java:120` starts at index 5) |
| `stateCubeTime`, `ntpCounter` | UNKNOWN | Only stored, not interpreted (`MaxCubeStateFactory.java:135-142`) |
| `F:` response format | UNKNOWN | Only `startsWith("F:")` checked; parsing not in read files |
| `OUTGOING_MORE_DATA` (`g:`) | UNKNOWN | Dead constant (`IOperationCodes.java:38`); no producer in Java tree. The operative `g:` response is clarified (section 6.1) |
| AES algorithm/key | UNKNOWN | Delegation to cube; no key found in local code |
| Remote proxy `B/G/J/P/O/V/W` including responses | CODE-VERIFIED, deliberately not adopted | Fully implemented in `decompiled/*/remoteproxy/`; but HTTP cloud protocol, not cube TCP. Cloud shut down since 01.07.2023 (section 6.1) |
| 5000-ms scheduler for watchdog/idleCall | INFERENCE | Verified by the commissioner; scheduler bean not in analyzed files |
| `CubeGateway.run()` loop | INFERENCE | JADX decompilation inconsistent (`CubeGateway.java:63-106`); flow derived from bytecode comment |
| `SetUrlCommand` default | source-evidenced | `http://www.eq3.de` (`SetUrlCommand.java:9`) |
| `infiniteInterval` (-1) | UNKNOWN | Configured (`max.local.properties:7`), but no caller visible in `IntervalCubeCommand` |
| Concrete values in examples | INFERENCE | Formats source-evidenced, addresses/serial numbers freely chosen |
| `RemoteServiceUnavailable=MAX10038` | source-evidenced | Presumably a typo in the original, not corrected (`errorcodes.properties:40`) |
| `ShutterConactState`, `HeatungThermostatState` | source-evidenced | Typos in `FrameType.java:20,28` deliberately adopted |
| REA scan limitation | source-evidenced | REA report notes that non-entry ZIP bytes are not captured (`evidence/localbackend-rea.json`, `limitations`) |

---

*End of specification. All line numbers refer to the JADX-decompiled sources under `app-maxcube/decompiled/` or the extracted configuration files under `app-maxcube/extracted/`; artifact hashes and evidence IDs come from the REA CLI bundles under `app-maxcube/evidence/`.*
