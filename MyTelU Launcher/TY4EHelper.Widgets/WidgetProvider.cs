using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using System.Text.Json;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using HtmlAgilityPack;

namespace TY4EHelper.Widgets
{
    [Guid("94819777-622C-4BA7-8A7C-0C023EFB31B1")]
    public class WidgetProvider : IWidgetProvider
    {
        private static Dictionary<string, string> _widgetDefinitions = new Dictionary<string, string>();
        private static Dictionary<string, int> _widgetPageIndices = new Dictionary<string, int>();

        public void CreateWidget(WidgetContext widgetContext)
        {
            WidgetFileLogger.Write("Provider", $"CreateWidget id={widgetContext.Id}, definition={widgetContext.DefinitionId}");
            _widgetDefinitions[widgetContext.Id] = widgetContext.DefinitionId;
            _widgetPageIndices[widgetContext.Id] = 0;
            UpdateWidget(widgetContext.Id);
        }

        public void DeleteWidget(string widgetId, string customState)
        {
            WidgetFileLogger.Write("Provider", $"DeleteWidget id={widgetId}, hadDefinition={_widgetDefinitions.ContainsKey(widgetId)}, hadPage={_widgetPageIndices.ContainsKey(widgetId)}");
            if (_widgetDefinitions.ContainsKey(widgetId))
            {
                _widgetDefinitions.Remove(widgetId);
            }
            if (_widgetPageIndices.ContainsKey(widgetId))
            {
                _widgetPageIndices.Remove(widgetId);
            }
        }

