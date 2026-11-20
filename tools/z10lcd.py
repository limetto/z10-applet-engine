#!/usr/bin/env python3
"""
z10lcd – Ansteuerung des LCDs der EVGA Z10 (Nicht-RGB, USB 3842:2601)

Protokoll (abgeleitet aus EVGA Unleash hid.exe, Funktionen 0x4048a0 / 0x4047f0 / 0x409050):

  HID-Feature-Report ID 7 auf dem Vendor-Interface (Usage Page 0xFF01), 264 Byte inkl. Report-ID:
    [0]      0x07   Report-ID
    [1]      0x21   Befehl "LCD-Seite schreiben"
    [2]      Seite  0..3  (jede Seite = 8 Pixelzeilen; 0 = UNTERSTE Zeile, 3 = oberste)
             Die Firmware puffert die Seiten und zeigt sie erst beim Schreiben von Seite 3 an
             -> immer in der Reihenfolge 0,1,2,3 senden (wie Unleash), Seite 3 zuletzt.
    [3..7]   0x00
    [8..167] 160 Bytes, je ein Byte pro Spalte (x = 0..159), Bit 7 = oberstes Pixel der Seite
    [168..]  0x00

  Display freigeben (zurück zur Firmware-Anzeige, Uhr usw.):
    [0]=0x07 [1]=0x21 [2]=0xFE, Rest 0x00

Display: 160 x 32 Pixel, monochrom -> 4 Seiten x 160 Byte = 640 Byte pro Bild.

WICHTIG: EVGA Unleash (EVGA.exe / hid.exe) vorher beenden, sonst streiten sich zwei Programme ums Display.

Abhängigkeiten:  pip install hidapi pillow
"""
import argparse
import sys
import time

try:
    import hid  # Paket "hidapi"
except ImportError:
    sys.exit("Bitte zuerst installieren:  pip install hidapi pillow")

from PIL import Image, ImageDraw, ImageFont

VID, PID = 0x3842, 0x2601
WIDTH, HEIGHT = 160, 32
PAGES = HEIGHT // 8
REPORT_ID = 0x07
REPORT_LEN = 264          # 1 Byte Report-ID + 263 Byte Daten (laut HID-Report-Descriptor)
CMD_LCD = 0x21
PAGE_RELEASE = 0xFE
# Befehl 0x20 (aus hid.exe, Funktion 0x404730):  07 20 <Tabelle[a]> <b-1>
# Die Tabelle steht so in hid.exe (0x53cfe4). Welche Stufe Helligkeit und welche Kontrast ist,
# ist aus dem Dekompilat nicht 100 % sicher -> am Gerät prüfen.
CMD_LCD_CONFIG = 0x20
CONTRAST_TABLE = (0x00, 0x0C, 0x18, 0x24, 0x30)   # Stufe 1..5
LEVELS = len(CONTRAST_TABLE)
VENDOR_USAGE_PAGE = 0xFF01
VENDOR_INTERFACE = 2


