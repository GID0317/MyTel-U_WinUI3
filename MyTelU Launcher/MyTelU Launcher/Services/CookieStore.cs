using System.Security.Cryptography;
using System.Text;

namespace MyTelU_Launcher.Services;

/// <summary>
/// Saves and loads the iGracias session cookies using Windows DPAPI
/// (ProtectedData, CurrentUser scope). The raw PHPSESSID is never stored
/// as plain text on disk — the binary blob can only be decrypted by the
/// same Windows user account on the same machine.
///
/// Migration: plain-text cookies.json files written before encryption was
/// added are automatically re-encrypted on first read so existing installs
/// upgrade silently.
/// </summary>
internal static class CookieStore
{
    private static readonly string _cookiesFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TY4EHelper", "cookies.json");

    // Application-specific entropy bound to this app's context.
    private static readonly byte[] s_entropy = "MyTelU-iGracias-cookies-v1"u8.ToArray();

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Encrypts <paramref name="json"/> and writes it to disk.</summary>
    public static void Save(string json)
    {
        var plain  = Encoding.UTF8.GetBytes(json);
        var cipher = ProtectedData.Protect(plain, s_entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_cookiesFile)!);
        File.WriteAllBytes(_cookiesFile, cipher);
    }

    /// <summary>Async overload — DPAPI is synchronous, so this offloads to a thread-pool thread.</summary>
    public static Task SaveAsync(string json, CancellationToken ct = default)
        => Task.Run(() => Save(json), ct);

    /// <summary>
    /// Decrypts and returns the stored cookie JSON, or <c>null</c> if missing/unreadable.
    /// Plain-text files from older installs are re-encrypted automatically.
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
                // Legacy plain-text install — re-encrypt transparently.
                var legacy = Encoding.UTF8.GetString(bytes).Trim();
                if (legacy.StartsWith("{"))
                {
                    try { Save(legacy); } catch { /* best-effort */ }
                    return legacy;
                }
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
