using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MyTelU_Launcher.Helpers;

public sealed class HideProfilePictureChangedMessage(bool value) : ValueChangedMessage<bool>(value);
