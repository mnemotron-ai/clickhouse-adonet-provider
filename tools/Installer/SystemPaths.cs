using System;
using System.IO;

namespace Mnemotron.Data.ClickHouse.Installer;

// Well-known machine-wide paths, ported from the $env:windir-based paths in
// deploy/register-provider.ps1.
internal static class SystemPaths
{
    public static string WindowsDirectory =>
        Environment.GetEnvironmentVariable("WINDIR")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    // Both .NET Framework 4.x branches: 64-bit (SSAS service) and 32-bit
    // (older/32-bit design-time tooling). Same two paths register-provider.ps1
    // edits.
    public static string[] MachineConfigPaths => new[]
    {
        Path.Combine(WindowsDirectory, "Microsoft.NET", "Framework64", "v4.0.30319", "Config", "machine.config"),
        Path.Combine(WindowsDirectory, "Microsoft.NET", "Framework", "v4.0.30319", "Config", "machine.config"),
    };

    // v4 GAC_MSIL root for a given (simple) assembly name, e.g.
    // %WINDIR%\Microsoft.NET\assembly\GAC_MSIL\Mnemotron.Data.ClickHouse
    public static string GacMsilAssemblyRoot(string assemblySimpleName) =>
        Path.Combine(WindowsDirectory, "Microsoft.NET", "assembly", "GAC_MSIL", assemblySimpleName);
}
