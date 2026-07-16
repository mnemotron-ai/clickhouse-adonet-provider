using System.IO;
using System.Linq;
using System.Xml;

namespace Mnemotron.Data.ClickHouse.Installer;

// Ports Edit-MachineConfig from deploy/register-provider.ps1: an idempotent,
// System.Xml-based (never text-replace) edit of a machine.config's
// <system.data><DbProviderFactories> section, with a timestamped backup
// before the first modification.
internal static class MachineConfigManager
{
    public static string EditMachineConfig(string configPath, string factoryTypeFullString, bool remove)
    {
        if (!File.Exists(configPath))
        {
            return "SKIPPED (file not found -- this Framework branch is not installed)";
        }

        var xml = new XmlDocument { PreserveWhitespace = true };
        xml.Load(configPath);

        XmlElement configuration = xml.DocumentElement;
        if (configuration == null || configuration.Name != "configuration")
        {
            throw new InstallerException(
                $"Unexpected root element in '{configPath}'. The file may be corrupted -- " +
                "restore it from a '*.mnemotron-backup-*' copy next to it, or repair .NET Framework 4.8.");
        }

        // machine.config can legitimately contain more than one
        // DbProviderFactories element (historical duplicate-element issue,
        // KB2468871); scan all of them. Materialize with ToList() before
        // removal: a live XmlNodeList is lazy, and removing nodes while
        // enumerating it can skip entries.
        var existingAdds = configuration
            .SelectNodes($"system.data/DbProviderFactories/add[@invariant='{ProviderIdentity.Invariant}']")
            .Cast<XmlElement>()
            .ToList();
        var existingRemoves = configuration
            .SelectNodes($"system.data/DbProviderFactories/remove[@invariant='{ProviderIdentity.Invariant}']")
            .Cast<XmlElement>()
            .ToList();

        // Idempotency: a single, exactly-matching entry means nothing to do.
        if (!remove && existingAdds.Count == 1 && existingRemoves.Count == 0)
        {
            XmlElement e = existingAdds[0];
            if (e.GetAttribute("name") == ProviderIdentity.DisplayName &&
                e.GetAttribute("description") == ProviderIdentity.Description &&
                e.GetAttribute("type") == factoryTypeFullString)
            {
                return "already registered (no change)";
            }
        }

        bool changed = false;
        foreach (XmlElement s in existingAdds.Concat(existingRemoves))
        {
            XmlNode parent = s.ParentNode;
            XmlNode prev = s.PreviousSibling;
            if (prev != null && prev.NodeType == XmlNodeType.Whitespace)
            {
                parent.RemoveChild(prev); // avoid blank-line build-up across runs
            }

            parent.RemoveChild(s);
            changed = true;
        }

        string status = "unregistered";
        if (!remove)
        {
            // Ensure <system.data><DbProviderFactories> exists.
            var systemData = configuration.SelectSingleNode("system.data") as XmlElement;
            if (systemData == null)
            {
                systemData = xml.CreateElement("system.data");
                configuration.AppendChild(systemData);
                changed = true;
            }

            var factories = systemData.SelectSingleNode("DbProviderFactories") as XmlElement;
            if (factories == null)
            {
                factories = xml.CreateElement("DbProviderFactories");
                systemData.AppendChild(factories);
                changed = true;
            }

            XmlElement add = xml.CreateElement("add");
            add.SetAttribute("name", ProviderIdentity.DisplayName);
            add.SetAttribute("invariant", ProviderIdentity.Invariant);
            add.SetAttribute("description", ProviderIdentity.Description);
            add.SetAttribute("type", factoryTypeFullString);
            factories.AppendChild(xml.CreateWhitespace("\r\n      "));
            factories.AppendChild(add);
            factories.AppendChild(xml.CreateWhitespace("\r\n    "));
            changed = true;
            status = "registered";
        }

        if (changed)
        {
            string backup = $"{configPath}.mnemotron-backup-{System.DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(configPath, backup, overwrite: false);
            xml.Save(configPath);
            return $"{status} (backup: {Path.GetFileName(backup)})";
        }

        return "no change needed";
    }

    public static bool IsRegistered(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return false;
        }

        var xml = new XmlDocument();
        xml.Load(configPath);
        XmlNode hit = xml.SelectSingleNode(
            $"configuration/system.data/DbProviderFactories/add[@invariant='{ProviderIdentity.Invariant}']");
        return hit != null;
    }
}
