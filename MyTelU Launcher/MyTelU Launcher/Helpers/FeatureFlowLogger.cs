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
#if DEBUG
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{area}] {message}";
        Debug.WriteLine(line);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 400_000)
                File.Delete(LogFile);
            File.AppendAllText(LogFile, line + Environment.NewLine);
        }
        catch
        {
        }
#endif
    }
}