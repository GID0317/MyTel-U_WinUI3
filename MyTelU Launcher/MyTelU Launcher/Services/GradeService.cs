using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using HtmlAgilityPack;
using MyTelU_Launcher.Models;

namespace MyTelU_Launcher.Services;

public interface IGradeService
{
    /// <summary>True when a grade cache file exists on disk.</summary>
    bool HasCachedGrades { get; }

    /// <summary>Reads the cached grades from disk without any network call.</summary>
    GradeResponse? GetCachedGrades();

    /// <summary>Fetches grades from iGracias for the requested year and semester filters.</summary>
    Task<GradeResponse?> GetGradesAsync(string schoolYear = "", string semester = "", CancellationToken ct = default);

    /// <summary>Fetches component score breakdown for one course by its internal_id.</summary>
    Task<List<GradeComponentScore>> GetCourseDetailAsync(string internalId, CancellationToken ct = default);

    /// <summary>Scrapes available academic years from the score page &lt;select name="schoolYear"&gt; dropdown.</summary>
    Task<List<AcademicYearOption>> FetchAcademicYearsAsync(CancellationToken ct = default);
}

/// <summary>
/// Fetches grade data directly from iGracias.
/// The score page has to be opened before the AJAX endpoints will return data.
/// </summary>
public class GradeService : IGradeService, IDisposable
{
    private static readonly string _appDataDir = AppDataStore.DirectoryPath;

    private static readonly string _cacheFile = Path.Combine(_appDataDir, "grade_cache.json");
    private static readonly string _componentCacheFile = Path.Combine(_appDataDir, "grade_component_cache.json");

    private const string ScorePageUrl =
        "https://igracias.telkomuniversity.ac.id/score/?pageid=11";

    private const string AjaxUrl =
        "https://igracias.telkomuniversity.ac.id/libraries/ajax/ajax.score.php";

    public bool HasCachedGrades => File.Exists(_cacheFile);

    public GradeResponse? GetCachedGrades() => LoadCache();

