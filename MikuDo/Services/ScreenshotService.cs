using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace MikuDo.Services;

public static class ScreenshotService
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    public static byte[] CaptureScreen(Window? appWindow = null, bool allScreens = false)
    {
        WindowState? prevState = null;
        if (appWindow != null)
        {
            prevState = appWindow.WindowState;
            appWindow.WindowState = WindowState.Minimized;

            for (int i = 0; i < 10; i++)
            {
                Application.Current.Dispatcher.Invoke(
                    DispatcherPriority.Background, new Action(() => { }));
                Thread.Sleep(50);
            }
        }

        try
        {
            int left, top, width, height;

            if (allScreens)
            {
                left = GetSystemMetrics(SM_XVIRTUALSCREEN);
                top = GetSystemMetrics(SM_YVIRTUALSCREEN);
                width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
                height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            }
            else
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                var hMonitor = MonitorFromWindow(IntPtr.Zero, MONITOR_DEFAULTTOPRIMARY);
                GetMonitorInfo(hMonitor, ref mi);
                left = mi.rcMonitor.Left;
                top = mi.rcMonitor.Top;
                width = mi.rcMonitor.Right - mi.rcMonitor.Left;
                height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
            }

            if (width <= 0 || height <= 0)
                return Array.Empty<byte>();

            using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height));
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch
        {
            return Array.Empty<byte>();
        }
        finally
        {
            if (appWindow != null && prevState.HasValue)
            {
                appWindow.WindowState = prevState.Value;
                appWindow.Activate();
            }
        }
    }
}
