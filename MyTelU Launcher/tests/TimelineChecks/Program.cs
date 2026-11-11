using System.Net;
using MyTelU_Launcher.Services;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.Helpers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

const string Host = "https://mobile.telkomuniversity.ac.id";
const string Token = "synthetic-token";
int checks = 0;

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
    Console.WriteLine($"PASS {message}");
}

async Task Reject<T>(Func<Task> action, string message) where T : Exception
{
    try { await action(); }
    catch (T) { Check(true, message); return; }
    Check(false, message);
}

static HttpResponseMessage Reply(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content) };

var handler = new FixtureHandler((request, _) =>
{
    Check(request.Method == HttpMethod.Get && request.RequestUri!.GetLeftPart(UriPartial.Authority) == Host,
        "Only GET requests are sent to the official origin");
    Check(request.Headers.Authorization is { Scheme: "Bearer", Parameter: Token }, "Existing MyTelU bearer is used");
    return Task.FromResult(request.RequestUri!.PathAndQuery switch
    {
        "/v2/feeds/list?currentPage=2" => Reply("""
            {"success":true,"data":[{"id":42,"fullname":"Example student","content":"Example post",
              "photo":"https://images.telkomuniversity.ac.id/example.jpg",
              "image":"https://mobile.telkomuniversity.ac.id/v2/file-management/get-by-id?id=example",
              "created_at":"2026-11-11T01:30:00Z","likecount":3,"commentcount":1,"pinned":true,"likestatus":1,"commentable":false}],
             "metadata":{"currentPage":2,"totalPage":3}}
            """),
        "/v2/feeds/list?currentPage=3" => Reply("""{"success":true,"data":[],"metadata":{"currentPage":3,"totalPage":3}}"""),
        "/v2/feeds/list?currentPage=4" => Reply("""{"success":false,"data":[]}"""),
        "/v2/feeds/list?currentPage=1&search=campus%20%26%20workshop" => Reply("""{"success":true,"data":[],"metadata":{"currentPage":1,"totalPage":1}}"""),
        "/v2/feeds/comments/list?feedId=42" => Reply("""
            {"success":true,"data":[{"fullname":"Example commenter","comment_":"Example reply","created_at":"2026-11-11T01:45:00Z",
              "profile_photo":"https://images.telkomuniversity.ac.id/example-reply.jpg"},
             {"fullname":"Another commenter","comment_":"Another reply","profile_photo":"https://images.telkomuniversity.ac.id.example.test/photo.jpg"}]}
            """),
        "/v2/file-management/get-by-id?id=example" or "/v2/file-management/get?path=example" =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE1]) },
        _ => throw new InvalidOperationException("Unexpected route")
    });
});
using var http = new HttpClient(handler);

var page = await TimelineApi.GetPageAsync(http, Token, 2, default);
Check(page.CurrentPage == 2 && page.TotalPages == 3, "Feed uses currentPage and preserves pagination metadata");
var post = page.Posts.Single();
Check(post is { Id: 42, Author: "Example student", Content: "Example post", IsPinned: true, IsLiked: true, Likes: 3, Comments: 1, HasImage: true },
    "Feed maps author, content, counts, pinned status, and attachment");
Check(post.Timestamp.Length != 0, "Feed timestamp is formatted for display");
Check(!post.CanComment, "Disabled commenting is respected");
Check(post.Photo?.AbsoluteUri == "https://images.telkomuniversity.ac.id/example.jpg", "Feed preserves the official author photo");
Check((await TimelineApi.GetPageAsync(http, Token, 3, default)).Posts.Count == 0, "Empty pages load successfully");
Check((await TimelineApi.GetPageAsync(http, Token, 1, default, " campus & workshop ")).Posts.Count == 0,
    "Search is sent to the official API with trimmed and escaped query text");
await Reject<InvalidDataException>(() => TimelineApi.GetPageAsync(http, Token, 4, default), "API errors are not shown as empty pages");

