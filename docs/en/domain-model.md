> English translation of docs/domain-model.md. Machine-created; DE/EN reviewed by the user on 27.11.2026; FR/IT/NL/PL feedback expected via issues/PRs.

# Domain model – MAX! Cube Local Application

Technical specification of the domain objects of the decompiled MAX! application (versions: `MaxEssentialsBackend-1.4.6.jar`, `MaxLocalBackend-1.4.6.jar`, `MaxLocalApp.jar`). The basis is exclusively the locally available Java sources, properties files and browser JS sources under `extracted/rootwar/`. The goal is to reconstruct the domain model as a basis for a *reimplementation*, not a 1:1 port of the existing classes.

## 0. Reading convention, source basis and marking

- **FACT**: directly evidenced by a source location. Every statement carries a path with a line number, e.g. `Room.java:43-64`.
- **INFERENCE**: derived from several evidenced facts, but not explicitly stated in the code.
- **UNKNOWN**: cannot be determined from the available material; must not be presented as an example value or behavior.

Three representation levels are distinguished:

| Level | Package / location | Purpose | Evidence |
| --- | --- | --- | --- |
| AL (Application Layer) | `de.eq3.max.al.core.cubestate`, `de.eq3.max.al.core.command` | Domain model, validation, state management | `decompiled/essentials/sources/de/eq3/max/al/core/cubestate/Room.java:14`, `decompiled/essentials/sources/de/eq3/max/al/core/command/ClientCommand.java:1` |
| DTO (Client Transport) | `de.eq3.max.cl.dto` | Serializable transport objects for DWR/REST to the browser UI | `decompiled/essentials/sources/de/eq3/max/cl/dto/MaxCubeState.java:9-21` |
| Browser model | `js/models/maxCubeModel.js`, `js/com_structure/models/localModel.js`, `js/com_configuration/models/remoteModel.js` | Client-side management of state and DWR calls | `extracted/rootwar/js/models/maxCubeModel.js:17-37` |

The AL level is authoritative for the domain. The DTO level is a deliberately greatly simplified image; the browser level adds its own derivations (e.g. device type from the serial number, `maxCubeModel.js:90-95`).

---

## 1. Aggregate root: Cube / Gateway (`MaxCubeState`)

### 1.1 Purpose

`MaxCubeState` is the aggregate root of the entire heating state. It holds devices, rooms, gateway identity, firmware, remote access, time information and the push-button configuration together.

**Evidence:** `decompiled/essentials/sources/de/eq3/max/al/core/cubestate/MaxCubeState.java:12-33` (class and fields), `.../IMaxCubeState.java:1` (interface).

### 1.2 Properties, types and defaults

| Property | Type | Default / note | Source |
| --- | --- | --- | --- |
| `rawState` | `IRawCubeState` | no default set | `MaxCubeState.java:13` |
| `serialNumber` | `String` | no default set | `MaxCubeState.java:14` |
| `firmwareVersion` | `int` | no default set | `MaxCubeState.java:15` |
| `rfAddress` | `int` | no default set | `MaxCubeState.java:16` |
| `cubeLastPing` | `MaxCubeLastPing` | no default set | `MaxCubeState.java:17` |
| `remoteAccessEnabled` | `boolean` | `false` (Java default) | `MaxCubeState.java:18` |
| `remoteAccessUser` | `String` | no explicit default | `MaxCubeState.java:19` |
| `remoteAccessKey` | `String` | no explicit default | `MaxCubeState.java:20` |
| `remoteAccessSessionToken` | `String` | no explicit default | `MaxCubeState.java:21` |
| `house` | `IHouse` | no default set in constructor | `MaxCubeState.java:22` |
| `daylightSaving` | `boolean` | `false` (Java default) | `MaxCubeState.java:23` |
| `invCube` | `IInventoryCube` | no default set | `MaxCubeState.java:24` |
| `illegalLoginCount` | `int` | `0` (Java default) | `MaxCubeState.java:25` |
| `lastUpdated` | `Date` | no default set | `MaxCubeState.java:26` |
| `cubeDate` | `Date` | no default set | `MaxCubeState.java:27` |
| `stateCubeTime` | `int` | `0` (Java default) | `MaxCubeState.java:28` |
| `ntpCounter` | `int` | `0` (Java default) | `MaxCubeState.java:29` |
| `rooms` | `List<IRoom>` | new empty `ArrayList` | `MaxCubeState.java:30` |
| `devicesNotAssociated` | `List<IDevice>` | new empty `ArrayList` | `MaxCubeState.java:31` |
| `deviceAppUrl` | `String` | `""` | `MaxCubeState.java:32` |
| `pushButtonConfiguration` | `PushButtonConfiguration` | new instance | `MaxCubeState.java:33` |

The DTO variant contains the same core domain fields, but additionally an already initialized `House` and an empty room list: `cl/dto/MaxCubeState.java:11-21`. It is annotated with `@XmlRootElement(name = "MaxCubeState")` (`cl/dto/MaxCubeState.java:9`). The browser stores the DWR result under `$("body").data('maxCubeState')` and enriches it before the callback (`extracted/rootwar/js/models/maxCubeModel.js:85-100`).

### 1.3 Behavior and lifecycle

- `addRoom` adds a room and immediately calls `enumerateRooms()` (`MaxCubeState.java:68-72`).
- `removeRoom` removes by `roomId` and renumbers (`MaxCubeState.java:74-84`).
- `rearrangeRoomOrder(int[] roomIds)` rebuilds the room list in the given order; an unknown id leads to `ValidationException("invalid room id")` (`MaxCubeState.java:86-99`).
- `enumerateRooms()` sets `order` consecutively starting at `0` (`MaxCubeState.java:142-149`).
- `resetChangedState()` is `synchronized` and delegates to all rooms (`MaxCubeState.java:151-158`).
- `hasStateChanges()` returns `true` as soon as a room reports `isStateChanged()` (`MaxCubeState.java:160-168`).
- Unassociated devices are kept separately: `addDevicesNotAssociated`, `removeDeviceFromNotAssociatedList`, `getDevicesNotAssociated` (`MaxCubeState.java:111-130`, `170-173`).

**Lifecycle (INFERENCE from `MaxLocalApi.java:47-104`):** State is read via `getMaxCubeState()`, changed by commands (`setClientCommands`) and then read again. `MaxLocalApi` offers the same operations at the domain level: `getMaxCubeState` (`:47`), `addRoom` (`:66`), `removeRoom` (`:95`), `rearrangeRoomOrder` (`:165`), `setClientCommands` (`:99`), `stillAlivePing` (`:104`), `connect/disconnect` (`:112/:116`).

---

## 2. `House` – the pseudo-aggregate of unassigned devices

### 2.1 Purpose

`House` represents the house as a container for devices that are not assigned to a room ("house-specific devices", e.g. push buttons or wall-mounted thermostats without room assignment).

**Evidence:** `decompiled/essentials/sources/de/eq3/max/al/core/cubestate/House.java:7-9`; the separate API methods `arrangeNewHouseSpecificDevice` and `removeHouseSpecificDevices` evidence the special handling (`decompiled/localbackend/sources/de/eq3/max/al/local/MaxLocalApi.java:75`, `:161`).

