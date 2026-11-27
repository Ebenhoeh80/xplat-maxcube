> English translation of docs/gui-specification.md. Machine-created; DE/EN reviewed by the user on 27.11.2026; FR/IT/NL/PL feedback expected via issues/PRs.

# GUI specification – MAX! Cube Local Application

Technical specification of the user interface, reconstructed from the local sources. A distinction is made between **(A) the desktop shell** (Java/Swing/AWT, `MaxLocalApp.jar`) and **(B) the browser interface** (HTML + jQuery MVC, delivered as WAR root). The presentation is a requirements basis for a new build, not a 1:1 port.

## 0. Source basis and marking

| Source | Content | Path |
| --- | --- | --- |
| HTML layout | 2586 lines of static markup with `j_*` classes and `@@token@@` placeholders | `extracted/rootwar/index.html` |
| MVC framework | Controller/view/model registration | `extracted/rootwar/js/framework/mvc.js` |
| Controllers | Interaction logic | `extracted/rootwar/js/controllers/`, `js/com_configuration/controllers/`, `js/com_structure/controllers/` |
| Views | Selector and template definitions | `extracted/rootwar/js/views/`, `js/com_configuration/views/`, `js/com_structure/views/` |
| DTO bridge | DWR wrapper, 120 s timeout | `extracted/rootwar/js/lib/APICalls.js:18-41` |
| Language | Six language files | `extracted/rootwar/js/plugins/lang/jquery.translate.lang_{de,en,fr,it,nl,pl}.js` |
| Swing shell | Startup, tray, proxy, bug report, update | `decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/` |
| Shell texts | Resource bundles | `extracted/maxlocalapp/MaxLocalAppStarter*.properties` |

Marking as in `domain-model.md`: **FACT** = directly evidenced, **INFERENCE** = derived from evidence, **UNKNOWN** = cannot be determined. Every view names at least one source location.

---

## 1. Architecture of the browser interface

### 1.1 MVC structure

The framework registers objects under `mvc.c.*` (controllers), `mvc.v.*` (views) and `mvc.m.*` (models). Each controller has a `defaultView` and an `initAction` (`extracted/rootwar/js/framework/mvc.js:1-346`). The start controller `mvc.c.IndexController` initializes the main model, the language, the environment (local/remote), the browser check and a 3-second timer for bundled temperature changes (`extracted/rootwar/js/controllers/indexController.js:86-123`, `:119-121`).

| Role | Object | Source |
| --- | --- | --- |
| Main controller | `mvc.c.IndexController` | `indexController.js:16`, `:86-123` |
| Room control | `mvc.c.RoomControlController` | `roomControlController.js:16`, `:79-108` |
| Main view | `mvc.v.IndexView` | `js/views/indexView.js:1-953` |
| State model | `mvc.m.MaxCubeModel` | `js/models/maxCubeModel.js:17-37` |
| DWR wrapper | `APICalls` | `js/lib/APICalls.js:18-41` |
| Helpers | `ActionHelperClass`, `ViewHelper` | `js/lib/ActionHelper.js:1`, `js/lib/ViewHelper.js:1` |
| Overlay | `$.fn.initDefaultOverlay` | `js/plugins/jquery.tools.overlay.js:1` |
| Validation | `$.fn.initValidator` | `js/plugins/jquery.initValidator.js:1` |
| Translation | `$.translate` | `js/plugins/jquery.translate.lang.js:1` |

### 1.2 State flow

1. `MaxCubeModel.init()` creates `new APICalls()` and sets `logLevel=5` (`maxCubeModel.js:28-36`).
2. `getMaxCubeState(callback)` calls `dwr.getMaxCubeState` and enriches each device with `deviceType = helper.getDeviceType(device)` (`maxCubeModel.js:85-100`).
3. The state is bound to `body`: `$("body").data('maxCubeState', ...)` (`indexController.js:162`, `:217-220`).
4. Write operations run via `setClientCommands` / `executeClientCommands` or direct DWR methods (`APICalls.js:2075`, `:2117`).
5. `applyChanges()` is triggered by the 3-second timer (`indexController.js:119-121`, `:187-217`).

### 1.3 View switching and overlays

Views are shown/hidden via `mvc.v.show(view)` / `mvc.v.hide(view)` (`globalSettingsMenuController.js:59-62`, `:70-73`). Many configuration views additionally open an overlay targeting `.j_overlayBox` (`globalSettingsMenuController.js:31`, `:54-62`). Subviews use a `subViewSelector` and are controlled via `mvc.v.showSubview` / `hideSubview` (`generalSendView.js:28-36`, `:29`; `globalSettingsMenuController.js:200-215`).

---

## 2. (A) Desktop shell / Swing / system tray

### 2.1 `Application` – startup and server operation

**Purpose:** Starts the embedded web server, monitors the settings file and opens the browser.

| Aspect | Description | Source |
| --- | --- | --- |
| Inputs | Command line (`main`), `max.settings`, locale, port | `Application.java:56-73` |
| Outputs | Jetty server on `serverSettings.getPort()`, browser URL, dialogs | `Application.java:210-227` |
| Actions | File monitor every 5000 ms, shutdown hook, server start, `Desktop.browse` | `Application.java:67-72`, `:210-217`, `:219-227` |
| Navigation | Direct jump into the browser UI via `getLocalServerAddress()` | `Application.java:79`, `:220` |
| Errors | `BindException` → `JOptionPane.showConfirmDialog` with `ServerAlreadyRunningOnPort` + port | `Application.java:228-229` |
| Special feature | Already running instance → `AlreadyRunningInstance` dialog and browser opening | `Application.java:75-91` |

**Evidence:** `decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/Application.java:66-91`, `:210-316`. If `Desktop.browse` fails (`IOException`, `SecurityException`, `UnsupportedOperationException`), `displayDialogWithAppURL()` is called (`:219-227`). The server is started with `setStopAtShutdown(true)` (`:216`).

### 2.2 `InteractiveSplash` – start screen with update check

**Purpose:** Welcome and status window with language selection, update check and start button.

