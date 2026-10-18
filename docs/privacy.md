# Privacy policy

*Last updated: October 18, 2026*

Firmly doesn't collect, send or share any information. It has no accounts, analytics, telemetry or ads, and it never connects to the internet on its own.

## What stays on your PC

Firmly keeps a few things on your PC to work. None of it leaves the PC.

- **Your preferences**, such as keeping Firmly in the notification area, and whether you closed the note on how a timed full charge ended. These are kept in Firmly's own app settings, per Windows user.
- **A timed full charge you started**, so it still ends on time after a restart. The Firmly service keeps it in `%ProgramData%\Firmly`, along with how the last one ended and, on HP PCs, how many wrong BIOS passwords in a row it has seen since the PC started.
- **Your BIOS administrator password, only if you ask Firmly to keep it:**
  - **"Remember it"** keeps it encrypted, with a key that only Windows Hello can unlock, for your Windows user alone.
  - **"Switch back by itself"** gives it to the Firmly service, encrypted for the service alone, until the timed full charge ends. The service then deletes it.

  **Settings > BIOS password > Forget** removes the remembered password.
- **Error details** in a log file in Firmly's app folder, and service messages in the Windows event log. They help with a problem report if you choose to send one.

Uninstalling Firmly removes its app settings and app folder. The service's files in `%ProgramData%\Firmly` are small, and you can delete them yourself.

## When information leaves your PC

Only when you choose to:

- **Settings > About > Copy details** copies your device model, BIOS version, Windows version and Firmly's version to the clipboard, for you to paste into a problem report.
- **Report a problem** opens a new GitHub issue in your browser with those same details filled in. They're part of the link, so GitHub receives them when the page opens, but nothing is posted until you submit the issue.
- **Help**, **Source code** and **License** open pages on GitHub in your browser.

GitHub's own privacy policy applies on GitHub.

## Contact

Questions about privacy: open an issue at https://github.com/akrymskiy/firmly/issues.