class Z10LCD:
    def __init__(self, page_delay: float = 0.02):
        """page_delay: Pause nach jeder Seite. Unleash nutzt ~0,25 s; kleinere Werte bitte ausprobieren."""
        self.page_delay = page_delay
        self.dev = hid.device()
        self.dev.open_path(self._find_path())

    @staticmethod
    def _find_path() -> bytes:
        devs = hid.enumerate(VID, PID)
        if not devs:
            raise RuntimeError("EVGA Z10 (3842:2601) nicht gefunden.")
        # Windows: jede Top-Level-Collection ist ein eigenes Gerät -> die mit Usage Page 0xFF01 nehmen
        for d in devs:
            if d.get("usage_page") == VENDOR_USAGE_PAGE:
                return d["path"]
        # Linux (hidraw): ein Knoten pro Interface -> Interface 2
        for d in devs:
            if d.get("interface_number") == VENDOR_INTERFACE:
                return d["path"]
        raise RuntimeError("Vendor-Interface der Z10 nicht gefunden: "
                           + ", ".join(f"if={d.get('interface_number')} up=0x{d.get('usage_page', 0):04x}" for d in devs))

    def close(self):
        self.dev.close()

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()

    # --- Low level -----------------------------------------------------------------
    def _send(self, page: int, payload: bytes = b""):
        buf = bytearray(REPORT_LEN)
        buf[0] = REPORT_ID
        buf[1] = CMD_LCD
        buf[2] = page
        buf[8:8 + len(payload)] = payload
        n = self.dev.send_feature_report(bytes(buf))
        if n < 0:
            raise IOError(f"send_feature_report fehlgeschlagen: {self.dev.error()}")

    def write_page(self, page: int, columns: bytes):
        if not 0 <= page < PAGES:
            raise ValueError("Seite muss 0..3 sein")
        if len(columns) != WIDTH:
            raise ValueError(f"Eine Seite braucht genau {WIDTH} Bytes")
        # Die Firmware zählt die Seiten von unten nach oben (Seite 0 = unterste Zeile),
        # am Hardware-Test bestätigt. Wir arbeiten intern von oben nach unten.
        self._send(PAGES - 1 - page, columns)
        time.sleep(self.page_delay)

    def _send_raw(self, cmd: int, b2: int, b3: int = 0):
        buf = bytearray(REPORT_LEN)
        buf[0], buf[1], buf[2], buf[3] = REPORT_ID, cmd, b2 & 0xFF, b3 & 0xFF
        if self.dev.send_feature_report(bytes(buf)) < 0:
            raise IOError(f"send_feature_report fehlgeschlagen: {self.dev.error()}")
        time.sleep(0.1)                                 # wie Unleash

    def set_backlight(self, brightness: int = 5, contrast: int = 3):
        """EXPERIMENTELL. Helligkeit und Kontrast in Stufen 1..5 (werden immer zusammen gesendet).
        Wirkt nur, solange die Tastatur an ist; Unleash setzt beim Start seine eigenen Werte."""
        for name, v in (("Helligkeit", brightness), ("Kontrast", contrast)):
            if not 1 <= v <= LEVELS:
                raise ValueError(f"{name} muss 1..{LEVELS} sein")
        self._send_raw(CMD_LCD_CONFIG, CONTRAST_TABLE[contrast - 1], brightness - 1)

    def release(self):
        """Display wieder an die Tastatur-Firmware zurückgeben."""
        self._send(PAGE_RELEASE)

    # --- High level ----------------------------------------------------------------
    @staticmethod
    def image_to_pages(img: Image.Image, threshold: int = 128) -> list[bytes]:
        """PIL-Bild (beliebige Größe) -> 4 Seiten à 160 Byte. Helle Pixel = an."""
        img = img.convert("L").resize((WIDTH, HEIGHT)) if img.size != (WIDTH, HEIGHT) else img.convert("L")
        px = img.load()
        pages = []
        for p in range(PAGES):
            col = bytearray(WIDTH)
            for x in range(WIDTH):
                b = 0
                for r in range(8):
                    if px[x, p * 8 + r] >= threshold:
                        b |= 0x80 >> r          # Bit 7 = oberste Zeile der Seite
                col[x] = b
            pages.append(bytes(col))
        return pages

    def show_image(self, img: Image.Image, only_changed: list[bytes] | None = None) -> list[bytes]:
        """Zeigt ein Bild an. Gibt die Seiten zurück; mit only_changed werden unveränderte Seiten übersprungen."""
        pages = self.image_to_pages(img)
        self.show_pages(pages, only_changed)
        return pages

    def show_pages(self, pages: list[bytes], only_changed: list[bytes] | None = None):
        """Sendet die Seiten in Geräte-Reihenfolge 0,1,2,3 (= von unten nach oben).
        Die Firmware übernimmt den Puffer erst beim Schreiben der Geräteseite 3 (oberste Zeile)
        aufs Display – sie muss daher immer als letzte gesendet werden, auch wenn sie sich nicht geändert hat."""
        for i in reversed(range(PAGES)):            # intern 3,2,1,0 = Gerät 0,1,2,3
            if i == 0 or only_changed is None or only_changed[i] != pages[i]:
                self.write_page(i, pages[i])

    def show_text(self, lines: list[str], font: ImageFont.ImageFont | None = None):
        """Bis zu 4 Zeilen Text (8 px hoch), z. B. ["Hallo", "Welt"]."""
        img = new_canvas()
        d = ImageDraw.Draw(img)
        font = font or ImageFont.load_default()
        for i, line in enumerate(lines[:PAGES]):
            d.text((0, i * 8 - 1), line, fill=255, font=font)
        self.show_image(img)


def new_canvas() -> Image.Image:
    return Image.new("L", (WIDTH, HEIGHT), 0)


# --- Testbilder / Demos --------------------------------------------------------------
def test_pattern() -> Image.Image:
    """Rahmen + Diagonale + gefülltes Quadrat oben links: zeigt Ausrichtung und Bit-Reihenfolge."""
    img = new_canvas()
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, WIDTH - 1, HEIGHT - 1), outline=255)
    d.line((0, 0, WIDTH - 1, HEIGHT - 1), fill=255)
    d.rectangle((2, 2, 9, 9), fill=255)
    d.text((40, 10), "Z10 TEST", fill=255, font=ImageFont.load_default())
    return img