var comments = await TimelineApi.GetCommentsAsync(http, Token, post.Id, default);
Check(comments[0] is { Author: "Example commenter", Content: "Example reply" }, "Comments use feedId and read comment_ text");
Check(comments[0].Photo?.AbsolutePath == "/example-reply.jpg", "Comments preserve the official profile photo");
Check(comments[1].Photo == null, "Non-university photo origins fall back to an avatar");
Check((await TimelineApi.GetImageAsync(http, Token, post.ImageUrl, default)).Length == 4, "Protected attachment bytes are fetched by ID");
Check((await TimelineApi.GetImageAsync(http, Token, Host + "/v2/file-management/get?path=example", default)).Length == 4,
    "Protected attachments with a path are supported");

int sent = handler.Count;
foreach (var url in new[]
{
    "https://example.test/v2/file-management/get-by-id?id=x",
    "https://mobile.telkomuniversity.ac.id.example.test/v2/file-management/get-by-id?id=x",
    "http://mobile.telkomuniversity.ac.id/v2/file-management/get-by-id?id=x",
    "https://user@mobile.telkomuniversity.ac.id/v2/file-management/get-by-id?id=x",
    "https://mobile.telkomuniversity.ac.id:444/v2/file-management/get-by-id?id=x",
    Host + "/v2/feeds/like?id=x"
})
    await Reject<InvalidDataException>(() => TimelineApi.GetImageAsync(http, Token, url, default), "Unsafe attachment URL is refused");
Check(handler.Count == sent, "No credential-bearing request is made for a refused attachment");

using var oversized = new HttpClient(new FixtureHandler((_, _) => Task.FromResult(Reply(new string('x', 2 * 1024 * 1024 + 1)))));
await Reject<HttpRequestException>(() => TimelineApi.GetPageAsync(oversized, Token, 1, default), "Oversized feed response is rejected");

using var missingImage = new HttpClient(new FixtureHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
{
    Content = new ByteArrayContent([])
})));
await Reject<FileNotFoundException>(() => TimelineApi.GetImageAsync(missingImage, Token, post.ImageUrl, default),
    "HTTP 200 with an empty attachment is reported as unavailable before image decoding");

using var cancelled = new CancellationTokenSource();
using var delayed = new HttpClient(new FixtureHandler(async (_, ct) =>
{
    cancelled.Cancel();
    await Task.Delay(Timeout.Infinite, ct);
    return Reply("");
}));
await Reject<OperationCanceledException>(() => TimelineApi.GetPageAsync(delayed, Token, 1, cancelled.Token), "Pending feed requests can be cancelled");

var reactions = new FixtureHandler(async (request, ct) =>
{
    Check(request.Method == HttpMethod.Post && request.RequestUri!.GetLeftPart(UriPartial.Authority) == Host,
        "Reaction uses POST on the official origin");
    Check(request.Headers.Authorization is { Scheme: "Bearer", Parameter: Token }, "Reaction uses the MyTelU bearer");
    Check(request.Content!.Headers.ContentType!.MediaType == "application/x-www-form-urlencoded"
        && await request.Content.ReadAsStringAsync(ct) == "feedId=42", "Reaction matches the CLI form body");
    return request.RequestUri!.AbsolutePath switch
    {
        "/v2/feeds/like" => Reply("""{"success":true}"""),
        "/v2/feeds/unlike" => Reply("""{"success":true}"""),
        _ => throw new InvalidOperationException("Unexpected reaction route")
    };
});
using var reactionHttp = new HttpClient(reactions);
await TimelineApi.SetLikeAsync(reactionHttp, Token, post.Id, false, default);
post.ApplyLike(false);
Check(!post.IsLiked && post.Likes == 2 && post.LikeLabel == "2 likes", "Unlike clears the liked state and decrements the count");
await TimelineApi.SetLikeAsync(reactionHttp, Token, post.Id, true, default);
post.ApplyLike(true);
post.ApplyLike(true);
Check(post.IsLiked && post.Likes == 3 && post.LikeAction == "Unlike post", "Like restores the state without double counting");
Check(reactions.Count == 2, "Each user action makes just one mocked reaction request");

