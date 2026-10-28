using Microsoft.UI.Xaml.Controls;

namespace MyTelU_Launcher.Helpers;

public static class FrameExtensions
{
    public static object? GetPageViewModel(this Frame frame) => frame?.Content switch
    {
        Views.HomePage page => page.ViewModel,
        Views.SchedulePage page => page.ViewModel,
        Views.AttendancePage page => page.ViewModel,
        Views.GradePage page => page.ViewModel,
        Views.OpenCommunityToolsPage page => page.ViewModel,
        Views.WebViewerPage page => page.ViewModel,
        Views.SettingsPage page => page.ViewModel,
        _ => null
    };
}
