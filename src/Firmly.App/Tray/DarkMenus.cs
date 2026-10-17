using static Firmly.App.Tray.NativeMethods;

namespace Firmly.App.Tray;

/// <summary>
/// Lets this process's classic Win32 menus (the tray menu) follow Windows' dark mode.
/// </summary>
/// <remarks>
/// Uses uxtheme.dll's SetPreferredAppMode and FlushMenuThemes, which are undocumented and exported by ordinal
/// only (135 and 136). They have worked unchanged since Windows 10 1903, and Explorer and many Win32 apps rely on
/// them, but Windows could drop them. Both are looked up at run time; if either is missing, menus stay light.
/// </remarks>
internal static unsafe class DarkMenus
{
    private const int AllowDark = 1;

    private static delegate* unmanaged<int, int> s_setPreferredAppMode;
    private static delegate* unmanaged<void> s_flushMenuThemes;
    private static bool s_looked;

    /// <summary>Menus follow the system's light or dark mode from now on. Call before creating menus.</summary>
    public static void Enable()
    {
        Find();
        if (s_setPreferredAppMode == null)
            return;

        s_setPreferredAppMode(AllowDark);
        Refresh();
    }

    /// <summary>Applies a change between light and dark to menus.</summary>
    public static void Refresh()
    {
        if (s_flushMenuThemes != null)
            s_flushMenuThemes();
    }

    private static void Find()
    {
        if (s_looked)
            return;
        s_looked = true;

        var uxtheme = LoadLibraryEx("uxtheme.dll", 0, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (uxtheme == 0)
            return;

        var setPreferredAppMode = GetProcAddress(uxtheme, 135);
        var flushMenuThemes = GetProcAddress(uxtheme, 136);
        // Use them only as a pair, and only from Windows 10 1903 (build 18362), where 135 became SetPreferredAppMode.
        if (setPreferredAppMode == 0 || flushMenuThemes == 0 || Environment.OSVersion.Version.Build < 18362)
            return;

        s_setPreferredAppMode = (delegate* unmanaged<int, int>)setPreferredAppMode;
        s_flushMenuThemes = (delegate* unmanaged<void>)flushMenuThemes;
    }
}
