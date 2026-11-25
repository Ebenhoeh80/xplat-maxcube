# GUI-Spezifikation – MAX! Cube Local Application

Technische Spezifikation der Benutzeroberfläche, rekonstruiert aus den lokalen Quellen. Es wird zwischen **(A) der Desktop-Shell** (Java/Swing/AWT, `MaxLocalApp.jar`) und **(B) der Browser-Oberfläche** (HTML + jQuery-MVC, ausgeliefert als WAR-Root) unterschieden. Die Darstellung ist eine Anforderungsgrundlage für einen Neubau, keine 1:1-Portierung.

## 0. Quellenbasis und Kennzeichnung

| Quelle | Inhalt | Pfad |
|---|---|---|
| HTML-Layout | 2586 Zeilen statisches Markup mit `j_*-Klassen und `@@token@@`-Platzhaltern | `extracted/rootwar/index.html` |
| MVC-Framework | Controller-/View-/Model-Registrierung | `extracted/rootwar/js/framework/mvc.js` |
| Controller | Interaktionslogik | `extracted/rootwar/js/controllers/`, `js/com_configuration/controllers/`, `js/com_structure/controllers/` |
| Views | Selektor- und Template-Definitionen | `extracted/rootwar/js/views/`, `js/com_configuration/views/`, `js/com_structure/views/` |
| DTO-Brücke | DWR-Wrapper, 120 s Timeout | `extracted/rootwar/js/lib/APICalls.js:18-41` |
| Sprache | Sechs Sprachdateien | `extracted/rootwar/js/plugins/lang/jquery.translate.lang_{de,en,fr,it,nl,pl}.js` |
| Swing-Shell | Start, Tray, Proxy, Bugreport, Update | `decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/` |
| Shell-Texte | Resource-Bundles | `extracted/maxlocalapp/MaxLocalAppStarter*.properties` |

Kennzeichnung wie in `domain-model.md`: **Fakt** = direkt belegt, **INFERENZ** = aus Belegen abgeleitet, **UNBEKANNT** = nicht bestimmbar. Jede Ansicht nennt mindestens eine Quellstelle.

---

## 1. Architektur der Browser-Oberfläche

### 1.1 MVC-Aufbau

Das Framework registriert Objekte unter `mvc.c.*` (Controller), `mvc.v.*` (Views) und `mvc.m.*` (Modelle). Jeder Controller besitzt ein `defaultView` und eine `initAction` (`extracted/rootwar/js/framework/mvc.js:1-346`). Der Startcontroller `mvc.c.IndexController` initialisiert das Hauptmodell, die Sprache, die Umwelt (lokal/remote), den Browsercheck und einen 3-Sekunden-Timer für gebündelte Temperaturänderungen (`extracted/rootwar/js/controllers/indexController.js:86-123`, `:119-121`).

| Rolle | Objekt | Quelle |
|---|---|---|
| Hauptcontroller | `mvc.c.IndexController` | `indexController.js:16`, `:86-123` |
| Raumsteuerung | `mvc.c.RoomControlController` | `roomControlController.js:16`, `:79-108` |
| Hauptansicht | `mvc.v.IndexView` | `js/views/indexView.js:1-953` |
| Zustandsmodell | `mvc.m.MaxCubeModel` | `js/models/maxCubeModel.js:17-37` |
| DWR-Wrapper | `APICalls` | `js/lib/APICalls.js:18-41` |
| Hilfsfunktionen | `ActionHelperClass`, `ViewHelper` | `js/lib/ActionHelper.js:1`, `js/lib/ViewHelper.js:1` |
| Overlay | `$.fn.initDefaultOverlay` | `js/plugins/jquery.tools.overlay.js:1` |
| Validierung | `$.fn.initValidator` | `js/plugins/jquery.initValidator.js:1` |
| Übersetzung | `$.translate` | `js/plugins/jquery.translate.lang.js:1` |

### 1.2 Zustandsablauf

1. `MaxCubeModel.init()` erzeugt `new APICalls()` und setzt `logLevel=5` (`maxCubeModel.js:28-36`).
2. `getMaxCubeState(callback)` ruft `dwr.getMaxCubeState` und reichert jedes Gerät um `deviceType = helper.getDeviceType(device)` an (`maxCubeModel.js:85-100`).
3. Der Zustand wird an `body` gebunden: `$("body").data('maxCubeState', ...)` (`indexController.js:162`, `:217-220`).
4. Schreiboperationen laufen über `setClientCommands` / `executeClientCommands` oder direkte DWR-Methoden (`APICalls.js:2075`, `:2117`).
5. `applyChanges()` wird vom 3-Sekunden-Timer ausgelöst (`indexController.js:119-121`, `:187-217`).

### 1.3 Ansichtswechsel und Overlays

Views werden über `mvc.v.show(view)` / `mvc.v.hide(view)` ein- und ausgeblendet (`globalSettingsMenuController.js:59-62`, `:70-73`). Viele Konfigurationsansichten öffnen zusätzlich ein Overlay mit Ziel `.j_overlayBox` (`globalSettingsMenuController.js:31`, `:54-62`). Unteransichten nutzen eine `subViewSelector` und werden über `mvc.v.showSubview` / `hideSubview` gesteuert (`generalSendView.js:28-36`, `:29`; `globalSettingsMenuController.js:200-215`).

---

## 2. (A) Desktop-Shell / Swing / Systemtray

### 2.1 `Application` – Start und Serverbetrieb

**Zweck:** Startet den eingebetteten Webserver, überwacht die Einstellungsdatei und öffnet den Browser.

| Aspekt | Beschreibung | Quelle |
|---|---|---|
| Eingaben | Kommandozeile (`main`), `max.settings`, Locale, Port | `Application.java:56-73` |
| Ausgaben | Jetty-Server auf `serverSettings.getPort()`, Browser-URL, Dialoge | `Application.java:210-227` |
| Aktionen | Datei-Monitor alle 5000 ms, Shutdown-Hook, Serverstart, `Desktop.browse` | `Application.java:67-72`, `:210-217`, `:219-227` |
| Navigation | Direktsprung in die Browser-UI via `getLocalServerAddress()` | `Application.java:79`, `:220` |
| Fehler | `BindException` → `JOptionPane.showConfirmDialog` mit `ServerAlreadyRunningOnPort` + Port | `Application.java:228-229` |
| Besonderheit | Bereits laufende Instanz → `AlreadyRunningInstance`-Dialog und Browseröffnung | `Application.java:75-91` |

**Beleg:** `decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/Application.java:66-91`, `:210-316`. Falls `Desktop.browse` scheitert (`IOException`, `SecurityException`, `UnsupportedOperationException`), wird `displayDialogWithAppURL()` aufgerufen (`:219-227`). Der Server wird mit `setStopAtShutdown(true)` gestartet (`:216`).

### 2.2 `InteractiveSplash` – Startbildschirm mit Update-Prüfung

**Zweck:** Begrüßungs- und Statusfenster mit Sprachauswahl, Updateprüfung und Startknopf.

| Aspekt | Beschreibung | Quelle |
|---|---|---|
| Eingaben | `USER_LANGUAGE`, Update-Ergebnis, Mausklick auf Startknopf | `InteractiveSplash.java:81-94`, `:297-320` |
| Ausgaben | `JWindow` mit MAX!-Logo, Textzeilen, Startknopf | `:61-78`, `:79-94` |
| Aktionen | Nimbus-Look-and-Feel, Sprachvergleich (`startsWith("de"/"en"/"pl"/"it"/"nl"/"fr")`) | `:64-67`, `:81-94` |
| Navigation | Startknopf → Anwendung; bei Update verfügbar → Download-Dialog | `:82-94`, `:297-320` |
| Status | "Suche nach Updates läuft…", "Keine Aktualisierungsinformationen", "Es ist eine Aktualisierung verfügbar!", "MAX! ist aktuell" | `TextRessources.java:23-46` |
| Minimieren-Hinweis | Fenster bleibt in der Taskleiste und muss dort beendet werden | `TextRessources.java:47-52` |

**Beleg:** `decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/gui/InteractiveSplash.java:61-169`, `:290-478`; `gui/TextRessources.java:5-83`. Sprachfallback: wenn keine der sechs Sprachen passt, wird `EN_TEXT_START_APPLICATION` verwendet (`InteractiveSplash.java:92-94`).

### 2.3 `MaxSystemTrayHandler` – Systemtray

**Zweck:** Dauerhafter Zugriff auf Anwendung, Proxy, Fehlerbericht und Beenden.

| Menüeintrag | Aktion | Quelle (Code) | Quelle (Text DE) |
|---|---|---|---|
| `Öffne MAX! im Browser` | `Desktop.getDesktop().browse(URI)` | `MaxSystemTrayHandler.java:26-160` | `MaxLocalAppStarter_de.properties:11` |
| `Proxy konfigurieren` | Öffnet `ProxySettingsWindow` | `MaxSystemTrayHandler.java:1-160` | `MaxLocalAppStarter_de.properties:7` |
| `Fehlerbericht senden` | Öffnet `BugReportWizard` | `MaxSystemTrayHandler.java:3`, `:1-160` | `MaxLocalAppStarter_de.properties:8` |
| `Beende MAX!` | Beendet Anwendung, Tooltip `MAX! wird beendet` | `MaxSystemTrayHandler.java:26-160` | `MaxLocalAppStarter_de.properties:10`, `:12` |

Weitere belegte Texte: `ErrorTrayIcon=TrayIcon could not be added.`, `TrayStarted=MAX!`, `TrayStarting=MAX! wird gestartet` (`MaxLocalAppStarter_de.properties:9`, `:13-14`). Sichtbarkeit der Einträge wird über `SettingsProvider.getApplicationSettings().isTrayProxySettingsEnabled()` und `isTraySystemProfilerEnabled()` gesteuert (`ApplicationSettings.java:80-100`). **INFERENZ:** Beide Menüpunkte sind konfigurierbar und können ausgeblendet sein.

**Beleg:** `decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/MaxSystemTrayHandler.java:26-160`; `extracted/maxlocalapp/MaxLocalAppStarter_de.properties:7-14`.

### 2.4 `ProxySettingsWindow` – Proxy-Dialog

**Zweck:** Aktivierung und Parameter eines HTTP-Proxys für die lokale Anwendung.

| Aspekt | Beschreibung | Quelle |
|---|---|---|
| Eingaben | Checkbox `Activate`, Hostname, Port | `ProxySettingsWindow.java:14-20`, `:1-142` |
| Ausgaben | `ProxySettings`-Objekt, `okay`-Flag | `:27-28` |
| Aktionen | `ActionListener` auf Buttons, `ChangeListener` auf Checkbox | `:11-12`, `:21-22` |
| Navigation | `Okay` übernimmt, `Abbrechen` verwirft | `MaxLocalAppStarter_de.properties:1`, `:3` |
| Validierung | **UNBEKANNT** – im dekompilierten Auszug ist keine Portbereichsprüfung sichtbar | `ProxySettingsWindow.java:1-142` |

**Beleg:** `decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/gui/ProxySettingsWindow.java:25-142`; `extracted/maxlocalapp/MaxLocalAppStarter.properties:1-6`. Das Modell liegt unter `decompiled/localbackend/sources/de/eq3/max/al/local/core/settings/ProxySettings.java:1`.

### 2.5 `BugReportWizard` – Fehlerbericht

**Zweck:** Erhebt Problembeschreibung und Systeminformationen, öffnet Logdatei-Auswahl und E-Mail-Programm.

| Aspekt | Beschreibung | Quelle |
|---|---|---|
| Eingaben | Freitext, Firmware-Version, Seriennummer, Browser, Router, Antivirus/Firewall | `TextRessources.java:71-82` |
| Ausgaben | E-Mail mit Systemprofil und Logauszug in der Zwischenablage | `TextRessources.java:69-70`, `:83` |
| Aktionen | `JOptionPane`, `JFileChooser`, `FileFilter`, `SystemProfiler` | `BugReportWizard.java:9-16`, `:23-128` |
| Navigation | Assistentenschritte in `JDialog`/`JFrame` | `:19-23` |
| Hinweistext | "Möchten Sie einen Fehlerbericht als E-Mail senden?" / "Do you want to send a bug report?" | `TextRessources.java:53-56` |

**Beleg:** `decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/gui/BugReportWizard.java:19-128`; `support/SystemProfiler.java:1-102`; `gui/TextRessources.java:53-83`. Der Empfänger stammt aus `ApplicationSettings.getSystemProfilerEMail()` (`ApplicationSettings.java:102-111`).

### 2.6 Update und Download

| Komponente | Zweck | Beleg |
|---|---|---|
| `VersionChecker` | Ermittelt `getLatestUpdateVersion()` | `VersionChecker.java:1-160`; `InteractiveSplash.java:301-307` |
| `DownloadManager` | Verwaltung mehrerer Downloads | `DownloadManager.java:1-156` |
| `Download` | Einzelner Download mit Fortschritt | `Download.java:1-173` |
| `DownloadsTableModel` | Tabellenmodell mit `ProgressRenderer` | `DownloadsTableModel.java:1-74`; `ProgressRenderer.java:1-20` |
| `DigestFile` / `MD5Checksum` | Integritätsprüfung | `DigestFile.java:1-28`; `MD5Checksum.java:1-27` |

Update-Auswahltexte sind in sechs Sprachen vorhanden (DE/EN/PL/IT/NL/FR), z. B. `DE_DO_UPDATE = "Möchten Sie die Aktualisierung im Hintergrund laden?"` (`TextRessources.java:57-68`). Die Update-URL ist konfigurierbar: `ApplicationSettings.UPDATE_URL` (`:13`, `:135-144`); `max.maxSoftwareURL=http://max.eq-3.de/software` (`extracted/settings/max.local.properties:14`). **UNBEKANNT:** Das genaue Update-Protokoll (Dateiformat, Signaturprüfung) ist aus den dekompilierten Klassen nicht zweifelsfrei rekonstruierbar.

