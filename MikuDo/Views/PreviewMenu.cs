using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MikuDo.Controls;
using MikuDo.Services;

namespace MikuDo.Views;

/// <summary>
/// The right-click menu of a Markdown preview, drawn as the app's own menus:
/// Copy and Select all, then whatever the page adds. The browser's own menu
/// never shows. Copy is the browser's own command, so text keeps its
/// formatting where it is pasted.
/// </summary>
public static class PreviewMenu
{
    /// <summary>
    /// Gives <paramref name="web"/> the menu. <paramref name="more"/> is asked
    /// for the page's own rows each time it opens.
    /// </summary>
    public static void Attach(WebView2 web, Func<object?[]> more)
    {
        var core = web.CoreWebView2;
        // With the browser's menus switched off it never asks for one at all.
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.ContextMenuRequested += (_, args) => Open(web, args, more);
    }

    private static void Open(WebView2 web, CoreWebView2ContextMenuRequestedEventArgs args, Func<object?[]> more)
    {
        args.Handled = true;
        var deferral = args.GetDeferral();
        var finished = false;

        // The browser runs the command it was handed once the deferral completes.
        void Finish(int? command)
        {
            if (finished) return;
            finished = true;
            if (command is { } id) args.SelectedCommandId = id;
            deferral.Complete();
        }

        var target = args.ContextMenuTarget;
        CoreWebView2ContextMenuItem? Native(string name) => args.MenuItems.FirstOrDefault(i => i.Name == name);

        MenuItem Browser(string header, string name, Action? otherwise, string? gesture = null)
            => Menus.Item(header, () =>
            {
                if (Native(name) is { } item) Finish(item.CommandId);
                else otherwise?.Invoke();
            }, gesture);

        var items = new List<object?>();
        if (target.HasSelection)
            items.Add(Browser("Copy", "copy", () => SetClipboard(target.SelectionText), "Ctrl+C"));
        if (target.HasLinkUri && target.LinkUri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            items.Add(Menus.Item("Copy link address", () => SetClipboard(target.LinkUri)));
        if (target.Kind == CoreWebView2ContextMenuTargetKind.Image && Native("copyImage") != null)
            items.Add(Browser("Copy image", "copyImage", null));
        items.Add(Menus.Item("Select all", () =>
            _ = web.CoreWebView2.ExecuteScriptAsync("document.getSelection().selectAllChildren(document.body)"), "Ctrl+A"));
        items.Add(Menus.Gap);
        items.AddRange(more());

        var menu = Menus.OpenAt(web, Pointer(web), items.ToArray());
        if (menu == null)
        {
            Finish(null);
            return;
        }
        // A row's click runs after the menu has closed, so the menu waits for it before letting the browser go.
        menu.Closed += (_, _) => web.Dispatcher.BeginInvoke(() => Finish(null), DispatcherPriority.ContextIdle);
    }

    private static void SetClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (COMException ex) { LogService.Error("The clipboard was busy", ex); }
    }

    /// <summary>Where the pointer is, in <paramref name="web"/>'s own coordinates.</summary>
    private static Point Pointer(WebView2 web)
    {
        if (!GetCursorPos(out var screen)) return new Point(web.ActualWidth / 2, web.ActualHeight / 2);
        return web.PointFromScreen(new Point(screen.X, screen.Y));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
}
