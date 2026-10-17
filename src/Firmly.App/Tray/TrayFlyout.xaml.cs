using System.Numerics;
using Firmly.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI.ViewManagement;
using static Firmly.App.Tray.NativeMethods;

namespace Firmly.App.Tray;

/// <summary>
/// The panel that opens by the tray icon when it's clicked, like Windows' own for Wi-Fi or volume: the charge, the
/// charge mode, and a full charge for a while. It hides as soon as anything else is clicked.
/// </summary>
public sealed unsafe partial class TrayFlyout : Window
{
    // Effective pixels: how far it keeps from the taskbar and the screen's edges.
    private const double Gap = 12;

    private RectInt32 _anchor;
    // The screen edge the taskbar is on: 0 top, 1 bottom, 2 left, 3 right.
    private int _edge = 1;
    private bool _closing;
    private bool _active;
    // How to focus the first control once the window is active; Unfocused when done.
    private FocusState _focusOnOpen;
    // When it last hid because something else was clicked. Clicking the tray icon does that first.
    private long _hiddenAt;

    public TrayFlyout()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            // Keeps the border, shadow and rounded corners.
            presenter.SetBorderAndTitleBar(true, false);
        }
        // Not on the taskbar or in Alt+Tab.
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Alt+F4 only hides it; it lives as long as the tray icon.
        AppWindow.Closing += (_, args) =>
        {
            if (!_closing)
            {
                args.Cancel = true;
                Hide();
            }
        };
        Activated += (_, args) =>
        {
            _active = args.WindowActivationState != WindowActivationState.Deactivated;
            // Focus can only move into the window once it's active. Without it, Esc and Tab do nothing.
            if (_active && _focusOnOpen != FocusState.Unfocused)
            {
                if (FocusManager.FindFirstFocusableElement(Panel) is Control first)
                    first.Focus(_focusOnOpen);
                _focusOnOpen = FocusState.Unfocused;
            }
            // The duration menu may take the focus while it's open; it closes too when something else is clicked.
            if (!_active && !DurationMenu.IsOpen)
                HideClickedAway();
        };
        DurationMenu.Closed += (_, _) =>
        {
            if (!_active)
                HideClickedAway();
        };
        // E.g. a timed charge starts, and its banner replaces the button.
        Panel.SizeChanged += (_, _) =>
        {
            if (IsOpen)
                Place();
        };
    }

    public SessionViewModel Session => App.Session;

    public string OpenText { get; } = $"Open {Package.Current.DisplayName}";

    public bool IsOpen => AppWindow.IsVisible;

    /// <summary>Opens next to the tray icon at <paramref name="anchor"/> (screen pixels), or closes if open.</summary>
    /// <param name="keyboard">Opened with the keyboard, so focus is shown.</param>
    public void Toggle(RectInt32 anchor, bool keyboard)
    {
        if (IsOpen)
        {
            Hide();
            return;
        }
        // The click that just hid it was on the icon: leave it closed.
        if (Environment.TickCount64 - _hiddenAt < 300)
            return;

        _anchor = anchor;
        // Pointer focus doesn't draw a focus rectangle, but still lets Esc and Tab work.
        _focusOnOpen = keyboard ? FocusState.Keyboard : FocusState.Pointer;
        Place();
        Activate();
        // Again now that it's shown: the invisible border's size can only be read from a visible window.
        Place();
        SlideIn();
        _ = Session.RefreshQuietlyAsync();
    }

    public void Hide() => AppWindow.Hide();

    private void HideClickedAway()
    {
        if (!IsOpen)
            return;
        Hide();
        _hiddenAt = Environment.TickCount64;
    }

    /// <summary>Closes the window for good, with the tray icon.</summary>
    public void CloseForGood()
    {
        _closing = true;
        Close();
    }

    /// <summary>Sizes the window to the panel and puts it by the icon, inside the screen's work area.</summary>
    private void Place()
    {
        var display = DisplayArea.GetFromRect(_anchor, DisplayAreaFallback.Nearest);
        uint dpiX = 96, dpiY = 96;
        GetDpiForMonitor(Win32Interop.GetMonitorFromDisplayId(display.DisplayId), MDT_EFFECTIVE_DPI, &dpiX, &dpiY);
        var scale = dpiX / 96.0;

        Panel.Measure(new Size(Panel.Width, double.PositiveInfinity));
        AppWindow.ResizeClient(new SizeInt32((int)Math.Ceiling(Panel.Width * scale), (int)Math.Ceiling(Panel.DesiredSize.Height * scale)));
        var gap = (int)Math.Round(Gap * scale);

        // The window has an invisible border on the sides and bottom (for its shadow): place what's visible.
        var hwnd = (nint)Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        RECT outer, visible;
        if (!GetWindowRect(hwnd, &outer)
            || DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &visible, (uint)sizeof(RECT)) < 0)
            outer = visible = default;
        var (width, height) = (visible.right - visible.left, visible.bottom - visible.top);
        if (width <= 0 || height <= 0)
            (width, height) = (AppWindow.Size.Width, AppWindow.Size.Height);

        // The taskbar is on the screen edge nearest the icon. Keep clear of it even while it auto-hides,
        // when the work area includes it.
        var work = display.WorkArea;
        var screen = display.OuterBounds;
        var (centerX, centerY) = (_anchor.X + _anchor.Width / 2, _anchor.Y + _anchor.Height / 2);
        int[] toEdge = [centerY - screen.Y, screen.Y + screen.Height - centerY, centerX - screen.X, screen.X + screen.Width - centerX];
        _edge = Array.IndexOf(toEdge, toEdge.Min());
        var (x, y) = _edge switch
        {
            0 => (centerX - width / 2, Math.Max(work.Y, _anchor.Y + _anchor.Height) + gap),
            2 => (Math.Max(work.X, _anchor.X + _anchor.Width) + gap, centerY - height / 2),
            3 => (Math.Min(work.X + work.Width, _anchor.X) - gap - width, centerY - height / 2),
            _ => (centerX - width / 2, Math.Min(work.Y + work.Height, _anchor.Y) - gap - height),
        };
        x = Math.Max(work.X + gap, Math.Min(x, work.X + work.Width - gap - width));
        y = Math.Max(work.Y + gap, Math.Min(y, work.Y + work.Height - gap - height));
        AppWindow.Move(new PointInt32(x - (visible.left - outer.left), y - (visible.top - outer.top)));
    }

    // Slides in from the taskbar, as Windows' own flyouts do, unless animations are turned off in Windows.
    private void SlideIn()
    {
        if (!new UISettings().AnimationsEnabled)
            return;

        ElementCompositionPreview.SetIsTranslationEnabled(Panel, true);
        var visual = ElementCompositionPreview.GetElementVisual(Panel);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0, _edge switch
        {
            0 => new Vector3(0, -24, 0),
            2 => new Vector3(-24, 0, 0),
            3 => new Vector3(24, 0, 0),
            _ => new Vector3(0, 24, 0),
        });
        slide.InsertKeyFrame(1, Vector3.Zero, ease);
        slide.Duration = TimeSpan.FromMilliseconds(250);
        visual.StartAnimation("Translation", slide);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, ease);
        fade.Duration = TimeSpan.FromMilliseconds(150);
        visual.StartAnimation("Opacity", fade);
    }

    public string ChargeOrNone(string charge) => charge.Length > 0 ? charge : "No battery reading";

    // The binding only ever selects the firmware's current mode (one-way), so another selection is the user's pick.
    private void OnModeSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Session.Charge.RequestMode(ModePicker.SelectedIndex);

    // Built on opening: it says which mode the battery goes back to.
    private void OnDurationMenuOpening(object? sender, object e)
    {
        var charge = Session.Charge;
        DurationMenu.Items.Clear();
        if (charge.RestoreIndex >= 0 && charge.RestoreIndex < charge.RestoreModes.Count)
        {
            DurationMenu.Items.Add(new MenuFlyoutItem { Text = $"Then back to {charge.RestoreModes[charge.RestoreIndex].Label}", IsEnabled = false });
            DurationMenu.Items.Add(new MenuFlyoutSeparator());
        }

        for (var i = 0; i < charge.Durations.Count; i++)
        {
            var index = i;
            var item = new MenuFlyoutItem { Text = charge.Durations[i].Label };
            item.Click += (_, _) =>
            {
                charge.DurationIndex = index;
                // A date is picked on the Battery page.
                if (charge.IsCustomDuration)
                {
                    Hide();
                    App.ShowBattery();
                    return;
                }
                charge.StartOverrideCommand.Execute(null);
            };
            if (charge.Durations[i].IsCustom)
                DurationMenu.Items.Add(new MenuFlyoutSeparator());
            DurationMenu.Items.Add(item);
        }
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        Hide();
        App.ShowMainWindow();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        Hide();
        App.ShowSettings();
    }

    private void OnPanelKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Hide();
        }
    }
}
