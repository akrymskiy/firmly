using System.Runtime.InteropServices;

namespace Firmly.App.Tray;

/// <summary>
/// The small badge in the corner of a window's taskbar button (ITaskbarList3.SetOverlayIcon). A packaged app's
/// taskbar button always shows its package logo, so this is how the taskbar can show the battery too.
/// </summary>
/// <remarks>Called through the COM vtable directly, which needs neither built-in COM interop nor generated wrappers.</remarks>
internal static unsafe partial class TaskbarOverlay
{
    private static readonly Guid TaskbarListClass = new("56FDF344-FD6D-11d0-958A-006097C9A090");
    private static readonly Guid TaskbarList3 = new("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF");

    // Slots in ITaskbarList3's vtable: IUnknown (0–2), ITaskbarList (3–7), ITaskbarList2 (8), then ITaskbarList3.
    private const int ReleaseSlot = 2, HrInitSlot = 3, SetOverlayIconSlot = 18;

    private static nint s_taskbar;

    /// <summary>Shows <paramref name="icon"/> on <paramref name="hwnd"/>'s taskbar button; 0 removes it. Best effort.</summary>
    /// <param name="description">Read out by screen readers.</param>
    public static void Set(nint hwnd, nint icon, string description)
    {
        var taskbar = Taskbar();
        if (taskbar == 0)
            return;

        var vtable = *(nint**)taskbar;
        fixed (char* text = description)
            ((delegate* unmanaged[Stdcall]<nint, nint, nint, char*, int>)vtable[SetOverlayIconSlot])(taskbar, hwnd, icon, text);
    }

    private static nint Taskbar()
    {
        if (s_taskbar != 0)
            return s_taskbar;

        nint taskbar;
        var classId = TaskbarListClass;
        var interfaceId = TaskbarList3;
        if (CoCreateInstance(&classId, 0, ClsctxInprocServer, &interfaceId, &taskbar) < 0 || taskbar == 0)
            return 0;

        var vtable = *(nint**)taskbar;
        if (((delegate* unmanaged[Stdcall]<nint, int>)vtable[HrInitSlot])(taskbar) < 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)vtable[ReleaseSlot])(taskbar);
            return 0;
        }
        return s_taskbar = taskbar;
    }

    private const uint ClsctxInprocServer = 1;

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* classId, nint outer, uint context, Guid* interfaceId, nint* result);
}
