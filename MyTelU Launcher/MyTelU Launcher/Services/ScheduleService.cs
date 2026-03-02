using System.Diagnostics;
using System.Text.Json;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using MyTelU_Launcher.Models;
using HtmlAgilityPack;

namespace MyTelU_Launcher.Services;

/// <summary>
/// Result of a session validation attempt — distinguishes a missing/invalid session
/// from a transient network failure so the UI can show cached data when offline.
/// </summary>
public enum SessionValidationResult
{
    /// <summary>Cookies exist and the server confirmed the session is active.</summary>
    Valid,
    /// <summary>No cookies on disk, or the server returned a login page.</summary>
    NoSession,
    /// <summary>Network is unreachable or the request timed out.</summary>
    NetworkError,
}

public interface IScheduleService
{
    /// <summary>Legacy stub — always true now (no server needed).</summary>
    bool IsServerRunning { get; }
    /// <summary>Legacy stub — no-op now.</summary>
    Task StartServerAsync();
    /// <summary>Legacy stub — no-op now.</summary>
    Task StopServerAsync();
    Task<ScheduleResponse?> GetScheduleAsync(CancellationToken ct = default);
    Task<SessionValidationResult> ValidateSessionAsync();
    Task<List<AcademicYearOption>> FetchAcademicYearsAsync();
    void SaveAcademicYear(string yearCode, string semesterCode);
    /// <summary>Returns the saved yearCode and semesterCode from settings, or nulls if not saved.</summary>
    (string? YearCode, string? SemesterCode) GetSavedAcademicYear();
    /// <summary>Deletes saved cookies, clearing the iGracias session.</summary>
    void ClearSession();
    /// <summary>True when cookies.json exists and has a PHPSESSID.</summary>
    bool HasSavedSession { get; }
    /// <summary>True when a schedule_cache.json exists on disk.</summary>
    bool HasCachedSchedule { get; }
    /// <summary>Reads the cached schedule from disk without any network call. Returns null if no cache.</summary>
    ScheduleResponse? GetCachedSchedule();
    /// <summary>Quick local check — true if at least one network interface is up.</summary>
    bool IsNetworkAvailable();
}

public class ScheduleService : IScheduleService, IDisposable
{
    private static readonly string _appDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TY4EHelper");

    private static readonly string _cacheFile    = Path.Combine(_appDataDir, "schedule_cache.json");
    private static readonly string _settingsFile = Path.Combine(_appDataDir, "settings.json");

    private readonly HttpClient _httpClient = new();
    private readonly object _settingsLock = new();

    // ── IScheduleService stubs (kept so HomeViewModel compiles without changes) ──
    public bool IsServerRunning => true;   // No Python server required any more.
    public Task StartServerAsync() => Task.CompletedTask;
    public Task StopServerAsync()  => Task.CompletedTask;

    // ── Session / cookie helpers ─────────────────────────────────────────────
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

    public void ClearSession()
    {
        CookieStore.Clear();
        try { if (File.Exists(_cacheFile)) File.Delete(_cacheFile); } catch { }
        // Do NOT delete _settingsFile here — it stores the user's academic year
        // preference which is independent of the session and must survive logouts/restarts.
    }

    public bool HasCachedSchedule => File.Exists(_cacheFile);

    public ScheduleResponse? GetCachedSchedule() => LoadCache();

    /// <summary>
    /// Quick synchronous check using OS network interfaces — no HTTP required.
    /// Returns false in airplane mode / no adapters / all adapters disconnected.
    /// </summary>
    public bool IsNetworkAvailable() => NetworkInterface.GetIsNetworkAvailable();

