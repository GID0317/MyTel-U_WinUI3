using System;
using System.Diagnostics;
using System.IO;
using MyTelU_Launcher.Services;

namespace MyTelU_Launcher.Helpers;

public static class FeatureFlowLogger
{
    private static readonly string LogFile = AppDataStore.GetFilePath("feature_flow.log");

    public static void Write(string area, string message)
    {
        if (!DiagnosticLogging.LoggingEnabled)
            return;

        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{area}] {message}";
        Debug.WriteLine(line);
        DiagnosticLogging.AppendLine(LogFile, line, 400_000);
    }
}