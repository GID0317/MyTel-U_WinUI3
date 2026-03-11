namespace TY4EHelper.Widgets;

internal static class WidgetAppDataStore
{
    private static readonly string s_localAppDataPath =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static readonly string s_rootDirectoryPath = Path.Combine(
        s_localAppDataPath,
        "MyTelU_Launcher");

    private static readonly string s_directoryPath = Path.Combine(
        s_rootDirectoryPath,
        "TY4EHelper");

    private static readonly string s_legacyDirectoryPath = Path.Combine(
        s_localAppDataPath,
        "TY4EHelper");

    private static int s_initialized;

    public static string DirectoryPath
    {
        get
        {
            EnsureInitialized();
            return s_directoryPath;
        }
    }

    public static string GetFilePath(params string[] segments)
    {
        EnsureInitialized();

        var path = s_directoryPath;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }

    private static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref s_initialized, 1) == 1)
            return;

        try
        {
            Directory.CreateDirectory(s_rootDirectoryPath);
            MigrateLegacyDirectory();
            Directory.CreateDirectory(s_directoryPath);
        }
        catch
        {
            try { Directory.CreateDirectory(s_directoryPath); } catch { }
        }
    }

    private static void MigrateLegacyDirectory()
    {
        if (!Directory.Exists(s_legacyDirectoryPath) || PathsEqual(s_legacyDirectoryPath, s_directoryPath))
            return;

        if (!Directory.Exists(s_directoryPath))
        {
            Directory.Move(s_legacyDirectoryPath, s_directoryPath);
            return;
        }

        MergeDirectoryContents(s_legacyDirectoryPath, s_directoryPath);

        try { Directory.Delete(s_legacyDirectoryPath, true); } catch { }
    }

    private static void MergeDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var sourceFile in Directory.GetFiles(sourceDirectory))
        {
            var destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
            File.Move(sourceFile, destinationFile, overwrite: true);
        }

        foreach (var sourceSubdirectory in Directory.GetDirectories(sourceDirectory))
        {
            var destinationSubdirectory = Path.Combine(destinationDirectory, Path.GetFileName(sourceSubdirectory));
            MergeDirectoryContents(sourceSubdirectory, destinationSubdirectory);
            try { Directory.Delete(sourceSubdirectory, true); } catch { }
        }
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}