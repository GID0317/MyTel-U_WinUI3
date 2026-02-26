using System;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
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

        private async void OpenImageLocationBtn_Click(object sender, RoutedEventArgs e)
        {
            var path = ViewModel.CustomImagePath;
            if (!string.IsNullOrEmpty(path))
            {
                var folder = System.IO.Path.GetDirectoryName(path);
                if (folder != null)
                {
                    await Windows.System.Launcher.LaunchFolderPathAsync(folder);
                }
            }
        }

        private async void ResetToolsButton_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog resetToolsDialog = new ContentDialog
            {
                Title = "Reset Tools?",
                Content = "This will restore the tools list to its default state. All custom tools, community tools, and changes will be lost. Are you sure?",
                PrimaryButtonText = "Reset",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            // Apply dynamic accent colors to fix ContentDialog button hover states
            var accentService = App.GetService<MyTelU_Launcher.Services.AccentColorService>();
            accentService?.ApplyToContentDialog(resetToolsDialog);

            var result = await resetToolsDialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                ViewModel.ResetTools();
                ResetSuccessInfoBar.Visibility = Visibility.Visible;
                await Task.Delay(3000);
                ResetSuccessInfoBar.Visibility = Visibility.Collapsed;
            }
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
                            await Task.Delay(3000);
                            UpdateUptodateInfoBar.Visibility = Visibility.Collapsed;
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
