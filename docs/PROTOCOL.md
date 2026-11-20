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