### 2.7 Port und Instanzschutz

| Aspekt | Beschreibung | Quelle |
|---|---|---|
| `PortFactory` | Vergibt einen freien Port | `PortFactory.java:1-36` |
| `InstanceLock` | Verhindert zweite Instanz | `InstanceLock.java:1-78` |
| `ApplicationRunningChecker` | Prüft laufende Instanz am Port | `ApplicationRunningChecker.java:1-40`; `Application.java:75` |
| Fehlertexte | "Alle Ports bereits vergeben", "Es ist kein freier Port verfügbar. MAX! wird nun beendet.", "Bitte geben Sie einen gültigen freien Port ein.", "Port Einabe" (Original-Tippfehler) | `MaxLocalAppStarter_de.properties:15-18` |
| Weitere Texte | "Es läuft bereits ein Server auf dem angegebenen Port", "Port bereits vergeben" | `MaxLocalAppStarter_de.properties:19-21` |

**Beleg:** `decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/PortFactory.java:1-36`, `InstanceLock.java:1-78`. Der Tippfehler „Einabe“ ist Bestandteil der Ressource und ist bei einer Neuimplementierung bewusst zu korrigieren, nicht zu übernehmen.

### 2.8 Shell-Sprachabdeckung

Die Shell-Ressourcen liegen für DE/EN/FR/IT/NL/PL vor (`extracted/maxlocalapp/MaxLocalAppStarter_{de,en,fr,it,nl,pl}.properties` und `MaxLocalAppStarter.properties`). `Application.run()` setzt `Locale.setDefault(new Locale(localeString))` aus `ApplicationSettings.getLocale()` (`Application.java:68-69`), wobei `getLocale()` auf `user.language` zurückfällt (`ApplicationSettings.java:19-29`). **INFERENZ:** Browser- und Shell-Sprache teilen sich dieselbe Einstellung, werden aber über getrennte Ressourcenmechanismen realisiert.

