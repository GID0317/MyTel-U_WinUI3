using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using MyTelU_Launcher.Models;
using Json = MyTelU_Launcher.Core.Helpers.Json;

namespace MyTelU_Launcher.Services;

public sealed class MobileAcademicService : IDisposable
{
    private readonly object _cacheGate = new();
    private int _generation;
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        BaseAddress = new Uri("https://mobile.telkomuniversity.ac.id"),
        Timeout = TimeSpan.FromSeconds(30)
    };
    private static string ProfilePath => AppDataStore.GetFilePath("mobile_profile_cache.json");
    private static string TakPath => AppDataStore.GetFilePath("tak_cache.json");

    public StudentProfile? GetCachedProfile()
    {
        var json = SecureFileStore.Load(ProfilePath);
        if (json == null) return null;
        var profile = Json.Deserialize<StudentProfile>(json);
        if (profile != null) profile.IsCached = true;
        return profile;
    }

    public TakSummary? GetCachedTak()
    {
        var json = SecureFileStore.Load(TakPath);
        if (json == null) return null;
        var summary = Json.Deserialize<TakSummary>(json);
        if (summary != null) summary.IsCached = true;
        return summary;
    }

    public async Task<StudentProfile> GetProfileAsync(CancellationToken ct = default)
    {
        try
        {
            var session = await LoginAsync(ct);
            return session.Profile;
        }
        catch (Exception ex) when ((ex is HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested) && GetCachedProfile() != null)
        {
            return GetCachedProfile()!;
        }
    }

    public async Task<TakSummary> GetTakAsync(CancellationToken ct = default)
    {
        int generation = Volatile.Read(ref _generation);
        try
        {
            var session = await LoginAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"/v2/students/tak?nim={Uri.EscapeDataString(session.Profile.StudentId)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = document.RootElement;
            if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True
                || !root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("tak", out var score) || !score.TryGetInt32(out int points))
                throw new InvalidDataException("The server did not return a TAK score.");

            var summary = new TakSummary { Score = points, Ikk = Text(data, "ikk"), FetchTime = DateTimeOffset.Now.ToString("O") };
            SaveCache(TakPath, Json.Serialize(summary), generation);
            return summary;
        }
        catch (Exception ex) when ((ex is HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested) && GetCachedTak() != null)
        {
            return GetCachedTak()!;
        }
    }

    private async Task<(StudentProfile Profile, string Token)> LoginAsync(CancellationToken ct)
    {
        int generation = Volatile.Read(ref _generation);
        var (username, password) = CredentialStore.Load();
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Sign in to iGracias again to load your MyTelU student information.");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username, ["password"] = password, ["appVersion"] = "14855"
        });
        using var response = await _http.PostAsync("/v2/auth/login", content, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;
        if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True
            || !root.TryGetProperty("data", out var data) || string.IsNullOrWhiteSpace(Text(data, "authToken"))
            || string.IsNullOrWhiteSpace(Text(data, "numberid")))
            throw new InvalidDataException("MyTelU could not verify your student account.");

        var profile = new StudentProfile
        {
            StudentId = Text(data, "numberid"), FullName = Text(data, "fullname"),
            StudyProgram = Text(data, "studyprogram"), Faculty = Text(data, "faculty"),
            Class = Text(data, "studentclass"), Photo = Text(data, "photo"),
            SchoolYear = Text(data, "schoolyear"), StudentType = Text(data, "studenttypename"),
            FetchTime = DateTimeOffset.Now.ToString("O")
        };
        ct.ThrowIfCancellationRequested();
        SaveCache(ProfilePath, Json.Serialize(profile), generation);
        return (profile, Text(data, "authToken"));
    }

    private static string Text(JsonElement data, string key)
        => data.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : string.Empty;

    private void SaveCache(string path, string json, int generation)
    {
        lock (_cacheGate)
        {
            if (_generation != generation)
                throw new InvalidOperationException("The account session has changed.");
            SecureFileStore.Save(path, json);
        }
    }

    public void ResetSession()
    {
        lock (_cacheGate) _generation++;
        _http.CancelPendingRequests();
    }

    public void Dispose() => _http.Dispose();
}
