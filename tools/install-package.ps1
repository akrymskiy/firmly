<#
Builds Firmly with its packaged service and registers it from a loose layout, the way an MSIX install would,
for trying the real LocalSystem service. Needs Developer Mode; asks for administrator rights (UAC).
  tools\install-package.ps1 [-Configuration Debug|Release] [-Platform x64|ARM64]
  tools\install-package.ps1 -Uninstall

While this is installed, dotnet run can't register the app (removing a package with a service needs administrator
rights), and tools\start-service.cmd can't start a second service. Run with -Uninstall to go back to those.
#>
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateSet('x64', 'ARM64')] [string] $Platform = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }),
    [switch] $Uninstall,
    # Set by the script itself when it relaunches elevated.
    [switch] $Elevated
)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$staging = Join-Path $root "artifacts\package\$Platform-$Configuration\build"
# The installed service runs from, and locks, this folder; builds go to $staging and are copied in once it's removed.
$layout = Join-Path $root "artifacts\package\$Platform-$Configuration\layout"
$packageName = 'AlexKrymskiy.Firmly'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $Elevated) {
    if (-not $Uninstall) {
        # Build before elevating, so the build output stays owned by you.
        dotnet build "$root\src\Firmly.App" -c $Configuration "-p:Platform=$Platform" -p:FirmlyPackageService=true "-p:OutDir=$staging\" -v q -nologo
        if ($LASTEXITCODE) { exit $LASTEXITCODE }
    }
    if (-not $isAdmin) {
        $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"",
            '-Configuration', $Configuration, '-Platform', $Platform, '-Elevated')
        if ($Uninstall) { $arguments += '-Uninstall' }
        $process = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru
        exit $process.ExitCode
    }
}

try {
    # Also removes the registration dotnet run made, which has the same identity.
    $existing = Get-AppxPackage $packageName
    if ($existing) {
        Write-Host "Removing $($existing.PackageFullName) (installed from $($existing.InstallLocation))"
        Remove-AppxPackage $existing.PackageFullName
    }
    if ($Uninstall) {
        Write-Host 'Firmly is uninstalled; dotnet run and tools\start-service.cmd work again.'
        return
    }

    if (Get-Process Firmly.Service -ErrorAction SilentlyContinue) {
        Write-Warning 'Firmly.Service is already running (from tools\start-service.cmd?). Stop it, or the packaged service will fail to create its pipe.'
    }

    robocopy $staging $layout /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying the build to $layout failed (robocopy exit code $LASTEXITCODE)." }

    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml')
    $package = Get-AppxPackage $packageName
    Write-Host "Installed $($package.PackageFullName) from $layout"

    $service = Get-Service Firmly
    if ($service.Status -ne 'Running') {
        Start-Service Firmly
        $service.Refresh()
    }
    Write-Host "Service '$($service.Name)': $($service.Status), $($service.StartType)"
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
finally {
    # Keep the elevated window open to read the output.
    if ($Elevated) { Read-Host 'Press Enter to close' | Out-Null }
}
