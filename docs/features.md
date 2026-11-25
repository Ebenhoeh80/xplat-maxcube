# Feature-Katalog – MAX! Cube Local Application

Priorisierte Funktionsliste als Grundlage für eine Neuimplementierung. Quellen sind ausschließlich die lokal vorliegenden dekompilierten Java-Klassen, Browser-JS-Dateien, HTML-Templates und Properties-Dateien. Es handelt sich nicht um eine 1:1-Portierung: Die Matrix beschreibt *fachliche Fähigkeiten*, nicht die vorhandene technische Realisierung.

## 1. Bewertungsschema

| Dimension | Werte | Bedeutung |
|---|---|---|
| Priorität | `P0` | Kernfunktion ohne die das Produkt nicht betriebsfähig ist (belegt durch Haupt-/Startansicht) |
| | `P1` | Wichtige Konfigurations- und Komfortfunktion |
| | `P2` | Erweiterung, Komfort, Kompatibilität oder Diagnose |
| Aufwand | `S` | < 1 Personentag, isolierte Funktion |
| | `M` | 1–3 Personentage, mehrere Ansichten/APIs |
| | `L` | 3–10 Personentage, State-Management und Hardwareprotokoll betroffen |
| | `XL` | > 10 Personentage, querschnittlich oder hardwarenah |
| Komplexität | `niedrig` / `mittel` / `hoch` / `sehr hoch` | Technische Schwierigkeit unabhängig vom Umfang |

**INFERENZ:** Aufwand und Komplexität sind Schätzungen aus der Struktur des Quellmaterials (z. B. Dateigrößen, Anzahl beteiligter Controller, Validierungsregeln), keine gemessenen Werte. Die Priorisierung folgt der Sichtbarkeit in der Startansicht und der Anzahl abhängiger Funktionen.

---

## 2. Feature-Matrix

Status (Stand 24.11.2026, Gremium Runde 5): `umgesetzt` = lokal nutzbar, `teilweise` = Kernfunktion mit Einschränkung, `ersetzt` = durch eine lokale Alternative abgebildet, `verworfen` = nicht abbildbar oder bewusst ausgeschlossen (Cloud abgeschaltet, Browser/Jetty, Firmware-Schreiben), `fehlt` = nicht verdrahtet (derzeit keine Zeile). Die Matrix beschreibt weiterhin das Original; der Status bewertet die C#-App.

**Belegtiefe der umgesetzten Zeilen (ehrliche Einordnung, kein Hardwarebeweis):**

| Belegstufe | Zeilen |
| --- | --- |
| Quell-/live-belegt (Format aus Java oder echtem Gerät) | Raumsteuerung (0x40), Wochenprofil (0x10, live bewährt), Temperaturkonfiguration/Fenster/Offset (0x11, Java-Bitfelder, Byte-Test), Wandthermostat-Anzeige (0x82, am echten Gerät bestätigt) |
| Abgeleitet (INFERENZ) und nur gegen Fake-Cube/Codec getestet | Boost/Entkalkung/Ventil-Offset (0x12, Spiegel des Lesepfads), Gruppen-/Linkzuordnung (0x20–0x23), Metadaten-Schreiben (`m:`, mit Rundreise-Sicherheitsprüfung), Taster (`w:`), Zeitzone/Zeit (`v:`), NTP (`f:`), Netzwerk (UDP `c`/`C`) |
| Rein lokal (keine Cube-Kommunikation) | i18n, Sprachwahl, Fehlercodes, Statusdialoge, Hilfesystem, Logdatei-Bereinigung, Instanzschutz, Tray, gebündelte Kommandos, kleine Displays, Diagnosebericht |