def bench(lcd: Z10LCD):
    """Zeigt abwechselnd zwei komplett unterschiedliche Bilder mit sinkender Seiten-Pause.
    Ein Durchgang ist sauber, wenn jedes Bild KOMPLETT steht (Rahmen ringsum, keine Reste)."""
    font = ImageFont.load_default()
    lcd.show_pages([bytes(WIDTH)] * PAGES)      # Aufwärmen
    for delay in (0.25, 0.15, 0.1, 0.05, 0.02, 0.01, 0.0):
        lcd.page_delay = delay
        print(f"Pause {delay * 1000:.0f} ms ...")
        for n in range(4):
            img = new_canvas()
            d = ImageDraw.Draw(img)
            if n % 2:                               # Bild B: invertiert
                d.rectangle((0, 0, WIDTH - 1, HEIGHT - 1), fill=255)
                d.text((40, 12), f"{delay * 1000:.0f} ms  B", fill=0, font=font)
            else:                                   # Bild A: Rahmen
                d.rectangle((0, 0, WIDTH - 1, HEIGHT - 1), outline=255)
                d.text((40, 12), f"{delay * 1000:.0f} ms  A", fill=255, font=font)
            t = time.time()
            lcd.show_image(img)
            print(f"   Bild {'AB'[n % 2]} gesendet in {(time.time() - t) * 1000:.0f} ms")
            time.sleep(1.5)
    lcd.page_delay = 0.02


def clock_loop(lcd: Z10LCD):
    try:
        big = ImageFont.truetype("DejaVuSans-Bold.ttf", 26)
    except OSError:
        try:
            big = ImageFont.truetype("arialbd.ttf", 26)
        except OSError:
            big = ImageFont.load_default()
    last = None
    while True:
        img = new_canvas()
        d = ImageDraw.Draw(img)
        d.text((WIDTH // 2, HEIGHT // 2), time.strftime("%H:%M:%S"), fill=255, font=big, anchor="mm")
        last = lcd.show_image(img, only_changed=last)
        time.sleep(max(0.0, 1 - (time.time() % 1)))


def main():
    ap = argparse.ArgumentParser(description="EVGA Z10 LCD ansteuern")
    ap.add_argument("--delay", type=float, default=0.02, help="Pause nach jeder Seite in s (Unleash: 0.25)")
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("list", help="gefundene HID-Interfaces der Z10 anzeigen")
    sub.add_parser("test", help="Testbild anzeigen")
    sub.add_parser("fill", help="alle Pixel an")
    sub.add_parser("clear", help="alle Pixel aus")
    t = sub.add_parser("text", help="bis zu 4 Zeilen Text")
    t.add_argument("lines", nargs="+")
    i = sub.add_parser("image", help="Bilddatei anzeigen (wird auf 160x32 skaliert)")
    i.add_argument("file")
    sub.add_parser("clock", help="Uhr (Strg+C beendet und gibt das Display frei)")
    sub.add_parser("bench", help="testet verschiedene Seiten-Pausen, um die kleinste zuverlässige zu finden")
    sub.add_parser("release", help="Display an die Firmware zurückgeben")
    bl = sub.add_parser("backlight", help="EXPERIMENTELL: Helligkeit/Kontrast setzen (Stufen 1..5)")
    bl.add_argument("-b", "--brightness", type=int, default=5)
    bl.add_argument("-c", "--contrast", type=int, default=3)
    raw = sub.add_parser("raw20", help="EXPERIMENTELL: Befehl 0x20 mit freien Bytes senden (zum Erforschen)")
    raw.add_argument("byte2", type=lambda s: int(s, 0))
    raw.add_argument("byte3", type=lambda s: int(s, 0))
    a = ap.parse_args()

    if a.cmd == "list":
        for d in hid.enumerate(VID, PID):
            print(f"if={d.get('interface_number')} usage_page=0x{d.get('usage_page', 0):04x} "
                  f"usage=0x{d.get('usage', 0):04x} path={d['path']!r}")
        return

    with Z10LCD(page_delay=a.delay) as lcd:
        if a.cmd == "test":
            lcd.show_image(test_pattern())
        elif a.cmd == "fill":
            lcd.show_pages([b"\xff" * WIDTH] * PAGES)
        elif a.cmd == "clear":
            lcd.show_pages([bytes(WIDTH)] * PAGES)
        elif a.cmd == "bench":
            bench(lcd)
        elif a.cmd == "text":
            lcd.show_text(a.lines)
        elif a.cmd == "image":
            lcd.show_image(Image.open(a.file))
        elif a.cmd == "clock":
            try:
                clock_loop(lcd)
            except KeyboardInterrupt:
                lcd.release()
        elif a.cmd == "backlight":
            lcd.set_backlight(a.brightness, a.contrast)
        elif a.cmd == "raw20":
            lcd._send_raw(CMD_LCD_CONFIG, a.byte2, a.byte3)
        elif a.cmd == "release":
            lcd.release()


if __name__ == "__main__":
    main()
