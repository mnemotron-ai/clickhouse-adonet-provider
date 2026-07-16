# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versioning: [SemVer 2.0](https://semver.org/).

## [Unreleased]

### Added
- Fork of ClickHouse.Client 7.14.0 as `Mnemotron.Data.ClickHouse`
  (net462/net48/net8.0, strong-named).
- Conformance suite: query corpus, golden outputs captured from a pinned
  ClickHouse 25.3 (HTTP `TSVWithNamesAndTypes`), replay through the provider,
  structural comparator with a written tolerance policy.
- `GetDataTypeName` returns the verbatim server type string.
- `Setup.exe` / `Uninstall.exe` (`tools/Installer/`, net48): a from-scratch
  C# port of `deploy/register-provider.ps1` and `deploy/install-cartridge.ps1`
  so installing/uninstalling the provider no longer requires PowerShell.
  Same behavior: GAC install of the provider + dependency closure,
  `DbProviderFactories` registration in both machine.config branches, and
  cartridge deployment to the discovered SSAS server / SSDT folders.
- Tag-driven release pipeline (`.github/workflows/release.yml`): pushing a
  `v*` tag publishes the provider and installer, assembles a Windows
  installer zip (`Setup.exe`, `Uninstall.exe`, provider build, cartridge,
  `deploy/*.ps1`, `INSTALL.md`, `SHA256SUMS`), and creates a GitHub release.
- `Mnemotron.Data.ClickHouse.csproj`: pinned `<AssemblyVersion>1.0.0.0</AssemblyVersion>`
  so the GAC/machine.config identity stays fixed while the package version
  floats per release tag.