**Nicht abbildbar / bewusst nicht enthalten (`verworfen`):** Internetsteuerung, Produktkonto, Kontoinformationen, Remote-Ablaufwarnung, Passwortstärke (Cloud seit 01.07.2023 abgeschaltet, Remote-Proxy ist Cloud-HTTP); Proxy-Konfiguration (nur für Cloud/Update nötig); App-Software-Update und Update-Splash (kein Update-Server, kein HTTP-Check); Cube-Firmware aktualisieren (Firmware-Schreiben bleibt aus Sicherheitsgründen deaktiviert: Gerätedefekt-Risiko, keine Firmwaredatei verfügbar); Browserwarnung (kein Browser). `Fehlerbericht senden` ist `ersetzt` durch den lokalen Diagnosebericht ohne Versand. `Wochenprogramm` bleibt `teilweise`: Tagesleiste und Schnellbearbeitung (±5 min, ±0,5 °C) sind da, ein Drag-and-drop-Editor nicht.

**FAKT (Nutzerhinweis 25.11.2026, in dieser Sitzung nicht extern verifiziert):** eQ-3 hat die MAX!-Cloud-Steuerung abgeschaltet und nach Angabe des Nutzers auch die zugehörige Cloud-App aus dem Android- und iOS-App-Store entfernt. Die dokumentierte Abschaltung seit 01.07.2023 wird dadurch ergänzt, nicht ersetzt. Fernzugriff, Produktkonto, Proxy und App-Update bleiben damit dauerhaft `verworfen`; die C#-App bleibt lokal.

