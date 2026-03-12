using System.Diagnostics;
using System.Linq;
using MyTelU_Launcher.Services;

namespace MyTelU_Launcher.Helpers;

public static class DiagnosticLogging
{
    private static readonly DefaultTraceListener DefaultListener = new();

    // Flip this to true in code when you need app-side diagnostics again.
    public static bool LoggingEnabled { get; set; } = false;

    public static void ApplyRuntimeConfiguration()
    {
        if (LoggingEnabled)
        {
            EnsureListener(Trace.Listeners);
            return;
        }

        Trace.Listeners.Clear();
        CleanupDisabledArtifacts();
    }

    public static void AppendLine(string filePath, string line, long maxBytes = 0)
    {
        if (!LoggingEnabled)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            if (maxBytes > 0 && File.Exists(filePath) && new FileInfo(filePath).Length > maxBytes)
                File.Delete(filePath);

            File.AppendAllText(filePath, line + Environment.NewLine);
        }
        catch
        {
        }
    }

    private static void EnsureListener(TraceListenerCollection listeners)
    {
        if (!listeners.OfType<DefaultTraceListener>().Any())
            listeners.Add(DefaultListener);
    }

    private static void CleanupDisabledArtifacts()
    {
        DeleteIfExists(AppDataStore.GetFilePath("feature_flow.log"));
        DeleteIfExists(AppDataStore.GetFilePath("silent_login.log"));
        DeleteIfExists(AppDataStore.GetFilePath("academic_years_debug.log"));
        DeleteIfExists(AppDataStore.GetFilePath("academic_years_page.html"));
        DeleteIfExists(AppDataStore.GetFilePath("schedule_debug.log"));
        DeleteIfExists(AppDataStore.GetFilePath("crash.log"));
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}