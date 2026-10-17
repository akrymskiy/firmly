using System.Runtime.InteropServices;
using static Firmly.App.Tray.NativeMethods;

namespace Firmly.App.Tray;

/// <summary>An entry in the tray icon's menu. <see cref="Id"/> 0 marks a heading or separator that can't be chosen.</summary>
/// <param name="Radio">Shows <paramref name="Checked"/> as a dot, for one-of-several choices.</param>
/// <param name="IsDefault">Shown in bold: what clicking the icon does.</param>
internal sealed record TrayMenuItem(
    string Text,
    int Id = 0,
    bool Enabled = true,
    bool Checked = false,
    bool Radio = false,
    bool IsDefault = false,
    IReadOnlyList<TrayMenuItem>? Children = null)
{
    public static TrayMenuItem Separator { get; } = new("-");
}

/// <summary>
/// One icon in the notification area (tray), with a tooltip and a native popup menu.
/// Lives on the UI thread: its hidden window receives the icon's mouse and keyboard events there.
/// </summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private const string WindowClass = "Firmly.TrayIcon";
    private const uint CallbackMessage = WM_APP + 1;

    // The window procedure is static (it's called from native code); there's one tray icon per process.
    private static TrayIcon? s_current;

    private readonly nint _hwnd;
    // Explorer broadcasts this when it restarts and the notification area is rebuilt.
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private nint _icon;
    private string _tip = "";
    private bool _added;

    public TrayIcon()
    {
        DarkMenus.Enable();
        var instance = GetModuleHandle(null);
        fixed (char* className = WindowClass)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProc,
                hInstance = instance,
                lpszClassName = className,
            };
            RegisterClassEx(&windowClass);
        }

        // A hidden top-level window, not a message-only one: those don't receive the TaskbarCreated broadcast.
        _hwnd = CreateWindowEx(0, WindowClass, "Firmly tray", 0, 0, 0, 0, 0, 0, 0, instance, 0);
        if (_hwnd == 0)
            throw new InvalidOperationException($"Couldn't create the tray icon's window (error {Marshal.GetLastPInvokeError()}).");
        s_current = this;
    }

    /// <summary>Clicked, or chosen with the keyboard.</summary>
    public event Action? Selected;

    /// <summary>Right-clicked, or the menu key pressed; the menu should open at the given screen point.</summary>
    public event Action<int, int>? MenuRequested;

    /// <summary>Windows switched between light and dark, so the icon may need redrawing.</summary>
    public event Action? ThemeChanged;

    /// <summary>The DPI the notification area draws at, for sizing the icon.</summary>
    public uint Dpi => GetDpiForWindow(_hwnd) is var dpi and > 0 ? dpi : 96;

    /// <summary>Shows the icon, or updates it. The caller keeps ownership of <paramref name="icon"/>.</summary>
    /// <param name="tip">At most 127 characters; longer ones are cut.</param>
    public void Update(nint icon, string tip)
    {
        _icon = icon;
        _tip = tip;
        if (!_added)
        {
            Add();
            return;
        }

        var data = NewData(NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        if (!Shell_NotifyIcon(NIM_MODIFY, &data))
            Add();
    }

    /// <summary>Shows a notification from the icon, e.g. when an action from its menu failed.</summary>
    public void Notify(string title, string text, bool warning = false)
    {
        var data = NewData(NIF_INFO);
        Copy(title, data.szInfoTitle, 64);
        Copy(text, data.szInfo, 256);
        data.dwInfoFlags = warning ? NIIF_WARNING : NIIF_NONE;
        Shell_NotifyIcon(NIM_MODIFY, &data);
    }

    /// <summary>Shows a menu at (<paramref name="x"/>, <paramref name="y"/>) and waits for a choice.</summary>
    /// <returns>The chosen item's <see cref="TrayMenuItem.Id"/>, or 0 when the menu was dismissed.</returns>
    public int ShowMenu(IReadOnlyList<TrayMenuItem> items, int x, int y)
    {
        var menu = BuildMenu(items);
        try
        {
            // Without this the menu doesn't close when clicking elsewhere (documented TrackPopupMenu behaviour).
            SetForegroundWindow(_hwnd);
            var id = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY, x, y, _hwnd, 0);
            PostMessage(_hwnd, WM_NULL, 0, 0);
            return id;
        }
        finally
        {
            // Also destroys the submenus.
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData(0);
            Shell_NotifyIcon(NIM_DELETE, &data);
            _added = false;
        }
        DestroyWindow(_hwnd);
        if (s_current == this)
            s_current = null;
    }

    private void Add()
    {
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIcon(NIM_ADD, &data);
        if (!_added)
            return;

        // Version 4: one message per click or key, with the anchor point for the menu.
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, &data);
    }

    private NOTIFYICONDATAW NewData(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        Copy(_tip, data.szTip, 128);
        return data;
    }

    private static void Copy(string text, char* buffer, int capacity)
    {
        var length = Math.Min(text.Length, capacity - 1);
        text.AsSpan(0, length).CopyTo(new Span<char>(buffer, capacity));
        buffer[length] = '\0';
    }

    private static nint BuildMenu(IReadOnlyList<TrayMenuItem> items)
    {
        var menu = CreatePopupMenu();
        foreach (var item in items)
        {
            if (ReferenceEquals(item, TrayMenuItem.Separator))
            {
                AppendMenu(menu, MF_SEPARATOR, 0, null);
                continue;
            }

            var flags = MF_STRING | (item.Enabled && (item.Id != 0 || item.Children is not null) ? 0 : MF_GRAYED);
            if (item.Children is { } children)
            {
                AppendMenu(menu, flags | MF_POPUP, (nuint)BuildMenu(children), item.Text);
                continue;
            }

            AppendMenu(menu, flags | (item.Checked && !item.Radio ? MF_CHECKED : 0), (nuint)item.Id, item.Text);
            if (item.Checked && item.Radio)
                CheckMenuRadioItem(menu, (uint)item.Id, (uint)item.Id, (uint)item.Id, MF_BYCOMMAND);
            if (item.IsDefault)
                SetMenuDefaultItem(menu, (uint)item.Id, 0);
        }
        return menu;
    }

    [UnmanagedCallersOnly]
    private static nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        // An exception can't cross back into native code; it would end the process.
        try
        {
            if (s_current is { } icon && hwnd == icon._hwnd)
            {
                if (message == CallbackMessage)
                {
                    var anchorX = (short)(wParam & 0xFFFF);
                    var anchorY = (short)((wParam >> 16) & 0xFFFF);
                    switch ((uint)(lParam & 0xFFFF))
                    {
                        case NIN_SELECT or NIN_KEYSELECT:
                            icon.Selected?.Invoke();
                            break;
                        case WM_CONTEXTMENU:
                            icon.MenuRequested?.Invoke(anchorX, anchorY);
                            break;
                    }
                    return 0;
                }

                // "ImmersiveColorSet": light or dark mode changed.
                if (message == WM_SETTINGCHANGE && lParam != 0
                    && new string((char*)lParam) == "ImmersiveColorSet")
                {
                    DarkMenus.Refresh();
                    icon.ThemeChanged?.Invoke();
                }

                if (message == icon._taskbarCreated && icon._taskbarCreated != 0)
                {
                    icon._added = false;
                    icon.Add();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Tray icon: {ex}");
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }
}
