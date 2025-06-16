using System.Collections.Specialized;

namespace MyTelU_Launcher.Contracts.Services;

public interface IAppNotificationService
{
    void Initialize();

    bool Show(string payload);

    NameValueCollection ParseArguments(string arguments);

    void Unregister();
    void ShowUpdateErrorDialog();
    void DisplayUpdateErrorDialog();
}
