using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MyTelU_Launcher.Helpers;

/// <summary>
/// Sent whenever the background image changes.
/// Value is true if the background is considered dark, false if bright.
/// </summary>
public class BackgroundBrightnessChangedMessage : ValueChangedMessage<bool>
{
    public BackgroundBrightnessChangedMessage(bool isDark) : base(isDark)
    {
    }
}