---

## 3. (B) Browser-UI – gemeinsame Grundlagen

### 3.1 HTML-Struktur und Navigation

`index.html` ist XHTML 1.0 Transitional, `lang="de"`, mit `X-UA-Compatible: IE=9`, `cache-control: no-cache` und Viewport `width=390, initial-scale=1.0, user-scalable=no` (`index.html:1-20`). Geladen werden `basic.css`, `max.css`, `mvc.css`, `jsApp.css` und eine große Scriptliste (`:22-25`).

| Bereich | Selektor / Marker | Quelle |
|---|---|---|
| Kopfbereich mit Steuerungen | `.j_headerControls.headerControls` | `index.html:263` |
| Startüberschrift | `@@headerHome@@` | `:287` |
| Raumkacheln-Container | `.j_showRoomsContainer` | `:361` |
| Temporärer Container | `.j_tmpRoomsContainer` (display:none) | `:367` |
| Raum-Template | `.j_roomTemplate.hidden` | `:370` |
| Infopanel | `.j_infoPanel` | `:386` |
| Aufmerksamkeitshinweis | `.j_attention.hidden` mit `<h1>!</h1>` | `:397-399` |

**INFERENZ:** Die `hidden`-Klassen und `j_*`-Präfixe bilden ein einheitliches Show/Hide-Muster; nahezu jede Ansicht besitzt zusätzliche `j_subview_hidden`-Zustände für Warten/Erfolg/Fehler.

### 3.2 Text-/Übersetzungssystem

Beschriftungen liegen als `@@token@@` im HTML und werden zur Laufzeit übersetzt, z. B. `@@h2AnErrorOccured@@`, `@@btnBack@@`, `@@btnSend@@` (`index.html:1846-1856`; `globalSettingsMenuController.js:229-239`). Die Sprachdateien enthalten z. B. `"roomStructure" : "Struktur"` (`jquery.translate.lang_de.js:82`), `"modeAuto" : "Auto"` (`:140`), `"changeRoomName" : "Raumname ändern"` (`:147`), `"deleteDevice" : "Geräte löschen"` (`:151`), `"h1DiscoveredCubes"` mit `MaxIdentifier.de.CubeShortName` (`:109`). **INFERENZ:** Produktbegriffe werden über `MaxIdentifier.<lang>.CubeShortName` sprachabhängig ersetzt; der konkrete Wert liegt in einer separaten Identifier-Datei (`UNBEKANNT` im Detail).

---

## 4. (B) Hauptansicht – Räume und Geräteübersicht

### 4.1 Zweck

Die Hauptansicht zeigt alle Räume mit ihrer aktuellen Temperatur, dem Sollwert und dem Zustand und bietet den Einstieg in Raumsteuerung, Konfiguration, Struktur und Globale Einstellungen.

| Aspekt | Beschreibung | Quelle |
|---|---|---|
| Eingaben | `maxCubeState.rooms`, Klick auf Raumkachel, Header-Buttons | `indexView.js:1-953`; `index.html:361-386` |
| Ausgaben | Raumliste, Zustandsanzeige, Hinweisbanner, Buttons | `index.html:361-399`, `:433-457` |
| Aktionen | `showRoomDeviceListAction`, `refreshCubeStateAction`, `btnRoomNotControllableMessageAction` | `indexController.js:767`, `:796`, `:809` |
| Navigation | Zur Raumsteuerung, Geräteliste, Konfiguration, Struktur, Global Settings | `indexController.js:91-123`; `globalSettingsMenuController.js:39` |
| Aktualisierung | `refreshCubeStateAction` lädt den Cube-Zustand neu | `indexController.js:796-808` |
| Sonderfall | Raum nicht steuerbar → eigener Hinweis / Overlay | `indexController.js:827-837` |

### 4.2 Raumkachel

Das Template `.j_roomTemplate` enthält Platzhalter `|id|` und `|name|`, Tooltips zum Erhöhen/Erniedrigen der Temperatur (`.j_tooltip_|id| .j_increaseTemperature`, `.j_decreaseTemperature`) sowie den Zustands-/Modusbereich `.j_StateAndMode.stateAndMode.j_roomPart` (`index.html:370-386`, `:433-457`).

**Beleg:** `extracted/rootwar/js/views/indexView.js:1-953`; `extracted/rootwar/index.html:361-457`; `extracted/rootwar/js/controllers/indexController.js:86-123`, `:767-859`.

---

## 5. (B) Raumsteuerung (`RoomControlController`)

### 5.1 Zweck

Steuerung der Solltemperatur und des Betriebsmodus pro Raum oder für mehrere Räume gleichzeitig (Haussteuerung).