### 2.2 Properties and cardinality

| Property | Type | Default | Source |
| --- | --- | --- | --- |
| `id` | `int` | `0` (`final`) | `House.java:8` |
| `devices` | `List<IDevice>` | new empty `ArrayList` | `House.java:9` |

### 2.3 Behavior

- `getId()` always returns `0` (`House.java:11-15`). There is therefore exactly **one** house per cube.
- `getDevice(int radioAddress)` searches linearly over the device list (`House.java:22-30`).
- `setDevices`, `addDevice` allow complete replacement or appending (`House.java:32-40`).

**Cardinality:** `MaxCubeState` 1 → 0..1 `House` (`MaxCubeState.java:22`); `House` 1 → 0..n `IDevice` (`House.java:9`). The upper limit for devices in total is configured: `max.maxDeviceCount=140` (`extracted/settings/max.local.properties:3`). **INFERENCE:** The limit of 140 applies to the overall system, not only to `House`; the AL class itself contains no corresponding check.

---

## 3. `Room` – heating room as control unit

### 3.1 Purpose

A `Room` bundles heating devices, temperatures, operating modes, weekly profile and special functions (window open, boost, descaling). Domain control runs via the room, not via the individual thermostat.

**Evidence:** `decompiled/essentials/sources/de/eq3/max/al/core/cubestate/Room.java:14-41`; `IRoom.java:7-22` (interface with defaults).

### 3.2 Properties, types and defaults

| Property | Type | Default (constructor / interface) | Source |
| --- | --- | --- | --- |
| `id` | `int` | not explicitly set | `Room.java:15`, `:43-64` |
| `name` | `String` | not explicitly set | `Room.java:16` |
| `order` | `int` | not explicitly set | `Room.java:17` |
| `devices` | `List<IDevice>` | new empty `ArrayList` | `Room.java:18`, `:44` |
| `ecoTemperature` | `double` | `16.5` | `Room.java:19`, `:45`; `IRoom.java:9` |
| `comfortTemperature` | `double` | `21.5` | `Room.java:20`, `:46`; `IRoom.java:10` |
| `maximumTemperature` | `double` | `30.5` | `Room.java:21`, `:47`; `IRoom.java:11` |
| `windowOpenTemperature` | `double` | `12.0` | `Room.java:22`, `:48`; `IRoom.java:12` |
| `windowOpenDuration` | `int` | `15` minutes | `Room.java:23`, `:49`; `IRoom.java:13` |
| `controlMode` | `RoomControlMode` | `Auto` | `Room.java:24`, `:50`; `IRoom.java:20` |
| `temperatureMode` | `RoomTemperatureMode` | `NotAvailable` | `Room.java:25`, `:51`; `IRoom.java:21` |
| `boostDuration` | `int` | `5` minutes | `Room.java:26`, `:52`; `IRoom.java:14` |
| `boostValveAngle` | `int` | `80` degrees | `Room.java:27`, `:53`; `IRoom.java:15` |
| `decalcificationDay` | `DayOfWeek` | `Saturday` | `Room.java:28`, `:54`; `IRoom.java:22` |
| `decalcificationHour` | `int` | `12` | `Room.java:29`, `:55`; `IRoom.java:16` |
| `maximumNoOfHeatingThermostats` | `int` | `8` | `Room.java:30`, `:56`; `IRoom.java:17` |
| `maximumNoOfWallMountedThermostats` | `int` | `1` | `Room.java:31`, `:57`; `IRoom.java:18` |
| `maximumNoOfShutterContacts` | `int` | `8` | `Room.java:32`, `:58`; `IRoom.java:19` |
| `temporaryModeStopDate` | `Date` | no default set | `Room.java:33` |
| `masterDeviceRadioAddress` | `int` | `0` (= `MasterDeviceUnknown`) | `Room.java:34`, `:59`; `IRoom.java:8` |
| `weekTemperatureProfile` | `IWeekTemperatureProfile` | `WeekTemperatureProfileBuilder.defaultProfile()` | `Room.java:35`, `:60` |
| `setPointTemperature` | `double` | `actualSetPointTemperature().getValue()` | `Room.java:36`, `:61` |
| `setPointTemperatureValid` | `boolean` | `true` | `Room.java:37`, `:62` |
| `currentAutoTemperature` | `double` | `actualSetPointTemperature().getValue()` | `Room.java:38`, `:63` |
| `actualTemperature` | `double` | `0.0` (Java default) | `Room.java:39` |
| `stateDirty` | `boolean` | `false` | `Room.java:40` |
| `stateChanged` | `boolean` | `false` | `Room.java:41` |

**Important conflict (evidenced, not to be resolved without a product decision):** The properties file `extracted/essentials/max.properties` defines different device defaults: target temperature `20.0`, eco `17.0`, comfort `21.0`, window open `17.0`, control mode `Permanently`, temperature mode `Normal`, boost `15` minutes, valve angle `100`, descaling day `6`, hour `12` (`max.properties:2-13`). The Java constructors use `16.5 / 21.5 / 12.0 / Auto / NotAvailable / 5 / 80 / Saturday` (see table). **INFERENCE:** The properties come from a different configuration layer (presumably hardware initialization of the thermostat), while `Room.java` specifies the runtime defaults of the domain model. A reimplementation must deliberately model both sources and must not silently equate them.

### 3.3 Behavior

- `isTemperatureControllable()` is `true` exactly when a device of type `HeatingThermostat` is present (`Room.java:175-178`). `HeatingThermostatPlus` is **not** checked here – evidenced by the direct comparison with `DeviceType.HeatingThermostat` (`:177`).
- `getActualTemperature()` overwrites the value with the actual temperature of the **last** found `WallMountedThermostatDeviceState` (`Room.java:212-221`).
- `getCurrentAutoTemperature()` determines today's switching point based on the hour (`Calendar.HOUR_OF_DAY`) (`Room.java:204-210`).
- `computeStateFlags()` sets `stateChanged` when a device reports changes and sets `stateDirty` to `false` (`Room.java:136-144`).
- `resetChangedState()` resets all device flags and then the room flag (`Room.java:146-152`).
- `setStateDirty()` sets `stateDirty = true` and calls `resetChangedState()` beforehand (`Room.java:154-158`).
- `equals`/`hashCode` are based solely on `id` (`Room.java:160-173`).
- `getDevicesFiltered(int[])` and `getDevice(int)` filter by radio address (`Room.java:95-118`).
- `getMasterThermostat()` returns the device with the `masterDeviceRadioAddress` (`Room.java:368-371`).

**Lifecycle:** A room is created via `MaxLocalApi.addRoom(String)` (`MaxLocalApi.java:66`), renamed via `changeRoomName` (`:83`) and deleted via `removeRoom(int, boolean)` (`:95`). Order is changed via `rearrangeRoomOrder(int[])` (`:165`). Deleting devices can be combined with `deviceFactoryReset` (`MaxLocalApi.java:91`, `:95`) – **UNKNOWN** remains which exact radio sequence is triggered by this; only the signature is visible in the available material.

---

## 4. `Device` – physical device

### 4.1 Purpose