| Aspect | Description | Source |
| --- | --- | --- |
| Inputs | `USER_LANGUAGE`, update result, mouse click on start button | `InteractiveSplash.java:81-94`, `:297-320` |
| Outputs | `JWindow` with MAX! logo, text lines, start button | `:61-78`, `:79-94` |
| Actions | Nimbus look-and-feel, language comparison (`startsWith("de"/"en"/"pl"/"it"/"nl"/"fr")`) | `:64-67`, `:81-94` |
| Navigation | Start button → application; if update available → download dialog | `:82-94`, `:297-320` |
| Status | "Suche nach Updates läuft…", "Keine Aktualisierungsinformationen", "Es ist eine Aktualisierung verfügbar!", "MAX! ist aktuell" | `TextRessources.java:23-46` |
| Minimize note | Window remains in the taskbar and must be ended there | `TextRessources.java:47-52` |

**Evidence:** `decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/gui/InteractiveSplash.java:61-169`, `:290-478`; `gui/TextRessources.java:5-83`. Language fallback: if none of the six languages matches, `EN_TEXT_START_APPLICATION` is used (`InteractiveSplash.java:92-94`).

### 2.3 `MaxSystemTrayHandler` – system tray

**Purpose:** Permanent access to application, proxy, error report and exit.

| Menu item | Action | Source (code) | Source (text DE) |
| --- | --- | --- | --- |
| `Öffne MAX! im Browser` | `Desktop.getDesktop().browse(URI)` | `MaxSystemTrayHandler.java:26-160` | `MaxLocalAppStarter_de.properties:11` |
| `Proxy konfigurieren` | Opens `ProxySettingsWindow` | `MaxSystemTrayHandler.java:1-160` | `MaxLocalAppStarter_de.properties:7` |
| `Fehlerbericht senden` | Opens `BugReportWizard` | `MaxSystemTrayHandler.java:3`, `:1-160` | `MaxLocalAppStarter_de.properties:8` |
| `Beende MAX!` | Ends application, tooltip `MAX! wird beendet` | `MaxSystemTrayHandler.java:26-160` | `MaxLocalAppStarter_de.properties:10`, `:12` |

Further evidenced texts: `ErrorTrayIcon=TrayIcon could not be added.`, `TrayStarted=MAX!`, `TrayStarting=MAX! wird gestartet` (`MaxLocalAppStarter_de.properties:9`, `:13-14`). Visibility of the entries is controlled by `SettingsProvider.getApplicationSettings().isTrayProxySettingsEnabled()` and `isTraySystemProfilerEnabled()` (`ApplicationSettings.java:80-100`). **INFERENCE:** Both menu items are configurable and can be hidden.

**Evidence:** `decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/MaxSystemTrayHandler.java:26-160`; `extracted/maxlocalapp/MaxLocalAppStarter_de.properties:7-14`.

### 2.4 `ProxySettingsWindow` – proxy dialog

**Purpose:** Activation and parameters of an HTTP proxy for the local application.

| Aspect | Description | Source |
| --- | --- | --- |
| Inputs | Checkbox `Activate`, host name, port | `ProxySettingsWindow.java:14-20`, `:1-142` |
| Outputs | `ProxySettings` object, `okay` flag | `:27-28` |
| Actions | `ActionListener` on buttons, `ChangeListener` on checkbox | `:11-12`, `:21-22` |
| Navigation | `Okay` applies, `Abbrechen` discards | `MaxLocalAppStarter_de.properties:1`, `:3` |
| Validation | **UNKNOWN** – no port range check is visible in the decompiled excerpt | `ProxySettingsWindow.java:1-142` |

**Evidence:** `decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/gui/ProxySettingsWindow.java:25-142`; `extracted/maxlocalapp/MaxLocalAppStarter.properties:1-6`. The model is at `decompiled/localbackend/sources/de/eq3/max/al/local/core/settings/ProxySettings.java:1`.

### 2.5 `BugReportWizard` – bug report

**Purpose:** Collects problem description and system information, opens log file selection and e-mail program.

| Aspect | Description | Source |
| --- | --- | --- |
| Inputs | Free text, firmware version, serial number, browser, router, antivirus/firewall | `TextRessources.java:71-82` |
| Outputs | E-mail with system profile and log excerpt in the clipboard | `TextRessources.java:69-70`, `:83` |
| Actions | `JOptionPane`, `JFileChooser`, `FileFilter`, `SystemProfiler` | `BugReportWizard.java:9-16`, `:23-128` |
| Navigation | Wizard steps in `JDialog`/`JFrame` | `:19-23` |
| Notice text | "Möchten Sie einen Fehlerbericht als E-Mail senden?" / "Do you want to send a bug report?" | `TextRessources.java:53-56` |

**Evidence:** `decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/gui/BugReportWizard.java:19-128`; `support/SystemProfiler.java:1-102`; `gui/TextRessources.java:53-83`. The recipient comes from `ApplicationSettings.getSystemProfilerEMail()` (`ApplicationSettings.java:102-111`).

### 2.6 Update and download

| Component | Purpose | Evidence |
| --- | --- | --- |
| `VersionChecker` | Determines `getLatestUpdateVersion()` | `VersionChecker.java:1-160`; `InteractiveSplash.java:301-307` |
| `DownloadManager` | Management of several downloads | `DownloadManager.java:1-156` |
| `Download` | Individual download with progress | `Download.java:1-173` |
| `DownloadsTableModel` | Table model with `ProgressRenderer` | `DownloadsTableModel.java:1-74`; `ProgressRenderer.java:1-20` |
| `DigestFile` / `MD5Checksum` | Integrity check | `DigestFile.java:1-28`; `MD5Checksum.java:1-27` |

Update selection texts are available in six languages (DE/EN/PL/IT/NL/FR), e.g. `DE_DO_UPDATE = "Möchten Sie die Aktualisierung im Hintergrund laden?"` (`TextRessources.java:57-68`). The update URL is configurable: `ApplicationSettings.UPDATE_URL` (`:13`, `:135-144`); `max.maxSoftwareURL=http://max.eq-3.de/software` (`extracted/settings/max.local.properties:14`). **UNKNOWN:** The exact update protocol (file format, signature check) cannot be reconstructed unambiguously from the decompiled classes.