| Aspekt | Beschreibung | Quelle |
|---|---|---|
| Eingaben | Raumauswahl, +/- Buttons, Modus, Datum/Zeit, Checkboxen | `roomControlController.js:179-265`, `:837-866` |
| Ausgaben | Solltemperatur, Modus, Sende-/Erfolgs-/Fehlerview | `roomControlController.js:744-785`; `index.html:866-906` |
| Aktionen | `setTransmitAutoMode`, `setRoomPermanentMode`, `setRoomTemporaryMode` | `:598`, `:618`, `:640` |
| Navigation | Modusauswahl → Zeitauswahl → Senden → Erfolg/Fehler → Zurück zur Hauptansicht | `:395-520`, `:548-559` |
| Steuerbarkeit | `isAtLeastOneRoomControllable()` prüft, ob mind. ein Raum steuerbar ist | `:109-118` |
| Haussteuerung | `registerHouseControlButtons()` und `checkUncheckAllRoomsAction()` | `:265-394`, `:837-865` |

### 5.2 Temperaturbedienung

`setSetPointTemperatureViewFromRoom(room)` und `setSetPointTemperatureViewToCurrentAutoTemperature(roomId)` befüllen die Anzeige (`roomControlController.js:120-178`). Die +/- Buttons werden über `registerInAndDecreaseButtons()` registriert (`:179-264`). `getTemp(roomID)` und `getTime()` liefern die Werte für das Senden (`:663-725`). Der Monatswechsel im Datumsfeld wird über `registerMonthChange()` behandelt (`:726-743`).

### 5.3 Modus- und Zeitdialoge

`forwardFromModeSelect` / `forwardFromTimeSelect` steuern den Vorwärtsfluss (`:406-498`), `backFromTimeSelectAction` und `backFromHouseControlRoomSelectAction` den Rückwärtsfluss (`:500-547`). Die Ansichten sind im HTML verankert:

| Ansicht | Selektor | Überschrift | Quelle |
|---|---|---|---|
| Raumtemperatur | `.j_roomTemperatureView` / Template `.j_roomTemperatureViewTemplate` | `<Raum> – @@roomControl@@` | `index.html:749-775` |
| Zeitmodus | `.j_timeSelectView` | `<Raum> – @@h1TimeModeSelect@@` | `:777-811` |
| Temporäre Zeit | `.j_setTemporaryTime` | `<Raum> – @@h1SetTemporaryTime@@` | `:812-842` |
| Haus-Raumauswahl | `.j_houseControlRoomSelection` | `@@h1HouseControlRoomSelect@@` | `:843-865` |
| Senden | `.j_roomControlSend` mit Unteransichten | `<Raum> – @@labelTransmitting@@` / `@@labelSuccess@@` / `@@labelError@@` | `:866-906` |

`showSetTemporaryTimeView()` und `showSuccess()` blenden die entsprechenden Unteransichten ein (`:757-776`); `showException()` zeigt den Fehlerfall (`:777-785`). `extendCubeState(roomID)` aktualisiert den lokalen Zustand nach dem Senden (`:786-812`).

**Beleg:** `extracted/rootwar/js/controllers/roomControlController.js:79-954`; `extracted/rootwar/index.html:749-906`.

---

## 6. (B) Geräte- und Raumkonfiguration

### 6.1 Konfigurationsmenü (`IndexConfController`)

**Zweck:** Einstieg in alle raum- und hausbezogenen Einstellungen.

| Aspekt | Beschreibung | Quelle |
|---|---|---|
| Eingaben | Klick auf Konfigurationsziel, Raumauswahl, Checkboxen | `indexConfController.js:68-115`, `:268-290` |
| Ausgaben | Konfigurationsübersicht, Overlay mit Zielansicht | `:116-215`, `:560-578` |
| Aktionen | `showRoomConfAction`, `showDeviceConfAction`, `showWeeklyProgramAction`, `showTemperatureConfAction`, `showBoostConfAction`, `showDecalcificationConfAction`, `showDeviceOffsetConfAction`, `showPushButtonConfAction`, `setWallThermTempModeConfAction` | `:116`, `:386`, `:481`, `:506`, `:522`, `:531`, `:540`, `:472`, `:550` |
| Navigation | Zurück über `closeOverlayAction` / `backFromDeviceConfig` | `:560`, `:569` |
| Hausmodus | `openHouseConfig()`, `bindSetHolidayMode()`, `showSetHolidayMode()` | `:236`, `:220`, `:397` |
| Allgemein | `showSetAutoModeAction`, `showSetComfortModeAction`, `showSetEcoModeAction` | `:427`, `:440`, `:454` |

**Beleg:** `extracted/rootwar/js/com_configuration/controllers/indexConfController.js:20-609`. Die Raumkonfigurationsansicht ist `.j_roomConfView` mit `<Raum> – @@h1LabelConfiguration@@` (`index.html:1243-1267`), die Gerätekonfiguration `.j_roomDeviceConfView` mit `@@h3LabelDeviceSettings@@` (`:1268-1288`).

### 6.2 Temperaturkonfiguration

`TemperatureConfController` erlaubt das Ändern von Eco-, Comfort-, Maximal- und Fenster-offen-Temperaturen mehrerer Räume.

| Aspekt | Beschreibung | Quelle |
|---|---|---|
| Eingaben | Temperaturen pro ausgewähltem Raum | `temperatureConfController.js:60-75`, `:149-166` |
| Ausgaben | Warten-/Erfolg-/Fehlerunteransicht | `:143-149`, `:180-194`; `index.html:1429-1492` |
| Aktionen | `forwardFromTemperatureConf`, `getSelectedTemperatures`, `getChangedTemperatures`, `updateMaxCube` | `:60`, `:149`, `:156`, `:167` |
| Navigation | Zurück zur Konfigurationsübersicht über `backToConfOverviewAction` | `:50-59` |
| Kopieren | `forwardFromCopyAction`, `backFromCopyAction` | `:76-142` |
| Comfort-Änderung | `bindComfortTemperatureChangeAction` | `:195-201` |

### 6.3 Boost und Entkalkung

| Ansicht | Zweck | Eingaben / Aktionen | Quelle |
|---|---|---|---|
| `.j_boostConfView` | Boost-Dauer und Ventilwinkel | `boostConfController.js:56-68`, `:76-146`; `index.html:1493-1530` | `updateMaxCube(changedValues)` `:134` |
| `.j_decalcificationConfView` | Entkalkungstag und -stunde | `decalcificationConfController.js:56-68`, `:69-146`; `index.html:1531-1563` | `forwardFromDecalcificationAction` `:56` |

Beide Ansichten besitzen `j_*WaitForResponse`- und `j_showNoSuccess`-Unteransichten (`index.html:1496-1510`, `:1534-1548`) und bieten Kopierfunktionen (`boostConfController.js:70-75`; `decalcificationConfController.js:69-132`).

### 6.4 Geräte-Offset und Wandthermostat-Anzeige

`DeviceOffsetConfController` führt über eine Geräteauswahl (`forwardFromDeviceSelectAction`, `selectDeviceAction`, `setDeviceForOffset`, `gatherDevicesForOffset`) und sendet den Offset über `updateMaxCube(selectedTemperatureOffset, deviceList)` (`deviceOffsetConfController.js:58-210`). Ansichten: `.j_devicesOffsetSelectView` und `.j_devicesOffsetConfView` (`index.html:1564-1628`).

