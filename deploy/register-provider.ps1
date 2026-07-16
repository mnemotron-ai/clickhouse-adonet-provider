<#
.SYNOPSIS
    Registers (or unregisters) the Mnemotron ADO.NET Data Provider for
    ClickHouse on a Windows machine hosting SSAS and/or SSDT (Visual Studio).

.DESCRIPTION
    Idempotent. Two things happen on install:

      1. GAC install of the provider assembly AND its full net48 dependency
         closure (all strong-named; produced by `dotnet publish -f net48`).
         Rationale: SSAS/SSDT load the provider from the GAC, and Fusion
         resolves the provider's dependencies only from the GAC or from the
         host process appbase (msmdsrv.exe / devenv.exe directory) — we
         control neither appbase, and bindingRedirect cannot redirect a load
         *location*. See docs/deploy-windows.md for the trade-off discussion.

      2. <system.data>/<DbProviderFactories> registration in machine.config
         of BOTH .NET Framework branches (64-bit for the SSAS service,
         32-bit for older/32-bit design-time tooling):
           %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\Config\machine.config
           %WINDIR%\Microsoft.NET\Framework\v4.0.30319\Config\machine.config
         Edited via System.Xml (never text replace); a timestamped backup is
         written next to each file before the first modification.

    Requires: Windows PowerShell 5.1 (NOT pwsh/PowerShell 7 — GAC install
    uses System.EnterpriseServices, which needs the .NET Framework CLR) and
    an elevated (Administrator) session.

.PARAMETER AssemblyPath
    Path to the net48 Mnemotron.Data.ClickHouse.dll. Default: next to this
    script. Point it at the output of:
        dotnet publish src/Mnemotron.Data.ClickHouse -f net48 -c Release
    so the dependency DLLs sit in the same directory.

.PARAMETER Uninstall
    Remove the machine.config entries and GAC-remove the provider assembly.
    Dependencies are left in the GAC unless -RemoveDependencies is also given
    (they are common Microsoft.Extensions.*/System.* assemblies that other
    products may rely on).

.PARAMETER RemoveDependencies
    With -Uninstall: also GAC-remove every dependency assembly found next to
    AssemblyPath. Use only when nothing else on the machine consumes them.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\register-provider.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\register-provider.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$AssemblyPath = (Join-Path -Path $PSScriptRoot -ChildPath 'Mnemotron.Data.ClickHouse.dll'),
    [switch]$Uninstall,
    [switch]$RemoveDependencies
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# --- Constants ---------------------------------------------------------------

$ProviderInvariant   = 'Mnemotron.Data.ClickHouse'
$ProviderDisplayName = 'Mnemotron ADO.NET Data Provider for ClickHouse'
$ProviderDescription = 'ADO.NET data provider for ClickHouse over HTTP(S). Unofficial; not affiliated with or endorsed by ClickHouse, Inc.'
$FactoryTypeName     = 'Mnemotron.Data.ClickHouse.ADO.ClickHouseConnectionFactory'

$MachineConfigPaths = @(
    (Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\Config\machine.config'),
    (Join-Path $env:windir 'Microsoft.NET\Framework\v4.0.30319\Config\machine.config')
)

# --- Preconditions -----------------------------------------------------------

if ($PSVersionTable.PSEdition -eq 'Core') {
    throw 'Run this script with Windows PowerShell 5.1 (powershell.exe), not pwsh: GAC installation requires System.EnterpriseServices on the .NET Framework CLR.'
}

$identity  = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Administrator privileges are required (GAC + machine.config are machine-wide).'
}

if (-not (Test-Path -LiteralPath $AssemblyPath)) {
    if ($Uninstall) {
        Write-Warning "Assembly not found at '$AssemblyPath'; uninstall will locate it inside the GAC instead."
    } else {
        throw "Provider assembly not found: '$AssemblyPath'. Build it first: dotnet publish src/Mnemotron.Data.ClickHouse -f net48 -c Release"
    }
}

