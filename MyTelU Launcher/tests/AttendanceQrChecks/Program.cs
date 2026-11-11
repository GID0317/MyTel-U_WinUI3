using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using MyTelU_Launcher.Services;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using ZXing;
using ZXing.Common;

const string qr = "00112233-4455-6677-8899-aabbccddeeff";
const string viewer = "https://mobile.telkomuniversity.ac.id/qrcode/view/SYNTHETIC-session";
if (args.Length == 1 && args[0] == "--list-cameras")
{
    using var discoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var cameras = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(
        Windows.Devices.Enumeration.DeviceClass.VideoCapture).AsTask(discoveryTimeout.Token);
    Console.WriteLine($"Windows camera discovery: {cameras.Count} device(s)");
    foreach (var camera in cameras) Console.WriteLine(camera.Name);
    return;
}
int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
    Console.WriteLine($"PASS {name}");
}
async Task Reject<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); }
    catch (T) { Check(true, name); return; }
    throw new InvalidOperationException(name);
}
HttpResponseMessage Reply(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body) };
Check(AttendanceQrService.ValidateQrValue(qr) == qr, "Opaque QR payload is preserved exactly");
foreach (string value in new[] { "", "../token", "foo/bar", "foo%2fbar", "foo?act=other", "foo\nbar", "tést", new string('x', 257) })
    await Reject<InvalidDataException>(() => Task.FromResult(AttendanceQrService.ValidateQrValue(value)), "Unsafe QR payload rejected");
Check(AttendanceQrService.ValidateViewerLink(viewer).Host == "mobile.telkomuniversity.ac.id", "Official viewer link accepted");
foreach (string link in new[]
{
    "http://mobile.telkomuniversity.ac.id/qrcode/view/test",
    "https://mobile.telkomuniversity.ac.id.evil.test/qrcode/view/test",
    "https://mobile.telkomuniversity.ac.id@evil.test/qrcode/view/test",
    "https://other@mobile.telkomuniversity.ac.id/qrcode/view/test",
    "https://mobile.telkomuniversity.ac.id:444/qrcode/view/test",
    "https://mobile.telkomuniversity.ac.id/qrcode/view/a/b",
    "https://mobile.telkomuniversity.ac.id/qrcode/view/%2Ftest",
    "https://mobile.telkomuniversity.ac.id/qrcode/view/test?redirect=https://evil.test",
    "https://mobile.telkomuniversity.ac.id/qrcode/view/test#fragment",
    "https://mobile.telkomuniversity.ac.id/v2/auth/login", "file:///C:/secret", "javascript:alert(1)"
})
    await Reject<InvalidDataException>(() => Task.FromResult(AttendanceQrService.ValidateViewerLink(link)), "Untrusted viewer link rejected");

var writer = new BarcodeWriterPixelData
{
    Format = BarcodeFormat.QR_CODE,
    Options = new EncodingOptions { Width = 256, Height = 256, Margin = 4 }
};
var image = writer.Write(qr);
Check(AttendanceQrService.ReadPixels(image.Pixels, image.Width, image.Height) == qr, "Synthetic QR decodes locally");
var inverted = image.Pixels.ToArray();
for (int i = 0; i < inverted.Length; i++) if (i % 4 != 3) inverted[i] = (byte)(255 - inverted[i]);
Check(AttendanceQrService.ReadPixels(inverted, image.Width, image.Height) == qr, "Inverted QR decodes locally");
Check(AttendanceQrService.ReadPixels(Enumerable.Repeat((byte)255, 256 * 256 * 4).ToArray(), 256, 256) == null,
    "An image without a QR does not produce a payload");
await Reject<InvalidDataException>(() => Task.FromResult(AttendanceQrService.ReadPixels([], 4097, 1)), "Oversized pixel dimensions rejected before decoding");
var wrongQr = writer.Write("https://evil.test/collect");
await Reject<InvalidDataException>(() => Task.FromResult(AttendanceQrService.ReadPixels(wrongQr.Pixels, wrongQr.Width, wrongQr.Height)), "QR containing an arbitrary URL is not submitted");
var multiple = Enumerable.Repeat((byte)255, 512 * 256 * 4).ToArray();
for (int row = 0; row < 256; row++)
{
    Array.Copy(image.Pixels, row * 256 * 4, multiple, row * 512 * 4, 256 * 4);
    Array.Copy(image.Pixels, row * 256 * 4, multiple, (row * 512 + 256) * 4, 256 * 4);
}
await Reject<InvalidDataException>(() => Task.FromResult(AttendanceQrService.ReadPixels(multiple, 512, 256)), "Ambiguous image containing multiple QR codes rejected");

