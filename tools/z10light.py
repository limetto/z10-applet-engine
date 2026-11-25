#!/usr/bin/env python3
"""
z10light – Test der Tastenbeleuchtung der EVGA Z10 (Nicht-RGB, USB 3842:2601)

Bildet die Schreibsequenz von EVGA Unleash nach (aus den USBPcap-Mitschnitten 05–07):

  81            Status lesen          -> 07 81 <aktives Profil> 00 00 01
  01 P          Profil P öffnen
  10 P + Maske  256 Byte, 1 Byte pro Tastenposition (01 = an), nur Modus "Customize"
  82 n / 02 n   Profilblöcke 1..5 lesen und zurückschreiben
                Block: [11]=0x30? [12]=0x80|Modus [13]=Helligkeit 0..10 [14]=Atmen [15]=0xFF?
                Modus: 0 = alle Tasten, 1 = Customize (Maske), 2 = Trigger, 3 = Explosion
  81 (Polling)  warten, bis die Tastatur bereit ist
  03 P + Tabelle  Tastenbelegung Position -> HID-Code (unverändert, wie Unleash)
  12 P          speichern
  90 P          Maske zurücklesen (Kontrolle)

Befehle:
  read        nur lesen: aktives Profil, Blöcke, aktuelle Maske (schreibt nichts)
  test-iso    nur <> (Pos. 19) und #' (Pos. 68) einschalten  -> Test, ob dort LEDs sitzen
  all-de      alle Tasten inkl. <> und #' (Customize-Modus)
  keys POS…   beliebige Positionen einschalten, z. B.  keys 8 10 16 18 26
  restore     Stand von vor dem ersten Schreiben wiederherstellen (Sicherung z10light_backup.json)

Vorher EVGA Unleash UND die Z10 Applet Engine beenden.
Abhängigkeit:  pip install hidapi
"""
import json
import os
import sys
import time

try:
    import hid  # Paket "hidapi"
except ImportError:
    sys.exit("Bitte zuerst installieren:  pip install hidapi")

VID, PID = 0x3842, 0x2601
REPORT_ID = 0x07
REPORT_LEN = 264
VENDOR_USAGE_PAGE = 0xFF01
VENDOR_INTERFACE = 2
BACKUP = os.path.join(os.path.dirname(os.path.abspath(__file__)), "z10light_backup.json")

# Tastenbelegung, wie Unleash sie mit Befehl 03 sendet (Position -> HID-Usage)
KEY_TABLE = bytes.fromhex(
    "48a5a7a8e4a6e03e142b04291d91351e1a3916641b8a3a1f083c073d06903b201517090a19052221181c0d0b101123240c300e3f36872e2512400f8937654126132f333432382d2747d8d8e2d8e6d846d82a3144284542435f5c592c53514cbc605d5a62544f49bd615e5b6355564b4e57855852bf504a4dade1e58be7d888d8000000000000000000000000000000c2000000000000a9c300000000000000c4000000000000c6c50000000000000000000000000000c0000000000000000000000000000000c100000000d600d600000000000000af000000000000000000000000000000be0000000000000000000000000000d7000000ad000000e7000000")
assert len(KEY_TABLE) == 256

# "Alle an" laut Unleash (US-Layout: ohne 19 = <> und 68 = #')
UNLEASH_ALL = [0, 4, 6, 7, 8, 9, 10, 11, 12, 14, 15, 16, 17, 18, 20, 22, 23, 24, 25, 26, 27, 28, 30, 31, 32, 33, 34,
               35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 54, 55, 56, 57, 58, 60, 61, 62,
               63, 64, 65, 66, 67, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89,
               90, 91, 92, 93, 94, 96, 97, 98, 99, 100, 101, 102, 104, 105, 106, 107, 108, 109, 110, 111, 112, 114,
               115, 117, 118, 119, 120, 121, 122, 124, 125, 127]
POS_ISO_LTGT, POS_ISO_HASH = 19, 68

MODE_NAMES = {0: "alle Tasten", 1: "Customize", 2: "Trigger", 3: "Explosion"}