Add-Type -AssemblyName System.EnterpriseServices
$publisher = New-Object System.EnterpriseServices.Internal.Publish

# --- Helpers -----------------------------------------------------------------

function Get-StrongName([string]$Path) {
    # Returns the AssemblyName or $null when the file is not a strong-named
    # managed assembly (GAC-ineligible).
    try {
        $an = [System.Reflection.AssemblyName]::GetAssemblyName($Path)
    } catch {
        return $null
    }
    $token = $an.GetPublicKeyToken()
    if ($null -eq $token -or $token.Length -eq 0) { return $null }
    return $an
}

function Get-GacPath([System.Reflection.AssemblyName]$Name) {
    # v4 GAC layout: %WINDIR%\Microsoft.NET\assembly\GAC_MSIL\<n>\v4.0_<ver>__<token>\<n>.dll
    # (our assemblies are all AnyCPU/MSIL). Returns the expected file path.
    $token = -join ($Name.GetPublicKeyToken() | ForEach-Object { $_.ToString('x2') })
    $dir = Join-Path $env:windir ('Microsoft.NET\assembly\GAC_MSIL\{0}\v4.0_{1}__{2}' -f $Name.Name, $Name.Version, $token)
    return (Join-Path $dir ($Name.Name + '.dll'))
}

function Test-InGac([System.Reflection.AssemblyName]$Name) {
    return (Test-Path -LiteralPath (Get-GacPath $Name))
}

function Install-ToGac([string]$Path, [System.Reflection.AssemblyName]$Name) {
    # Publish.GacInstall reports some failures only on its console output,
    # so success is verified against the GAC directory afterwards.
    $publisher.GacInstall($Path)
    if (-not (Test-InGac $Name)) {
        throw "GAC install failed for '$Path' (assembly not present in GAC after GacInstall). Run elevated and check the assembly is strong-named."
    }
}

function Remove-FromGac([System.Reflection.AssemblyName]$Name, [string]$PreferredPath) {
    if (-not (Test-InGac $Name)) { return $false }
    $path = $PreferredPath
    if (-not ($path -and (Test-Path -LiteralPath $path))) {
        $path = Get-GacPath $Name   # GacRemove accepts the GAC copy's own path
    }
    $publisher.GacRemove($path)
    if (Test-InGac $Name) {
        Write-Warning "GacRemove did not remove '$($Name.FullName)' (still present)."
        return $false
    }
    return $true
}

function Get-DependencyFiles([string]$ProviderPath) {
    # Every strong-named managed DLL sitting next to the provider, i.e. the
    # `dotnet publish -f net48` closure. Non-strong-named files are reported
    # and skipped (they cannot go to the GAC).
    $dir = Split-Path -Parent $ProviderPath
    $providerFile = Split-Path -Leaf $ProviderPath
    $result = @()
    foreach ($f in (Get-ChildItem -LiteralPath $dir -Filter '*.dll' | Sort-Object Name)) {
        if ($f.Name -eq $providerFile) { continue }
        $an = Get-StrongName $f.FullName
        if ($null -eq $an) {
            Write-Warning "Skipping '$($f.Name)': not a strong-named managed assembly (GAC-ineligible)."
            continue
        }
        $result += [pscustomobject]@{ Path = $f.FullName; Name = $an }
    }
    return $result
}

