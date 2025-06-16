using System.Collections.Specialized;
using System.Web;

using Microsoft.Windows.AppNotifications;

using MyTelU_Launcher.Contracts.Services;
using MyTelU_Launcher.ViewModels;

namespace MyTelU_Launcher.Notifications;

public class AppNotificationService : IAppNotificationService
{
    private readonly INavigationService _navigationService;

    public AppNotificationService(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    ~AppNotificationService()
    {
        Unregister();
    }

    public void Initialize()
    {
        AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;

        AppNotificationManager.Default.Register();
    }

    public void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        // TODO: Handle notification invocations when your app is already running.

        //// // Navigate to a specific page based on the notification arguments.
        //// if (ParseArguments(args.Argument)["action"] == "Settings")
        //// {
        ////    App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        ////    {
        ////        _navigationService.NavigateTo(typeof(SettingsViewModel).FullName!);
        ////    });
        //// }

        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            App.MainWindow.ShowMessageDialogAsync("To check for an update, please check your internet connection settings and try again later.", "Netwo");

            App.MainWindow.BringToFront();
        });
    }

    public bool Show(string payload)
    {
        var appNotification = new AppNotification(payload);

        AppNotificationManager.Default.Show(appNotification);

        return appNotification.Id != 0;
    }

    public NameValueCollection ParseArguments(string arguments)
    {
        return HttpUtility.ParseQueryString(arguments);
    }

    public void Unregister()
    {
        AppNotificationManager.Default.Unregister();
    }

    public void ShowUpdateErrorDialog()
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            App.MainWindow.ShowMessageDialogAsync("Please check your internet connection settings and try again later.", "Update check failed");
        });
    }

    public void DisplayUpdateErrorDialog()
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            App.MainWindow.ShowMessageDialogAsync("An error occurred while displaying the update error dialog. Please try again later.", "Display Error");
        });
    }
}
