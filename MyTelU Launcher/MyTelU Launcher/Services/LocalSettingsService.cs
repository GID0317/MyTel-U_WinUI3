using Microsoft.Extensions.Options;
using MyTelU_Launcher.Contracts.Services;
using MyTelU_Launcher.Core.Contracts.Services;
using MyTelU_Launcher.Core.Helpers;
using MyTelU_Launcher.Helpers;
using MyTelU_Launcher.Models;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Windows.Storage;

namespace MyTelU_Launcher.Services;

public class LocalSettingsService : ILocalSettingsService
{
    private const string _defaultApplicationDataFolder = "MyTelU Launcher/ApplicationData";
    private const string _defaultLocalSettingsFile = "LocalSettings.json";
    private const string _protectedKeySuffix = ".Protected.v1";
    private static readonly byte[] s_entropy = "MyTelU-local-settings-v1"u8.ToArray();

    private readonly IFileService _fileService;
    private readonly LocalSettingsOptions _options;

    private readonly string _localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private readonly string _applicationDataFolder;
    private readonly string _localsettingsFile;

    private IDictionary<string, string> _settings;

    private bool _isInitialized;
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    private ApplicationDataContainer? _packageLocalSettings;

    private bool _packageLocalSettingsResolved;

    public LocalSettingsService(IFileService fileService, IOptions<LocalSettingsOptions> options)
    {
        _fileService = fileService;
        _options = options.Value;

        _applicationDataFolder = Path.Combine(_localApplicationData, _options.ApplicationDataFolder ?? _defaultApplicationDataFolder);
        _localsettingsFile = _options.LocalSettingsFile ?? _defaultLocalSettingsFile;

        _settings = new Dictionary<string, string>();
    }

    private async Task InitializeAsync()
    {
        if (!_isInitialized)
        {
            _settings = await Task.Run(() => _fileService.Read<IDictionary<string, string>>(_applicationDataFolder, _localsettingsFile)) ?? new Dictionary<string, string>();

            _isInitialized = true;
        }
    }

    public async Task<T?> ReadSettingAsync<T>(string key)
    {
        await _semaphore.WaitAsync();
        try
        {
            var packagedSettings = GetPackagedLocalSettings();
            if (packagedSettings != null)
            {
                if (packagedSettings.Values.TryGetValue(key, out var obj))
                {
                    return await Json.ToObjectAsync<T>((string)obj);
                }
            }

            await InitializeAsync();

            if (_settings != null && _settings.TryGetValue(key, out var fileValue))
            {
                return await Json.ToObjectAsync<T>(fileValue);
            }

            return default;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveSettingAsync<T>(string key, T? value)
    {
        await _semaphore.WaitAsync();
        try
        {
            var serializedValue = await Json.StringifyAsync(value);
            var packagedSettings = GetPackagedLocalSettings();

            if (packagedSettings != null)
            {
                packagedSettings.Values[key] = serializedValue;
            }

            await InitializeAsync();

            _settings[key] = serializedValue;

            await Task.Run(() => _fileService.Save(_applicationDataFolder, _localsettingsFile, _settings));
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<T?> ReadProtectedSettingAsync<T>(string key)
    {
        await _semaphore.WaitAsync();
        try
        {
            var protectedKey = GetProtectedKey(key);
            var protectedValue = await ReadRawSettingAsync(protectedKey);
            if (!string.IsNullOrWhiteSpace(protectedValue))
            {
                var json = Unprotect(protectedValue);
                if (json != null)
                    return await Json.ToObjectAsync<T>(json);
            }

            await RemoveRawSettingAsync(key);
            return default;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveProtectedSettingAsync<T>(string key, T? value)
    {
        await _semaphore.WaitAsync();
        try
        {
            await SaveProtectedSettingCoreAsync(key, value);
            await RemoveRawSettingAsync(key);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task SaveProtectedSettingCoreAsync<T>(string key, T? value)
    {
        var protectedKey = GetProtectedKey(key);

        if (value == null)
        {
            await RemoveRawSettingAsync(protectedKey);
            return;
        }

        var serializedValue = await Json.StringifyAsync(value);
        await SaveRawSettingAsync(protectedKey, Protect(serializedValue));
    }

    private async Task<string?> ReadRawSettingAsync(string key)
    {
        var packagedSettings = GetPackagedLocalSettings();
        if (packagedSettings != null && packagedSettings.Values.TryGetValue(key, out var obj))
            return obj as string;

        await InitializeAsync();

        return _settings.TryGetValue(key, out var fileValue)
            ? fileValue
            : null;
    }

    private async Task SaveRawSettingAsync(string key, string value)
    {
        var packagedSettings = GetPackagedLocalSettings();
        if (packagedSettings != null)
            packagedSettings.Values[key] = value;

        await InitializeAsync();
        _settings[key] = value;

        await Task.Run(() => _fileService.Save(_applicationDataFolder, _localsettingsFile, _settings));
    }

    private async Task RemoveRawSettingAsync(string key)
    {
        var packagedSettings = GetPackagedLocalSettings();
        packagedSettings?.Values.Remove(key);

        await InitializeAsync();
        if (_settings.Remove(key))
            await Task.Run(() => _fileService.Save(_applicationDataFolder, _localsettingsFile, _settings));
    }

    private static string GetProtectedKey(string key) => $"{key}{_protectedKeySuffix}";

    private static string Protect(string value)
    {
        var plain = Encoding.UTF8.GetBytes(value);
        var cipher = ProtectedData.Protect(plain, s_entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cipher);
    }

    private static string? Unprotect(string value)
    {
        try
        {
            var cipher = Convert.FromBase64String(value);
            var plain = ProtectedData.Unprotect(cipher, s_entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    private ApplicationDataContainer? GetPackagedLocalSettings()
    {
        if (_packageLocalSettingsResolved)
            return _packageLocalSettings;

        _packageLocalSettingsResolved = true;

        if (!RuntimeHelper.IsMSIX)
            return null;

        try
        {
            _packageLocalSettings = ApplicationData.Current.LocalSettings;
        }
        catch (InvalidOperationException)
        {
            _packageLocalSettings = null;
        }

        return _packageLocalSettings;
    }
}
