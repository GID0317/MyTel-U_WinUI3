using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using System.Text.Json;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using System.Net;
using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace TY4EHelper.Widgets
{
    [Guid("94819777-622C-4BA7-8A7C-0C023EFB31B1")]
    public class WidgetProvider : IWidgetProvider
    {
        // Keeping track of which widget instance is which type
        // Key: WidgetId, Value: DefinitionId
        private static Dictionary<string, string> _widgetDefinitions = new Dictionary<string, string>();
        
        // Key: WidgetId, Value: PageIndex
        private static Dictionary<string, int> _widgetPageIndices = new Dictionary<string, int>();

        public void CreateWidget(WidgetContext widgetContext)
        {
            _widgetDefinitions[widgetContext.Id] = widgetContext.DefinitionId;
            _widgetPageIndices[widgetContext.Id] = 0;
            UpdateWidget(widgetContext.Id);
        }

        public void DeleteWidget(string widgetId, string customState)
        {
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
            
            if (verb == "Refresh")
            {
                UpdateWidget(id);
            }
            else if (verb == "NextPage")
            {
                if (!_widgetPageIndices.ContainsKey(id)) _widgetPageIndices[id] = 0;
                _widgetPageIndices[id]++;
                UpdateWidget(id);
            }
            else if (verb == "PrevPage")
            {
                if (!_widgetPageIndices.ContainsKey(id)) _widgetPageIndices[id] = 0;
                if (_widgetPageIndices[id] > 0) _widgetPageIndices[id]--;
                UpdateWidget(id);
            }
        }

        public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
        {
            UpdateWidget(contextChangedArgs.WidgetContext.Id);
        }

        public void Activate(WidgetContext widgetContext)
        {
            _widgetDefinitions[widgetContext.Id] = widgetContext.DefinitionId;
            UpdateWidget(widgetContext.Id);
        }

        public void Deactivate(string widgetId)
        {
            // Pause high frequency updates
        }

        private async void UpdateWidget(string widgetId)
        {
            try
            {
                var definitionId = "MySchedule"; // Default
                if (_widgetDefinitions.TryGetValue(widgetId, out var defId))
                {
                    definitionId = defId;
                }

                if (!_widgetPageIndices.ContainsKey(widgetId)) _widgetPageIndices[widgetId] = 0;
                int currentPage = _widgetPageIndices[widgetId];

                var schedule = await FetchScheduleAsync();
                var allCourses = schedule?.Courses ?? new List<CourseItem>();
                
                // Sort courses by Day and Time
                SortCourses(allCourses);

                // Display Logic
                List<CourseItem> displayCourses;
                string statusText;
                string headerText = "My Schedule";
                bool showPagination = false;
                bool hasPrev = false;
                bool hasNext = false;

                var isCached = schedule?.IsCachedData == true;
                var cacheNote = isCached ? " (cached)" : "";

                if (definitionId == "MySchedule_All")
                {
                    headerText = "All Upcoming Classes";
                    int pageSize = 4; // Use small page size to ensure it fits
                    
                    int totalCount = allCourses.Count;
                    int maxPages = (int)Math.Ceiling((double)totalCount / pageSize);
                    
                    if (currentPage >= maxPages) currentPage = Math.Max(0, maxPages - 1);
                    _widgetPageIndices[widgetId] = currentPage;

                    displayCourses = allCourses.Skip(currentPage * pageSize).Take(pageSize).ToList();
                    statusText = schedule == null ? "Server offline" : $"Page {currentPage + 1}/{Math.Max(1, maxPages)}{cacheNote}";
                    
                    showPagination = totalCount > pageSize;
                    hasPrev = currentPage > 0;
                    hasNext = (currentPage + 1) * pageSize < totalCount;
                }
                else  // "MySchedule" (Today)
                {
                    headerText = "Today's Classes";
                    
                    // Robust Day Matching using GetDayRank
                    int currentDayOfWeek = (int)DateTime.Now.DayOfWeek; // 0=Sunday, 1=Monday...
                    int targetRank = currentDayOfWeek == 0 ? 7 : currentDayOfWeek; // Make Sunday=7 to match GetDayRank

                    var todaysCourses = allCourses
                        .Where(c => GetDayRank(c.Day) == targetRank)
                        .ToList();
                        
                    // Add Status Text for Today
                    foreach (var c in todaysCourses)
                    {
                        var status = GetCourseStatus(c.Time);
                        c.StatusText = status.text;
                        c.StatusColor = status.color;
                    }

                    // Show all courses for today (remove limit) but sort by time
                    todaysCourses.Sort((a, b) => string.Compare(a.Time, b.Time)); 
                    displayCourses = todaysCourses;
                    
                    if (allCourses.Count > 0 && todaysCourses.Count == 0)
                    {
                        // Fallback: If no classes today, show upcoming classes from future days
                        // Find the next available day with classes
                         var futureCourses = allCourses
                            .Where(c => GetDayRank(c.Day) > targetRank) // Days after today
                            .OrderBy(c => GetDayRank(c.Day))
                            .ThenBy(c => c.Time)
                            .Take(4)
                            .ToList();
                            
                         if (futureCourses.Count == 0 && targetRank == 7) // If Sunday, check next week (Monday etc)
                         {
                             futureCourses = allCourses
                                .OrderBy(c => GetDayRank(c.Day))
                                .ThenBy(c => c.Time)
                                .Take(4)
                                .ToList();
                         }

                         if (futureCourses.Count > 0)
                         {
                             headerText = "Upcoming Classes"; // Change header
                             displayCourses = futureCourses;
                             // Status for these should be UPCOMING
                             foreach(var c in displayCourses) { c.StatusText = "UPCOMING"; c.StatusColor = "Good"; }
                             statusText = $"Next: {displayCourses[0].Day}";
                         }
                         else
                         {
                            // Fallback name for display
                            var culture = new System.Globalization.CultureInfo("id-ID");
                            var todayDisplay = culture.DateTimeFormat.GetDayName(DateTime.Now.DayOfWeek).ToUpper();
                            statusText = $"No classes for {todayDisplay}";
                         }
                    }
                    else
                    {
                        statusText = widthStatus(todaysCourses.Count) + cacheNote;
                    }
                }
                
                var dataObj = new 
                { 
                    headerTitle = headerText,
                    courses = displayCourses,
                    hasCourses = displayCourses.Count > 0,
                    statusMessage = statusText,
                    lastUpdated = DateTime.Now.ToString("HH:mm"),
                    showPagination = showPagination,
                    canGoBack = hasPrev,
                    canGoNext = hasNext
                };

                string dataJson = JsonSerializer.Serialize(dataObj);
                
                var updateOptions = new WidgetUpdateRequestOptions(widgetId)
                {
                    Template = GetAdaptiveCardTemplate(),
                    Data = dataJson,
                    CustomState = ""
                };

                WidgetManager.GetDefault().UpdateWidget(updateOptions);
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

                // Get times
                DateTime startTime = DateTime.Today;
                DateTime endTime = DateTime.Today;
                
                // Parse Start Time
                // Assuming format like "08:30"
                if (DateTime.TryParseExact(startStr, "HH:mm", null, System.Globalization.DateTimeStyles.None, out var sTime))
                {
                    startTime = sTime;
                }
                else if (DateTime.TryParse(startStr, out sTime)) // Fallback
                {
                    startTime = sTime;
                }
                else
                {
                    return ("", "Default");
                }

                // Parse End Time
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
                    endTime = startTime.AddHours(2); // Default
                }
                
                // Adjust date to today
                startTime = DateTime.Today.Add(startTime.TimeOfDay);
                endTime = DateTime.Today.Add(endTime.TimeOfDay);
                var now = DateTime.Now;

                if (now >= startTime && now <= endTime)
                {
                    return ("ONGOING", "Attention"); // Attention = Red/Orange
                }
                else if (now > endTime)
                {
                    return ("FINISHED", "Default"); // Default (usually dim/grey if isSubtle=true)
                }
                else
                {
                    return ("UPCOMING", "Good"); // Good = Green
                }
            }
            catch { return ("", "Default"); }
        }

        private void SortCourses(List<CourseItem> courses)
        {
            courses.Sort((a, b) =>
            {
                int dayA = GetDayRank(a.Day);
                int dayB = GetDayRank(b.Day);
                if (dayA != dayB) return dayA.CompareTo(dayB);

                // Simple string comparison for time (e.g., "08:30" < "10:30")
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
            
            return 99; // Unknown
        }

        private string widthStatus(int count)
        {
            return count == 0 ? "No Data" : $"{count} Classes";
        }

        private static readonly string _cacheFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TY4EHelper", "schedule_cache.json");

        private static readonly string _cookiesFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TY4EHelper", "cookies.json");

        private async Task<ScheduleResponse?> FetchScheduleAsync()
        {
            // 1. Try live server
            var result = await TryFetchFromServer();
            if (result != null)
            {
                SaveCache(result);
                return result;
            }

            // 2. Fall back to cached data
            return LoadCache();
        }

        private async Task<ScheduleResponse?> TryFetchFromServer()
        {
            try
            {
                // 1. Load cookies
                if (!File.Exists(_cookiesFile)) return null;

                var cookiesJson = File.ReadAllText(_cookiesFile);
                var cookiesDict = JsonSerializer.Deserialize<Dictionary<string, string>>(cookiesJson);
                if (cookiesDict == null || !cookiesDict.ContainsKey("PHPSESSID")) return null;

                // 2. Setup HttpClient with cookies
                var cookieContainer = new CookieContainer();
                var handler = new HttpClientHandler { CookieContainer = cookieContainer };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
                
                var baseUri = new Uri("https://igracias.telkomuniversity.ac.id");
                foreach (var kvp in cookiesDict)
                {
                    cookieContainer.Add(baseUri, new Cookie(kvp.Key, kvp.Value));
                }

                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

                // 3. Fetch HTML
                var url = "https://igracias.telkomuniversity.ac.id/libraries/ajax/ajax.schedule.php?act=previewSchedule&studentid=101032300012&sch=2526&sm=2";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;

                var html = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(html) || html.Trim() == "0" || html.Trim() == "null") return null;

                // 4. Parse HTML
                return ParseScheduleHtml(html);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Widget Fetch Error: {ex.Message}");
                return null;
            }
        }

        private ScheduleResponse? ParseScheduleHtml(string html)
        {
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var table = doc.GetElementbyId("table2");
                if (table == null) return null;

                var schedule = new ScheduleResponse
                {
                    StudentId = "101032300012",
                    FetchTime = DateTime.Now.ToString("o"),
                    AcademicYear = "2526/2"
                };

                var days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
                var rows = table.SelectNodes(".//tr");
                if (rows == null || rows.Count <= 1) return schedule;

                // Skip header row
                for (int i = 1; i < rows.Count; i++)
                {
                    var cells = rows[i].SelectNodes(".//td");
                    if (cells == null || cells.Count < 2) continue;

                    var timeSlot = cells[0].InnerText.Trim();

                    // cells[1] is the blank TEL-U column, so days start at cells[2]
                    for (int dayIdx = 0; dayIdx < days.Length; dayIdx++)
                    {
                        int cellIdx = dayIdx + 2;
                        if (cellIdx >= cells.Count) break;

                        var cell = cells[cellIdx];
                        var day = days[dayIdx];

                        var courseDivs = cell.SelectNodes(".//div[@style]");
                        if (courseDivs == null) continue;

                        foreach (var div in courseDivs)
                        {
                            var text = div.InnerText.Trim();
                            if (string.IsNullOrEmpty(text) || text == "&nbsp;") continue;

                            var style = div.GetAttributeValue("style", "");
                            bool hasConflict = style.Contains("#FFFF00", StringComparison.OrdinalIgnoreCase);

                            var parts = text.Split(new[] { '-' }, 2);
                            var courseCode = parts.Length >= 2 ? parts[0].Trim() : "";
                            var courseName = parts.Length >= 2 ? parts[1].Trim() : text;

                            schedule.Courses.Add(new CourseItem
                            {
                                Day = day,
                                Time = timeSlot,
                                RawText = text,
                                HasConflict = hasConflict,
                                CourseCode = courseCode,
                                CourseName = courseName
                            });
                        }
                    }
                }

                return schedule;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Widget Parse Error: {ex.Message}");
                return null;
            }
        }

        private void SaveCache(ScheduleResponse schedule)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
                var json = JsonSerializer.Serialize(schedule);
                File.WriteAllText(_cacheFile, json);
            }
            catch { /* best-effort */ }
        }

        private ScheduleResponse? LoadCache()
        {
            try
            {
                if (!File.Exists(_cacheFile)) return null;
                var json = File.ReadAllText(_cacheFile);
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
    ""version"": ""1.3"",
    ""body"": [
        {
            ""type"": ""TextBlock"",
            ""text"": ""${headerTitle}"",
            ""size"": ""Large"",
            ""weight"": ""Bolder""
        },
        {
            ""type"": ""TextBlock"",
            ""text"": ""${lastUpdated} - ${statusMessage}"",
            ""size"": ""Small"",
            ""isSubtle"": true
        },
        {
            ""type"": ""TextBlock"",
            ""text"": ""No classes for today"",
            ""wrap"": true,
            ""$when"": ""${!hasCourses}""
        },
        {
            ""type"": ""Container"",
            ""$data"": ""${courses}"",
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
                                    ""weight"": ""Bolder""
                                },
                                {
                                    ""type"": ""TextBlock"",
                                    ""text"": ""${StatusText}"",
                                    ""size"": ""Small"",
                                    ""weight"": ""Bolder"",
                                    ""color"": ""${StatusColor}"",
                                    ""isSubtle"": true,
                                    ""$when"": ""${StatusText != ''}""
                                },
                                {
                                    ""type"": ""TextBlock"",
                                    ""text"": ""${day}"",
                                    ""size"": ""Small"",
                                    ""isSubtle"": true,
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
                                    ""text"": ""${course_name}"",
                                    ""wrap"": true
                                },
                                {
                                    ""type"": ""TextBlock"",
                                    ""text"": ""${course_code}"",
                                    ""size"": ""Small"",
                                    ""isSubtle"": true
                                }
                            ]
                        }
                    ]
                }
            ],
            ""separator"": true,
            ""spacing"": ""Medium""
        }
    ],
    ""actions"": [
        {
            ""type"": ""Action.Execute"",
            ""title"": ""Previous"",
            ""verb"": ""PrevPage"",
            ""$when"": ""${canGoBack}""
        },
        {
            ""type"": ""Action.Execute"",
            ""title"": ""Next"",
            ""verb"": ""NextPage"",
            ""$when"": ""${canGoNext}""
        },
        {
            ""type"": ""Action.Execute"",
            ""title"": ""Refresh"",
            ""verb"": ""Refresh""
        }
    ]
}";
        }
    }

    // Models (Recreated here to avoid dependency on main project which is an App)
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

        [JsonPropertyName("has_conflict")]
        public bool HasConflict { get; set; }

        [JsonPropertyName("raw_text")]
        public string RawText { get; set; } = "";
        
        // Computed property for display in Adaptive Card
        public string StatusColor { get; set; } = "Default"; // Default, Warning (Ongoing), Good (Upcoming), Attention (Finished)
        public string StatusText { get; set; } = "";
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
