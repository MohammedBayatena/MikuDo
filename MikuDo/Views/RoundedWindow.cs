using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MikuDo.Views;

/// <summary>
/// Rounds the corners of a borderless window.
///
/// Windows 11 can do this itself through DWM, which anti-aliases. Windows 10
/// cannot, so the window is clipped to a rounded region instead. The obvious
/// third option — AllowsTransparency with a rounded Border — is not usable here:
/// it makes the window layered, and layered windows do not composite the child
/// HWND that hosts the WebView2 markdown preview.
///
/// A maximised window gets square corners, the way every other window does.
/// </summary>
internal static class RoundedWindow
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmcpRound = 2;
    private const int DwmcpDoNotRound = 1;

    public static void Apply(Window window, double radius)
    {
        void Hook()
        {
            Update(window, radius);
            window.SizeChanged += (_, _) => Update(window, radius);
            window.StateChanged += (_, _) => Update(window, radius);
            window.DpiChanged += (_, _) => Update(window, radius);
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Hook();
        else window.SourceInitialized += (_, _) => Hook();
    }

    private static void Update(Window window, double radius)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var rounded = window.WindowState != WindowState.Maximized;

        if (TrySetDwmCorners(hwnd, rounded))
        {
            // DWM owns the corners; a region on top of it would only fight the
            // anti-aliasing.
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        if (!rounded)
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        if (!GetWindowRect(hwnd, out var bounds)) return;

        var scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
        var diameter = (int)Math.Round(radius * 2 * scale);

        // CreateRoundRectRgn treats right and bottom as exclusive.
        var region = CreateRoundRectRgn(
            0, 0,
            bounds.Right - bounds.Left + 1,
            bounds.Bottom - bounds.Top + 1,
            diameter, diameter);

        if (region != IntPtr.Zero) SetWindowRgn(hwnd, region, true);
    }

    /// <summary>Returns false on Windows 10, where the attribute does not exist.</summary>
    private static bool TrySetDwmCorners(IntPtr hwnd, bool rounded)
    {
        try
        {
            var preference = rounded ? DwmcpRound : DwmcpDoNotRound;
            return DwmSetWindowAttribute(
                hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int)) == 0;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom,
                                                    int widthEllipse, int heightEllipse);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
}
