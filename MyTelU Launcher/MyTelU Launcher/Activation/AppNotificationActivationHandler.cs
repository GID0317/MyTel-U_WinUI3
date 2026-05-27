using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

using MyTelU_Launcher.Contracts.Services;
using MyTelU_Launcher.ViewModels;

namespace MyTelU_Launcher.Activation;

public class AppNotificationActivationHandler : ActivationHandler<LaunchActivatedEventArgs>
{
    private readonly INavigationService _navigationService;
    private readonly IAppNotificationService _notificationService;

    public AppNotificationActivationHandler(INavigationService navigationService, IAppNotificationService notificationService)
    {
        _navigationService = navigationService;
        _notificationService = notificationService;
    }

    protected override bool CanHandleInternal(LaunchActivatedEventArgs args)
    {
        return AppInstance.GetCurrent().GetActivatedEventArgs()?.Kind == ExtendedActivationKind.AppNotification;
    }

    protected async override Task HandleInternalAsync(LaunchActivatedEventArgs args)
    {
        var activatedEventArgs = (AppNotificationActivatedEventArgs)AppInstance.GetCurrent().GetActivatedEventArgs().Data;
        var arguments = _notificationService.ParseArguments(activatedEventArgs.Argument);

        App.MainWindow.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (arguments["action"] == "schedule")
                _navigationService.NavigateTo(typeof(ScheduleViewModel).FullName!);
            else if (arguments["action"] == "settings")
                _navigationService.NavigateTo(typeof(SettingsViewModel).FullName!);
        });

        await Task.CompletedTask;
    }
}
