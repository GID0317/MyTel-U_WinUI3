using Microsoft.UI.Xaml.Controls;

using MyTelU_Launcher.ViewModels;

namespace MyTelU_Launcher.Views;

public sealed partial class OpenCommunityToolsPage : Page
{
    public OpenCommunityToolsViewModel ViewModel
    {
        get;
    }

    public OpenCommunityToolsPage()
    {
        ViewModel = App.GetService<OpenCommunityToolsViewModel>();
        InitializeComponent();
    }
}
