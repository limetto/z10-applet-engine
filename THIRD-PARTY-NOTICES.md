# Third-party notices

The release build of Z10 Applet Engine (`Z10AppletEngine.exe`) bundles the following libraries, unmodified, from NuGet.
Their licenses apply to those parts; the source code is available at the linked projects.

| Package | Version | License | Source |
|---|---|---|---|
| LibreHardwareMonitorLib | 0.9.6 | Mozilla Public License 2.0 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| HidSharp | 2.6.4 | Apache License 2.0 | https://www.zer7.com/software/hidsharp |
| DiskInfoToolkit | 1.1.2 | see project | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD | 1.4.2 | see project | https://github.com/Blacktempel/RAMSPDToolkit |
| BlackSharp.Core | 1.0.7 | see project | https://github.com/Blacktempel/BlackSharp |
| Mono.Posix.NETStandard | 1.0.0 | MIT | https://github.com/mono/mono |
| System.Management, System.CodeDom, System.IO.Ports, System.Threading.AccessControl | 10.0.x | MIT | https://github.com/dotnet/runtime |
| .NET 8 runtime (self-contained build) | 8.0 | MIT | https://github.com/dotnet/runtime |

**Mozilla Public License 2.0 (LibreHardwareMonitorLib):** Z10 Applet Engine uses the library as an unmodified binary.
In accordance with section 3.2 of the MPL 2.0, the source code of the library is available at
https://github.com/LibreHardwareMonitor/LibreHardwareMonitor and the license text at https://mozilla.org/MPL/2.0/.

**Not bundled:** the PawnIO driver (https://github.com/namazso/PawnIO.Setup) is installed separately by the user.

The versions above are the ones used by the build at the time of writing; `dotnet list src/Z10.App package --include-transitive`
shows the current list.
