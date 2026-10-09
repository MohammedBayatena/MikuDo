using System.IO;
using System.Text;

namespace MikuDo.Services;

/// <summary>
/// Appends errors to a file next to the database.
/// </summary>
/// <remarks>
/// A crash in a released desktop app leaves nothing behind but whatever was
/// written before it, so this stays deliberately plain: no dependency, no
/// buffering, and every failure inside it swallowed. A logger that throws
/// while reporting a crash hides the crash.
/// </remarks>
public static class LogService
{
    /// <summary>Beyond this the file is rolled, so it cannot grow without end.</summary>
    private const long MaxBytes = 2 * 1024 * 1024;

    private static readonly object Gate = new();

    public static string FilePath { get; } = BuildPath();

    private static string BuildPath()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mikudo");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "mikudo.log");
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), "mikudo.log");
        }
    }

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Error(string context, Exception? error) => Write("ERROR", context, error);

    private static void Write(string level, string message, Exception? error)
    {
        var text = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append("  ").Append(level).Append("  ").AppendLine(message);

        for (var ex = error; ex != null; ex = ex.InnerException)
        {
            text.Append("    ").Append(ex.GetType().FullName).Append(": ").AppendLine(ex.Message);
            if (!string.IsNullOrWhiteSpace(ex.StackTrace)) text.AppendLine(ex.StackTrace);
            if (ex.InnerException != null) text.AppendLine("    --- caused by ---");
        }

        lock (Gate)
        {
            try
            {
                Roll();
                File.AppendAllText(FilePath, text.ToString());
            }
            catch
            {
                // Nothing useful is left to do: the report itself is what failed.
            }
        }
    }

    private static void Roll()
    {
        var file = new FileInfo(FilePath);
        if (!file.Exists || file.Length < MaxBytes) return;

        var previous = FilePath + ".1";
        File.Delete(previous);
        File.Move(FilePath, previous);
    }
}
