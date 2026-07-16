# Installing the Mnemotron ADO.NET Data Provider for ClickHouse

This is the release zip: `Setup.exe`, `Uninstall.exe`, the provider build
(`provider-net48\`), the SSAS cartridge (`cartridge\clickhouse.xsl`), and the
PowerShell scripts (`deploy\`) that do the same work outside this installer.

**Unsigned binaries**: Windows SmartScreen will warn on first run ("Windows
protected your PC"). Click "More info" -> "Run anyway". Code signing is a
tracked work item; see `docs/deploy-windows.md` in the source repository.

## Quick path

1. Copy this whole folder to the target machine (the SSAS server and/or the
   SSDT/Visual Studio machine).
2. Right-click `Setup.exe` -> "Run as administrator" (or launch it from an
   elevated command prompt). It GAC-installs the provider and its dependency
   closure from `provider-net48\`, registers it in both .NET Framework
   `machine.config` branches (32-bit and 64-bit), and deploys
   `cartridge\clickhouse.xsl` to the discovered SSAS server and SSDT
   design-time folders.
3. Verify from an elevated **Windows PowerShell 5.1** prompt (not `pwsh`):

   ```powershell
   [System.Data.Common.DbProviderFactories]::GetFactory('Mnemotron.Data.ClickHouse')
   ```

   This should return without error.
4. Restart the SSAS service (and Visual Studio) so the new cartridge is
   picked up, then work through `docs/ssas-smoke-checklist.md` in the source
   repository.

## Uninstall

Right-click `Uninstall.exe` -> "Run as administrator" (it is the same binary
as `Setup.exe`, copied under a different name; `Setup.exe --uninstall` works
identically). Pass `--remove-dependencies` to also remove the shared
dependency assemblies from the GAC, or `--keep-cartridge` to leave the
deployed cartridge files in place. Run `Setup.exe --help` for all options.

## Scripted / CI alternative

`deploy\register-provider.ps1` and `deploy\install-cartridge.ps1` (included
in `deploy\`) do the same work from Windows PowerShell 5.1 and are the
scripted path used in automation. See `docs/deploy-windows.md` in the source
repository for full details, the GAC strategy, and known limitations.