`Device` represents a physical MAX! device with radio address, serial number, name, room assignment, firmware and type-specific state object.

**Evidence:** `decompiled/essentials/sources/de/eq3/max/al/core/cubestate/Device.java:13-25`.

### 4.2 Properties, types and defaults

| Property | Type | Default | Source |
| --- | --- | --- | --- |
| `deviceType` | `DeviceType` | no default set | `Device.java:14` |
| `numericDeviceType` | `int` | `0` | `Device.java:15` |
| `radioAddress` | `int` | `0` | `Device.java:16` |
| `serialNumber` | `String` | `null` | `Device.java:17` |
| `name` | `String` | `null` | `Device.java:18` |
| `roomId` | `int` | `0` | `Device.java:19` |
| `groupAddress` | `int` | `0` | `Device.java:20` |
| `state` | `IDeviceState` | `null` | `Device.java:21` |
| `firmwareVersion` | `int` | `0` | `Device.java:22` |
| `testResult` | `int` | `0` | `Device.java:23` |
| `stateInfo` | `DeviceStateInfo` | `NotInitialized` | `Device.java:24` |
| `radioState` | `RadioState` | `Ok` | `Device.java:25` |

The DTO deliberately deviates: `radioAddress=-1`, `serialNumber="EEE0000000"`, `name="GENERIC DEVICE"`, `deviceType=GenericDevice`, `state=null`, `stateInfo=Valid`, `radioState=Ok` (`decompiled/essentials/sources/de/eq3/max/cl/dto/Device.java:5-11`). `NewDevice` knows only radio address, serial number and type with the same placeholder defaults (`cl/dto/NewDevice.java:5-7`).

### 4.3 Behavior

- `setDeviceType(...)` creates the matching state object depending on the enum: `ShutterContact` → `ShutterContactDeviceState`, `HeatingThermostat` → `HeatingThermostatDeviceState`, `WallMountedThermostat` → `WallMountedThermostatDeviceState`, `PushButton` → `PushButtonDeviceState` (`Device.java:82-99`). For `HeatingThermostatPlus` and `Invalid`, **no** state is created there; this is an evidenced special case (`:85-98`, switch map `:103-124`).
- `getRadioAddressBytes()` splits the address into three bytes (`Device.java:126-133`).
- `getSerialNumberBytes()` returns exactly 10 bytes; missing characters are padded with spaces (`Device.java:135-144`).
- `getState(Class<T>)` is an unsafe cast to the set state object (`Device.java:151-154`).

**Lifecycle:** New devices appear in `getNewDevices()` (`MaxLocalApi.java:54`), are assigned to a room via `arrangeNewDevice(int, String, String)` (`:79`) or to the house via `arrangeNewHouseSpecificDevice(int, String)` (`:75`). Renaming: `changeDeviceName(int, int, String)` (`:87`); moving: `reArrangeDevice(String, int, String)` (`:178`); removal: `removeDevices(int[], boolean)` (`:91`) or `removeHouseSpecificDevices(int[])` (`:161`). Search is ended with `cancelGetNewDevices()` (`:62`).

---

## 5. Device types (`DeviceType`)

### 5.1 AL enum

| Value | Numeric code | Source |
| --- | --- | --- |
| `Invalid` | `256` | `decompiled/essentials/sources/de/eq3/max/al/core/cubestate/DeviceType.java:5` |
| `HeatingThermostat` | `1` | `DeviceType.java:6` |
| `HeatingThermostatPlus` | `2` | `DeviceType.java:7` |
| `WallMountedThermostat` | `3` | `DeviceType.java:8` |
| `ShutterContact` | `4` | `DeviceType.java:9` |
| `PushButton` | `5` | `DeviceType.java:10` |

`fromValue(int)` returns `Invalid` for unknown codes (`DeviceType.java:22-30`).

### 5.2 DTO enum and deviations

The DTO knows only `GenericDevice`, `ShutterContact`, `HeatingThermostat`, `WallMountedThermostat`, `PushButton` (`decompiled/essentials/sources/de/eq3/max/cl/dto/DeviceType.java:5-9`). `HeatingThermostatPlus` and `Invalid` are missing. The browser layer corrects the type afterwards via `ActionHelperClass.getDeviceType(device)`, because devices with serial numbers `xxx9`, `xxx8` or `xxx7` have an "extra datatype" (`extracted/rootwar/js/models/maxCubeModel.js:90-95`). **INFERENCE:** This reconstructs `HeatingThermostatPlus` on the client side from the serial number; the exact regex lies in `js/lib/ActionHelper.js` and is not evidenced in detail in the available excerpt. **UNKNOWN:** Whether the serial number rule also applies to `Invalid`.

---

## 6. State objects (`DeviceState`)

### 6.1 Common base

`AbstractDeviceState` holds `stateChanged=false`, offers `isStateChanged()`, `resetChangedState()` and the protected helper method `change(actual, newValue)`, which sets the flag when values differ (`decompiled/essentials/sources/de/eq3/max/al/core/cubestate/devices/AbstractDeviceState.java:4-19`). The interface `IDeviceState` declares only `resetChangedState()` (`devices/IDeviceState.java:4-7`).

### 6.2 `HeatingThermostatDeviceState`

Purpose: device state of a radiator thermostat with target temperature, weekly profile, boost, window open, descaling and battery/transmit error.

| Property | Type | Default | Source |
| --- | --- | --- | --- |
| `daylightSaving` | `boolean` | `true` | `HeatingThermostatDeviceState.java:18` |
| `isBatteryLow` | `boolean` | `false` | `:19` |
| `transmitError` | `boolean` | `false` | `:20` |
| `comfortTemperature` | `ITemperature` | `21.5` | `:21` |
| `ecoTemperature` | `ITemperature` | `16.5` | `:22` |
| `windowOpenTemperature` | `ITemperature` | `12.0` | `:23` |
| `roomControlMode` | `RoomControlMode` | `IRoom.DEFAULT_ROOM_CONTROL_MODE` (`Auto`) | `:24` |
| `windowOpenDuration` | `int` | `15` | `:25` |
| `boostDuration` | `int` | `5` | `:26` |
| `boostValve` | `int` | `80` | `:27` |
| `maxValve` | `int` | `100` | `:28` |
| `valveOffset` | `int` | `0` | `:29` |
| `minSetPointTemperature` | `ITemperature` | `Temperature.T_OFF` (4.5) | `:30` |
| `maxSetPointTemperature` | `ITemperature` | `Temperature.T_ON` (30.5) | `:31` |
| `temperatureOffset` | `double` | `DEFAULT_TEMPERATURE_OFFSET = 0.0` | `:32`; `IHeatingThermostatDeviceState.java:14` |
| `weekTemperatureProfile` | `IWeekTemperatureProfile` | `defaultProfile()` | `:33` |
| `setPointTemperature` | `ITemperature` | `actualSetPointTemperature()` | `:34` |
| `decalcificationDate` | `Calendar` | `saturday12()` | `:35`, `:314-315` |
| `vacationEndDay/Month/MonthLSB/Year/Time` | `int` | no explicit default | `:36-40` |

