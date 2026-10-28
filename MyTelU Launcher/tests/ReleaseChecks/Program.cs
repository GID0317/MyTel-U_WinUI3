using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MyTelU_Launcher.Core.Services;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.Services;
using Json = MyTelU_Launcher.Core.Helpers.Json;

Json.Options.TypeInfoResolver = TestJsonContext.Default;
var scratch = Path.Combine(Path.GetTempPath(), $"MyTelU-release-checks-{Guid.NewGuid():N}");
Directory.CreateDirectory(scratch);
int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
    Console.WriteLine($"PASS {name}");
}

try
{
    var path = Path.Combine(scratch, "cache.dat");
    const string payload = "{\"student\":\"SYNTHETIC-PRIVATE-STUDENT\",\"url\":\"https://example.test\"}";
    SecureFileStore.Save(path, payload);
    var cipher = File.ReadAllBytes(path);
    Check(SecureFileStore.Load(path) == payload, "encrypted cache round trip");
    Check(!Encoding.UTF8.GetString(cipher).Contains("SYNTHETIC-PRIVATE-STUDENT"), "no plaintext private data on disk");
    foreach (int offset in new[] { 0, cipher.Length / 2, cipher.Length - 1 })
    {
        var tampered = (byte[])cipher.Clone();
        tampered[offset] ^= 0x40;
        File.WriteAllBytes(path, tampered);
        Check(SecureFileStore.Load(path) == null, $"bit flip rejected at offset {offset}");
    }
    foreach (int length in new[] { 0, 1, cipher.Length / 2, cipher.Length - 1 })
    {
        File.WriteAllBytes(path, cipher.AsSpan(0, length).ToArray());
        Check(SecureFileStore.Load(path) == null, $"truncated cache rejected ({length} bytes)");
    }
    for (int i = 0; i < 32; i++)
    {
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(32 + i * 13));
        Check(SecureFileStore.Load(path) == null, $"random replacement rejected ({i})");
    }
    File.WriteAllText(path, payload);
    Check(SecureFileStore.Load(path) == null, "plaintext cache fallback rejected");
    File.WriteAllBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(payload), "wrong-purpose"u8.ToArray(), DataProtectionScope.CurrentUser));
    Check(SecureFileStore.Load(path) == null, "wrong DPAPI entropy rejected");
    using (var stream = File.Create(path)) stream.SetLength(SecureFileStore.MaximumFileSize + 1L);
    long allocationStart = GC.GetAllocatedBytesForCurrentThread();
    Check(SecureFileStore.Load(path) == null, "oversized encrypted cache rejected");
    Check(GC.GetAllocatedBytesForCurrentThread() - allocationStart < 1024 * 1024, "oversized cache rejected before large allocation");

    var values = new Dictionary<string, string> { ["studentId"] = "synthetic-id" };
    SecureSettingsStore.Save(path, values);
    Check(SecureSettingsStore.Load(path)["studentId"] == "synthetic-id", "shared encrypted settings round trip");
    SecureFileStore.Save(path, "not JSON");
    Check(SecureSettingsStore.Load(path).Count == 0, "malformed authenticated settings fail closed");
    File.WriteAllText(path, "{\"studentId\":\"forged\"}");
    Check(SecureSettingsStore.Load(path).Count == 0, "plaintext settings are not migrated");

    var fileService = new FileService();
    var options = Options.Create(new LocalSettingsOptions { ApplicationDataFolder = scratch, LocalSettingsFile = "local.json" });
    LocalSettingsService NewSettings() => new(fileService, options);
    var settings = NewSettings();
    await settings.SaveProtectedSettingAsync("Tools", new List<ToolItem> { new() { Name = "Synthetic", Url = "https://example.test" } });
    Check((await NewSettings().ReadProtectedSettingAsync<List<ToolItem>>("Tools"))?[0].Url == "https://example.test", "protected tools survive restart");
    var stored = fileService.Read<IDictionary<string, string>>(scratch, "local.json");
    stored["Tools"] = Json.Serialize(new List<ToolItem> { new() { Url = "https://phishing.invalid" } });
    stored["Tools.Protected.v1"] = "invalid ciphertext";
    fileService.Save(scratch, "local.json", stored);
    Check(await NewSettings().ReadProtectedSettingAsync<List<ToolItem>>("Tools") == null, "forged tool URL and legacy plaintext rejected");
    Check(!fileService.Read<IDictionary<string, string>>(scratch, "local.json").ContainsKey("Tools"), "legacy unprotected tool key removed");
    File.WriteAllText(Path.Combine(scratch, "local.json"), "broken JSON");
    Check(await NewSettings().ReadSettingAsync<string>("Theme") == null, "malformed local settings do not crash startup");
    using (var stream = File.Create(Path.Combine(scratch, "local.json"))) stream.SetLength(16 * 1024 * 1024 + 1L);
    Check(await NewSettings().ReadSettingAsync<string>("Theme") == null, "oversized local settings do not crash startup");

    foreach (var size in new[] { (4096, 2048), (20, 10), (1, 5000) })
    {
        string imagePath = Path.Combine(scratch, "synthetic.png");
        using (var image = new Bitmap(size.Item1, size.Item2))
        {
            using var graphics = Graphics.FromImage(image);
            graphics.Clear(Color.FromArgb(40, 120, 180));
            image.Save(imagePath, ImageFormat.Png);
        }
        using var thumbnail = await AccentImageLoader.LoadAsync(imagePath);
        Check(thumbnail.Width <= 256 && thumbnail.Height <= 256, $"bounded accent decode {size}");
        var pixel = thumbnail.GetPixel(0, 0);
        Check(Math.Abs(pixel.R - 40) <= 1 && Math.Abs(pixel.G - 120) <= 1 && Math.Abs(pixel.B - 180) <= 1, $"accent pixel channels preserved {size}");
    }
    var firstSession = await IGraciasSessionGate.EnterAsync(scratch);
    var secondSession = IGraciasSessionGate.EnterAsync(scratch);
    await Task.Delay(100);
    Check(!secondSession.IsCompleted, "overlapping academic request waits without blocking UI");
    firstSession.Dispose();
    using (await secondSession.WaitAsync(TimeSpan.FromSeconds(3)))
    {
        using var cancellation = new CancellationTokenSource(100);
        bool cancelled = false;
        try { using var unexpected = await IGraciasSessionGate.EnterAsync(scratch, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "queued academic request supports cancellation");
    }
    using (await IGraciasSessionGate.EnterAsync(scratch))
        Check(true, "academic session gate released after disposal");
    Console.WriteLine($"Verified {checks} release checks.");
}
finally
{
    Directory.Delete(scratch, recursive: true);
}

[JsonSerializable(typeof(IDictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<ToolItem>))]
[JsonSerializable(typeof(string))]
internal partial class TestJsonContext : JsonSerializerContext;