`setWallThermostatDisplayConfigController` konfiguriert die Anzeige von Soll- vs. Ist-Temperatur am Wandthermostat: `forwardFromConfDisplayAction`, `processAPICallResponse`, `updateMaxCube(roomId)` (`setWallThermostatDisplayConfigController.js:55-109`). Ansicht `.j_wallThermostatConfView` (`index.html:1629-1675`).

### 6.5 Tasterkonfiguration und Konfigurationskopie

- `PushButtonConfController`: `forwardFromPushButtonConfAction` (`:51-112`), Auswahländerung über `changeSelectBoxAction` (`:113-118`). Ansicht `.j_pushButtonConfView` (`index.html:2106-2149`), Überschrift `@@h1LabelPushButtonConfig@@`.
- `copyConfView` / `j_copyConfView`: kopiert Konfigurationen zwischen Räumen (`index.html:1676-1702`); wird von mehreren Controllern als `forwardFromCopyAction` / `backFromCopyAction` genutzt (z. B. `temperatureConfController.js:76-142`, `weeklyProgramController.js:1707-1785`).

---

## 7. (B) Wochenprogramm

### 7.1 Zweck

Grafischer Editor für die sieben Tagesprofile mit Schaltpunkten, der über 1800 Zeilen Controllerlogik umfasst.

| Aspekt | Beschreibung | Quelle |
|---|---|---|
| Eingaben | Maus-/Touch-Interaktion auf Balken, Tagesauswahl, schnelle +/- Buttons, Kopie-Dialog | `weeklyProgramController.js:96-121`, `:195-353`, `:643-823` |
| Ausgaben | Temperaturkurve pro Tag, Editier-Tooltip, Sende-/Fehlerstatus | `:723-760`, `:1015-1125`; `index.html:1289-1428` |
| Aktionen | `updateDailyProgramAction`, `addIntervalAction`, `deleteInterval`, `changeIntervalTimeSpan`, `validateTime` | `:1293`, `:1152`, `:1407`, `:1480`, `:1521` |
| Navigation | Zurück zur Konfigurationsübersicht (`backToConfOverviewAction`), Kopie (`forwardFromCopyAction`) | `:1180-1197`, `:1716-1785` |
| Tabs | `tabAction`, `setVisibleProgram(dayId)` | `:1126-1151` |
| Validierung | Zeitfenster prüfen, minimale Differenzen korrigieren, auf 0,5 °C runden | `:1049-1090`, `:1521-1536` |

### 7.2 Interaktionsdetails

- Resize von Intervallen: `initResizable`, `start`, `resize`, `stop`, `setCurrentDragDirection`, `resizeTimeInterval` (`weeklyProgramController.js:195-578`).
- Implizites Löschen benachbarter Intervalle beim Verschieben: `removePrevResizeInterval`, `removeNextResizeInterval`, `implicitDeleteToTheRight`, `implicitDeleteToTheLeft`, `implicitDeleteCurrent`, `implicitDeleteWithin` (`:579-643`, `:1537-1666`).
- Schnellbearbeitung: `showFastEditing`, `closeFastEditing`, `bindFastEditingSubmit`, `fastEditingPlusPressed`, `fastEditingMinusPressed` (`:643-823`).
- Koordinatenumrechnung: `convertHeightToTemperature`, `convertWidthAndPositionToTime`, `getMousePosition` (`:1091-1125`, `:1229-1245`).
- Tooltips: `initToolTip`, `openToolTip`, `closeToolTip`, `bindTooltipHelp` (`:1198-1270`, `:1836`).
- Hinweis bei deaktivierten Checkboxen: `showAttentionWhenClickOnDisabledCheckbox` (`:1815-1835`).

**Beleg:** `extracted/rootwar/js/com_configuration/controllers/weeklyProgramController.js:21-1842`; `extracted/rootwar/js/com_configuration/views/weeklyProgramConfView.js:1-930`; `extracted/rootwar/index.html:1289-1428`.

---

## 8. (B) Strukturverwaltung

### 8.1 Einstieg (`IndexStructureController`)

**Zweck:** Cube-Verbindung, Erkennung, Firmware, IP-Eingabe und Airtime-Überwachung.

| Aspekt | Beschreibung | Quelle |
|---|---|---|
| Eingaben | IP-Felder, Cube-Auswahl, Buttons | `indexStructureController.js:81-129`, `:552-636` |
| Ausgaben | Erkennungsliste, Status, Fehlerviews | `:190-311`, `:753-815`; `index.html:909-1000`, `:2213-2329` |
| Aktionen | `checkNewSoftware`, `showCubeDiscovery`, `selectCubeAction`, `checkFirmwareVersion`, `performCubeFirmwareUpdate`, `connectToCube`, `stillAlivePing`, `disconnectFromCubeAction` | `:130`, `:190`, `:272`, `:313`, `:401`, `:637`, `:723`, `:738` |
| Navigation | Erkennung → IP/Netzwerk → Verbindung → Steuerung | `:289-312`, `:637-710` |
| Fehler | `lostConnectionAction`, `notConnectedAction`, `errorFirmwareUpdate`, `dutyCycleFullCheck` | `:753`, `:786`, `:502`, `:803` |

### 8.2 Raumstruktur (`ChangeRoomStructureController`)

**Zweck:** Räume umbenennen/löschen, Geräte umbenennen/löschen/verschieben und Raumreihenfolge ändern.

| Aktion | Funktion | Quelle |
|---|---|---|
| Raum umbenennen | `bindChangeRoomNameEvents`, `changeRoomNameAction` | `changeRoomStructureController.js:190-311` |
| Raum löschen | `bindDeleteRoomEvents`, `performDeleteRoom`, `deleteRoomAction` | `:312-391` |
| Gerät umbenennen | `bindChangeDeviceNameSelectEvents`, `bindChangeDeviceNameEvents`, `changeDeviceNameSelectAction` | `:410-538` |
| Gerät löschen | `bindDeleteDeviceEvents`, `isDeleteAllowed`, `removeDevicesAction`, `deleteDeviceAction` | `:539-769` |
| Gerät verschieben | `moveDeviceAction`, `finalizeMoveDevice` | `:770-1031` |
| Raum positionieren | `roomPositionAction`, `bindRoomPositionEvents`, `getRoomIdsFromOrderedRooms` | `:1105-1192` |
| Zustand aktualisieren | `refreshMaxCubeState`, `refreshIndexView` | `:1032-1078`, `:1241-1249` |
| Limits | `bindRoomLimitReachedViewOkBtn`, `getNumberOfRooms` | `:1003-1013`, `:139-145` |

