<#
.SYNOPSIS
    Deploys (or removes) the ClickHouse SSAS Multidimensional cartridge
    (clickhouse.xsl) to the SSAS server and/or design-time cartridge folders.

.DESCRIPTION
    SSAS Multidimensional generates relational SQL through XSL cartridges.
    Every Analysis Services engine binary reads a "Cartridges" folder next to
    itself, so several copies may be needed on one machine:

      * Server (processing):   [SSAS instance]\OLAP\bin\Cartridges\
      * SSDT design time:      the cartridge folders of the Visual Studio
                               "Analysis Services Projects" extension
                               (Common7\IDE\CommonExtensions\Microsoft\SSAS\...)
                               and/or the legacy "UIRdmsCartridge" folders of
                               older SSDT/BIDS layouts.
      * MSOLAP local engine:   %ProgramFiles%\Microsoft Analysis Services\
                               AS OLEDB\<ver>\Cartridges\ (used by local-cube
                               tooling; optional).

    Server instances are auto-discovered from the registry
    (HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\OLAP) with a
    filesystem fallback (%ProgramFiles%\Microsoft SQL Server\MSAS*\OLAP\bin).
    Design-time folders are auto-discovered by searching the Visual Studio /
    SQL tools install roots; pass -SsdtCartridgeDir to skip the search.
    The folder layout is documented in docs/deploy-windows.md ("Cartridge
    locations"); the discovery list follows the deployment script of the
    community PostgreSQL cartridge (Npgsql.xsl) plus Microsoft's HIS
    troubleshooting doc for db2v0801.xsl.

    Idempotent: copying an identical file again is a no-op; -Uninstall removes
    only clickhouse.xsl and never touches other cartridges.

    Requires an elevated session (destinations live under Program Files).
    Works in Windows PowerShell 5.1 and PowerShell 7.

    After installing, restart the SSAS service (and Visual Studio): the
    cartridge list is read at engine startup.

.PARAMETER CartridgePath
    Path to clickhouse.xsl. Default: ..\cartridge\clickhouse.xsl relative to
    this script, falling back to clickhouse.xsl next to the script.

.PARAMETER SsasCartridgeDir
    Explicit server cartridge folder(s). Skips registry auto-discovery.

.PARAMETER SsdtCartridgeDir
    Explicit design-time cartridge folder(s). Skips the VS search.

.PARAMETER ServerOnly
    Deploy only to SSAS server instances (skip design-time folders).

.PARAMETER DesignTimeOnly
    Deploy only to design-time folders (skip server instances).

.PARAMETER Uninstall
    Remove clickhouse.xsl from every discovered/-given destination.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install-cartridge.ps1

.EXAMPLE
    powershell -File .\install-cartridge.ps1 -SsdtCartridgeDir 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\SSAS\Cartridges'
#>
[CmdletBinding()]
param(
    [string]$CartridgePath,
    [string[]]$SsasCartridgeDir,
    [string[]]$SsdtCartridgeDir,
    [switch]$ServerOnly,
    [switch]$DesignTimeOnly,
    [switch]$Uninstall
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$CartridgeFileName = 'clickhouse.xsl'

# --- Preconditions -----------------------------------------------------------

$identity  = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Administrator privileges are required (destinations live under Program Files).'
}

if (-not $CartridgePath) {
    $candidates = @(
        (Join-Path (Split-Path -Parent $PSScriptRoot) (Join-Path 'cartridge' $CartridgeFileName)),
        (Join-Path $PSScriptRoot $CartridgeFileName)
    )
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c) { $CartridgePath = $c; break }
    }
}
if (-not $Uninstall) {
    if (-not ($CartridgePath -and (Test-Path -LiteralPath $CartridgePath))) {
        throw "Cartridge file not found. Pass -CartridgePath <path to $CartridgeFileName>."
    }
}

# --- Discovery ---------------------------------------------------------------

