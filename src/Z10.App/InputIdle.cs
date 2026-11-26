using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Z10.App;

/// <summary>
/// Misst, wie lange keine Eingabe kam – für den Bildschirmschoner.
/// Tastatur: systemweiter Low-Level-Hook, der nur den Zeitpunkt merkt (keine Tasten, kein Inhalt).
/// Tastatur und Maus: GetLastInputInfo von Windows.
/// </summary>
public sealed class InputIdle : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private readonly HookProc _proc;   // Referenz halten, sonst sammelt der GC den Delegaten ein
    private IntPtr _hook;
    private long _lastKeyTicks = Environment.TickCount64;
    private readonly System.Threading.Thread _thread;
    private uint _threadId;

    public InputIdle()
    {
        _proc = Callback;
        // Eigener Thread mit eigener Nachrichtenschleife: der Hook wird nie durch die Oberfläche ausgebremst
        // (Windows entfernt Hooks, die zu langsam antworten).
        var ready = new System.Threading.ManualResetEventSlim();
        _thread = new System.Threading.Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            using (var module = Process.GetCurrentProcess().MainModule)
                _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
            ready.Set();
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) { }
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        }) { IsBackground = true, Name = "Z10 Eingabe-Leerlauf" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    /// <summary>Zeit seit dem letzten Tastendruck (oder, mit <paramref name="includeMouse"/>, seit irgendeiner Eingabe).</summary>
    public TimeSpan Idle(bool includeMouse)
    {
        long now = Environment.TickCount64;
        long last = _hook == IntPtr.Zero || includeMouse ? LastInputTicks(now) : System.Threading.Interlocked.Read(ref _lastKeyTicks);
        return TimeSpan.FromMilliseconds(Math.Max(0, now - last));
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0) System.Threading.Interlocked.Exchange(ref _lastKeyTicks, Environment.TickCount64);
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static long LastInputTicks(long now)
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return now;
        // dwTime ist ein 32-Bit-TickCount → Abstand in 32 Bit rechnen (Überlauf nach 49 Tagen)
        uint diff = unchecked((uint)Environment.TickCount - info.dwTime);
        return now - diff;
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(1));
    }

    private const uint WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x, y; }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
