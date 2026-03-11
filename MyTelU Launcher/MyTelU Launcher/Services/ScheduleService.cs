using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Net;
using System.Net.Http;
using MyTelU_Launcher.Models;
using HtmlAgilityPack;

namespace MyTelU_Launcher.Services;

public interface IScheduleService
{
    /// <summary>Compatibility member kept while callers still expect a server-backed service.</summary>
    bool IsServerRunning { get; }
    /// <summary>Compatibility member; schedule fetching now happens in-process.</summary>
    Task StartServerAsync();
    /// <summary>Compatibility member; schedule fetching now happens in-process.</summary>
    Task StopServerAsync();

    /// <summary>Try live fetch first, fall back to cache.</summary>
    Task<ScheduleResponse?> GetScheduleAsync();
    /// <summary>Try live fetch first, fall back to cache. Honours cancellation.</summary>
    Task<ScheduleResponse?> GetScheduleAsync(CancellationToken ct);
    /// <summary>Performs a live fetch only (no cache fallback). Honours cancellation.</summary>
    Task<ScheduleResponse?> GetLiveScheduleAsync(CancellationToken ct);

    /// <summary>Validates the current iGracias session.</summary>
    Task<SessionValidationResult> ValidateSessionAsync();

    Task<List<AcademicYearOption>> FetchAcademicYearsAsync();
    void SaveAcademicYear(string yearCode, string semesterCode);

    /// <summary>Returns the last saved academic year/semester codes, or (null,null) if none.</summary>
    (string? yearCode, string? semCode) GetSavedAcademicYear();

    /// <summary>True when cookies.json exists and has a PHPSESSID.</summary>
    bool HasSavedSession { get; }

    /// <summary>True when a schedule cache file exists on disk.</summary>
    bool HasCachedSchedule { get; }

    /// <summary>Reads the cached schedule from disk without any network call.</summary>
    ScheduleResponse? GetCachedSchedule();

    /// <summary>Returns whether a default network interface is available.</summary>
    bool IsNetworkAvailable();

    /// <summary>Clears the locally stored session and cached academic data for the current user.</summary>
    void ClearSession();
}

public class ScheduleService : IScheduleService, IDisposable
{
    private static readonly string _appDataDir = AppDataStore.DirectoryPath;

    private static readonly string _cookiesFile  = Path.Combine(_appDataDir, "cookies.json");
    private static readonly string _cacheFile    = Path.Combine(_appDataDir, "schedule_cache.json");
    private static readonly string _settingsFile = Path.Combine(_appDataDir, "settings.json");

    private readonly HttpClient _httpClient = new();

    public bool IsServerRunning => true;
    public Task StartServerAsync() => Task.CompletedTask;
    public Task StopServerAsync()  => Task.CompletedTask;

    public bool HasSavedSession
    {
        get
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
    }

    public bool HasCachedSchedule => SecureFileStore.Load(_cacheFile) != null;

    public ScheduleResponse? GetCachedSchedule() => LoadCache();

    public bool IsNetworkAvailable() =>
        NetworkInterface.GetIsNetworkAvailable();

    public void ClearSession()
    {
        AppDataStore.ClearAll();
    }

    public (string? yearCode, string? semCode) GetSavedAcademicYear()
    {
        var s = LoadSettings();
        s.TryGetValue("yearCode",      out var yr);
        s.TryGetValue("semesterCode",  out var sm);
        return (yr, sm);
    }

