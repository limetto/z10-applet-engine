using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;

namespace Z10.Core.Sensors;

/// <summary>Liest benannten Shared Memory anderer Programme (RTSS, MSI Afterburner).</summary>
[SupportedOSPlatform("windows")]
internal static class SharedMemory
{
    /// <summary>Inhalt des Speichers oder null, wenn er nicht existiert (Programm läuft nicht).</summary>
    public static byte[]? TryRead(string name, int maxBytes = 8 * 1024 * 1024)
    {
        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
            using var view = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
            var buffer = new byte[Math.Min(view.Length, maxBytes)];
            int read = 0;
            while (read < buffer.Length)
            {
                int n = view.Read(buffer, read, buffer.Length - read);
                if (n <= 0) break;
                read += n;
            }
            return read == buffer.Length ? buffer : buffer[..read];
        }
        catch (Exception e) when (e is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
