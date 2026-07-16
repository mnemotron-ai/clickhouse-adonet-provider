using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Mnemotron.Data.ClickHouse.Installer;

// Ports the GAC-related functions of deploy/register-provider.ps1:
// Get-StrongName, Get-GacPath, Test-InGac, Install-ToGac, Remove-FromGac,
// Get-DependencyFiles. Behavior is intentionally identical, including which
// failures are hard errors vs. warnings.
internal static class GacManager
{
    // Returns the AssemblyName of a strong-named managed assembly, or null
    // when the file is a valid managed assembly but is NOT strong-named
    // (GAC-ineligible). Unreadable/missing/non-managed files raise an
    // InstallerException instead of silently masquerading as "not strong-named".
    public static AssemblyName GetStrongName(string path)
    {
        AssemblyName an;
        try
        {
            an = AssemblyName.GetAssemblyName(path);
        }
        catch (Exception ex)
        {
            throw new InstallerException($"Cannot read assembly '{path}': {ex.Message}");
        }

        byte[] token = an.GetPublicKeyToken();
        return token == null || token.Length == 0 ? null : an;
    }

    // v4 GAC layout: %WINDIR%\Microsoft.NET\assembly\GAC_MSIL\<name>\v4.0_<ver>__<token>\<name>.dll
    // (our assemblies are all AnyCPU/MSIL). Returns the expected file path;
    // does not check existence.
    public static string GetGacPath(AssemblyName name)
    {
        var token = new StringBuilder();
        foreach (byte b in name.GetPublicKeyToken())
        {
            token.Append(b.ToString("x2"));
        }

        string dir = Path.Combine(SystemPaths.GacMsilAssemblyRoot(name.Name), $"v4.0_{name.Version}__{token}");
        return Path.Combine(dir, name.Name + ".dll");
    }

    public static bool IsInGac(AssemblyName name) => File.Exists(GetGacPath(name));

    // Publish.GacInstall reports some failures only on its console output, so
    // success is verified against the GAC directory afterwards -- same
    // approach as Install-ToGac in the ps1.
    public static void InstallToGac(string path, AssemblyName name)
    {
        var publisher = new System.EnterpriseServices.Internal.Publish();
        publisher.GacInstall(path);
        if (!IsInGac(name))
        {
            throw new InstallerException(
                $"GAC install failed for '{path}' (assembly not present in GAC after GacInstall). " +
                "Re-run Setup.exe as Administrator and confirm the assembly is strong-named.");
        }
    }

    public static bool RemoveFromGac(AssemblyName name, string preferredPath)
    {
        if (!IsInGac(name))
        {
            return false;
        }

        string path = preferredPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            path = GetGacPath(name); // GacRemove accepts the GAC copy's own path.
        }

        var publisher = new System.EnterpriseServices.Internal.Publish();
        publisher.GacRemove(path);
        if (IsInGac(name))
        {
            Console.Error.WriteLine(
                $"WARNING: GacRemove did not remove '{name.FullName}' (still present). " +
                "Close any process that may still hold it open (msmdsrv.exe, devenv.exe) and re-run with --uninstall.");
            return false;
        }

        return true;
    }

    // Every strong-named managed DLL sitting next to the provider, i.e. the
    // `dotnet publish -f net48` closure. Non-strong-named files are reported
    // and skipped (they cannot go to the GAC); unreadable files are a hard
    // error (see GetStrongName).
    public static List<(string Path, AssemblyName Name)> GetDependencyFiles(string providerPath)
    {
        string dir = Path.GetDirectoryName(providerPath) ?? ".";
        string providerFile = Path.GetFileName(providerPath);
        var result = new List<(string, AssemblyName)>();

        foreach (string file in Directory.GetFiles(dir, "*.dll").OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
        {
            string fileName = Path.GetFileName(file);
            if (string.Equals(fileName, providerFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AssemblyName an = GetStrongName(file);
            if (an == null)
            {
                Console.Error.WriteLine($"WARNING: Skipping '{fileName}': not a strong-named managed assembly (GAC-ineligible).");
                continue;
            }

            result.Add((file, an));
        }

        return result;
    }
}