    /// <summary>
    /// Checks whether the registration page still renders a real logged-in schedule view.
    /// </summary>
    public async Task<SessionValidationResult> ValidateSessionAsync()
    {
        if (!HasSavedSession) return SessionValidationResult.NoSession;
        try
        {
            // The registration page only returns the full markup when the AJAX header is absent.
            using var client = BuildPageHttpClient();
            var resp = await client.GetAsync("https://igracias.telkomuniversity.ac.id/registration/?pageid=17985");
            if (!resp.IsSuccessStatusCode) return SessionValidationResult.NoSession;

            var html  = await resp.Content.ReadAsStringAsync();
            var lower = html.ToLowerInvariant();

            if (lower.Contains("name=\"username\"") && lower.Contains("name=\"password\""))
                return SessionValidationResult.NoSession;

            if (html.Contains("name=\"schoolYear\"") ||
                lower.Contains("logout") ||
                lower.Contains("sign out"))
                return SessionValidationResult.Valid;

            if (!lower.Contains("login")) return SessionValidationResult.Valid;

            return SessionValidationResult.NoSession;
        }
        catch (HttpRequestException)
        {
            return SessionValidationResult.NetworkError;
        }
        catch (TaskCanceledException)
        {
            return SessionValidationResult.NetworkError;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ScheduleService] ValidateSession error: {ex.Message}");
            return SessionValidationResult.NetworkError;
        }
    }

    public Task<ScheduleResponse?> GetScheduleAsync() =>
        GetScheduleAsync(CancellationToken.None);

    public async Task<ScheduleResponse?> GetScheduleAsync(CancellationToken ct)
    {
        var live = await TryFetchLive(ct);
        if (live != null)
        {
            SaveCache(live);
            return live;
        }
        if (ct.IsCancellationRequested) return null;
        return LoadCache();
    }

    public async Task<ScheduleResponse?> GetLiveScheduleAsync(CancellationToken ct)
    {
        var live = await TryFetchLive(ct);
        if (live != null) SaveCache(live);
        return live;
    }

    private async Task<ScheduleResponse?> TryFetchLive(CancellationToken ct = default)
    {
        if (!HasSavedSession) return null;
        try
        {
            ct.ThrowIfCancellationRequested();
            var settings  = LoadSettings();
            if (!settings.TryGetValue("studentId", out var studentId) || string.IsNullOrWhiteSpace(studentId))
            {
                return null;
            }

            string yearCode, semCode;
            if (settings.TryGetValue("yearCode", out var yr) && settings.TryGetValue("semesterCode", out var sm))
            {
                yearCode = yr;
                semCode  = sm;
            }
            else
            {
                var years   = await FetchAcademicYearsAsync();
                var current = years.FirstOrDefault(y => y.IsSelected);
                if (current != null)
                {
                    yearCode = current.YearCode;
                    semCode  = current.SemesterCode;
                    settings["yearCode"]      = yearCode;
                    settings["semesterCode"]  = semCode;
                    SaveSettings(settings);
                }
                else { yearCode = "2526"; semCode = "2"; }
            }

            string schoolYear = $"{yearCode}/{semCode}";
            using var client  = BuildHttpClient();

            // The DataTables response has the fields shown in the UI.
            var courses = await FetchScheduleListAsync(client, studentId, schoolYear, ct);
            if (courses == null) return null;

            // The HTML grid is only used to mark conflict highlights.
            var conflictCodes = courses.Count > 0
                ? await FetchConflictCodesAsync(client, studentId, yearCode, semCode, ct)
                : new HashSet<string>();

            foreach (var c in courses)
                c.HasConflict = conflictCodes.Contains(c.CourseCode ?? "");

            var timetable = new Dictionary<string, object>();
            foreach (var c in courses)
            {
                var day  = c.Day  ?? "";
                var slot = c.Time ?? "";
                if (day == "" || slot == "") continue;
                if (!timetable.ContainsKey(day))
                    timetable[day] = new Dictionary<string, object>();
                var dayDict = (Dictionary<string, object>)timetable[day];
                if (!dayDict.ContainsKey(slot))
                    dayDict[slot] = new List<object>();
                ((List<object>)dayDict[slot]).Add(new
                {
                    course_code  = c.CourseCode,
                    course_name  = c.CourseName,
                    room         = c.Room,
                    @class       = c.Class,
                    status       = c.Status,
                    has_conflict = c.HasConflict
                });
            }

            return new ScheduleResponse
            {
                StudentId    = studentId,
                FetchTime    = DateTime.Now.ToString("o"),
                AcademicYear = schoolYear,
                Courses      = courses,
                Timetable    = timetable
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ScheduleService] Live fetch error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Calls act=viewStudentSchedule (DataTables JSON) — primary course source.
    /// Column layout: [0]=Day [1]=TimeStart [2]=Room [3]=Code [4]=Name [5]=Lecturer [6]=Class [7]=TimeEnd [8]=Status
    /// </summary>
    private async Task<List<CourseItem>?> FetchScheduleListAsync(HttpClient client, string studentId, string schoolYear, CancellationToken ct = default)
    {
        var parts = new List<string>
        {
            "act=viewStudentSchedule",
            $"studentId={Uri.EscapeDataString(studentId)}",
            "sEcho=1", "iColumns=9", "sColumns=",
            "iDisplayStart=0", "iDisplayLength=200",
        };
        for (int i = 0; i < 9; i++) parts.Add($"mDataProp_{i}={i}");
        parts.Add("sSearch="); parts.Add("bRegex=false");
        for (int i = 0; i < 9; i++)
        {
            parts.Add($"sSearch_{i}=");
            parts.Add($"bRegex_{i}=false");
            parts.Add($"bSearchable_{i}=true");
            parts.Add($"bSortable_{i}=true");
        }
        parts.Add("iSortCol_0=0"); parts.Add("sSortDir_0=asc"); parts.Add("iSortingCols=1");
        parts.Add($"schoolYear={Uri.EscapeDataString(schoolYear)}");

        var url  = "https://igracias.telkomuniversity.ac.id/libraries/ajax/ajax.schedule.php?" + string.Join("&", parts);
        var resp = await client.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;

        var body = await resp.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body) || body.Trim() is "0" or "" or "null")
            return null;

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("aaData", out var aaData)) return null;

        var courses = new List<CourseItem>();
        foreach (var row in aaData.EnumerateArray())
        {
            var cols = row.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
            if (cols.Count < 9) continue;

            string timeStart = cols[1].Length >= 5 ? cols[1][..5] : cols[1]; // "10:30"
            string timeEnd   = cols[7].Length >= 5 ? cols[7][..5] : cols[7]; // "13:30"

            courses.Add(new CourseItem
            {
                Day        = cols[0].Trim(),
                Time       = $"{timeStart} - {timeEnd}",
                TimeStart  = timeStart,
                TimeEnd    = timeEnd,
                Room       = cols[2].Trim(),
                CourseCode = cols[3].Trim(),
                CourseName = cols[4].Trim(),
                Lecturer   = cols[5].Trim(),
                Class      = cols[6].Trim(),
                Status     = cols[8].Trim(),
                HasConflict = false
            });
        }
        return courses;
    }

    /// <summary>
    /// Calls act=previewSchedule (HTML grid) and returns all course codes that
    /// appear inside a yellow-background (#FFFF00) div — indicating a conflict.
    /// </summary>
    private async Task<HashSet<string>> FetchConflictCodesAsync(HttpClient client, string studentId, string yearCode, string semCode, CancellationToken ct = default)
    {
        var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var url  = $"https://igracias.telkomuniversity.ac.id/libraries/ajax/ajax.schedule.php?act=previewSchedule&studentid={studentId}&sch={yearCode}&sm={semCode}";
            var resp = await client.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return conflicts;

            var html = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(html) || html.Trim() is "0" or "null") return conflicts;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var divs = doc.DocumentNode.SelectNodes("//div[@style]");
            if (divs == null) return conflicts;

            foreach (var div in divs)
            {
                var style = div.GetAttributeValue("style", "");
                if (!style.Contains("#FFFF00", StringComparison.OrdinalIgnoreCase)) continue;

                var text = div.InnerText.Trim();
                if (!text.Contains('-')) continue;

                var code = text.Split('-', 2)[0].Trim();
                if (!string.IsNullOrEmpty(code))
                    conflicts.Add(code);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ScheduleService] Conflict detection error: {ex.Message}");
        }
        return conflicts;
    }

    // ── Cache helpers ────────────────────────────────────────────────────────
    private void SaveCache(ScheduleResponse schedule)
    {
        try
        {
            Directory.CreateDirectory(_appDataDir);
            SecureFileStore.Save(_cacheFile, JsonSerializer.Serialize(schedule));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ScheduleService] SaveCache error: {ex.Message}");
        }
    }

    private ScheduleResponse? LoadCache()
    {
        try
        {
            var json = SecureFileStore.Load(_cacheFile);
            if (json == null) return null;
            return JsonSerializer.Deserialize<ScheduleResponse>(json);
        }
        catch { return null; }
    }

    // ── Settings helpers ─────────────────────────────────────────────────────
    private Dictionary<string, string> LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsFile)) return new Dictionary<string, string>();
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_settingsFile));
            return d ?? new Dictionary<string, string>();
        }
        catch { return new Dictionary<string, string>(); }
    }

    private void SaveSettings(Dictionary<string, string> settings)
    {
        try
        {
            Directory.CreateDirectory(_appDataDir);
            File.WriteAllText(_settingsFile, JsonSerializer.Serialize(settings));
        }
        catch { }
    }

    public void SaveAcademicYear(string yearCode, string semesterCode)
    {
        var settings = LoadSettings();
        settings["yearCode"] = yearCode;
        settings["semesterCode"] = semesterCode;
        SaveSettings(settings);
    }

    public async Task<List<AcademicYearOption>> FetchAcademicYearsAsync()
    {
#if DEBUG
        var logFile = Path.Combine(_appDataDir, "academic_years_debug.log");
        // Keep the debug log small enough to inspect without manual cleanup.
        try { if (File.Exists(logFile) && new FileInfo(logFile).Length > 100_000) File.Delete(logFile); } catch { }
        void Log(string msg) {
            try { File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); } catch { }
        }
