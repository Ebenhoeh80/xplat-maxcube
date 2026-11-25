# Domänenmodell – MAX! Cube Local Application

Technische Spezifikation der Fachobjekte der dekompilierten MAX!-Anwendung (Stände: `MaxEssentialsBackend-1.4.6.jar`, `MaxLocalBackend-1.4.6.jar`, `MaxLocalApp.jar`). Grundlage sind ausschließlich die lokal vorliegenden Java-Quellen, Properties-Dateien und die Browser-JS-Quellen unter `extracted/rootwar/`. Ziel ist die Rekonstruktion des Domänenmodells als Grundlage für eine *Neuimplementierung*, nicht eine 1:1-Portierung der bestehenden Klassen.

## 0. Lesart, Quellenbasis und Kennzeichnung

- **Fakt**: Direkt aus einer Quellstelle belegbar. Jede Aussage trägt einen Pfad mit Zeilennummer, z. B. `Room.java:43-64`.
- **INFERENZ**: Aus mehreren belegten Fakten abgeleitet, aber nicht explizit im Code formuliert.
- **UNBEKANNT**: Im vorliegenden Material nicht bestimmbar; darf nicht als Beispielwert oder Verhalten ausgegeben werden.

Es werden drei Representationsebenen unterschieden:

| Ebene | Paket / Ort | Zweck | Beleg |
|---|---|---|---|
| AL (Application Layer) | `de.eq3.max.al.core.cubestate`, `de.eq3.max.al.core.command` | Fachmodell, Validierung, Zustandsverwaltung | `decompiled/essentials/sources/de/eq3/max/al/core/cubestate/Room.java:14`, `decompiled/essentials/sources/de/eq3/max/al/core/command/ClientCommand.java:1` |
| DTO (Client Transport) | `de.eq3.max.cl.dto` | Serialisierbare Transportobjekte für DWR/REST an die Browser-UI | `decompiled/essentials/sources/de/eq3/max/cl/dto/MaxCubeState.java:9-21` |
| Browser-Modell | `js/models/maxCubeModel.js`, `js/com_structure/models/localModel.js`, `js/com_configuration/models/remoteModel.js` | Clientseitige Verwaltung des Zustands und der DWR-Aufrufe | `extracted/rootwar/js/models/maxCubeModel.js:17-37` |

Die AL-Ebene ist fachlich führend. Die DTO-Ebene ist ein bewusst stark vereinfachtes Abbild; die Browser-Ebene ergänzt eigene Ableitungen (z. B. Gerätetyp aus der Seriennummer, `maxCubeModel.js:90-95`).

---

## 1. Aggregatwurzel: Cube / Gateway (`MaxCubeState`)

### 1.1 Zweck

`MaxCubeState` ist die Aggregatwurzel des gesamten Heizungszustands. Es hält Geräte, Räume, Gateway-Identität, Firmware, Fernzugriff, Zeitinformationen und die Tastaturkonfiguration zusammen.

**Beleg:** `decompiled/essentials/sources/de/eq3/max/al/core/cubestate/MaxCubeState.java:12-33` (Klasse und Felder), `.../IMaxCubeState.java:1` (Interface).

### 1.2 Properties, Typen und Defaults

| Property | Typ | Default / Anmerkung | Quelle |
|---|---|---|---|
| `rawState` | `IRawCubeState` | kein Default gesetzt | `MaxCubeState.java:13` |
| `serialNumber` | `String` | kein Default gesetzt | `MaxCubeState.java:14` |
| `firmwareVersion` | `int` | kein Default gesetzt | `MaxCubeState.java:15` |
| `rfAddress` | `int` | kein Default gesetzt | `MaxCubeState.java:16` |
| `cubeLastPing` | `MaxCubeLastPing` | kein Default gesetzt | `MaxCubeState.java:17` |
| `remoteAccessEnabled` | `boolean` | `false` (Java-Default) | `MaxCubeState.java:18` |
| `remoteAccessUser` | `String` | kein expliziter Default | `MaxCubeState.java:19` |
| `remoteAccessKey` | `String` | kein expliziter Default | `MaxCubeState.java:20` |
| `remoteAccessSessionToken` | `String` | kein expliziter Default | `MaxCubeState.java:21` |
| `house` | `IHouse` | kein Default im Konstruktor gesetzt | `MaxCubeState.java:22` |
| `daylightSaving` | `boolean` | `false` (Java-Default) | `MaxCubeState.java:23` |
| `invCube` | `IInventoryCube` | kein Default gesetzt | `MaxCubeState.java:24` |
| `illegalLoginCount` | `int` | `0` (Java-Default) | `MaxCubeState.java:25` |
| `lastUpdated` | `Date` | kein Default gesetzt | `MaxCubeState.java:26` |
| `cubeDate` | `Date` | kein Default gesetzt | `MaxCubeState.java:27` |
| `stateCubeTime` | `int` | `0` (Java-Default) | `MaxCubeState.java:28` |
| `ntpCounter` | `int` | `0` (Java-Default) | `MaxCubeState.java:29` |
| `rooms` | `List<IRoom>` | neue leere `ArrayList` | `MaxCubeState.java:30` |
| `devicesNotAssociated` | `List<IDevice>` | neue leere `ArrayList` | `MaxCubeState.java:31` |
| `deviceAppUrl` | `String` | `""` | `MaxCubeState.java:32` |
| `pushButtonConfiguration` | `PushButtonConfiguration` | neue Instanz | `MaxCubeState.java:33` |

Die DTO-Variante enthält dieselben fachlichen Kernfelder, zusätzlich aber ein bereits initialisiertes `House` und eine leere Räumeliste: `cl/dto/MaxCubeState.java:11-21`. Sie ist mit `@XmlRootElement(name = "MaxCubeState")` annotiert (`cl/dto/MaxCubeState.java:9`). Der Browser speichert das DWR-Ergebnis unter `$("body").data('maxCubeState')` und reichert es vor dem Callback an (`extracted/rootwar/js/models/maxCubeModel.js:85-100`).

### 1.3 Verhalten und Lebenszyklus

- `addRoom` fügt einen Raum hinzu und ruft unmittelbar `enumerateRooms()` auf (`MaxCubeState.java:68-72`).
- `removeRoom` entfernt anhand der `roomId` und nummeriert neu (`MaxCubeState.java:74-84`).
- `rearrangeRoomOrder(int[] roomIds)` baut die Raumliste in der übergebenen Reihenfolge neu auf; eine unbekannte Id führt zu `ValidationException("invalid room id")` (`MaxCubeState.java:86-99`).
- `enumerateRooms()` setzt `order` fortlaufend ab `0` (`MaxCubeState.java:142-149`).
- `resetChangedState()` ist `synchronized` und delegiert an alle Räume (`MaxCubeState.java:151-158`).
- `hasStateChanges()` liefert `true`, sobald ein Raum `isStateChanged()` meldet (`MaxCubeState.java:160-168`).
- Nicht zugeordnete Geräte werden separat geführt: `addDevicesNotAssociated`, `removeDeviceFromNotAssociatedList`, `getDevicesNotAssociated` (`MaxCubeState.java:111-130`, `170-173`).

**Lebenszyklus (INFERENZ aus `MaxLocalApi.java:47-104`):** Der Zustand wird über `getMaxCubeState()` gelesen, durch Kommandos (`setClientCommands`) verändert und anschließend erneut gelesen. `MaxLocalApi` bietet dieselben Operationen fachlich an: `getMaxCubeState` (`:47`), `addRoom` (`:66`), `removeRoom` (`:95`), `rearrangeRoomOrder` (`:165`), `setClientCommands` (`:99`), `stillAlivePing` (`:104`), `connect/disconnect` (`:112/:116`).