    /// <summary>
    /// Validates the session by fetching the /schedule page and checking for the
    /// academic-year dropdown — the same technique as the Python validate_session.py.
    /// Returns false immediately if no cookies are saved.
    /// </summary>
    public async Task<SessionValidationResult> ValidateSessionAsync()
    {
        if (!HasSavedSession) return SessionValidationResult.NoSession;
        try
        {
            // Use page client (no X-Requested-With) so the server returns the full HTML page.
            using var client = BuildPageHttpClient();
            var resp = await client.GetAsync("https://igracias.telkomuniversity.ac.id/registration/?pageid=17985");
            if (!resp.IsSuccessStatusCode) return SessionValidationResult.NoSession;

            var html = await resp.Content.ReadAsStringAsync();
            var lower = html.ToLowerInvariant();

            // Login detected?
            if (lower.Contains("name=\"username\"") && lower.Contains("name=\"password\""))
                return SessionValidationResult.NoSession;

            // Logged in indicators
            if (html.Contains("name=\"schoolYear\"") || 
                lower.Contains("logout") || 
                lower.Contains("sign out"))
                return SessionValidationResult.Valid;

            // If we fetched successfully and it's not a login form, assume OK.
            if (!lower.Contains("login")) return SessionValidationResult.Valid;

            return SessionValidationResult.NoSession;
        }
        catch (HttpRequestException ex)
        {
            // Network unreachable / DNS failure / timeout
            Debug.WriteLine($"[ScheduleService] ValidateSession network error (offline?): {ex.Message}");
            return SessionValidationResult.NetworkError;
        }
        catch (TaskCanceledException ex)
        {
            Debug.WriteLine($"[ScheduleService] ValidateSession timeout: {ex.Message}");
            return SessionValidationResult.NetworkError;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ScheduleService] ValidateSession error: {ex.Message}");
            return SessionValidationResult.NetworkError;
        }
    }

    // ── Schedule fetch / cache ───────────────────────────────────────────────
    public async Task<ScheduleResponse?> GetScheduleAsync(CancellationToken ct = default)
    {
        // 1. Try live fetch
        var live = await TryFetchLive(ct);
        if (live != null)
        {
            SaveCache(live);
            return live;
        }

        // 2. Fall back to last cached data
        var cached = LoadCache();
        
        // Only return cache if it matches the current settings (or if we can't determine current settings)
        // Check local settings
        var settings = LoadSettings();
        if (settings.TryGetValue("yearCode", out var yr) && settings.TryGetValue("semesterCode", out var sm))
        {
            var expectedYear = $"{yr}/{sm}";
            if (cached != null && cached.AcademicYear != expectedYear)
            {
                // Cache mismatch - better to show error/empty than wrong data
                return null;
            }
        }
        
        return cached;
    }

    private async Task<ScheduleResponse?> TryFetchLive(CancellationToken ct)
    {
        if (!HasSavedSession) return null;
        try
        {
            var settings  = LoadSettings();
            if (!settings.TryGetValue("studentId", out var studentId) || string.IsNullOrWhiteSpace(studentId))
                return null; // Student ID not yet captured — cannot fetch without it

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
                    SaveSettings(settings); // This uses lock now, safe.
                }
                else { (yearCode, semCode) = ComputeDefaultAcademicYear(); }
            }

            string schoolYear = $"{yearCode}/{semCode}";
            using var client  = BuildHttpClient();

            // ── SOURCE 1: JSON DataTables list (primary) ─────────────────────
            var courses = await FetchScheduleListAsync(client, studentId, schoolYear, ct);
            if (courses == null) return null;

            // ── SOURCE 2: HTML grid (supplementary – conflict detection only) ─
            var conflictCodes = new HashSet<string>();
            if (courses.Count > 0)
            {
               conflictCodes = await FetchConflictCodesAsync(client, studentId, yearCode, semCode, ct);
            }

            // ── MERGE: apply conflict flags ───────────────────────────────────
            foreach (var c in courses)
                c.HasConflict = conflictCodes.Contains(c.CourseCode ?? "");

            // Check cancellation before processing large dataset
            if (ct.IsCancellationRequested) return null;

            // ── BUILD timetable index ─────────────────────────────────────────
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
    private async Task<List<CourseItem>?> FetchScheduleListAsync(HttpClient client, string studentId, string schoolYear, CancellationToken ct)
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
        
        // If "aaData" is missing or null, treat as empty schedule rather than failure
        // Some backends return aaData:null or omit it when empty.
        JsonElement aaData;
        if (!doc.RootElement.TryGetProperty("aaData", out aaData) || aaData.ValueKind == JsonValueKind.Null)
        {
            // success, just no data
            return new List<CourseItem>();
        }

        if (aaData.ValueKind != JsonValueKind.Array)
        {
             // Not an array - likely an error or unexpected format
             return null;
        }

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
    private async Task<HashSet<string>> FetchConflictCodesAsync(HttpClient client, string studentId, string yearCode, string semCode, CancellationToken ct)
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
            return json == null ? null : JsonSerializer.Deserialize<ScheduleResponse>(json);
        }
        catch { return null; }
    }

    // ── Settings helpers ─────────────────────────────────────────────────────
    private Dictionary<string, string> LoadSettings()
    {
        lock (_settingsLock)
        {
            try
            {
                if (!File.Exists(_settingsFile)) return new Dictionary<string, string>();
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_settingsFile));
                return d ?? new Dictionary<string, string>();
            }
            catch { return new Dictionary<string, string>(); }
        }
    }

    private void SaveSettings(Dictionary<string, string> settings)
    {
        lock (_settingsLock)
        {
            try
            {
                Directory.CreateDirectory(_appDataDir);
                File.WriteAllText(_settingsFile, JsonSerializer.Serialize(settings));
            }
            catch { }
        }
    }

    public void SaveAcademicYear(string yearCode, string semesterCode)
    {
        var settings = LoadSettings();
        settings["yearCode"] = yearCode;
        settings["semesterCode"] = semesterCode;
        SaveSettings(settings);
    }

    public (string? YearCode, string? SemesterCode) GetSavedAcademicYear()
    {
        var settings = LoadSettings();
        var yr = settings.TryGetValue("yearCode",      out var y) ? y : null;
        var sm = settings.TryGetValue("semesterCode",  out var s) ? s : null;
        return (yr, sm);
    }

    public async Task<List<AcademicYearOption>> FetchAcademicYearsAsync()
    {
#if DEBUG
        var logFile = Path.Combine(_appDataDir, "academic_years_debug.log");
        // Rotate log if > 100 KB
        try { if (File.Exists(logFile) && new FileInfo(logFile).Length > 100_000) File.Delete(logFile); } catch { }
        void Log(string msg) {
            try { File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); } catch { }
        }
