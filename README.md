<p align="center"><img src="docs/logo.png" width="128" alt="Logo der Z10 Applet Engine"></p>

# Z10 Applet Engine

<p align="center"><img src="docs/boot.gif" width="640" alt="Startanimation auf dem Display der Z10"></p>

Eigene Applets für das 160 × 32-Pixel-LCD der **EVGA Z10** (die ursprüngliche Variante ohne RGB, USB `3842:2601`).
Die Applets setzt du im Layout-Editor aus Bausteinen zusammen. Die Werte kommen aus **LibreHardwareMonitor**.

> Inoffizielles Projekt, nicht von EVGA. Das Display-Protokoll wurde durch Analyse ermittelt, siehe [docs/PROTOCOL.md](docs/PROTOCOL.md).

## Funktionen

- **Applet-Rotation** mit eigener Anzeigedauer und Aktualisierung pro Applet; Rotation anhalten, vor und zurück blättern
- **Layout-Editor** mit Pixelraster, Ziehen mit der Maus, Pfeiltasten und Ebenenliste
- **Bausteine:** Text, Sensorwert (mit Format wie `{value/1000:0.0} GHz`), Balken, Verlauf, Uhr/Datum, 8×8-Symbol
- **Schriften:** eingebaute 5×7-Pixelschrift in 4 Größen oder jede Windows-Schrift (ohne Kantenglättung gerendert)
- **Überlagerung** pro Baustein: Normal, Invertieren (XOR, z. B. Schrift auf einem Balken bleibt lesbar) oder Ausstanzen
- **Glättung** pro Baustein (Mittelwert oder Median über N Sekunden) für stark schwankende Sensoren
- **Weitere Datenquellen:** RTSS (Framerate, Frametime), MSI Afterburner (alle Überwachungswerte), Lautstärke und Stummschaltung der Audiogeräte und **eigene Sensoren** per Formel aus bestehenden Sensoren
- **Warnwerte:** Ein Baustein wird invertiert oder blinkt, wenn ein Grenzwert überschritten ist. Das Applet kann dann sofort nach vorne springen.
- **Auto-Sensoren** wie „CPU-Temperatur“, die auf Intel, AMD und NVIDIA ohne Anpassung funktionieren
- Helligkeit und Kontrast (5 Stufen), Symbol im Infobereich, Autostart ohne UAC-Abfrage
- Startanimation mit Logo auf der Tastatur und im Startfenster (abschaltbar)
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

### Eigene Sensoren

Unter *Gerät & Sensoren → Eigene Sensoren* legst du Sensoren an, die aus anderen berechnet werden. Im Layout-Editor heißen sie dann `custom:<Kurzname>`.

```
max([auto:cpu/Temperature/*], [auto:gpu/Temperature/*])          heißester Chip
avg([/lpc/nct6798d/fan/0], [/lpc/nct6798d/fan/1])                 mittlere Lüfterdrehzahl
[auto:memory/Data/Memory Used] / ([auto:memory/Data/Memory Used] + [auto:memory/Data/Memory Available]) * 100
```

Erlaubt sind `+ - * / ^`, Klammern, `min`, `max`, `avg`, `sum` (diese überspringen fehlende Werte), `abs`, `round(x, Stellen)` und `clamp(x, min, max)`. Als Dezimaltrenner gilt der Punkt.

### RTSS (RivaTuner Statistics Server)

Läuft RTSS, stehen `/rtss/fps` (Framerate) und `/rtss/frametime` (ms) des aktiven Spiels zur Verfügung. Abschalten kannst du das unter *Gerät & Sensoren*.
RTSS ist ein eigenes Programm (Symbol im Infobereich), das mit MSI Afterburner installiert wird. Afterburner allein liefert keine FPS. RTSS muss laufen, zum Beispiel mit „Start with Windows“ in den RTSS-Einstellungen.

### MSI Afterburner

Läuft Afterburner, erscheinen alle Werte aus seiner Hardwareüberwachung als `/afterburner/…`, zum Beispiel `/afterburner/gpu0/gpu-temperature`. Die Framerate darin ist nur gefüllt, wenn auch RTSS läuft.

### Lautstärke

Über die Windows Core Audio API gibt es folgende Sensoren:

| Sensor | Wert |
|---|---|
| `/audio/default/volume` | Lautstärke des Standard-Wiedergabegeräts in % |
| `/audio/default/mute` | Standard-Wiedergabegerät stumm: 1, sonst 0 |
| `/audio/default-mic/volume`, `/audio/default-mic/mute` | Standard-Mikrofon |
| `/audio/out/<gerät>/volume`, `/audio/in/<gerät>/volume` | jedes einzelne aktive Gerät |

Text je nach Stummschaltung zeigt das Format `{value:STUMM;;laut}`: Bei 1 erscheint „STUMM“, bei 0 „laut“.

### Glättung

Sensorwert, Balken und Verlauf können über die letzten 2–600 Sekunden glätten:
- **Mittelwert** beruhigt allgemeines Zittern.
- **Median** filtert einzelne Ausreißer heraus.

Warnwerte richten sich nach dem geglätteten Wert.

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