---

## 2. `House` – dasseudo-Aggregat der raumlosen Geräte

### 2.1 Zweck

`House` repräsentiert das Haus als Container für Geräte, die keinem Raum zugeordnet sind („house-specific devices“, z. B. Taster oder wandmontierte Thermostate ohne Raumzuordnung).

**Beleg:** `decompiled/essentials/sources/de/eq3/max/al/core/cubestate/House.java:7-9`; die getrennten API-Methoden `arrangeNewHouseSpecificDevice` und `removeHouseSpecificDevices` belegen die gesonderte Behandlung (`decompiled/localbackend/sources/de/eq3/max/al/local/MaxLocalApi.java:75`, `:161`).

### 2.2 Properties und Kardinalität

| Property | Typ | Default | Quelle |
|---|---|---|---|
| `id` | `int` | `0` (`final`) | `House.java:8` |
| `devices` | `List<IDevice>` | neue leere `ArrayList` | `House.java:9` |

### 2.3 Verhalten

- `getId()` liefert konstant `0` (`House.java:11-15`). Es gibt also genau **ein** House pro Cube.
- `getDevice(int radioAddress)` sucht linear über die Geräteliste (`House.java:22-30`).
- `setDevices`, `addDevice` erlauben vollständigen Austausch bzw. Anfügen (`House.java:32-40`).

**Kardinalität:** `MaxCubeState` 1 → 0..1 `House` (`MaxCubeState.java:22`); `House` 1 → 0..n `IDevice` (`House.java:9`). Die Obergrenze für Geräte insgesamt ist konfiguriert: `max.maxDeviceCount=140` (`extracted/settings/max.local.properties:3`). **INFERENZ:** Die 140er-Grenze gilt für das Gesamtsystem, nicht nur für `House`; die AL-Klasse selbst enthält keine entsprechende Prüfung.

---

## 3. `Room` – Heizraum als Steuerungseinheit

### 3.1 Zweck

Ein `Room` bündelt Heizgeräte, Temperaturen, Betriebsmodi, Wochenprofil und Sonderfunktionen (Fenster auf, Boost, Entkalkung). Die fachliche Steuerung läuft über den Raum, nicht über das einzelne Thermostat.

**Beleg:** `decompiled/essentials/sources/de/eq3/max/al/core/cubestate/Room.java:14-41`; `IRoom.java:7-22` (Schnittstelle mit Defaults).

### 3.2 Properties, Typen und Defaults

| Property | Typ | Default (Konstruktor / Interface) | Quelle |
|---|---|---|---|
| `id` | `int` | nicht explizit gesetzt | `Room.java:15`, `:43-64` |
| `name` | `String` | nicht explizit gesetzt | `Room.java:16` |
| `order` | `int` | nicht explizit gesetzt | `Room.java:17` |
| `devices` | `List<IDevice>` | neue leere `ArrayList` | `Room.java:18`, `:44` |
| `ecoTemperature` | `double` | `16.5` | `Room.java:19`, `:45`; `IRoom.java:9` |
| `comfortTemperature` | `double` | `21.5` | `Room.java:20`, `:46`; `IRoom.java:10` |
| `maximumTemperature` | `double` | `30.5` | `Room.java:21`, `:47`; `IRoom.java:11` |
| `windowOpenTemperature` | `double` | `12.0` | `Room.java:22`, `:48`; `IRoom.java:12` |
| `windowOpenDuration` | `int` | `15` Minuten | `Room.java:23`, `:49`; `IRoom.java:13` |
| `controlMode` | `RoomControlMode` | `Auto` | `Room.java:24`, `:50`; `IRoom.java:20` |
| `temperatureMode` | `RoomTemperatureMode` | `NotAvailable` | `Room.java:25`, `:51`; `IRoom.java:21` |
| `boostDuration` | `int` | `5` Minuten | `Room.java:26`, `:52`; `IRoom.java:14` |
| `boostValveAngle` | `int` | `80` Grad | `Room.java:27`, `:53`; `IRoom.java:15` |
| `decalcificationDay` | `DayOfWeek` | `Saturday` | `Room.java:28`, `:54`; `IRoom.java:22` |
| `decalcificationHour` | `int` | `12` | `Room.java:29`, `:55`; `IRoom.java:16` |
| `maximumNoOfHeatingThermostats` | `int` | `8` | `Room.java:30`, `:56`; `IRoom.java:17` |
| `maximumNoOfWallMountedThermostats` | `int` | `1` | `Room.java:31`, `:57`; `IRoom.java:18` |
| `maximumNoOfShutterContacts` | `int` | `8` | `Room.java:32`, `:58`; `IRoom.java:19` |
| `temporaryModeStopDate` | `Date` | kein Default gesetzt | `Room.java:33` |
| `masterDeviceRadioAddress` | `int` | `0` (= `MasterDeviceUnknown`) | `Room.java:34`, `:59`; `IRoom.java:8` |
| `weekTemperatureProfile` | `IWeekTemperatureProfile` | `WeekTemperatureProfileBuilder.defaultProfile()` | `Room.java:35`, `:60` |
| `setPointTemperature` | `double` | `actualSetPointTemperature().getValue()` | `Room.java:36`, `:61` |
| `setPointTemperatureValid` | `boolean` | `true` | `Room.java:37`, `:62` |
| `currentAutoTemperature` | `double` | `actualSetPointTemperature().getValue()` | `Room.java:38`, `:63` |
| `actualTemperature` | `double` | `0.0` (Java-Default) | `Room.java:39` |
| `stateDirty` | `boolean` | `false` | `Room.java:40` |
| `stateChanged` | `boolean` | `false` | `Room.java:41` |

**Wichtiger Konflikt (belegt, nicht aufzulösen ohne Produktentscheidung):** Die Properties-Datei `extracted/essentials/max.properties` definiert abweichende Geräte-Defaults: Solltemperatur `20.0`, Eco `17.0`, Comfort `21.0`, Fenster-offen `17.0`, ControlMode `Permanently`, TemperatureMode `Normal`, Boost `15` Minuten, Ventilwinkel `100`, Entkalkungstag `6`, Stunde `12` (`max.properties:2-13`). Die Java-Konstruktoren verwenden `16.5 / 21.5 / 12.0 / Auto / NotAvailable / 5 / 80 / Saturday` (siehe Tabelle). **INFERENZ:** Die Properties stammen aus einer anderen Konfigurationsschicht (vermutlich Hardware-Initialisierung des Thermostats), während `Room.java` die Laufzeit-Defaults des Fachmodells vorgibt. Eine Neuimplementierung muss beide Quellen bewusst modellieren und darf sie nicht stillschweigend gleichsetzen.

### 3.3 Verhalten

- `isTemperatureControllable()` ist genau dann `true`, wenn ein Gerät vom Typ `HeatingThermostat` vorhanden ist (`Room.java:175-178`). `HeatingThermostatPlus` wird hier **nicht** geprüft – belegt durch den direkten Vergleich mit `DeviceType.HeatingThermostat` (`:177`).
- `getActualTemperature()` überschreibt den Wert mit der Ist-Temperatur des **letzten** gefundenen `WallMountedThermostatDeviceState` (`Room.java:212-221`).
- `getCurrentAutoTemperature()` ermittelt den Schaltpunkt des heutigen Tages anhand der Stunde (`Calendar.HOUR_OF_DAY`) (`Room.java:204-210`).
- `computeStateFlags()` setzt `stateChanged`, wenn ein Gerät Änderungen meldet, und setzt `stateDirty` auf `false` (`Room.java:136-144`).
- `resetChangedState()` setzt alle Geräteflags und anschließend das Raum-Flag zurück (`Room.java:146-152`).
- `setStateDirty()` setzt `stateDirty = true` und ruft vorher `resetChangedState()` (`Room.java:154-158`).
- `equals`/`hashCode` basieren ausschließlich auf `id` (`Room.java:160-173`).
- `getDevicesFiltered(int[])` und `getDevice(int)` filtern nach Funkadresse (`Room.java:95-118`).
- `getMasterThermostat()` liefert das Gerät mit der `masterDeviceRadioAddress` (`Room.java:368-371`).

