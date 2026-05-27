using System.Security.Cryptography;
using System.Text;

namespace MyTelU_Launcher.Services;

/// <summary>
/// Saves and loads the iGracias session cookies using Windows DPAPI
/// (ProtectedData, CurrentUser scope). The raw PHPSESSID is never stored
/// as plain text on disk — the binary blob can only be decrypted by the
/// same Windows user account on the same machine.
/// </summary>
internal static class CookieStore
{
    private static readonly string _cookiesFile = AppDataStore.GetFilePath("cookies.json");

    // Application-specific entropy bound to this app's context.
    private static readonly byte[] s_entropy = "MyTelU-iGracias-cookies-v1"u8.ToArray();

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Encrypts <paramref name="json"/> and writes it to disk.</summary>
    public static void Save(string json)
    {
        var plain  = Encoding.UTF8.GetBytes(json);
        var cipher = ProtectedData.Protect(plain, s_entropy, DataProtectionScope.CurrentUser);
        var directory = Path.GetDirectoryName(_cookiesFile)!;
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $"{Path.GetFileName(_cookiesFile)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tempPath, cipher);
        File.Move(tempPath, _cookiesFile, true);
    }

    /// <summary>Async overload — DPAPI is synchronous, so this offloads to a thread-pool thread.</summary>
    public static Task SaveAsync(string json, CancellationToken ct = default)
        => Task.Run(() => Save(json), ct);

    /// <summary>
    /// Decrypts and returns the stored cookie JSON, or <c>null</c> if missing/unreadable.
    /// </summary>
    public static string? Load()
    {
        if (!File.Exists(_cookiesFile)) return null;
        try
        {
            var bytes = File.ReadAllBytes(_cookiesFile);
            try
            {
                var plain = ProtectedData.Unprotect(bytes, s_entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (CryptographicException)
            {
                return null;
            }
        }
        catch { return null; }
    }

    /// <summary>
    /// Deletes the cookies file from disk.
    /// Safe to call even if no file exists.
    /// </summary>
    public static void Clear()
    {
        try { if (File.Exists(_cookiesFile)) File.Delete(_cookiesFile); } catch { }
    }
}
