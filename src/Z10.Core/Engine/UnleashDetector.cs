using System.Diagnostics;

namespace Z10.Core.Engine;

/// <summary>Erkennt, ob EVGA Unleash läuft (hid.exe bzw. EVGA.exe aus dem Unleash-Ordner).</summary>
public static class UnleashDetector
{
    private static DateTime _lastCheck;
    private static bool _lastResult;

    public static bool IsRunning()
    {
        // Prozessliste nur alle 2 s abfragen
        if (DateTime.UtcNow - _lastCheck < TimeSpan.FromSeconds(2)) return _lastResult;
        _lastCheck = DateTime.UtcNow;
        _lastResult = Check("hid") || Check("EVGA");
        return _lastResult;
    }

    private static bool Check(string processName)
    {
        foreach (var p in Process.GetProcessesByName(processName))
        {
            using (p)
            {
                try
                {
                    var path = p.MainModule?.FileName ?? "";
                    if (path.Contains("Unleash", StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (Exception)
                {
                    // Kein Zugriff auf den Pfad (ohne Adminrechte) – vorsichtshalber nicht als Konflikt werten.
                }
            }
        }
        return false;
    }
}