Further evidenced constants: `DEFAULT_WINDOW_OPEN_DURATION=15`, `DEFAULT_MAXIMUM_VALVE=100`, `DEFAULT_VALVE_OFFSET=0` (`IHeatingThermostatDeviceState.java:11-13`). `setSetPointTemperature` reports changes via `change(...)` (`HeatingThermostatDeviceState.java:83-85`); `internal_setSetPointTemperature(int)` uses `Temperature.fromInt` (`:89-90`).

The DTO reduces the state to `isBatteryLow`, `transmitError`, `setPointTemperature=4.5`, `temperatureOffset=0.0` (`cl/dto/HeatingThermostatDeviceState.java:5-8`).

### 6.3 `WallMountedThermostatDeviceState`

Purpose: wall thermostat with additional actual temperature measurement and display option.

| Property | Type | Default | Source |
| --- | --- | --- | --- |
| `actualTemperature` | `double` | `0.0` | `WallMountedThermostatDeviceState.java:18` |
| `vacationEnd*` | `int` | no explicit default | `:19-23` |
| `actualTemperatureState`, `actualTemperatureStateLSB` | `int` | `0` | `:24-25` |
| `daylightSaving` | `boolean` | `true` | `:26` |
| `isBatteryLow` | `boolean` | `false` | `:27` |
| `transmitError` | `boolean` | `false` | `:28` |
| `displayActual` | `boolean` | `false` | `:29` |
| `setPointTemperature` | `ITemperature` | `actualSetPointTemperature()` | `:30` |
| `roomControlMode` | `RoomControlMode` | `Auto` | `:31` |
| `comfortTemperature` / `ecoTemperature` | `ITemperature` | `21.5` / `16.5` | `:32-33` |
| `windowOpenTemperature` | `ITemperature` | `12.0` | `:34` |
| `min/maxSetPointTemperature` | `ITemperature` | `T_OFF` / `T_ON` | `:35-36` |
| `boostDuration` / `boostValve` | `int` | `5` / `80` | `:37-38` |
| `weekTemperatureProfile` | `IWeekTemperatureProfile` | `defaultProfile()` | `:39` |
| `temperatureOffset` | `double` | `0.0` | `:40` |
| `decalcificationDate` | `Calendar` | `saturday12()` | `:41`, `:321-322` |

The DTO additionally contains `actualTemperature`, `displayActual` and `temperatureOffset` (`cl/dto/WallMountedThermostatDeviceState.java:5-10`). `setSetPointTemperature` sets the change flag (`:90-92`).

### 6.4 `ShutterContactDeviceState` and `PushButtonDeviceState`

- Window contact: `isBatteryLow`, `isWindowOpen`, `transmitError`, all `false` as Java default; getter/setter evidenced in `devices/ShutterContactDeviceState.java:7-43`. The DTO adds explicit defaults `false` (`cl/dto/ShutterContactDeviceState.java:5-7`).
- Push button: `isBatteryLow`, `transmitError`, `upperButtonPressed`, `lowerButtonPressed` (`devices/PushButtonDeviceState.java:7-48`; `devices/IPushButtonDeviceState.java:7-19`). The DTO maps only battery and transmit error (`cl/dto/PushButtonDeviceState.java:5-6`).

### 6.5 `DeviceStateInfo` and `RadioState`

`DeviceStateInfo`: `Valid`, `NotInitialized`, `OutOfDate` (`cubestate/DeviceStateInfo.java:5-7`; identical in DTO `cl/dto/DeviceStateInfo.java:5-7`). `RadioState`: `Ok`, `Error`, `ControlFailed`, `ConfigFailed`, `AssignFailed`, `DeleteFailed` (`cubestate/RadioState.java:5-10`). **INFERENCE:** `RadioState` is the domain error class for failed radio operations; a mapping to concrete error codes is not directly evidenced in the source material.

---

## 7. `Temperature` – value object with limits

### 7.1 Purpose

`Temperature` encapsulates a temperature value and normalizes the two special states OFF and ON.

**Evidence:** `decompiled/essentials/sources/de/eq3/max/al/core/cubestate/Temperature.java:6-16`.

### 7.2 Constants and rules

| Constant / rule | Value | Source |
| --- | --- | --- |
| `OFF` | `4.5` | `Temperature.java:7` |
| `ON` | `30.5` | `Temperature.java:8` |
| `T_OFF` / `T_ON` | `new Temperature(4.5)` / `new Temperature(30.5)` | `:9-10` |
| `DEFAULT` | `21.0` | `:11` |
| `@Range` | `min=0, max=40` on `value` | `:13-14` |
| Default constructor | `21.0` | `:18-20` |

### 7.3 Validation and normalization

`setValue(double)` resets `on/off` and normalizes: values `<= 4.5` become `4.5` with `off=true`; values `>= 30.5` become `30.5` with `on=true`; in between the value remains unchanged (`Temperature.java:31-49`). `isValid()` is `true` exactly when neither `on` nor `off` is set (`:61-64`). `setOn()` and `setOff()` call `setValue(30.5)` or `setValue(4.5)` (`:66-74`). `fromInt(int)` divides by `2` (`:76-80`).

`equals` compares valid values with a tolerance `< 0.1`; for ON/OFF the flags are compared (`:82-93`). `toString()` formats as `%1$.1fºC` (`:95-97`).

**Evidenced break:** The DTO `TemperatureProfileSwitchPoint` uses `@Range(min=0, max=40)` on a primitive `double` and `Date stop` instead of `Calendar` (`cl/dto/TemperatureProfileSwitchPoint.java:10-14`). **INFERENCE:** The DTO layer dispenses with ON/OFF semantics and transfers only the numeric value.

---

## 8. Operating modes and domain states

### 8.1 `RoomControlMode`

Enum: `Auto`, `Permanently`, `Temporary`, `NotAvailable` (`cubestate/RoomControlMode.java:5-8`; DTO identical `cl/dto/RoomControlMode.java:5-8`).

| Mode | Domain meaning (evidenced by command / property) | Source |
| --- | --- | --- |
| `Auto` | Target temperature follows the weekly profile; default according to `IRoom` | `IRoom.java:20`; `SetRoomAutoMode.java:1` |
| `Permanently` | Fixed target temperature without automation; `setRoomPermanentMode(roomId, temperature)` | `MaxLocalApi.java` API via `APICalls.js:2162`; `SetRoomPermanentMode.java:1` |
| `Temporary` | Target temperature until an end date; `setRoomTemporaryMode(roomId, temperature, date)` | `extracted/rootwar/js/models/maxCubeModel.js:148-163`; `SetRoomTemporaryMode.java:1` |
| `NotAvailable` | No controllable mode | `RoomControlMode.java:8` |

`Room` holds the end of temporary mode in `temporaryModeStopDate` (`Room.java:33`, `:293-301`).

### 8.2 `RoomTemperatureMode`

Enum: `Eco`, `Comfort`, `Normal`, `NotAvailable` (`cubestate/RoomTemperatureMode.java:5-8`; DTO identical `cl/dto/RoomTemperatureMode.java:5-8`). The corresponding temperatures are present in the room: `ecoTemperature=16.5`, `comfortTemperature=21.5`, `maximumTemperature=30.5` (`Room.java:45-47`). **INFERENCE:** `Normal` corresponds to the automatic profile temperature, `Eco`/`Comfort` to the two configurable comfort values; this is not explicitly stated.