function Edit-MachineConfig([string]$ConfigPath, [string]$FactoryTypeFullString, [bool]$Remove) {
    # Returns a status string for the final report.
    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        return 'SKIPPED (file not found — this Framework branch is not installed)'
    }

    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    $xml.Load($ConfigPath)

    $configuration = $xml.DocumentElement
    if ($null -eq $configuration -or $configuration.Name -ne 'configuration') {
        throw "Unexpected root element in '$ConfigPath'."
    }

    # machine.config can legitimately contain more than one DbProviderFactories
    # element (historical duplicate-element issue, KB2468871); scan all of them.
    # Snapshot node lists with @() before removal: SelectNodes lists are lazy
    # and removing while enumerating can skip nodes.
    $existingAdds    = @($configuration.SelectNodes("system.data/DbProviderFactories/add[@invariant='$ProviderInvariant']"))
    $existingRemoves = @($configuration.SelectNodes("system.data/DbProviderFactories/remove[@invariant='$ProviderInvariant']"))

    # Idempotency: a single, exactly-matching entry means nothing to do.
    if (-not $Remove -and $existingAdds.Count -eq 1 -and $existingRemoves.Count -eq 0) {
        $e = $existingAdds[0]
        if ($e.GetAttribute('name') -eq $ProviderDisplayName -and
            $e.GetAttribute('description') -eq $ProviderDescription -and
            $e.GetAttribute('type') -eq $FactoryTypeFullString) {
            return 'already registered (no change)'
        }
    }

    $changed = $false
    foreach ($s in ($existingAdds + $existingRemoves)) {
        $parent = $s.ParentNode
        $prev = $s.PreviousSibling
        if ($null -ne $prev -and $prev.NodeType -eq [System.Xml.XmlNodeType]::Whitespace) {
            [void]$parent.RemoveChild($prev)   # avoid blank-line build-up across runs
        }
        [void]$parent.RemoveChild($s)
        $changed = $true
    }

    $status = 'unregistered'
    if (-not $Remove) {
        # Ensure <system.data><DbProviderFactories> exists.
        $systemData = $configuration.SelectSingleNode('system.data')
        if ($null -eq $systemData) {
            $systemData = $xml.CreateElement('system.data')
            [void]$configuration.AppendChild($systemData)
            $changed = $true
        }
        $factories = $systemData.SelectSingleNode('DbProviderFactories')
        if ($null -eq $factories) {
            $factories = $xml.CreateElement('DbProviderFactories')
            [void]$systemData.AppendChild($factories)
            $changed = $true
        }

        $add = $xml.CreateElement('add')
        $add.SetAttribute('name',        $ProviderDisplayName)
        $add.SetAttribute('invariant',   $ProviderInvariant)
        $add.SetAttribute('description', $ProviderDescription)
        $add.SetAttribute('type',        $FactoryTypeFullString)
        [void]$factories.AppendChild($xml.CreateWhitespace("`r`n      "))
        [void]$factories.AppendChild($add)
        [void]$factories.AppendChild($xml.CreateWhitespace("`r`n    "))
        $changed = $true
        $status = 'registered'
    }

    if ($changed) {
        $backup = '{0}.mnemotron-backup-{1}' -f $ConfigPath, (Get-Date -Format 'yyyyMMdd-HHmmss')
        Copy-Item -LiteralPath $ConfigPath -Destination $backup
        $xml.Save($ConfigPath)
        return ('{0} (backup: {1})' -f $status, (Split-Path -Leaf $backup))
    }
    return 'no change needed'
}

# --- Resolve assembly identity -----------------------------------------------

if (Test-Path -LiteralPath $AssemblyPath) {
    $providerName = Get-StrongName $AssemblyPath
    if ($null -eq $providerName) { throw "'$AssemblyPath' is not a strong-named assembly." }
} else {
    # Uninstall with the original file gone: reconstruct identity from the GAC.
    $gacRoot = Join-Path $env:windir 'Microsoft.NET\assembly\GAC_MSIL\Mnemotron.Data.ClickHouse'
    $candidate = $null
    if (Test-Path -LiteralPath $gacRoot) {
        $candidate = Get-ChildItem -LiteralPath $gacRoot -Recurse -Filter 'Mnemotron.Data.ClickHouse.dll' | Select-Object -First 1
    }
    if ($null -eq $candidate) {
        Write-Warning 'Provider assembly found neither on disk nor in the GAC; only machine.config entries will be removed.'
        $providerName = $null
    } else {
        $providerName = Get-StrongName $candidate.FullName
        $AssemblyPath = $candidate.FullName
    }
}

