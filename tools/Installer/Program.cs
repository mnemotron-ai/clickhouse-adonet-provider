// Setup.exe / Uninstall.exe -- installs or removes the Mnemotron ADO.NET
// Data Provider for ClickHouse on a Windows machine hosting SSAS and/or
// SSDT. Ports the LOGIC of deploy/register-provider.ps1 (GAC + machine.config)
// and deploy/install-cartridge.ps1 (SSAS MD cartridge deployment); those two
// scripts remain the field-tested behavioral spec and the scripted/CI
// alternative -- see docs/deploy-windows.md.
//
// Mode selection: uninstall when the running executable's file name contains
// "uninstall" (case-insensitive -- the release pipeline ships the same
// binary copied to Uninstall.exe), OR when the command line has /uninstall
// or --uninstall.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;

namespace Mnemotron.Data.ClickHouse.Installer;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            InstallOptions options = InstallOptions.Parse(args);
            if (options.ShowHelp)
            {
                PrintUsage();
                return 0;
            }

            bool uninstall = DetectUninstallMode(args);
            RequireAdministrator();

            string exeDir = AppDomain.CurrentDomain.BaseDirectory
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Resolve every path to absolute FIRST. .NET file APIs (and
            // AssemblyName.GetAssemblyName / Publish.GacInstall below)
            // resolve relative paths against the process's current working
            // directory, which is not necessarily the exe's own directory --
            // the same bug class documented in register-provider.ps1's
            // header comment (there: PowerShell's logical location vs. the
            // process CWD). Canonicalizing here means every later Test/Exists
            // check and every error message uses an unambiguous absolute path.
            string payloadDir = Path.GetFullPath(options.PayloadDir ?? Path.Combine(exeDir, "provider-net48"));
            string cartridgePath = Path.GetFullPath(
                options.CartridgePath ?? Path.Combine(exeDir, "cartridge", ProviderIdentity.CartridgeFileName));

            return uninstall
                ? RunUninstall(options, payloadDir, cartridgePath)
                : RunInstall(options, payloadDir, cartridgePath);
        }
        catch (InstallerException ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"ERROR: unexpected failure: {ex}");
            Console.Error.WriteLine("This is not a recognized failure mode -- please file an issue with the output above.");
            return 1;
        }
    }

    private static bool DetectUninstallMode(string[] args)
    {
        string exePath = Process.GetCurrentProcess().MainModule?.FileName;
        string exeName = !string.IsNullOrEmpty(exePath)
            ? Path.GetFileNameWithoutExtension(exePath)
            : Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);

        if (!string.IsNullOrEmpty(exeName) && exeName.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        foreach (string a in args)
        {
            if (string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void RequireAdministrator()
    {
        // Belt-and-suspenders alongside the app.manifest's
        // requestedExecutionLevel: catches launch paths that bypass shell
        // elevation prompts (e.g. some remote-execution / scheduled-task
        // tooling runs the manifest's requested level differently).
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new InstallerException(
                "Administrator privileges are required (GAC, machine.config, and the cartridge " +
                "destinations under Program Files are all machine-wide). Right-click this executable " +
                "and choose \"Run as administrator\", or launch it from an elevated command prompt.");
        }
    }

    private static int RunInstall(InstallOptions options, string payloadDir, string cartridgePath)
    {
        string assemblyPath = Path.Combine(payloadDir, ProviderIdentity.AssemblyFileName);
        if (!File.Exists(assemblyPath))
        {
            throw new InstallerException(
                $"Provider assembly not found: '{assemblyPath}'. Build it first: " +
                "dotnet publish src/Mnemotron.Data.ClickHouse -f net48 -c Release, then either copy the " +
                "publish output into a 'provider-net48' folder next to this executable or re-run with " +
                "--payload <path to the publish output>.");
        }

        AssemblyName providerName = GacManager.GetStrongName(assemblyPath);
        if (providerName == null)
        {
            throw new InstallerException(
                $"'{assemblyPath}' is not a strong-named assembly; GAC installation requires a strong name. " +
                "Rebuild src/Mnemotron.Data.ClickHouse with its signing key (SignAssembly / " +
                "AssemblyOriginatorKeyFile) intact.");
        }

        string factoryTypeFullString = $"{ProviderIdentity.FactoryTypeName}, {providerName.FullName}";
        var report = new ReportBuilder();

        // 1. GAC: provider + dependency closure.
        GacManager.InstallToGac(assemblyPath, providerName);
        report.Add($"GAC: {providerName.FullName}", "installed");
        foreach ((string Path, AssemblyName Name) dep in GacManager.GetDependencyFiles(assemblyPath))
        {
            GacManager.InstallToGac(dep.Path, dep.Name);
            report.Add($"GAC: {dep.Name.FullName}", "installed");
        }

        // 2. machine.config, both bitnesses.
        foreach (string cfg in SystemPaths.MachineConfigPaths)
        {
            report.Add(cfg, MachineConfigManager.EditMachineConfig(cfg, factoryTypeFullString, remove: false));
        }

        // 3. cartridge.
        if (!options.NoCartridge)
        {
            if (!File.Exists(cartridgePath))
            {
                throw new InstallerException(
                    $"Cartridge file not found: '{cartridgePath}'. Pass --cartridge <path to " +
                    $"{ProviderIdentity.CartridgeFileName}>, or run with --no-cartridge to skip cartridge deployment.");
            }

            foreach ((string Kind, string Dir) target in CartridgeManager.ResolveTargets(options))
            {
                string dest = Path.Combine(target.Dir, ProviderIdentity.CartridgeFileName);
                report.Add($"[{target.Kind}] {dest}", CartridgeManager.DeployCartridge(cartridgePath, target.Dir));
            }
        }

        report.Print("Installation status");

        Console.WriteLine();
        Console.WriteLine("Verification:");
        Console.WriteLine($"  GAC contains provider: {(GacManager.IsInGac(providerName) ? "PRESENT" : "ABSENT")}");
        foreach (string cfg in SystemPaths.MachineConfigPaths)
        {
            if (File.Exists(cfg))
            {
                Console.WriteLine($"  {cfg}: {(MachineConfigManager.IsRegistered(cfg) ? "REGISTERED" : "not registered")}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            "Next step: verify with Windows PowerShell 5.1 -- " +
            "[System.Data.Common.DbProviderFactories]::GetFactory('Mnemotron.Data.ClickHouse') -- then restart " +
            "the SSAS service (and Visual Studio) so the cartridge list is re-read, and run docs/ssas-smoke-checklist.md.");

        return 0;
    }

    private static int RunUninstall(InstallOptions options, string payloadDir, string cartridgePath)
    {
        string assemblyPath = Path.Combine(payloadDir, ProviderIdentity.AssemblyFileName);
        AssemblyName providerName;

        if (File.Exists(assemblyPath))
        {
            providerName = GacManager.GetStrongName(assemblyPath);
            if (providerName == null)
            {
                throw new InstallerException($"'{assemblyPath}' is not a strong-named assembly.");
            }
        }
        else
        {
            // Uninstall with the original file gone: reconstruct identity
            // from the GAC, same as the ps1's fallback.
            string gacRoot = SystemPaths.GacMsilAssemblyRoot("Mnemotron.Data.ClickHouse");
            string candidate = Directory.Exists(gacRoot)
                ? Directory.EnumerateFiles(gacRoot, ProviderIdentity.AssemblyFileName, SearchOption.AllDirectories).FirstOrDefault()
                : null;

            if (candidate == null)
            {
                Console.Error.WriteLine(
                    "WARNING: provider assembly found neither on disk nor in the GAC; only machine.config entries will be removed.");
                providerName = null;
            }
            else
            {
                providerName = GacManager.GetStrongName(candidate);
                assemblyPath = candidate;
            }
        }

        string factoryTypeFullString = providerName != null
            ? $"{ProviderIdentity.FactoryTypeName}, {providerName.FullName}"
            : null;

        var report = new ReportBuilder();

        // 1. machine.config, both bitnesses.
        foreach (string cfg in SystemPaths.MachineConfigPaths)
        {
            report.Add(cfg, MachineConfigManager.EditMachineConfig(cfg, factoryTypeFullString, remove: true));
        }

        // 2. GAC: provider, dependencies only with --remove-dependencies.
        if (providerName != null)
        {
            var deps = File.Exists(assemblyPath)
                ? GacManager.GetDependencyFiles(assemblyPath)
                : new System.Collections.Generic.List<(string, AssemblyName)>();

            bool removed = GacManager.RemoveFromGac(providerName, assemblyPath);
            report.Add($"GAC: {providerName.FullName}", removed ? "removed from GAC" : "not in GAC / not removed");

            if (options.RemoveDependencies)
            {
                foreach ((string Path, AssemblyName Name) dep in deps)
                {
                    bool depRemoved = GacManager.RemoveFromGac(dep.Name, dep.Path);
                    report.Add($"GAC: {dep.Name.FullName}", depRemoved ? "removed from GAC" : "not in GAC / not removed");
                }
            }
            else
            {
                Console.WriteLine("Dependency assemblies left in the GAC (rerun with --remove-dependencies to remove them).");
            }
        }

        // 3. cartridge copies.
        if (!options.KeepCartridge)
        {
            foreach ((string Kind, string Dir) target in CartridgeManager.ResolveTargets(options))
            {
                string dest = Path.Combine(target.Dir, ProviderIdentity.CartridgeFileName);
                report.Add($"[{target.Kind}] {dest}", CartridgeManager.RemoveCartridge(target.Dir));
            }
        }

        report.Print("Uninstallation status");

        Console.WriteLine();
        Console.WriteLine(
            "Next step: confirm removal with Windows PowerShell 5.1 -- " +
            "[System.Data.Common.DbProviderFactories]::GetFactoryClasses() should no longer list " +
            "'Mnemotron.Data.ClickHouse' -- then restart the SSAS service (and Visual Studio).");

        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Mnemotron ADO.NET Data Provider for ClickHouse -- Setup / Uninstall");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  Setup.exe [options]              Install: GAC + machine.config + cartridge");
        Console.WriteLine("  Uninstall.exe [options]          Remove the above");
        Console.WriteLine("  Setup.exe --uninstall [options]  Same as Uninstall.exe (the file name also selects the mode)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --payload <dir>             Provider publish folder (default: provider-net48 next to this exe)");
        Console.WriteLine("  --cartridge <path>          Path to clickhouse.xsl (default: cartridge\\clickhouse.xsl next to this exe)");
        Console.WriteLine("  --no-cartridge               Install: skip cartridge deployment");
        Console.WriteLine("  --keep-cartridge             Uninstall: skip cartridge removal");
        Console.WriteLine("  --remove-dependencies        Uninstall: also GAC-remove dependency assemblies");
        Console.WriteLine("  --server-only                Cartridge: target SSAS server instances only");
        Console.WriteLine("  --design-time-only           Cartridge: target design-time (SSDT/VS) folders only");
        Console.WriteLine("  --ssas-cartridge-dir <dir>   Explicit server cartridge folder (repeatable); skips auto-discovery");
        Console.WriteLine("  --ssdt-cartridge-dir <dir>   Explicit design-time cartridge folder (repeatable); skips auto-discovery");
        Console.WriteLine("  --uninstall                  Force uninstall mode regardless of the exe file name");
        Console.WriteLine("  --help                        Show this message");
        Console.WriteLine();
        Console.WriteLine("Must run elevated (Administrator). See docs/deploy-windows.md.");
    }
}
