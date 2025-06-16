using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
namespace MyTelU_Launcher.Views
{
    public sealed partial class HomePage : Page
    {
        public HomePage()
        {
            this.InitializeComponent();
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

        private void BrowserUIOpenFisdas_Click(object sender, RoutedEventArgs e)
        {
            // Set the target URL for the web viewer.
            string targetUrl = "https://labfisdas-telu.com/";

            // Use the static ShellPage reference to show the overlay.
            ShellPage.Current?.ShowOverlay(typeof(InAppBrowserPage), targetUrl);
        }

        private void BrowserUIOpenSEALab_Click(object sender, RoutedEventArgs e)
        {
            // Set the target URL for the web viewer.
            string targetUrl = "https://sealab-telu.com/";

            // Use the static ShellPage reference to show the overlay.
            ShellPage.Current?.ShowOverlay(typeof(InAppBrowserPage), targetUrl);
        }

        private void BrowserUIOpenJeyyFileKuliah_Click(object sender, RoutedEventArgs e)
        {
            // Set the target URL for the web viewer.
            string targetUrl = "https://ac.jeyy.xyz/files/";

            // Use the static ShellPage reference to show the overlay.
            ShellPage.Current?.ShowOverlay(typeof(InAppBrowserPage), targetUrl);
        }

        private void BrowserUIOpenRegresiLinear_Click(object sender, RoutedEventArgs e)
        {
            // Set the target URL for the web viewer.
            string targetUrl = "https://regresi.msatrio.com/";

            // Use the static ShellPage reference to show the overlay.
            ShellPage.Current?.ShowOverlay(typeof(InAppBrowserPage), targetUrl);
        }

        private void NavigateToWebViewer(string url)
        {
            Frame.Navigate(typeof(WebViewerPage), url);
        }
    }

}