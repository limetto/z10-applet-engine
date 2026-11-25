# LCD-Protokoll der EVGA Z10 (Nicht-RGB)

Ermittelt aus USB-Mitschnitten (USBPcap) und dem Verhalten von EVGA Unleash, am Gerät getestet mit Firmware `0x0072`.

## Gerät

- USB `3842:2601`, Produktname „EVGA TORQ Z10 Gaming Keyboard“
- Interface 0 ist die Boot-Tastatur, Interface 1 eine zweite Tastatur (NKRO), Interface 2 ein HID-Verbund mit mehreren Report-IDs
- Display und Einstellungen laufen über **Feature-Report ID 7** in der Collection mit Usage Page `0xFF01`. Die Länge ist 263 Byte plus Report-ID, also **264 Byte**.
- Unter Windows ist das eine eigene HID-Collection. Man sucht sie über `HidP_GetCaps` (`UsagePage == 0xFF01`, `FeatureReportByteLength == 264`) und schreibt mit `HidD_SetFeature`.

## Display

160 × 32 Pixel, monochrom, aufgeteilt in 4 Seiten zu je 8 Pixelzeilen.

### Seite schreiben

```
Byte  0     0x07     Report-ID
Byte  1     0x21     Befehl
Byte  2     Seite    0..3, 0 = UNTERSTE Zeile, 3 = oberste
Byte  3–7   0x00
Byte  8–167          160 Bytes, je ein Byte pro Spalte x = 0..159
                     Bit 7 = oberstes Pixel der Seite, Bit 0 = unterstes
Byte 168–263 0x00
```

**Wichtig:** Die Firmware puffert die Seiten und bringt den Puffer erst aufs Display, wenn **Seite 3** geschrieben wird.
Deshalb immer in der Reihenfolge 0, 1, 2, 3 senden. Wer Seiten überspringen will, die sich nicht geändert haben, muss Seite 3 trotzdem jedes Mal als letzte senden.

Zwischen zwei Seiten braucht die Firmware etwas Zeit: 10 ms gingen im Test sauber, 0 ms nicht. Unleash wartet sogar rund 250 ms.

### Display freigeben

```
07 21 FE 00 …
```

Danach zeigt die Firmware wieder ihre eigene Anzeige (Uhr, Profil usw.).

## Helligkeit und Kontrast

```
07 20 <Kontrast> <Helligkeit-1> 00 …
Kontrast: Stufe 1..5 → 0x00, 0x0C, 0x18, 0x24, 0x30
Helligkeit: Stufe 1..5 → 0..4
```

Wirkt sofort und wird nicht dauerhaft gespeichert. Die Farbe des Displays wird nur an der Tastatur mit FN umgeschaltet.

## Tastenereignisse (E-Tasten)

Kommen als **Input-Report 9** über die Collection mit Usage Page `0xFF00` (Interface 2, Interrupt-Endpoint `0x83`):

```
09 E2 0F <Taste> 03 <Zustand> 00 00 00
Taste:   0x05 = E6, 0x07 = E7
Zustand: 01 = gedrückt, 00 = losgelassen
```

Nach dem Loslassen folgt noch ein leerer Report 1 (`01 00 …`). Unter Windows lässt sich die Collection ohne Treiber mit `ReadFile` lesen.
Während die Display-Seiten geschrieben werden, quittiert die Tastatur jede Seite mit `09 00 00 21 …`.

## Tastenbeleuchtung

Aus USBPcap-Mitschnitten von EVGA Unleash. Alle Befehle sind Feature-Reports 7 (264 Byte), Nutzdaten ab Byte 8.
„GET“ heißt: Befehl per SET_REPORT senden, danach Report 7 per GET_REPORT lesen (Antwort beginnt mit `07 <Befehl>`).

| Befehl | Richtung | Bedeutung |
|---|---|---|
| `81` | GET | Status: `07 81 <aktives Profil> 00 00 01` |
| `01 P` | SET | Profil P öffnen |
| `10 P` + 256 Byte | SET | Tastenmaske, 1 Byte pro Position (01 = an), nur im Modus „Customize“ wirksam |
| `82 n` | GET | Profilblock n (1..5) lesen |
| `02 n` + 256 Byte | SET | Profilblock n schreiben |
| `03 P` + 256 Byte | SET | Tastenbelegung Position → HID-Code (immer dieselbe Tabelle) |
| `12 P` | SET | Speichern (Flash, ca. 3 s) |
| `90 P` | GET | Tastenmaske von Profil P lesen |

Unleash schreibt: `81`, `01 P`, `10 P`, fünfmal `82 n` + `02 n`, ~9 × `81` (Polling), `03 P`, `12 P`, `90 P`.
Die Änderung wird erst am Ende sichtbar; Unleash zeigt währenddessen „APPLYING DRIVER“ auf dem LCD.

Profilblock (Nutzdaten, einer pro Profil 1..5, laut `hid.exe` und am Gerät geprüft):

| Byte | Inhalt |
|---|---|
| 1 | Spielmodus: Windows-Taste sperren |
| 3 | 0x30, Bedeutung offen |
| 4 | 0x80 \| Modus (0 = alle Tasten, 1 = Customize, 2 = Trigger, 3 = Explosion) |
| 5 | Helligkeit 0..10 (0 = aus; Unleash setzt sie in allen Blöcken) |
| 6 | Atmen (01) |
| 7..9 | Farbe der Displaybeleuchtung R, G, B (Grün und Blau sind etwa doppelt so hell: Weiß ≈ `FF 7F 7F`, Gelb ≈ `FF 7F 00`; die App rechnet das um) |
| 10..15 | Spielmodus: Alt+F4, Alt+Tab, Menütaste, Tab, Shift links, Strg+Esc sperren |

Ein einzelnes `02 n` ohne `12` ändert die Displayfarbe sofort, aber nicht dauerhaft (so blinkt Unleash das Display
rot/blau). Unbekannte Bytes werden unverändert zurückgeschrieben.

Tastenpositionen: Die Tabelle von `03` ordnet jeder Position (0..127) einen HID-Code zu, darüber ist die Maske
entschlüsselt (Q = 8, W = 16, A = 10, S = 18, D = 26, Fn = 120 mit Code 0xAD, E-Tasten = 73, 74, 76, 78, 80, 125, 127 mit
Code 0xD8). Beleuchtet sind 110 Positionen. `<>` (19) und `#'` (68) der deutschen Tastatur haben keine LED; Unleashs
„Alle“ setzt stattdessen den US-Backslash (82), den es auf der deutschen Tastatur nicht gibt.
Positionen 128..255 sind immer 0. Das Layout steht in `src/Z10.Core/Device/Z10KeyLayout.cs`, ein Testskript in
`tools/z10light.py`.