### 2.7 Port and instance protection

| Aspect | Description | Source |
| --- | --- | --- |
| `PortFactory` | Assigns a free port | `PortFactory.java:1-36` |
| `InstanceLock` | Prevents a second instance | `InstanceLock.java:1-78` |
| `ApplicationRunningChecker` | Checks running instance by port | `ApplicationRunningChecker.java:1-40`; `Application.java:75` |
| Error texts | "Alle Ports bereits vergeben", "Es ist kein freier Port verfügbar. MAX! wird nun beendet.", "Bitte geben Sie einen gültigen freien Port ein.", "Port Einabe" (original typo) | `MaxLocalAppStarter_de.properties:15-18` |
| Further texts | "Es läuft bereits ein Server auf dem angegebenen Port", "Port bereits vergeben" | `MaxLocalAppStarter_de.properties:19-21` |

**Evidence:** `decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/PortFactory.java:1-36`, `InstanceLock.java:1-78`. The typo "Einabe" is part of the resource and should be deliberately corrected in a reimplementation, not adopted.

### 2.8 Shell language coverage

The shell resources are available for DE/EN/FR/IT/NL/PL (`extracted/maxlocalapp/MaxLocalAppStarter_{de,en,fr,it,nl,pl}.properties` and `MaxLocalAppStarter.properties`). `Application.run()` sets `Locale.setDefault(new Locale(localeString))` from `ApplicationSettings.getLocale()` (`Application.java:68-69`), with `getLocale()` falling back to `user.language` (`ApplicationSettings.java:19-29`). **INFERENCE:** Browser and shell language share the same setting, but are realized via separate resource mechanisms.

---

## 3. (B) Browser UI – common foundations

### 3.1 HTML structure and navigation

`index.html` is XHTML 1.0 Transitional, `lang="de"`, with `X-UA-Compatible: IE=9`, `cache-control: no-cache` and viewport `width=390, initial-scale=1.0, user-scalable=no` (`index.html:1-20`). Loaded are `basic.css`, `max.css`, `mvc.css`, `jsApp.css` and a large script list (`:22-25`).

| Area | Selector / marker | Source |
| --- | --- | --- |
| Header area with controls | `.j_headerControls.headerControls` | `index.html:263` |
| Start heading | `@@headerHome@@` | `:287` |
| Room tile container | `.j_showRoomsContainer` | `:361` |
| Temporary container | `.j_tmpRoomsContainer` (display:none) | `:367` |
| Room template | `.j_roomTemplate.hidden` | `:370` |
| Info panel | `.j_infoPanel` | `:386` |
| Attention notice | `.j_attention.hidden` with `<h1>!</h1>` | `:397-399` |

**INFERENCE:** The `hidden` classes and `j_*` prefixes form a uniform show/hide pattern; nearly every view additionally has `j_subview_hidden` states for waiting/success/error.

### 3.2 Text / translation system

Labels are stored as `@@token@@` in the HTML and translated at runtime, e.g. `@@h2AnErrorOccured@@`, `@@btnBack@@`, `@@btnSend@@` (`index.html:1846-1856`; `globalSettingsMenuController.js:229-239`). The language files contain e.g. `"roomStructure" : "Struktur"` (`jquery.translate.lang_de.js:82`), `"modeAuto" : "Auto"` (`:140`), `"changeRoomName" : "Raumname ändern"` (`:147`), `"deleteDevice" : "Geräte löschen"` (`:151`), `"h1DiscoveredCubes"` with `MaxIdentifier.de.CubeShortName` (`:109`). **INFERENCE:** Product terms are replaced language-dependently via `MaxIdentifier.<lang>.CubeShortName`; the concrete value lies in a separate identifier file (`UNKNOWN` in detail).

---

## 4. (B) Main view – rooms and device overview

### 4.1 Purpose

The main view shows all rooms with their current temperature, target value and state and provides entry to room control, configuration, structure and global settings.

| Aspect | Description | Source |
| --- | --- | --- |
| Inputs | `maxCubeState.rooms`, click on room tile, header buttons | `indexView.js:1-953`; `index.html:361-386` |
| Outputs | Room list, state display, notice banner, buttons | `index.html:361-399`, `:433-457` |
| Actions | `showRoomDeviceListAction`, `refreshCubeStateAction`, `btnRoomNotControllableMessageAction` | `indexController.js:767`, `:796`, `:809` |
| Navigation | To room control, device list, configuration, structure, global settings | `indexController.js:91-123`; `globalSettingsMenuController.js:39` |
| Update | `refreshCubeStateAction` reloads cube state | `indexController.js:796-808` |
| Special case | Room not controllable → dedicated notice / overlay | `indexController.js:827-837` |

### 4.2 Room tile

The template `.j_roomTemplate` contains placeholders `|id|` and `|name|`, tooltips for increasing/decreasing temperature (`.j_tooltip_|id| .j_increaseTemperature`, `.j_decreaseTemperature`) as well as the state/mode area `.j_StateAndMode.stateAndMode.j_roomPart` (`index.html:370-386`, `:433-457`).

**Evidence:** `extracted/rootwar/js/views/indexView.js:1-953`; `extracted/rootwar/index.html:361-457`; `extracted/rootwar/js/controllers/indexController.js:86-123`, `:767-859`.

---

## 5. (B) Room control (`RoomControlController`)

### 5.1 Purpose

Control of target temperature and operating mode per room or for several rooms at once (house control).

