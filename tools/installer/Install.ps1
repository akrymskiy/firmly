<#
Installs Firmly from this folder, as downloaded from a GitHub release: trusts the certificate the package is signed
with, adds the Windows App SDK runtime Firmly needs, and installs Firmly with its service. Asks for administrator
rights once (the service runs as LocalSystem, so installing it needs them).
  Install.cmd                 (or: powershell -ExecutionPolicy Bypass -File Install.ps1)
  Install.cmd -Uninstall

Written for Windows PowerShell 5.1, which every Windows 10 and 11 PC has.
#>
param(
    [switch] $Uninstall
)
$ErrorActionPreference = 'Stop'
$packageName = 'AlexKrymskiy.Firmly'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($Uninstall) { $arguments += '-Uninstall' }
    try {
        $process = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru
        exit $process.ExitCode
    }
    catch {
        Write-Host 'Firmly needs administrator rights to install its service, and the prompt was declined.' -ForegroundColor Yellow
        exit 1
    }
}

try {
    $installed = Get-AppxPackage $packageName
    if ($Uninstall) {
        if ($installed) {
            Write-Host "Removing $($installed.PackageFullName)"
            Remove-AppxPackage $installed.PackageFullName
        }
        Write-Host 'Firmly is uninstalled. Its test certificate stays trusted; remove "1F69C46A-..." from certlm.msc > Trusted People if you like.'
        return
    }

    $architecture = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }
    $package = Get-ChildItem $PSScriptRoot -Filter 'Firmly_*.msix' | Select-Object -First 1
    if (-not $package) { throw 'There is no Firmly_*.msix next to this script. Extract the whole zip first.' }
    if ($package.Name -notlike "*_$architecture.msix") {
        throw "$($package.Name) is not for this PC, which needs the $architecture download."
    }

    Write-Host 'Trusting the certificate Firmly is signed with (Local Computer > Trusted People)'
    Import-Certificate -FilePath (Join-Path $PSScriptRoot 'Firmly.cer') -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

    $runtime = Get-ChildItem $PSScriptRoot -Filter 'Microsoft.WindowsAppRuntime*.msix' | Select-Object -First 1
    Write-Host "Installing $($package.Name)"
    $options = @{ Path = $package.FullName; ForceApplicationShutdown = $true; ForceUpdateFromAnyVersion = $true }
    if ($runtime) { $options.DependencyPath = $runtime.FullName }
    Add-AppxPackage @options

    $service = Get-Service Firmly -ErrorAction SilentlyContinue
    if (-not $service) { throw 'Firmly installed, but its service is missing.' }
    if ($service.Status -ne 'Running') {
        Start-Service Firmly
        $service.Refresh()
    }
    Write-Host "Installed $((Get-AppxPackage $packageName).PackageFullName). Service: $($service.Status)."
    Write-Host 'Start Firmly from the Start menu.' -ForegroundColor Green
}
catch {
    Write-Host $_ -ForegroundColor Red
    exit 1
}
finally {
    Read-Host 'Press Enter to close' | Out-Null
}
