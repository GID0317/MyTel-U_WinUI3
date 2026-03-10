using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MyTelU_Launcher.Helpers;

/// <summary>
/// Broadcast whenever the browser login starts or finishes so every page's
/// login overlay can mirror the in-progress spinner regardless of which VM
/// initiated the login.
/// Value = true  → login in progress
/// Value = false → login finished (success or cancelled)
/// </summary>
public class BrowserLoginStateMessage : ValueChangedMessage<bool>
{
    public BrowserLoginStateMessage(bool isRunning) : base(isRunning) { }
}
