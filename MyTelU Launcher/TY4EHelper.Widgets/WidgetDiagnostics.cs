using System.Diagnostics;
using System.Linq;

namespace TY4EHelper.Widgets;

internal static class WidgetDiagnostics
{
    private static readonly DefaultTraceListener DefaultListener = new();

    // Flip this to true in code when you need widget-side diagnostics again.
    internal static bool LoggingEnabled { get; set; } = false;

    internal static void ApplyRuntimeConfiguration()
    {
        if (LoggingEnabled)
        {
            EnsureListener(Trace.Listeners);
            return;
        }

        Trace.Listeners.Clear();
        CleanupDisabledArtifacts();
    }

    private static void EnsureListener(TraceListenerCollection listeners)
    {
        if (!listeners.OfType<DefaultTraceListener>().Any())
            listeners.Add(DefaultListener);
    }

    private static void CleanupDisabledArtifacts()
    {
        DeleteIfExists(WidgetAppDataStore.GetFilePath("widget_provider.log"));
        DeleteIfExists(WidgetAppDataStore.GetFilePath("widget_provider.previous.log"));
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