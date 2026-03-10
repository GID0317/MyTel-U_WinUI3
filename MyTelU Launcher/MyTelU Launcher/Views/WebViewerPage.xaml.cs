// WebViewerPage.xaml.cs
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using MyTelU_Launcher.ViewModels;
using MyTelU_Launcher.Contracts.ViewModels;

namespace MyTelU_Launcher.Views
{
    public sealed partial class WebViewerPage : Page
    {
        public WebViewerViewModel ViewModel { get; }

        public WebViewerPage()
        {
            ViewModel = App.GetService<WebViewerViewModel>();
            InitializeComponent();
            ViewModel.WebViewService.Initialize(WebView);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel.OnNavigatedTo(e.Parameter);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Cleanup();
            base.OnNavigatedFrom(e);
        }

        public void Cleanup()
        {
            if (ViewModel is INavigationAware navigationAware)
            {
                navigationAware.OnNavigatedFrom();
            }

            ViewModel.WebViewService.Cleanup();
        }

        /// <summary>Expose the WebView2 control publicly (used by InAppBrowserPage).</summary>
        public WebView2 BrowserWebView => WebView;
    }
}
