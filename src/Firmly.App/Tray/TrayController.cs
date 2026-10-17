using System.ComponentModel;
using Firmly.App.ViewModels;
using Windows.ApplicationModel;
using static Firmly.App.Tray.NativeMethods;

namespace Firmly.App.Tray;

/// <summary>Keeps the tray icon's tooltip and menu in step with the session, and acts on what's chosen there.</summary>
internal sealed class TrayController : IDisposable
{
    private const int OpenId = 1, ExitId = 2, SwitchBackId = 3, FirstModeId = 100, FirstDurationId = 200;

    private readonly SessionViewModel _session;
    private readonly TrayIcon _icon = new();
    private readonly string _name = Package.Current.DisplayName;
    private readonly nint _iconHandle;

    public TrayController(SessionViewModel session)
    {
        _session = session;
        _iconHandle = LoadAppIcon(_icon.Dpi);

        // The icon's events arrive inside its native window procedure. Act once that has returned: closing the
        // main window from in there fails, which made Exit do nothing.
        _icon.Selected += () => App.DispatcherQueue.TryEnqueue(App.ShowMainWindow);
        _icon.MenuRequested += OnMenuRequested;
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

    private void Update() => _icon.Update(_iconHandle, Tooltip());

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

    private static nint LoadAppIcon(uint dpi)
    {
        var size = GetSystemMetricsForDpi(SM_CXSMICON, dpi);
        return LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), IMAGE_ICON, size, size, LR_LOADFROMFILE);
    }
}
