Firmly changes HP business laptops' battery charge settings from Windows. It runs on Windows 10 (version 2004 or later) and Windows 11.

### Install

1. Download the zip for your PC: **x64** for Intel and AMD processors, **ARM64** for Snapdragon.
2. Extract the whole zip.
3. Double-click **Install.cmd** and accept the administrator prompt. Firmly's service runs as LocalSystem, so installing it needs administrator rights.

These test builds are signed with a self-signed certificate. The installer adds that certificate to the computer's Trusted People, so Windows accepts the package. Windows may warn about Install.cmd because it comes from the internet.

To uninstall, run `Install.cmd -Uninstall`, or remove Firmly in Settings > Apps.