$factoryTypeFullString = $null
if ($null -ne $providerName) {
    $factoryTypeFullString = '{0}, {1}' -f $FactoryTypeName, $providerName.FullName
}

# --- Execute -----------------------------------------------------------------

$report = New-Object System.Collections.ArrayList

if ($Uninstall) {
    foreach ($cfg in $MachineConfigPaths) {
        $r = Edit-MachineConfig -ConfigPath $cfg -FactoryTypeFullString $factoryTypeFullString -Remove $true
        [void]$report.Add([pscustomobject]@{ Item = $cfg; Status = $r })
    }
    if ($null -ne $providerName) {
        if (Test-Path -LiteralPath $AssemblyPath) {
            $deps = Get-DependencyFiles $AssemblyPath
        } else {
            $deps = @()
        }
        $removed = Remove-FromGac -Name $providerName -PreferredPath $AssemblyPath
        if ($removed) { $s = 'removed from GAC' } else { $s = 'not in GAC / not removed' }
        [void]$report.Add([pscustomobject]@{ Item = "GAC: $($providerName.FullName)"; Status = $s })
        if ($RemoveDependencies) {
            foreach ($d in $deps) {
                $removed = Remove-FromGac -Name $d.Name -PreferredPath $d.Path
                if ($removed) { $s = 'removed from GAC' } else { $s = 'not in GAC / not removed' }
                [void]$report.Add([pscustomobject]@{ Item = "GAC: $($d.Name.FullName)"; Status = $s })
            }
        } elseif ($null -ne $providerName) {
            Write-Host 'Dependency assemblies left in the GAC (rerun with -RemoveDependencies to remove them).'
        }
    }
} else {
    # 1. GAC: provider + dependency closure.
    Install-ToGac -Path $AssemblyPath -Name $providerName
    [void]$report.Add([pscustomobject]@{ Item = "GAC: $($providerName.FullName)"; Status = 'installed' })
    foreach ($d in (Get-DependencyFiles $AssemblyPath)) {
        Install-ToGac -Path $d.Path -Name $d.Name
        [void]$report.Add([pscustomobject]@{ Item = "GAC: $($d.Name.FullName)"; Status = 'installed' })
    }
    # 2. machine.config, both bitnesses.
    foreach ($cfg in $MachineConfigPaths) {
        $r = Edit-MachineConfig -ConfigPath $cfg -FactoryTypeFullString $factoryTypeFullString -Remove $false
        [void]$report.Add([pscustomobject]@{ Item = $cfg; Status = $r })
    }
}

# --- Final check-status ------------------------------------------------------

Write-Host ''
Write-Host '=== Registration status ==='
foreach ($row in $report) {
    Write-Host ('  {0}' -f $row.Item)
    Write-Host ('      -> {0}' -f $row.Status)
}
Write-Host ''
Write-Host 'Verification:'
if ($null -ne $providerName) {
    if (Test-InGac $providerName) { $s = 'PRESENT' } else { $s = 'ABSENT' }
    Write-Host ('  GAC contains provider: {0}' -f $s)
}
foreach ($cfg in $MachineConfigPaths) {
    if (Test-Path -LiteralPath $cfg) {
        $xml = New-Object System.Xml.XmlDocument
        $xml.Load($cfg)
        $hit = $xml.SelectSingleNode("configuration/system.data/DbProviderFactories/add[@invariant='$ProviderInvariant']")
        if ($null -ne $hit) { $s = 'REGISTERED' } else { $s = 'not registered' }
        Write-Host ('  {0}: {1}' -f $cfg, $s)
    }
}
Write-Host ''
Write-Host 'Next step: deploy the MD cartridge with install-cartridge.ps1, then run docs/ssas-smoke-checklist.md.'
