using System.Globalization;
using Firmly.Ipc;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Firmly.App;

/// <summary>
/// Firmly's own words, from Strings\&lt;language&gt;\Resources.resw in the user's Windows display language (English
/// when Firmly has no translation). XAML uses the same file through x:Uid.
/// </summary>
internal static class Text
{
    private static readonly ResourceLoader Loader = new();

    /// <summary>The text named <paramref name="key"/>; the key itself when it's missing, so a gap shows rather than hides.</summary>
    public static string Get(string key)
    {
        var text = Loader.GetString(key);
        return string.IsNullOrEmpty(text) ? key : text;
    }

    /// <summary>The text named <paramref name="key"/>, with {0}, {1}… filled in for the user's culture.</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>The text named <paramref name="key"/>, or <paramref name="fallback"/> when Firmly has none.</summary>
    public static string GetOr(string key, string fallback)
    {
        var text = Loader.GetString(key);
        return string.IsNullOrEmpty(text) ? fallback : text;
    }

    /// <summary>
    /// What went wrong, in the user's language for errors Firmly understands; otherwise the service's own words,
    /// which are specific but English.
    /// </summary>
    public static string ForError(FirmlyServiceException ex) => ex.Error switch
    {
        IpcError.ServiceUnavailable => Get("Error_ServiceUnavailable"),
        IpcError.IncompatibleService => Get("Error_IncompatibleService"),
        IpcError.AdministratorRequired => Get("Error_AdministratorRequired"),
        IpcError.BiosPasswordRequired => Get("Error_BiosPasswordRequired"),
        IpcError.Unsupported => Get("Error_Unsupported"),
        _ => ex.Message,
    };
}