| Feature | Beschreibung | Priorität | Status | Aufwand | Begründung | Komplexität | Abhängigkeiten | Quelle |
|---|---|---|---|---|---|---|---|---|
| Cube-Verbindung herstellen | Verbindung zum MAX! Cube über IP aufbauen und trennen | P0 | umgesetzt | L | Ohne Verbindung keine Zustandsdaten | hoch | Netzwerk, Instanzschutz | `MaxLocalApi.java:112`, `:116`; `APICalls.js:1268`, `:1294` |
| Auto-Discovery | Verfügbare Cubes im Netzwerk erkennen und zur Auswahl stellen | P0 | umgesetzt | L | Erstinstallation und IP-unbekannte Installation erfordern Erkennung | hoch | Cube-Verbindung, UDP 23272 | `CubeFinder.java:22-28,47-51,84-106`; `docs/maxcube-protocol.md:47-67`; `max.local.properties:26-27` ist nur der verborgene Cloud-Fallback |
| Manuelle IP-Eingabe | IP des Cubes über vier Oktettfelder mit 0–255-Prüfung eingeben | P0 | umgesetzt | M | Fallback bei fehlgeschlagener Erkennung | mittel | Cube-Verbindung | `index.html:936-975`; `indexStructureController.js:552-636` |
| Zustandsabruf | Räume, Geräte und Zustände über `getMaxCubeState` laden | P0 | umgesetzt | M | Datenbasis der gesamten UI | mittel | DTO-Modell, DWR-Brücke | `MaxLocalApi.java:47`; `maxCubeModel.js:85-100` |
| Raumübersicht | Alle Räume mit Ist-/Solltemperatur und Modus anzeigen | P0 | umgesetzt | M | Hauptansicht der Anwendung | mittel | Zustandsabruf | `indexView.js:1-953`; `index.html:361-457` |
| Raumsteuerung Auto | Raum auf automatische Profilsteuerung setzen | P0 | umgesetzt | S | Default-Modus laut `IRoom` | niedrig | Zustandsabruf, Kommandos | `IRoom.java:20`; `SetRoomAutoMode.java:1`; `APICalls.js:2133` |
| Raumsteuerung Permanently | Feste Solltemperatur über einen Zeitraum setzen | P0 | umgesetzt | S | Zentrale Heizfunktion | niedrig | Raumsteuerung | `roomControlController.js:618-639`; `APICalls.js:2162` |
| Raumsteuerung Temporary | Solltemperatur mit Enddatum/-zeit setzen | P0 | umgesetzt | M | Zeitlich begrenzte Steuerung; Datums-/Zeitdialog nötig | mittel | Raumsteuerung, Cube-Zeit | `roomControlController.js:640-662`, `:757-766`; `APICalls.js:2225` |
| Haussteuerung | Mehrere Räume gleichzeitig auswählen und steuern | P1 | umgesetzt | M | Komfortfunktion für ganze Wohnung | mittel | Raumsteuerung | `roomControlController.js:265-394`, `:837-866`; `index.html:843-865` |
| Solltemperatur per +/- ändern | Inkrementelle Temperaturbedienung auf Raumkacheln und im Dialog | P0 | umgesetzt | S | Direkte Interaktion in der Hauptansicht | niedrig | Raumsteuerung | `index.html:433-444`; `roomControlController.js:179-264` |
| Wochenprogramm | Sieben Tagesprofile mit bis zu 13 Schaltpunkten grafisch bearbeiten | P0 | teilweise | XL | Kern der Heizautomation; größter Controller der UI | sehr hoch | Wochenprofil-Modell, Validierung, Drag/Resize | `weeklyProgramController.js:96-1842`; `WeekTemperatureProfile.java:13-15`; `DayTemperatureProfile.java:16-18` |
| Wochenprogramm kopieren | Tagesprofile/Raumkonfiguration auf andere Räume übertragen | P1 | umgesetzt | M | Mehrfachkonfiguration spart Aufwand | mittel | Wochenprogramm | `weeklyProgramController.js:122-153`, `:1716-1785`; `index.html:1676-1702` |
| Schnellbearbeitung Wochenprogramm | +/- Buttons und Zeitfenster-Tooltip im Diagramm | P2 | umgesetzt | L | Komfort, nicht für Grundfunktion nötig | hoch | Wochenprogramm | `weeklyProgramController.js:643-823`, `:1198-1270` |
| Temperaturkonfiguration | Eco-, Comfort-, Maximal- und Fenster-offen-Temperatur je Raum setzen | P1 | umgesetzt | M | Beeinflusst alle Automatikmodi | mittel | Raumkonfiguration | `temperatureConfController.js:60-201`; `index.html:1429-1492` |
| Boost-Konfiguration | Boost-Dauer und Ventilwinkel einstellen | P1 | umgesetzt | S | Sonderfunktion für schnelles Aufheizen | niedrig | Raumkonfiguration | `boostConfController.js:56-146`; `index.html:1493-1530` |
| Entkalkung konfigurieren | Wochentag und Uhrzeit der Entkalkung festlegen | P1 | umgesetzt | S | Gerätewartung | niedrig | Raumkonfiguration | `decalcificationConfController.js:56-146`; `index.html:1531-1563` |
| Fenster-offen-Logik | Temperatur und Dauer bei geöffnetem Fenster konfigurieren | P1 | umgesetzt | S | Energiesparfunktion | niedrig | Temperaturkonfiguration, ShutterContact | `Room.java:48-49`, `:283-311`; `APICalls.js:2338` |
| Temperatur-Offset | Sensor-/Ventil-Offset pro Gerät korrigieren | P1 | umgesetzt | M | Genauigkeit der Regelung | mittel | Gerätekonfiguration | `deviceOffsetConfController.js:58-210`; `APICalls.js:2440` |
| Wandthermostat-Anzeige | Anzeige von Soll- vs. Ist-Temperatur am Gerät umschalten | P2 | umgesetzt | S | Reines Komfortfeature | niedrig | Gerätekonfiguration | `setWallThermostatDisplayConfigController.js:55-109`; `APICalls.js:2458` |
| Tasterkonfiguration | Oberen/unteren Taster eines PushButton konfigurieren | P1 | umgesetzt | M | Gerätespezifische Funktion | mittel | House-Specific Devices | `pushButtonConfController.js:51-118`; `index.html:2106-2149` |
| Neue Geräte suchen (Inclusion) | Erkennungsmodus starten und gefundene Geräte anzeigen | P0 | umgesetzt | L | Geräte können nur so in die Anlage aufgenommen werden | hoch | Funkprotokoll, Raumzuordnung | `MaxLocalApi.java:54`, `:62`; `addNewDeviceController.js:195-264`; `max.local.properties:9` |
| Gerät einem Raum zuordnen | Gefundenes Gerät mit Name und Raum versehen | P0 | umgesetzt | M | Abschluss der Inclusion | mittel | Neue Geräte, Validierung | `MaxLocalApi.java:79`; `ConfigureDeviceValidator.java:60-77`; `addNewDeviceController.js:359-403` |
| Hausgeräte ohne Raum | Geräte dem `House` statt einem Raum zuordnen | P1 | umgesetzt | M | Taster/Wandthermostate können raumlos sein | mittel | Neue Geräte, House-Modell | `MaxLocalApi.java:75`, `:161`; `addNewDeviceController.js:727` |
| Gerätelimits prüfen | Maximal 8 Heiz-/Fensterkontakte, 1 Wandthermostat pro Raum | P0 | umgesetzt | S | Verhindert ungültige Konfigurationen | niedrig | Validierung | `ConfigureDeviceValidator.java:80-101`; `IRoom.java:17-19` |
| Namensvalidierung | Namen nicht null, ≤ 32 Zeichen, case-insensitive eindeutig | P0 | umgesetzt | S | Datenkonsistenz und Fehlervermeidung | niedrig | Validierung, Fehlercodes | `ConfigureDeviceValidator.java:148-154`, `:123-138`; `max.local.properties:1` |
| Raum umbenennen | Name eines Raums ändern | P1 | umgesetzt | S | Strukturpflege | niedrig | Strukturverwaltung | `changeRoomStructureController.js:190-311`; `MaxLocalApi.java:83` |
| Raum löschen | Raum inkl. Geräte entfernen, optional mit Factory-Reset | P1 | umgesetzt | M | Strukturpflege, Datenverlustrisiko | mittel | Strukturverwaltung | `changeRoomStructureController.js:312-391`; `MaxLocalApi.java:95` |
| Gerät umbenennen | Namen eines Geräts ändern | P1 | umgesetzt | S | Strukturpflege | niedrig | Strukturverwaltung | `changeRoomStructureController.js:410-538`; `MaxLocalApi.java:87` |
| Gerät löschen | Gerät entfernen, Löschen nur bei Erlaubnis | P1 | umgesetzt | M | Strukturpflege | mittel | Strukturverwaltung | `changeRoomStructureController.js:539-769`, `:623-661` |
| Gerät verschieben | Gerät in anderen Raum übernehmen, inkl. Modus-Hinweis | P1 | umgesetzt | L | Komplexe Zustandsänderung mit Warnung | hoch | Strukturverwaltung, Modus | `changeRoomStructureController.js:770-1031`; `index.html:1893-1894` |
| Raumreihenfolge ändern | Drag-and-drop-Sortierung der Räume | P2 | umgesetzt | M | Reine Darstellungskonfiguration | mittel | Strukturverwaltung | `changeRoomStructureController.js:1105-1192`; `MaxLocalApi.java:165` |
| Netzwerkkonfiguration anzeigen/ändern | IP, Subnetz, Gateway, DNS, DHCP des Cubes | P1 | umgesetzt | L | Hardwarenahe Konfiguration mit Fehlerfolgen | hoch | Cube-Verbindung | `cubeNetworkSettingController.js:46-269`; `NetworkConfiguration.java:17-24`; `index.html:976-1100` |
| DHCP-Umschaltung | DHCP an/aus und abhängige Felder ein-/ausblenden | P1 | umgesetzt | S | Teil der Netzwerkkonfiguration | niedrig | Netzwerkkonfiguration | `cubeNetworkSettingController.js:210-225`; `index.html:1015` |
| Zeitzone / Sommerzeit | Zeitzone und automatische Sommer-/Winterzeitumschaltung setzen | P1 | umgesetzt | M | Beeinflusst Wochenprogramm und Cube-Zeit | mittel | Global Settings, Cube-Zeit | `globalSettingsMenuController.js:134-251`; `MaxLocalApi.java:197-205` |
| Cube-Zeit setzen | Zeitdifferenz Browser/Cube anzeigen und Cube-Zeit synchronisieren | P1 | umgesetzt | M | Zeitbasis für Zeitsteuerung | mittel | Zeitzone | `setCubeTimeController.js:57-168`; `MaxLocalApi.java:209` |
| Firmware-Version prüfen | Aktuelle und empfohlene Cube-Firmware vergleichen | P1 | umgesetzt | M | Updateentscheidung | mittel | Update-Service | `indexStructureController.js:313-400`; `max.local.properties:17-18` |
| Cube-Firmware aktualisieren | Firmware-Update mit Fortschritt und Abschlussstatus | P1 | verworfen | XL | Hardwarenah, risikobehaftet | sehr hoch | Firmware-Prüfung, Download | `indexStructureController.js:401-551`; `APICalls.js:1157-1225` |
| App-Software-Update | Neue Client-/Serverversion erkennen und herunterladen | P1 | verworfen | L | Betriebssicherheit und Aktualität | hoch | Update-URL, Shell | `InteractiveSplash.java:297-320`; `VersionChecker.java:1-160`; `max.local.properties:13-14` |
| Internetsteuerung / Fernzugriff | ~~Remote-Zugriff aktivieren und Zugangsdaten setzen~~ **verworfen 24.11.2026** (Cloud abgeschaltet, keine Zugangsdaten in der App) | ~~P1~~ | verworfen | M | Fernbedienung über MAX! Portal | mittel | Produktkonto, Validierung | `settingsInternetController.js:74-330`; `MaxLocalApi.java:120-124` |
| Produktkonto-Aktivierung | ~~Cube über Product Key mit dem Konto verknüpfen~~ **verworfen 24.11.2026** (Remote-Proxy ist Cloud-HTTP, bewusst ausgeschlossen) | ~~P1~~ | verworfen | M | Voraussetzung für Fernzugriff | mittel | Internetsteuerung | `indexController.js:157-163`, `:658-745`; `MaxLocalApi.java:221-225` |
| Kontoinformationen anzeigen | Aktivierungs- und Ablaufinformationen anzeigen | P2 | verworfen | S | Informationsfunktion | niedrig | Produktkonto | `indexController.js:705-720`; `showProductAccountInfoView.js:1-73` |
| Remote-Ablaufwarnung | Hinweis vor Ablauf der Fernzugriffsberechtigung | P2 | verworfen | S | Präventive Information | niedrig | Produktkonto | `indexController.js:688-704`; `remoteSoonInvalidView.js:1-137` |
| Passwortstärke-Anzeige | Visuelle Bewertung des Passworts bei Eingabe | P2 | verworfen | S | Komfort bei der Zugangsdatenvergabe | niedrig | Internetsteuerung | `settingsInternetController.js:293-330` |
| Proxy konfigurieren | HTTP-Proxy in der Desktop-Shell aktivieren und parametrisieren | P1 | verworfen | M | Netzwerke mit Proxy erfordern dies | mittel | Shell, Settings | `ProxySettingsWindow.java:25-142`; `MaxLocalAppStarter_de.properties:1-7` |
| Systemtray-Steuerung | Browser öffnen, Proxy, Fehlerbericht, Beenden über Tray | P1 | umgesetzt | M | Dauerbetrieb ohne sichtbares Fenster | mittel | Shell, Settings | `MaxSystemTrayHandler.java:26-160`; `MaxLocalAppStarter_de.properties:7-14` |
| Fehlerbericht senden | Systemprofil, Problembeschreibung und Logdatei per E-Mail versenden | P2 | ersetzt | M | Support und Diagnose | mittel | Shell, SystemProfiler | `BugReportWizard.java:19-128`; `TextRessources.java:53-83` |
| Update-Splash | Begrüßungsfenster mit Updateprüfung und Sprachauswahl beim Start | P2 | verworfen | M | Erster Eindruck und Updatehinweis | mittel | Shell, Update-Service | `InteractiveSplash.java:61-169`, `:290-478` |
| Instanzschutz und Portwahl | Zweite Instanz verhindern, freien Port finden, Konfliktdialog | P1 | umgesetzt | M | Verhindert Datenkonflikte | mittel | Shell, Jetty | `Application.java:75-91`, `:228-229`; `PortFactory.java:1-36`; `InstanceLock.java:1-78` |
| i18n DE/EN/FR/IT/NL/PL | Sechs Sprachen für Browser-UI und Shell | P0 | umgesetzt | L | Produkt wird mehrsprachig ausgeliefert | mittel | Alle Ansichten | `jquery.translate.lang_{de,en,fr,it,nl,pl}.js`; `indexController.js:125-134`; `TextRessources.java:5-83` |
| Sprachwahl (Cookie/Browser/Setting) | Sprache automatisch wählen oder manuell umschalten | P1 | umgesetzt | M | Benutzerkomfort | mittel | i18n | `indexController.js:323-479`; `maxCubeModel.js:233-252` |
| Fehlercodes MAX0001–MAX1051 | Zentrale Fehlercode-Tabelle und Anzeige in Dialogen | P1 | umgesetzt | M | Verständliche Fehlermeldungen | niedrig | Alle APIs, Statusdialoge | `errorcodes.properties:1-53`; `APICalls.js:2514-2546` |
| Statusdialoge (Warten/Erfolg/Fehler) | Wiederverwendbarer Dialog für alle Schreiboperationen | P0 | umgesetzt | M | Einheitliches Feedback | niedrig | Alle Aktionen | `generalSendView.js:28-77`; `index.html:2254-2300` |
| Airtime-/Verbindungsüberwachung | `stillAlivePing`, Verbindungsabbruch und Duty-Cycle-Hinweis | P1 | umgesetzt | M | Betriebssicherheit bei Funkproblemen | mittel | Cube-Verbindung | `indexStructureController.js:723-815`; `index.html:515-527`, `:2213-2213` |
| Gebündelte Kommandos | Temperaturänderungen 3 s sammeln und gemeinsam senden | P1 | umgesetzt | M | Reduziert Funkverkehr und Keyboard-Lag | mittel | Command-Modell | `indexController.js:119-121`, `:187-217`; `maxCubeModel.js:178-200` |
| Hilfesystem | Mehrsprachige kontextbezogene Hilfe | P2 | umgesetzt | M | Einarbeitung und Support | mittel | i18n | `helpController.js:1-570`; `jquery.translateHelp.lang.js:1-834` |
| Logdatei-Bereinigung | Alte Logs nach konfigurierbarer Anzahl Tage entfernen | P2 | umgesetzt | S | Datenhaushalt | niedrig | Shell-Settings | `ApplicationSettings.java:7`, `:67-78` |
| Offline-/Demo-Modus | Schalter im DWR-Wrapper zum Arbeiten ohne Cube | P2 | umgesetzt | S | Entwicklung und Tests | niedrig | DWR-Brücke | `APICalls.js:30-32`, `:2497-2513` |
| Browserwarnung | Hinweis bei nicht unterstützten Browsern anzeigen | P2 | verworfen | S | Kompatibilitätskommunikation | niedrig | Startcontroller | `indexController.js:307-318`; `noSupportedBrowserView.js:16-34` |
| Mobile/kleine Displays | Navigation bei kleinem Viewport ausblenden | P1 | umgesetzt | S | Bedienbarkeit auf kleinen Geräten | niedrig | Hauptansicht | `index.html:20`; `indexController.js:136-154`; `jquery.mobileDetect.js:26-90` |
| Zwei-Default-Welten trennen | Hardware-Defaults (max.properties) und Laufzeit-Defaults (Java) getrennt modellieren | P0 | umgesetzt | M | Verhindert falsche Solltemperaturen | mittel | Domänenmodell | `max.properties:2-13`; `Room.java:43-64`; `IRoom.java:9-22` |
| DTO/AL-Trennung wahren | Transportobjekte nie direkt in das AL-Aggregat schreiben | P0 | umgesetzt | M | Saubere Architektur, weniger Fehler | mittel | Domänenmodell, APIs | `MaxLocalApi.java:99`; `command/ClientCommand.java:1`; `domain-model.md` Abschnitt 13 |