def key_name(pos: int) -> str:
    u = KEY_TABLE[pos]
    if u == 0xD8:
        return "E-Taste"
    if u == 0xAD:
        return "Fn"
    if 0x04 <= u <= 0x1D:
        return chr(ord("A") + u - 0x04)
    if 0x1E <= u <= 0x27:
        return str((u - 0x1D) % 10)
    if 0x3A <= u <= 0x45:
        return f"F{u - 0x39}"
    names = {0x28: "Enter", 0x29: "Esc", 0x2A: "Backspace", 0x2B: "Tab", 0x2C: "Space", 0x31: "\\ (US)",
             0x32: "#' (ISO)", 0x64: "<> (ISO)", 0x39: "CapsLock", 0x46: "Druck", 0x47: "Rollen", 0x48: "Pause",
             0x53: "NumLock", 0x65: "Menü", 0xE0: "Strg L", 0xE1: "Shift L", 0xE2: "Alt", 0xE4: "Strg R",
             0xE5: "Shift R", 0xE6: "AltGr", 0xE7: "Win R"}
    return names.get(u, f"0x{u:02X}")


class Z10Light:
    def __init__(self):
        self.dev = hid.device()
        self.dev.open_path(self._find_path())

    @staticmethod
    def _find_path() -> bytes:
        devs = hid.enumerate(VID, PID)
        if not devs:
            raise RuntimeError("EVGA Z10 (3842:2601) nicht gefunden.")
        for d in devs:
            if d.get("usage_page") == VENDOR_USAGE_PAGE:
                return d["path"]
        for d in devs:
            if d.get("interface_number") == VENDOR_INTERFACE:
                return d["path"]
        raise RuntimeError("Vendor-Interface der Z10 nicht gefunden.")

    def close(self):
        self.dev.close()

    def set(self, cmd: int, arg: int = 0, payload: bytes = b""):
        buf = bytearray(REPORT_LEN)
        buf[0], buf[1], buf[2] = REPORT_ID, cmd, arg
        buf[8:8 + len(payload)] = payload
        if self.dev.send_feature_report(bytes(buf)) < 0:
            raise IOError(f"SET {cmd:02X} {arg:02X} fehlgeschlagen")

    def get(self) -> bytes:
        r = bytes(self.dev.get_feature_report(REPORT_ID, REPORT_LEN))
        if len(r) < 16:
            raise IOError("GET fehlgeschlagen")
        return r

    def query(self, cmd: int, arg: int = 0) -> bytes:
        self.set(cmd, arg)
        time.sleep(0.05)
        return self.get()

    def status(self) -> bytes:
        return self.query(0x81)

    def read_block(self, n: int) -> bytes:
        r = self.query(0x82, n)
        if r[1] != 0x82 or r[2] != n:
            raise IOError(f"Unerwartete Antwort auf 82 {n}: {r[:16].hex(' ')}")
        return r[8:]

    def read_mask(self, profile: int) -> bytes:
        r = self.query(0x90, profile)
        if r[1] != 0x90:
            raise IOError(f"Unerwartete Antwort auf 90: {r[:16].hex(' ')}")
        return r[8:8 + 256]

    def write_profile(self, profile: int, mask: bytes, blocks: dict[int, bytes]):
        """Sequenz wie Unleash. blocks: {1..5: Payload ab Byte 8}"""
        print("Schreibe … (dauert ca. 15 s, Tastatur nicht abziehen)")
        self.set(0x01, profile); time.sleep(0.3)
        self.set(0x10, profile, mask); time.sleep(0.3)
        for n in range(1, 6):
            self.read_block(n); time.sleep(0.5)          # Unleash liest jeden Block direkt vor dem Schreiben
            self.set(0x02, n, blocks[n]); time.sleep(0.3)
        for _ in range(9):                               # Unleash pollt ~9x im Abstand von ~0,57 s
            self.status(); time.sleep(0.5)
        self.set(0x03, profile, KEY_TABLE); time.sleep(0.3)
        self.set(0x12, profile)                          # speichern
        time.sleep(3.0)
        back = self.read_mask(profile)
        ok = back[:128] == bytes(mask[:128])
        print("Kontrolle (90):", "Maske übernommen ✔" if ok else "ABWEICHUNG ✘")
        if not ok:
            print("  gelesen:", [i for i, b in enumerate(back) if b])