Ansichten im HTML: `.j_roomStrucView` (`:1703-1725`), `.j_changeRoomNameView` (`:1726-1756`), `.j_deleteRoomView` (`:1757-1807`), `.j_changeDeviceNameSelectView` (`:1808-1829`), `.j_changeDeviceNameView` (`:1830-1858`), `.j_deleteDeviceView` (`:1860-1896`), `.j_changeRoomPositionView` (`:1942-1956`), `.j_roomStrucTransmitView` (`:1957-1991`). Hinweise zum Moduswechsel: `.j_sourceModeChangeMessage` / `.j_targetModeChangeMessage` (`index.html:1893-1894`). **INFERENZ:** Beim Verschieben oder Löschen kann sich der Steuerungsmodus ändern; die UI warnt dafür explizit.

### 8.3 Neue Geräte (`AddNewDeviceController`)

| Aspekt | Beschreibung | Quelle |
|---|---|---|
| Eingaben | Suchstart, Geräteauswahl, Raumauswahl, Gerätename, Neuer-Raum-Name | `addNewDeviceController.js:117-148`, `:265-443`, `:520-679` |
| Ausgaben | Suchansicht, „kein Gerät“, Geräteinfo, Raumauswahl, Namensfeld, Erfolg | `index.html:528-715`; `addNewDeviceController.js:195-264`, `:680-901` |
| Aktionen | `getNewDevices`, `selectDevice`, `confirmNewDeviceAction`, `createNewRoomAction`, `setDeviceNameAction`, `sendNewDevicesConfigAction` | `:195`, `:265`, `:296`, `:520`, `:595`, `:680` |
| Navigation | Suche → Auswahl → Raum → Name → Senden → Erfolg; `closeOverlayAction` bricht ab | `:957-967` |
| Limits | `setMaxDevicesPerType`, `countDevicesOfThisTypeInThisRoom`, `hasMaxNumberOfDevicesAchieved`, `isForbiddenDeviceAvailable` | `:403-444` |
| Sonderfall | Hausgeräte ohne Raum: `houseArrangeNewDevice` vs. `roomArrangeNewDevice` | `:727`, `:765` |
| Erstinstallation | `openOverlay`, `SearchDeviceView.setFirstInstallHeader()` über `globalSettingsMenuController.js:245-246` | `addNewDeviceController.js:163-194` |

**Beleg:** `extracted/rootwar/js/com_structure/controllers/addNewDeviceController.js:20-1033`; `extracted/rootwar/index.html:528-715`.

---

## 9. (B) Netzwerk, Zeit, Firmware und Internet

### 9.1 Cube-Netzwerkkonfiguration

**Zweck:** IP, Subnetz, Gateway, DNS und DHCP des Cubes anzeigen und ändern.

| Aspekt | Beschreibung | Quelle |
|---|---|---|
| Eingaben | Vier IP-Oktettfelder je Adresse, DHCP-Checkbox | `index.html:1043-1100`; `cubeNetworkSettingController.js:166-209` |
| Ausgaben | Netzwerkformular, Warten-/Erfolg-/Fehleransicht | `index.html:976-1099`; `cubeNetworkSettingController.js:111-165` |
| Aktionen | `getCubeNetworkConfAndOpenView`, `collectValues`, `sendConfig`, `bindActivateDhcpAction` | `:71`, `:166`, `:185`, `:210` |
| Navigation | Zwei Einstiege: aus Cube-Erkennung oder aus IP-Maske | `:46`, `:55` |
| Fehler | `onFail(e, errors)`, `bindBackFromErrorAction` | `:134`, `:226` |
| Validierung | Felder mit `elvMin=0`, `elvMax=255`, `checkInteger=true`, `maxlength=3`, `required` | `index.html:1047-1055` |

`dhcpOffSubview` wird bei deaktiviertem DHCP eingeblendet (`index.html:1015`). Das zugrunde liegende Objekt ist `NetworkConfiguration` mit den belegten Defaults `192.168.0.123 / 255.255.255.0 / 192.168.0.1` und `dhcpEnabled=true` (`decompiled/essentials/sources/de/eq3/max/al/core/cubestate/NetworkConfiguration.java:17-24`).

### 9.2 Cube-Zeit

`setCubeTimeController` zeigt die Zeitdifferenz zwischen Browser und Cube und setzt die Cube-Zeit: `showOverlayAction`, `sendAction`, `setCubeTime`, `getTimeDiffFrom2000`, `getUserDate` (`setCubeTimeController.js:57-168`). Ansicht `.j_setCubeTimeView` mit `@@headlineSetCubeTime@@` (`index.html:2227-2253`). Die Zeitzone wird im globalen Einstellungsmenü konfiguriert (siehe 10.1). Der Block `localCubeUpdateController.js:97` (`updateCubeTime`) zeigt eine lokale Synchronisierung bei Offline-Betrieb.

### 9.3 Firmware und Softwareupdate

| Ansicht | Selektor | Aktion | Quelle |
|---|---|---|---|
| Neue Software | `.j_newSoftwareAvailableView` | `checkNewSoftware()` | `index.html:2150-2173`; `indexStructureController.js:130-179` |
| Firmware prüfen | `.j_checkFirmwareVersionView` | `checkFirmwareVersion()` | `index.html:2174-2193`; `indexStructureController.js:313-400` |
| Firmware aktualisieren | `.j_updateFirmwareView` | `performCubeFirmwareUpdate()`, `checkFirmwareUpdateStatus()` | `index.html:2194-2212`; `indexStructureController.js:401-513` |
| Abschluss | `finishFirmwareUpdate(param)` | Erfolg/Nachricht | `:514-551` |

Texte: `"newSoftwareAvailable" : "Software Check!"`, `"checkFirmwareVersion" : "Prüfen der Firmware"`, `"updateFirmwareVersion" : "Aktualisierung des <Cube>s"` (`jquery.translate.lang_de.js:56`, `:57`, `:64`). Versionsgrenzen: `latestCubeFirmwareVersion=275`, `leastCompatibleFirmwareVersion=273` (`max.local.properties:17-18`).

### 9.4 Internet / Fernzugriff (`SettingsInternetController`)

| Aspekt | Beschreibung | Quelle |
|---|---|---|
| Eingaben | Benutzername, Passwort, Remote-Aktivierung, Passwortstärke | `settingsInternetController.js:58-92`, `:256-330` |
| Ausgaben | Formular, Warten-/Erfolg-/Fehleransicht, Zugangsdaten | `:93-218`; `index.html:1153-1223` |
| Aktionen | `setRemoteAction`, `sendDataAction`, `setRemoteCredentials`, `checkUserData`, `registerPasswordStrength`, `showLabelPasswordStrength` | `:74`, `:156`, `:219`, `:256`, `:293`, `:317` |
| Navigation | `showOverlayAction` öffnet, `closeOverlayAction`/`closeSuccessView`/`closeOverlay` schließen | `:93`, `:331`, `:345`, `:376` |
| Validierung | Clientseitige Prüfung plus `RemoteCredentialsValidator` (Benutzername nicht leer, `[^\r\n,]+`) | `:256-292`; `RemoteCredentialsValidator.java:12`, `:24-38` |

Ansicht `.j_settingsInternetView` mit `@@internetSettings@@` und Unteransichten `.j_setRemoteAccessWaitForResponse`, `.j_showSuccess`, `.j_showNoSuccess`, `.j_getCredentialError`, `.j_changeAccess`, `.j_userData` (`index.html:1153-1223`).