#else
        void Log(string msg) { }
#endif

        Log($"=== FetchAcademicYearsAsync called. HasSavedSession={HasSavedSession} ===");
        if (!HasSavedSession) { Log("No saved session, returning empty."); return new List<AcademicYearOption>(); }
        try
        {
            Log("Fetching page with BuildPageHttpClient...");
            // The registration page only returns the dropdown when the AJAX header is absent.
            using var client = BuildPageHttpClient();
            var url = "https://igracias.telkomuniversity.ac.id/registration/?pageid=17985";
            var resp = await client.GetAsync(url);
            Log($"HTTP {(int)resp.StatusCode} {resp.StatusCode}");
            if (!resp.IsSuccessStatusCode) return new List<AcademicYearOption>();

            var html = await resp.Content.ReadAsStringAsync();
            Log($"Response length: {html.Length} chars");

            // A short script-redirect page means the saved session is no longer usable.
            if (html.Length < 5000 && (html.Contains("window.location=") || html.Contains("window.location =")))
            {
                Log("Script redirect detected — session likely expired. Returning empty academic years list.");
                return new List<AcademicYearOption>();
            }

            Log($"Contains 'schoolYear': {html.Contains("schoolYear")}");
            Log($"Contains login form: {html.Contains("name=\"username\"")}");