**Lebenszyklus:** Ein Raum entsteht über `MaxLocalApi.addRoom(String)` (`MaxLocalApi.java:66`), wird über `changeRoomName` umbenannt (`:83`) und über `removeRoom(int, boolean)` gelöscht (`:95`). Die Reihenfolge wird über `rearrangeRoomOrder(int[])` geändert (`:165`). Das Löschen von Geräten kann mit `deviceFactoryReset` kombiniert werden (`MaxLocalApi.java:91`, `:95`) – **UNBEKANNT** bleibt, welche Funksequenz damit exakt ausgelöst wird; im vorliegenden Material ist nur die Signatur sichtbar.

---

## 4. `Device` – physisches Gerät

### 4.1 Zweck

`Device` repräsentiert ein physisches MAX!-Gerät mit Funkadresse, Seriennummer, Name, Raumzuordnung, Firmware und typspezifischem Zustandsobjekt.

**Beleg:** `decompiled/essentials/sources/de/eq3/max/al/core/cubestate/Device.java:13-25`.

### 4.2 Properties, Typen und Defaults

| Property | Typ | Default | Quelle |
|---|---|---|---|
| `deviceType` | `DeviceType` | kein Default gesetzt | `Device.java:14` |
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

Das DTO weicht bewusst ab: `radioAddress=-1`, `serialNumber="EEE0000000"`, `name="GENERIC DEVICE"`, `deviceType=GenericDevice`, `state=null`, `stateInfo=Valid`, `radioState=Ok` (`decompiled/essentials/sources/de/eq3/max/cl/dto/Device.java:5-11`). `NewDevice` kennt nur Funkadresse, Seriennummer und Typ mit denselben Platzhalter-Defaults (`cl/dto/NewDevice.java:5-7`).

### 4.3 Verhalten

- `setDeviceType(...)` erzeugt abhängig vom Enum das passende Zustandsobjekt: `ShutterContact` → `ShutterContactDeviceState`, `HeatingThermostat` → `HeatingThermostatDeviceState`, `WallMountedThermostat` → `WallMountedThermostatDeviceState`, `PushButton` → `PushButtonDeviceState` (`Device.java:82-99`). Für `HeatingThermostatPlus` und `Invalid` wird dort **kein** Zustand erzeugt; das ist ein belegter Sonderfall (`:85-98`, Switch-Map `:103-124`).
- `getRadioAddressBytes()` zerlegt die Adresse in drei Bytes (`Device.java:126-133`).
- `getSerialNumberBytes()` liefert exakt 10 Bytes, fehlende Zeichen werden mit Leerzeichen aufgefüllt (`Device.java:135-144`).
- `getState(Class<T>)` ist ein nicht typsicherer Cast auf das gesetzte Zustandsobjekt (`Device.java:151-154`).

**Lebenszyklus:** Neue Geräte erscheinen in `getNewDevices()` (`MaxLocalApi.java:54`), werden über `arrangeNewDevice(int, String, String)` einem Raum zugeordnet (`:79`) oder über `arrangeNewHouseSpecificDevice(int, String)` dem House (`:75`). Umbenennen: `changeDeviceName(int, int, String)` (`:87`); Verschieben: `reArrangeDevice(String, int, String)` (`:178`); Entfernen: `removeDevices(int[], boolean)` (`:91`) bzw. `removeHouseSpecificDevices(int[])` (`:161`). Die Suche wird mit `cancelGetNewDevices()` beendet (`:62`).

---

## 5. Gerätetypen (`DeviceType`)

### 5.1 AL-Enum

| Wert | Numerischer Code | Quelle |
|---|---|---|
| `Invalid` | `256` | `decompiled/essentials/sources/de/eq3/max/al/core/cubestate/DeviceType.java:5` |
| `HeatingThermostat` | `1` | `DeviceType.java:6` |
| `HeatingThermostatPlus` | `2` | `DeviceType.java:7` |
| `WallMountedThermostat` | `3` | `DeviceType.java:8` |
| `ShutterContact` | `4` | `DeviceType.java:9` |
| `PushButton` | `5` | `DeviceType.java:10` |

`fromValue(int)` liefert bei unbekanntem Code `Invalid` (`DeviceType.java:22-30`).

### 5.2 DTO-Enum und Abweichungen

Das DTO kennt nur `GenericDevice`, `ShutterContact`, `HeatingThermostat`, `WallMountedThermostat`, `PushButton` (`decompiled/essentials/sources/de/eq3/max/cl/dto/DeviceType.java:5-9`). `HeatingThermostatPlus` und `Invalid` fehlen. Die Browser-Schicht korrigiert den Typ nachträglich über `ActionHelperClass.getDeviceType(device)`, weil Geräte mit Seriennummern `xxx9`, `xxx8` oder `xxx7` einen „extra datatype“ hätten (`extracted/rootwar/js/models/maxCubeModel.js:90-95`). **INFERENZ:** Damit wird `HeatingThermostatPlus` auf Clientseite aus der Seriennummer rekonstruiert; die genaue Regex liegt in `js/lib/ActionHelper.js` und ist im vorliegenden Auszug nicht im Detail belegt. **UNBEKANNT:** Ob die Seriennummernregel auch für `Invalid` greift.

---

## 6. Zustandsobjekte (`DeviceState`)

### 6.1 Gemeinsame Basis

`AbstractDeviceState` hält `stateChanged=false`, bietet `isStateChanged()`, `resetChangedState()` und die geschützte Hilfsmethode `change(actual, newValue)`, die das Flag setzt, wenn sich Werte unterscheiden (`decompiled/essentials/sources/de/eq3/max/al/core/cubestate/devices/AbstractDeviceState.java:4-19`). Das Interface `IDeviceState` deklariert nur `resetChangedState()` (`devices/IDeviceState.java:4-7`).

### 6.2 `HeatingThermostatDeviceState`

Zweck: Gerätezustand eines Heizkörperthermostats mit Solltemperatur, Wochenprofil, Boost, Fenster-offen, Entkalkung und Batterie-/Sendefehler.

| Property | Typ | Default | Quelle |
|---|---|---|---|
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
| `vacationEndDay/Month/MonthLSB/Year/Time` | `int` | kein expliziter Default | `:36-40` |

Weitere belegte Konstanten: `DEFAULT_WINDOW_OPEN_DURATION=15`, `DEFAULT_MAXIMUM_VALVE=100`, `DEFAULT_VALVE_OFFSET=0` (`IHeatingThermostatDeviceState.java:11-13`). `setSetPointTemperature` meldet über `change(...)` Änderungen (`HeatingThermostatDeviceState.java:83-85`); `internal_setSetPointTemperature(int)` nutzt `Temperature.fromInt` (`:89-90`).

Das DTO reduziert den Zustand auf `isBatteryLow`, `transmitError`, `setPointTemperature=4.5`, `temperatureOffset=0.0` (`cl/dto/HeatingThermostatDeviceState.java:5-8`).

### 6.3 `WallMountedThermostatDeviceState`

