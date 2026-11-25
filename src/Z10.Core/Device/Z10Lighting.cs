namespace Z10.Core.Device;

/// <summary>Beleuchtungseffekte, wie EVGA Unleash sie anbietet.</summary>
public enum LightingEffect
{
    /// <summary>Helligkeit 0.</summary>
    Off,
    /// <summary>Alle Tasten leuchten gleichmäßig.</summary>
    Static,
    /// <summary>Alle Tasten pulsieren.</summary>
    Breathing,
    /// <summary>Nur die ausgewählten Tasten leuchten (Maske).</summary>
    Customize,
    /// <summary>Gedrückte Taste leuchtet auf.</summary>
    Trigger,
    /// <summary>Licht breitet sich von der gedrückten Taste aus.</summary>
    Explosion,
}

/// <summary>Gewünschte Beleuchtung für das aktive Profil.</summary>
public sealed class LightingConfig
{
    public LightingEffect Effect { get; set; } = LightingEffect.Static;

    /// <summary>Helligkeit 1..10 (bei <see cref="LightingEffect.Off"/> wird 0 gesendet).</summary>
    public int Brightness { get; set; } = 5;

    /// <summary>Tastenpositionen (0..127) für <see cref="LightingEffect.Customize"/>.</summary>
    public List<int> Keys { get; set; } = new();

    public LightingConfig Clone() => new() { Effect = Effect, Brightness = Brightness, Keys = Keys.ToList() };
}

/// <summary>Was aus der Tastatur gelesen wurde.</summary>
public sealed class LightingState
{
    /// <summary>Aktives Profil 1..5.</summary>
    public int Profile { get; init; }

    /// <summary>Profilblöcke 1..5 (Index 0..4), jeweils 256 Byte ab Report-Byte 8.</summary>
    public byte[][] Blocks { get; init; } = Array.Empty<byte[]>();

    /// <summary>Tastenmaske des aktiven Profils (256 Byte, 01 = an).</summary>
    public byte[] Mask { get; init; } = Array.Empty<byte>();

    public byte[] ActiveBlock => Blocks[Profile - 1];
    public int Mode => ActiveBlock[Z10Lighting.BlockMode] & 0x7F;
    public int Brightness => ActiveBlock[Z10Lighting.BlockBrightness];
    public bool Breathing => ActiveBlock[Z10Lighting.BlockBreathing] != 0;

    /// <summary>Übersetzt den gelesenen Zustand in die Einstellungen der Oberfläche.</summary>
    public LightingConfig ToConfig()
    {
        var keys = Enumerable.Range(0, Z10Lighting.Positions).Where(i => Mask[i] != 0).ToList();
        var effect = Brightness == 0 ? LightingEffect.Off : Mode switch
        {
            Z10Lighting.ModeCustomize => LightingEffect.Customize,
            Z10Lighting.ModeTrigger => LightingEffect.Trigger,
            Z10Lighting.ModeExplosion => LightingEffect.Explosion,
            _ => Breathing ? LightingEffect.Breathing : LightingEffect.Static,
        };
        return new LightingConfig { Effect = effect, Brightness = Brightness == 0 ? 5 : Brightness, Keys = keys };
    }
}

/// <summary>
/// Tastenbeleuchtung der Z10 (Nicht-RGB). Abgeleitet aus USBPcap-Mitschnitten von EVGA Unleash.
///
/// Alle Befehle: Feature-Report 7 (264 Byte), Nutzdaten ab Byte 8. Unleash schreibt in dieser Reihenfolge:
///   81        Status lesen (GET)  → 07 81 &lt;aktives Profil&gt; 00 00 01
///   01 P      Profil P öffnen
///   10 P      Tastenmaske, 1 Byte pro Position (01 = an)
///   82 n/02 n Profilblöcke 1..5 lesen (GET) und zurückschreiben
///   81 …      Status pollen, bis die Tastatur fertig ist
///   03 P      Tastenbelegung Position → HID-Code (immer dieselbe Tabelle)
///   12 P      speichern (Flash, dauert ~3 s)
///   90 P      Maske zurücklesen (GET)
/// Block: [3]=0x30? [4]=0x80|Modus [5]=Helligkeit 0..10 [6]=Atmen [7]=0xFF?  – unbekannte Bytes bleiben erhalten.
/// Die Änderungen landen im Flash der Tastatur: nur bei Bedarf schreiben, nicht laufend.
/// </summary>
public static class Z10Lighting
{
    public const int Positions = 128;
    public const int BlockMode = 4;
    public const int BlockBrightness = 5;
    public const int BlockBreathing = 6;
    public const int ModeAll = 0, ModeCustomize = 1, ModeTrigger = 2, ModeExplosion = 3;
    public const int MaxBrightness = 10;

    public const byte CmdOpen = 0x01, CmdWriteBlock = 0x02, CmdKeyTable = 0x03, CmdMask = 0x10, CmdSave = 0x12,
        CmdStatus = 0x81, CmdReadBlock = 0x82, CmdReadMask = 0x90;

