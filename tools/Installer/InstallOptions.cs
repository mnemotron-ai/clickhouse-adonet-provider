using System.Collections.Generic;

namespace Mnemotron.Data.ClickHouse.Installer;

// Parsed command-line options, shared by the install and uninstall paths.
// Path-valued options are stored as given by the user; Program.cs resolves
// them to absolute paths immediately after parsing (see its comment on why).
internal sealed class InstallOptions
{
    public string PayloadDir { get; private set; }

    public string CartridgePath { get; private set; }

    public bool NoCartridge { get; private set; }

    public bool KeepCartridge { get; private set; }

    public bool RemoveDependencies { get; private set; }

    public bool ServerOnly { get; private set; }

    public bool DesignTimeOnly { get; private set; }

    public List<string> SsasCartridgeDirs { get; } = new();

    public List<string> SsdtCartridgeDirs { get; } = new();

    public bool ShowHelp { get; private set; }

    public static InstallOptions Parse(string[] args)
    {
        var o = new InstallOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--uninstall":
                case "/uninstall":
                    // Mode is decided in Program.DetectUninstallMode before
                    // parsing; accept the flag here too so it is not reported
                    // as unrecognized.
                    break;
                case "--payload":
                    o.PayloadDir = RequireValue(args, ref i, a);
                    break;
                case "--cartridge":
                    o.CartridgePath = RequireValue(args, ref i, a);
                    break;
                case "--no-cartridge":
                    o.NoCartridge = true;
                    break;
                case "--keep-cartridge":
                    o.KeepCartridge = true;
                    break;
                case "--remove-dependencies":
                    o.RemoveDependencies = true;
                    break;
                case "--server-only":
                    o.ServerOnly = true;
                    break;
                case "--design-time-only":
                    o.DesignTimeOnly = true;
                    break;
                case "--ssas-cartridge-dir":
                    o.SsasCartridgeDirs.Add(RequireValue(args, ref i, a));
                    break;
                case "--ssdt-cartridge-dir":
                    o.SsdtCartridgeDirs.Add(RequireValue(args, ref i, a));
                    break;
                case "--help":
                case "-h":
                case "/?":
                    o.ShowHelp = true;
                    break;
                default:
                    throw new InstallerException($"Unrecognized argument '{a}'. Run with --help for usage.");
            }
        }

        if (o.ServerOnly && o.DesignTimeOnly)
        {
            throw new InstallerException("--server-only and --design-time-only are mutually exclusive.");
        }

        return o;
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
        {
            throw new InstallerException($"'{flag}' requires a value.");
        }

        i++;
        return args[i];
    }
}