Zweck: Wandthermostat mit zusätzlicher Ist-Temperaturmessung und Anzeigeoption.

| Property | Typ | Default | Quelle |
|---|---|---|---|
| `actualTemperature` | `double` | `0.0` | `WallMountedThermostatDeviceState.java:18` |
| `vacationEnd*` | `int` | kein expliziter Default | `:19-23` |
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

Das DTO enthält zusätzlich `actualTemperature`, `displayActual` und `temperatureOffset` (`cl/dto/WallMountedThermostatDeviceState.java:5-10`). `setSetPointTemperature` setzt das Änderungsflag (`:90-92`).

### 6.4 `ShutterContactDeviceState` und `PushButtonDeviceState`

- Fensterkontakt: `isBatteryLow`, `isWindowOpen`, `transmitError`, alle `false` als Java-Default; Getter/Setter belegt in `devices/ShutterContactDeviceState.java:7-43`. DTO ergänzt explizite Defaults `false` (`cl/dto/ShutterContactDeviceState.java:5-7`).
- Taster: `isBatteryLow`, `transmitError`, `upperButtonPressed`, `lowerButtonPressed` (`devices/PushButtonDeviceState.java:7-48`; `devices/IPushButtonDeviceState.java:7-19`). Das DTO bildet nur Batterie und Sendefehler ab (`cl/dto/PushButtonDeviceState.java:5-6`).

### 6.5 `DeviceStateInfo` und `RadioState`

`DeviceStateInfo`: `Valid`, `NotInitialized`, `OutOfDate` (`cubestate/DeviceStateInfo.java:5-7`; identisch im DTO `cl/dto/DeviceStateInfo.java:5-7`). `RadioState`: `Ok`, `Error`, `ControlFailed`, `ConfigFailed`, `AssignFailed`, `DeleteFailed` (`cubestate/RadioState.java:5-10`). **INFERENZ:** `RadioState` ist die fachliche Fehlerklasse für fehlgeschlagene Funkoperationen; eine Zuordnung zu konkreten Fehlercodes ist im Quellmaterial nicht direkt belegt.

---

## 7. `Temperature` – Wertobjekt mit Grenzen

### 7.1 Zweck

`Temperature` kapselt einen Temperaturwert und normiert die beiden Sonderzustände AUS und AN.

**Beleg:** `decompiled/essentials/sources/de/eq3/max/al/core/cubestate/Temperature.java:6-16`.

### 7.2 Konstanten und Regeln

| Konstante / Regel | Wert | Quelle |
|---|---|---|
| `OFF` | `4.5` | `Temperature.java:7` |
| `ON` | `30.5` | `Temperature.java:8` |
| `T_OFF` / `T_ON` | `new Temperature(4.5)` / `new Temperature(30.5)` | `:9-10` |
| `DEFAULT` | `21.0` | `:11` |
| `@Range` | `min=0, max=40` auf `value` | `:13-14` |
| Default-Konstruktor | `21.0` | `:18-20` |

### 7.3 Validierung und Normalisierung

`setValue(double)` setzt `on/off` zurück und normalisiert: Werte `<= 4.5` werden zu `4.5` mit `off=true`; Werte `>= 30.5` werden zu `30.5` mit `on=true`; dazwischen bleibt der Wert unverändert (`Temperature.java:31-49`). `isValid()` ist genau dann `true`, wenn weder `on` noch `off` gesetzt ist (`:61-64`). `setOn()` und `setOff()` rufen `setValue(30.5)` bzw. `setValue(4.5)` auf (`:66-74`). `fromInt(int)` teilt durch `2` (`:76-80`).

`equals` vergleicht gültige Werte mit einer Toleranz `< 0.1`; für ON/OFF wird auf Flags verglichen (`:82-93`). `toString()` formatiert als `%1$.1fºC` (`:95-97`).

**Belegter Bruch:** Das DTO `TemperatureProfileSwitchPoint` nutzt `@Range(min=0, max=40)` auf einem primitiven `double` und `Date stop` statt `Calendar` (`cl/dto/TemperatureProfileSwitchPoint.java:10-14`). **INFERENZ:** Die DTO-Schicht verzichtet auf die ON/OFF-Semantik und überträgt nur den numerischen Wert.

---

## 8. Betriebsmodi und Fachzustände

### 8.1 `RoomControlMode`

Enum: `Auto`, `Permanently`, `Temporary`, `NotAvailable` (`cubestate/RoomControlMode.java:5-8`; DTO identisch `cl/dto/RoomControlMode.java:5-8`).

| Modus | Fachliche Bedeutung (belegt durch Kommando / Property) | Quelle |
|---|---|---|
| `Auto` | Solltemperatur folgt dem Wochenprofil; Default laut `IRoom` | `IRoom.java:20`; `SetRoomAutoMode.java:1` |
| `Permanently` | Feste Solltemperatur ohne Automatik; `setRoomPermanentMode(roomId, temperature)` | `MaxLocalApi.java` API über `APICalls.js:2162`; `SetRoomPermanentMode.java:1` |
| `Temporary` | Solltemperatur bis zu einem Enddatum; `setRoomTemporaryMode(roomId, temperature, date)` | `extracted/rootwar/js/models/maxCubeModel.js:148-163`; `SetRoomTemporaryMode.java:1` |
| `NotAvailable` | Kein steuerbarer Modus | `RoomControlMode.java:8` |

`Room` hält das Ende des temporären Modus in `temporaryModeStopDate` (`Room.java:33`, `:293-301`).

### 8.2 `RoomTemperatureMode`

Enum: `Eco`, `Comfort`, `Normal`, `NotAvailable` (`cubestate/RoomTemperatureMode.java:5-8`; DTO identisch `cl/dto/RoomTemperatureMode.java:5-8`). Die zugehörigen Temperaturen liegen im Raum vor: `ecoTemperature=16.5`, `comfortTemperature=21.5`, `maximumTemperature=30.5` (`Room.java:45-47`). **INFERENZ:** `Normal` entspricht der automatischen Profiltemperatur, `Eco`/`Comfort` den beiden konfigurierbaren Komfortwerten; explizit formuliert ist das nicht.

### 8.3 Weitere Betriebszustände

- **Fenster-offen**: `windowOpenTemperature=12.0`, `windowOpenDuration=15` (`Room.java:48-49`). Kommando `SetRoomWindowOpenTemperature` (`al/core/command/SetRoomWindowOpenTemperature.java:1`), DWR-Methode `APICalls.prototype.setRoomWindowOpenTemperature` (`extracted/rootwar/js/lib/APICalls.js:2338`).
- **Boost**: `boostDuration=5`, `boostValveAngle=80` (`Room.java:52-53`); Kommando `SetRoomConfigBoost` (`command/SetRoomConfigBoost.java:1`); API `APICalls.js:2387`.
- **Entkalkung**: `decalcificationDay=Saturday`, `decalcificationHour=12` (`Room.java:54-55`; `IRoom.java:16`, `:22`); Kommando `SetRoomConfigDecalcification` (`command/SetRoomConfigDecalcification.java:1`); API `APICalls.js:2413`.
- **Temperatur-Offset**: `valveOffset=0` am Thermostat (`HeatingThermostatDeviceState.java:29`), `temperatureOffset=0.0` (`IHeatingThermostatDeviceState.java:14`); Kommando `SetRoomConfigValveOffset` (`command/SetRoomConfigValveOffset.java:1`) und `SetRoomTemperatureOffset` (`command/SetRoomTemperatureOffset.java:1`).
- **Sommer-/Winterzeit**: `daylightSaving` am Cube (`MaxCubeState.java:23`, `:255-263`) und am Gerät (`HeatingThermostatDeviceState.java:18`); API `adjustTimeZoneConfiguration` / `setTimeZoneConfiguration` (`MaxLocalApi.java:197`, `:205`).

