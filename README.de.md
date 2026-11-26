<p align="center"><img src="docs/logo.png" width="128" alt="Logo der Z10 Applet Engine"></p>

# Z10 Applet Engine

*[English](README.md) · Deutsch*

<p align="center"><img src="docs/boot.gif" width="640" alt="Startanimation auf dem Display der Z10"></p>

Eigene Applets für das 160 × 32-Pixel-LCD der **EVGA Z10** (die ursprüngliche Variante ohne RGB, USB `3842:2601`).
Die Applets setzt du im Layout-Editor aus Bausteinen zusammen. Die Werte kommen aus **LibreHardwareMonitor**.

> Inoffizielles Projekt, nicht von EVGA. Das Display-Protokoll wurde durch Analyse ermittelt, siehe [docs/PROTOCOL.md](docs/PROTOCOL.md).

## Funktionen

- **Applet-Rotation** mit eigener Anzeigedauer und Aktualisierung pro Applet; Rotation anhalten, vor und zurück blättern
- **Abfrageintervall** der Sensoren einstellbar (0,25–2 s); häufig abgefragt wird nur die Hardware, die gerade gebraucht wird
- **Grundformen** im Editor: Linie (mit Stärke), Rechteck (leer/gefüllt, runde Ecken) und Oval/Kreis
- **Bildschirmschoner** nach einstellbarer Zeit ohne Tastendruck: Logo, Sternenflug, Matrix, Lagerfeuer oder Aquarium
- **Verschiebeanimation** beim Applet-Wechsel (zuschaltbar, Dauer einstellbar)
- **Tastenbeleuchtung**: Effekt (Aus, Statisch, Atmen, Ausgewählte Tasten, Trigger, Explosion), Helligkeit in 10 Stufen und Auswahl einzelner Tasten auf einer Tastaturgrafik mit Gruppen (WASD, F-Tasten, E-Tasten, Ziffernblock …); dazu die **Farbe der Displaybeleuchtung** mit Live-Vorschau; wird wie bei Unleash ins aktive Profil der Tastatur geschrieben
- **Tasten E6/E7** an der Tastatur: standardmäßig voriges und nächstes Applet, belegbar mit Rotation anhalten oder Display freigeben
- **Layout-Editor** mit Pixelraster, Ziehen mit der Maus, Pfeiltasten und Ebenenliste
- **Bausteine:** Text, Sensorwert (mit Format wie `{value/1000:0.0} GHz`), Balken, Verlauf, Uhr/Datum, Symbol
- **Symbole:** über 60 eingebaute Pixel-Symbole (8 × 8 und 16 × 16) mit Vorschau in der Auswahl, dazu **eigene Grafiken und animierte GIFs** per Import (Schwellwert, Rasterung, Invertieren, Größe)
- **Schriften:** eingebaute 5×7-Pixelschrift in 4 Größen oder jede Windows-Schrift (ohne Kantenglättung gerendert)
- **Überlagerung** pro Baustein: Normal, Invertieren (XOR, z. B. Schrift auf einem Balken bleibt lesbar) oder Ausstanzen
- **Glättung** pro Baustein (Mittelwert oder Median über N Sekunden) für stark schwankende Sensoren
- **Weitere Datenquellen:** RTSS (Framerate, Frametime), MSI Afterburner (alle Überwachungswerte), Lautstärke und Stummschaltung der Audiogeräte und **eigene Sensoren** per Formel aus bestehenden Sensoren
- **Warnwerte:** Ein Baustein wird invertiert oder blinkt, wenn ein Grenzwert überschritten ist. Das Applet kann dann sofort nach vorne springen.
- **Auto-Sensoren** wie „CPU-Temperatur“, die auf Intel, AMD und NVIDIA ohne Anpassung funktionieren
- **Sprachen:** Deutsch und Englisch, weitere über Sprachdateien ohne Neubau (Vorlage unter *Optionen*)
- **Tastaturlayouts** für die Beleuchtungsgrafik: US (ANSI), UK, Deutsch, Französisch (AZERTY), Nordisch
- Helligkeit und Kontrast (5 Stufen), Symbol im Infobereich, Autostart ohne UAC-Abfrage
- Startanimation mit Logo auf der Tastatur und im Startfenster (abschaltbar)
- Läuft **EVGA Unleash**, gibt die Engine das Display automatisch frei