using var rejectedReaction = new HttpClient(new FixtureHandler((_, _) => Task.FromResult(Reply("""{"success":false}"""))));
await Reject<InvalidDataException>(() => TimelineApi.SetLikeAsync(rejectedReaction, Token, post.Id, true, default),
    "Rejected reactions are not treated as successful");

var commentHandler = new FixtureHandler(async (request, ct) =>
{
    Check(request.Method == HttpMethod.Post && request.RequestUri!.AbsoluteUri == Host + "/v2/feeds/comments/create",
        "Comment targets only the official create endpoint");
    Check(request.Headers.Authorization is { Scheme: "Bearer", Parameter: Token }, "Comment uses the MyTelU bearer");
    Check(request.Content!.Headers.ContentType!.MediaType == "application/x-www-form-urlencoded"
        && await request.Content.ReadAsStringAsync(ct) == "feedId=42&comment=Example+%26+reply",
        "Comment matches the CLI fields and escapes user text");
    return Reply("""{"success":true}""");
});
using var commentHttp = new HttpClient(commentHandler);
await TimelineApi.AddCommentAsync(commentHttp, Token, post.Id, "  Example & reply  ", default);
post.CommentAdded();
Check(post.Comments == 2 && post.CommentLabel == "2 comments", "Confirmed comments update the post count");
await Reject<ArgumentException>(() => TimelineApi.AddCommentAsync(commentHttp, Token, post.Id, "  ", default),
    "Empty comments are rejected before sending");
Check(commentHandler.Count == 1, "Comment creation is not duplicated or retried");

using var pngStream = new InMemoryRandomAccessStream();
var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, pngStream);
encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, 1, 1, 96, 96, [0, 128, 255, 255]);
await encoder.FlushAsync();
byte[] png = new byte[(int)pngStream.Size];
using (var reader = new DataReader(pngStream.GetInputStreamAt(0)))
{
    await reader.LoadAsync((uint)png.Length);
    reader.ReadBytes(png);
}
const string postText = "Example post\nWith a second line.";
foreach (TimelinePostImage? attachment in new TimelinePostImage?[] { null, new("example.png", png) })
{
    var createHandler = new FixtureHandler(async (request, ct) =>
    {
        Check(request.Method == HttpMethod.Post && request.RequestUri!.AbsoluteUri == Host + "/v2/feeds/create",
            "New post targets only the official create route");
        Check(request.Headers.Authorization is { Scheme: "Bearer", Parameter: Token }, "New post uses the existing MyTelU session");
        Check(request.Content is MultipartFormDataContent && request.Content.Headers.ContentType!.MediaType == "multipart/form-data",
            "Both mobile post flows use multipart form data");
        var fields = ((MultipartFormDataContent)request.Content!).ToArray();
        Check(fields.Length == (attachment == null ? 1 : 2), "Only the expected form parts are sent");
        Check(fields[0].Headers.ContentDisposition!.Name == "content" && await fields[0].ReadAsStringAsync(ct) == postText,
            "The content field preserves the post's text and line breaks");
        if (attachment != null)
        {
            Check(fields[1].Headers.ContentDisposition is { Name: "image", FileName: "example.png" }, "Image has the expected field and filename");
            Check(fields[1].Headers.ContentType!.MediaType == "application/octet-stream"
                && (await fields[1].ReadAsByteArrayAsync(ct)).SequenceEqual(png), "Image is uploaded in the same request as binary data");
        }
        return new HttpResponseMessage(HttpStatusCode.OK);
    });
    using var createHttp = new HttpClient(createHandler);
    await TimelineApi.CreatePostAsync(createHttp, Token, postText, attachment, default);
    Check(createHandler.Count == 1, "A post requires exactly one request, with no separate upload");
}