---

## 9. Wochenprofil (`WeekTemperatureProfile`)

### 9.1 Struktur und Kardinalität

`WeekTemperatureProfile` enthält `@Valid @Size(min = Constants.COOKIE_AGE_DAYS, max = Constants.COOKIE_AGE_DAYS) List<IDayTemperatureProfile>` (`cubestate/WeekTemperatureProfile.java:13-15`). `Constants.COOKIE_AGE_DAYS = 7` (imCheckpoint belegt, Quelle `de/eq3/max/al/core/Constants.java`). Damit gilt: **genau 7 Tagesprofile** pro Wochenprofil. Das DTO spiegelt dieselbe Annotation (`cl/dto/WeekTemperatureProfile.java:12-14`).

| Objekt | Kardinalität | Quelle |
|---|---|---|
| `Room` → `WeekTemperatureProfile` | 1 → 1 | `Room.java:35`, `:233-241` |
| `WeekTemperatureProfile` → `DayTemperatureProfile` | 1 → 7 | `WeekTemperatureProfile.java:13-15` |
| `DayTemperatureProfile` → `TemperatureProfileSwitchPoint` | 1 → 1..13 | `DayTemperatureProfile.java:16-18` |

### 9.2 `DayTemperatureProfile`

- `switchPoints`: `@Valid @Size(min=1, max=13)` (`DayTemperatureProfile.java:16-18`).
- `dayOfWeek`: `@NotNull` (`:20-21`), Konstruktor setzt den Wochentag (`:26-28`).
- `switchPointAt(int hour)` iteriert über normalisierte Punkte und liefert den ersten Punkt, dessen Stunde größer als `hour` ist oder gleich `0`; sonst `IllegalArgumentException` (`:76-85`).
- `getSwitchPointsNormalized()` sortiert in einem `TreeSet`, setzt beim letzten Punkt Stunde/Minute auf `0` (`:87-100`). **INFERENZ:** Der Tag endet damit implizit um 00:00 Uhr.
- `compareTo` vergleicht bei Gleichheit `0`, sonst den Wochentag (`:102-108`).

### 9.3 `TemperatureProfileSwitchPoint`

- `stop`: `@NotNull Calendar`, Default `Calendar.getInstance()` mit Stunde/Minute `0` (`TemperatureProfileSwitchPoint.java:10-11`, `:17-20`).
- `temperature`: `@NotNull @Valid ITemperature`, Default `new Temperature(21.5)` (`:13-15`, `:20`).
- `compareTo` vergleicht `hour*60+minute`; `0` wird als Tagesende gewertet (`:55-70`).
- `toString()`: `HH:MM - Temperatur` (`:72-74`).

DTO: `stop` ist `Date` mit `@NotNull`, `temperature` ist `double` mit `@Range(0..40)` (`cl/dto/TemperatureProfileSwitchPoint.java:10-14`).

### 9.4 Kommando und Client-Editor

`SetRoomWeekTemperatureProfile` ist das zugehörige Kommando (`command/SetRoomWeekTemperatureProfile.java:1`), `APICalls.prototype.setRoomWeekTemperatureProfile` der DWR-Aufruf (`extracted/rootwar/js/lib/APICalls.js:2264`). Der Browser-Editor implementiert Resize/Drag der Intervalle (`extracted/rootwar/js/com_configuration/controllers/weeklyProgramController.js:195-227`, `:353-579`), Fast-Editing (`:643-823`), Rundung auf 0,5 °C (`:1066`), Hinzufügen/Löschen von Intervallen (`:1152`, `:1407`) und Zeitvalidierung (`:1521-1536`). Der Default-Tagesplan des Clients erzeugt genau einen Schaltpunkt um 24:00 Uhr mit `21.5` (`extracted/rootwar/js/models/maxCubeModel.js:165-177`).

**Lebenszyklus:** Wochenprofil wird mit dem Raum initialisiert (`Room.java:60`), im Editor verändert (`weeklyProgramController.js:1293-1312`) und über `setRoomWeekTemperatureProfile` persistiert (`APICalls.js:2264`). **UNBEKANNT:** Ob die 13-Punkte-Grenze im Browser-Editor hart enforciert wird; die AL-Annotation ist belegt, die JS-Validierung nur teilweise sichtbar.

---

## 10. Weitere Wertobjekte

| Objekt | Zweck / Properties | Quelle |
|---|---|---|
| `NewDevice` | Gerät im Erkennungsmodus: Funkadresse, Seriennummer, Typ | `cubestate/NewDevice.java:1`; DTO `cl/dto/NewDevice.java:4-7` |
| `NetworkConfiguration` | IP, Subnetz, Gateway, DNS1, DNS2, DHCP, Verschlüsselung. Defaults `192.168.0.123`, `255.255.255.0`, `192.168.0.1`, `192.168.0.1`, `192.168.0.1`, `dhcpEnabled=true`, `encryptionEnabled` ohne Default | `cubestate/NetworkConfiguration.java:7-24`; DTO `cl/dto/NetworkConfiguration.java:5-10` (ohne `encryptionEnabled`) |
| `PushButtonConfiguration` | `upperKey`, `lowerKey`, je `PushButtonKeyConfiguration` | `cubestate/PushButtonConfiguration.java:4-6` |
| `PushButtonKeyConfiguration` / `PushButtonKeyMode` | Tastenbelegung und -modus | `cubestate/PushButtonKeyConfiguration.java:1`, `cubestate/PushButtonKeyMode.java:1` |
| `DaylightSavingConfiguration` | Sommer-/Winterzeit-Parameter für `adjustTimeZoneConfiguration` | `MaxLocalApi.java:197`, `cubestate/DaylightSavingConfiguration.java:1` |
| `MaxCubeLastPing` | Letzter Airtime-Status des Cubes | `cubestate/MaxCubeLastPing.java:1`; `MaxLocalApi.java:104` |
| `MaxCubeAirtimeStatus` | Ergebnis von `stillAlivePing()` | `MaxLocalApi.java:104`; `cubestate/MaxCubeAirtimeStatus.java:1` |
| `FirmwareInfo` | Firmware-Version/- Prüfung (`getLatestFirmwareVersion`) | `MaxLocalApi.java:145`; `cubestate/FirmwareInfo.java:1` |
| `SoftwareInformation` | `downloadURL`, `version`, `updateAvailable`; DTO-Default `""`, `"0.0.1"`, `false` | `cl/dto/SoftwareInformation.java:7-20` |
| `UpdateStatus` | `finished`, `steps`, `currentStep`, `info`; Default `true`, `0`, `0`, `"INVALID"` | `cl/dto/UpdateStatus.java:7-17` |
| `ProductActivation` / `ProductInformation` | Fernzugriffs-Aktivierung und Produktinfo | `MaxLocalApi.java:221`, `:225` |
| `RemoteCredentials` | `userName`, `remoteAccessEnabled` | `cl/dto/RemoteCredentials.java:5-6`; `MaxLocalApi.java:124` |
| `AdditionalInformation` | Zusatzinformationen (`getAdditionalInformation`) | `MaxLocalApi.java:233` |
| `CubeInfo` | Ergebnis der Cube-Erkennung (`listAvailableMaxCubes`) | `MaxLocalApi.java:133`; `cl/dto/CubeInfo.java:1` |
| `Metadata` / `RawCubeState` | Rohdaten des Cube-Status | `cubestate/Metadata.java:1`, `cubestate/RawCubeState.java:1` |

