using System.Net;
using System.Net.Http;
using MyTelU_Launcher.Models;

namespace MyTelU_Launcher.Services;

public sealed partial class MobileAcademicService
{
    private string? _timelineToken;

    public Task<TimelinePageResult> GetTimelineAsync(int page, CancellationToken ct, string search = "")
        => ReadTimelineAsync((token, cancellation) => TimelineApi.GetPageAsync(_http, token, page, cancellation, search), ct);

    public Task<IReadOnlyList<TimelineComment>> GetTimelineCommentsAsync(int postId, CancellationToken ct)
        => ReadTimelineAsync((token, cancellation) => TimelineApi.GetCommentsAsync(_http, token, postId, cancellation), ct);

    public Task<byte[]> GetTimelineImageAsync(string url, CancellationToken ct)
        => ReadTimelineAsync((token, cancellation) => TimelineApi.GetImageAsync(_http, token, url, cancellation), ct);

    public async Task SetTimelineLikeAsync(int postId, bool liked, CancellationToken ct)
        => await WriteTimelineAsync((token, cancellation) => TimelineApi.SetLikeAsync(_http, token, postId, liked, cancellation), ct);

    public async Task AddTimelineCommentAsync(int postId, string comment, CancellationToken ct)
        => await WriteTimelineAsync((token, cancellation) => TimelineApi.AddCommentAsync(_http, token, postId, comment, cancellation), ct);

    internal Task CreateTimelinePostAsync(string content, TimelinePostImage? image, CancellationToken ct)
        => WriteTimelineAsync((token, cancellation) => TimelineApi.CreatePostAsync(_http, token, content, image, cancellation), ct);

    private async Task WriteTimelineAsync(Func<string, CancellationToken, Task> write, CancellationToken ct)
    {
        int generation = Volatile.Read(ref _generation);
        string token = await GetTimelineTokenAsync(generation, ct);
        Task pending;
        lock (_cacheGate)
        {
            CheckTimelineSession(generation, ct);
            pending = write(token, ct);
        }
        await pending;
        CheckTimelineSession(generation, ct);
    }

    private async Task<T> ReadTimelineAsync<T>(Func<string, CancellationToken, Task<T>> read, CancellationToken ct)
    {
        int generation = Volatile.Read(ref _generation);
        string token = await GetTimelineTokenAsync(generation, ct);
        T result;
        try
        {
            result = await read(token, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            lock (_cacheGate)
            {
                CheckTimelineSession(generation, ct);
                if (_timelineToken == token) _timelineToken = null;
            }
            token = await GetTimelineTokenAsync(generation, ct);
            result = await read(token, ct);
        }
        CheckTimelineSession(generation, ct);
        return result;
    }

    private async Task<string> GetTimelineTokenAsync(int generation, CancellationToken ct)
    {
        lock (_cacheGate)
        {
            CheckTimelineSession(generation, ct);
            if (_timelineToken != null) return _timelineToken;
        }
        var session = await LoginAsync(ct);
        lock (_cacheGate)
        {
            CheckTimelineSession(generation, ct);
            return _timelineToken = session.Token;
        }
    }

    private void CheckTimelineSession(int generation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (generation != Volatile.Read(ref _generation))
            throw new InvalidOperationException("The MyTelU account session has changed.");
    }
}
