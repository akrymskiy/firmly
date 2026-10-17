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

    /// <summary>Call once at start-up, on the UI thread, before showing any.</summary>
    public static void Register()
    {
        try
        {
            if (!AppNotificationManager.IsSupported())
                return;
            AppNotificationManager.Default.NotificationInvoked += (_, args) =>
                App.DispatcherQueue.TryEnqueue(() => Handle(args.Arguments));
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

    /// <param name="action">With <paramref name="button"/>: what the button does, e.g. <see cref="SwitchBackAction"/>.</param>
    public static void Show(string title, string text, string? button = null, string? action = null)
    {
        if (!s_registered)
            return;
        var builder = new AppNotificationBuilder().AddText(title).AddText(text);
        if (button is not null && action is not null)
            builder.AddButton(new AppNotificationButton(button).AddArgument(ActionKey, action));
        try
        {
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
    }

    /// <summary>Acts on a click: the notification itself opens Firmly; a button does what it says.</summary>
    public static void Handle(IDictionary<string, string> arguments)
    {
        if (arguments.TryGetValue(ActionKey, out var action) && action == SwitchBackAction)
        {
            App.ShowMainWindow();
            App.Session.Charge.SwitchBackNowCommand.Execute(null);
            return;
        }
        App.ShowMainWindow();
    }
}
