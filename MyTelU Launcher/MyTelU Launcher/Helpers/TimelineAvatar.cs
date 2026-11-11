using Microsoft.UI.Xaml.Media.Imaging;

namespace MyTelU_Launcher.Helpers;

public static class TimelineAvatar
{
    public static BitmapImage? Create(Uri? photo) => photo == null ? null : new BitmapImage
    {
        DecodePixelWidth = 64,
        UriSource = photo
    };
}
