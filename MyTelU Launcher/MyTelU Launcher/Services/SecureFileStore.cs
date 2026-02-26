using System.Security.Cryptography;
using System.Text;

namespace MyTelU_Launcher.Services;

/// <summary>
/// Encrypts arbitrary JSON blobs with Windows DPAPI (CurrentUser scope)
/// before writing to disk. Used for personal data that is not a secret
/// credential — e.g. the cached schedule.
///
/// Migration: plain-text files written before encryption was added are
/// automatically re-encrypted on first read so existing installs upgrade silently.
/// </summary>
internal static class SecureFileStore
{
    private static readonly byte[] s_entropy = "MyTelU-secure-store-v1"u8.ToArray();

    public static void Save(string path, string json)
    {
        var plain  = Encoding.UTF8.GetBytes(json);
        var cipher = ProtectedData.Protect(plain, s_entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, cipher);
    }

    public static string? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            try
            {
                var plain = ProtectedData.Unprotect(bytes, s_entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (CryptographicException)
            {
                // Legacy plain-text file — re-encrypt transparently
                var text = Encoding.UTF8.GetString(bytes).Trim();
                if (text.StartsWith("{") || text.StartsWith("["))
                {
                    try { Save(path, text); } catch { /* best-effort */ }
                    return text;
                }
                return null; // Unrecognised format — treat as missing
            }
        }
        catch { return null; }
    }
}
