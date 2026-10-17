#Requires -Version 7.4
<#
Builds Firmly with its packaged service and registers it from a loose layout, the way an MSIX install would,
for trying the real LocalSystem service. Needs Developer Mode; asks for administrator rights (UAC).
  tools\install-package.ps1 [-Configuration Debug|Release] [-Platform x64|ARM64]
  tools\install-package.ps1 -Uninstall

Dev builds (dotnet run) register as "Firmly Dev" and can run next to it; tools\start-service.cmd stops this
service while the dev one runs.
#>
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateSet('x64', 'ARM64')] [string] $Platform = $env:PROCESSOR_ARCHITECTURE -eq 'ARM64' ? 'ARM64' : 'x64',
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

$isAdmin = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $Elevated) {
    if (-not $Uninstall) {
        # Build before elevating, so the build output stays owned by you. Start empty: files left from earlier
        # builds would be copied into the layout too.
        if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
        dotnet build "$root\src\Firmly.App" -c $Configuration "-p:Platform=$Platform" -p:FirmlyPackageService=true "-p:OutDir=$staging\" -v q -nologo
        if ($LASTEXITCODE) { exit $LASTEXITCODE }

        # Native AOT gives one exe; a trimmed build (-p:PublishAot=false) keeps a .dll beside it.
        $serviceFiles = Get-ChildItem "$staging\Firmly.Service" -Recurse -File
        $kind = (Test-Path "$staging\Firmly.Service\Firmly.Service.dll") ? 'trimmed, not native AOT' : 'native AOT'
        Write-Host ('Service: {0}, {1:N1} MB in {2} files' -f $kind, (($serviceFiles | Measure-Object Length -Sum).Sum / 1MB), $serviceFiles.Count)
    }
    if (-not $isAdmin) {
        $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"",
            '-Configuration', $Configuration, '-Platform', $Platform, '-Elevated')
        if ($Uninstall) { $arguments += '-Uninstall' }
        $process = Start-Process pwsh.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru
        exit $process.ExitCode
    }
}

try {
    $existing = Get-AppxPackage $packageName
    if ($existing) {
        Write-Host "Removing $($existing.PackageFullName) (installed from $($existing.InstallLocation))"
        Remove-AppxPackage $existing.PackageFullName
    }
    if ($Uninstall) {
        Write-Host 'Firmly is uninstalled.'
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