        public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
        {
            var id = actionInvokedArgs.WidgetContext.Id;
            var verb = actionInvokedArgs.Verb;
            WidgetFileLogger.Write("Provider", $"OnActionInvoked id={id}, verb={verb}");

            if (verb == "Refresh")
            {
                _liveScheduleCache = null;
                UpdateWidget(id, forceRefresh: true);
            }
            else if (verb == "NextPage")
            {
                if (!_widgetPageIndices.ContainsKey(id)) _widgetPageIndices[id] = 0;
                _widgetPageIndices[id]++;
                UpdateWidget(id, forceRefresh: false);
            }
            else if (verb == "PrevPage")
            {
                if (!_widgetPageIndices.ContainsKey(id)) _widgetPageIndices[id] = 0;
                if (_widgetPageIndices[id] > 0) _widgetPageIndices[id]--;
                UpdateWidget(id, forceRefresh: false);
            }
            else if (verb == "LaunchApp")
            {
                try
                {
                    var widgetExe = Process.GetCurrentProcess().MainModule?.FileName;
                    if (widgetExe != null)
                    {
                        var widgetDir = Path.GetDirectoryName(widgetExe);
                        var appDir = Path.GetDirectoryName(widgetDir);
                        var mainExe = Path.Combine(appDir!, "MyTelU Launcher.exe");
                        if (File.Exists(mainExe))
                        {
                            Process.Start(new ProcessStartInfo(mainExe) { UseShellExecute = true });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Widget] LaunchApp error: {ex.Message}");
                }
            }
        }

        public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
        {
            WidgetFileLogger.Write("Provider", $"OnWidgetContextChanged id={contextChangedArgs.WidgetContext.Id}, definition={contextChangedArgs.WidgetContext.DefinitionId}");
            UpdateWidget(contextChangedArgs.WidgetContext.Id);
        }

        public void Activate(WidgetContext widgetContext)
        {
            WidgetFileLogger.Write("Provider", $"Activate id={widgetContext.Id}, definition={widgetContext.DefinitionId}");
            _widgetDefinitions[widgetContext.Id] = widgetContext.DefinitionId;
            UpdateWidget(widgetContext.Id);
        }

        public void Deactivate(string widgetId)
        {
            WidgetFileLogger.Write("Provider", $"Deactivate id={widgetId}");
        }

        private const bool DEMO_MODE = false;

        private static ScheduleResponse GetDemoSchedule() => new ScheduleResponse
        {
            StudentId = "1234567890",
            FetchTime = DateTime.Now.ToString("o"),
            AcademicYear = "2025/2026 Semester 2",
            Courses = new List<CourseItem>
            {
                new CourseItem { Day = "Monday", Time = "07:00 - 08:40", CourseCode = "TK1234", CourseName = "Introduction to Algorithms", Room = "GKU Timur 301", ClassCode = "TK-47-01", Lecturer = "Budi", RoomClass = "GKU Timur 301 · TK-47-01", StatusText = "Done", StatusColor = "Good" },
                new CourseItem { Day = "Monday", Time = "09:00 - 10:40", CourseCode = "TK2345", CourseName = "Discrete Mathematics", Room = "GKU Barat 201", ClassCode = "TK-47-03", Lecturer = "Agus", RoomClass = "GKU Barat 201 · TK-47-03", StatusText = "Ongoing", StatusColor = "Accent" },
                new CourseItem { Day = "Monday", Time = "13:00 - 14:40", CourseCode = "TK3456", CourseName = "Object Oriented Programming", Room = "Lab Komputer 1", ClassCode = "TK-47-02", Lecturer = "Umar", RoomClass = "Lab Komputer 1 · TK-47-02", StatusText = "Later", StatusColor = "Default" },
                new CourseItem { Day = "Tuesday", Time = "07:00 - 08:40", CourseCode = "TK4567", CourseName = "Database Systems", Room = "GKU Timur 405", ClassCode = "TK-47-05", Lecturer = "Wildan", RoomClass = "GKU Timur 405 · TK-47-05", StatusText = "", StatusColor = "Default" },
                new CourseItem { Day = "Tuesday", Time = "10:00 - 11:40", CourseCode = "TK5678", CourseName = "Computer Networks", Room = "Lab Jaringan 2", ClassCode = "TK-47-04", Lecturer = "M.T. Rina Wulandari", RoomClass = "Lab Jaringan 2 · TK-47-04", StatusText = "", StatusColor = "Default" },
                new CourseItem { Day = "Wednesday", Time = "08:00 - 09:40", CourseCode = "TK6789", CourseName = "Software Engineering", Room = "GKU Barat 303", ClassCode = "TK-47-06", Lecturer = "Dr. Dewi Anggraini", RoomClass = "GKU Barat 303 · TK-47-06", StatusText = "", StatusColor = "Default" },
                new CourseItem { Day = "Wednesday", Time = "13:00 - 15:40", CourseCode = "TK7890", CourseName = "Artificial Intelligence", Room = "Lab AI 1", ClassCode = "TK-47-07", Lecturer = "Prof. Bambang Hari", RoomClass = "Lab AI 1 · TK-47-07", StatusText = "", StatusColor = "Default" },
                new CourseItem { Day = "Thursday", Time = "07:00 - 08:40", CourseCode = "TK8901", CourseName = "Operating Systems", Room = "GKU Timur 201", ClassCode = "TK-47-01", Lecturer = "Dr. Fajar Nugraha", RoomClass = "GKU Timur 201 · TK-47-01", StatusText = "", StatusColor = "Default" },
                new CourseItem { Day = "Friday", Time = "09:00 - 10:40", CourseCode = "TK9012", CourseName = "Human Computer Interaction", Room = "GKU Barat 101", ClassCode = "TK-47-08", Lecturer = "M.Sc. Lestari Putri", RoomClass = "GKU Barat 101 · TK-47-08", StatusText = "", StatusColor = "Default" },
                new CourseItem { Day = "Friday", Time = "13:00 - 14:40", CourseCode = "TK0123", CourseName = "Capstone Project", Room = "Lab Riset 3", ClassCode = "TK-47-09", Lecturer = "Dr. Yusuf Hakim", RoomClass = "Lab Riset 3 · TK-47-09", StatusText = "", StatusColor = "Default" },
            }
        };

        private static string? _cachedGalihDataUri;
        private static string? _cachedGalihEmptyDataUri;
        private static ScheduleResponse? _liveScheduleCache;
        private static DateTime _liveScheduleCacheTime = DateTime.MinValue;

        private static string GetGalihImageUrl()
        {
            if (_cachedGalihDataUri != null) return _cachedGalihDataUri;
            try
            {
                var assetPath = Path.Combine(AppContext.BaseDirectory, "GalihKoper_Widget.png");
                if (!File.Exists(assetPath))
                {
                    Debug.WriteLine($"[Widget] GalihKoper_Icon.png not found at: {assetPath}");
                    _cachedGalihDataUri = "";
                    return "";
                }
                var bytes = File.ReadAllBytes(assetPath);
                _cachedGalihDataUri = "data:image/png;base64," + Convert.ToBase64String(bytes);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Widget] GetGalihImageUrl failed: {ex.Message}");
                _cachedGalihDataUri = "";
            }

            return _cachedGalihDataUri ?? "";
        }