### 8.3 Further operating states

- **Window open**: `windowOpenTemperature=12.0`, `windowOpenDuration=15` (`Room.java:48-49`). Command `SetRoomWindowOpenTemperature` (`al/core/command/SetRoomWindowOpenTemperature.java:1`), DWR method `APICalls.prototype.setRoomWindowOpenTemperature` (`extracted/rootwar/js/lib/APICalls.js:2338`).
- **Boost**: `boostDuration=5`, `boostValveAngle=80` (`Room.java:52-53`); command `SetRoomConfigBoost` (`command/SetRoomConfigBoost.java:1`); API `APICalls.js:2387`.
- **Descaling**: `decalcificationDay=Saturday`, `decalcificationHour=12` (`Room.java:54-55`; `IRoom.java:16`, `:22`); command `SetRoomConfigDecalcification` (`command/SetRoomConfigDecalcification.java:1`); API `APICalls.js:2413`.
- **Temperature offset**: `valveOffset=0` on the thermostat (`HeatingThermostatDeviceState.java:29`), `temperatureOffset=0.0` (`IHeatingThermostatDeviceState.java:14`); command `SetRoomConfigValveOffset` (`command/SetRoomConfigValveOffset.java:1`) and `SetRoomTemperatureOffset` (`command/SetRoomTemperatureOffset.java:1`).
- **Summer/winter time**: `daylightSaving` on the cube (`MaxCubeState.java:23`, `:255-263`) and on the device (`HeatingThermostatDeviceState.java:18`); API `adjustTimeZoneConfiguration` / `setTimeZoneConfiguration` (`MaxLocalApi.java:197`, `:205`).

---

## 9. Weekly profile (`WeekTemperatureProfile`)

### 9.1 Structure and cardinality

`WeekTemperatureProfile` contains `@Valid @Size(min = Constants.COOKIE_AGE_DAYS, max = Constants.COOKIE_AGE_DAYS) List<IDayTemperatureProfile>` (`cubestate/WeekTemperatureProfile.java:13-15`). `Constants.COOKIE_AGE_DAYS = 7` (evidenced in the checkpoint, source `de/eq3/max/al/core/Constants.java`). This means: **exactly 7 day profiles** per weekly profile. The DTO mirrors the same annotation (`cl/dto/WeekTemperatureProfile.java:12-14`).

| Object | Cardinality | Source |
| --- | --- | --- |
| `Room` → `WeekTemperatureProfile` | 1 → 1 | `Room.java:35`, `:233-241` |
| `WeekTemperatureProfile` → `DayTemperatureProfile` | 1 → 7 | `WeekTemperatureProfile.java:13-15` |
| `DayTemperatureProfile` → `TemperatureProfileSwitchPoint` | 1 → 1..13 | `DayTemperatureProfile.java:16-18` |

### 9.2 `DayTemperatureProfile`

- `switchPoints`: `@Valid @Size(min=1, max=13)` (`DayTemperatureProfile.java:16-18`).
- `dayOfWeek`: `@NotNull` (`:20-21`), constructor sets the day of week (`:26-28`).
- `switchPointAt(int hour)` iterates over normalized points and returns the first point whose hour is greater than `hour` or equal to `0`; otherwise `IllegalArgumentException` (`:76-85`).
- `getSwitchPointsNormalized()` sorts in a `TreeSet`, sets hour/minute of the last point to `0` (`:87-100`). **INFERENCE:** The day thus implicitly ends at 00:00.
- `compareTo` compares `0` for equality, otherwise the day of week (`:102-108`).

### 9.3 `TemperatureProfileSwitchPoint`

- `stop`: `@NotNull Calendar`, default `Calendar.getInstance()` with hour/minute `0` (`TemperatureProfileSwitchPoint.java:10-11`, `:17-20`).
- `temperature`: `@NotNull @Valid ITemperature`, default `new Temperature(21.5)` (`:13-15`, `:20`).
- `compareTo` compares `hour*60+minute`; `0` is treated as end of day (`:55-70`).
- `toString()`: `HH:MM - temperature` (`:72-74`).

DTO: `stop` is `Date` with `@NotNull`, `temperature` is `double` with `@Range(0..40)` (`cl/dto/TemperatureProfileSwitchPoint.java:10-14`).

### 9.4 Command and client editor

`SetRoomWeekTemperatureProfile` is the corresponding command (`command/SetRoomWeekTemperatureProfile.java:1`), `APICalls.prototype.setRoomWeekTemperatureProfile` the DWR call (`extracted/rootwar/js/lib/APICalls.js:2264`). The browser editor implements resize/drag of intervals (`extracted/rootwar/js/com_configuration/controllers/weeklyProgramController.js:195-227`, `:353-579`), fast editing (`:643-823`), rounding to 0.5 °C (`:1066`), adding/deleting intervals (`:1152`, `:1407`) and time validation (`:1521-1536`). The client's default day plan creates exactly one switching point at 24:00 with `21.5` (`extracted/rootwar/js/models/maxCubeModel.js:165-177`).

**Lifecycle:** Weekly profile is initialized with the room (`Room.java:60`), changed in the editor (`weeklyProgramController.js:1293-1312`) and persisted via `setRoomWeekTemperatureProfile` (`APICalls.js:2264`). **UNKNOWN:** Whether the 13-point limit is hard-enforced in the browser editor; the AL annotation is evidenced, the JS validation only partially visible.

---

## 10. Further value objects

| Object | Purpose / properties | Source |
| --- | --- | --- |
| `NewDevice` | Device in discovery mode: radio address, serial number, type | `cubestate/NewDevice.java:1`; DTO `cl/dto/NewDevice.java:4-7` |
| `NetworkConfiguration` | IP, subnet, gateway, DNS1, DNS2, DHCP, encryption. Defaults `192.168.0.123`, `255.255.255.0`, `192.168.0.1`, `192.168.0.1`, `192.168.0.1`, `dhcpEnabled=true`, `encryptionEnabled` without default | `cubestate/NetworkConfiguration.java:7-24`; DTO `cl/dto/NetworkConfiguration.java:5-10` (without `encryptionEnabled`) |
| `PushButtonConfiguration` | `upperKey`, `lowerKey`, each `PushButtonKeyConfiguration` | `cubestate/PushButtonConfiguration.java:4-6` |
| `PushButtonKeyConfiguration` / `PushButtonKeyMode` | Button assignment and mode | `cubestate/PushButtonKeyConfiguration.java:1`, `cubestate/PushButtonKeyMode.java:1` |
| `DaylightSavingConfiguration` | Summer/winter time parameters for `adjustTimeZoneConfiguration` | `MaxLocalApi.java:197`, `cubestate/DaylightSavingConfiguration.java:1` |
| `MaxCubeLastPing` | Last airtime status of the cube | `cubestate/MaxCubeLastPing.java:1`; `MaxLocalApi.java:104` |
| `MaxCubeAirtimeStatus` | Result of `stillAlivePing()` | `MaxLocalApi.java:104`; `cubestate/MaxCubeAirtimeStatus.java:1` |
| `FirmwareInfo` | Firmware version / check (`getLatestFirmwareVersion`) | `MaxLocalApi.java:145`; `cubestate/FirmwareInfo.java:1` |
| `SoftwareInformation` | `downloadURL`, `version`, `updateAvailable`; DTO defaults `""`, `"0.0.1"`, `false` | `cl/dto/SoftwareInformation.java:7-20` |
| `UpdateStatus` | `finished`, `steps`, `currentStep`, `info`; default `true`, `0`, `0`, `"INVALID"` | `cl/dto/UpdateStatus.java:7-17` |
| `ProductActivation` / `ProductInformation` | Remote access activation and product info | `MaxLocalApi.java:221`, `:225` |
| `RemoteCredentials` | `userName`, `remoteAccessEnabled` | `cl/dto/RemoteCredentials.java:5-6`; `MaxLocalApi.java:124` |
| `AdditionalInformation` | Additional information (`getAdditionalInformation`) | `MaxLocalApi.java:233` |
| `CubeInfo` | Result of cube discovery (`listAvailableMaxCubes`) | `MaxLocalApi.java:133`; `cl/dto/CubeInfo.java:1` |
| `Metadata` / `RawCubeState` | Raw data of cube state | `cubestate/Metadata.java:1`, `cubestate/RawCubeState.java:1` |