| Aspect | Description | Source |
| --- | --- | --- |
| Inputs | Room selection, +/- buttons, mode, date/time, checkboxes | `roomControlController.js:179-265`, `:837-866` |
| Outputs | Target temperature, mode, send/success/error view | `roomControlController.js:744-785`; `index.html:866-906` |
| Actions | `setTransmitAutoMode`, `setRoomPermanentMode`, `setRoomTemporaryMode` | `:598`, `:618`, `:640` |
| Navigation | Mode selection → time selection → send → success/error → back to main view | `:395-520`, `:548-559` |
| Controllability | `isAtLeastOneRoomControllable()` checks whether at least one room is controllable | `:109-118` |
| House control | `registerHouseControlButtons()` and `checkUncheckAllRoomsAction()` | `:265-394`, `:837-865` |

### 5.2 Temperature operation

`setSetPointTemperatureViewFromRoom(room)` and `setSetPointTemperatureViewToCurrentAutoTemperature(roomId)` fill the display (`roomControlController.js:120-178`). The +/- buttons are registered via `registerInAndDecreaseButtons()` (`:179-264`). `getTemp(roomID)` and `getTime()` provide the values for sending (`:663-725`). Month changes in the date field are handled via `registerMonthChange()` (`:726-743`).

### 5.3 Mode and time dialogs

`forwardFromModeSelect` / `forwardFromTimeSelect` control forward flow (`:406-498`), `backFromTimeSelectAction` and `backFromHouseControlRoomSelectAction` backward flow (`:500-547`). The views are anchored in the HTML:

| View | Selector | Heading | Source |
| --- | --- | --- | --- |
| Room temperature | `.j_roomTemperatureView` / template `.j_roomTemperatureViewTemplate` | `<room> – @@roomControl@@` | `index.html:749-775` |
| Time mode | `.j_timeSelectView` | `<room> – @@h1TimeModeSelect@@` | `:777-811` |
| Temporary time | `.j_setTemporaryTime` | `<room> – @@h1SetTemporaryTime@@` | `:812-842` |
| House room selection | `.j_houseControlRoomSelection` | `@@h1HouseControlRoomSelect@@` | `:843-865` |
| Send | `.j_roomControlSend` with subviews | `<room> – @@labelTransmitting@@` / `@@labelSuccess@@` / `@@labelError@@` | `:866-906` |

`showSetTemporaryTimeView()` and `showSuccess()` show the corresponding subviews (`:757-776`); `showException()` shows the error case (`:777-785`). `extendCubeState(roomID)` updates local state after sending (`:786-812`).

**Evidence:** `extracted/rootwar/js/controllers/roomControlController.js:79-954`; `extracted/rootwar/index.html:749-906`.

---

## 6. (B) Device and room configuration

### 6.1 Configuration menu (`IndexConfController`)

**Purpose:** Entry to all room- and house-related settings.

| Aspect | Description | Source |
| --- | --- | --- |
| Inputs | Click on configuration target, room selection, checkboxes | `indexConfController.js:68-115`, `:268-290` |
| Outputs | Configuration overview, overlay with target view | `:116-215`, `:560-578` |
| Actions | `showRoomConfAction`, `showDeviceConfAction`, `showWeeklyProgramAction`, `showTemperatureConfAction`, `showBoostConfAction`, `showDecalcificationConfAction`, `showDeviceOffsetConfAction`, `showPushButtonConfAction`, `setWallThermTempModeConfAction` | `:116`, `:386`, `:481`, `:506`, `:522`, `:531`, `:540`, `:472`, `:550` |
| Navigation | Back via `closeOverlayAction` / `backFromDeviceConfig` | `:560`, `:569` |
| House mode | `openHouseConfig()`, `bindSetHolidayMode()`, `showSetHolidayMode()` | `:236`, `:220`, `:397` |
| General | `showSetAutoModeAction`, `showSetComfortModeAction`, `showSetEcoModeAction` | `:427`, `:440`, `:454` |

**Evidence:** `extracted/rootwar/js/com_configuration/controllers/indexConfController.js:20-609`. The room configuration view is `.j_roomConfView` with `<room> – @@h1LabelConfiguration@@` (`index.html:1243-1267`), device configuration `.j_roomDeviceConfView` with `@@h3LabelDeviceSettings@@` (`:1268-1288`).

### 6.2 Temperature configuration

`TemperatureConfController` allows changing eco, comfort, maximum and window-open temperatures of several rooms.

| Aspect | Description | Source |
| --- | --- | --- |
| Inputs | Temperatures per selected room | `temperatureConfController.js:60-75`, `:149-166` |
| Outputs | Wait/success/error subview | `:143-149`, `:180-194`; `index.html:1429-1492` |
| Actions | `forwardFromTemperatureConf`, `getSelectedTemperatures`, `getChangedTemperatures`, `updateMaxCube` | `:60`, `:149`, `:156`, `:167` |
| Navigation | Back to configuration overview via `backToConfOverviewAction` | `:50-59` |
| Copy | `forwardFromCopyAction`, `backFromCopyAction` | `:76-142` |
| Comfort change | `bindComfortTemperatureChangeAction` | `:195-201` |

### 6.3 Boost and descaling

| View | Purpose | Inputs / actions | Source |
| --- | --- | --- | --- |
| `.j_boostConfView` | Boost duration and valve angle | `boostConfController.js:56-68`, `:76-146`; `index.html:1493-1530` | `updateMaxCube(changedValues)` `:134` |
| `.j_decalcificationConfView` | Descaling day and hour | `decalcificationConfController.js:56-68`, `:69-146`; `index.html:1531-1563` | `forwardFromDecalcificationAction` `:56` |

Both views have `j_*WaitForResponse` and `j_showNoSuccess` subviews (`index.html:1496-1510`, `:1534-1548`) and offer copy functions (`boostConfController.js:70-75`; `decalcificationConfController.js:69-132`).

### 6.4 Device offset and wall thermostat display

`DeviceOffsetConfController` guides through device selection (`forwardFromDeviceSelectAction`, `selectDeviceAction`, `setDeviceForOffset`, `gatherDevicesForOffset`) and sends the offset via `updateMaxCube(selectedTemperatureOffset, deviceList)` (`deviceOffsetConfController.js:58-210`). Views: `.j_devicesOffsetSelectView` and `.j_devicesOffsetConfView` (`index.html:1564-1628`).

