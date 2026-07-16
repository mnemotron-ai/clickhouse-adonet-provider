using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace Mnemotron.Data.ClickHouse.Installer;

// Ports the discovery and deploy/remove logic of deploy/install-cartridge.ps1:
// Get-SsasCartridgeDirs (registry + filesystem fallback), Get-SsdtCartridgeDirs
// (design-time folder search), and the copy/remove-with-hash-check loop.
internal static class CartridgeManager
{
    // 1) Registry: OLAP instance map (instance name -> instance id, e.g.
    //    MSAS16.MSSQLSERVER). The per-instance Setup value names are probed
    //    defensively, same as the ps1.
    // 2) Filesystem fallback: default install layout
    //    %ProgramFiles%\Microsoft SQL Server\MSAS<nn>.<INSTANCE>\OLAP\bin\Cartridges
    public static List<string> GetSsasCartridgeDirs()
    {
        var dirs = new List<string>();

        const string instKeyPath = @"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\OLAP";
        using (RegistryKey instKey = Registry.LocalMachine.OpenSubKey(instKeyPath))
        {
            if (instKey != null)
            {
                foreach (string valueName in instKey.GetValueNames())
                {
                    string instanceId = instKey.GetValue(valueName) as string;
                    if (string.IsNullOrEmpty(instanceId))
                    {
                        continue;
                    }

                    string setupKeyPath = $@"SOFTWARE\Microsoft\Microsoft SQL Server\{instanceId}\Setup";
                    using (RegistryKey setupKey = Registry.LocalMachine.OpenSubKey(setupKeyPath))
                    {
                        if (setupKey == null)
                        {
                            continue;
                        }

                        foreach (string valName in new[] { "SQLBinRoot", "SQLPath" })
                        {
                            string root = setupKey.GetValue(valName) as string;
                            if (string.IsNullOrEmpty(root))
                            {
                                continue;
                            }

                            foreach (string suffix in new[] { "Cartridges", Path.Combine("bin", "Cartridges") })
                            {
                                string probe = Path.Combine(root, suffix);
                                AddIfDirectory(dirs, probe);
                            }
                        }
                    }
                }
            }
        }

        foreach (string pf in ProgramFilesRoots())
        {
            string root = Path.Combine(pf, "Microsoft SQL Server");
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string inst in Directory.GetDirectories(root, "MSAS*"))
            {
                AddIfDirectory(dirs, Path.Combine(inst, "OLAP", "bin", "Cartridges"));
            }
        }

