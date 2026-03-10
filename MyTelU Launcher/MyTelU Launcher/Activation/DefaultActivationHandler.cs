using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

using MyTelU_Launcher.Contracts.Services;
using MyTelU_Launcher.ViewModels;

namespace MyTelU_Launcher.Activation;

public class DefaultActivationHandler : ActivationHandler<LaunchActivatedEventArgs>
{
    private readonly INavigationService _navigationService;

    public DefaultActivationHandler(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    protected override bool CanHandleInternal(LaunchActivatedEventArgs args)
    {
        // None of the ActivationHandlers has handled the activation.
        return _navigationService.Frame?.Content == null;
    }

    protected async override Task HandleInternalAsync(LaunchActivatedEventArgs args)
    {
        _navigationService.NavigateTo(typeof(HomeViewModel).FullName!, args.Arguments, transitionInfo: new DrillInNavigationTransitionInfo());

        await Task.CompletedTask;
    }
}
