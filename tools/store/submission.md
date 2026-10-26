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
- **Support contact:** firmly.dev@outlook.com, under Support info > **Use different details for this app**. Certification wants a contact that works without an account, so not the GitHub issues page.
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
> Firmly currently supports HP business notebooks. Support for other brands is in progress. Follow the project on GitHub.
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

Firmly declares `runFullTrust`, `packagedServices` and `localSystemServices`. Partner Center asks for a reason for each. Submission 4 (0.2.0) was denied `packagedServices` and `localSystemServices` under 10.6.3 with the shorter reason this replaced, and certification asked for "new or expanded information" and a support contact (see Properties).

Each reason box takes at most 500 characters, so these are short and point to the full reasons in the notes for certification below. Paste these:

**runFullTrust**

> Firmly is a WinUI 3 desktop app (Windows App SDK), packaged as MSIX. It runs as a full trust desktop process, as every WinUI 3 desktop app does, to show its window and notification area icon. The app itself never runs elevated and never touches the firmware; that is the service's job (see packagedServices).

**packagedServices**

> HP's BIOS battery settings are only reachable via WMI in root\HP\InstrumentedBIOS, which needs admin rights even to read. UAC per change fails for standard users and for timed charges that must revert when no one is signed in, so one packaged service is the only part that touches firmware. It takes a fixed command set over a local pipe (network logons denied), changes only allowlisted settings, rate-limits writes, has no network code. Details in Notes for certification. firmly.dev@outlook.com

**localSystemServices**

> HP's BIOS WMI provider accepts calls only from administrators and LocalSystem; LocalService and NetworkService are denied. A service can't run as an admin account without storing its password, so LocalSystem is the only account that works for standard users and when no one is signed in (reverting a timed charge on schedule). Its reach: allowlisted HP battery settings via a local pipe, no network code. Source: github.com/akrymskiy/firmly (src/Firmly.Service). firmly.dev@outlook.com

### Notes for certification

Under Supplemental info > **Additional Testing Information** (up to 6000 characters; it belongs to the product, not to one submission):

> Firmly's battery features need an HP business notebook (EliteBook, ProBook, ZBook, Dragonfly). On other PCs and in virtual machines, Firmly starts normally and shows "This device's firmware isn't supported yet" on the Battery page. That's expected: it then has nothing to change, and it changes nothing.
>
> To check the app on any PC: start Firmly from the Start menu. The Battery page shows the battery charge (or that there's no battery), Settings shows the app and service versions, and the Firmly service appears in services.msc as "Firmly", running as LocalSystem. Closing the window keeps Firmly in the notification area; right-click its icon and choose Exit to quit.
>
> No account, sign-in or test credentials are needed.
>
> RESTRICTED CAPABILITIES (packagedServices, localSystemServices): fuller reasons than fit in Submission options. Questions: firmly.dev@outlook.com
>
> Firmly changes the battery charge settings in HP business notebooks' BIOS: stop charging at 80%, charge fully, or charge to 100% until a set time. HP exposes these settings only through the WMI provider in root\HP\InstrumentedBIOS, which accepts calls only from administrators and LocalSystem, even to read.
>
> We considered the alternatives. Running the app elevated, or asking for elevation (UAC) on each change, doesn't work: most users of business notebooks are standard users with no administrator password, and a timed "charge to 100% until Friday" must switch back on schedule when no one is signed in, after a restart, or at the sign-in screen. So the package installs one Windows service, the only component that talks to the firmware. Packaging it (rather than a separate installer) means it's installed, updated and removed with the app, and signed by the Store. It runs as LocalSystem because LocalService and NetworkService get access denied from HP's provider, and a service can't use an administrator's account without storing that account's password.
>
> The service is deliberately narrow:
> - It accepts a short, fixed command set over one local named pipe. It never runs WMI queries or values from the app; only an allowlist of HP battery and power settings can be read or changed, and only to values the firmware itself offers.
> - The pipe allows only signed-in (interactive) users and denies network logons. The service identifies each caller without impersonating it: standard users can change the charging mode only, as in Windows' own power options; anything else needs an administrator, and a machine policy can restrict all changes to administrators.
> - Firmware writes are capped machine-wide (a burst of 10, then one every 30 seconds) so nothing can wear the BIOS flash by writing in a loop. Refused requests and errors go to the Windows event log.
> - It has no network code, no driver, and touches no other WMI namespace or system setting.
>
> Firmly is open source, so all of this can be checked: https://github.com/akrymskiy/firmly (the service is in src/Firmly.Service).

## Updates

Each release goes to the Store as an update of the existing submission. Firmly's Store ID is `9N9V9740JP8D`; its Partner Center page is https://partner.microsoft.com/dashboard/products/9N9V9740JP8D/overview.

1. Push a `v*` tag. When the Release run finishes, download its **Firmly_Store** artifact and unzip it to get `Firmly_<version>.msixupload`.
2. On the product page, click **Start update**. The new submission starts as a copy of the last one: listing, screenshots, age rating, restricted-capability reasons and audience.
3. **Packages:** drop in the new `.msixupload`, wait until it's validated, and remove the previous package so only the new version ships. The workflow numbers packages `MAJOR.MINOR.<run>.0`, so each release's version is higher.
4. **Store listing > English:** fill in "What's new in this version" with a line or two from the release notes.
5. Check the submission overview, then **Submit to the Store**. Installed copies update through the Store once the update is certified.

The package is over 10 MB, which is too big for Claude's browser upload, so step 3's upload is done by hand.
