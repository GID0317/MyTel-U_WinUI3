using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MyTelU_Launcher.Models;

namespace MyTelU_Launcher.Services;

/// <summary>
/// Fetches attendance data directly from iGracias and keeps a local cache.
/// </summary>
public interface IAttendanceService
{
    /// <summary>Fetches attendance summary for the given semester.</summary>
    Task<AttendanceResponse?> GetAttendanceAsync(string? schoolYear = null, CancellationToken ct = default);

    /// <summary>Fetches per-session detail for a single course.</summary>
    Task<AttendanceCourseDetail?> GetCourseDetailAsync(int courseId, CancellationToken ct = default);

    /// <summary>Returns cached attendance from disk (no network).</summary>
    AttendanceResponse? GetCachedAttendance();

    /// <summary>True when an attendance cache file exists on disk.</summary>
    bool HasCachedAttendance { get; }

    /// <summary>Fetches the list of available semesters from the attendance page dropdown.</summary>
    Task<List<AcademicYearOption>> FetchAvailableSemestersAsync(CancellationToken ct = default);
}

public partial class AttendanceService : IAttendanceService, IDisposable
{
    private static readonly string _appDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TY4EHelper");

    private static readonly string _cacheFile = Path.Combine(_appDataDir, "attendance_cache.json");

    private static string DetailCacheFile(int courseId)
        => Path.Combine(_appDataDir, $"attendance_detail_{courseId}.json");

    private const string PresenceUrl =
        "https://igracias.telkomuniversity.ac.id/presence/index.php?pageid=3942";

    private const string DetailUrl =
        "https://igracias.telkomuniversity.ac.id/libraries/ajax/ajax.presence.php";

    public bool HasCachedAttendance => File.Exists(_cacheFile);

    public AttendanceResponse? GetCachedAttendance() => LoadCache();

