using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace MyTelU_Launcher.Services;

internal static class AccentImageLoader
{
    public static async Task<Bitmap> LoadAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        // Color analysis needs a representative thumbnail, not a full wallpaper decode.
        var scale = Math.Min(1.0, 256.0 / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var width = Math.Max(1, (int)(decoder.PixelWidth * scale));
        var height = Math.Max(1, (int)(decoder.PixelHeight * scale));
        var transform = new BitmapTransform { ScaledWidth = (uint)width, ScaledHeight = (uint)height };
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var pixels = data.DetachPixelData();
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var map = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try
        {
            for (int y = 0; y < height; y++)
                Marshal.Copy(pixels, y * width * 4, IntPtr.Add(map.Scan0, y * map.Stride), width * 4);
        }
        finally
        {
            bitmap.UnlockBits(map);
        }
        return bitmap;
    }
}