## Installation

1. Unter [Releases](../../releases) bzw. in den Actions-Artefakten `Z10AppletEngine-win-x64.zip` herunterladen und entpacken.
2. **EVGA Unleash beenden** (auch `hid.exe` im Task-Manager), sonst teilen sich zwei Programme das Display.
3. `Z10AppletEngine.exe` starten. Das Programm fragt nach Adminrechten, weil LibreHardwareMonitor sie für CPU-Temperaturen und Lüfter braucht.
4. Optional unter *Optionen* „Mit Windows starten“ aktivieren. Dabei wird eine Aufgabe in der Aufgabenplanung angelegt, sodass beim Anmelden keine UAC-Abfrage kommt.

Das Schließen-Kreuz minimiert die App in den Infobereich. Beenden geht über das Kontextmenü des Symbols.

### Treiber PawnIO

Neuere Versionen von LibreHardwareMonitor (die App nutzt 0.9.6) lesen CPU-, Mainboard- und SMBus-Sensoren über den Treiber **[PawnIO](https://github.com/namazso/PawnIO.Setup/releases)**. Er ersetzt den alten Treiber WinRing0, den Windows Defender inzwischen blockiert. Ist PawnIO nicht installiert, fehlen viele Werte; die Seite *Gerät & Sensoren* weist dann darauf hin. GPU-Werte (über die Grafiktreiber), RTSS, Afterburner und Lautstärke funktionieren in der Regel auch ohne.

### SmartScreen

Die EXE ist nicht signiert. Beim ersten Start warnt Windows deshalb eventuell („Der Computer wurde durch Windows geschützt“) – über *Weitere Informationen → Trotzdem ausführen* geht es weiter.

## Daten

Applets und Einstellungen liegen in `%APPDATA%\Z10AppletEngine\`:

- `applets\*.json`: ein Applet pro Datei, von Hand bearbeitbar und teilbar
- `settings.json`: Helligkeit, Kontrast, Seiten-Pause usw.
- `images\`: importierte Grafiken
- `lang\*.json`: zusätzliche Sprachen

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

Läuft RTSS, stehen `/rtss/fps` (Framerate) und `/rtss/frametime` (ms) des aktiven Spiels zur Verfügung. Abschalten kannst du das unter *Optionen*.
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

### Eigene Grafiken

Im Layout-Editor beim Symbol-Baustein auf **„Grafik importieren …“** klicken. Möglich sind PNG, JPG, BMP und GIF, auch animiert. Im Dialog stellst du ein:
- die Zielgröße (bis 160 × 32),
- den Schwellwert,
- Rasterung (für Fotos und Verläufe),
- Invertieren (für dunkle Motive auf hellem Grund).

Die Vorschau zeigt das Ergebnis in Displaygröße, Animationen laufen mit. Gespeichert wird in `%APPDATA%\Z10AppletEngine\images\`, im Applet heißt die Grafik dann `img:<Name>`. Animationen laufen auf der Tastatur mit bis zu etwa 10 Bildern pro Sekunde.

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

GitHub Actions baut bei jedem Push eine selbstständige EXE (win-x64, keine .NET-Installation nötig). Ein Release entsteht über *Actions → Release → Run workflow* (Versionsnummer eingeben) oder ein Tag `v*`.

## Lizenz

MIT, siehe [LICENSE](LICENSE). Verwendete Bibliotheken und ihre Lizenzen: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Dieses Projekt steht in keiner Verbindung zu EVGA. „EVGA“ und „Z10“ sind Marken ihrer Inhaber. Das Protokoll wurde aus USB-Mitschnitten und der Analyse von EVGA Unleash abgeleitet, um die eigene Tastatur mit eigener Software nutzen zu können; das Repository enthält keinen Code und keine Dateien von EVGA. Benutzung auf eigene Gefahr – insbesondere das Schreiben der Beleuchtungsprofile verändert den Speicher der Tastatur.

