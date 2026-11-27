# EVGA Z10 (non-RGB) LCD protocol

*Deutsche Fassung: [PROTOCOL.de.md](PROTOCOL.de.md)*

Worked out from USB captures (USBPcap) and the behaviour of EVGA Unleash, tested on a device with firmware `0x0072`.

## Device

- USB `3842:2601`, product name "EVGA TORQ Z10 Gaming Keyboard"
- Interface 0 is the boot keyboard, interface 1 a second keyboard (NKRO), interface 2 an HID composite with several report IDs
- Display and settings use **feature report ID 7** in the collection with usage page `0xFF01`. The length is 263 bytes plus the report ID, so **264 bytes**.
- On Windows this is a separate HID collection. Find it with `HidP_GetCaps` (`UsagePage == 0xFF01`, `FeatureReportByteLength == 264`) and write with `HidD_SetFeature`.

## Display

160 × 32 pixels, monochrome, split into 4 pages of 8 pixel rows each.

### Writing a page

```
Byte  0     0x07     report ID
Byte  1     0x21     command
Byte  2     page     0..3, 0 = BOTTOM row, 3 = top
Byte  3–7   0x00
Byte  8–167          160 bytes, one byte per column x = 0..159
                     bit 7 = top pixel of the page, bit 0 = bottom
Byte 168–263 0x00
```

**Important:** The firmware buffers the pages and only puts the buffer on the display when **page 3** is written.
So always send them in the order 0, 1, 2, 3. If you skip pages that haven't changed, you still have to send page 3 last every time.

The firmware needs some time between two pages: 10 ms worked reliably in testing, 0 ms did not. Unleash even waits around 250 ms.

### Releasing the display

```
07 21 FE 00 …
```

After this the firmware shows its own screen again (clock, profile etc.).

## Brightness and contrast

```
07 20 <contrast> <brightness-1> 00 …
Contrast:   level 1..5 → 0x00, 0x0C, 0x18, 0x24, 0x30
Brightness: level 1..5 → 0..4
```

Takes effect immediately and is not stored permanently. The display colour is in the profile block (bytes 7..9, see key lighting).

## Key events (E-keys)

Arrive as **input report 9** on the collection with usage page `0xFF00` (interface 2, interrupt endpoint `0x83`):

```
09 E2 <row> <column> 03 <state> 00 00 00
Position: row × 8 + column (as in the lighting mask)
E1..E7:   73, 74, 76, 78, 80, 125, 127   (E6 = 0F/05, E7 = 0F/07)
State:    01 = pressed, 00 = released
```

The firmware reports normal keys this way too (e.g. arrow right = `0C/05`, key 7 = `05/07`). If you only look at the
column, these keys switch as well – the row has to be checked.

Releasing a key is followed by an empty report 1 (`01 00 …`). On Windows the collection can be read with `ReadFile` without a driver.
While display pages are being written, the keyboard acknowledges each page with `09 00 00 21 …`.

Other reports on the same channel:

```
09 00 01 00 <P>        profile changed to P (1..5), e.g. with Fn+1..5 on the keyboard or by command 01 P
09 00 01 02 … <v>      unconfirmed: state changes, probably game mode (G/PC key);
09 00 01 04 … <v>      the same values appear in status bytes 4 and 5 of command 81
```

## Key lighting

From USBPcap captures of EVGA Unleash. All commands are feature reports 7 (264 bytes), payload from byte 8.
"GET" means: send the command with SET_REPORT, then read report 7 with GET_REPORT (the response starts with `07 <command>`).

| Command | Direction | Meaning |
|---|---|---|
| `81` | GET | status: `07 81 <active profile> 00 00 01` |
| `01 P` | SET | open profile P; on its own it also switches the active profile (see below) |
| `10 P` + 256 bytes | SET | key mask, 1 byte per position (01 = on), only effective in "Customize" mode |
| `82 n` | GET | read profile block n (1..5) |
| `02 n` + 256 bytes | SET | write profile block n |
| `03 P` + 256 bytes | SET | key assignment position → HID code; 0..127 normal layer, 128..255 Fn layer |
| `12 P` | SET | save (flash, about 3 s) |
| `90 P` | GET | read key mask of profile P |

Unleash writes: `81`, `01 P`, `10 P`, five times `82 n` + `02 n`, ~9 × `81` (polling), `03 P`, `12 P`, `90 P`.
The change only becomes visible at the end; meanwhile Unleash shows "APPLYING DRIVER" on the LCD.

### Timing and reading reliably

Unleash (`hid.exe`) waits **50 ms after every SET** and **500 ms after every GET**. Without the long pause after
reading, the firmware hung at least once in testing: `HidD_SetFeature` then failed until the keyboard was unplugged.

Shortly after write commands, a GET sometimes returns a response for a different request – e.g. asking for block 2
with `82 02` returned `07 82 03 …` with the content of block 3, or an intermediate state. So every response has to
be checked (byte 1 = command, for `82` also byte 2 = block number). If it doesn't match, read again after about
100 ms and, if necessary, send the request again. The app tries up to 3 requests with 5 reads each.

### Switching profiles

`01 P` on its own switches the active profile (this is what the profile switch in Unleash's tray menu does, followed by
a 150 ms pause). Check the result with `81`. The keyboard sends `09 00 01 00 P` and remembers the active profile
after unplugging. On the keyboard itself, Fn+1..5 switch profiles (codes `C2`..`C6` in the Fn layer of the `03` table).

Profile block (payload, one per profile 1..5, according to `hid.exe` and verified on the device):

| Byte | Content |
|---|---|
| 1 | game mode: lock Windows key |
| 3 | 0x30 – required: without it, the key selection (Customize) has no effect in this profile. On the test device it was missing in profiles 2..5; the app sets it when writing |
| 4 | 0x80 \| mode (0 = all keys, 1 = Customize, 2 = Trigger, 3 = Explosion) |
| 5 | brightness 0..10 (0 = off; Unleash sets it in all blocks) |
| 6 | breathing (01) |
| 7..9 | display backlight colour R, G, B (green and blue are about twice as bright: white ≈ `FF 7F 7F`, yellow ≈ `FF 7F 00`; the app compensates for this) |
| 10..15 | game mode: lock Alt+F4, Alt+Tab, menu key, Tab, left Shift, Ctrl+Esc |

A single `02 n` without `12` changes the display colour immediately (this is how Unleash flashes the display red/blue) – and the
colour is still there after unplugging. So `02` ends up in non-volatile memory; it is not suitable for constantly changing
colours. Unknown bytes are written back unchanged.

Key positions: The table from `03` assigns an HID code to each position (0..127), which is how the mask was
decoded (Q = 8, W = 16, A = 10, S = 18, D = 26, Fn = 120 with code 0xAD, E-keys = 73, 74, 76, 78, 80, 125, 127 with
code 0xD8). 110 positions are lit. `<>` (19) and `#'` (68) on the German keyboard have no LED; Unleash's
"All" sets the US backslash (82) instead, which doesn't exist on the German keyboard.
Mask positions 128..255 are always 0. The layout is in `src/Z10.Core/Device/Z10KeyLayout.cs`, a test script in
`tools/z10light.py`.

## Unconfirmed

- `01 FF` appears in Unleash captures; its meaning is unknown.
- `05` uploads macros (not used by the app).
