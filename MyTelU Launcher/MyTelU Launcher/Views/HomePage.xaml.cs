using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using CommunityToolkit.Mvvm.Messaging;
using MyTelU_Launcher.Helpers;
using MyTelU_Launcher.ViewModels;
using MyTelU_Launcher.Models;

namespace MyTelU_Launcher.Views
{
    public sealed partial class HomePage : Page
    {
        private ToolsFlyoutContent? _toolsFlyoutContent;

        public OpenCommunityToolsViewModel ViewModel { get; }
        public HomePage()
        {
            ViewModel = App.GetService<OpenCommunityToolsViewModel>();
            this.InitializeComponent();

            // Store the messenger reference to unregister safely in Unloaded
            this.Loaded += Page_Loaded;
            this.Unloaded += Page_Unloaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // Register here so we don't duplicate on navigation
            WeakReferenceMessenger.Default.Register<BackgroundBrightnessChangedMessage>(this, (r, m) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ApplicationListTxtTitle.Foreground = m.Value
                        ? new SolidColorBrush(Colors.White)
                        : new SolidColorBrush(Colors.Black);
                });
            });

            // Set initial text color based on current background brightness
            var accentColorService = App.GetService<MyTelU_Launcher.Services.AccentColorService>();
            if (accentColorService.LastBackgroundBrightness.HasValue)
            {
                // If average brightness is low, background is dark, so use white text
                bool isDark = accentColorService.LastBackgroundBrightness.Value < 128;
                ApplicationListTxtTitle.Foreground = isDark
                    ? new SolidColorBrush(Colors.White)
                    : new SolidColorBrush(Colors.Black);
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            // Crucial: remove the reference to this page from the global messenger
            WeakReferenceMessenger.Default.Unregister<BackgroundBrightnessChangedMessage>(this);
        }

        // Event handlers for Main Buttons
        private void MyTuctuc_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://tucar.telkomuniversity.ac.id/");
        }

        private void IGracias_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://igracias.telkomuniversity.ac.id/");
        }

        private void CeloeLMS_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://lms.telkomuniversity.ac.id/my/");
        }

        private void Celoe_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://celoe.telkomuniversity.ac.id/");
        }

        private void Sirama_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://sirama.telkomuniversity.ac.id/home");
        }

        private void CampusLive_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://campuslife.telkomuniversity.ac.id/");
        }

        private void TOS_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://toss.telkomuniversity.ac.id/");
        }

        private void OpenLibrary_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://openlibrary.telkomuniversity.ac.id/");
        }

        private void ServiceDeskSitu_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://situ-sis.telkomuniversity.ac.id/service-desk/auth/login");
        }

        private void TAK_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://situ-kem.telkomuniversity.ac.id/tak");
        }
        private void SatuTlU_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://satu.telkomuniversity.ac.id/auth/login");
        }

        private void Simka_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://simka.telkomuniversity.ac.id/");
        }

        private void LAC_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://lac.telkomuniversity.ac.id/");
        }

        private void SMB_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://smb.telkomuniversity.ac.id/");
        }

        private void Merpati_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://merpati.telkomuniversity.ac.id/");
        }

        private void SuaraTelutizen_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://suaratelutizen.telkomuniversity.ac.id/");
        }

        private void PelaporanKodeEtik_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://tel-u.ac.id/pelaporanpelanggaran");
        }

        private void PengembanganKarakter_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://lms.telkomuniversity.ac.id/course/index.php?categoryid=217");
        }

        private void Konseling_Click(object sender, RoutedEventArgs e)
        {
            NavigateToWebViewer("https://linktr.ee/ditmawa_univtelkom");
        }

        // Event handlers for the bottom buttons
        private void CommunityTool_Click(object sender, RoutedEventArgs e)
        {
            FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);
        }

        private void CustomFlyout_Opening(object sender, object e)
        {
            if (_toolsFlyoutContent != null)
                return;

            _toolsFlyoutContent = new ToolsFlyoutContent();
            _toolsFlyoutContent.ToolInvoked += ToolsFlyoutContent_ToolInvoked;
            _toolsFlyoutContent.EditRequested += ToolsFlyoutContent_EditRequested;
            CustomFlyout.Content = _toolsFlyoutContent;
        }

        private void CustomFlyout_Closed(object sender, object e)
        {
            if (_toolsFlyoutContent == null)
                return;

            _toolsFlyoutContent.ToolInvoked -= ToolsFlyoutContent_ToolInvoked;
            _toolsFlyoutContent.EditRequested -= ToolsFlyoutContent_EditRequested;
            CustomFlyout.Content = null;
            _toolsFlyoutContent = null;
        }

        private void ToolsFlyoutContent_ToolInvoked(object? sender, string url)
        {
            ShellPage.Current?.ShowOverlay(typeof(InAppBrowserPage), url);
        }

        private async void ToolsFlyoutContent_EditRequested(object? sender, EventArgs e)
        {
            await ShowManageToolsDialogAsync();
        }

        private async void EditToolsButton_Click(object sender, RoutedEventArgs e)
        {
            await ShowManageToolsDialogAsync();
        }

        private void Applicationlist_Toggle(object sender, RoutedEventArgs e)
        {
            var toggleSwitch = sender as ToggleSwitch;

            if (toggleSwitch != null)
            {
                if (toggleSwitch.IsOn == true)
                {
                    AplicationListContainer.Visibility = Visibility.Visible;
                    ApplicationListTxtTitle.Visibility = Visibility.Visible;
                }
                else
                {
                    AplicationListContainer.Visibility = Visibility.Collapsed;
                    ApplicationListTxtTitle.Visibility = Visibility.Collapsed;
                }

            }
        }

        private void ToolButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string url)
            {
                ShellPage.Current?.ShowOverlay(typeof(InAppBrowserPage), url);
            }
        }

        private async System.Threading.Tasks.Task ShowManageToolsDialogAsync()
        {
            CustomFlyout.Hide();

            var dialog = new ManageToolsDialog
            {
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private void NavigateToWebViewer(string url)
        {
            Frame.Navigate(typeof(WebViewerPage), url);
        }


    }

}