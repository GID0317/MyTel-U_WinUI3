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
        public OpenCommunityToolsViewModel ViewModel { get; }
        public HomePage()
        {
            ViewModel = App.GetService<OpenCommunityToolsViewModel>();
            this.InitializeComponent();

            // Adapt title text color to background brightness
            WeakReferenceMessenger.Default.Register<BackgroundBrightnessChangedMessage>(this, (r, m) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ApplicationListTxtTitle.Foreground = m.Value
                        ? new SolidColorBrush(Colors.White)
                        : new SolidColorBrush(Colors.Black);
                });
            });
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
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

        // Event handlers for the bottom buttons
        private void CommunityTool_Click(object sender, RoutedEventArgs e)
        {
            FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);
        }

        private async void EditToolsButton_Click(object sender, RoutedEventArgs e)
        {
            // Close the flyout first
            CustomFlyout.Hide();

            // Show the manage tools dialog
            var dialog = new ManageToolsDialog
            {
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
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

        private void NavigateToWebViewer(string url)
        {
            Frame.Navigate(typeof(WebViewerPage), url);
        }


    }

}