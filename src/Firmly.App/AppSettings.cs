using Windows.Foundation.Collections;
using Windows.Storage;

namespace Firmly.App;

/// <summary>Firmly's own preferences, kept per user in the package's local settings.</summary>
internal static class AppSettings
{
    /// <summary>The startup task in Package.appxmanifest that starts Firmly at sign-in.</summary>
    public const string StartupTaskId = "FirmlyStartup";

    private static IPropertySet Values => ApplicationData.Current.LocalSettings.Values;

    /// <summary>Closing the window keeps Firmly running in the notification area. On unless turned off.</summary>
    public static bool CloseToTray
    {
        get => Values["CloseToTray"] as bool? ?? true;
        set => Values["CloseToTray"] = value;
    }

    /// <summary>
    /// With a BIOS password, a timed full charge lets the service keep the password until it ends, to switch back by
    /// itself. Off unless turned on: Firmly then asks when it's time.
    /// </summary>
    public static bool SwitchBackByItself
    {
        get => Values["SwitchBackByItself"] as bool? ?? false;
        set => Values["SwitchBackByItself"] = value;
    }

    /// <summary>
    /// The note on how a timed charge ended is closed for every one that ended at this time or before. Kept per user,
    /// so closing it doesn't hide it from others on the same PC.
    /// </summary>
    public static DateTimeOffset EndNoteClosedUpTo
    {
        get => Values["EndNoteClosedUpTo"] is long ticks ? new DateTimeOffset(ticks, TimeSpan.Zero) : DateTimeOffset.MinValue;
        set => Values["EndNoteClosedUpTo"] = value.UtcTicks;
    }
}
