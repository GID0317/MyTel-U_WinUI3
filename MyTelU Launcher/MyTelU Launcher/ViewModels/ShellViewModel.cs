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
using MyTelU_Launcher.Services;
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
        private readonly AccentColorService _accentColorService;

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
            AccentColorService accentColorService,
            SettingsViewModel settingsViewModel)
        {
            NavigationService = navigationService;
            NavigationService.Navigated += OnNavigated;
            NavigationViewService = navigationViewService;
            _localSettingsService = localSettingsService;
            _accentColorService = accentColorService;

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
            System.Diagnostics.Debug.WriteLine($"===== LOADING BACKGROUND IMAGE =====");
            System.Diagnostics.Debug.WriteLine($"Custom Background Enabled: {isCustomEnabled}");
            
            if (isCustomEnabled)
            {
                var imagePath = await _localSettingsService.ReadSettingAsync<string>("CustomBackgroundImagePath");
                System.Diagnostics.Debug.WriteLine($"Stored Image Path: {imagePath}");
                System.Diagnostics.Debug.WriteLine($"File Exists: {!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath)}");
                await UpdateBackgroundImageAsync(imagePath);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Using default background (no custom image)");
                await UpdateBackgroundImageAsync(string.Empty);
            }
            System.Diagnostics.Debug.WriteLine($"====================================");
        }

        private async Task UpdateBackgroundImageAsync(string imagePath)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateBackgroundImageAsync called with path: {imagePath}");
            
            // Set the background image immediately to ensure UI updates before color extraction starts
            // This ensures acrylics have the correct background to sample from
            BitmapImage bitmap;
            string colorExtractionPath = string.Empty;
            bool isCustomImage = !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath);

            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            if (isCustomImage)
            {
                // Custom background case
                try
                {
                    // Create bitmap for UI
                    // Use a stream to avoid file locking issues that might affect color extraction
                    var file = await StorageFile.GetFileFromPathAsync(imagePath);
                    using (var stream = await file.OpenAsync(FileAccessMode.Read))
                    {
                        bitmap = new BitmapImage();
                        await bitmap.SetSourceAsync(stream);
                    }
                    
                    // Set path for color extraction
                    colorExtractionPath = imagePath;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading custom background image: {ex.Message}");
                    // Fallback to default if loading fails
                    bitmap = new BitmapImage(new Uri("ms-appx:///Assets/Img_Background.png"));
                    isCustomImage = false; 
                }
            }
            else
            {
                // Default background case
                bitmap = new BitmapImage(new Uri("ms-appx:///Assets/Img_Background.png"));
            }

            // Update the UI immediately
            if (dispatcherQueue.HasThreadAccess)
            {
                BackgroundImage = bitmap;
            }
            else
            {
                dispatcherQueue.TryEnqueue(() => BackgroundImage = bitmap);
            }

            // Now extract and apply the accent color
            // This happens after the background is set, so acrylic effects will sample the correct image
            // when the theme refresh happens
            try
            {
                if (isCustomImage)
                {
                   System.Diagnostics.Debug.WriteLine($"Extracting accent color from custom path: {colorExtractionPath}");
                   await _accentColorService.UpdateAccentFromImageAsync(colorExtractionPath);
                }
                else
                {
                    // Find the default background file for color extraction
                    System.Diagnostics.Debug.WriteLine("No custom background, using default background accent");
                    string defaultBgPath = string.Empty;

                    // 1. Try finding relative to base directory (works in unpackaged/debug)
                    var basePath = AppDomain.CurrentDomain.BaseDirectory;
                    var candidatePath = Path.Combine(basePath, "Assets", "Img_Background.png");
                    
                    if (File.Exists(candidatePath))
                    {
                        defaultBgPath = candidatePath;
                    }
                    else
                    {
                        // 2. Try packaged asset URI (works in packaged app)
                        var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Img_Background.png"));
                        if (file != null)
                        {
                            defaultBgPath = file.Path;
                        }
                    }

                    if (!string.IsNullOrEmpty(defaultBgPath) && File.Exists(defaultBgPath))
                    {
                        await _accentColorService.UpdateAccentFromImageAsync(defaultBgPath);
                    }
                    else
                    {
                        // Fallback to extraction from empty string (which returns system accent)
                        // or just set system accent directly
                        var systemColor = await _accentColorService.ExtractDominantColorAsync(string.Empty);
                        _accentColorService.ApplyAccentColor(systemColor);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating accent color: {ex.Message}");
            }

            // Broadcast background brightness so subscribers (e.g. HomePage) can adapt text color.
            if (_accentColorService.LastExtractedColor.HasValue)
            {
                var c = _accentColorService.LastExtractedColor.Value;
                // Relative luminance (ITU-R BT.601)
                double luminance = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                bool isDark = luminance < 128;
                WeakReferenceMessenger.Default.Send(new BackgroundBrightnessChangedMessage(isDark));
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