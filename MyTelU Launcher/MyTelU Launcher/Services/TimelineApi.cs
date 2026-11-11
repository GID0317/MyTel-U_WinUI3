using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MyTelU_Launcher.Models;

namespace MyTelU_Launcher.Services;

internal static class TimelineApi
{
    private const string Origin = "https://mobile.telkomuniversity.ac.id";
    internal const int MaxPostImageBytes = 5 * 1024 * 1024;
    internal const int MaxPostCharacters = 280;

    internal static int CountPostCharacters(string text) => new StringInfo(text).LengthInTextElements;

    public static async Task<TimelinePageResult> GetPageAsync(HttpClient http, string token, int page, CancellationToken ct, string search = "")
    {
        var path = $"/v2/feeds/list?currentPage={page.ToString(CultureInfo.InvariantCulture)}";
        if (!string.IsNullOrWhiteSpace(search)) path += "&search=" + Uri.EscapeDataString(search.Trim());
        using var document = await ReadJsonAsync(http, token, path, ct);
        var root = document.RootElement;
        var posts = new List<TimelinePost>();
        foreach (var item in ReadItems(root))
        {
            int id = Number(item, "id");
            if (id <= 0) continue;
            posts.Add(new(id, Author(item), Text(item, "content"), Text(item, "image"), Timestamp(item),
                Number(item, "likecount"), Number(item, "commentcount"),
                Flag(item, "pinned"), Flag(item, "likestatus"),
                !item.TryGetProperty("commentable", out var commentable) || commentable.ValueKind != JsonValueKind.False,
                Photo(item, "photo")));
        }
        var current = page;
        var total = page;
        if (root.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
        {
            current = Math.Max(1, Number(metadata, "currentPage", page));
            total = Math.Max(current, Number(metadata, "totalPage", page));
        }
        return new(posts, current, total);
    }

    public static async Task<IReadOnlyList<TimelineComment>> GetCommentsAsync(HttpClient http, string token, int id, CancellationToken ct)
    {
        using var document = await ReadJsonAsync(http, token, $"/v2/feeds/comments/list?feedId={id.ToString(CultureInfo.InvariantCulture)}", ct);
        return ReadItems(document.RootElement).Select(item => new TimelineComment(
            Author(item), Text(item, "comment_"), Timestamp(item), Photo(item, "profile_photo"))).ToArray();
    }

    public static async Task SetLikeAsync(HttpClient http, string token, int id, bool liked, CancellationToken ct)
    {
        var route = liked ? "/v2/feeds/like" : "/v2/feeds/unlike";
        await WriteAsync(http, token, route, [new("feedId", id.ToString(CultureInfo.InvariantCulture))], ct);
    }

    public static async Task AddCommentAsync(HttpClient http, string token, int id, string comment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(comment)) throw new ArgumentException("Enter a comment.", nameof(comment));
        await WriteAsync(http, token, "/v2/feeds/comments/create",
            [new("feedId", id.ToString(CultureInfo.InvariantCulture)), new("comment", comment.Trim())], ct);
    }

    public static async Task CreatePostAsync(HttpClient http, string token, string content, TimelinePostImage? image, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Enter post text.", nameof(content));
        if (CountPostCharacters(content) > MaxPostCharacters)
            throw new ArgumentException($"Posts can contain up to {MaxPostCharacters} characters.", nameof(content));
        if (image != null && (image.Bytes.Length == 0 || image.Bytes.Length > MaxPostImageBytes))
            throw new ArgumentException("Choose an image no larger than 5 MB.", nameof(image));

        using var request = new HttpRequestMessage(HttpMethod.Post, Origin + "/v2/feeds/create");
        var form = new MultipartFormDataContent();
        request.Content = form;
        form.Add(new StringContent(content, Encoding.UTF8), "content");
        if (image != null)
        {
            var file = new ByteArrayContent(image.Bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "image", Path.GetFileName(image.FileName));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(http.Timeout);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
            throw new HttpRequestException("MyTelU did not confirm the post.", null, response.StatusCode);
    }

    private static async Task WriteAsync(HttpClient http, string token, string route,
        IEnumerable<KeyValuePair<string, string>> fields, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Origin + route)
        {
            Content = new FormUrlEncodedContent(fields)
        };
        var bytes = await SendAsync(http, token, request, "application/json", 64 * 1024, ct);
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !Flag(document.RootElement, "success"))
            throw new InvalidDataException("MyTelU did not confirm the action.");
    }

    public static async Task<byte[]> GetImageAsync(HttpClient http, string token, string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("mobile.telkomuniversity.ac.id", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || uri.AbsolutePath is not ("/v2/file-management/get-by-id" or "/v2/file-management/get"))
            throw new InvalidDataException("This attachment is not hosted by MyTelU.");

        var bytes = await ReadAsync(http, token, uri, "*/*", 8 * 1024 * 1024, ct);
        if (bytes.Length == 0)
            throw new FileNotFoundException("The post image is unavailable.");
        return bytes;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpClient http, string token, string path, CancellationToken ct)
    {
        var bytes = await ReadAsync(http, token, new Uri(Origin + path), "application/json", 2 * 1024 * 1024, ct);
        return JsonDocument.Parse(bytes);
    }

    private static async Task<byte[]> ReadAsync(HttpClient http, string token, Uri uri, string accept, int maxBytes, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        return await SendAsync(http, token, request, accept, maxBytes, ct);
    }

    private static async Task<byte[]> SendAsync(HttpClient http, string token, HttpRequestMessage request, string accept, int maxBytes, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(http.Timeout);
        ct = timeout.Token;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd(accept);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(maxBytes, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static IEnumerable<JsonElement> ReadItems(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var success)
            || success.ValueKind != JsonValueKind.True || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("MyTelU did not return a Timeline list.");
        return data.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object);
    }

    private static string Text(JsonElement item, string key)
        => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    private static int Number(JsonElement item, string key, int fallback = 0)
        => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : fallback;

    private static bool Flag(JsonElement item, string key)
        => item.TryGetProperty(key, out var value) && (value.ValueKind == JsonValueKind.True
            || value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) && number == 1);

    private static string Author(JsonElement item)
    {
        string name = Text(item, "fullname");
        return string.IsNullOrWhiteSpace(name) ? "Telutizen" : name;
    }

    private static Uri? Photo(JsonElement item, string key)
        => Uri.TryCreate(Text(item, key), UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
            && uri.Host is "images.telkomuniversity.ac.id" or "cdndata.telkomuniversity.ac.id" or "igracias.telkomuniversity.ac.id"
            ? uri : null;

    private static string Timestamp(JsonElement item)
        => DateTimeOffset.TryParse(Text(item, "created_at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "";
}