function Get-SsasCartridgeDirs {
    $dirs = New-Object System.Collections.ArrayList

    # 1) Registry: OLAP instance map (instance name -> instance id, e.g.
    #    MSAS16.MSSQLSERVER). The per-instance Setup value names are probed
    #    defensively. TODO(ssas-smoke): record which Setup value name each
    #    SSAS version actually exposes.
    $instKey = 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\OLAP'
    if (Test-Path $instKey) {
        $map = Get-ItemProperty -Path $instKey
        foreach ($prop in $map.PSObject.Properties) {
            if ($prop.Name -in @('PSPath','PSParentPath','PSChildName','PSDrive','PSProvider')) { continue }
            $instanceId = $prop.Value
            $setupKey = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instanceId\Setup"
            if (-not (Test-Path $setupKey)) { continue }
            $setup = Get-ItemProperty -Path $setupKey
            foreach ($valueName in @('SQLBinRoot','SQLPath')) {
                $root = $null
                if ($setup.PSObject.Properties.Name -contains $valueName) { $root = $setup.$valueName }
                if (-not $root) { continue }
                foreach ($suffix in @('Cartridges', 'bin\Cartridges')) {
                    $probe = Join-Path $root $suffix
                    if ((Test-Path -LiteralPath $probe) -and -not $dirs.Contains($probe)) { [void]$dirs.Add($probe) }
                }
            }
        }
    }

    # 2) Filesystem fallback: default install layout
    #    %ProgramFiles%\Microsoft SQL Server\MSAS<nn>.<INSTANCE>\OLAP\bin\Cartridges
    foreach ($pf in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if (-not $pf) { continue }
        $root = Join-Path $pf 'Microsoft SQL Server'
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($inst in (Get-ChildItem -LiteralPath $root -Directory -Filter 'MSAS*' -ErrorAction SilentlyContinue)) {
            $probe = Join-Path $inst.FullName 'OLAP\bin\Cartridges'
            if ((Test-Path -LiteralPath $probe) -and -not $dirs.Contains($probe)) { [void]$dirs.Add($probe) }
        }
    }
    return $dirs
}

function Get-SsdtCartridgeDirs {
    # Known design-time locations (confirmed by third-party cartridge
    # deployments; the VS2022 layout is extrapolated from VS2019 and marked
    # for verification):
    #   VS2017+ AS Projects VSIX:
    #     [VS]\Common7\IDE\CommonExtensions\Microsoft\SSAS\Cartridges
    #     [VS]\Common7\IDE\CommonExtensions\Microsoft\SSAS\LocalServer\cartridges
    #     [VS]\Common7\IDE\CommonExtensions\Microsoft\SSAS\LocalServer\MSOLAP\Cartridges
    #     [VS]\Common7\IDE\CommonExtensions\Microsoft\SSAS\MSOLAP\Cartridges
    #   Legacy BIDS/SSDT and SQL tools VS shell:
    #     [VS]\Common7\IDE\PrivateAssemblies\DataWarehouseDesigner\UIRdmsCartridge
    #     [SQL tools]\<ver>\Tools\Binn\VSShell\Common7\IDE\DataWarehouseDesigner\UIRdmsCartridge
    #   MSOLAP local engine (optional, used by local-cube tooling):
    #     %ProgramFiles%[ (x86)]\Microsoft Analysis Services\AS OLEDB\<ver>\Cartridges
    # TODO(ssas-smoke): record the folders actually present on the operator
    # machine (VS2022) in docs/deploy-windows.md.
    $dirs = New-Object System.Collections.ArrayList
    $roots = New-Object System.Collections.ArrayList
    foreach ($pf in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if (-not $pf) { continue }
        foreach ($sub in @('Microsoft Visual Studio', 'Microsoft SQL Server', 'Microsoft SQL Server Management Studio 21', 'Microsoft Analysis Services')) {
            $p = Join-Path $pf $sub
            if (Test-Path -LiteralPath $p) { [void]$roots.Add($p) }
        }
    }
    if ($env:LOCALAPPDATA) {
        $p = Join-Path $env:LOCALAPPDATA 'Microsoft\VisualStudio'   # per-user VSIX installs
        if (Test-Path -LiteralPath $p) { [void]$roots.Add($p) }
    }
    foreach ($root in $roots) {
        # Legacy design-time folder name.
        $found = Get-ChildItem -LiteralPath $root -Recurse -Directory -Filter 'UIRdmsCartridge' -ErrorAction SilentlyContinue
        foreach ($f in $found) {
            if (-not $dirs.Contains($f.FullName)) { [void]$dirs.Add($f.FullName) }
        }
        # VSIX / engine-adjacent folders: any 'Cartridges' directory that
        # already contains stock cartridges. Requiring sql2000.xsl inside
        # marks a real cartridge folder and skips unrelated directories that
        # happen to share the name.
        $found = Get-ChildItem -LiteralPath $root -Recurse -Directory -Filter 'Cartridges' -ErrorAction SilentlyContinue
        foreach ($f in $found) {
            if ($f.FullName -like '*\OLAP\bin\Cartridges') { continue }   # server dirs handled separately
            if (-not (Test-Path -LiteralPath (Join-Path $f.FullName 'sql2000.xsl'))) { continue }
            if (-not $dirs.Contains($f.FullName)) { [void]$dirs.Add($f.FullName) }
        }
    }
    return $dirs
}