    public async Task<AttendanceResponse?> GetAttendanceAsync(string? schoolYear = null, CancellationToken ct = default)
    {
        if (!HasSavedSession()) return null;

        try
        {
            schoolYear ??= GetSavedSchoolYear();

            if (string.IsNullOrEmpty(schoolYear))
            {
                var semesters = await FetchAvailableSemestersAsync(ct);
                var current = semesters.FirstOrDefault(s => s.IsSelected);
                schoolYear = current?.Value ?? ComputeDefaultSchoolYear();
            }

            var html = await FetchPresenceHtmlAsync(schoolYear, ct);
            if (html == null) return null;

            var courses = ParsePresenceHtml(html);
            if (courses == null) return null;

            var totalAttended = courses.Sum(c => c.Attended);
            var totalSessions = courses.Where(c => c.TotalSessions.HasValue).Sum(c => c.TotalSessions!.Value);
            var below75 = courses.Where(c => c.Percentage < 75).ToList();
            var below80 = courses.Where(c => c.Percentage < 80).ToList();

            var result = new AttendanceResponse
            {
                StudentId = GetSavedStudentId() ?? "unknown",
                AcademicYear = schoolYear,
                FetchTime = DateTime.Now.ToString("o"),
                Courses = courses,
                Summary = new AttendanceSummary
                {
                    TotalCourses = courses.Count,
                    TotalAttended = totalAttended,
                    TotalSessions = totalSessions > 0 ? totalSessions : null,
                    CoursesBelow75Pct = below75.Count,
                    CoursesBelow80Pct = below80.Count,
                    AtRiskCourses = below75.Select(c => c.CourseCode).ToList(),
                }
            };

            SaveCache(result);
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AttendanceService] GetAttendanceAsync error: {ex.Message}");
            return null;
        }
    }

    public async Task<AttendanceCourseDetail?> GetCourseDetailAsync(int courseId, CancellationToken ct = default)
    {
        if (!HasSavedSession())
            return LoadDetailCache(courseId);

        try
        {
            using var client = BuildHttpClient(ajax: true);
            var studentId = GetSavedStudentId() ?? "";

            var url = $"{DetailUrl}?act=getDataPresence&course={courseId}&hist=0";

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["course"] = courseId.ToString(),
                ["student"] = studentId,
            });

            var resp = await client.PostAsync(url, content, ct);
            if (!resp.IsSuccessStatusCode) return LoadDetailCache(courseId);

            var html = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(html)) return LoadDetailCache(courseId);

            var detail = ParseDetailHtml(html);
            SaveDetailCache(courseId, detail);
            return detail;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AttendanceService] GetCourseDetailAsync error: {ex.Message}");
            return LoadDetailCache(courseId);
        }
    }

    public async Task<List<AcademicYearOption>> FetchAvailableSemestersAsync(CancellationToken ct = default)
    {
        if (!HasSavedSession()) return new();

        try
        {
            using var client = BuildHttpClient(ajax: false);
            var resp = await client.GetAsync(PresenceUrl, ct);
            if (!resp.IsSuccessStatusCode) return new();

            var html = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(html) || html.Length < 500) return new();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var select = doc.DocumentNode.SelectSingleNode("//select[@id='changeSemester']");
            if (select == null) return new();

            var options = new List<AcademicYearOption>();
            foreach (var opt in select.SelectNodes(".//option") ?? Enumerable.Empty<HtmlNode>())
            {
                var value = opt.GetAttributeValue("value", "");
                var text = opt.InnerText.Trim();
                var isSelected = opt.GetAttributeValue("selected", null) != null;

                if (string.IsNullOrEmpty(value)) continue;

                var parts = value.Split('/');
                if (parts.Length == 2)
                {
                    options.Add(new AcademicYearOption
                    {
                        Value = value,
                        Text = isSelected ? $"{text} (Current)" : text,
                        YearCode = parts[0],
                        SemesterCode = parts[1],
                        IsSelected = isSelected,
                    });
                }
            }

            return options;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AttendanceService] FetchAvailableSemestersAsync error: {ex.Message}");
            return new();
        }
    }

    private async Task<string?> FetchPresenceHtmlAsync(string schoolYear, CancellationToken ct)
    {
        try
        {
            using var client = BuildHttpClient(ajax: false);
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["changeSemester"] = schoolYear,
            });

            var resp = await client.PostAsync(PresenceUrl, content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[AttendanceService] HTTP {resp.StatusCode}");
                return null;
            }

            var html = await resp.Content.ReadAsStringAsync(ct);

            if (html.Length < 500 || resp.RequestMessage?.RequestUri?.AbsoluteUri.Contains("login", StringComparison.OrdinalIgnoreCase) == true)
            {
                Debug.WriteLine("[AttendanceService] Session appears expired.");
                return null;
            }

            return html;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AttendanceService] FetchPresenceHtmlAsync error: {ex.Message}");
            return null;
        }
    }

    private static List<AttendanceCourseItem>? ParsePresenceHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var courses = new List<AttendanceCourseItem>();

        var rows = doc.DocumentNode.SelectNodes("//tr");
        if (rows == null) return courses;

        foreach (var tr in rows)
        {
            var tds = tr.SelectNodes("td[@class='tbf2']");
            if (tds == null || tds.Count < 7) continue;

            var cells = tds.Select(td => td.InnerText.Trim()).ToList();

            var courseCode = cells[1];
            var rawName = cells[2];
            var lecturer = cells[3];
            var attendedStr = cells[4];
            var totalStr = cells[5];
            var pctStr = cells[6];

            if (string.IsNullOrEmpty(courseCode) || string.IsNullOrEmpty(rawName))
                continue;

            string? classCode = null;
            var courseName = rawName;
            var classMatch = ClassCodeRegex().Match(rawName);
            if (classMatch.Success)
            {
                classCode = classMatch.Groups[1].Value.Trim();
                courseName = rawName[..classMatch.Index].Trim();
            }

            int? courseId = null;
            if (tds.Count >= 8)
            {
                var btn = tds[7].SelectSingleNode(".//button");
                if (btn != null)
                {
                    var onclick = btn.GetAttributeValue("onclick", "");
                    var idMatch = CourseIdRegex().Match(onclick);
                    if (idMatch.Success && int.TryParse(idMatch.Groups[1].Value, out var parsedId))
                        courseId = parsedId;
                }
            }

            int attended = int.TryParse(attendedStr, out var a) ? a : 0;
            int? total = int.TryParse(totalStr, out var t) ? t : null;

            var pctMatch = PercentageRegex().Match(pctStr);
            var pct = pctMatch.Success && double.TryParse(pctMatch.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0.0;

            courses.Add(new AttendanceCourseItem
            {
                CourseId = courseId,
                CourseCode = courseCode,
                CourseName = courseName,
                ClassCode = classCode,
                Lecturer = lecturer,
                Attended = attended,
                TotalSessions = total,
                Percentage = pct,
                PercentageStr = !string.IsNullOrEmpty(pctStr) ? pctStr : "0%",
            });
        }

        return courses;
    }

    private static AttendanceCourseDetail ParseDetailHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var courseCode = "";
        var courseName = "";
        var studentIdParsed = "";

        var headerB = doc.DocumentNode.SelectSingleNode("//b");
        if (headerB != null)
        {
            var parts = headerB.InnerText.Split('-', 3);
            if (parts.Length > 0) courseCode = parts[0].Trim();
            if (parts.Length > 1) courseName = parts[1].Trim();
            if (parts.Length > 2) studentIdParsed = parts[2].Trim();
        }

        var sessions = new List<AttendanceSessionDetail>();
        var table = doc.DocumentNode.SelectSingleNode("//table");
        if (table != null)
        {
            var rows = table.SelectNodes(".//tr");
            if (rows != null)
            {
                foreach (var row in rows.Skip(1))
                {
                    var cells = row.SelectNodes("td")?.Select(td => td.InnerText.Trim()).ToList();
                    if (cells == null || cells.Count < 9) continue;

                    int.TryParse(cells[0], out var no);
                    sessions.Add(new AttendanceSessionDetail
                    {
                        No = no,
                        Date = cells[1],
                        Day = cells[2],
                        Lecturer = cells[3],
                        StartTime = cells[4],
                        EndTime = cells[5],
                        Rfid = cells[6],
                        SessionType = cells[7],
                        Attendance = cells[8],
                        Content = cells.Count > 9 ? cells[9] : "",
                    });
                }
            }
        }

        return new AttendanceCourseDetail
        {
            CourseCode = courseCode,
            CourseName = courseName,
            StudentId = studentIdParsed,
            Sessions = sessions,
        };
    }

    private void SaveCache(AttendanceResponse data)
    {
        try
        {
            SecureFileStore.Save(_cacheFile, JsonSerializer.Serialize(data));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AttendanceService] SaveCache error: {ex.Message}");
        }
    }

    private AttendanceResponse? LoadCache()
    {
        try
        {
            var json = SecureFileStore.Load(_cacheFile);
            return json == null ? null : JsonSerializer.Deserialize<AttendanceResponse>(json);
        }
        catch { return null; }
    }

    private static void SaveDetailCache(int courseId, AttendanceCourseDetail detail)
    {
        try
        {
            Directory.CreateDirectory(_appDataDir);
            SecureFileStore.Save(DetailCacheFile(courseId), JsonSerializer.Serialize(detail));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AttendanceService] SaveDetailCache error: {ex.Message}");
        }
    }

    private static AttendanceCourseDetail? LoadDetailCache(int courseId)
    {
        try
        {
            var json = SecureFileStore.Load(DetailCacheFile(courseId));
            return json == null ? null : JsonSerializer.Deserialize<AttendanceCourseDetail>(json);
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

    private static string? GetSavedStudentId()
    {
        try
        {
            var settingsFile = Path.Combine(_appDataDir, "settings.json");
            if (!File.Exists(settingsFile)) return null;
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(settingsFile));
            return d?.TryGetValue("studentId", out var id) == true ? id : null;
        }
        catch { return null; }
    }

    private static string? GetSavedSchoolYear()
    {
        try
        {
            var settingsFile = Path.Combine(_appDataDir, "settings.json");
            if (!File.Exists(settingsFile)) return null;
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(settingsFile));
            if (d?.TryGetValue("yearCode", out var yr) == true && d.TryGetValue("semesterCode", out var sm) == true)
                return $"{yr}/{sm}";
            return null;
        }
        catch { return null; }
    }

    private static HttpClient BuildHttpClient(bool ajax)
    {
        try
        {
            var cookiesJson = CookieStore.Load() ?? "{}";
            var cookiesDict = JsonSerializer.Deserialize<Dictionary<string, string>>(cookiesJson)
                              ?? new Dictionary<string, string>();

            var jar = new CookieContainer();
            var baseUri = new Uri("https://igracias.telkomuniversity.ac.id");
            foreach (var kvp in cookiesDict)
                jar.Add(baseUri, new Cookie(kvp.Key, kvp.Value));

            var handler = new HttpClientHandler { CookieContainer = jar, AllowAutoRedirect = true };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", ajax
                ? "text/html, */*; q=0.01"
                : "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.Add("Referer", "https://igracias.telkomuniversity.ac.id/presence/");
            if (ajax)
                client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            return client;
        }
        catch
        {
            return new HttpClient();
        }
    }

    /// <summary>
    /// Computes the current academic semester string dynamically (e.g. "2526/2").
    /// Semester 1 = Aug–Jan, Semester 2 = Feb–Jul.
    /// </summary>
    private static string ComputeDefaultSchoolYear()
    {
        var now = DateTime.Now;
        int y1, y2;
        string sem;
        if (now.Month >= 8)
        {
            y1 = now.Year % 100;
            y2 = (now.Year + 1) % 100;
            sem = "1";
        }
        else
        {
            y1 = (now.Year - 1) % 100;
            y2 = now.Year % 100;
            sem = "2";
        }
        return $"{y1:D2}{y2:D2}/{sem}";
    }

    [GeneratedRegex(@"\(([^)]+)\)\s*$")]
    private static partial Regex ClassCodeRegex();

    [GeneratedRegex(@"getPresence\((\d+)")]
    private static partial Regex CourseIdRegex();

    [GeneratedRegex(@"([\d.]+)")]
    private static partial Regex PercentageRegex();

    public void Dispose() { /* nothing pooled */ }
}