    public async Task<List<AcademicYearOption>> FetchAcademicYearsAsync(CancellationToken ct = default)
    {
        if (!HasSavedSession()) return new();

        try
        {
            using var client = BuildHttpClient(ajax: false);
            var resp = await client.GetAsync(ScorePageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) return new();

            var html = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(html) || html.Length < 500) return new();

            return ParseAcademicYears(html);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GradeService] FetchAcademicYearsAsync error: {ex.Message}");
            return new();
        }
    }

    public async Task<GradeResponse?> GetGradesAsync(string schoolYear = "", string semester = "", CancellationToken ct = default)
    {
        if (!HasSavedSession()) return null;

        try
        {
            using var client = BuildHttpClient(ajax: true);

            var pageResp = await client.GetAsync(ScorePageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!pageResp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[GradeService] Score page returned {pageResp.StatusCode}");
                return null;
            }

            var pageHtml = await pageResp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(pageHtml) || pageHtml.Length < 500)
            {
                Debug.WriteLine("[GradeService] Score page too short — session likely expired.");
                return null;
            }

            var ajaxUrl = BuildAjaxUrl(schoolYear, semester);

            using var ajaxClient = BuildHttpClient(ajax: true);
            ajaxClient.DefaultRequestHeaders.Referrer = new Uri(ScorePageUrl);

            var resp = await ajaxClient.GetAsync(ajaxUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[GradeService] AJAX returned {resp.StatusCode}");
                return null;
            }

            await using var responseStream = await resp.Content.ReadAsStreamAsync(ct);
            if (resp.Content.Headers.ContentLength == 0)
            {
                Debug.WriteLine("[GradeService] Session expired or access denied.");
                return null;
            }

            List<GradeItem> grades;
            try { grades = await ParseGradesAsync(responseStream, ct); }
            catch (JsonException ex)
            {
                Debug.WriteLine($"[GradeService] JSON parse error: {ex.Message}");
                return null;
            }

            var result = new GradeResponse
            {
                Total      = grades.Count,
                Grades     = grades,
                FetchTime  = DateTime.Now.ToString("o"),
                SchoolYear = schoolYear,
                Semester   = semester,
            };

            SaveCache(result);
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GradeService] GetGradesAsync error: {ex.Message}");
            return null;
        }
    }

    public async Task<List<GradeComponentScore>> GetCourseDetailAsync(string internalId, CancellationToken ct = default)
    {
        if (!HasSavedSession())
            return LoadComponentCache(internalId) ?? new();

        try
        {
            using var client = BuildHttpClient(ajax: true);

            await client.GetAsync(ScorePageUrl, HttpCompletionOption.ResponseHeadersRead, ct);

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["rId"] = internalId,
            });

            var resp = await client.PostAsync($"{AjaxUrl}?act=getcomponentscore", content, ct);
            if (!resp.IsSuccessStatusCode) return LoadComponentCache(internalId) ?? new();

            await using var responseStream = await resp.Content.ReadAsStreamAsync(ct);
            if (resp.Content.Headers.ContentLength == 0) return LoadComponentCache(internalId) ?? new();

            List<GradeComponentScore> components;
            try { components = await ParseComponentScoresAsync(responseStream, ct); }
            catch { return LoadComponentCache(internalId) ?? new(); }

            if (components.Count > 0)
                SaveComponentCache(internalId, components);

            return components;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GradeService] GetCourseDetailAsync error: {ex.Message}");
            return LoadComponentCache(internalId) ?? new();
        }
    }

    private static async Task<List<GradeItem>> ParseGradesAsync(Stream jsonStream, CancellationToken ct)
    {
        using var doc = await JsonDocument.ParseAsync(jsonStream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("aaData", out var aaData)
            || aaData.ValueKind != JsonValueKind.Array)
            return new();

        var grades = new List<GradeItem>(aaData.GetArrayLength());

        foreach (var row in aaData.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array)
                continue;

            string courseCode = string.Empty;
            string courseName = string.Empty;
            int credits = 0;
            string period = string.Empty;
            string grade = string.Empty;
            string activeStr = string.Empty;
            string internalId = string.Empty;

            int index = 0;
            foreach (var cell in row.EnumerateArray())
            {
                switch (index)
                {
                    case 0:
                        courseCode = cell.GetString() ?? string.Empty;
                        break;
                    case 1:
                        courseName = cell.GetString() ?? string.Empty;
                        break;
                    case 2:
                        credits = ParseInt32(cell);
                        break;
                    case 3:
                        period = cell.GetString() ?? string.Empty;
                        break;
                    case 4:
                        grade = cell.GetString() ?? string.Empty;
                        break;
                    case 6:
                        activeStr = cell.GetString() ?? string.Empty;
                        break;
                    case 7:
                        internalId = cell.GetString() ?? string.Empty;
                        break;
                }

                index++;
                if (index > 7)
                    break;
            }

            if (index < 8)
                continue;

            SplitPeriod(period, out var schoolYear, out var semester);

            grades.Add(new GradeItem
            {
                CourseCode = courseCode,
                CourseName = courseName,
                Credits = credits,
                Period = period,
                SchoolYear = schoolYear,
                Semester = semester,
                Grade = grade,
                Active = string.Equals(activeStr, "Y", StringComparison.OrdinalIgnoreCase),
                InProgress = string.IsNullOrEmpty(grade),
                InternalId = internalId,
            });
        }

        return grades;
    }

    private static async Task<List<GradeComponentScore>> ParseComponentScoresAsync(Stream jsonStream, CancellationToken ct)
    {
        using var doc = await JsonDocument.ParseAsync(jsonStream, cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return new();

        var components = new List<GradeComponentScore>(doc.RootElement.GetArrayLength());
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var component = item.TryGetProperty("COMPONENTNAME", out var cn)
                ? cn.GetString() ?? string.Empty
                : string.Empty;
            var score = item.TryGetProperty("TSCORE", out var ts)
                ? ParseDouble(ts)
                : 0;
            var percentage = item.TryGetProperty("PERCENTAGE", out var pct)
                ? ParseInt32(pct)
                : 0;

            components.Add(new GradeComponentScore
            {
                Component = component,
                Score = score,
                Percentage = percentage,
            });
        }

        return components;
    }

    private static int ParseInt32(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
            return number;

        return int.TryParse(element.GetString(), out var parsed) ? parsed : 0;
    }

    private static double ParseDouble(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number))
            return number;

        return double.TryParse(
            element.GetString(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0;
    }

    private static void SplitPeriod(string period, out string schoolYear, out string semester)
    {
        if (string.IsNullOrEmpty(period))
        {
            schoolYear = string.Empty;
            semester = string.Empty;
            return;
        }

        var separator = period.IndexOf('/');
        if (separator < 0)
        {
            schoolYear = period;
            semester = string.Empty;
            return;
        }

        schoolYear = separator == 0 ? string.Empty : period[..separator];
        semester = separator + 1 < period.Length ? period[(separator + 1)..] : string.Empty;
    }

    /// <summary>Builds the AJAX GET URL with the DataTables parameters iGracias expects.</summary>
    private static string BuildAjaxUrl(string schoolYear, string semester)
    {
        var sb = new StringBuilder(AjaxUrl);
        sb.Append("?act=viewCompleteScoreStudent");
        sb.Append("&sEcho=1");
        sb.Append("&iColumns=20");
        sb.Append("&sColumns=");
        sb.Append("&iDisplayStart=0");
        sb.Append("&iDisplayLength=1000");
        sb.Append($"&schoolYear={Uri.EscapeDataString(schoolYear)}");
        sb.Append($"&semester={Uri.EscapeDataString(semester)}");
        for (int i = 0; i < 20; i++)
            sb.Append($"&mDataProp_{i}={i}");
        return sb.ToString();
    }

    private static List<AcademicYearOption> ParseAcademicYears(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var select = doc.DocumentNode.SelectSingleNode("//select[@name='schoolYear']");
        if (select == null) return new();

        var options = new List<AcademicYearOption>();
        foreach (var opt in select.SelectNodes(".//option") ?? Enumerable.Empty<HtmlNode>())
        {
            var value      = opt.GetAttributeValue("value", "").Trim();
            var text       = opt.InnerText.Trim();
            var isSelected = opt.GetAttributeValue("selected", null) != null;

            if (string.IsNullOrEmpty(value)) continue;

            options.Add(new AcademicYearOption
            {
                Value        = value,
                Text         = isSelected ? $"{text} (Current)" : text,
                YearCode     = value,
                SemesterCode = "",
                IsSelected   = isSelected,
            });
        }

        return options;
    }

    private static readonly object _componentCacheLock = new();

    private static void SaveComponentCache(string internalId, List<GradeComponentScore> components)
    {
        lock (_componentCacheLock)
        {
            try
            {
                Directory.CreateDirectory(_appDataDir);
                // Keep the whole read-modify-write cycle under one lock.
                var json = SecureFileStore.Load(_componentCacheFile);
                var dict = json == null
                    ? new Dictionary<string, List<GradeComponentScore>>()
                    : JsonSerializer.Deserialize<Dictionary<string, List<GradeComponentScore>>>(json)
                      ?? new Dictionary<string, List<GradeComponentScore>>();
                dict[internalId] = components;
                SecureFileStore.Save(_componentCacheFile, JsonSerializer.Serialize(dict));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GradeService] SaveComponentCache error: {ex.Message}");
            }
        }
    }

    private static List<GradeComponentScore>? LoadComponentCache(string internalId)
    {
        try
        {
            var dict = LoadAllComponentCache();
            return dict != null && dict.TryGetValue(internalId, out var data) ? data : null;
        }
        catch { return null; }
    }

    private static Dictionary<string, List<GradeComponentScore>>? LoadAllComponentCache()
    {
        try
        {
            var json = SecureFileStore.Load(_componentCacheFile);
            return json == null ? null : JsonSerializer.Deserialize<Dictionary<string, List<GradeComponentScore>>>(json);
        }
        catch { return null; }
    }

    private static void SaveCache(GradeResponse data)
    {
        try
        {
            Directory.CreateDirectory(_appDataDir);
            SecureFileStore.Save(_cacheFile, JsonSerializer.Serialize(data));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GradeService] SaveCache error: {ex.Message}");
        }
    }

    private static GradeResponse? LoadCache()
    {
        try
        {
            var json = SecureFileStore.Load(_cacheFile);
            return json == null ? null : JsonSerializer.Deserialize<GradeResponse>(json);
        }
        catch { return null; }
    }

    private static bool HasSavedSession()
    {
        try
        {
            var json = CookieStore.Load();
            if (json == null) return false;
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return d != null && d.ContainsKey("PHPSESSID");
        }
        catch { return false; }
    }

    private static HttpClient BuildHttpClient(bool ajax)
    {
        try
        {
            var cookiesJson  = CookieStore.Load() ?? "{}";
            var cookiesDict  = JsonSerializer.Deserialize<Dictionary<string, string>>(cookiesJson)
                               ?? new Dictionary<string, string>();

            var jar     = new CookieContainer();
            var baseUri = new Uri("https://igracias.telkomuniversity.ac.id");
            foreach (var kvp in cookiesDict)
                jar.Add(baseUri, new Cookie(kvp.Key, kvp.Value));

            var handler = new HttpClientHandler
            {
                CookieContainer = jar,
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            };
            var client  = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", ajax
                ? "application/json, text/javascript, */*; q=0.01"
                : "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.Add("Referer",
                "https://igracias.telkomuniversity.ac.id/score/");
            if (ajax)
                client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

            return client;
        }
        catch
        {
            return new HttpClient();
        }
    }

    public void Dispose() { }
}