# --- Execute -----------------------------------------------------------------

$targets = New-Object System.Collections.ArrayList

if (-not $DesignTimeOnly) {
    $serverDirs = $SsasCartridgeDir
    if (-not $serverDirs) { $serverDirs = @(Get-SsasCartridgeDirs) }
    if (-not $serverDirs -or $serverDirs.Count -eq 0) {
        Write-Warning 'No SSAS server cartridge folder found (no local SSAS MD instance?). Use -SsasCartridgeDir to specify one.'
    }
    foreach ($d in $serverDirs) { [void]$targets.Add([pscustomobject]@{ Kind = 'server'; Dir = $d }) }
}

if (-not $ServerOnly) {
    $ssdtDirs = $SsdtCartridgeDir
    if (-not $ssdtDirs) {
        Write-Host 'Searching for design-time cartridge folders (may take a moment)...'
        $ssdtDirs = @(Get-SsdtCartridgeDirs)
    }
    if (-not $ssdtDirs -or $ssdtDirs.Count -eq 0) {
        Write-Warning 'No design-time cartridge folder found. Is the "Analysis Services Projects" VS extension installed? Use -SsdtCartridgeDir to specify the folder (see docs/deploy-windows.md).'
    }
    foreach ($d in $ssdtDirs) { [void]$targets.Add([pscustomobject]@{ Kind = 'design-time'; Dir = $d }) }
}

$report = New-Object System.Collections.ArrayList

foreach ($t in $targets) {
    $dest = Join-Path $t.Dir $CartridgeFileName
    if ($Uninstall) {
        if (Test-Path -LiteralPath $dest) {
            Remove-Item -LiteralPath $dest
            $status = 'removed'
        } else {
            $status = 'not present'
        }
    } else {
        if (-not (Test-Path -LiteralPath $t.Dir)) {
            $status = 'SKIPPED (directory does not exist)'
        } else {
            $identical = $false
            if (Test-Path -LiteralPath $dest) {
                $srcHash = (Get-FileHash -LiteralPath $CartridgePath -Algorithm SHA256).Hash
                $dstHash = (Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash
                $identical = ($srcHash -eq $dstHash)
            }
            if ($identical) {
                $status = 'already up to date'
            } else {
                Copy-Item -LiteralPath $CartridgePath -Destination $dest -Force
                $status = 'installed'
            }
        }
    }
    [void]$report.Add([pscustomobject]@{ Kind = $t.Kind; Path = $dest; Status = $status })
}

# --- Final check-status ------------------------------------------------------

Write-Host ''
Write-Host '=== Cartridge deployment status ==='
foreach ($row in $report) {
    Write-Host ('  [{0}] {1}' -f $row.Kind, $row.Path)
    Write-Host ('      -> {0}' -f $row.Status)
}
if (-not $Uninstall) {
    Write-Host ''
    Write-Host 'Restart the SSAS service (and Visual Studio) so the cartridge list is re-read.'
}