    /// <summary>Tastenbelegung, wie Unleash sie mit Befehl 03 sendet: Position → HID-Usage. 0xD8 = E-Taste, 0xAD = Fn.</summary>
    public static readonly byte[] KeyTable = Convert.FromHexString(
        "48a5a7a8e4a6e03e142b04291d91351e1a3916641b8a3a1f083c073d06903b201517090a19052221181c0d0b101123240c300e3f36872e25" +
        "12400f8937654126132f333432382d2747d8d8e2d8e6d846d82a3144284542435f5c592c53514cbc605d5a62544f49bd615e5b6355564b4e" +
        "57855852bf504a4dade1e58be7d888d8000000000000000000000000000000c2000000000000a9c300000000000000c4000000000000c6c5" +
        "0000000000000000000000000000c0000000000000000000000000000000c100000000d600d600000000000000af0000000000000000000000" +
        "00000000be0000000000000000000000000000d7000000ad000000e7000000");

    /// <summary>Pausen; in Tests auf 0 setzen.</summary>
    public static TimeSpan CommandDelay { get; set; } = TimeSpan.FromMilliseconds(150);
    public static TimeSpan SaveDelay { get; set; } = TimeSpan.FromSeconds(3);
    public static int StatusPolls { get; set; } = 9;

    public static LightingState Read(IZ10Device dev)
    {
        var status = Query(dev, CmdStatus, 0);
        int profile = status[2] is >= 1 and <= 5 ? status[2] : 1;
        var blocks = new byte[5][];
        for (int n = 1; n <= 5; n++) blocks[n - 1] = ReadBlock(dev, n);
        var mask = Query(dev, CmdReadMask, (byte)profile)[Z10Protocol.PayloadOffset..(Z10Protocol.PayloadOffset + 256)];
        return new LightingState { Profile = profile, Blocks = blocks, Mask = mask };
    }

    /// <summary>
    /// Schreibt die Beleuchtung ins aktive Profil – Sequenz wie Unleash, unbekannte Bytes werden aus der Tastatur
    /// übernommen. Gibt den danach gelesenen Zustand zurück. Dauert ca. 10–15 s.
    /// </summary>
    public static LightingState Write(IZ10Device dev, LightingConfig config, IProgress<string>? progress = null)
    {
        progress?.Report("Lese Profil …");
        var status = Query(dev, CmdStatus, 0);
        byte profile = (byte)(status[2] is >= 1 and <= 5 ? status[2] : 1);
        var mask = BuildMask(config);
        int brightness = config.Effect == LightingEffect.Off ? 0 : Math.Clamp(config.Brightness, 1, MaxBrightness);
        int mode = config.Effect switch
        {
            LightingEffect.Customize => ModeCustomize,
            LightingEffect.Trigger => ModeTrigger,
            LightingEffect.Explosion => ModeExplosion,
            _ => ModeAll,
        };

        progress?.Report($"Schreibe Profil {profile} …");
        Send(dev, CmdOpen, profile);
        Send(dev, CmdMask, profile, mask);
        for (int n = 1; n <= 5; n++)
        {
            var block = ReadBlock(dev, n);
            block[BlockBrightness] = (byte)brightness;          // Unleash setzt die Helligkeit in allen Blöcken
            if (n == profile)
            {
                block[BlockMode] = (byte)(0x80 | mode);
                block[BlockBreathing] = (byte)(config.Effect == LightingEffect.Breathing ? 1 : 0);
            }
            Send(dev, CmdWriteBlock, (byte)n, block);
        }

        progress?.Report("Warte auf die Tastatur …");
        for (int i = 0; i < StatusPolls; i++) { Query(dev, CmdStatus, 0); Sleep(CommandDelay * 3); }

        progress?.Report("Speichere …");
        Send(dev, CmdKeyTable, profile, KeyTable);
        Send(dev, CmdSave, profile);
        Sleep(SaveDelay);

        progress?.Report("Prüfe …");
        var state = Read(dev);
        if (!state.Mask.AsSpan(0, Positions).SequenceEqual(mask.AsSpan(0, Positions)))
            throw new IOException("Die Tastatur hat die Tastenauswahl nicht übernommen.");
        return state;
    }

    public static byte[] BuildMask(LightingConfig config)
    {
        var mask = new byte[256];
        foreach (var p in config.Keys)
            if (p is >= 0 and < Positions) mask[p] = 1;
        return mask;
    }

    private static byte[] ReadBlock(IZ10Device dev, int n)
    {
        var r = Query(dev, CmdReadBlock, (byte)n);
        if (r[2] != n) throw new IOException($"Unerwartete Antwort auf Block {n}.");
        return r[Z10Protocol.PayloadOffset..(Z10Protocol.PayloadOffset + 256)];
    }

    private static byte[] Query(IZ10Device dev, byte cmd, byte arg)
    {
        Send(dev, cmd, arg);
        var r = dev.GetFeature();
        if (r.Length < Z10Protocol.ReportLength || r[1] != cmd)
            throw new IOException($"Unerwartete Antwort auf Befehl 0x{cmd:X2}.");
        return r;
    }

    private static void Send(IZ10Device dev, byte cmd, byte arg, ReadOnlySpan<byte> payload = default)
    {
        var buf = new byte[Z10Protocol.ReportLength];
        buf[0] = Z10Protocol.ReportId;
        buf[1] = cmd;
        buf[2] = arg;
        payload.CopyTo(buf.AsSpan(Z10Protocol.PayloadOffset));
        dev.SendFeature(buf);
        Sleep(CommandDelay);
    }

    private static void Sleep(TimeSpan t)
    {
        if (t > TimeSpan.Zero) Thread.Sleep(t);
    }
}
