using System.Text.Json;

namespace MyTelU_Launcher.Services;

internal static class SecureSettingsStore
{
    public static Dictionary<string, string> Load(string path)
    {
        try
        {
            var encryptedJson = SecureFileStore.Load(path);
            if (!string.IsNullOrWhiteSpace(encryptedJson))
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(encryptedJson)
                    ?? new Dictionary<string, string>();
            }

            if (!File.Exists(path))
                return new Dictionary<string, string>();

            var settings = new Dictionary<string, string>();
            Save(path, settings);
            return settings;
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    public static async Task<Dictionary<string, string>> LoadAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var encryptedJson = SecureFileStore.Load(path);
            if (!string.IsNullOrWhiteSpace(encryptedJson))
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(encryptedJson)
                    ?? new Dictionary<string, string>();
            }

            if (!File.Exists(path))
                return new Dictionary<string, string>();

            var settings = new Dictionary<string, string>();
            Save(path, settings);
            return settings;
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    public static void Save(string path, Dictionary<string, string> settings)
    {
        SecureFileStore.Save(path, JsonSerializer.Serialize(settings));
    }

    public static Task SaveAsync(string path, Dictionary<string, string> settings, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Save(path, settings);
        return Task.CompletedTask;
    }
}
