using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using MyTelU_Launcher.Contracts.Services;
using MyTelU_Launcher.Helpers;
using MyTelU_Launcher.Views;
using Windows.Networking.Connectivity;
using Windows.Storage;
using System.Text.Json;

namespace MyTelU_Launcher.ViewModels
{
    public partial class ShellViewModel : ObservableRecipient
    {
        [ObservableProperty]
        private bool isBackEnabled;

        [ObservableProperty]
        private object? selected;

        [ObservableProperty]
        private BitmapImage? backgroundImage;

        // By default, no update is available (InfoBar collapsed).
        [ObservableProperty]
        private bool isUpdateAvailable;

        private readonly ILocalSettingsService _localSettingsService;

        public INavigationService NavigationService
        {
            get;
        }
        public INavigationViewService NavigationViewService
        {
            get;
        }

        public ShellViewModel(
            INavigationService navigationService,
            INavigationViewService navigationViewService,
            ILocalSettingsService localSettingsService,
            SettingsViewModel settingsViewModel)
        {
            NavigationService = navigationService;
            NavigationService.Navigated += OnNavigated;
            NavigationViewService = navigationViewService;
            _localSettingsService = localSettingsService;

            // Register for background image update messages.
            WeakReferenceMessenger.Default.Register<BackgroundImageChangedMessage>(this, async (r, m) =>
            {
                await UpdateBackgroundImageAsync(m.Value);
            });

            // Listen for background image settings changes.
            settingsViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.IsCustomImageBGEnabled))
                {
                    LoadBackgroundImageAsync();
                }
            };

            LoadBackgroundImageAsync();

            // Perform emergency update check at startup.
            _ = CheckEmergencyUpdateAsync();
        }

        private void OnNavigated(object sender, NavigationEventArgs e)
        {
            IsBackEnabled = NavigationService.CanGoBack;

            if (e.SourcePageType == typeof(SettingsPage))
            {
                Selected = NavigationViewService.SettingsItem;
                return;
            }

            var selectedItem = NavigationViewService.GetSelectedItem(e.SourcePageType);
            if (selectedItem != null)
            {
                Selected = selectedItem;
            }
        }

        private async void LoadBackgroundImageAsync()
        {
            var isCustomEnabled = await _localSettingsService.ReadSettingAsync<bool>("IsCustomImageBGEnabled");
            if (isCustomEnabled)
            {
                var imagePath = await _localSettingsService.ReadSettingAsync<string>("CustomBackgroundImagePath");
                await UpdateBackgroundImageAsync(imagePath);
            }
            else
            {
                await UpdateBackgroundImageAsync(string.Empty);
            }
        }

        private async Task UpdateBackgroundImageAsync(string imagePath)
        {
            BitmapImage bitmap = null;
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(imagePath);
                    using (var stream = await file.OpenAsync(FileAccessMode.Read))
                    {
                        bitmap = new BitmapImage();
                        await bitmap.SetSourceAsync(stream);
                    }
                }
                catch
                {
                    // Handle or log error if needed.
                }
            }

            // Load default background if custom image fails.
            if (bitmap == null)
            {
                bitmap = new BitmapImage(new Uri("ms-appx:///Assets/Img_Background.png"));
            }

            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            if (dispatcherQueue.HasThreadAccess)
            {
                BackgroundImage = bitmap;
            }
            else
            {
                dispatcherQueue.TryEnqueue(() => BackgroundImage = bitmap);
            }
        }

        /// <summary>
        /// Checks for updates by comparing the remote version against the local version.
        /// </summary>
        public async Task CheckForUpdatesAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    // URL of the file containing the latest version string.
                    var url = "https://raw.githubusercontent.com/GID0317/MyTel-U_WinUI3/main/UpdateHelper/Versions.config";
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        string remoteVersionString = content.Trim();
                        if (!string.IsNullOrEmpty(remoteVersionString))
                        {
                            Version remoteVersion = new Version(remoteVersionString);
                            Version localVersion = App.AppVersion;

                            if (remoteVersion > localVersion)
                            {
                                // Delay and re-check to avoid transient errors.
                                await Task.Delay(2000);
                                var response2 = await client.GetAsync(url);
                                if (response2.IsSuccessStatusCode)
                                {
                                    var content2 = await response2.Content.ReadAsStringAsync();
                                    string remoteVersionString2 = content2.Trim();
                                    if (!string.IsNullOrEmpty(remoteVersionString2))
                                    {
                                        Version remoteVersion2 = new Version(remoteVersionString2);
                                        if (remoteVersion2 > localVersion)
                                        {
                                            UpdateInfoBarVisibility(true);
                                            return;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
            }

            UpdateInfoBarVisibility(false);
        }

        /// <summary>
        /// Checks the remote emergency update flag and, if enabled, performs the update check.
        /// </summary>
        private async Task CheckEmergencyUpdateAsync()
        {
            bool emergencyUpdateEnabled = await GetRemoteEmergencyUpdateFlagAsync();
            if (emergencyUpdateEnabled)
            {
                await CheckForUpdatesAsync();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Remote emergency auto-update is disabled; skipping update check.");
            }
        }

        /// <summary>
        /// Retrieves the emergency update flag from the remote HotUpdates.json file.
        /// </summary>
        private async Task<bool> GetRemoteEmergencyUpdateFlagAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    // GitHub URL for HotUpdates.json.
                    var configUrl = "https://raw.githubusercontent.com/GID0317/MyTel-U_WinUI3/refs/heads/main/UpdateHelper/HotUpdates.json";
                    var response = await client.GetAsync(configUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using (var doc = JsonDocument.Parse(json))
                        {
                            if (doc.RootElement.TryGetProperty("EmergencyAutoUpdateEnabled", out var prop) &&
                                prop.ValueKind == JsonValueKind.True)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to fetch remote config: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Updates the UI-bound IsUpdateAvailable property on the UI thread.
        /// </summary>
        private void UpdateInfoBarVisibility(bool available)
        {
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            if (dispatcherQueue.HasThreadAccess)
            {
                IsUpdateAvailable = available;
            }
            else
            {
                dispatcherQueue.TryEnqueue(() =>
                {
                    IsUpdateAvailable = available;
                });
            }
        }

        private bool IsInternetAvailable()
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            return (profile != null &&
                    profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess);
        }
    }
}