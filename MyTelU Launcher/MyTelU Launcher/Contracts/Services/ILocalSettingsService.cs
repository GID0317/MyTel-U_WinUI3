namespace MyTelU_Launcher.Contracts.Services;

public interface ILocalSettingsService
{
    Task<T?> ReadSettingAsync<T>(string key);

    Task SaveSettingAsync<T>(string key, T? value);

    Task<T?> ReadProtectedSettingAsync<T>(string key);

    Task SaveProtectedSettingAsync<T>(string key, T? value);
}
