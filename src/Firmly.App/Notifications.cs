using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Firmly.App;

/// <summary>
/// Windows notifications from Firmly, and what clicking them does. Clicks reach the running Firmly through the COM
/// class in Package.appxmanifest, or start it.
/// </summary>
internal static class Notifications
{
    private const string ActionKey = "action";

    /// <summary>A button that ends the timed charge now (asking for the BIOS password if needed).</summary>
    public const string SwitchBackAction = "switch-back";

    private static bool s_registered;

    /// <summary>
    /// Call once at start-up, before anything reads the activation (<c>AppInstance.GetActivatedEventArgs</c>), and
    /// before showing any.
    /// </summary>
    public static void Register()
    {
        try
        {
            if (!AppNotificationManager.IsSupported())
                return;
            // A click that starts Firmly arrives with the activation instead, so there's a window by the time any comes here.
            AppNotificationManager.Default.NotificationInvoked += (_, args) =>
                App.DispatcherQueue?.TryEnqueue(() => Handle(args.Arguments));
            AppNotificationManager.Default.Register();
            s_registered = true;
        }
        catch (Exception ex)
        {
            // Firmly works without notifications; the tray tooltip and Battery page still say what happened.
            App.LogError(ex);
        }
    }

    /// <summary>Lets a later click start a new Firmly once this one has exited.</summary>
    public static void Unregister()
    {
        if (s_registered)
            AppNotificationManager.Default.Unregister();
        s_registered = false;
    }

    /// <param name="action">
    /// With <paramref name="button"/>: what the button does, e.g. <see cref="SwitchBackAction"/>. Clicking the notification
    /// itself does the same: it's what the notification is for.
    /// </param>
    public static void Show(string title, string text, string? button = null, string? action = null)
    {
        if (!s_registered)
            return;
        var builder = new AppNotificationBuilder().AddText(title).AddText(text);
        if (button is not null && action is not null)
            builder.AddArgument(ActionKey, action).AddButton(new AppNotificationButton(button).AddArgument(ActionKey, action));
        try
        {
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
    }

    /// <summary>Acts on a click: opens Firmly on the Battery page, which all its notifications are about, then does the action.</summary>
    public static void Handle(IDictionary<string, string> arguments)
    {
        App.ShowBattery();
        if (arguments.TryGetValue(ActionKey, out var action) && action == SwitchBackAction)
            App.Session.Charge.SwitchBackNowCommand.Execute(null);
    }
}