---

## 3. Priorisierungslogik

### 3.1 P0 – Betriebsfähiger Kern

P0 umfasst alles, was für die Grundfunktion „Heizung über Cube steuern“ nötig ist: Verbindung, Erkennung, Zustandsabruf, Raumübersicht, die drei Modi, Wochenprogramm, Inclusion mit Validierung, Statusdialoge, i18n und die Trennung der Default-Welten. Begründung: Ohne diese Funktionen ist keine steuerbare Anlage erreichbar; sie sind in der Startansicht oder im Erstinstallationsablauf verankert (`indexController.js:86-123`; `globalSettingsMenuController.js:243-248`).

### 3.2 P1 – Konfiguration und Betriebssicherheit

P1 umfasst Strukturpflege, Temperatur- und Sonderfunktionen, Netzwerk, Zeit, Updates, Fernzugriff, Proxy, Tray, Instanzschutz, Fehlercodes und mobile Displays. Diese Features sind nicht für den ersten Heizvorgang nötig, aber für den Dauerbetrieb und die Wartung (`indexConfController.js:68-609`; `Application.java:66-91`).

### 3.3 P2 – Komfort und Diagnose

P2 umfasst Schnellbearbeitung, Kontoinformationen, Ablaufwarnungen, Passwortstärke, Bugreport, Splash, Offline-Modus, Browserwarnung, Hilfe und Logbereinigung. Sie erhöhen Qualität und Supportfähigkeit, blockieren aber keine Kernfunktion.

