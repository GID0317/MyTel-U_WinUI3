using System.Security;

using MyTelU_Launcher.Contracts.Services;
using Windows.ApplicationModel;

namespace MyTelU_Launcher.Services;

public class UpdateNotificationService : IUpdateNotificationService
{
    private const string LastSeenVersionKey = "PostUpdateNotificationLastSeenVersion";
    private const string LastSeenPackageStampKey = "PostUpdateNotificationLastSeenPackageStamp";

    private readonly ILocalSettingsService _localSettingsService;
    private readonly IAppNotificationService _appNotificationService;

    public UpdateNotificationService(
        ILocalSettingsService localSettingsService,
        IAppNotificationService appNotificationService)
    {
        _localSettingsService = localSettingsService;
        _appNotificationService = appNotificationService;
    }

    public async Task ShowPostUpdateNotificationAsync()
    {
        var currentVersion = FormatVersion(App.AppVersion);
        if (string.IsNullOrWhiteSpace(currentVersion))
            return;

        var currentPackageStamp = GetCurrentPackageStamp();
        var lastSeenVersion = await _localSettingsService.ReadSettingAsync<string>(LastSeenVersionKey);
        var lastSeenPackageStamp = await _localSettingsService.ReadSettingAsync<string>(LastSeenPackageStampKey);

        if (string.IsNullOrWhiteSpace(lastSeenVersion))
        {
            if (HasExistingUserData())
                _appNotificationService.Show(BuildPayload(currentVersion));

            await SaveNotificationMarkerAsync(currentVersion, currentPackageStamp);
            return;
        }

        if (HasAlreadyShownForThisPackage(lastSeenVersion, lastSeenPackageStamp, currentVersion, currentPackageStamp))
            return;

        _appNotificationService.Show(BuildPayload(currentVersion));
        await SaveNotificationMarkerAsync(currentVersion, currentPackageStamp);
    }

    private async Task SaveNotificationMarkerAsync(string currentVersion, string currentPackageStamp)
    {
        await _localSettingsService.SaveSettingAsync(LastSeenVersionKey, currentVersion);

        if (!string.IsNullOrWhiteSpace(currentPackageStamp))
            await _localSettingsService.SaveSettingAsync(LastSeenPackageStampKey, currentPackageStamp);
    }

    private static bool HasAlreadyShownForThisPackage(
        string? lastSeenVersion,
        string? lastSeenPackageStamp,
        string currentVersion,
        string currentPackageStamp)
    {
        if (!string.Equals(lastSeenVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(currentPackageStamp))
            return true;

        return string.Equals(lastSeenPackageStamp, currentPackageStamp, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPayload(string currentVersion)
    {
        var escapedVersion = SecurityElement.Escape(currentVersion);

        return $$"""
        <toast launch="action=settings">
          <visual>
            <binding template="ToastGeneric">
              <image placement="hero" src="ms-appx:///Assets/Img_Background.png"/>
              <text>MyTel-U is up-to-date!</text>
              <text>MyTelU Launcher has been updated to: {{escapedVersion}}.</text>
              <text>Go to &quot;Settings&quot; to check for future updates.</text>
            </binding>
          </visual>
          <actions>
            <action content="Open Settings" arguments="action=settings"/>
          </actions>
        </toast>
        """;
    }

    private static bool HasExistingUserData()
    {
        var knownDataFiles = new[]
        {
            "cookies.json",
            "schedule_cache.json",
            "grade_cache.json",
            "grade_component_cache.json",
            "attendance_cache.json",
            "settings.json"
        };

        return knownDataFiles
            .Select(fileName => AppDataStore.GetFilePath(fileName))
            .Any(File.Exists);
    }

    private static string FormatVersion(Version version)
    {
        if (version.Revision > 0)
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";

        return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string GetCurrentPackageStamp()
    {
        try
        {
            var installPath = Package.Current.InstalledLocation?.Path;
            if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
                return string.Empty;

            var creationTime = Directory.GetCreationTimeUtc(installPath);
            var writeTime = Directory.GetLastWriteTimeUtc(installPath);
            var packageTime = creationTime > writeTime ? creationTime : writeTime;

            return $"{installPath}|{packageTime.Ticks}";
        }
        catch
        {
            return string.Empty;
        }
    }
}