Configuration values that limit domain behavior: `maxRoomCount=20`, `maxDeviceCount=140`, `maxNameLength=32`, `inclusionModeTimeoutSeconds=60`, `cubeScanTime=1800`, `latestCubeFirmwareVersion=275`, `leastCompatibleFirmwareVersion=273`, `idleTimeBeforeLocalAppDisconnect=30000`, `cubeIdleTimeToSend=900000` (`extracted/settings/max.local.properties:1-11`, `:17-18`, `:4-5`).

---

## 11. Relationships and cardinalities (overview)

| Relationship | Cardinality | Source |
| --- | --- | --- |
| `MaxCubeState` → `House` | 1 → 0..1 | `MaxCubeState.java:22`, `:35-43` |
| `MaxCubeState` → `Room` | 1 → 0..20 (configured) | `MaxCubeState.java:30`, `:45-48`; `max.local.properties:2` |
| `MaxCubeState` → `IDevice` (unassigned) | 1 → 0..n | `MaxCubeState.java:31`, `:171-173` |
| `MaxCubeState` → `PushButtonConfiguration` | 1 → 1 | `MaxCubeState.java:33`, `:285-293` |
| `House` → `IDevice` | 1 → 0..n | `House.java:9`, `:17-20` |
| `Room` → `IDevice` | 1 → 0..n, limited per type | `Room.java:18`, `:90-123`; `IRoom.java:17-19` |
| `IDevice` → `Room` | n → 1 (`roomId`) | `Device.java:19`, `:57-65` |
| `IDevice` → `IDeviceState` | 1 → 1, created depending on type | `Device.java:21`, `:82-99` |
| `Room` → `WeekTemperatureProfile` | 1 → 1 | `Room.java:35`, `:233-241` |
| `WeekTemperatureProfile` → `DayTemperatureProfile` | 1 → 7 | `WeekTemperatureProfile.java:13-15` |
| `DayTemperatureProfile` → `TemperatureProfileSwitchPoint` | 1 → 1..13 | `DayTemperatureProfile.java:16-18` |
| `TemperatureProfileSwitchPoint` → `ITemperature` | 1 → 1 | `TemperatureProfileSwitchPoint.java:13-15` |
| `Room` → `Device` (master) | 1 → 0..1 via radio address | `Room.java:34`, `:363-381` |

**INFERENCE on identity:** Rooms are identified by `id`, devices by `radioAddress` (`Room.java:160-173`; `Device.java:27-35`; `Room.java:110-118`). Names are not identity-bearing, but should be kept domain-unique (see validation).

---

## 12. Validation rules

| Rule | Implementation | Exception / behavior | Source |
| --- | --- | --- | --- |
| Device name must not be `null` | `assertNameLength` | `NameIsNullException` | `ConfigureDeviceValidator.java:148-151` |
| Device name ≤ `maxNameLength` (32) | `assertNameLength` | `NameTooLongException` | `:152-154`; `max.local.properties:1` |
| Device name unique (case-insensitive) | `getDeviceByName` | `DeviceNameAlreadyExistsException` | `:50-55`, `:123-138` |
| Room name unique (case-insensitive) | `getRoom` + `assertUniqueDeviceName` | `DeviceNameAlreadyExistsException` for device names in the room | `:104-121` |
| Radio address must be under `NewDevices` | `assertExistingNewDevice` | `InvalidDeviceRadioAddressException` | `:141-145`, `:157-164` |
| Per room: heating thermostats ≤ 8 | `assertConstraintDeviceTypeRoom` | `MaximumNoOfDevicesWithThisTypeInRoomException` | `:80-99` |
| Per room: wall thermostats ≤ 1 | same | same | `:84-85` |
| Per room: window contacts ≤ 8 | same | same | `:86-87` |
| Other device types ≤ 50 | `maxNoOfDevices = 50` | same | `:81` |
| User name must not be empty | `validateUserName` | `NoUserNameException` | `RemoteCredentialsValidator.java:24-27` |
| User name must match `[^\r\n,]+` | `matches(UNALLOWED_CHARS)` | `InvalidUserNameException` | `:12`, `:28-30` |
| User/password set but remote disabled | `validateArguments` | `InvalidArgumentsException` | `:35-38` |
| Software info exactly 3 values | `SoftwareInformationValidator` | `SoftwareInformationHasInvalidCountOfArgumentsException` | `SoftwareInformationValidator.java:14-17` |
| First value matches `\d+.\d+.\d+` | same | `SoftwareVersionIsInvalidException` | `:18-20` |
| Temperature in range 0..40 | `@Range` | Bean validation | `Temperature.java:13`; `cl/dto/TemperatureProfileSwitchPoint.java:13` |
| Weekly profile exactly 7 days | `@Size(min=COOKIE_AGE_DAYS, max=...)` | Bean validation | `WeekTemperatureProfile.java:14` |
| Day profile 1..13 switching points | `@Size` | Bean validation | `DayTemperatureProfile.java:17` |
| `stop` and `temperature` not null | `@NotNull` | Bean validation | `TemperatureProfileSwitchPoint.java:10`, `:13` |
| `dayOfWeek` not null | `@NotNull` | Bean validation | `DayTemperatureProfile.java:20-21` |
| Invalid room id when sorting | `rearrangeRoomOrder` | `ValidationException("invalid room id")` | `MaxCubeState.java:93-95` |

**Note on the regex:** `[^\r\n,]+` is to be read as a *match of the entire string* (`String.matches`), i.e. the user name may contain neither line breaks nor a comma; spaces are not excluded. The constant is misleadingly named `UNALLOWED_CHARS` (`RemoteCredentialsValidator.java:12`). The software version regex `\d+.\d+.\d+` uses unescaped dots (any character), so strictly speaking it is wider than a SemVer check (`:18`). **INFERENCE:** A reimplementation should check the intention (three blocks of digits) without adopting the escaped-dot error.