        return dirs;
    }

    // Known design-time locations (see docs/deploy-windows.md "Cartridge
    // locations" for the full list and evidence). Searches VS / SQL tools
    // install roots for 'UIRdmsCartridge' folders (legacy name) and
    // 'Cartridges' folders that already contain a stock cartridge
    // (sql2000.xsl), which marks a real cartridge folder and skips unrelated
    // directories that happen to share the name.
    public static List<string> GetSsdtCartridgeDirs()
    {
        var dirs = new List<string>();
        var roots = new List<string>();

        foreach (string pf in ProgramFilesRoots())
        {
            foreach (string sub in new[]
                     {
                         "Microsoft Visual Studio",
                         "Microsoft SQL Server",
                         "Microsoft SQL Server Management Studio 21",
                         "Microsoft Analysis Services",
                     })
            {
                string p = Path.Combine(pf, sub);
                if (Directory.Exists(p))
                {
                    roots.Add(p);
                }
            }
        }

        string localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(localAppData))
        {
            string p = Path.Combine(localAppData, "Microsoft", "VisualStudio"); // per-user VSIX installs
            if (Directory.Exists(p))
            {
                roots.Add(p);
            }
        }

        foreach (string root in roots)
        {
            foreach (string f in FindDirectoriesNamed(root, "UIRdmsCartridge"))
            {
                AddIfNew(dirs, f);
            }

            foreach (string f in FindDirectoriesNamed(root, "Cartridges"))
            {
                if (f.EndsWith(Path.Combine("OLAP", "bin", "Cartridges"), StringComparison.OrdinalIgnoreCase))
                {
                    continue; // server dirs handled separately
                }

                if (!File.Exists(Path.Combine(f, "sql2000.xsl")))
                {
                    continue;
                }

                AddIfNew(dirs, f);
            }
        }

        return dirs;
    }

    public static List<(string Kind, string Dir)> ResolveTargets(InstallOptions options)
    {
        var targets = new List<(string, string)>();

        if (!options.DesignTimeOnly)
        {
            List<string> serverDirs = options.SsasCartridgeDirs.Count > 0
                ? options.SsasCartridgeDirs
                : GetSsasCartridgeDirs();
            if (serverDirs.Count == 0)
            {
                Console.Error.WriteLine(
                    "WARNING: no SSAS server cartridge folder found (no local SSAS MD instance?). " +
                    "Use --ssas-cartridge-dir to specify one.");
            }

            foreach (string d in serverDirs)
            {
                targets.Add(("server", d));
            }
        }

        if (!options.ServerOnly)
        {
            List<string> ssdtDirs;
            if (options.SsdtCartridgeDirs.Count > 0)
            {
                ssdtDirs = options.SsdtCartridgeDirs;
            }
            else
            {
                Console.WriteLine("Searching for design-time cartridge folders (may take a moment)...");
                ssdtDirs = GetSsdtCartridgeDirs();
            }

            if (ssdtDirs.Count == 0)
            {
                Console.Error.WriteLine(
                    "WARNING: no design-time cartridge folder found. Is the \"Analysis Services Projects\" " +
                    "VS extension installed? Use --ssdt-cartridge-dir to specify the folder (see docs/deploy-windows.md).");
            }

            foreach (string d in ssdtDirs)
            {
                targets.Add(("design-time", d));
            }
        }

        return targets;
    }

    public static string DeployCartridge(string cartridgePath, string destDir)
    {
        string dest = Path.Combine(destDir, ProviderIdentity.CartridgeFileName);
        if (!Directory.Exists(destDir))
        {
            return "SKIPPED (directory does not exist)";
        }

        if (File.Exists(dest) && ComputeSha256(cartridgePath) == ComputeSha256(dest))
        {
            return "already up to date";
        }

        File.Copy(cartridgePath, dest, overwrite: true);
        return "installed";
    }

    public static string RemoveCartridge(string destDir)
    {
        string dest = Path.Combine(destDir, ProviderIdentity.CartridgeFileName);
        if (!File.Exists(dest))
        {
            return "not present";
        }

        File.Delete(dest);
        return "removed";
    }

    private static IEnumerable<string> ProgramFilesRoots()
    {
        foreach (string pf in new[]
                 {
                     Environment.GetEnvironmentVariable("ProgramFiles"),
                     Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                 })
        {
            if (!string.IsNullOrEmpty(pf))
            {
                yield return pf;
            }
        }
    }

    private static void AddIfDirectory(List<string> dirs, string probe)
    {
        if (Directory.Exists(probe))
        {
            AddIfNew(dirs, probe);
        }
    }

    private static void AddIfNew(List<string> dirs, string path)
    {
        if (!dirs.Any(d => string.Equals(d, path, StringComparison.OrdinalIgnoreCase)))
        {
            dirs.Add(path);
        }
    }

    // Recursive directory search that swallows per-directory access errors,
    // matching Get-ChildItem -Recurse -Directory -ErrorAction SilentlyContinue:
    // an unreadable subtree is skipped rather than failing the whole search.
    private static List<string> FindDirectoriesNamed(string root, string name)
    {
        var results = new List<string>();
        CollectDirectoriesRecursive(root, name, results);
        return results;
    }

    private static void CollectDirectoriesRecursive(string dir, string name, List<string> results)
    {
        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(dir);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (IOException)
        {
            return;
        }

        foreach (string sub in subDirs)
        {
            string subName = Path.GetFileName(sub.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.Equals(subName, name, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(sub);
            }

            CollectDirectoriesRecursive(sub, name, results);
        }
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        byte[] hash = sha.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", string.Empty);
    }
}