        private static string GetGalihEmptyUrl()
        {
            if (_cachedGalihEmptyDataUri != null) return _cachedGalihEmptyDataUri;
            try
            {
                var assetPath = Path.Combine(AppContext.BaseDirectory, "GalihEmpty_Widget.png");
                if (!File.Exists(assetPath))
                {
                    Debug.WriteLine($"[Widget] GalihEmpty_Widget.png not found at: {assetPath}");
                    _cachedGalihEmptyDataUri = "";
                    return "";
                }
                var bytes = File.ReadAllBytes(assetPath);
                _cachedGalihEmptyDataUri = "data:image/png;base64," + Convert.ToBase64String(bytes);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Widget] GetGalihEmptyUrl failed: {ex.Message}");
                _cachedGalihEmptyDataUri = "";
            }

            return _cachedGalihEmptyDataUri ?? "";
        }

        private static bool HasSavedSession()
        {
            try
            {
                var json = LoadCookiesJson();
                if (json == null) return false;
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return d != null && d.ContainsKey("PHPSESSID");
            }
            catch
            {
                return false;
            }
        }

        private void PushWidgetUpdate(string widgetId, object dataObj)
        {
            WidgetFileLogger.Write("Render", $"PushWidgetUpdate id={widgetId}, payloadType={dataObj.GetType().Name}");
            WidgetManager.GetDefault().UpdateWidget(new WidgetUpdateRequestOptions(widgetId)
            {
                Template = GetAdaptiveCardTemplate(),
                Data = JsonSerializer.Serialize(dataObj),
                CustomState = ""
            });
        }

