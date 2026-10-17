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
}
