using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyTelU_Launcher.Services;

/// <summary>
/// Saves and loads the user's iGracias credentials using Windows DPAPI
/// (ProtectedData, CurrentUser scope). The raw password is never stored
/// as plain text on disk — the binary blob can only be decrypted by the
/// same Windows user account on the same machine.
/// </summary>
internal static class CredentialStore
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TY4EHelper", "credentials.dat");

    private static readonly byte[] s_entropy = "MyTelU-iGracias-creds-v1"u8.ToArray();

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>Encrypts the credentials and writes them to disk.</summary>
    public static void Save(string username, string password)
    {
        var json   = JsonSerializer.Serialize(new { username, password });
        var plain  = Encoding.UTF8.GetBytes(json);
        var cipher = ProtectedData.Protect(plain, s_entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllBytes(_path, cipher);
    }

    /// <summary>
    /// Decrypts and returns the stored credentials, or (null, null) if missing or unreadable.
    /// </summary>
    public static (string? Username, string? Password) Load()
    {
        if (!File.Exists(_path)) return (null, null);
        try
        {
            var cipher = File.ReadAllBytes(_path);
            var plain  = ProtectedData.Unprotect(cipher, s_entropy, DataProtectionScope.CurrentUser);
            var doc    = JsonDocument.Parse(plain);
            return (
                doc.RootElement.GetProperty("username").GetString(),
                doc.RootElement.GetProperty("password").GetString()
            );
        }
        catch { return (null, null); }
    }

    /// <summary>True when saved credentials are available on disk.</summary>
    public static bool HasCredentials()
    {
        var (u, p) = Load();
        return !string.IsNullOrEmpty(u) && !string.IsNullOrEmpty(p);
    }

    /// <summary>Deletes the credentials file from disk. Safe to call even if no file exists.</summary>
    public static void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }
}
