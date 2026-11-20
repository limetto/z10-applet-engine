# Z10 Applet Engine

Eigene Applets für das 160 × 32-Pixel-LCD der **EVGA Z10** (die ursprüngliche Variante ohne RGB, USB `3842:2601`).
Die Applets setzt du im Layout-Editor aus Bausteinen zusammen. Die Werte kommen aus **LibreHardwareMonitor**.

> Inoffizielles Projekt, nicht von EVGA. Das Display-Protokoll wurde durch Analyse ermittelt, siehe [docs/PROTOCOL.md](docs/PROTOCOL.md).

## Funktionen

- **Applet-Rotation** mit eigener Anzeigedauer und Aktualisierung pro Applet; Rotation anhalten, vor und zurück blättern
- **Layout-Editor** mit Pixelraster, Ziehen mit der Maus, Pfeiltasten und Ebenenliste
- **Bausteine:** Text, Sensorwert (mit Format wie `{value/1000:0.0} GHz`), Balken, Verlauf, Uhr/Datum, 8×8-Symbol
- **Schriften:** eingebaute 5×7-Pixelschrift in 4 Größen oder jede Windows-Schrift (ohne Kantenglättung gerendert)
- **Warnwerte:** Ein Baustein wird invertiert oder blinkt, wenn ein Grenzwert überschritten ist. Das Applet kann dann sofort nach vorne springen.
- **Auto-Sensoren** wie „CPU-Temperatur“, die auf Intel, AMD und NVIDIA ohne Anpassung funktionieren
- Helligkeit und Kontrast (5 Stufen), Symbol im Infobereich, Autostart ohne UAC-Abfrage
- Läuft **EVGA Unleash**, gibt die Engine das Display automatisch frei

## Installation

1. Unter [Releases](../../releases) bzw. in den Actions-Artefakten `Z10AppletEngine-win-x64.zip` herunterladen und entpacken.
2. **EVGA Unleash beenden** (auch `hid.exe` im Task-Manager), sonst teilen sich zwei Programme das Display.
3. `Z10AppletEngine.exe` starten. Das Programm fragt nach Adminrechten, weil LibreHardwareMonitor sie für CPU-Temperaturen und Lüfter braucht.
4. Optional unter *Gerät & Sensoren* „Mit Windows starten“ aktivieren. Dabei wird eine Aufgabe in der Aufgabenplanung angelegt, sodass beim Anmelden keine UAC-Abfrage kommt.

Das Schließen-Kreuz minimiert die App in den Infobereich. Beenden geht über das Kontextmenü des Symbols.

### Hinweis zu Virenscannern

LibreHardwareMonitor nutzt einen Kernel-Treiber, um Sensoren auszulesen. Je nach Version der Bibliothek (älter: WinRing0, neuer: PawnIO) kann Windows Defender warnen oder ein zusätzlicher Treiber nötig sein. Fehlen Sensoren, zeigt die Seite *Gerät & Sensoren* den Fehler an.

## Daten

Applets und Einstellungen liegen in `%APPDATA%\Z10AppletEngine\`:

- `applets\*.json`: ein Applet pro Datei, von Hand bearbeitbar und teilbar
- `settings.json`: Helligkeit, Kontrast, Seiten-Pause usw.

Beim ersten Start werden Vorlagen angelegt: CPU, GPU, Uhr & Datum, Arbeitsspeicher und CPU-Verlauf (dieses ist zunächst deaktiviert).

### Sensoren

Ein Sensor ist entweder ein LibreHardwareMonitor-Identifier wie `/amdcpu/0/temperature/2` oder eine Auto-Abfrage:

```
auto:<Art>/<Sensortyp>/<Name 1>||<Name 2>||*
auto:cpu/Temperature/CPU Package||Core (Tctl/Tdie)||*
```

Art: `cpu`, `gpu`, `memory`, `motherboard`, `network`, `storage`. Die Namen werden der Reihe nach probiert, `*` nimmt den ersten Sensor des Typs.

### Formate

| Vorlage | Wert | Ergebnis |
|---|---|---|
| `{value:0}°C` | 54,4 | `54°C` |
| `{value/1000:0.0} GHz` | 4210 | `4,2 GHz` |
| `Last {value:0}%` | 23,2 | `Last 23%` |

Fehlt der Sensor, wird `--` angezeigt. Uhr/Datum nutzt .NET-Datumsformate (`HH:mm:ss`, `dddd, d. MMMM yyyy`).

## Entwicklung

```
src/Z10.Core          Protokoll, HID-Zugriff, Rendering, Datenmodell, Engine (net8.0, keine Abhängigkeiten)
src/Z10.App           WPF-Oberfläche, LibreHardwareMonitor-Anbindung (net8.0-windows)
tests/Z10.Core.Tests  Tests ohne externe Pakete: dotnet run --project tests/Z10.Core.Tests
```

`dotnet run --project tests/Z10.Core.Tests -- --dump <Ordner>` rendert alle Vorlagen als PBM-Bilder, ohne dass die Tastatur angeschlossen sein muss.

GitHub Actions baut bei jedem Push eine selbstständige EXE (win-x64, keine .NET-Installation nötig). Ein Tag `v*` erzeugt ein Release.

### Noch offen

- Applets mit den Tasten E6/E7 wechseln (Tastenereignisse noch nicht erforscht)