`setWallThermostatDisplayConfigController` configures display of target vs. actual temperature on the wall thermostat: `forwardFromConfDisplayAction`, `processAPICallResponse`, `updateMaxCube(roomId)` (`setWallThermostatDisplayConfigController.js:55-109`). View `.j_wallThermostatConfView` (`index.html:1629-1675`).

### 6.5 Push-button configuration and configuration copy

- `PushButtonConfController`: `forwardFromPushButtonConfAction` (`:51-112`), selection change via `changeSelectBoxAction` (`:113-118`). View `.j_pushButtonConfView` (`index.html:2106-2149`), heading `@@h1LabelPushButtonConfig@@`.
- `copyConfView` / `j_copyConfView`: copies configurations between rooms (`index.html:1676-1702`); used by several controllers as `forwardFromCopyAction` / `backFromCopyAction` (e.g. `temperatureConfController.js:76-142`, `weeklyProgramController.js:1707-1785`).

---

## 7. (B) Weekly program

### 7.1 Purpose

Graphical editor for the seven day profiles with switching points, comprising over 1800 lines of controller logic.

| Aspect | Description | Source |
| --- | --- | --- |
| Inputs | Mouse/touch interaction on bars, day selection, quick +/- buttons, copy dialog | `weeklyProgramController.js:96-121`, `:195-353`, `:643-823` |
| Outputs | Temperature curve per day, editing tooltip, send/error status | `:723-760`, `:1015-1125`; `index.html:1289-1428` |
| Actions | `updateDailyProgramAction`, `addIntervalAction`, `deleteInterval`, `changeIntervalTimeSpan`, `validateTime` | `:1293`, `:1152`, `:1407`, `:1480`, `:1521` |
| Navigation | Back to configuration overview (`backToConfOverviewAction`), copy (`forwardFromCopyAction`) | `:1180-1197`, `:1716-1785` |
| Tabs | `tabAction`, `setVisibleProgram(dayId)` | `:1126-1151` |
| Validation | Check time windows, correct minimal differences, round to 0.5 °C | `:1049-1090`, `:1521-1536` |

### 7.2 Interaction details

- Resizing intervals: `initResizable`, `start`, `resize`, `stop`, `setCurrentDragDirection`, `resizeTimeInterval` (`weeklyProgramController.js:195-578`).
- Implicit deletion of neighboring intervals when moving: `removePrevResizeInterval`, `removeNextResizeInterval`, `implicitDeleteToTheRight`, `implicitDeleteToTheLeft`, `implicitDeleteCurrent`, `implicitDeleteWithin` (`:579-643`, `:1537-1666`).
- Fast editing: `showFastEditing`, `closeFastEditing`, `bindFastEditingSubmit`, `fastEditingPlusPressed`, `fastEditingMinusPressed` (`:643-823`).
- Coordinate conversion: `convertHeightToTemperature`, `convertWidthAndPositionToTime`, `getMousePosition` (`:1091-1125`, `:1229-1245`).
- Tooltips: `initToolTip`, `openToolTip`, `closeToolTip`, `bindTooltipHelp` (`:1198-1270`, `:1836`).
- Notice for disabled checkboxes: `showAttentionWhenClickOnDisabledCheckbox` (`:1815-1835`).

**Evidence:** `extracted/rootwar/js/com_configuration/controllers/weeklyProgramController.js:21-1842`; `extracted/rootwar/js/com_configuration/views/weeklyProgramConfView.js:1-930`; `extracted/rootwar/index.html:1289-1428`.

---

## 8. (B) Structure management

### 8.1 Entry (`IndexStructureController`)

**Purpose:** Cube connection, discovery, firmware, IP entry and airtime monitoring.

| Aspect | Description | Source |
| --- | --- | --- |
| Inputs | IP fields, cube selection, buttons | `indexStructureController.js:81-129`, `:552-636` |
| Outputs | Discovery list, status, error views | `:190-311`, `:753-815`; `index.html:909-1000`, `:2213-2329` |
| Actions | `checkNewSoftware`, `showCubeDiscovery`, `selectCubeAction`, `checkFirmwareVersion`, `performCubeFirmwareUpdate`, `connectToCube`, `stillAlivePing`, `disconnectFromCubeAction` | `:130`, `:190`, `:272`, `:313`, `:401`, `:637`, `:723`, `:738` |
| Navigation | Discovery → IP/network → connection → control | `:289-312`, `:637-710` |
| Errors | `lostConnectionAction`, `notConnectedAction`, `errorFirmwareUpdate`, `dutyCycleFullCheck` | `:753`, `:786`, `:502`, `:803` |

### 8.2 Room structure (`ChangeRoomStructureController`)

**Purpose:** Rename/delete rooms, rename/delete/move devices and change room order.

| Action | Function | Source |
| --- | --- | --- |
| Rename room | `bindChangeRoomNameEvents`, `changeRoomNameAction` | `changeRoomStructureController.js:190-311` |
| Delete room | `bindDeleteRoomEvents`, `performDeleteRoom`, `deleteRoomAction` | `:312-391` |
| Rename device | `bindChangeDeviceNameSelectEvents`, `bindChangeDeviceNameEvents`, `changeDeviceNameSelectAction` | `:410-538` |
| Delete device | `bindDeleteDeviceEvents`, `isDeleteAllowed`, `removeDevicesAction`, `deleteDeviceAction` | `:539-769` |
| Move device | `moveDeviceAction`, `finalizeMoveDevice` | `:770-1031` |
| Position room | `roomPositionAction`, `bindRoomPositionEvents`, `getRoomIdsFromOrderedRooms` | `:1105-1192` |
| Update state | `refreshMaxCubeState`, `refreshIndexView` | `:1032-1078`, `:1241-1249` |
| Limits | `bindRoomLimitReachedViewOkBtn`, `getNumberOfRooms` | `:1003-1013`, `:139-145` |

