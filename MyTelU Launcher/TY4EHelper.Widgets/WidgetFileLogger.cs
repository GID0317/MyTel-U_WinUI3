using System.Diagnostics;
using System.Text;

namespace TY4EHelper.Widgets;

internal static class WidgetFileLogger
{
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = WidgetAppDataStore.DirectoryPath;

    internal static readonly string LogPath = Path.Combine(LogDirectory, "widget_provider.log");

    public static void Write(string area, string message)
    {
        if (!WidgetDiagnostics.LoggingEnabled)
            return;

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{area}] {message}";
        Debug.WriteLine(line);

        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);

                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 750_000)
                {
                    var archivedPath = Path.Combine(LogDirectory, "widget_provider.previous.log");
                    if (File.Exists(archivedPath))
                    {
                        File.Delete(archivedPath);
                    }

                    File.Move(LogPath, archivedPath);
                }

                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
        }
    }

    public static void WriteException(string area, Exception ex, string? context = null)
    {
        if (!WidgetDiagnostics.LoggingEnabled)
            return;

        var baseMessage = context is null
            ? $"{ex.GetType().Name}: {ex.Message}"
            : $"{context} | {ex.GetType().Name}: {ex.Message}";
        Write(area, baseMessage);

        try
        {
            lock (SyncRoot)
            {
                File.AppendAllText(LogPath, ex + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
        }
    }
}