        private async void UpdateWidget(string widgetId, bool forceRefresh = true)
        {
            try
            {
                var definitionId = "MySchedule";
                if (_widgetDefinitions.TryGetValue(widgetId, out var defId))
                    definitionId = defId;

                if (!_widgetPageIndices.ContainsKey(widgetId)) _widgetPageIndices[widgetId] = 0;
                WidgetFileLogger.Write("Update", $"Begin id={widgetId}, definition={definitionId}, forceRefresh={forceRefresh}, page={_widgetPageIndices[widgetId]}");

                if (!DEMO_MODE && !HasSavedSession())
                {
                    WidgetFileLogger.Write("Update", $"No saved session for id={widgetId}; showing login prompt");
                    PushWidgetUpdate(widgetId, new
                    {
                        headerTitle = "MyTelU Schedule",
                        courses = new List<CourseItem>(),
                        hasCourses = false,
                        showNoCourses = false,
                        loginRequired = true,
                        showLoginPrompt = true,
                        showStatus = false,
                        isCached = false,
                        galihImageUrl = GetGalihImageUrl(),
                        galihEmptyUrl = GetGalihEmptyUrl(),
                        statusColor = "Default",
                        statusMessage = "",
                        lastUpdated = DateTime.Now.ToString("HH:mm"),
                        showPagination = false,
                        canGoBack = false,
                        canGoNext = false
                    });
                    return;
                }

                int currentPage = _widgetPageIndices[widgetId];

                if (DEMO_MODE)
                {
                    forceRefresh = false;
                    _liveScheduleCache = GetDemoSchedule();
                }

                ScheduleResponse? schedule;
                if (!forceRefresh && _liveScheduleCache != null)
                {
                    schedule = _liveScheduleCache;
                }
                else
                {
                    schedule = await FetchScheduleAsync();
                    if (schedule != null && !schedule.IsCachedData)
                    {
                        _liveScheduleCache = schedule;
                        _liveScheduleCacheTime = DateTime.Now;
                    }
                }

                bool isCached = schedule?.IsCachedData == true;
                var allCourses = schedule?.Courses ?? new List<CourseItem>();
                SortCourses(allCourses);

                List<CourseItem> displayCourses;
                string statusText;
                string headerText = "My Schedule";
                bool showPagination = false;
                bool hasPrev = false;
                bool hasNext = false;

                string cacheNote = isCached ? " · cached" : "";
                string statusColor = isCached ? "Warning" : "Default";

                if (definitionId == "MySchedule_All")
                {
                    headerText = "All Upcoming Classes";
                    int pageSize = 4;
                    int totalCount = allCourses.Count;
                    int maxPages = (int)Math.Ceiling((double)totalCount / pageSize);
                    if (currentPage >= maxPages) currentPage = Math.Max(0, maxPages - 1);
                    _widgetPageIndices[widgetId] = currentPage;

                    displayCourses = allCourses.Skip(currentPage * pageSize).Take(pageSize).ToList();
                    statusText = schedule == null
                        ? "Session expired · no cached data yet"
                        : $"Page {currentPage + 1}/{Math.Max(1, maxPages)}{cacheNote}";

                    showPagination = totalCount > pageSize;
                    hasPrev = currentPage > 0;
                    hasNext = (currentPage + 1) * pageSize < totalCount;
                }
                else
                {
                    headerText = "Today's Classes";

                    if (schedule == null)
                    {
                        displayCourses = new List<CourseItem>();
                        statusText = "Session expired · open the app to refresh";
                        statusColor = "Attention";
                    }
                    else
                    {
                        int dayOfWeek = (int)DateTime.Now.DayOfWeek;
                        int targetRank = dayOfWeek == 0 ? 7 : dayOfWeek;

                        var todaysCourses = allCourses
                            .Where(c => GetDayRank(c.Day) == targetRank)
                            .ToList();

                        foreach (var c in todaysCourses)
                        {
                            var s = GetCourseStatus(c.Time);
                            c.StatusText = s.text;
                            c.StatusColor = s.color;
                        }

                        todaysCourses.Sort((a, b) => string.Compare(a.Time, b.Time));
                        displayCourses = todaysCourses;

                        int n = todaysCourses.Count;
                        statusText = (n == 0 ? "No classes today" : $"{n} class{(n == 1 ? "" : "es")} today") + cacheNote;
                    }
                }

                bool hasCourses = displayCourses.Count > 0;
                string noCourseMessage = definitionId == "MySchedule_All"
                    ? "No upcoming classes!"
                    : "No classes today!";

                PushWidgetUpdate(widgetId, new
                {
                    headerTitle = headerText,
                    courses = displayCourses,
                    hasCourses = hasCourses,
                    showNoCourses = !hasCourses,
                    noCourseMessage = noCourseMessage,
                    loginRequired = false,
                    showLoginPrompt = false,
                    galihImageUrl = GetGalihImageUrl(),
                    galihEmptyUrl = GetGalihEmptyUrl(),
                    showStatus = statusText.Length > 0,
                    isCached = isCached,
                    statusColor = statusColor,
                    statusMessage = statusText,
                    lastUpdated = DateTime.Now.ToString("HH:mm"),
                    showPagination = showPagination,
                    canGoBack = hasPrev,
                    canGoNext = hasNext
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating widget: {ex.Message}");
            }
        }

        private (string text, string color) GetCourseStatus(string timeString)
        {
            if (string.IsNullOrEmpty(timeString)) return ("", "Default");

            try
            {
                var parts = timeString.Split('-');
                var startStr = parts[0].Trim();
                var endStr = parts.Length > 1 ? parts[1].Trim() : "";

                DateTime startTime = DateTime.Today;
                DateTime endTime = DateTime.Today;

                if (DateTime.TryParseExact(startStr, "HH:mm", null, System.Globalization.DateTimeStyles.None, out var sTime))
                {
                    startTime = sTime;
                }
                else if (DateTime.TryParse(startStr, out sTime))
                {
                    startTime = sTime;
                }
                else
                {
                    return ("", "Default");
                }

                if (!string.IsNullOrEmpty(endStr) && DateTime.TryParseExact(endStr, "HH:mm", null, System.Globalization.DateTimeStyles.None, out var eTime))
                {
                    endTime = eTime;
                }
                else if (!string.IsNullOrEmpty(endStr) && DateTime.TryParse(endStr, out eTime))
                {
                    endTime = eTime;
                }
                else
                {
                    endTime = startTime.AddHours(2);
                }

                startTime = DateTime.Today.Add(startTime.TimeOfDay);
                endTime = DateTime.Today.Add(endTime.TimeOfDay);
                var now = DateTime.Now;

                if (now >= startTime && now <= endTime)
                {
                    return ("ONGOING", "Attention");
                }
                else if (now > endTime)
                {
                    return ("FINISHED", "Default");
                }
                else
                {
                    return ("UPCOMING", "Good");
                }
            }
            catch
            {
                return ("", "Default");
            }
        }

        private void SortCourses(List<CourseItem> courses)
        {
            courses.Sort((a, b) =>
            {
                int dayA = GetDayRank(a.Day);
                int dayB = GetDayRank(b.Day);
                if (dayA != dayB) return dayA.CompareTo(dayB);

                return string.Compare(a.Time, b.Time);
            });
        }

        private int GetDayRank(string day)
        {
            if (string.IsNullOrEmpty(day)) return 99;
            day = day.Trim().ToUpper();

            if (day.Contains("SENIN") || day.Contains("MONDAY")) return 1;
            if (day.Contains("SELASA") || day.Contains("TUESDAY")) return 2;
            if (day.Contains("RABU") || day.Contains("WEDNESDAY")) return 3;
            if (day.Contains("KAMIS") || day.Contains("THURSDAY")) return 4;
            if (day.Contains("JUMAT") || day.Contains("FRIDAY")) return 5;
            if (day.Contains("SABTU") || day.Contains("SATURDAY")) return 6;
            if (day.Contains("MINGGU") || day.Contains("SUNDAY")) return 7;

            return 99;
        }

        private static readonly string _appDataDir = WidgetAppDataStore.DirectoryPath;

        private static readonly string _cacheFile = WidgetAppDataStore.GetFilePath("schedule_cache.json");

        private static readonly string _settingsFile = WidgetAppDataStore.GetFilePath("settings.json");

        private static readonly string _cookiesFile = WidgetAppDataStore.GetFilePath("cookies.json");

        private static string? LoadCookiesJson()
        {
            if (!File.Exists(_cookiesFile)) return null;
            try
            {
                var bytes = File.ReadAllBytes(_cookiesFile);
                var entropy = "MyTelU-iGracias-cookies-v1"u8.ToArray();
                try
                {
                    var plain = ProtectedData.Unprotect(bytes, entropy, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plain);
                }
                catch (CryptographicException)
                {
                    var text = Encoding.UTF8.GetString(bytes).Trim();
                    return text.StartsWith("{") ? text : null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string? LoadSecureFile(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var bytes = File.ReadAllBytes(path);
                var entropy = "MyTelU-secure-store-v1"u8.ToArray();
                try
                {
                    var plain = ProtectedData.Unprotect(bytes, entropy, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plain);
                }
                catch (CryptographicException)
                {
                    var text = Encoding.UTF8.GetString(bytes).Trim();
                    return (text.StartsWith("{") || text.StartsWith("[")) ? text : null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void SaveSecureFile(string path, string json)
        {
            try
            {
                var cipher = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(json),
                    "MyTelU-secure-store-v1"u8.ToArray(),
                    DataProtectionScope.CurrentUser);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, cipher);
            }
            catch
            {
            }
        }

        private static Dictionary<string, string> LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsFile)) return new();
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_settingsFile));
                return d ?? new();
            }
            catch
            {
                return new();
            }
        }

        private async Task<ScheduleResponse?> FetchScheduleAsync()
        {
            var result = await TryFetchFromServer();
            if (result != null)
            {
                SaveCache(result);
                return result;
            }

            return LoadCache();
        }

        private async Task<ScheduleResponse?> TryFetchFromServer()
        {
            try
            {
                var cookiesJson = LoadCookiesJson();
                if (cookiesJson == null) return null;

                var cookiesDict = JsonSerializer.Deserialize<Dictionary<string, string>>(cookiesJson);
                if (cookiesDict == null || !cookiesDict.ContainsKey("PHPSESSID")) return null;

                var settings = LoadSettings();

                if (!settings.TryGetValue("studentId", out var studentId) || string.IsNullOrWhiteSpace(studentId))
                    return null;

                string yearCode;
                string semCode;
                if (settings.TryGetValue("yearCode", out var yr) && settings.TryGetValue("semesterCode", out var sm)
                    && !string.IsNullOrWhiteSpace(yr) && !string.IsNullOrWhiteSpace(sm))
                {
                    yearCode = yr;
                    semCode = sm;
                }
                else
                {
                    var now = DateTime.Now;
                    int year = now.Month >= 8 ? now.Year : now.Year - 1;
                    yearCode = $"{year % 100}{(year + 1) % 100}";
                    semCode = now.Month is >= 2 and <= 7 ? "2" : "1";
                }

                string schoolYear = $"{yearCode}/{semCode}";

                var cookieContainer = new CookieContainer();
                var handler = new HttpClientHandler { CookieContainer = cookieContainer };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

                var baseUri = new Uri("https://igracias.telkomuniversity.ac.id");
                foreach (var kvp in cookiesDict)
                    cookieContainer.Add(baseUri, new Cookie(kvp.Key, kvp.Value));

                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

                var parts = new List<string>
                {
                    "act=viewStudentSchedule",
                    $"studentId={Uri.EscapeDataString(studentId)}",
                    "sEcho=1", "iColumns=9", "sColumns=",
                    "iDisplayStart=0", "iDisplayLength=200",
                };
                for (int i = 0; i < 9; i++) parts.Add($"mDataProp_{i}={i}");
                parts.Add("sSearch=");
                parts.Add("bRegex=false");
                for (int i = 0; i < 9; i++)
                {
                    parts.Add($"sSearch_{i}=");
                    parts.Add($"bRegex_{i}=false");
                    parts.Add($"bSearchable_{i}=true");
                    parts.Add($"bSortable_{i}=true");
                }
                parts.Add("iSortCol_0=0");
                parts.Add("sSortDir_0=asc");
                parts.Add("iSortingCols=1");
                parts.Add($"schoolYear={Uri.EscapeDataString(schoolYear)}");

                var url = "https://igracias.telkomuniversity.ac.id/libraries/ajax/ajax.schedule.php?" + string.Join("&", parts);
                var resp = await client.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return null;

                var body = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(body) || body.Trim() is "0" or "" or "null") return null;

                return ParseJsonSchedule(body, studentId, schoolYear);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Widget] Fetch error: {ex.Message}");
                return null;
            }
        }

        private static ScheduleResponse? ParseJsonSchedule(string body, string studentId, string schoolYear)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty("aaData", out var aaData)
                    || aaData.ValueKind == JsonValueKind.Null)
                {
                    return new ScheduleResponse { StudentId = studentId, FetchTime = DateTime.Now.ToString("o"), AcademicYear = schoolYear };
                }

                if (aaData.ValueKind != JsonValueKind.Array) return null;

                var courses = new List<CourseItem>();
                foreach (var row in aaData.EnumerateArray())
                {
                    var cols = row.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
                    if (cols.Count < 9) continue;

                    string timeStart = cols[1].Length >= 5 ? cols[1][..5] : cols[1];
                    string timeEnd = cols[7].Length >= 5 ? cols[7][..5] : cols[7];

                    var room = cols[2].Trim();
                    var classCode = cols.Count > 6 ? cols[6].Trim() : "";
                    var lecturer = cols.Count > 5 ? cols[5].Trim() : "";
                    var roomClass = (room, classCode) switch
                    {
                        ({ Length: > 0 }, { Length: > 0 }) => $"{room} · {classCode}",
                        ({ Length: > 0 }, _) => room,
                        (_, { Length: > 0 }) => classCode,
                        _ => ""
                    };

                    courses.Add(new CourseItem
                    {
                        Day = cols[0].Trim(),
                        Time = $"{timeStart} - {timeEnd}",
                        CourseCode = cols[3].Trim(),
                        CourseName = cols[4].Trim(),
                        Room = room,
                        ClassCode = classCode,
                        Lecturer = lecturer,
                        RoomClass = roomClass,
                    });
                }

                return new ScheduleResponse
                {
                    StudentId = studentId,
                    FetchTime = DateTime.Now.ToString("o"),
                    AcademicYear = schoolYear,
                    Courses = courses,
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Widget] JSON parse error: {ex.Message}");
                return null;
            }
        }

        private void SaveCache(ScheduleResponse schedule)
        {
            try
            {
                SaveSecureFile(_cacheFile, JsonSerializer.Serialize(schedule));
            }
            catch
            {
            }
        }

        private ScheduleResponse? LoadCache()
        {
            try
            {
                var json = LoadSecureFile(_cacheFile);
                if (json == null) return null;
                var cached = JsonSerializer.Deserialize<ScheduleResponse>(json);
                if (cached != null) cached.IsCachedData = true;
                return cached;
            }
            catch
            {
                return null;
            }
        }

        private string GetAdaptiveCardTemplate()
        {
            return @"{
    ""type"": ""AdaptiveCard"",
    ""$schema"": ""http://adaptivecards.io/schemas/adaptive-card.json"",
    ""version"": ""1.5"",
    ""body"": [
        {
            ""type"": ""TextBlock"",
            ""text"": ""${lastUpdated} · ${statusMessage}"",
            ""size"": ""Small"",
            ""color"": ""${statusColor}"",
            ""isSubtle"": true,
            ""$when"": ""${showStatus}""
        },
        {
            ""type"": ""Container"",
            ""$when"": ""${showLoginPrompt}"",
            ""height"": ""stretch"",
            ""verticalContentAlignment"": ""Center"",
            ""items"": [
                {
                    ""type"": ""Image"",
                    ""url"": ""${galihImageUrl}"",
                    ""size"": ""Large"",
                    ""horizontalAlignment"": ""Center""
                },
                {
                    ""type"": ""TextBlock"",
                    ""text"": ""To track your schedule in the widget, let\u2019s set it up first in the MyTelU Launcher!"",
                    ""wrap"": true,
                    ""horizontalAlignment"": ""Center"",
                    ""spacing"": ""Small""
                }
            ]
        },
        {
            ""type"": ""Container"",
            ""$when"": ""${showNoCourses}"",
            ""height"": ""stretch"",
            ""verticalContentAlignment"": ""Center"",
            ""items"": [
                {
                    ""type"": ""Image"",
                    ""url"": ""${galihEmptyUrl}"",
                    ""size"": ""Large"",
                    ""horizontalAlignment"": ""Center""
                },
                {
                    ""type"": ""TextBlock"",
                    ""text"": ""${noCourseMessage}"",
                    ""wrap"": true,
                    ""horizontalAlignment"": ""Center"",
                    ""spacing"": ""Small""
                }
            ]
        },
        {
            ""type"": ""Container"",
            ""$when"": ""${hasCourses}"",
            ""height"": ""stretch"",
            ""verticalContentAlignment"": ""Center"",
            ""items"": [
                {
                    ""type"": ""Container"",
                    ""$data"": ""${courses}"",
                    ""separator"": true,
                    ""spacing"": ""Small"",
                    ""items"": [
                        {
                            ""type"": ""ColumnSet"",
                            ""columns"": [
                                {
                                    ""type"": ""Column"",
                                    ""width"": ""auto"",
                                    ""items"": [
                                        {
                                            ""type"": ""TextBlock"",
                                            ""text"": ""${time}"",
                                            ""weight"": ""Bolder"",
                                            ""size"": ""Small""
                                        },
                                        {
                                            ""type"": ""TextBlock"",
                                            ""text"": ""${StatusText}"",
                                            ""size"": ""Small"",
                                            ""weight"": ""Bolder"",
                                            ""color"": ""${StatusColor}"",
                                            ""spacing"": ""None""
                                        },
                                        {
                                            ""type"": ""TextBlock"",
                                            ""text"": ""${day}"",
                                            ""size"": ""Small"",
                                            ""isSubtle"": true,
                                            ""spacing"": ""None"",
                                            ""wrap"": false
                                        }
                                    ]
                                },
                                {
                                    ""type"": ""Column"",
                                    ""width"": ""stretch"",
                                    ""items"": [
                                        {
                                            ""type"": ""TextBlock"",
                                            ""text"": ""${course_code} - ${course_name}"",
                                            ""weight"": ""Bolder"",
                                            ""size"": ""Small"",
                                            ""wrap"": true
                                        },
                                        {
                                            ""type"": ""TextBlock"",
                                            ""text"": ""${RoomClass}"",
                                            ""size"": ""Small"",
                                            ""isSubtle"": true,
                                            ""spacing"": ""None"",
                                            ""wrap"": true,
                                            ""$when"": ""${RoomClass != """"}"" 
                                        },
                                        {
                                            ""type"": ""TextBlock"",
                                            ""text"": ""${Lecturer}"",
                                            ""size"": ""Small"",
                                            ""isSubtle"": true,
                                            ""spacing"": ""None"",
                                            ""wrap"": true,
                                            ""$when"": ""${Lecturer != """"}"" 
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        },
        {
            ""type"": ""ColumnSet"",
            ""spacing"": ""Small"",
            ""columns"": [
                {
                    ""type"": ""Column"",
                    ""width"": ""stretch"",
                    ""spacing"": ""None"",
                    ""items"": [
                        {
                            ""type"": ""ActionSet"",
                            ""$when"": ""${canGoBack}"",
                            ""actions"": [
                                { ""type"": ""Action.Execute"", ""title"": ""< Prev"", ""verb"": ""PrevPage"" }
                            ]
                        }
                    ]
                },
                {
                    ""type"": ""Column"",
                    ""width"": ""stretch"",
                    ""spacing"": ""None"",
                    ""horizontalAlignment"": ""Center"",
                    ""items"": [
                        {
                            ""type"": ""ActionSet"",
                            ""$when"": ""${showStatus}"",
                            ""horizontalAlignment"": ""Center"",
                            ""actions"": [
                                { ""type"": ""Action.Execute"", ""title"": ""Refresh"", ""verb"": ""Refresh"" }
                            ]
                        }
                    ]
                },
                {
                    ""type"": ""Column"",
                    ""width"": ""stretch"",
                    ""spacing"": ""None"",
                    ""horizontalAlignment"": ""Right"",
                    ""items"": [
                        {
                            ""type"": ""ActionSet"",
                            ""$when"": ""${canGoNext}"",
                            ""horizontalAlignment"": ""Right"",
                            ""actions"": [
                                { ""type"": ""Action.Execute"", ""title"": ""Next >"", ""verb"": ""NextPage"" }
                            ]
                        }
                    ]
                }
            ]
        }
    ],
    ""actions"": []
}";
        }
    }

    public class CourseItem
    {
        [JsonPropertyName("day")]
        public string Day { get; set; } = "";

        [JsonPropertyName("time")]
        public string Time { get; set; } = "";

        [JsonPropertyName("course_code")]
        public string CourseCode { get; set; } = "";

        [JsonPropertyName("course_name")]
        public string CourseName { get; set; } = "";

        [JsonPropertyName("room")]
        public string Room { get; set; } = "";

        [JsonPropertyName("class")]
        public string ClassCode { get; set; } = "";

        [JsonPropertyName("lecturer")]
        public string Lecturer { get; set; } = "";

        [JsonPropertyName("has_conflict")]
        public bool HasConflict { get; set; }

        [JsonPropertyName("raw_text")]
        public string RawText { get; set; } = "";

        public string StatusColor { get; set; } = "Default";
        public string StatusText { get; set; } = "";
        public string RoomClass { get; set; } = "";
    }

    public class ScheduleResponse
    {
        [JsonPropertyName("student_id")]
        public string StudentId { get; set; } = "";

        [JsonPropertyName("fetch_time")]
        public string FetchTime { get; set; } = "";

        [JsonPropertyName("academic_year")]
        public string AcademicYear { get; set; } = "";

        [JsonPropertyName("courses")]
        public List<CourseItem> Courses { get; set; } = new();

        [JsonPropertyName("timetable")]
        public Dictionary<string, object> Timetable { get; set; } = new();

        [JsonIgnore]
        public bool IsCachedData { get; set; } = false;
    }
}