Views in HTML: `.j_roomStrucView` (`:1703-1725`), `.j_changeRoomNameView` (`:1726-1756`), `.j_deleteRoomView` (`:1757-1807`), `.j_changeDeviceNameSelectView` (`:1808-1829`), `.j_changeDeviceNameView` (`:1830-1858`), `.j_deleteDeviceView` (`:1860-1896`), `.j_changeRoomPositionView` (`:1942-1956`), `.j_roomStrucTransmitView` (`:1957-1991`). Notes on mode changes: `.j_sourceModeChangeMessage` / `.j_targetModeChangeMessage` (`index.html:1893-1894`). **INFERENCE:** Moving or deleting can change the control mode; the UI warns explicitly about this.

### 8.3 New devices (`AddNewDeviceController`)

| Aspect | Description | Source |
| --- | --- | --- |
| Inputs | Start search, device selection, room selection, device name, new room name | `addNewDeviceController.js:117-148`, `:265-443`, `:520-679` |
| Outputs | Search view, "no device", device info, room selection, name field, success | `index.html:528-715`; `addNewDeviceController.js:195-264`, `:680-901` |
| Actions | `getNewDevices`, `selectDevice`, `confirmNewDeviceAction`, `createNewRoomAction`, `setDeviceNameAction`, `sendNewDevicesConfigAction` | `:195`, `:265`, `:296`, `:520`, `:595`, `:680` |
| Navigation | Search → selection → room → name → send → success; `closeOverlayAction` aborts | `:957-967` |
| Limits | `setMaxDevicesPerType`, `countDevicesOfThisTypeInThisRoom`, `hasMaxNumberOfDevicesAchieved`, `isForbiddenDeviceAvailable` | `:403-444` |
| Special case | House devices without room: `houseArrangeNewDevice` vs. `roomArrangeNewDevice` | `:727`, `:765` |
| First installation | `openOverlay`, `SearchDeviceView.setFirstInstallHeader()` via `globalSettingsMenuController.js:245-246` | `addNewDeviceController.js:163-194` |

**Evidence:** `extracted/rootwar/js/com_structure/controllers/addNewDeviceController.js:20-1033`; `extracted/rootwar/index.html:528-715`.

---

## 9. (B) Network, time, firmware and internet

### 9.1 Cube network configuration

**Purpose:** Display and change IP, subnet, gateway, DNS and DHCP of the cube.

| Aspect | Description | Source |
| --- | --- | --- |
| Inputs | Four IP octet fields per address, DHCP checkbox | `index.html:1043-1100`; `cubeNetworkSettingController.js:166-209` |
| Outputs | Network form, wait/success/error view | `index.html:976-1099`; `cubeNetworkSettingController.js:111-165` |
| Actions | `getCubeNetworkConfAndOpenView`, `collectValues`, `sendConfig`, `bindActivateDhcpAction` | `:71`, `:166`, `:185`, `:210` |
| Navigation | Two entries: from cube discovery or from IP mask | `:46`, `:55` |
| Errors | `onFail(e, errors)`, `bindBackFromErrorAction` | `:134`, `:226` |
| Validation | Fields with `elvMin=0`, `elvMax=255`, `checkInteger=true`, `maxlength=3`, `required` | `index.html:1047-1055` |

`dhcpOffSubview` is shown when DHCP is disabled (`index.html:1015`). The underlying object is `NetworkConfiguration` with evidenced defaults `192.168.0.123 / 255.255.255.0 / 192.168.0.1` and `dhcpEnabled=true` (`decompiled/essentials/sources/de/eq3/max/al/core/cubestate/NetworkConfiguration.java:17-24`).

### 9.2 Cube time

`setCubeTimeController` shows the time difference between browser and cube and sets cube time: `showOverlayAction`, `sendAction`, `setCubeTime`, `getTimeDiffFrom2000`, `getUserDate` (`setCubeTimeController.js:57-168`). View `.j_setCubeTimeView` with `@@headlineSetCubeTime@@` (`index.html:2227-2253`). The time zone is configured in the global settings menu (see 10.1). Block `localCubeUpdateController.js:97` (`updateCubeTime`) shows local synchronization in offline operation.

### 9.3 Firmware and software update

| View | Selector | Action | Source |
| --- | --- | --- | --- |
| New software | `.j_newSoftwareAvailableView` | `checkNewSoftware()` | `index.html:2150-2173`; `indexStructureController.js:130-179` |
| Check firmware | `.j_checkFirmwareVersionView` | `checkFirmwareVersion()` | `index.html:2174-2193`; `indexStructureController.js:313-400` |
| Update firmware | `.j_updateFirmwareView` | `performCubeFirmwareUpdate()`, `checkFirmwareUpdateStatus()` | `index.html:2194-2212`; `indexStructureController.js:401-513` |
| Completion | `finishFirmwareUpdate(param)` | Success / message | `:514-551` |

Texts: `"newSoftwareAvailable" : "Software Check!"`, `"checkFirmwareVersion" : "Prüfen der Firmware"`, `"updateFirmwareVersion" : "Aktualisierung des <Cube>s"` (`jquery.translate.lang_de.js:56`, `:57`, `:64`). Version limits: `latestCubeFirmwareVersion=275`, `leastCompatibleFirmwareVersion=273` (`max.local.properties:17-18`).

### 9.4 Internet / remote access (`SettingsInternetController`)

| Aspect | Description | Source |
| --- | --- | --- |
| Inputs | User name, password, remote activation, password strength | `settingsInternetController.js:58-92`, `:256-330` |
| Outputs | Form, wait/success/error view, access data | `:93-218`; `index.html:1153-1223` |
| Actions | `setRemoteAction`, `sendDataAction`, `setRemoteCredentials`, `checkUserData`, `registerPasswordStrength`, `showLabelPasswordStrength` | `:74`, `:156`, `:219`, `:256`, `:293`, `:317` |
| Navigation | `showOverlayAction` opens, `closeOverlayAction`/`closeSuccessView`/`closeOverlay` close | `:93`, `:331`, `:345`, `:376` |
| Validation | Client-side check plus `RemoteCredentialsValidator` (user name not empty, `[^\r\n,]+`) | `:256-292`; `RemoteCredentialsValidator.java:12`, `:24-38` |