using var pngStream = new InMemoryRandomAccessStream();
var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, pngStream);
encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)image.Width, (uint)image.Height, 96, 96, image.Pixels);
await encoder.FlushAsync();
pngStream.Seek(0);
using (var bitmap = await AttendanceQrService.LoadImageAsync(pngStream))
    Check(AttendanceQrService.ReadBitmap(bitmap) == qr, "Native Windows PNG decoder reads the QR image");
pngStream.Seek(0);
byte[] png = new byte[(int)pngStream.Size];
using (var reader = new DataReader(pngStream.GetInputStreamAt(0))) { await reader.LoadAsync((uint)png.Length); reader.ReadBytes(png); }
string html = $"<html><img alt='qrcode' src='data:image/png;base64,{Convert.ToBase64String(png)}'></html>";
var viewerHandler = new FakeHandler((request, _) =>
{
    Check(request.Method == HttpMethod.Get && request.Headers.Authorization == null, "Viewer is read-only and receives no bearer token");
    return Task.FromResult(Reply(html));
});
using (var service = new AttendanceQrService(new HttpClient(viewerHandler)))
{
    using var bitmap = await service.LoadLinkImageAsync(viewer);
    Check(bitmap.PixelWidth == image.Width && bitmap.PixelHeight == image.Height, "Link supplies the decoded image for preview");
    Check(AttendanceQrService.ReadBitmap(bitmap) == qr && viewerHandler.Calls == 1, "Viewer HTML image decodes without any attendance POST");
    await Reject<InvalidDataException>(() => service.LoadLinkImageAsync("https://evil.test/"), "Host validation occurs before any network request");
    Check(viewerHandler.Calls == 1, "Rejected URL makes zero additional requests");
}
foreach (string body in new[] { "Pastikan anda scan QR yang paling update", "<img alt='qrcode' src='https://evil.test/track.png'>", "<img alt='qrcode' src='data:image/png;base64,%%%'>" })
{
    var handler = new FakeHandler((_, _) => Task.FromResult(Reply(body)));
    using var service = new AttendanceQrService(new HttpClient(handler));
    await Reject<InvalidDataException>(() => service.LoadLinkImageAsync(viewer), "Expired, external, or malformed QR image rejected");
    Check(handler.Calls == 1, "Invalid viewer content is not retried");
}
var redirectHandler = new FakeHandler((_, _) =>
{
    var response = Reply("", HttpStatusCode.Redirect);
    response.Headers.Location = new Uri("https://evil.test/");
    return Task.FromResult(response);
});
using (var service = new AttendanceQrService(new HttpClient(redirectHandler)))
{
    await Reject<InvalidDataException>(() => service.LoadLinkImageAsync(viewer), "Viewer redirect is not followed");
    Check(redirectHandler.Calls == 1, "Redirect does not cause a second request");
}
var hugeHandler = new FakeHandler((_, _) =>
{
    var response = Reply("x");
    response.Content.Headers.ContentLength = 3 * 1024 * 1024;
    return Task.FromResult(response);
});
using (var service = new AttendanceQrService(new HttpClient(hugeHandler)))
    await Reject<InvalidDataException>(() => service.LoadLinkImageAsync(viewer), "Oversized viewer response is rejected");

