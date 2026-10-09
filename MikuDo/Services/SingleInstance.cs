using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MikuDo.Services;

/// <summary>
/// One MikuDo at a time. A second start hands the file it was given to the
/// first and quits: two copies would share one database, and "Open with
/// MikuDo" should open the note in the window already there.
/// </summary>
public static class SingleInstance
{
    private static Mutex? _mutex;

    private static string Name(string what) => $"MikuDo.{what}.{Environment.UserName}";

    /// <summary>True for the first MikuDo; otherwise <paramref name="args"/> have gone to it.</summary>
    public static bool TryBecomeFirst(string[] args)
    {
        _mutex = new Mutex(true, @"Local\" + Name("Instance"), out var first);
        if (first) return true;

        _mutex.Dispose();
        _mutex = null;
        try
        {
            // The first copy may bring its window forward only with leave from this one.
            AllowSetForegroundWindow(-1);
            using var pipe = new NamedPipeClientStream(".", Name("Open"), PipeDirection.Out);
            pipe.Connect(3000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
            writer.Write(args.FirstOrDefault() ?? string.Empty);
        }
        catch (Exception ex)
        {
            LogService.Error("Could not reach the MikuDo already running", ex);
        }
        return false;
    }

    /// <summary>
    /// Passes what each later start sends to <paramref name="received"/>: a
    /// file's path, or nothing when it was started without one. Called off the
    /// UI thread.
    /// </summary>
    public static void Listen(Action<string> received)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(Name("Open"), PipeDirection.In, 1, PipeTransmissionMode.Byte);
                    pipe.WaitForConnection();
                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    received(reader.ReadToEnd().Trim());
                }
                catch (Exception ex)
                {
                    LogService.Error("A later start could not hand over", ex);
                    Thread.Sleep(1000);
                }
            }
        })
        {
            IsBackground = true,
            Name = "MikuDo hand-over"
        };
        thread.Start();
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}

/// <summary>
/// Puts MikuDo under Explorer's "Open with" for Markdown files, for this user
/// only. It never makes MikuDo the default app for them.
/// </summary>
public static class OpenWith
{
    private const string ProgId = "MikuDo.Markdown";

    public static void Register()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null || !Path.GetFileName(exe).Equals("MikuDo.exe", StringComparison.OrdinalIgnoreCase)) return;

            var command = $"\"{exe}\" \"%1\"";
            using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
            var changed = false;

            changed |= Set(classes, ProgId, null, "Markdown note");
            changed |= Set(classes, ProgId + @"\DefaultIcon", null, $"\"{exe}\",0");
            changed |= Set(classes, ProgId + @"\shell\open\command", null, command);
            changed |= Set(classes, @"Applications\MikuDo.exe", "FriendlyAppName", "MikuDo");
            changed |= Set(classes, @"Applications\MikuDo.exe\shell\open\command", null, command);

            foreach (var extension in NoteFiles.Extensions)
            {
                changed |= Set(classes, @"Applications\MikuDo.exe\SupportedTypes", extension, string.Empty);
                using var list = classes.CreateSubKey(extension + @"\OpenWithProgids");
                if (list.GetValue(ProgId) == null)
                {
                    list.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
                    changed = true;
                }
            }

            // Explorer caches what it offers; this tells it to look again.
            if (changed) SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            LogService.Error("Could not add MikuDo to Open with", ex);
        }
    }

    /// <summary>Writes a value only when it differs. True when it wrote.</summary>
    private static bool Set(RegistryKey root, string path, string? name, string value)
    {
        using var key = root.CreateSubKey(path);
        if (key.GetValue(name) as string == value) return false;
        key.SetValue(name, value);
        return true;
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
}