#if DEBUG
            // Save the raw HTML for inspection (debug builds only)
            try { File.WriteAllText(Path.Combine(_appDataDir, "academic_years_page.html"), html); } catch { }
#endif

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var select = doc.DocumentNode.SelectSingleNode("//select[@name='schoolYear']");
            Log($"select[@name='schoolYear'] found: {select != null}");
            if (select == null) return new List<AcademicYearOption>();

            var options = new List<AcademicYearOption>();
            var optionNodes = select.SelectNodes(".//option");
            Log($"Option nodes count: {optionNodes?.Count ?? 0}");
            if (optionNodes != null)
            {
                foreach (var opt in optionNodes)
                {
                    var value = opt.GetAttributeValue("value", "");
                    var text = opt.InnerText.Trim();
                    var isSelected = opt.GetAttributeValue("selected", null) != null;
                    Log($"  option: value='{value}' text='{text}' selected={isSelected}");

                    if (!string.IsNullOrEmpty(value))
                    {
                        var parts = value.Split('/');
                        if (parts.Length == 2)
                        {
                            options.Add(new AcademicYearOption
                            {
                                Value        = value,
                                Text         = isSelected ? $"{text} (Current)" : text,
                                YearCode     = parts[0],
                                SemesterCode = parts[1],
                                IsSelected   = isSelected
                            });
                        }
                    }
                }
            }

            // Older years sometimes come back with missing semester entries, so probe them directly.
            if (options.Count > 0)
            {
                var semesterNames = new Dictionary<string, string>
                {
                    { "1", "GANJIL" }, { "2", "GENAP" }, { "3", "ANTARA" }
                };

                var validYearCodes = options
                    .Where(o => o.YearCode.Length == 4)
                    .Select(o => o.YearCode)
                    .Distinct()
                    .ToList();

                if (validYearCodes.Count > 0)
                {
                    var oldestYear     = validYearCodes.Min()!;
                    var yearLabel      = $"20{oldestYear[..2]}/20{oldestYear[2..]}";
                    var existingValues = new HashSet<string>(options.Select(o => o.Value));

                    var patches = new List<AcademicYearOption>();
                    foreach (var smCode in new[] { "1", "2", "3" })
                    {
                        var val = $"{oldestYear}/{smCode}";
                        if (existingValues.Contains(val)) continue;

                        try
                        {
                            var probeUrl = $"https://igracias.telkomuniversity.ac.id/registration/?pageid=17985&sch={oldestYear}&sm={smCode}";
                            Log($"  Gap-fill probe: {probeUrl}");
                            using var probeClient = BuildPageHttpClient();
                            var probeResp = await probeClient.GetAsync(probeUrl);
                            if (probeResp.IsSuccessStatusCode)
                            {
                                var probeBody = await probeResp.Content.ReadAsStringAsync();
                                Log($"  Gap-fill probe {val}: HTTP {(int)probeResp.StatusCode}, length={probeBody.Length}");
                                if (probeBody.Length > 10000)
                                {
                                    patches.Add(new AcademicYearOption
                                    {
                                        Value        = val,
                                        Text         = $"{yearLabel} - {semesterNames[smCode]}",
                                        YearCode     = oldestYear,
                                        SemesterCode = smCode,
                                        IsSelected   = false
                                    });
                                    Log($"  Gap-fill: added missing semester {val}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"  Gap-fill probe failed for {val}: {ex.Message}");
                        }
                    }

                    if (patches.Count > 0)
                    {
                        var insertIdx = options.FindIndex(o => o.YearCode == oldestYear);
                        if (insertIdx >= 0)
                            options.InsertRange(insertIdx, patches.OrderBy(p => p.SemesterCode));
                        else
                            options.AddRange(patches.OrderBy(p => p.SemesterCode));
                        Log($"  Gap-fill: inserted {patches.Count} patch(es) for {oldestYear}");
                    }
                }
            }

            Log($"Returning {options.Count} option(s).");
            return options;
        }
        catch (Exception ex)
        {
            Log($"Exception: {ex.Message}");
            Debug.WriteLine($"[ScheduleService] FetchAcademicYears error: {ex.Message}");
            return new List<AcademicYearOption>();
        }
    }

    /// <summary>
    /// Builds an HttpClient with AJAX headers for DataTables / AJAX endpoints.
    /// </summary>
    private HttpClient BuildHttpClient()
    {
        try
        {
            var cookiesJson = CookieStore.Load() ?? "{}";
            var cookiesDict = JsonSerializer.Deserialize<Dictionary<string, string>>(cookiesJson)
                              ?? new Dictionary<string, string>();

            var jar     = new CookieContainer();
            var baseUri = new Uri("https://igracias.telkomuniversity.ac.id");
            foreach (var kvp in cookiesDict)
                jar.Add(baseUri, new Cookie(kvp.Key, kvp.Value));

            var handler = new HttpClientHandler { CookieContainer = jar };
            var client  = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            client.DefaultRequestHeaders.Add("Accept", "text/html, */*; q=0.01");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.Add("Referer", "https://igracias.telkomuniversity.ac.id/");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            return client;
        }
        catch
        {
            return new HttpClient();
        }
    }

    /// <summary>
    /// Builds an HttpClient for fetching full HTML pages (no AJAX headers).
    /// The X-Requested-With header must be absent so the server returns the full page
    /// including the schoolYear &lt;select&gt; dropdown.
    /// </summary>
    private HttpClient BuildPageHttpClient()
    {
        try
        {
            var cookiesJson = CookieStore.Load() ?? "{}";
            var cookiesDict = JsonSerializer.Deserialize<Dictionary<string, string>>(cookiesJson)
                              ?? new Dictionary<string, string>();

            var jar     = new CookieContainer();
            var baseUri = new Uri("https://igracias.telkomuniversity.ac.id");
            foreach (var kvp in cookiesDict)
                jar.Add(baseUri, new Cookie(kvp.Key, kvp.Value));

            var handler = new HttpClientHandler { CookieContainer = jar };
            var client  = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            client.DefaultRequestHeaders.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.Add("Referer", "https://igracias.telkomuniversity.ac.id/");
            return client;
        }
        catch
        {
            return new HttpClient();
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