Error code mapping (excerpt, full table `extracted/essentials/errorcodes.properties:1-53`): `DefaultErrorCode/GeneralTechnicalFailure=MAX0001`, `DeviceNameAlreadyExists=MAX1015`, `InvalidDeviceRadioAddress=MAX1022`, `NameIsNull=MAX1029`, `NameTooLong=MAX1030`, `NoUserName=MAX1031`, `RemoteAccessDisabled=MAX1037`, `SoftwareVersionIsInvalid=MAX1044`, `Validation=MAX1050`, `WrongSize=MAX1051`. **Finding preserved:** `RemoteServiceUnavailable=MAX10038` deviates from the otherwise `MAX10xx` structure (`:40`); the value must be adopted as-is, not "corrected".

---

## 13. DTO ↔ AL package mapping

| Domain object | AL class (`de.eq3.max.al.core...`) | DTO class (`de.eq3.max.cl.dto`) | Essential deviation | Source |
| --- | --- | --- | --- | --- |
| Cube state | `cubestate.MaxCubeState` | `cl.dto.MaxCubeState` | DTO has `house=new House()`, `rooms=[]`, `@XmlRootElement`; AL additionally has `invCube`, `rawState`, `illegalLoginCount`, `deviceAppUrl` | `MaxCubeState.java:12-33`; `cl/dto/MaxCubeState.java:9-21` |
| House | `cubestate.House` | `cl.dto.House` | DTO house is a simple container | `House.java:7-9`; `cl/dto/House.java:1` |
| Room | `cubestate.Room` (implements `IRoom`) | `cl.dto.Room` | DTO: `id=-1`, `order=-1`, `name="GENERIC ROOM"`, `isTemperatureControllable/stateDirty/stateChanged` as explicit fields | `Room.java:14-64`; `cl/dto/Room.java:9-35` |
| Device | `cubestate.Device` | `cl.dto.Device` | DTO: `radioAddress=-1`, `EEE0000000`, `GENERIC DEVICE`, `GenericDevice`, `stateInfo=Valid` | `Device.java:13-25`; `cl/dto/Device.java:5-11` |
| New device | `cubestate.NewDevice` / `INewDevice` | `cl.dto.NewDevice` | DTO only 3 fields | `cubestate/NewDevice.java:1`; `cl/dto/NewDevice.java:4-7` |
| Device state (base) | `devices.AbstractDeviceState` / `IDeviceState` | `cl.dto.DeviceState` | DTO abstract without fields | `devices/AbstractDeviceState.java:4-19`; `cl/dto/DeviceState.java:4-5` |
| Radiator thermostat state | `devices.HeatingThermostatDeviceState` | `cl.dto.HeatingThermostatDeviceState` | AL knows boost, profile, descaling, valve; DTO only 4 fields | `HeatingThermostatDeviceState.java:17-40`; `cl/dto/HeatingThermostatDeviceState.java:5-8` |
| Wall thermostat state | `devices.WallMountedThermostatDeviceState` | `cl.dto.WallMountedThermostatDeviceState` | DTO adds `actualTemperature`, `displayActual`, `temperatureOffset` | `WallMountedThermostatDeviceState.java:17-41`; `cl/dto/WallMountedThermostatDeviceState.java:5-10` |
| Window contact state | `devices.ShutterContactDeviceState` | `cl.dto.ShutterContactDeviceState` | DTO defaults explicitly `false` | `devices/ShutterContactDeviceState.java:7-10`; `cl/dto/ShutterContactDeviceState.java:5-7` |
| Push-button state | `devices.PushButtonDeviceState` | `cl.dto.PushButtonDeviceState` | DTO knows no button presses | `devices/PushButtonDeviceState.java:7-9`; `cl/dto/PushButtonDeviceState.java:5-6` |
| Device type | `cubestate.DeviceType` (6 values, numeric) | `cl.dto.DeviceType` (5 values, no codes) | `Invalid(256)`, `HeatingThermostatPlus(2)` missing in DTO | `DeviceType.java:5-10`; `cl/dto/DeviceType.java:5-9` |
| State info | `cubestate.DeviceStateInfo` | `cl.dto.DeviceStateInfo` | identical | `DeviceStateInfo.java:5-7`; `cl/dto/DeviceStateInfo.java:5-7` |
| Radio state | `cubestate.RadioState` | `cl.dtо.RadioState` | identical (6 values) | `RadioState.java:5-10`; `cl/dto/RadioState.java:1` |
| Control mode | `cubestate.RoomControlMode` | `cl.dto.RoomControlMode` | identical | `RoomControlMode.java:5-8`; `cl/dto/RoomControlMode.java:5-8` |
| Temperature mode | `cubestate.RoomTemperatureMode` | `cl.dto.RoomTemperatureMode` | identical | `RoomTemperatureMode.java:5-8`; `cl/dto/RoomTemperatureMode.java:5-8` |
| Weekly profile | `cubestate.WeekTemperatureProfile` | `cl.dto.WeekTemperatureProfile` | both `@Valid @Size(7)` | `WeekTemperatureProfile.java:13-15`; `cl/dto/WeekTemperatureProfile.java:12-14` |
| Day profile | `cubestate.DayTemperatureProfile` | `cl.dto.DayTemperatureProfile` | both `@Size(1..13)`, `@NotNull dayOfWeek` | `DayTemperatureProfile.java:16-21`; `cl/dto/DayTemperatureProfile.java:12-17` |
| Switching point | `cubestate.TemperatureProfileSwitchPoint` (`Calendar` + `ITemperature`) | `cl.dto.TemperatureProfileSwitchPoint` (`Date` + `double`) | Type difference, ON/OFF semantics lost | `TemperatureProfileSwitchPoint.java:10-15`; `cl/dto/TemperatureProfileSwitchPoint.java:10-14` |
| Network | `cubestate.NetworkConfiguration` | `cl.dto.NetworkConfiguration` | AL additionally `encryptionEnabled` | `NetworkConfiguration.java:9-15`; `cl/dto/NetworkConfiguration.java:5-10` |
| Push-button configuration | `cubestate.PushButtonConfiguration` | `cl.dto.PushButtonConfiguration` | both held in cube state | `PushButtonConfiguration.java:4-6`; `cl/dto/PushButtonConfiguration.java:1` |
| Remote access | AL settings + `MaxCubeState.remoteAccess*` | `cl.dto.RemoteCredentials` | DTO only `userName` + `remoteAccessEnabled`; password/session not in DTO | `MaxCubeState.java:18-21`, `:205-242`; `cl/dto/RemoteCredentials.java:5-6` |
| Software update | `de.eq3.max.al.local.update...` | `cl.dto.SoftwareInformation`, `cl.dto.UpdateStatus` | DTO defaults `"0.0.1"/false` and `INVALID` | `cl/dto/SoftwareInformation.java:17-21`; `cl/dto/UpdateStatus.java:12-17` |

**INFERENCE on mapping direction:** The AL→DTO direction is a reduction to UI-relevant fields; the DTO→AL direction (e.g. `setClientCommands`) acts via commands, not via directly setting AL fields (`MaxLocalApi.java:99`; `command/ClientCommand.java:1`). A reimplementation should therefore never write DTOs directly into the AL aggregate.

