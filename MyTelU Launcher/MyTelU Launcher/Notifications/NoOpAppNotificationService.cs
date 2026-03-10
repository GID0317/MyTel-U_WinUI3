using System.Collections.Specialized;

using MyTelU_Launcher.Contracts.Services;

namespace MyTelU_Launcher.Notifications;

public class NoOpAppNotificationService : IAppNotificationService
{
    public void Initialize()
    {
    }

    public bool Show(string payload)
    {
        return false;
    }

    public NameValueCollection ParseArguments(string arguments)
    {
        return new NameValueCollection();
    }

    public void Unregister()
    {
    }

    public void ShowUpdateErrorDialog()
    {
    }

    public void DisplayUpdateErrorDialog()
    {
    }
}