### 9.5 Produktkonto / Fernaktivierung

`IndexController` steuert die Kontoansichten: `checkUrlProductKey` (`:157-163`), `showRemoteActivationDataIfNecessary` (`:658-669`), `showRemoteAccountButton` (`:670-687`), `showUserRemoteSoonInvalidView` (`:688-704`), `showProductAccountManagementView` (`:705-712`), `showProductAccountInfoView` (`:713-720`), `activateUserAccountManagementBtn` (`:729-745`), `bindForwardFromRemoteSoonInvalidView` (`:746-760`). Zugehörige Views: `activateProductAccountView.js:1-182`, `activateProductAccountSuccessView.js:1-96`, `showProductAccountInfoView.js:1-73`, `remoteSoonInvalidView.js:1-137`. Das Modell schaltet die Kontoverwaltung über eine buildzeitige Variable ab, wenn `accountManagementActive` nicht `true` ist (`maxCubeModel.js:32-36`, `:45-60`).

---

## 10. (B) Globale Einstellungen, Hilfe und Status

### 10.1 Globales Einstellungsmenü

`GlobalSettingsMenuController` bietet vier Einträge, die über `confModeSelected` unterschieden werden:

| Schlüssel | Ziel | Quelle |
|---|---|---|
| `timezoneConf` | Zeitzone / Sommer-Winter-Umschaltung | `globalSettingsMenuController.js:33`, `:83-87` |
| `internetSettings` | Internetsteuerung | `:34`, `:88-90` |
| `productAccountActivationOverlay` | Kontoaktivierung | `:35`, `:91-93` |
| `productAccountInformationOverlay` | Kontoinformationen | `:36`, `:94-96` |

Ansicht `.j_globalSettingsMenuView` mit `@@globalSettings@@` (`index.html:1224-1242`). `selectConfModeAction` markiert die Auswahl und löst bei den ersten beiden Einträgen sofort `btnOk.click()` aus (`:121-132`). `backFromSetTimezoneAction` unterscheidet Erstinstallation und normalen Aufruf (`:169-188`). `forwardFromSetTimezoneAction` speichert über `MaxCubeModel.setTimeZoneConfiguration` und zeigt bei Fehlern `@@h2AnErrorOccured@@` plus Fehlercode (`:194-251`).

### 10.2 Zeitzone

`SetTimezoneView` bindet an `.j_TimezoneView` (`index.html:2014-2105`). Bei Auswahl von `noChange` werden OK-Button und Sommer/Winter-Auswahl deaktiviert (`globalSettingsMenuController.js:134-151`). Die Hauskonfiguration `.j_houseConfView` liegt bei `index.html:1992-2013`.

### 10.3 Hilfe

`HelpController` (570 Zeilen) und `helpController.js`-bezogene Views (`activateHelpView.js:1-58`) sowie die Sprachdatei `jquery.translateHelp.lang.js` (834 Zeilen) zeigen, dass es ein eigenes, mehrsprachiges Hilfesystem gibt. `IndexController.setDirtyStateHelp()` merkt sich laufende Verarbeitungen (`indexController.js:761-765`), `HelpController.setHelpActive()` wird nach der Erstinstallation aktiviert (`globalSettingsMenuController.js:184`, `:247`).

### 10.4 Gemeinsamer Sende-/Statusdialog

`mvc.v.GeneralSendView` ist ein wiederverwendbarer Statusdialog mit drei Unteransichten:

| Unteransicht | Selektor | Quelle |
|---|---|---|
| Warten | `.j_waitingForResponse` | `generalSendView.js:29`, `:34` |
| Erfolg | `.j_showSuccess` | `:35`, `:48-50` |
| Fehler | `.j_showNoSuccess` | `:36` |

Buttons: `.j_btnOK` (Vorwärts) und `.j_btnBack` (Zurück), konfigurierbar über `initialForwardClasses` / `initialBackClasses` (`generalSendView.js:38-55`, `:69-77`). Das HTML dazu liegt bei `index.html:2254-2300` mit `@@labelTransmitting@@`, `@@transferSuccessful@@`, `@@transferNotSuccessful@@`. **Befund:** In `generalSendView.js:53-55` zeigen die Fehler-Selektoren auf `.j_showSuccess` statt auf `.j_showNoSuccess`; das ist ein im Code sichtbarer Fehler, der in einer Neuimplementierung zu beheben ist.

Weitere Statusansichten: `.j_cubeLastPingView` (`@@headlineCubeNotConnected@@`, `index.html:2213-2226`), `.j_cubeLostConnectionView` (`:2301-2309`), `.j_noMoreDevicesPossibleView` (`:2310-2318`), `.j_roomLimitReachedView` (`:2319-2329`), `.j_dutyCycleFullView` (`@@h1dutyCycleFull@@`, `:515-527`). **UNBEKANNT:** Der genaue Text von `@@h1dutyCycleFull@@` liegt nicht im ausgewerteten DE-Auszug.

### 10.5 Fehlercodes in der UI

`APICalls.errorHandler(message, exception)` und `warningHandler(message, exception)` sind zentrale Fehlerbehandler (`APICalls.js:2514-2546`). Fehlercodes stammen aus `extracted/essentials/errorcodes.properties:1-53` (z. B. `MAX0001`, `MAX1015`, `MAX1030`, `MAX1051`). Die UI zeigt Fehler meist als `@@h2AnErrorOccured@@` plus `@@<Code>@@` an (z. B. `globalSettingsMenuController.js:229`). **INFERENZ:** Die `@@...@@`-Tokens werden über die Sprachdateien aufgelöst; ein unbekannter Code würde als Token sichtbar bleiben.

---

## 11. Responsives Verhalten, Mobile und Browser-Erkennung

### 11.1 Viewport und mobile Erkennung

`index.html:20` setzt `width=390, initial-scale=1.0, user-scalable=no` – ein festes Layout, **kein** fluid-responsive Grid. `$.fn.mobileDetect()` prüft in dieser Reihenfolge: GET-Parameter `mobile=true/false`, globale/jquery-Variable `$.mobile === true`, dann User-Agent-Liste mit über 30 Schlüsseln (`midp`, `iphone`, `ipod`, `android`, `webOS`, `windows phone`, `iemobile`, `opera mini`, `blackberry`, `nokia`, `samsung`, …) (`jquery.mobileDetect.js:26-90`).

`IndexController.initAction` speichert das Ergebnis in `this.isMobile` (`indexController.js:94`). Bei Mobile **oder** kleiner Viewport (`height < 470 || width < 550`) wird `isSmallDisplay=true` gesetzt und anschließend `.j_headerControls`, der Hauskonfigurations-Button und der Raumkonfigurations-Button ausgeblendet (`indexController.js:136-154`). Für mobile Clients wird `IndexRemoteController.isMobile = true` übernommen (`:616-617`).