View `.j_settingsInternetView` with `@@internetSettings@@` and subviews `.j_setRemoteAccessWaitForResponse`, `.j_showSuccess`, `.j_showNoSuccess`, `.j_getCredentialError`, `.j_changeAccess`, `.j_userData` (`index.html:1153-1223`).

### 9.5 Product account / remote activation

`IndexController` controls the account views: `checkUrlProductKey` (`:157-163`), `showRemoteActivationDataIfNecessary` (`:658-669`), `showRemoteAccountButton` (`:670-687`), `showUserRemoteSoonInvalidView` (`:688-704`), `showProductAccountManagementView` (`:705-712`), `showProductAccountInfoView` (`:713-720`), `activateUserAccountManagementBtn` (`:729-745`), `bindForwardFromRemoteSoonInvalidView` (`:746-760`). Related views: `activateProductAccountView.js:1-182`, `activateProductAccountSuccessView.js:1-96`, `showProductAccountInfoView.js:1-73`, `remoteSoonInvalidView.js:1-137`. The model switches account management off via a build-time variable when `accountManagementActive` is not `true` (`maxCubeModel.js:32-36`, `:45-60`).

---

## 10. (B) Global settings, help and status

### 10.1 Global settings menu

`GlobalSettingsMenuController` offers four entries, distinguished via `confModeSelected`:

| Key | Target | Source |
| --- | --- | --- |
| `timezoneConf` | Time zone / summer-winter changeover | `globalSettingsMenuController.js:33`, `:83-87` |
| `internetSettings` | Internet control | `:34`, `:88-90` |
| `productAccountActivationOverlay` | Account activation | `:35`, `:91-93` |
| `productAccountInformationOverlay` | Account information | `:36`, `:94-96` |

View `.j_globalSettingsMenuView` with `@@globalSettings@@` (`index.html:1224-1242`). `selectConfModeAction` marks the selection and immediately triggers `btnOk.click()` for the first two entries (`:121-132`). `backFromSetTimezoneAction` distinguishes first installation and normal invocation (`:169-188`). `forwardFromSetTimezoneAction` saves via `MaxCubeModel.setTimeZoneConfiguration` and shows `@@h2AnErrorOccured@@` plus error code on errors (`:194-251`).

### 10.2 Time zone

`SetTimezoneView` binds to `.j_TimezoneView` (`index.html:2014-2105`). When `noChange` is selected, OK button and summer/winter selection are disabled (`globalSettingsMenuController.js:134-151`). House configuration `.j_houseConfView` is at `index.html:1992-2013`.

### 10.3 Help

`HelpController` (570 lines) and `helpController.js`-related views (`activateHelpView.js:1-58`) as well as the language file `jquery.translateHelp.lang.js` (834 lines) show that there is a separate, multilingual help system. `IndexController.setDirtyStateHelp()` remembers ongoing processing (`indexController.js:761-765`), `HelpController.setHelpActive()` is activated after first installation (`globalSettingsMenuController.js:184`, `:247`).

### 10.4 Common send/status dialog

`mvc.v.GeneralSendView` is a reusable status dialog with three subviews:

| Subview | Selector | Source |
| --- | --- | --- |
| Waiting | `.j_waitingForResponse` | `generalSendView.js:29`, `:34` |
| Success | `.j_showSuccess` | `:35`, `:48-50` |
| Error | `.j_showNoSuccess` | `:36` |

Buttons: `.j_btnOK` (forward) and `.j_btnBack` (back), configurable via `initialForwardClasses` / `initialBackClasses` (`generalSendView.js:38-55`, `:69-77`). The corresponding HTML is at `index.html:2254-2300` with `@@labelTransmitting@@`, `@@transferSuccessful@@`, `@@transferNotSuccessful@@`. **Finding:** In `generalSendView.js:53-55` the error selectors point to `.j_showSuccess` instead of `.j_showNoSuccess`; this is an error visible in the code and to be fixed in a reimplementation.

Further status views: `.j_cubeLastPingView` (`@@headlineCubeNotConnected@@`, `index.html:2213-2226`), `.j_cubeLostConnectionView` (`:2301-2309`), `.j_noMoreDevicesPossibleView` (`:2310-2318`), `.j_roomLimitReachedView` (`:2319-2329`), `.j_dutyCycleFullView` (`@@h1dutyCycleFull@@`, `:515-527`). **UNKNOWN:** The exact text of `@@h1dutyCycleFull@@` is not in the evaluated DE excerpt.

### 10.5 Error codes in the UI

`APICalls.errorHandler(message, exception)` and `warningHandler(message, exception)` are central error handlers (`APICalls.js:2514-2546`). Error codes come from `extracted/essentials/errorcodes.properties:1-53` (e.g. `MAX0001`, `MAX1015`, `MAX1030`, `MAX1051`). The UI usually shows errors as `@@h2AnErrorOccured@@` plus `@@<Code>@@` (e.g. `globalSettingsMenuController.js:229`). **INFERENCE:** The `@@...@@` tokens are resolved via the language files; an unknown code would remain visible as a token.

---

## 11. Responsive behavior, mobile and browser detection

### 11.1 Viewport and mobile detection

`index.html:20` sets `width=390, initial-scale=1.0, user-scalable=no` – a fixed layout, **not** a fluid responsive grid. `$.fn.mobileDetect()` checks in this order: GET parameter `mobile=true/false`, global/jquery variable `$.mobile === true`, then user agent list with more than 30 keys (`midp`, `iphone`, `ipod`, `android`, `webOS`, `windows phone`, `iemobile`, `opera mini`, `blackberry`, `nokia`, `samsung`, …) (`jquery.mobileDetect.js:26-90`).

`IndexController.initAction` stores the result in `this.isMobile` (`indexController.js:94`). For mobile **or** small viewport (`height < 470 || width < 550`), `isSmallDisplay=true` is set and then `.j_headerControls`, the house configuration button and the room configuration button are hidden (`indexController.js:136-154`). For mobile clients, `IndexRemoteController.isMobile = true` is adopted (`:616-617`).

