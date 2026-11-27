<p align="center"><img src="docs/logo.png" width="128" alt="Z10 Applet Engine logo"></p>

# Z10 Applet Engine

*English · [Deutsch](README.de.md)*

<p align="center"><img src="docs/boot.gif" width="640" alt="Startup animation on the Z10 display"></p>

Custom applets for the 160 × 32 pixel LCD of the **EVGA Z10** keyboard (the original non-RGB model, USB `3842:2601`).
You build applets from elements in a layout editor; values come from **LibreHardwareMonitor** and other sources.
The app can also configure the keyboard's key lighting and display colour.

> Unofficial project, not affiliated with EVGA. The protocol was worked out from USB captures, see [docs/PROTOCOL.md](docs/PROTOCOL.md).

## Features

- **Applet rotation** with display time and refresh rate per applet; pause, step forward and back, optional **slide animation**
- **Layout editor** with pixel grid, mouse dragging, arrow keys and a layer list
- **Elements:** text, sensor value (with formats like `{value/1000:0.0} GHz`), bar, graph, clock/date, icon, line, rectangle, oval
- **Icons:** over 60 built-in pixel icons (8 × 8 and 16 × 16), plus **your own images and animated GIFs** (threshold, dithering, invert, size)
- **Fonts:** built-in 5×7 pixel font in 4 sizes or any Windows font (rendered without anti-aliasing)
- **Blending** per element: normal, invert (XOR – text on a bar stays readable) or cut out
- **Smoothing** per element (average or median over N seconds) for noisy sensors
- **Warnings:** an element inverts or blinks when a threshold is exceeded, and the applet can jump to the front
- **Data sources:** LibreHardwareMonitor, RTSS (frame rate, frame time), MSI Afterburner (all monitoring values), audio device volume/mute, **media playback** (title, artist, progress of Spotify, browsers, Media Player … via Windows), and **custom sensors** calculated by formula
- **Auto sensors** such as “CPU temperature” that work on Intel, AMD and NVIDIA without changes
- **Polling interval** 0.25–2 s; only hardware that an applet needs is polled that often
- **Screensaver** after a configurable idle time: logo, starfield, Matrix, campfire or aquarium
- **Key lighting:** effect (off, static, breathing, selected keys, trigger, explosion), brightness in 10 steps and per-key selection on a keyboard graphic with groups (WASD, F-keys, E-keys, numpad …), plus the **display backlight colour**; written to the keyboard's active profile like EVGA Unleash does
- **Keyboard layouts** for the lighting graphic: US (ANSI), UK, German, French (AZERTY), Nordic
- **E6/E7 keys** on the keyboard: previous/next applet by default, or pause rotation / release display
- **Languages:** English and German; more can be added with language files, no rebuild needed (template under *Options*)
- Display brightness and contrast, tray icon, autostart without UAC prompt, startup animation
- While **EVGA Unleash** is running, the engine releases the display automatically

## Installation

