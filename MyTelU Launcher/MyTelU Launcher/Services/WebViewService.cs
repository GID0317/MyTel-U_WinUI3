using System.Diagnostics.CodeAnalysis;

using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

using MyTelU_Launcher.Contracts.Services;

namespace MyTelU_Launcher.Services;

public class WebViewService : IWebViewService
{
    private WebView2? _webView;

    public Uri? Source => _webView?.Source;

    [MemberNotNullWhen(true, nameof(_webView))]
    public bool CanGoBack => _webView != null && _webView.CanGoBack;

    [MemberNotNullWhen(true, nameof(_webView))]
    public bool CanGoForward => _webView != null && _webView.CanGoForward;

    public event EventHandler<CoreWebView2WebErrorStatus>? NavigationCompleted;

    public WebViewService()
    {
    }

    [MemberNotNull(nameof(_webView))]
    public void Initialize(WebView2 webView)
    {
        _webView = webView;
        _webView.NavigationCompleted += OnWebViewNavigationCompleted;
    }

    public void GoBack() => _webView?.GoBack();

    public void GoForward() => _webView?.GoForward();

    public void Reload() => _webView?.Reload();

    public void UnregisterEvents()
    {
        if (_webView != null)
        {
            _webView.NavigationCompleted -= OnWebViewNavigationCompleted;
        }
    }

    public void Cleanup()
    {
        if (_webView == null)
        {
            return;
        }

        _webView.NavigationCompleted -= OnWebViewNavigationCompleted;

        try
        {
            _webView.CoreWebView2?.Stop();
            _webView.Source = new Uri("about:blank");
            _webView.Close();
        }
        catch
        {
            // Best-effort cleanup. WebView2 teardown can fail during shutdown/navigation races.
        }
        finally
        {
            _webView = null;
        }
    }

    private void OnWebViewNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args) => NavigationCompleted?.Invoke(this, args.WebErrorStatus);
}
