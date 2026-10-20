Firmly changes HP business laptops' battery charge settings from Windows. It runs on Windows 10 (version 2004 or later) and Windows 11.

### Install from the Microsoft Store (recommended)

https://apps.microsoft.com/detail/9N9V9740JP8D

The Store version is signed by Microsoft, updates itself, and runs with Smart App Control on.

### Install from this page

1. Download the zip for your PC: **x64** for Intel and AMD processors, **ARM64** for Snapdragon.
2. Extract the whole zip.
3. Double-click **Install.cmd** and accept the administrator prompt. Firmly's service runs as LocalSystem, so installing it needs administrator rights.

These builds are signed with a self-signed certificate. The installer adds that certificate to the computer's Trusted People, so Windows accepts the package. Windows may warn about Install.cmd because it comes from the internet.

**Smart App Control:** if it's on (Windows Security > App & browser control), Windows blocks these builds even with the certificate installed, and Firmly won't start. Use the Store version instead.

To uninstall, run `Install.cmd -Uninstall`, or remove Firmly in Settings > Apps.
