# Microsoft Store submission

What to enter in Partner Center for Firmly, field by field. Keep this up to date when the listing changes.

## Product identity

Already in `src/Firmly.App/Package.appxmanifest`:

| Field | Value |
|---|---|
| Package/Identity/Name | `AlexKrymskiy.Firmly` |
| Package/Identity/Publisher | `CN=1F69C46A-C1B5-4AA6-9EBE-60DF36002C10` |
| Package/Properties/PublisherDisplayName | `Alex Krymskiy` |

## Packages

Upload `Firmly_<version>.msixupload` from the release run's **Firmly_Store** artifact (Actions > Release > the run > Artifacts). It holds the x64 and ARM64 packages, unsigned; the Store signs them.

## Pricing and availability

- **Markets:** all.
- **Visibility:** for the first submissions, **Private audience**, with your own Microsoft account as the only member, so you can install it from the Store on test PCs. Make it public once it has been tested.
- **Pricing:** free. No trial, and no in-app purchases.

## Properties

- **Category:** Utilities & tools.
- **Privacy policy URL:** https://github.com/akrymskiy/firmly/blob/main/docs/privacy.md
- **Website:** https://github.com/akrymskiy/firmly
- **Support contact:** https://github.com/akrymskiy/firmly/issues
- **Product declarations:** none apply. Firmly has no in-app purchases, collects no personal information, and doesn't depend on Microsoft account sign-in.
- **System requirements:** Windows 10 version 2004 (build 19041) or later, x64 or ARM64. Needs an HP business notebook (EliteBook, ProBook, ZBook, Dragonfly and similar) for the battery settings. Elsewhere it starts but has nothing to change.

## Age ratings

Answer the questionnaire as a **utility** app. It has no violence, sexual content, language, controlled substances or gambling. Users don't communicate with each other or share content, it doesn't share location, and there are no purchases. The expected rating is 3+ / Everyone.

## Store listing (English)

**Product name:** Firmly

**Short description:**

> Battery charge settings for HP business laptops, from the notification area. Keep the battery healthy, or charge to 100% for a trip.

**Description:**

> Firmly is a small, open-source app for the battery settings in your HP business laptop's BIOS, without opening the BIOS setup or installing HP's larger tools.
>
> - Choose how the battery charges: let HP manage it, stop at 80% to make the battery last more years, or charge fully.
> - Charge to 100% for a while (a day, a week, or until a date) before a trip. Firmly switches back by itself when the time is up, even if Firmly isn't open.
> - See the battery's real charge: when HP limits charging to 80%, Windows shows 80% as "100%", and Firmly shows the actual level.
> - Everything from the notification area: a battery icon with the charge, and a panel to change the mode in two clicks.
> - Works with a BIOS administrator password: Firmly asks for it when a change needs it, and can keep it, encrypted and unlocked with Windows Hello, if you ask.
>
> Firmly changes only an allowlisted set of settings. Standard users can change the charging mode, as with Windows' own power settings. Administrators can restrict all changes to themselves through Group Policy.
>
> Firmly collects no data and shows no ads. The source code is at github.com/akrymskiy/firmly.
>
> Firmly currently supports HP business notebooks. Support for other brands may follow. Follow the project on GitHub.
>
> Firmly is an independent project, not affiliated with or endorsed by HP Inc. HP, EliteBook, ProBook, ZBook and Dragonfly are trademarks of HP Inc.

**Product features** (one per line in Partner Center):

- Choose the battery charge mode: managed, stop at 80%, or charge fully
- Charge to 100% for a while, then switch back automatically
- Shows the real battery charge when charging is limited
- Battery icon and quick panel in the notification area
- Works with a BIOS administrator password, with Windows Hello
- Free and open source, with no data collection

**Search terms:** battery, charge limit, HP, battery health, BIOS, EliteBook, laptop battery

**Copyright and trademark info:** © 2026 Alex Krymskiy. HP and the HP product names are trademarks of HP Inc.

**Screenshots:** `tools/store/screenshots/battery-light.png` and `battery-dark.png` (the Battery page, 2138×1230). The Store wants at least one, 1366×768 or larger. A tray panel screenshot is best taken by hand with Win+Shift+S on a real desktop.

## Submission options

### Restricted capabilities

Firmly declares `runFullTrust`, `packagedServices` and `localSystemServices`. Paste this as the reason:

> Firmly is a desktop (full trust) app for the battery charge settings in HP business notebooks' BIOS. HP exposes those settings only through WMI classes in root\HP\InstrumentedBIOS, which require administrator rights even to read. Rather than running the app elevated, Firmly's package installs one Windows service (packagedServices, localSystemServices), the only component that touches the firmware.
>
> The service is deliberately narrow. It accepts a short, fixed command set over a named pipe and never passes WMI queries or arbitrary values from the app. Only allowlisted settings can be read or changed, and only to values the firmware offers. Standard users can change power and charging settings only, as with Windows' power options; anything else needs an administrator, and a machine policy can restrict all changes to administrators. The service checks who is calling, limits how often firmware may be written, and logs refused changes and errors to the Windows event log.
>
> The app itself never runs elevated. The service runs as LocalSystem because the firmware interface requires it, and because it must switch a timed charge back on schedule when no one is signed in.
>
> Source code: https://github.com/akrymskiy/firmly

### Notes for certification

> Firmly's battery features need an HP business notebook (EliteBook, ProBook, ZBook, Dragonfly). On other PCs and in virtual machines, Firmly starts normally and shows "This device's firmware isn't supported yet" on the Battery page. That's expected: it then has nothing to change, and it changes nothing.
>
> To check the app on any PC: start Firmly from the Start menu. The Battery page shows the battery charge (or that there's no battery), Settings shows the app and service versions, and the Firmly service appears in services.msc as "Firmly", running as LocalSystem. Closing the window keeps Firmly in the notification area; right-click its icon and choose Exit to quit.
>
> No account, sign-in or test credentials are needed.