def describe_block(b: bytes) -> str:
    mode = b[4] & 0x7F
    return (f"{b[:8].hex(' ')}  -> Modus {mode} ({MODE_NAMES.get(mode, '?')}), "
            f"Helligkeit {b[5]}/10, Atmen {b[6]}")


def read_state(z: Z10Light):
    st = z.status()
    profile = st[2]
    blocks = {n: z.read_block(n) for n in range(1, 6)}
    mask = z.read_mask(profile)
    return profile, blocks, mask


def print_state(profile, blocks, mask):
    print(f"Aktives Profil: {profile}")
    for n in range(1, 6):
        print(f"  Block {n}{' *' if n == profile else '  '} {describe_block(blocks[n])}")
    on = [i for i in range(128) if mask[i]]
    print(f"Maske: {len(on)} Positionen an")
    if len(on) <= 20:
        print("  " + ", ".join(f"{i}={key_name(i)}" for i in on))
    for p in (POS_ISO_LTGT, POS_ISO_HASH):
        print(f"  Pos. {p} ({key_name(p)}): {'an' if mask[p] else 'aus'}")


def save_backup(profile, blocks, mask):
    if os.path.exists(BACKUP):
        print(f"Sicherung existiert schon ({BACKUP}), wird nicht überschrieben.")
        return
    with open(BACKUP, "w") as f:
        json.dump({"profile": profile, "mask": mask.hex(),
                   "blocks": {str(n): b.hex() for n, b in blocks.items()}}, f, indent=1)
    print(f"Sicherung geschrieben: {BACKUP}")


def apply_mask(z: Z10Light, positions: list[int]):
    profile, blocks, old_mask = read_state(z)
    print("Vorher:"); print_state(profile, blocks, old_mask)
    save_backup(profile, blocks, old_mask)
    blk = bytearray(blocks[profile])
    blk[4] = 0x80 | 1                                   # Modus Customize, alles andere unverändert
    if blk[5] == 0:
        print("Hinweis: Helligkeit ist 0 – es wird nichts leuchten. Setze auf 5.")
        blk[5] = 5
    blocks = dict(blocks); blocks[profile] = bytes(blk)
    mask = bytearray(256)
    for p in positions:
        if not 0 <= p < 128:
            raise ValueError(f"Position {p} außerhalb 0..127")
        mask[p] = 1
    print("Neu:", ", ".join(f"{p}={key_name(p)}" for p in positions) if len(positions) <= 20
          else f"{len(positions)} Positionen")
    z.write_profile(profile, bytes(mask), blocks)


def restore(z: Z10Light):
    if not os.path.exists(BACKUP):
        sys.exit("Keine Sicherung vorhanden.")
    with open(BACKUP) as f:
        d = json.load(f)
    profile = d["profile"]
    blocks = {int(n): bytes.fromhex(h) for n, h in d["blocks"].items()}
    z.write_profile(profile, bytes.fromhex(d["mask"]), blocks)
    print("Wiederhergestellt. (Sicherung bleibt liegen – bei Bedarf löschen.)")


def main():
    if len(sys.argv) < 2 or sys.argv[1] not in ("read", "test-iso", "all-de", "keys", "restore"):
        print(__doc__); sys.exit(1)
    cmd = sys.argv[1]
    z = Z10Light()
    try:
        if cmd == "read":
            print_state(*read_state(z))
        elif cmd == "test-iso":
            apply_mask(z, [POS_ISO_LTGT, POS_ISO_HASH])
        elif cmd == "all-de":
            apply_mask(z, sorted(set(UNLEASH_ALL) | {POS_ISO_LTGT, POS_ISO_HASH}))
        elif cmd == "keys":
            apply_mask(z, [int(a) for a in sys.argv[2:]])
        elif cmd == "restore":
            restore(z)
    finally:
        z.close()


if __name__ == "__main__":
    main()