var submitHandler = new FakeHandler(async (request, ct) =>
{
    Check(request.Method == HttpMethod.Post && request.RequestUri!.AbsoluteUri == $"https://mobile.telkomuniversity.ac.id/v2/qrcodes/validate/{qr}", "Submission targets only the official endpoint");
    Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == "SYNTHETIC-TOKEN", "Bearer is scoped to the single submission request");
    Check(await request.Content!.ReadAsStringAsync(ct) == "{}" && request.Content.Headers.ContentType?.MediaType == "application/json", "Submission uses the verified empty JSON body");
    return Reply("{\"success\":true,\"message\":\"Synthetic accepted response\"}");
});
using (var http = new HttpClient(submitHandler))
{
    var result = await AttendanceQrService.SubmitAsync(http, "SYNTHETIC-TOKEN", qr);
    Check(result.Success && result.Message == "Synthetic accepted response" && submitHandler.Calls == 1, "Server acceptance is returned after exactly one POST");
}
var rejection = new FakeHandler((_, _) => Task.FromResult(Reply("{\"success\":false,\"message\":\"QR expired\"}", HttpStatusCode.BadRequest)));
using (var http = new HttpClient(rejection))
{
    var result = await AttendanceQrService.SubmitAsync(http, "SYNTHETIC-TOKEN", qr);
    Check(!result.Success && result.Message == "QR expired" && rejection.Calls == 1, "Server rejection is shown without retrying");
}
foreach (string body in new[] { "<html>unavailable</html>", "[]", "{}", "{\"success\":\"true\"}" })
{
    var handler = new FakeHandler((_, _) => Task.FromResult(Reply(body)));
    using var http = new HttpClient(handler);
    bool unknown = false;
    try { await AttendanceQrService.SubmitAsync(http, "SYNTHETIC-TOKEN", qr); }
    catch (Exception ex) when (ex is InvalidDataException or JsonException) { unknown = true; }
    Check(unknown && handler.Calls == 1, "Unconfirmed submission is never assumed successful or retried");
}
var timeoutHandler = new FakeHandler((_, _) => throw new TaskCanceledException("Synthetic timeout"));
using (var http = new HttpClient(timeoutHandler))
{
    await Reject<OperationCanceledException>(() => AttendanceQrService.SubmitAsync(http, "SYNTHETIC-TOKEN", qr), "Lost response remains an unknown result");
    Check(timeoutHandler.Calls == 1, "Timed-out attendance write is not retried");
}
var xaml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "ScanAttendanceDialog.xaml"));
Check((string?)xaml.Root!.Attribute("DefaultButton") == "Close" && (string?)xaml.Root.Attribute("IsPrimaryButtonEnabled") == "False",
    "Dialog defaults to Cancel and cannot submit before a QR is read");
Check(xaml.Descendants().Where(e => e.Name.LocalName == "SelectorBarItem").Select(e => (string?)e.Attribute("Text")).SequenceEqual(new[] { "Camera", "Image", "Link" }),
    "Native selector offers all three input methods");
Check((string?)xaml.Root.Attribute("Opened") == "Dialog_Opened",
    "Camera discovery is triggered when the dialog opens, not by starting the stream");
XNamespace xamlNames = "http://schemas.microsoft.com/winfx/2006/xaml";
foreach (string name in new[] { "CameraPicker", "CameraButton" })
    Check((string?)xaml.Descendants().Single(e => (string?)e.Attribute(xamlNames + "Name") == name).Attribute("IsEnabled") == "False",
        $"{name} waits for camera discovery instead of exposing an empty active control");
var attendancePage = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AttendancePage.xaml"));
var scanButton = attendancePage.Descendants().Single(e => e.Name.LocalName == "DropDownButton"
    && (string?)e.Attribute("AutomationProperties.Name") == "Scan presensi");
Check(scanButton.Descendants().Any(e => e.Name.LocalName == "FontIcon" && (string?)e.Attribute("Glyph") == "\uEE6F"),
    "Scan presensi uses the requested EE6F glyph on a native dropdown");
var scanOptions = scanButton.Descendants().Where(e => e.Name.LocalName == "MenuFlyoutItem").ToList();
Check(scanOptions.Select(e => (string?)e.Attribute("Text")).SequenceEqual(new[] { "Camera", "Image", "Link" }),
    "Scan presensi dropdown offers Camera, Image, and Link");
foreach (var option in scanOptions)
    Check((string?)option.Attribute("Click") == $"ScanAttendance{option.Attribute("Text")!.Value}_Click"
        && option.Elements().Any(e => e.Name.LocalName == "MenuFlyoutItem.Icon"),
        $"{option.Attribute("Text")!.Value} has its own entry handler and native icon");
if (args.Length == 2 && args[0] == "--fixture")
{
    await File.WriteAllBytesAsync(args[1], png);
    Console.WriteLine("Wrote a synthetic QR fixture for UI testing; no attendance request was made.");
}
Console.WriteLine($"Verified {checks} attendance QR checks. All HTTP requests were mocked; no camera or live attendance submission was used.");

sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        cancellationToken.ThrowIfCancellationRequested();
        return respond(request, cancellationToken);
    }
}
