using System.Security.Cryptography;
using System.Text;

namespace MyTelU_Launcher.Services;

/// <summary>
/// Encrypts arbitrary JSON blobs with Windows DPAPI (CurrentUser scope)
/// before writing to disk. Used for personal data that is not a secret
/// credential — e.g. the cached schedule.
/// </summary>
internal static class SecureFileStore
{
    private static readonly byte[] s_entropy = "MyTelU-secure-store-v1"u8.ToArray();

    public static void Save(string path, string json)
    {
        var plain  = Encoding.UTF8.GetBytes(json);
        var cipher = ProtectedData.Protect(plain, s_entropy, DataProtectionScope.CurrentUser);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tempPath, cipher);
        File.Move(tempPath, path, true);
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
                return null;
            }
        }
        catch { return null; }
    }
}
