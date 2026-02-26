using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MyTelU_Launcher.Helpers;

/// <summary>
/// Sent whenever iGracias session cookies are captured from the WebView and saved to disk.
/// HomeViewModel listens for this to trigger an automatic schedule reload.
/// </summary>
public class SessionCookiesSavedMessage : ValueChangedMessage<bool>
{
    public SessionCookiesSavedMessage() : base(true) { }
}