**INFERENCE:** "Responsive" here means targeted hiding of navigation on small displays, not automatic rearrangement. **UNKNOWN:** There are no evidenced CSS media queries in the examined files; `css/max.css` and `css/jsApp.css` were not evaluated in detail.

### 11.2 Browser check

`browserCheck()` shows a replacement view for non-mobile clients whose warning has not yet been accepted if one of the following conditions applies (`indexController.js:307-318`):

| Condition | Source |
| --- | --- |
| Internet Explorer, version number < 7 | `:308` (`$.browser.msie`, `parseInt(...substr(0,1)) < 7`) |
| Opera (any version) | `:308` (`$.browser.opera`) |
| Chrome with version < `534.10` (comment: Chrome 8) | `:308` |
| Safari with version < `533` (comment: Safari 5.0.5.*) | `:308` |
| Mozilla/Firefox with version < `1.9.2.16` (comment: FF 3.6.*) | `:308` |

Then `.pageWrapper` is hidden, `NoSupportedBrowserView` shown and `registerForwardFromOldBrowserWarning()` registered (`:309-314`). The view itself is minimal: `viewSelector: "#j_notSupportedBrowsers"`, empty `init()` (`noSupportedBrowserView.js:16-34`). Browser icons (`chrome`, `ff`, `ie`, `opera`, `safari`) are under `extracted/rootwar/images/`. **INFERENCE:** The warning is designed as a deliberate abort with subsequent forwarding; the exact target text is not evidenced in the DE excerpt.

---

## 12. Language coverage of the browser UI

| Language | File | Source |
| --- | --- | --- |
| German | `jquery.translate.lang_de.js` (523 lines) | `extracted/rootwar/js/plugins/lang/` |
| English | `jquery.translate.lang_en.js` (523) | ditto |
| French | `jquery.translate.lang_fr.js` (524) | ditto |
| Italian | `jquery.translate.lang_it.js` (523) | ditto |
| Dutch | `jquery.translate.lang_nl.js` (522) | ditto |
| Polish | `jquery.translate.lang_pl.js` (522) | ditto |

`IndexController.getAvailableLanguages()` returns exactly `[DE, EN, FR, IT, NL, PL]` (`indexController.js:125-134`). Language selection is via flags (`bindLanguageChangeClick`, `:323-352`), cookie (`isLanguageCookieSet`, `:430`) and browser language (`getBrowserLanguage`, `:413`; `isBrowserLangSupported`, `:418`; `chooseRightLanguage`, `:444-479`). The stored value is written to the server via `MaxCubeModel.setLocale` (`maxCubeModel.js:246-252`). **INFERENCE:** Language selection is thus partly client-side (cookie), partly server-side (locale).

---

## 13. Source index by view

| View / function | Primary file | HTML anchor |
| --- | --- | --- |
| Main view | `js/views/indexView.js:1-953` | `index.html:263-457` |
| Room control | `js/controllers/roomControlController.js:79-954` | `index.html:749-906` |
| Configuration menu | `js/com_configuration/controllers/indexConfController.js:68-609` | `index.html:1243-1288` |
| Weekly program | `js/com_configuration/controllers/weeklyProgramController.js:96-1842` | `index.html:1289-1428` |
| Temperature/boost/descaling/offset | `js/com_configuration/controllers/{temperature,boost,decalcification,deviceOffset}ConfController.js` | `index.html:1429-1675` |
| Push buttons & house | `js/com_configuration/controllers/pushButtonConfController.js`, `indexConfController.js:236-268` | `index.html:1992-2149` |
| Structure | `js/com_structure/controllers/changeRoomStructureController.js:78-1310` | `index.html:1703-1991` |
| New devices | `js/com_structure/controllers/addNewDeviceController.js:117-1033` | `index.html:528-715` |
| Network | `js/com_structure/controllers/cubeNetworkSettingController.js:33-271` | `index.html:976-1100` |
| Time | `js/com_structure/controllers/setCubeTimeController.js:37-181` | `index.html:2227-2253` |
| Firmware/update | `js/com_structure/controllers/indexStructureController.js:130-551` | `index.html:2150-2212` |
| Internet/remote | `js/com_structure/controllers/settingsInternetController.js:58-384` | `index.html:1153-1223` |
| Global settings | `js/controllers/globalSettingsMenuController.js:38-252` | `index.html:1224-1242`, `:2014-2105` |
| Product account | `js/controllers/indexController.js:658-760`, `js/views/activateProduct*.js` | `index.html` (overlay) |
| Help | `js/controllers/helpController.js:1-570` | `js/views/activateHelpView.js:1-58` |
| Status/errors | `js/views/generalSendView.js:1-177`, `js/lib/APICalls.js:2514-2546` | `index.html:1846-1856`, `:2254-2329` |
| Browser warning | `js/controllers/indexController.js:307-318`, `js/views/noSupportedBrowserView.js:16-34` | `#j_notSupportedBrowsers` |
| Desktop shell | `decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/` | `extracted/maxlocalapp/*.properties` |

---

## 14. Deliberate deviations for a new build (INFERENCE)

1. Do not adopt the `j_*` class and `@@token@@` platform; instead build explicit components with data binding and a central error-code resolver.
2. Replace the fixed viewport width 390 (`index.html:20`) with a genuinely responsive layout; understand the existing behavior only as a requirement for small displays (`indexController.js:136-154`).
3. Replace outdated browser detection via `$.browser` (`indexController.js:308`) with feature detection; document the supported minimum versions (IE 7, Chrome 8, Safari 5.0.5, FF 3.6) only as historical reference.
4. Implement the send status dialog with correct selectors for success and error (`generalSendView.js:53-55` contains an evidenced error).
5. Do not mix the two default worlds (`max.properties` vs. Java constructors, see `domain-model.md` section 3.2) in the GUI.
6. Configure shell and browser UI via a shared locale instance, but keep separate resource bundles (`Application.java:68-69`; `maxCubeModel.js:246-252`).
