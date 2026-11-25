using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Z10.Core.Device;

/// <summary>
/// Tastenereignisse der E-Tasten. Die Tastatur meldet sie als Input-Report 9 auf der Vendor-Collection
/// (Usage Page 0xFF00):  09 E2 0F &lt;Taste&gt; 03 &lt;1 = gedrückt, 0 = losgelassen&gt; 00 00 00
/// Am Gerät ermittelt: E6 = 0x05, E7 = 0x07.
/// </summary>
public static class Z10Keys
{
    public const ushort EventUsagePage = 0xFF00;
    public const byte E6 = 0x05;
    public const byte E7 = 0x07;

    public readonly record struct KeyEvent(byte Code, bool Pressed)
    {
        public string Name => KeyName(Code);
    }

    public static string KeyName(byte code) => code switch
    {
        E6 => "E6",
        E7 => "E7",
        _ => $"Taste 0x{code:X2}",
    };

    /// <summary>Wertet einen Input-Report aus (inkl. Report-ID im ersten Byte). null = kein Tastenereignis.</summary>
    public static KeyEvent? Parse(ReadOnlySpan<byte> report)
    {
        if (report.Length < 6 || report[0] != 0x09 || report[1] != 0xE2 || report[4] != 0x03) return null;
        return new KeyEvent(report[3], report[5] == 1);
    }
}

/// <summary>
/// Liest Tastenereignisse der Z10 in einem eigenen Thread. Verbindet sich bei Bedarf neu
/// (Abstecken, Ruhezustand). Keine Tastatur-Hooks, kein Treiber nötig.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Z10KeyListener : IDisposable
{
    private readonly Thread _thread;
    private volatile bool _running = true;
    private volatile SafeFileHandle? _handle;

    /// <summary>Wird im Lese-Thread ausgelöst.</summary>
    public event Action<Z10Keys.KeyEvent>? KeyEvent;

    public bool IsConnected => _handle is { IsInvalid: false, IsClosed: false };

    public Z10KeyListener()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "Z10 Tasten" };
        _thread.Start();
    }

    private void Loop()
    {
        while (_running)
        {
            var (handle, length) = Z10HidDevice.OpenCollection(Z10Keys.EventUsagePage, minInputLength: 9);
            if (handle is null)
            {
                Sleep(3000);
                continue;
            }
            _handle = handle;
            var buffer = new byte[Math.Max(9, length)];
            try
            {
                while (_running)
                {
                    if (!ReadFile(handle, buffer, buffer.Length, out int read, IntPtr.Zero) || read <= 0) break;
                    if (Z10Keys.Parse(buffer.AsSpan(0, read)) is Z10Keys.KeyEvent ev)
                    {
                        try { KeyEvent?.Invoke(ev); } catch (Exception) { }
                    }
                }
            }
            finally
            {
                _handle = null;
                handle.Dispose();
            }
            Sleep(1000);
        }
    }

    private void Sleep(int ms)
    {
        for (int i = 0; i < ms / 100 && _running; i++) Thread.Sleep(100);
    }

    public void Dispose()
    {
        _running = false;
        var h = _handle;
        if (h is not null && !h.IsClosed) CancelIoEx(h, IntPtr.Zero);   // blockierendes ReadFile beenden
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(SafeFileHandle file, byte[] buffer, int toRead, out int read, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
}
