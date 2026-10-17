using System.ComponentModel;
using Firmly.App.ViewModels;
using Microsoft.Win32;
using Windows.ApplicationModel;
using static Firmly.App.Tray.NativeMethods;

namespace Firmly.App.Tray;

/// <summary>
/// Keeps the tray icon (a battery drawn to match the charge), its tooltip and menu, and the taskbar badge in step
/// with the session, and acts on what's chosen in the menu.
/// </summary>
internal sealed unsafe class TrayController : IDisposable
{
    private const int OpenId = 1, ExitId = 2, SwitchBackId = 3, FirstModeId = 100, FirstDurationId = 200;

    private readonly SessionViewModel _session;
    private readonly TrayIcon _icon = new();
    private readonly string _name = Package.Current.DisplayName;
    private nint _iconHandle;
    // What _iconHandle shows, so it's only redrawn when that changes.
    private (BatteryIconState State, int Size, bool LightTaskbar)? _drawn;

    public TrayController(SessionViewModel session)
    {
        _session = session;

        // The icon's events arrive inside its native window procedure. Act once that has returned: closing the
        // main window from in there fails, which made Exit do nothing.
        _icon.Selected += () => App.DispatcherQueue.TryEnqueue(App.ShowMainWindow);
        _icon.MenuRequested += OnMenuRequested;
        _icon.ThemeChanged += Update;
        _session.PropertyChanged += OnStateChanged;
        _session.Battery.PropertyChanged += OnStateChanged;
        _session.Charge.PropertyChanged += OnStateChanged;
        _session.Charge.ChangeFailed += OnChangeFailed;
        Update();
    }

    public void Dispose()
    {
        _session.PropertyChanged -= OnStateChanged;
        _session.Battery.PropertyChanged -= OnStateChanged;
        _session.Charge.PropertyChanged -= OnStateChanged;
        _session.Charge.ChangeFailed -= OnChangeFailed;
        _icon.Dispose();
        DestroyIcon(_iconHandle);
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => Update();

    /// <summary>Redraws the icon if what it shows changed, and refreshes the tooltip and taskbar badge.</summary>
    public void Update()
    {
        var drawn = (IconState(), GetSystemMetricsForDpi(SM_CXSMICON, _icon.Dpi), TaskbarIsLight());
        nint previous = 0;
        if (_drawn != drawn)
        {
            previous = _iconHandle;
            _iconHandle = CreateIcon(BatteryIconRenderer.Render(drawn.Item1, drawn.Item2, drawn.Item3), drawn.Item2);
            _drawn = drawn;
        }

        var tip = Tooltip();
        _icon.Update(_iconHandle, tip);
        if (App.IsMainWindowVisible)
            TaskbarOverlay.Set(App.WindowHandle, _iconHandle, tip.Replace('\n', ' '));

        // Only once nothing shows the old icon any more.
        if (previous != 0)
            DestroyIcon(previous);
    }

    private BatteryIconState IconState()
    {
        var battery = _session.Battery;
        var status = battery.Power switch
        {
            PowerState.Charging => BatteryIconStatus.Charging,
            PowerState.PluggedIn => BatteryIconStatus.PluggedIn,
            PowerState.OnBattery when battery.Percent <= LowPercent => BatteryIconStatus.Low,
            _ => BatteryIconStatus.None,
        };
        return new BatteryIconState(battery.Percent, status, _session.Charge.HasModes ? _session.Charge.CurrentLimitPercent : null);
    }

    // At or below this, on battery, the fill turns red.
    private const int LowPercent = 20;

    // The notification area follows the system theme (not the apps' one).
    private static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int light && light != 0;
    }

    private static nint CreateIcon(uint[] pixels, int size)
    {
        nint color;
        fixed (uint* bits = pixels)
            color = CreateBitmap(size, size, 1, 32, bits);

        // The alpha channel does the masking; the mask itself must exist, all zeros. Rows are padded to 16 bits.
        var maskBits = new byte[(size + 15) / 16 * 2 * size];
        nint mask;
        fixed (byte* bits = maskBits)
            mask = CreateBitmap(size, size, 1, 1, bits);

        var info = new ICONINFO { fIcon = 1, hbmMask = mask, hbmColor = color };
        var icon = CreateIconIndirect(&info);
        DeleteObject(color);
        DeleteObject(mask);
        return icon;
    }

    // e.g. "Firmly\nBattery 80% or more · plugged in, not charging\nMinimize Battery Health Management until …"
    private string Tooltip()
    {
        List<string> lines = [_name];
        var battery = _session.Battery;
        lines.Add(battery.HasReading ? $"{battery.Charge} · {battery.Details}" : _session.Status);
        if (_session.Charge.HasOverride)
            lines.Add(_session.Charge.OverrideTitle);
        return string.Join("\n", lines);
    }

    private void OnMenuRequested(int x, int y)
    {
        var choice = _icon.ShowMenu(BuildMenu(), x, y);
        App.DispatcherQueue.TryEnqueue(() =>
        {
            Handle(choice);
            // Pick up changes made elsewhere, e.g. in the BIOS setup, for next time.
            if (choice != ExitId)
                _ = _session.RefreshQuietlyAsync();
        });
    }

    private List<TrayMenuItem> BuildMenu()
    {
        var charge = _session.Charge;
        var battery = _session.Battery;
        List<TrayMenuItem> items = [new(battery.HasReading ? battery.Charge : "No battery reading"), TrayMenuItem.Separator];

        if (charge.HasModes)
        {
            for (var i = 0; i < charge.Modes.Count; i++)
                items.Add(new(charge.Modes[i].Label, FirstModeId + i, charge.CanChange, Checked: i == charge.SelectedIndex, Radio: true));

            if (charge.HasOverride)
            {
                items.Add(TrayMenuItem.Separator);
                items.Add(new(charge.OverrideTitle));
                items.Add(new("Switch back now", SwitchBackId, charge.CanChange));
            }
            else if (charge.IsOverrideOffered && charge.RestoreIndex >= 0 && charge.RestoreIndex < charge.RestoreModes.Count)
            {
                List<TrayMenuItem> durations =
                [
                    new($"Then back to {charge.RestoreModes[charge.RestoreIndex].Label}"),
                    TrayMenuItem.Separator,
                    .. charge.Durations.Select((d, i) => new TrayMenuItem(d.Label, FirstDurationId + i, charge.CanChange)),
                ];
                items.Add(TrayMenuItem.Separator);
                items.Add(new("Full charge for", Children: durations));
            }
        }
        else
        {
            items.Add(new(_session.Status));
        }

        items.Add(TrayMenuItem.Separator);
        items.Add(new($"Open {_name}", OpenId, IsDefault: true));
        items.Add(new("Exit", ExitId));
        return items;
    }

    private void Handle(int id)
    {
        var charge = _session.Charge;
        switch (id)
        {
            case OpenId:
                App.ShowMainWindow();
                break;
            case ExitId:
                App.ExitApp();
                break;
            case SwitchBackId:
                charge.SwitchBackNowCommand.Execute(null);
                break;
            case >= FirstDurationId when id - FirstDurationId < charge.Durations.Count:
                charge.DurationIndex = id - FirstDurationId;
                charge.StartOverrideCommand.Execute(null);
                break;
            case >= FirstModeId and < FirstDurationId:
                charge.RequestMode(id - FirstModeId);
                break;
        }
    }

    // Changes made from the tray menu would otherwise fail silently while the window is hidden.
    private void OnChangeFailed(string message)
    {
        if (!App.IsMainWindowVisible)
            _icon.Notify("Couldn't change the battery setting", message, warning: true);
    }
}
