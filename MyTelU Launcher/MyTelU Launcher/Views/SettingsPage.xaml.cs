using System;
using System.Net.Http;
using System.Reflection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyTelU_Launcher.Contracts.Services;
using MyTelU_Launcher.Notifications;
using MyTelU_Launcher.ViewModels;

namespace MyTelU_Launcher.Views
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsViewModel ViewModel
        {
            get;
        }

        public SettingsPage()
        {
            ViewModel = App.GetService<SettingsViewModel>();
            this.InitializeComponent();
        }

        private async void CostumImageBGBtn_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.PickBackgroundImageAsync();
        }

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Starting update check from settings page...");
                using (var client = new HttpClient())
                {
                    // Use the raw URL to fetch the version string.
                    var url = "https://raw.githubusercontent.com/GID0317/MyTel-U_WinUI3/main/UpdateHelper/Versions.config";
                    System.Diagnostics.Debug.WriteLine($"Fetching update info from: {url}");
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        string remoteVersionString = content.Trim();
                        System.Diagnostics.Debug.WriteLine($"Remote version string: {remoteVersionString}");

                        // Create a Version object from the remote version string.
                        Version remoteVersion = new Version(remoteVersionString);

                        // Use the stored package version from App.
                        Version localVersion = App.AppVersion;
                        System.Diagnostics.Debug.WriteLine($"Local version: {localVersion}");
                        System.Diagnostics.Debug.WriteLine($"Remote version: {remoteVersion}");

                        if (remoteVersion > localVersion)
                        {
                            System.Diagnostics.Debug.WriteLine("A newer version is available!");
                            // Set the ShellPage update flag so its InfoBar becomes visible.
                            if (ShellPage.Current != null)
                            {
                                var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
                                if (dispatcherQueue.HasThreadAccess)
                                {
                                    ShellPage.Current.ViewModel.IsUpdateAvailable = true;
                                }
                                else
                                {
                                    dispatcherQueue.TryEnqueue(() =>
                                    {
                                        ShellPage.Current.ViewModel.IsUpdateAvailable = true;
                                    });
                                }
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("Already up-to-date.");
                            // Show the up-to-date InfoBar on the SettingsPage.
                            UpdateUptodateInfoBar.Visibility = Visibility.Visible;
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to fetch update file. Status code: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update check from settings failed: {ex.Message}");
                // Resolve your AppNotificationService and call its ShowUpdateErrorDialog method.
                var notificationService = App.GetService<IAppNotificationService>();
                notificationService.ShowUpdateErrorDialog();
            }
        }
    }
}