---

## 4. Komplexitätstreiber

| Treiber | Betroffene Features | Begründung / Quelle |
|---|---|---|
| Grafischer Wochenprogramm-Editor | Wochenprogramm, Schnellbearbeitung, Kopieren | 1842 Zeilen Controllerlogik, Drag/Resize, 13-Punkt-Grenze, 0,5-°C-Rundung (`weeklyProgramController.js:195-1125`) |
| Hardwarenahe Funkoperationen | Inclusion, Firmware, Netzwerk, Connection | Zeitlimits und Timeouts konfiguriert: `inclusionModeTimeoutSeconds=60`, `connectionTimeout=30000`, `socketTimeout=70000` (`max.local.properties:9`, `:23-24`) |
| Zustandskonsistenz | Verschieben, Löschen, Moduswechsel, gebündelte Kommandos | Flags `stateDirty`/`stateChanged` und Modus-Hinweise (`Room.java:40-41`, `:136-158`; `index.html:1893-1894`) |
| Zwei Default-Quellen | Temperaturkonfiguration, Offline-Betrieb, Domänenmodell | `max.properties` weicht von `IRoom.java` ab (siehe Abschnitt 2, letzte Zeile) |
| Sechs Sprachen × zwei Shells | i18n, Sprachwahl, Splash, Hilfe | Browser-Dateien ~520 Zeilen je Sprache, Shell-Resource-Bundles und `TextRessources.java` (`jquery.translate.lang_*.js`; `TextRessources.java:5-83`) |
| Drei Representationsebenen | DTO/AL-Trennung, Zustandsabruf, Kommandos | AL, DTO und Browser-Modell haben abweichende Typen und Defaults (`domain-model.md` Abschnitt 13) |

