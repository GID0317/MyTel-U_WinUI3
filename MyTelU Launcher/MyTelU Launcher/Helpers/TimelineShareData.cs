using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MyTelU_Launcher.Helpers;

internal static class TimelineShareData
{
    internal static async Task SetImageAsync(DataPackage data, byte[] bytes, CancellationToken ct)
    {
        var stream = new InMemoryRandomAccessStream();
        try
        {
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync().AsTask(ct);
                writer.DetachStream();
            }
            stream.Seek(0);
            await BitmapDecoder.CreateAsync(stream).AsTask(ct);
            stream.Seek(0);
            var reference = RandomAccessStreamReference.CreateFromStream(stream);
            data.SetBitmap(reference);
            data.Properties.Thumbnail = reference;
            // The receiving app can read the image after DataRequested has returned.
            data.ShareCompleted += (_, _) => stream.Dispose();
            data.ShareCanceled += (_, _) => stream.Dispose();
            data.Destroyed += (_, _) => stream.Dispose();
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