1. Download `Z10AppletEngine-win-x64.zip` from [Releases](../../releases) and extract it. No .NET installation is needed.
2. **Quit EVGA Unleash** (including `hid.exe` in Task Manager); otherwise two programs share the display.
3. Install the **[PawnIO](https://github.com/namazso/PawnIO.Setup/releases)** driver (see below).
4. Start `Z10AppletEngine.exe`. It asks for administrator rights because LibreHardwareMonitor needs them for CPU temperatures and fans.
5. Optionally enable “Start with Windows” under *Options*. This creates a Task Scheduler task, so there is no UAC prompt at logon.

The close button minimises the app to the tray; exit it from the tray icon's menu.

### PawnIO driver

Recent versions of LibreHardwareMonitor (the app uses 0.9.6) read CPU, motherboard and SMBus sensors through the **PawnIO** driver, which replaces the old WinRing0 driver that Windows Defender now blocks. Without PawnIO many values are missing, and the *Device & sensors* page shows a hint. GPU values (via the graphics driver), RTSS, Afterburner and audio usually work without it.

### SmartScreen

Code signing via the SignPath Foundation is in preparation (see [code signing policy](#code-signing-policy)). Until then, and for the first downloads of a new signed release, Windows may warn on first start (“Windows protected your PC”). Choose *More info → Run anyway*, or unblock the ZIP before extracting it (right-click → *Properties* → *Unblock*).

## Data

Applets and settings are stored in `%APPDATA%\Z10AppletEngine\`:

- `applets\*.json` – one applet per file; can be edited by hand and shared
- `settings.json` – brightness, contrast, page delay etc.
- `images\` – imported graphics
- `lang\*.json` – additional languages

On first start, templates are created: CPU, GPU, clock & date, memory, and CPU history (disabled at first).

### Sensors

A sensor is either a LibreHardwareMonitor identifier such as `/amdcpu/0/temperature/2` or an auto query:

```
auto:<kind>/<sensor type>/<name 1>||<name 2>||*
auto:cpu/Temperature/CPU Package||Core (Tctl/Tdie)||*
```

Kind: `cpu`, `gpu`, `memory`, `motherboard`, `network`, `storage`. Names are tried in order; `*` takes the first sensor of that type.

### Custom sensors

Under *Device & sensors → Custom sensors* you create sensors calculated from others. In the layout editor they are called `custom:<short name>`.

```
max([auto:cpu/Temperature/*], [auto:gpu/Temperature/*])          hottest chip
avg([/lpc/nct6798d/fan/0], [/lpc/nct6798d/fan/1])                 average fan speed
[auto:memory/Data/Memory Used] / ([auto:memory/Data/Memory Used] + [auto:memory/Data/Memory Available]) * 100
```

Allowed: `+ - * / ^`, parentheses, `min`, `max`, `avg`, `sum` (these skip missing values), `abs`, `round(x, digits)` and `clamp(x, min, max)`. Use a decimal point.

### RTSS (RivaTuner Statistics Server)

While RTSS is running, `/rtss/fps` (frame rate) and `/rtss/frametime` (ms) of the active game are available. RTSS is a separate program (tray icon) installed with MSI Afterburner; Afterburner alone does not provide FPS.

### MSI Afterburner

While Afterburner is running, all values from its hardware monitoring appear as `/afterburner/…`, e.g. `/afterburner/gpu0/gpu-temperature`.

### Volume

Via the Windows Core Audio API:

| Sensor | Value |
|---|---|
| `/audio/default/volume` | volume of the default playback device in % |
| `/audio/default/mute` | default playback device muted: 1, otherwise 0 |
| `/audio/default-mic/volume`, `/audio/default-mic/mute` | default microphone |
| `/audio/out/<device>/volume`, `/audio/in/<device>/volume` | every active device |

To show text depending on mute state, use the format `{value:MUTED;;on}`: 1 shows “MUTED”, 0 shows “on”.

### Media playback

Via the Windows System Media Transport Controls (the same data as the volume overlay): Spotify, browsers (YouTube …), Media Player, VLC and other apps that report to Windows. If several are open, the one currently playing wins. The **Media** element shows title, artist, artist – title, album, app or the time (position, duration, remaining) and scrolls long texts. The built-in *Now playing* applet only appears in the rotation while something is playing (applet option *Only show while media is playing*).

| Id | Value |
|---|---|
| `/media/progress` | progress in % (for a bar) |
| `/media/position`, `/media/duration`, `/media/remaining` | seconds |
| `/media/playing` | 1 while playing, otherwise 0 |

The icon `media-status` switches between play, pause and stop.

### Formats

| Template | Value | Result |
|---|---|---|
| `{value:0}°C` | 54.4 | `54°C` |
| `{value/1000:0.0} GHz` | 4210 | `4.2 GHz` |
| `Load {value:0}%` | 23.2 | `Load 23%` |

If the sensor is missing, `--` is shown. Clock/date uses .NET date formats (`HH:mm:ss`, `dddd, MMMM d, yyyy`).

### Translating

*Options → Template for a new language …* writes `_template.json` with every text of the app (German source → English) to the language folder. Translate the values, save the file as e.g. `fr.json`, set `"@name"` to the language's name and restart – the language appears in the selection. Pull requests with new languages are welcome.

## Development

```
src/Z10.Core          protocol, HID access, rendering, data model, engine, localisation (net8.0, no dependencies)
src/Z10.App           WPF UI, LibreHardwareMonitor integration (net8.0-windows)
tests/Z10.Core.Tests  tests without external packages: dotnet run --project tests/Z10.Core.Tests
tools/                Python scripts used while reverse engineering (LCD and lighting tests)
```

`dotnet run --project tests/Z10.Core.Tests -- --dump <folder>` renders all templates as PBM images without a keyboard attached.

GitHub Actions builds a self-contained EXE (win-x64) on every push. A release is created via *Actions → Release → Run workflow* (enter a version) or by pushing a tag `v*`.

UI texts are written in German in the source; `src/Z10.Core/Localization/en.json` maps them to English. A test fails the build if a visible text has no English translation.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org). Details, team roles and privacy policy: [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md).

Privacy: this program does not transfer any information to other networked systems unless specifically requested by the user.

## License

MIT, see [LICENSE](LICENSE). Third-party libraries and their licenses: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

This project is not affiliated with or endorsed by EVGA. “EVGA” and “Z10” are trademarks of their respective owners. The protocol was derived from USB captures and analysis of EVGA Unleash for interoperability with your own keyboard; the repository contains no code or files from EVGA. Use at your own risk – writing lighting profiles in particular changes the keyboard's memory.