var postFailure = new FixtureHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));
using (var createHttp = new HttpClient(postFailure))
{
    await Reject<ArgumentException>(() => TimelineApi.CreatePostAsync(createHttp, Token, "  ", null, default), "Blank posts never reach the handler");
    await Reject<ArgumentException>(() => TimelineApi.CreatePostAsync(createHttp, Token,
        new string('x', TimelineApi.MaxPostCharacters + 1), null, default), "Posts over 280 characters never reach the handler");
    await Reject<ArgumentException>(() => TimelineApi.CreatePostAsync(createHttp, Token, postText,
        new("large.png", new byte[TimelineApi.MaxPostImageBytes + 1]), default), "Attachments over 5 MiB never reach the handler");
    Check(postFailure.Count == 0, "Invalid post data triggers no requests");
    await Reject<HttpRequestException>(() => TimelineApi.CreatePostAsync(createHttp, Token, postText, null, default),
        "A failed create response is not treated as a published post");
    Check(postFailure.Count == 1, "Failed posts are not automatically retried");
}

string boundaryPost = new string('x', TimelineApi.MaxPostCharacters - 1) + "\U0001F44D\U0001F3FD";
Check(TimelineApi.CountPostCharacters(boundaryPost) == 280, "A combined emoji counts as one character at the limit");
var boundaryHandler = new FixtureHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
using (var boundaryHttp = new HttpClient(boundaryHandler))
    await TimelineApi.CreatePostAsync(boundaryHttp, Token, boundaryPost, null, default);
Check(boundaryHandler.Count == 1, "A post of exactly 280 characters is accepted");

var share = new DataPackage();
share.SetText("Example post");
await TimelineShareData.SetImageAsync(share, png, default);
var view = share.GetView();
Check(view.Contains(StandardDataFormats.Bitmap) && view.Contains(StandardDataFormats.Text), "Share offers both the image and post text");
Check(await view.GetTextAsync() == "Example post", "Adding an image preserves post text");
var reference = await view.GetBitmapAsync();
using (var sharedStream = await reference.OpenReadAsync())
{
    var decoder = await BitmapDecoder.CreateAsync(sharedStream);
    Check(decoder.PixelWidth == 1 && decoder.PixelHeight == 1, "Share package retains a readable image after preparation returns");
}
using (var thumbnail = await view.Properties.Thumbnail.OpenReadAsync())
    Check(thumbnail.Size == (ulong)png.Length, "Share includes an image thumbnail");
await Reject<Exception>(() => TimelineShareData.SetImageAsync(new DataPackage(), [0, 1, 2], default), "Invalid attachment is not supplied as an image");

foreach (var (text, expected) in new (string, string[])[]
{
    ("Plain text\nwith no link", []),
    ("Read https://example.test/path now.", ["https://example.test/path"]),
    ("Visit www.example.test/info.", ["www.example.test/info"]),
    ("First http://example.test/a then HTTPS://example.test/b", ["http://example.test/a", "HTTPS://example.test/b"]),
    ("(https://example.test/a).", ["https://example.test/a"]),
    ("https://example.test/wiki/Topic_(detail)", ["https://example.test/wiki/Topic_(detail)"]),
    ("https://example.test/?one=1&two=2#part", ["https://example.test/?one=1&two=2#part"]),
    ("javascript:alert(1) file:///C:/private data:text/html,hello", []),
    ("https://user:password@example.test/path", []),
    ("<https://example.test/path>\nThanks!", ["https://example.test/path"]),
    ("https://", [])
})
{
    var parts = WebLinkParser.Parse(text).ToArray();
    Check(string.Concat(parts.Select(p => p.Text)) == text, "Link rendering preserves all original text and line breaks");
    Check(parts.Where(p => p.Link != null).Select(p => p.Text).SequenceEqual(expected), "Web links are detected without unsafe schemes or trailing punctuation");
}

Console.WriteLine($"Verified {checks} Timeline checks with no live network requests.");

sealed class FixtureHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    public int Count { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Count++;
        return send(request, cancellationToken);
    }
}
