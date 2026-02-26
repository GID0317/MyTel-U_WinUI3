using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Web.WebView2.Core;
using MyTelU_Launcher.Contracts.Services;
using MyTelU_Launcher.Contracts.ViewModels;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;

namespace MyTelU_Launcher.ViewModels
{
    public partial class WebViewerViewModel : ObservableRecipient, INavigationAware
    {
        private readonly DispatcherTimer _failureTimer;
        private bool _isManualReload;

        public WebViewerViewModel(IWebViewService webViewService)
        {
            WebViewService = webViewService;
            _failureTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _failureTimer.Tick += OnFailureTimerTick;
        }

        [ObservableProperty]
        private Uri source = new("https://igracias.telkomuniversity.ac.id/");

        [ObservableProperty]
        private bool isLoading = true;

        [ObservableProperty]
        private bool hasFailures;

        public IWebViewService WebViewService
        {
            get;
        }

        [RelayCommand]
        private async Task OpenInBrowser()
        {
            if (WebViewService.Source != null)
            {
                await Windows.System.Launcher.LaunchUriAsync(WebViewService.Source);
            }
        }

        [RelayCommand]
        private void Reload()
        {
            IsLoading = true;
            HasFailures = false;
            _isManualReload = true;
            WebViewService.Reload();
        }

        [RelayCommand(CanExecute = nameof(BrowserCanGoForward))]
        private void BrowserForward()
        {
            if (WebViewService.CanGoForward)
            {
                WebViewService.GoForward();
            }
        }

        private bool BrowserCanGoForward() => WebViewService.CanGoForward;

        [RelayCommand(CanExecute = nameof(BrowserCanGoBack))]
        private void BrowserBack()
        {
            if (WebViewService.CanGoBack)
            {
                WebViewService.GoBack();
            }
        }

        private bool BrowserCanGoBack() => WebViewService.CanGoBack;

        public void OnNavigatedTo(object parameter)
        {
            if (parameter is string url)
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    Source = uri;
                }
                else if (Uri.TryCreate("https://" + url, UriKind.Absolute, out var httpsUri))
                {
                    Source = httpsUri;
                }
            }

            WebViewService.NavigationCompleted += OnNavigationCompleted;
        }

        public void OnNavigatedFrom()
        {
            _failureTimer.Stop();
            WebViewService.UnregisterEvents();
            WebViewService.NavigationCompleted -= OnNavigationCompleted;
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2WebErrorStatus webErrorStatus)
        {
            if (webErrorStatus != default)
            {
                if (_isManualReload)
                {
                    _failureTimer.Start();
                }
                else
                {
                    IsLoading = false;
                    HasFailures = true;
                }
            }
            else
            {
                IsLoading = false;
                HasFailures = false;
            }

            BrowserBackCommand.NotifyCanExecuteChanged();
            BrowserForwardCommand.NotifyCanExecuteChanged();
            _isManualReload = false;
        }

        private void OnFailureTimerTick(object? sender, object e)
        {
            _failureTimer.Stop();
            IsLoading = false;
            HasFailures = true;
        }

        [RelayCommand]
        private void OnRetry()
        {
            HasFailures = false;
            IsLoading = true;
            _isManualReload = true;
            WebViewService?.Reload();
        }
    }
}