Konfigurationswerte, die das Domänenverhalten begrenzen: `maxRoomCount=20`, `maxDeviceCount=140`, `maxNameLength=32`, `inclusionModeTimeoutSeconds=60`, `cubeScanTime=1800`, `latestCubeFirmwareVersion=275`, `leastCompatibleFirmwareVersion=273`, `idleTimeBeforeLocalAppDisconnect=30000`, `cubeIdleTimeToSend=900000` (`extracted/settings/max.local.properties:1-11`, `:17-18`, `:4-5`).

---

## 11. Beziehungen und Kardinalitäten (Gesamtübersicht)

| Beziehung | Kardinalität | Quelle |
|---|---|---|
| `MaxCubeState` → `House` | 1 → 0..1 | `MaxCubeState.java:22`, `:35-43` |
| `MaxCubeState` → `Room` | 1 → 0..20 (konfiguriert) | `MaxCubeState.java:30`, `:45-48`; `max.local.properties:2` |
| `MaxCubeState` → `IDevice` (nicht zugeordnet) | 1 → 0..n | `MaxCubeState.java:31`, `:171-173` |
| `MaxCubeState` → `PushButtonConfiguration` | 1 → 1 | `MaxCubeState.java:33`, `:285-293` |
| `House` → `IDevice` | 1 → 0..n | `House.java:9`, `:17-20` |
| `Room` → `IDevice` | 1 → 0..n, pro Typ begrenzt | `Room.java:18`, `:90-123`; `IRoom.java:17-19` |
| `IDevice` → `Room` | n → 1 (`roomId`) | `Device.java:19`, `:57-65` |
| `IDevice` → `IDeviceState` | 1 → 1, typabhängig erzeugt | `Device.java:21`, `:82-99` |
| `Room` → `WeekTemperatureProfile` | 1 → 1 | `Room.java:35`, `:233-241` |
| `WeekTemperatureProfile` → `DayTemperatureProfile` | 1 → 7 | `WeekTemperatureProfile.java:13-15` |
| `DayTemperatureProfile` → `TemperatureProfileSwitchPoint` | 1 → 1..13 | `DayTemperatureProfile.java:16-18` |
| `TemperatureProfileSwitchPoint` → `ITemperature` | 1 → 1 | `TemperatureProfileSwitchPoint.java:13-15` |
| `Room` → `Device` (Master) | 1 → 0..1 über Funkadresse | `Room.java:34`, `:363-381` |

**INFERENZ zur Identität:** Räume werden über `id`, Geräte über `radioAddress` identifiziert (`Room.java:160-173`; `Device.java:27-35`; `Room.java:110-118`). Namen sind nicht identitätsstiftend, aber fachlich eindeutig zu halten (siehe Validierung).

---

## 12. Validierungsregeln

| Regel | Implementierung | Exception / Verhalten | Quelle |
|---|---|---|---|
| Gerätename darf nicht `null` sein | `assertNameLength` | `NameIsNullException` | `ConfigureDeviceValidator.java:148-151` |
| Gerätename ≤ `maxNameLength` (32) | `assertNameLength` | `NameTooLongException` | `:152-154`; `max.local.properties:1` |
| Gerätename eindeutig (case-insensitive) | `getDeviceByName` | `DeviceNameAlreadyExistsException` | `:50-55`, `:123-138` |
| Raumname eindeutig (case-insensitive) | `getRoom` + `assertUniqueDeviceName` | `DeviceNameAlreadyExistsException` bei Gerätenamen im Raum | `:104-121` |
| Funkadresse muss unter `NewDevices` liegen | `assertExistingNewDevice` | `InvalidDeviceRadioAddressException` | `:141-145`, `:157-164` |
| Pro Raum: Heizthermostate ≤ 8 | `assertConstraintDeviceTypeRoom` | `MaximumNoOfDevicesWithThisTypeInRoomException` | `:80-99` |
| Pro Raum: Wandthermostate ≤ 1 |ebenso |ebenso | `:84-85` |
| Pro Raum: Fensterkontakte ≤ 8 |ebenso |ebenso | `:86-87` |
| Sonstige Gerätetypen ≤ 50 | `maxNoOfDevices = 50` |ebenso | `:81` |
| Benutzername darf nicht leer sein | `validateUserName` | `NoUserNameException` | `RemoteCredentialsValidator.java:24-27` |
| Benutzername muss `[^\r\n,]+` matchen | `matches(UNALLOWED_CHARS)` | `InvalidUserNameException` | `:12`, `:28-30` |
| User/Passwort gesetzt, Remote aber aus | `validateArguments` | `InvalidArgumentsException` | `:35-38` |
| Softwareinfo genau 3 Werte | `SoftwareInformationValidator` | `SoftwareInformationHasInvalidCountOfArgumentsException` | `SoftwareInformationValidator.java:14-17` |
| Erster Wert matches `\d+.\d+.\d+` |ebenso | `SoftwareVersionIsInvalidException` | `:18-20` |
| Temperatur im Bereich 0..40 | `@Range` | Bean-Validation | `Temperature.java:13`; `cl/dto/TemperatureProfileSwitchPoint.java:13` |
| Wochenprofil genau 7 Tage | `@Size(min=COOKIE_AGE_DAYS, max=...)` | Bean-Validation | `WeekTemperatureProfile.java:14` |
| Tagesprofil 1..13 Schaltpunkte | `@Size` | Bean-Validation | `DayTemperatureProfile.java:17` |
| `stop` und `temperature` nicht null | `@NotNull` | Bean-Validation | `TemperatureProfileSwitchPoint.java:10`, `:13` |
| `dayOfWeek` nicht null | `@NotNull` | Bean-Validation | `DayTemperatureProfile.java:20-21` |
| Ungültige Raum-Id beim Sortieren | `rearrangeRoomOrder` | `ValidationException("invalid room id")` | `MaxCubeState.java:93-95` |

**Hinweis zur Regex:** `[^\r\n,]+` ist als *Match des gesamten Strings* zu lesen (`String.matches`), d. h. der Benutzername darf keine Zeilenumbrüche und kein Komma enthalten; Leerzeichen sind nicht ausgeschlossen. Die Konstante heißt irreführend `UNALLOWED_CHARS` (`RemoteCredentialsValidator.java:12`). Die Softwareversions-Regex `\d+.\d+.\d+` nutzt ungepunktete Punkte (beliebiges Zeichen), ist also streng genommen weiter als eine Semver-Prüfung (`:18`). **INFERENZ:** Eine Neuimplementierung sollte die Absicht (drei Ziffernblöcke) prüfen, ohne den Fehler der Punkt-Escapierung zu übernehmen.

Fehlercode-Zuordnung (Auszug, vollständige Tabelle `extracted/essentials/errorcodes.properties:1-53`): `DefaultErrorCode/GeneralTechnicalFailure=MAX0001`, `DeviceNameAlreadyExists=MAX1015`, `InvalidDeviceRadioAddress=MAX1022`, `NameIsNull=MAX1029`, `NameTooLong=MAX1030`, `NoUserName=MAX1031`, `RemoteAccessDisabled=MAX1037`, `SoftwareVersionIsInvalid=MAX1044`, `Validation=MAX1050`, `WrongSize=MAX1051`. **Befunderhaltung:** `RemoteServiceUnavailable=MAX10038` weicht von der sonstigen `MAX10xx`-Struktur ab (`:40`); der Wert ist so zu übernehmen, nicht zu „korrigieren“.

---

## 13. DTO ↔ AL-Paket-Zuordnung