---

## 14. Use cases / command catalog

The `al/core/command` packages contain the domain write operations. Complete list (source: directory `decompiled/essentials/sources/de/eq3/max/al/core/command/`):

| Command | Domain operation | Source |
| --- | --- | --- |
| `SetRoomAutoMode` | Set room to automatic | `SetRoomAutoMode.java:1` |
| `SetRoomAutoModeWithTemperature` | Automatic with temperature mode | `SetRoomAutoModeWithTemperature.java:1` |
| `SetRoomPermanentMode` | Permanent operation with target temperature | `SetRoomPermanentMode.java:1` |
| `SetRoomTemporaryMode` | Temporary mode with end date | `SetRoomTemporaryMode.java:1` |
| `SetRoomWeekTemperatureProfile` | Apply weekly profile | `SetRoomWeekTemperatureProfile.java:1` |
| `SetRoomEcoTemperature` | Change eco temperature | `SetRoomEcoTemperature.java:1` |
| `SetRoomComfortTemperature` | Change comfort temperature | `SetRoomComfortTemperature.java:1` |
| `SetRoomMaximumTemperature` | Change maximum temperature | `SetRoomMaximumTemperature.java:1` |
| `SetRoomWindowOpenTemperature` | Change window-open temperature | `SetRoomWindowOpenTemperature.java:1` |
| `SetRoomConfigBoost` | Configure boost duration/valve angle | `SetRoomConfigBoost.java:1` |
| `SetRoomConfigDecalcification` | Configure descaling time | `SetRoomConfigDecalcification.java:1` |
| `SetRoomConfigValveOffset` | Configure valve offset | `SetRoomConfigValveOffset.java:1` |
| `SetRoomTemperatureOffset` | Configure temperature offset | `SetRoomTemperatureOffset.java:1` |
| `SetDeviceDisplaySetPointActual` | Toggle wall thermostat display | `SetDeviceDisplaySetPointActual.java:1` |
| `WallMountedThermostatDisplayConfigCommand` | Configure display (alias/variant) | `WallMountedThermostatDisplayConfigCommand.java:1` |
| `SetDeviceDaylightSavingMode` | Set summer time on device | `SetDeviceDaylightSavingMode.java:1` |
| `AssociateNewDeviceToRoom` | Assign new device to a room | `AssociateNewDeviceToRoom.java:1` |
| `DisassociateDeviceFromRoom` | Detach device from room | `DisassociateDeviceFromRoom.java:1` |
| `ClientCommand` / `IClientCommand` | Command contract and collective command | `ClientCommand.java:1`, `IClientCommand.java:1` |
| `CanNotUpdateStateException` | Error case on state update | `CanNotUpdateStateException.java:1` |

Related DWR methods of the browser layer (selection, complete `extracted/rootwar/js/lib/APICalls.js:2133-2458`): `setRoomAutoMode` (`:2133`), `setRoomPermanentMode` (`:2162`), `setRoomAutoWithTemperatureMode` (`:2193`), `setRoomTemporaryMode` (`:2225`), `setRoomWeekTemperatureProfile` (`:2264`), `setRoomEcoTemperature` (`:2289`), `setRoomComfortTemperature` (`:2313`), `setRoomWindowOpenTemperature` (`:2338`), `setRoomMaximumTemperature` (`:2361`), `setRoomConfigBoost` (`:2387`), `setRoomConfigDecalcification` (`:2413`), `setRoomTemperatureOffset` (`:2440`), `setDeviceDisplaySetPointActual` (`:2458`). The client model knows a collective mode: `setClientCommandAssembleMode()` / `setClientCommandImmediateMode()` / `executeClientCommands()` (`extracted/rootwar/js/models/maxCubeModel.js:178-200`).

---

## 15. Explicit open points

1. **UNKNOWN** – The exact hardware semantics of `deviceFactoryReset` (`MaxLocalApi.java:91`, `:95`) are not evidenced.
2. **UNKNOWN** – The complete regex for deriving `HeatingThermostatPlus` from the serial number is not in the evaluated excerpt (`maxCubeModel.js:90-95` refers to `ActionHelperClass`).
3. **UNKNOWN** – Whether `HeatingThermostatPlus` should be considered in `Room.isTemperatureControllable()`; it is only evidenced that the code does not (`Room.java:176-178`).
4. **INFERENCE** – `Room` is the domain control unit, not the device; justified by command naming (`SetRoom*`) and UI navigation via rooms.
5. **INFERENCE** – The 7-day rule follows from `Constants.COOKIE_AGE_DAYS = 7`; the constant was not reopened in the available excerpt, but is referenced in both profile classes (`WeekTemperatureProfile.java:14`; `cl/dto/WeekTemperatureProfile.java:13`).
6. **UNKNOWN** – Meaning and structure of `AdditionalInformation` in detail (`MaxLocalApi.java:233`).
7. **INFERENCE** – The properties `max.properties` and the Java constructor defaults describe two different default levels; a reimplementation must keep both separate (evidence in section 3.2).
8. **UNKNOWN** – Whether `encryptionEnabled` of the AL `NetworkConfiguration` can be set at all via the DTO layer; the DTO has no corresponding field (`cl/dto/NetworkConfiguration.java:5-10`).

---

## 16. Implementation of value objects and enums in C# (24.11.2026)

`src/MaxCube.Domain/` contains the immutable counterparts of the above value objects. The enum list is derived exhaustively from the Java sources (AP-2.1, criterion 1):

| C# type | Java source | Special feature |
| --- | --- | --- |
| `Temperature` | `Temperature.java:6-98` | OFF 4.5 / ON 30.5 / DEFAULT 21.0; exact value comparison instead of `< 0.1` tolerance (`:82-93`) |
| `RadioAddress` | `Device.java:126-133`, `CubeConnection.java:55-56,297-302` | 24 bits, three bytes big-endian; six hex digits only in the examples |
| `SerialNumber` | `Device.java:135-144`, `errorcodes.properties:42-43` | ten bytes with space padding; pattern `UNKNOWN` |
| `DeviceType` | `DeviceType.java:4-30` | `Invalid=256`, 1…5, unknown → `Invalid` |
| `RoomControlMode` | `RoomControlMode.java:4-9` | 4 values |
| `RoomTemperatureMode` | `RoomTemperatureMode.java:4-9` | 4 values |
| `DeviceStateInfo` | `DeviceStateInfo.java:4-8` | 3 values |
| `RadioState` | `RadioState.java:4-11` | 6 values |
| `DayOfWeek` | `DayOfWeek.java:6-45` | **Monday=1…Sunday=7**, not `System.DayOfWeek`; `fromCalendar` computes `((day + 5) % 7) + 1` |
| `PushButtonKeyMode` | `PushButtonKeyMode.java:4-10` | 5 values |

The error codes `MAX1022`, `MAX1034`, `MAX1035`, `MAX1040` are used in the C# code with a meaning marked as `INFERENCE` (`errorcodes.properties:24,36,37,42`); the Java sources contain no triggering location for MAX1034/1035/1040. `MAX1041` remains without a pattern implementation.
