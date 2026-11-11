using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using HtmlAgilityPack;
using MyTelU_Launcher.Models;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using ZXing;
using ZXing.Common;

namespace MyTelU_Launcher.Services;

public sealed partial class AttendanceQrService : IDisposable
{
    private const string OfficialHost = "mobile.telkomuniversity.ac.id";
    private readonly HttpClient _http;

    public AttendanceQrService() : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(20)
    }) { }

    public AttendanceQrService(HttpClient http) => _http = http;

    public static string ValidateQrValue(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 256
            || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidDataException("This is not a supported MyTelU attendance QR code.");
        return value;
    }

    public static Uri ValidateViewerLink(string text)
    {
        const string prefix = "/qrcode/view/";
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals(OfficialHost, StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || !uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)
            || uri.AbsolutePath[prefix.Length..].Length == 0)
            throw new InvalidDataException("Enter an official MyTelU attendance link.");

        ValidateQrValue(uri.AbsolutePath[prefix.Length..]);
        return uri;
    }

    public async Task<SoftwareBitmap> LoadLinkImageAsync(string text, CancellationToken ct = default)
    {
        var uri = ValidateViewerLink(text);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        // Do not follow redirects or forward authentication to a QR viewer.
        if (!response.IsSuccessStatusCode)
            throw new InvalidDataException("The attendance link is unavailable. Check that it is still valid.");
        var html = Encoding.UTF8.GetString(await ReadLimitedAsync(response.Content, 2 * 1024 * 1024, ct));
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var source = document.DocumentNode.SelectSingleNode("//img[@alt='qrcode']")?.GetAttributeValue("src", "");
        const string imagePrefix = "data:image/png;base64,";
        if (source == null || !source.StartsWith(imagePrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This link has no active attendance QR code. Ask for the latest link.");

        byte[] bytes;
        try { bytes = Convert.FromBase64String(source[imagePrefix.Length..]); }
        catch (System.FormatException) { throw new InvalidDataException("The attendance link returned an unreadable QR image."); }
        using var stream = new MemoryStream(bytes).AsRandomAccessStream();
        return await LoadImageAsync(stream, ct);
    }

    public static async Task<SoftwareBitmap> LoadImageAsync(IRandomAccessStream stream, CancellationToken ct = default)
    {
        if (stream.Size == 0 || stream.Size > 32 * 1024 * 1024)
            throw new InvalidDataException("Choose an image smaller than 32 MB.");
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct);
        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0)
            throw new InvalidDataException("The image could not be read.");
        double scale = Math.Min(1, 2048.0 / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var transform = new BitmapTransform
        {
            ScaledWidth = Math.Max(1, (uint)(decoder.PixelWidth * scale)),
            ScaledHeight = Math.Max(1, (uint)(decoder.PixelHeight * scale))
        };
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage).AsTask(ct);
    }

    public static string? ReadBitmap(SoftwareBitmap bitmap)
    {
        if (bitmap.PixelWidth > 4096 || bitmap.PixelHeight > 4096)
            throw new InvalidDataException("The QR image dimensions are not supported.");
        var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
        bitmap.CopyToBuffer(pixels.AsBuffer());
        return ReadPixels(pixels, bitmap.PixelWidth, bitmap.PixelHeight);
    }

    public static string? ReadPixels(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096
            || bgra.Length != checked(width * height * 4))
            throw new InvalidDataException("The QR image dimensions are not supported.");
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { PossibleFormats = [BarcodeFormat.QR_CODE], TryHarder = true }
        };
        var luminance = new RGBLuminanceSource(bgra, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var results = reader.DecodeMultiple(luminance);
        // ZXing's multiple-QR reader does not apply the single-reader inversion option.
        if (results == null || results.Length == 0) results = reader.DecodeMultiple(luminance.invert());
        if (results == null || results.Length == 0) return null;
        if (results.Length != 1)
            throw new InvalidDataException("More than one QR code was found. Use an image containing only the attendance QR code.");
        return ValidateQrValue(results[0].Text);
    }

    public static async Task<AttendanceSubmissionResult> SubmitAsync(HttpClient http, string token, string qrValue, CancellationToken ct = default)
    {
        ValidateQrValue(qrValue);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://{OfficialHost}/v2/qrcodes/validate/{qrValue}")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // A lost response does not mean the write failed. Never retry this request automatically.
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var bytes = await ReadLimitedAsync(response.Content, 16 * 1024, ct);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var success)
            || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("MyTelU did not confirm the attendance result.");
        if (success.ValueKind == JsonValueKind.True) response.EnsureSuccessStatusCode();
        string message = root.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString() ?? "" : "";
        if (message.Length > 512) message = message[..512];
        return new(success.GetBoolean(), string.IsNullOrWhiteSpace(message)
            ? (success.GetBoolean() ? "Attendance submitted." : "MyTelU did not accept this attendance QR code.") : message);
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maximum, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maximum)
            throw new InvalidDataException("The server response is too large.");
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (output.Length + read > maximum)
                throw new InvalidDataException("The server response is too large.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    public void Dispose() => _http.Dispose();
}
