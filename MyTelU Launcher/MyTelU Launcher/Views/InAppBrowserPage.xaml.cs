using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;
using Windows.System;

namespace MyTelU_Launcher.Views
{
    public sealed partial class InAppBrowserPage : Page
    {
        public InAppBrowserPage()
        {
            this.InitializeComponent();

            // Register Navigating and Navigated events for the nested frame.
            ContentFrame.Navigating += ContentFrame_Navigating;
            ContentFrame.Navigated += ContentFrame_Navigated;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // If a URL parameter was passed, navigate the nested frame to WebViewerPage.
            if (e.Parameter is string url && !string.IsNullOrEmpty(url))
            {
                ContentFrame.Navigate(typeof(WebViewerPage), url);
            }
        }

        private void ContentFrame_Navigating(object sender, NavigatingCancelEventArgs e)
        {
            BrowserUIProgressBar.Visibility = Visibility.Visible;
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            BrowserUIProgressBar.Visibility = Visibility.Collapsed;

            // Once the nested frame has navigated, check if it is WebViewerPage.
            if (ContentFrame.Content is WebViewerPage webViewerPage)
            {
                // Subscribe to the WebView2 NavigationCompleted event.
                webViewerPage.BrowserWebView.NavigationCompleted -= WebView_NavigationCompleted;
                webViewerPage.BrowserWebView.NavigationCompleted += WebView_NavigationCompleted;

                // Update header immediately if available.
                UpdateBrowserHeader(webViewerPage);
            }
        }

        private void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (ContentFrame.Content is WebViewerPage webViewerPage)
            {
                UpdateBrowserHeader(webViewerPage);
            }
        }

        private void UpdateBrowserHeader(WebViewerPage webViewerPage)
        {
            if (webViewerPage.BrowserWebView.CoreWebView2 != null)
            {
                BrowserUIText.Text = webViewerPage.BrowserWebView.CoreWebView2.DocumentTitle;
                BrowserUITextBox.Text = webViewerPage.BrowserWebView.Source?.ToString();
            }
        }

        // --- UPDATED BACK / FORWARD HANDLERS ---

        private void BrowserUIBtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.Content is WebViewerPage page && page.BrowserWebView.CoreWebView2 != null)
            {
                if (page.BrowserWebView.CoreWebView2.CanGoBack)
                {
                    page.BrowserWebView.CoreWebView2.GoBack();
                }
            }
        }

        private void BrowserUIBtnForward_Click(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.Content is WebViewerPage page && page.BrowserWebView.CoreWebView2 != null)
            {
                if (page.BrowserWebView.CoreWebView2.CanGoForward)
                {
                    page.BrowserWebView.CoreWebView2.GoForward();
                }
            }
        }

        private void BrowserUIBtnReload_Click(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.Content is WebViewerPage currentPage)
            {
                string? currentUrl = currentPage.ViewModel.Source?.ToString();
                if (!string.IsNullOrEmpty(currentUrl))
                {
                    ContentFrame.Navigate(typeof(WebViewerPage), currentUrl);
                }
            }
        }

        private async void BrowserUIBtnOpenOnBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.Content is WebViewerPage page && page.BrowserWebView.Source != null)
            {
                // Use the Launcher to open the URL in the system browser.
                await Launcher.LaunchUriAsync(page.BrowserWebView.Source);
            }
        }

        private void BrowserUIBtnClose_Click(object sender, RoutedEventArgs e)
        {
            ShellPage.Current?.HideOverlay();
        }
    }
}