**INFERENZ:** „Responsiv“ bedeutet hier gezieltes Ausblenden von Navigation bei kleinem Display, nicht automatische Neuanordnung. **UNBEKANNT:** Es gibt keine belegten CSS-Media-Queries in den betrachteten Dateien; `css/max.css` und `css/jsApp.css` wurden nicht im Detail ausgewertet.

### 11.2 Browsercheck

`browserCheck()` zeigt bei nicht-mobilen Clients und noch nicht akzeptierter Warnung eine Ersatzansicht, wenn eine der folgenden Bedingungen zutrifft (`indexController.js:307-318`):

| Bedingung | Quelle |
|---|---|
| Internet Explorer, Versionsziffer < 7 | `:308` (`$.browser.msie`, `parseInt(...substr(0,1)) < 7`) |
| Opera (jede Version) | `:308` (`$.browser.opera`) |
| Chrome mit Version < `534.10` (Kommentar: Chrome 8) | `:308` |
| Safari mit Version < `533` (Kommentar: Safari 5.0.5.*) | `:308` |
| Mozilla/Firefox mit Version < `1.9.2.16` (Kommentar: FF 3.6.*) | `:308` |

Dann wird `.pageWrapper` ausgeblendet, `NoSupportedBrowserView` eingeblendet und `registerForwardFromOldBrowserWarning()` registriert (`:309-314`). Die View selbst ist minimal: `viewSelector: "#j_notSupportedBrowsers"`, leere `init()` (`noSupportedBrowserView.js:16-34`). Browser-Icons (`chrome`, `ff`, `ie`, `opera`, `safari`) liegen unter `extracted/rootwar/images/`. **INFERENZ:** Die Warnung ist als bewusster Abbruch mit anschließender Weiterleitung konzipiert; der genaue Zieltext ist nicht im DE-Auszug belegt.

---

## 12. Sprachabdeckung der Browser-UI

| Sprache | Datei | Quelle |
|---|---|---|
| Deutsch | `jquery.translate.lang_de.js` (523 Zeilen) | `extracted/rootwar/js/plugins/lang/` |
| Englisch | `jquery.translate.lang_en.js` (523) | ebenda |
| Französisch | `jquery.translate.lang_fr.js` (524) | ebenda |
| Italienisch | `jquery.translate.lang_it.js` (523) | ebenda |
| Niederländisch | `jquery.translate.lang_nl.js` (522) | ebenda |
| Polnisch | `jquery.translate.lang_pl.js` (522) | ebenda |

`IndexController.getAvailableLanguages()` liefert genau `[DE, EN, FR, IT, NL, PL]` (`indexController.js:125-134`). Die Sprachwahl erfolgt über Flags (`bindLanguageChangeClick`, `:323-352`), über Cookie (`isLanguageCookieSet`, `:430`) und über Browser-Sprache (`getBrowserLanguage`, `:413`; `isBrowserLangSupported`, `:418`; `chooseRightLanguage`, `:444-479`). Der gespeicherte Wert wird über `MaxCubeModel.setLocale` an den Server geschrieben (`maxCubeModel.js:246-252`). **INFERENZ:** Die Sprachauswahl ist damit teils clientseitig (Cookie), teils serverseitig (Locale) verankert.

---

## 13. Quellenindex nach Ansicht

| Ansicht / Funktion | Primärdatei | HTML-Anker |
|---|---|---|
| Hauptansicht | `js/views/indexView.js:1-953` | `index.html:263-457` |
| Raumsteuerung | `js/controllers/roomControlController.js:79-954` | `index.html:749-906` |
| Konfigurationsmenü | `js/com_configuration/controllers/indexConfController.js:68-609` | `index.html:1243-1288` |
| Wochenprogramm | `js/com_configuration/controllers/weeklyProgramController.js:96-1842` | `index.html:1289-1428` |
| Temperatur/Boot/Entkalkung/Offset | `js/com_configuration/controllers/{temperature,boost,decalcification,deviceOffset}ConfController.js` | `index.html:1429-1675` |
| Taster & Haus | `js/com_configuration/controllers/pushButtonConfController.js`, `indexConfController.js:236-268` | `index.html:1992-2149` |
| Struktur | `js/com_structure/controllers/changeRoomStructureController.js:78-1310` | `index.html:1703-1991` |
| Neue Geräte | `js/com_structure/controllers/addNewDeviceController.js:117-1033` | `index.html:528-715` |
| Netzwerk | `js/com_structure/controllers/cubeNetworkSettingController.js:33-271` | `index.html:976-1100` |
| Zeit | `js/com_structure/controllers/setCubeTimeController.js:37-181` | `index.html:2227-2253` |
| Firmware/Update | `js/com_structure/controllers/indexStructureController.js:130-551` | `index.html:2150-2212` |
| Internet/Remote | `js/com_structure/controllers/settingsInternetController.js:58-384` | `index.html:1153-1223` |
| Global Settings | `js/controllers/globalSettingsMenuController.js:38-252` | `index.html:1224-1242`, `:2014-2105` |
| Produktkonto | `js/controllers/indexController.js:658-760`, `js/views/activateProduct*.js` | `index.html` (Overlay) |
| Hilfe | `js/controllers/helpController.js:1-570` | `js/views/activateHelpView.js:1-58` |
| Status/Fehler | `js/views/generalSendView.js:1-177`, `js/lib/APICalls.js:2514-2546` | `index.html:1846-1856`, `:2254-2329` |
| Browserwarnung | `js/controllers/indexController.js:307-318`, `js/views/noSupportedBrowserView.js:16-34` | `#j_notSupportedBrowsers` |
| Desktop-Shell | `decompiled/maxlocalapp/sources/de/eq3/max/MaxLocalAppStarter/` | `extracted/maxlocalapp/*.properties` |

---

## 14. Bewusste Abweichungen für einen Neubau (INFERENZ)

1. Nicht die `j_*-Klassen und `@@token@@`-Plattform übernehmen, sondern explizite Komponenten mit Datenbindung und einem zentralen Fehlercode-Resolver bauen.
2. Die feste Viewport-Breite 390 (`index.html:20`) durch ein echtes responsives Layout ersetzen; das bestehende Verhalten nur als Anforderung an kleine Displays verstehen (`indexController.js:136-154`).
3. Die veraltete Browsererkennung über `$.browser` (`indexController.js:308`) durch Feature-Detection ersetzen; die unterstützten Mindestversionen (IE 7, Chrome 8, Safari 5.0.5, FF 3.6) nur als historische Referenz dokumentieren.
4. Den Sende-Statusdialog mit korrekten Selektoren für Erfolg und Fehler implementieren (`generalSendView.js:53-55` enthält einen belegten Fehler).
5. Die zwei Default-Welten (`max.properties` vs. Java-Konstruktoren, siehe `domain-model.md` Abschnitt 3.2) in der GUI nicht vermischen.
6. Shell und Browser UI über eine gemeinsame Locale-Instanz konfigurieren, aber getrennte Ressourcenbundles beibehalten (`Application.java:68-69`; `maxCubeModel.js:246-252`).
