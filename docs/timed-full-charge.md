# Timed full charge

Most of the time a battery lasts more years if it doesn't sit at 100%, so many people keep their laptop on a mode that stops charging early, such as HP's *Maximize Battery Health Management* (stops at 80%). Before a trip you may want a full battery instead. A **timed full charge** charges to 100% for as long as you choose, then switches back to your usual mode by itself.

## Starting one

- **Battery page:** under **Full charge for a while**, pick how long (1 day to 2 weeks, or **Until a date…** up to 31 days ahead) and the mode to switch back to, then select **Start**.
- **Tray icon:** select the Firmly icon in the notification area, then **Full charge for…** and a length of time. It switches back to the mode you're on now.

If your PC's BIOS has an administrator password, Firmly asks for it first. See [With a BIOS password](#with-a-bios-password).

## While it runs

The Battery page and the tray panel show a green bar such as *Minimize Battery Health Management until Saturday, October 25, 2026 9:00 AM*, with the mode it will switch back to.

- **Switch back now** ends it early.
- Picking another charge mode, in Firmly or in the BIOS setup, also ends it. Firmly then leaves the mode you picked alone.
- Restarting or shutting down doesn't end it. Firmly remembers it.

## When the time is up

The switch back is done by the **Firmly service**, which runs in the background whenever Windows is running. The Firmly window doesn't need to be open, Firmly doesn't need to be in the tray, and nobody needs to be signed in.

The service checks once a minute. If the laptop was asleep or off when the time ran out, it switches back within a minute of the PC waking or starting.

What happens next depends on whether the BIOS has an administrator password.

### Without a BIOS password

The service switches back by itself. You don't need to do anything.

### With a BIOS password

Every BIOS change needs the password, the switch back included. Choose how Firmly handles this in **Settings > BIOS password > When a timed full charge ends**:

| Choice | When the time is up |
|---|---|
| **Ask me, with Windows Hello or the password** (the default) | The battery stays at full charge until you give the password. |
| **Switch back by itself** | The service switches back alone, like a PC without a BIOS password. |

**Ask me.** When the time is up, the bar on the Battery page and in the tray panel turns yellow: *Switching back to … needs the BIOS password.* You also get a notification with a **Switch back now** button. Select it, or the bar's own **Switch back now** button, then confirm with Windows Hello (if you chose to remember the password) or type the password. If Firmly isn't running when the time runs out, the notification comes the next time it starts, for example when you sign in.

Until you give the password, Firmly doesn't try again on its own, so it can't use up your tries. The service can't ask for the password itself.

**Switch back by itself.** When you start the timed charge, Firmly passes the password you give to the service, which keeps it encrypted so that only the service on this PC can read it. It's used once, to switch back, and then deleted. If the password stops working before then (for example, you changed it in the BIOS setup), the service deletes it and falls back to asking.

The choice applies to timed charges you start after changing it. Your organization can turn off keeping the password, in which case Firmly always asks.

> **Wrong passwords.** HP's BIOS refuses all changes after three wrong passwords in a row, until the PC restarts. Firmly warns you before the last try, and once the BIOS has stopped accepting passwords, Firmly stops sending them.

## Afterwards: the note on how it ended

Once the service has switched back, the Battery page and the tray panel show a note:

> **Your timed full charge has ended**
> On Saturday, October 25, 2026 9:01 AM, Firmly switched the battery back to Maximize Battery Health Management.

The note shows the time the switch actually happened, so it stays accurate however long it is until you see it. Dates and times throughout Firmly follow your Windows settings (**Settings > Time & language > Language & region > Regional format**), so they may look different from the examples here. It stays until you close it, or until you start another timed charge.

- **If Firmly was running at the time** (open, or in the tray), you also get a notification saying the battery is back on your usual mode. Selecting the notification opens the Battery page and closes the note.
- **If Firmly wasn't running**, there's no notification later. The note is waiting the next time you open Firmly or its tray panel, even if Firmly starts in the tray when you sign in.
- Each person closes the note for themselves. On a shared PC, closing it doesn't hide it from anyone else.

If you end the timed charge yourself, by **Switch back now**, by picking another mode, or by giving the password when asked, there's no note, since you already know.

### If it couldn't switch back

If the mode you chose to return to is gone when the time is up (for example, a BIOS update removed it), the battery stays at full charge and the note turns yellow:

> On Saturday, October 25, 2026 9:01 AM, Firmly couldn't switch back to Maximize Battery Health Management: this device no longer offers it. The battery is still on Minimize Battery Health Management; pick a mode below.

Pick a charge mode on the Battery page or in the tray panel.

## At a glance

| BIOS password | Setting | Firmly running when the time is up | What you see |
|---|---|---|---|
| None | — | Yes | The service switches back. Notification, plus the note until you close it. |
| None | — | No | The service switches back. The note, next time you open Firmly. |
| Set | Switch back by itself | Yes | The same as with no password: notification and note. |
| Set | Switch back by itself | No | The same as with no password: the note. |
| Set | Ask me | Yes | Full charge continues. A yellow bar and a notification with **Switch back now**. |
| Set | Ask me | No | Full charge continues. The yellow bar and the notification come when Firmly next starts. |
