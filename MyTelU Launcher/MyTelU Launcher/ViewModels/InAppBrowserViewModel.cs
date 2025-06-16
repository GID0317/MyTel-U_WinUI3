using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

namespace MyTelU_Launcher.ViewModels;

public partial class InAppBrowserViewModel : ObservableRecipient
{
    public InAppBrowserViewModel()
    {
    }

    [RelayCommand]
    public void BrowserBack()
    {
        // Implement the logic for Browser Back
    }

    [RelayCommand]
    public void BrowserForward()
    {
        // Implement the logic for Browser Forward
    }

    [RelayCommand]
    public void Reload()
    {
        // Implement the logic for Reload
    }

    [RelayCommand]
    public async Task OpenInBrowser()
    {
        // Implement the logic for Open In Browser
    }
}