---

## 5. Abhängigkeiten (INFERENZ)

```
Cube-Verbindung / Auto-Discovery
  ├── Zustandsabruf ──> Raumübersicht ──> Raumsteuerung (Auto/Permanent/Temporary)
  │                                      └──> Haussteuerung
  │                                      └──> gebündelte Kommandos
  ├── Inclusion ──> Namensvalidierung ──> Gerätelimits ──> Raumzuordnung
  │                                      └──> Hausgeräte ohne Raum
  ├── Strukturverwaltung ──> Verschieben ──> Modus-Hinweis
  ├── Netzwerkkonfiguration ──> DHCP-Umschaltung
  ├── Zeitzone ──> Cube-Zeit ──> Wochenprogramm ──> Wochenprogramm kopieren
  ├── Firmware-Prüfung ──> Cube-Firmware-Update
  └── Airtime-Überwachung ──> Statusdialoge

Shell: Instanzschutz ──> Update-Splash ──> Systemtray ──> Proxy / Fehlerbericht
Querschnitt: i18n ──> alle Ansichten; Fehlercodes ──> Statusdialoge; DTO/AL-Trennung ──> alle APIs
```

**Belegungsgrenze:** Der abgebildete Abhängigkeitsgraph ist aus Aufrufketten abgeleitet (z. B. `globalSettingsMenuController.js:245-247` ruft nach der Zeitzone `AddNewDeviceController.openOverlay()`), nicht aus einem expliziten Architekturdiagramm. Er ist daher als **INFERENZ** zu lesen.

