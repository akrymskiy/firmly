# Firmly

A lightweight, open-source power and firmware settings app for Windows business notebooks — a small alternative to bulky vendor utilities.

> **Status:** early development. Not yet usable.

## Planned features

- Battery charge management (e.g. HP Battery Health Manager) from a tray app
- Curated view of other BIOS/UEFI settings
- Support for HP business notebooks first (EliteBook, ProBook, ZBook), other vendors later

## How it works

Vendor firmware settings are exposed through WMI, which requires administrator rights even to read. Firmly is split in two:

| Component | Runs as | Role |
|---|---|---|
| `Firmly.App` | Current user | WinUI 3 UI and tray icon; never elevated |
| `Firmly.Service` | LocalSystem (Windows service) | The only component that touches firmware; exposes a narrow, allow-listed command set over a named pipe |

```
src/
  Firmly.App        WinUI 3 app (MSIX)
  Firmly.Core       Firmware provider abstraction + vendor implementations
  Firmly.Ipc        App <-> service contract
  Firmly.Service    Windows service
tests/
  Firmly.Core.Tests
```

### Who can change what

Firmly only exposes settings on a per-vendor allowlist. Each writable setting is either changeable by any signed-in user (power and charging settings, as with Windows power options) or by administrators only. If a BIOS administrator password is set, the firmware requires it for every change.

On shared or managed machines, an administrator can restrict all changes to administrators, directly or through Group Policy:

```powershell
New-Item -Path HKLM:\SOFTWARE\Policies\Firmly -Force | Out-Null
Set-ItemProperty -Path HKLM:\SOFTWARE\Policies\Firmly -Name RequireAdminForAllChanges -Value 1 -Type DWord
```

## Building

Requirements: Windows 10 2004+ and the .NET 10 SDK (pinned in `global.json`).

```powershell
dotnet build Firmly.slnx -p:Platform=x64
dotnet test --solution Firmly.slnx -p:Platform=x64
```

To try the service against real hardware, run it from an elevated terminal:

```powershell
dotnet run --project src/Firmly.Service
```

## Disclaimer

Firmly is an independent project and is not affiliated with, endorsed by, or sponsored by HP Inc. or any other hardware vendor. All product names and trademarks are the property of their respective owners. Changing firmware settings is at your own risk.

## License

[MIT](LICENSE)
