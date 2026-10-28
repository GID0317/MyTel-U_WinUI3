using System.Diagnostics;

namespace MyTelU_Launcher.Services;

internal static class IGraciasSessionGate
{
    // iGracias stores page permissions in the shared PHP session. Keep each
    // page-and-AJAX sequence together, including requests from other app processes.
    public static async Task<FileStream> EnterAsync(string directory, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "igracias-session.lock");
        var timer = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33 && timer.Elapsed < TimeSpan.FromSeconds(90))
            {
                await Task.Delay(50, ct);
            }
        }
    }
}