---

## 6. Nicht-Ziele / bewusst ausgeschlossen

1. Keine 1:1-Übernahme der DWR-API, der `j_*`-Selektoren oder der `@@token@@`-Plattform; diese sind Implementierungsdetails der Altlösung (`APICalls.js:8-18`; `index.html:22-25`).
2. Keine Übernahme der belegten Fehler im Altcode: falsche Fehler-Selektoren im Statusdialog (`generalSendView.js:53-55`), Tippfehler „Port Einabe“ (`MaxLocalAppStarter_de.properties:18`), Regex `\d+.\d+.\d+` ohne escapte Punkte (`SoftwareInformationValidator.java:18`), inkonsistente Fehlercode-Nummer `MAX10038` (`errorcodes.properties:40`).
3. Keine Unterstützung veralteter Browser über `$.browser`-Erkennung als Zielzustand; nur als historische Referenz dokumentiert (`indexController.js:308`).
4. Keine Annahme, dass `HeatingThermostatPlus` in `Room.isTemperatureControllable()` berücksichtigt wird; der Altcode tut es nicht (`Room.java:176-178`).

---

## 7. Explizite Offenheiten

1. **UNBEKANNT** – Fachliche Regeln hinter `deviceFactoryReset` (`MaxLocalApi.java:91`, `:95`).
2. **UNBEKANNT** – Genaues Update-Protokoll und Dateiformat der Softwareaktualisierung (`VersionChecker.java:1-160`, `DownloadManager.java:1-156`).
3. **UNBEKANNT** – Bedeutung des `encryptionEnabled`-Flags der AL-Netzwerkkonfiguration für die UI (`NetworkConfiguration.java:15` vs. `cl/dto/NetworkConfiguration.java:5-10`).
4. **UNBEKANNT** – Der Text der Browser-Warnansicht `#j_notSupportedBrowsers` und des `@@h1dutyCycleFull@@`-Hinweises im ausgewerteten DE-Auszug.
5. **INFERENZ** – Aufwandsklassen sind Größenordnungen; für das Wochenprogramm und die Firmwareaktualisierung ist eine eigene Machbarkeitsprüfung nötig.
6. **INFERENZ** – Die Priorisierung setzt einen lokalen Cube voraus; für ein reines Cloud-/Remote-Produkt müssten Internetsteuerung und Produktkonto nach P0 hochgestuft werden.