| Fachobjekt | AL-Klasse (`de.eq3.max.al.core...`) | DTO-Klasse (`de.eq3.max.cl.dto`) | Wesentliche Abweichung | Quelle |
|---|---|---|---|---|
| Cube-Zustand | `cubestate.MaxCubeState` | `cl.dto.MaxCubeState` | DTO hat `house=new House()`, `rooms=[]`, `@XmlRootElement`; AL hat zusätzlich `invCube`, `rawState`, `illegalLoginCount`, `deviceAppUrl` | `MaxCubeState.java:12-33`; `cl/dto/MaxCubeState.java:9-21` |
| Haus | `cubestate.House` | `cl.dto.House` | DTO-Haus ist einfacher Container | `House.java:7-9`; `cl/dto/House.java:1` |
| Raum | `cubestate.Room` (implements `IRoom`) | `cl.dto.Room` | DTO: `id=-1`, `order=-1`, `name="GENERIC ROOM"`, `isTemperatureControllable/stateDirty/stateChanged` als explizite Felder | `Room.java:14-64`; `cl/dto/Room.java:9-35` |
| Gerät | `cubestate.Device` | `cl.dto.Device` | DTO: `radioAddress=-1`, `EEE0000000`, `GENERIC DEVICE`, `GenericDevice`, `stateInfo=Valid` | `Device.java:13-25`; `cl/dto/Device.java:5-11` |
| Neues Gerät | `cubestate.NewDevice` / `INewDevice` | `cl.dto.NewDevice` | DTO nur 3 Felder | `cubestate/NewDevice.java:1`; `cl/dto/NewDevice.java:4-7` |
| Gerätezustand (Basis) | `devices.AbstractDeviceState` / `IDeviceState` | `cl.dto.DeviceState` | DTO abstrakt ohne Felder | `devices/AbstractDeviceState.java:4-19`; `cl/dto/DeviceState.java:4-5` |
| Heizthermostat-Zustand | `devices.HeatingThermostatDeviceState` | `cl.dto.HeatingThermostatDeviceState` | AL kennt Boost, Profil, Entkalkung, Ventil; DTO nur 4 Felder | `HeatingThermostatDeviceState.java:17-40`; `cl/dto/HeatingThermostatDeviceState.java:5-8` |
| Wandthermostat-Zustand | `devices.WallMountedThermostatDeviceState` | `cl.dto.WallMountedThermostatDeviceState` | DTO führt `actualTemperature`, `displayActual`, `temperatureOffset` | `WallMountedThermostatDeviceState.java:17-41`; `cl/dto/WallMountedThermostatDeviceState.java:5-10` |
| Fensterkontakt-Zustand | `devices.ShutterContactDeviceState` | `cl.dto.ShutterContactDeviceState` | DTO-Defaults explizit `false` | `devices/ShutterContactDeviceState.java:7-10`; `cl/dto/ShutterContactDeviceState.java:5-7` |
| Taster-Zustand | `devices.PushButtonDeviceState` | `cl.dto.PushButtonDeviceState` | DTO kennt keine Tastendrucke | `devices/PushButtonDeviceState.java:7-9`; `cl/dto/PushButtonDeviceState.java:5-6` |
| Gerätetyp | `cubestate.DeviceType` (6 Werte, numerisch) | `cl.dto.DeviceType` (5 Werte, keine Codes) | `Invalid(256)`, `HeatingThermostatPlus(2)` fehlen im DTO | `DeviceType.java:5-10`; `cl/dto/DeviceType.java:5-9` |
| Zustandsinfo | `cubestate.DeviceStateInfo` | `cl.dto.DeviceStateInfo` | identisch | `DeviceStateInfo.java:5-7`; `cl/dto/DeviceStateInfo.java:5-7` |
| Funkzustand | `cubestate.RadioState` | `cl.dtо.RadioState` | identisch (6 Werte) | `RadioState.java:5-10`; `cl/dto/RadioState.java:1` |
| Steuerungsmodus | `cubestate.RoomControlMode` | `cl.dto.RoomControlMode` | identisch | `RoomControlMode.java:5-8`; `cl/dto/RoomControlMode.java:5-8` |
| Temperaturmodus | `cubestate.RoomTemperatureMode` | `cl.dto.RoomTemperatureMode` | identisch | `RoomTemperatureMode.java:5-8`; `cl/dto/RoomTemperatureMode.java:5-8` |
| Wochenprofil | `cubestate.WeekTemperatureProfile` | `cl.dto.WeekTemperatureProfile` | beide `@Valid @Size(7)` | `WeekTemperatureProfile.java:13-15`; `cl/dto/WeekTemperatureProfile.java:12-14` |
| Tagesprofil | `cubestate.DayTemperatureProfile` | `cl.dto.DayTemperatureProfile` | beide `@Size(1..13)`, `@NotNull dayOfWeek` | `DayTemperatureProfile.java:16-21`; `cl/dto/DayTemperatureProfile.java:12-17` |
| Schaltpunkt | `cubestate.TemperatureProfileSwitchPoint` (`Calendar` + `ITemperature`) | `cl.dto.TemperatureProfileSwitchPoint` (`Date` + `double`) | Typunterschied, ON/OFF-Semantik verloren | `TemperatureProfileSwitchPoint.java:10-15`; `cl/dto/TemperatureProfileSwitchPoint.java:10-14` |
| Netzwerk | `cubestate.NetworkConfiguration` | `cl.dto.NetworkConfiguration` | AL zusätzlich `encryptionEnabled` | `NetworkConfiguration.java:9-15`; `cl/dto/NetworkConfiguration.java:5-10` |
| Tasterkonfiguration | `cubestate.PushButtonConfiguration` | `cl.dto.PushButtonConfiguration` | beide im Cube-State gehalten | `PushButtonConfiguration.java:4-6`; `cl/dto/PushButtonConfiguration.java:1` |
| Fernzugriff | AL-Settings + `MaxCubeState.remoteAccess*` | `cl.dto.RemoteCredentials` | DTO nur `userName` + `remoteAccessEnabled`; Passwort/Session nicht im DTO | `MaxCubeState.java:18-21`, `:205-242`; `cl/dto/RemoteCredentials.java:5-6` |
| Softwareupdate | `de.eq3.max.al.local.update...` | `cl.dto.SoftwareInformation`, `cl.dto.UpdateStatus` | DTO-Defaults `"0.0.1"/false` und `INVALID` | `cl/dto/SoftwareInformation.java:17-21`; `cl/dto/UpdateStatus.java:12-17` |

**INFERENZ zur Mapping-Richtung:** Die AL→DTO-Richtung ist eine Reduktion auf UI-relevante Felder; DTO→AL-Richtung (z. B. `setClientCommands`) wirkt über Kommandos, nicht über direktes Setzen von AL-Feldern (`MaxLocalApi.java:99`; `command/ClientCommand.java:1`). Eine Neuimplementierung sollte daher DTOs nie direkt in das AL-Aggregat schreiben.

---

## 14. Anwendungsfälle / Kommandokatalog

Die `al/core/command`-Pakete enthalten die fachlichen Schreiboperationen. Vollständige Liste (Quelle: Verzeichnis `decompiled/essentials/sources/de/eq3/max/al/core/command/`):