#else
        void Log(string msg) { } // no-op in Release
#endif

        Log($"=== FetchAcademicYearsAsync called. HasSavedSession={HasSavedSession} ===");
        if (!HasSavedSession) { Log("No saved session, returning empty."); return new List<AcademicYearOption>(); }
        try
        {
            Log("Fetching page with BuildPageHttpClient...");
            // Must use page client (no X-Requested-With) — the AJAX header causes the server
            // to return a partial response that does not include the schoolYear <select>.
            using var client = BuildPageHttpClient();
            var url = "https://igracias.telkomuniversity.ac.id/registration/?pageid=17985";
            var resp = await client.GetAsync(url);
            Log($"HTTP {(int)resp.StatusCode} {resp.StatusCode}");
            if (!resp.IsSuccessStatusCode) return new List<AcademicYearOption>();

            var html = await resp.Content.ReadAsStringAsync();
            Log($"Response length: {html.Length} chars");

            // DETECT INVALID SESSION / SCRIPT REDIRECT
            // If the page is just a script redirect (length ~3-4KB, contains 'window.location'),
            // the session is likely expired or invalid.
            // NOTE: Do NOT call ClearSession() here — silently deleting cookies and the schedule
            // cache in the background causes both Attendance and Schedule pages to lose their
            // cached data and drop into the NeedsLogin state unexpectedly while the user is
            // navigating around in offline/cache mode.  Just return empty; the ViewModels will
            // handle the expired-session state properly the next time the user refreshes.
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
            // ── Gap-fill: iGracias may omit the 1st/2nd semester of the oldest year ──
            // Mirror the same probe logic used in the Python fetch_academic_years.py.
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
                    var oldestYear    = validYearCodes.Min()!;
                    var yearLabel     = $"20{oldestYear[..2]}/20{oldestYear[2..]}";
                    var existingValues = new HashSet<string>(options.Select(o => o.Value));

                    var patches = new List<AcademicYearOption>();
                    foreach (var smCode in new[] { "1", "2", "3" })
                    {
                        var val = $"{oldestYear}/{smCode}";
                        if (existingValues.Contains(val)) continue;

                        // Probe the server — only add if it returns a real schedule page (>10 KB)
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
                        options.InsertRange(0, patches);
                        Log($"  Gap-fill: prepended {patches.Count} missing semester(s).");
                    }
                }
            }

            Log($"Returning {options.Count} options.");
            return options;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ScheduleService] FetchAcademicYears error: {ex.Message}");
#if DEBUG
            try { File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] EXCEPTION: {ex}\n"); } catch { }
#endif
            return new List<AcademicYearOption>();
        }
    }

    // ── HttpClient factory ───────────────────────────────────────────────────

    /// <summary>
    /// Client for AJAX/DataTables endpoints — sends X-Requested-With: XMLHttpRequest.
    /// </summary>
    private HttpClient BuildHttpClient() => BuildCookieClient(ajaxMode: true);

    /// <summary>
    /// Client for full HTML page requests (login check, academic-year dropdown).
    /// Does NOT send X-Requested-With so the server returns the complete page.
    /// </summary>
    private HttpClient BuildPageHttpClient() => BuildCookieClient(ajaxMode: false);

    private HttpClient BuildCookieClient(bool ajaxMode)
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

            var handler = new HttpClientHandler { CookieContainer = jar, AllowAutoRedirect = true };
            var client  = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", ajaxMode
                ? "text/html, */*; q=0.01"
                : "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.Add("Referer", "https://igracias.telkomuniversity.ac.id/");
            if (ajaxMode)
                client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            return client;
        }
        catch
        {
            return new HttpClient();
        }
    }

    /// <summary>
    /// Computes the current academic year and semester code dynamically.
    /// Semester 1 = Aug–Jan, Semester 2 = Feb–Jul.
    /// </summary>
    private static (string yearCode, string semCode) ComputeDefaultAcademicYear()
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
        return ($"{y1:D2}{y2:D2}", sem);
    }

    public void Dispose() => _httpClient.Dispose();
}