| Kommando | Fachliche Operation | Quelle |
|---|---|---|
| `SetRoomAutoMode` | Raum auf Automatik setzen | `SetRoomAutoMode.java:1` |
| `SetRoomAutoModeWithTemperature` | Automatik mit Temperaturmodus | `SetRoomAutoModeWithTemperature.java:1` |
| `SetRoomPermanentMode` | Dauerbetrieb mit Solltemperatur | `SetRoomPermanentMode.java:1` |
| `SetRoomTemporaryMode` | Temporärer Modus mit Enddatum | `SetRoomTemporaryMode.java:1` |
| `SetRoomWeekTemperatureProfile` | Wochenprofil übernehmen | `SetRoomWeekTemperatureProfile.java:1` |
| `SetRoomEcoTemperature` | Eco-Temperatur ändern | `SetRoomEcoTemperature.java:1` |
| `SetRoomComfortTemperature` | Comfort-Temperatur ändern | `SetRoomComfortTemperature.java:1` |
| `SetRoomMaximumTemperature` | Maximaltemperatur ändern | `SetRoomMaximumTemperature.java:1` |
| `SetRoomWindowOpenTemperature` | Fenster-offen-Temperatur ändern | `SetRoomWindowOpenTemperature.java:1` |
| `SetRoomConfigBoost` | Boost-Dauer/Ventilwinkel konfigurieren | `SetRoomConfigBoost.java:1` |
| `SetRoomConfigDecalcification` | Entkalkungszeitpunkt konfigurieren | `SetRoomConfigDecalcification.java:1` |
| `SetRoomConfigValveOffset` | Ventil-Offset konfigurieren | `SetRoomConfigValveOffset.java:1` |
| `SetRoomTemperatureOffset` | Temperatur-Offset konfigurieren | `SetRoomTemperatureOffset.java:1` |
| `SetDeviceDisplaySetPointActual` | Wandthermostat-Anzeige umschalten | `SetDeviceDisplaySetPointActual.java:1` |
| `WallMountedThermostatDisplayConfigCommand` | Anzeige konfigurieren (Alias/Variant) | `WallMountedThermostatDisplayConfigCommand.java:1` |
| `SetDeviceDaylightSavingMode` | Sommerzeit am Gerät setzen | `SetDeviceDaylightSavingMode.java:1` |
| `AssociateNewDeviceToRoom` | Neues Gerät einem Raum zuordnen | `AssociateNewDeviceToRoom.java:1` |
| `DisassociateDeviceFromRoom` | Gerät aus Raum lösen | `DisassociateDeviceFromRoom.java:1` |
| `ClientCommand` / `IClientCommand` | Kommando-Vertrag und Sammelkommando | `ClientCommand.java:1`, `IClientCommand.java:1` |
| `CanNotUpdateStateException` | Fehlerfall bei Zustandsaktualisierung | `CanNotUpdateStateException.java:1` |

Zugehörige DWR-Methoden der Browser-Schicht (Auswahl, vollständig `extracted/rootwar/js/lib/APICalls.js:2133-2458`): `setRoomAutoMode` (`:2133`), `setRoomPermanentMode` (`:2162`), `setRoomAutoWithTemperatureMode` (`:2193`), `setRoomTemporaryMode` (`:2225`), `setRoomWeekTemperatureProfile` (`:2264`), `setRoomEcoTemperature` (`:2289`), `setRoomComfortTemperature` (`:2313`), `setRoomWindowOpenTemperature` (`:2338`), `setRoomMaximumTemperature` (`:2361`), `setRoomConfigBoost` (`:2387`), `setRoomConfigDecalcification` (`:2413`), `setRoomTemperatureOffset` (`:2440`), `setDeviceDisplaySetPointActual` (`:2458`). Das Client-Modell kennt einen Sammelmodus: `setClientCommandAssembleMode()` / `setClientCommandImmediateMode()` / `executeClientCommands()` (`extracted/rootwar/js/models/maxCubeModel.js:178-200`).

---

## 15. Explizite Offenheiten

1. **UNBEKANNT** – Die genaue Hardware-Semantik von `deviceFactoryReset` (`MaxLocalApi.java:91`, `:95`) ist nicht belegt.
2. **UNBEKANNT** – Die vollständige Regex zur Ableitung von `HeatingThermostatPlus` aus der Seriennummer liegt nicht im ausgewerteten Auszug (`maxCubeModel.js:90-95` verweist auf `ActionHelperClass`).
3. **UNBEKANNT** – Ob `HeatingThermostatPlus` in `Room.isTemperatureControllable()` berücksichtigt werden sollte; belegt ist nur, dass der Code es nicht tut (`Room.java:176-178`).
4. **INFERENZ** – `Room` ist die fachliche Steuereinheit, nicht das Gerät; begründet durch die Kommando-Namensgebung (`SetRoom*`) und die UI-Navigation über Räume.
5. **INFERENZ** – Die 7-Tage-Regel folgt aus `Constants.COOKIE_AGE_DAYS = 7`; die Konstante ist im vorliegenden Auszug nicht erneut geöffnet worden, aber in beiden Profilklassen referenziert (`WeekTemperatureProfile.java:14`; `cl/dto/WeekTemperatureProfile.java:13`).
6. **UNBEKANNT** – Bedeutung und Aufbau von `AdditionalInformation` im Detail (`MaxLocalApi.java:233`).
7. **INFERENZ** – Die Properties `max.properties` und die Java-Konstruktor-Defaults beschreiben zwei unterschiedliche Default-Ebenen; eine Neuimplementierung muss beide getrennt führen (Belege in Abschnitt 3.2).
8. **UNBEKANNT** – Ob `encryptionEnabled` der AL-`NetworkConfiguration` über die DTO-Schicht überhaupt gesetzt werden kann; das DTO hat kein entsprechendes Feld (`cl/dto/NetworkConfiguration.java:5-10`).

---

## 16. Umsetzung der Value Objects und Enums in C# (24.11.2026)

`src/MaxCube.Domain/` enthält die immutable Entsprechungen der obigen Wertobjekte. Die Enum-Liste ist exhaustiv aus den Java-Quellen abgeleitet (AP-2.1, Kriterium 1):

| C#-Typ | Java-Quelle | Besonderheit |
|---|---|---|
| `Temperature` | `Temperature.java:6-98` | OFF 4,5 / ON 30,5 / DEFAULT 21,0; exakter Wertvergleich statt `< 0,1`-Toleranz (`:82-93`) |
| `RadioAddress` | `Device.java:126-133`, `CubeConnection.java:55-56,297-302` | 24 Bit, drei Bytes big-endian; sechs Hex-Ziffern nur in den Beispielen |
| `SerialNumber` | `Device.java:135-144`, `errorcodes.properties:42-43` | zehn Byte mit Leerzeichen-Padding; Pattern `UNBEKANNT` |
| `DeviceType` | `DeviceType.java:4-30` | `Invalid=256`, 1…5, unbekannt → `Invalid` |
| `RoomControlMode` | `RoomControlMode.java:4-9` | 4 Werte |
| `RoomTemperatureMode` | `RoomTemperatureMode.java:4-9` | 4 Werte |
| `DeviceStateInfo` | `DeviceStateInfo.java:4-8` | 3 Werte |
| `RadioState` | `RadioState.java:4-11` | 6 Werte |
| `DayOfWeek` | `DayOfWeek.java:6-45` | **Monday=1…Sunday=7**, nicht `System.DayOfWeek`; `fromCalendar` rechnet `((tag + 5) % 7) + 1` |
| `PushButtonKeyMode` | `PushButtonKeyMode.java:4-10` | 5 Werte |

Die Fehlercodes `MAX1022`, `MAX1034`, `MAX1035`, `MAX1040` werden im C#-Code mit einer als `INFERENZ` gekennzeichneten Bedeutung verwendet (`errorcodes.properties:24,36,37,42`); die Java-Quellen enthalten für MAX1034/1035/1040 keine auslösende Stelle. `MAX1041` bleibt ohne Pattern-Implementierung.
