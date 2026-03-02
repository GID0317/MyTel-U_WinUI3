using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MyTelU_Launcher.Helpers;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.Services;

namespace MyTelU_Launcher.ViewModels;

public partial class ScheduleViewModel : ObservableRecipient,
    IRecipient<SessionCookiesSavedMessage>
{
    private static readonly Dictionary<string, int> DayOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Monday",    0 }, { "Senin",   0 },
        { "Tuesday",   1 }, { "Selasa",  1 },
        { "Wednesday", 2 }, { "Rabu",    2 },
        { "Thursday",  3 }, { "Kamis",   3 },
        { "Friday",    4 }, { "Jumat",   4 },
        { "Saturday",  5 }, { "Sabtu",   5 },
        { "Sunday",    6 }, { "Minggu",  6 },
    };

    private readonly IScheduleService      _scheduleService;
    private readonly IBrowserLoginService  _browserLoginService;

    [RelayCommand]
    private async Task BeginLoginFromOnboardingAsync() => await LoginWithBrowserAsync();

    [ObservableProperty]
    private bool _isLoading;

    // Derived: notified whenever IsLoading flips so XAML bindings update.
    public bool IsNotLoading => !IsLoading;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private ScheduleResponse? _schedule;

    [ObservableProperty]
    private bool _needsLogin;

    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>
    /// True when the displayed schedule comes from the local cache because no live fetch succeeded.
    /// </summary>
    [ObservableProperty]
    private bool _isOffline;

    /// <summary>
    /// Human-readable age of the cached schedule shown when IsOffline is true.
    /// </summary>
    public string CacheTimestamp
    {
        get
        {
            if (!IsOffline || Schedule is null) return string.Empty;
            if (DateTime.TryParse(Schedule.FetchTime, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            {
                var ago = DateTime.Now - dt;
                if (ago.TotalMinutes < 2)  return "just now";
                if (ago.TotalHours   < 1)  return $"{(int)ago.TotalMinutes} min ago";
                if (ago.TotalDays    < 1)  return $"{(int)ago.TotalHours} hr ago";
                if (ago.TotalDays    < 7)  return $"{(int)ago.TotalDays} day{((int)ago.TotalDays == 1 ? "" : "s")} ago";
                return dt.ToString("d MMM yyyy");
            }
            return "cached";
        }
    }

    /// <summary>Full message shown in the offline InfoBar, including the last-synced time.</summary>
    public string CacheTimestampMessage =>
        IsOffline ? $"Viewing cached schedule, last synced {CacheTimestamp}." : string.Empty;

    [ObservableProperty]
    private bool _isBrowserLoginRunning;

    [ObservableProperty]
    private bool _isListLayout = true;

    [ObservableProperty]
    private bool _isTableLayout = false;

    partial void OnIsListLayoutChanged(bool value)
    {
        OnPropertyChanged(nameof(IsTimetableLayout));
        OnPropertyChanged(nameof(CurrentLayoutIcon));
    }

    partial void OnIsTableLayoutChanged(bool value)
    {
        OnPropertyChanged(nameof(IsTimetableLayout));
        OnPropertyChanged(nameof(CurrentLayoutIcon));
    }

    public bool IsTimetableLayout => !IsListLayout && !IsTableLayout;

    public string CurrentLayoutIcon => IsListLayout ? "\uE8FD" : IsTableLayout ? "\uE8A1" : "\uECA5";

    [RelayCommand] private void SetListLayout()      { IsListLayout = true;  IsTableLayout = false; }
    [RelayCommand] private void SetTableLayout()     { IsListLayout = false; IsTableLayout = true;  }
    [RelayCommand] private void SetTimetableLayout() { IsListLayout = false; IsTableLayout = false; }

    [RelayCommand]
    private void OpenScheduleInBrowser()
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://igracias.telkomuniversity.ac.id/registration/?pageid=17985"));
    }

    [RelayCommand]
    public async Task RequestClearSessionAsync()
    {
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
             Title = "Confirm Sign Out",
             Content = "Are you sure you want to sign out and clear your schedule data?",
             PrimaryButtonText = "Sign out",
             CloseButtonText = "Cancel",
             DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
             XamlRoot = App.MainWindow.Content.XamlRoot
        };

        // Apply dynamic accent colors to fix ContentDialog button hover states
        var accentService = App.GetService<AccentColorService>();
        accentService?.ApplyToContentDialog(dialog);

        var result = await dialog.ShowAsync();
        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
        {
            await ClearSessionAsync();
        }
    }

    private async Task ClearSessionAsync()
    {
        _scheduleService.ClearSession();
        // Reset state and reload (will show needs-login)
        await LoadScheduleAsync();
    }

    [ObservableProperty]
    private string _selectedDay = string.Empty;

    private bool _suppressDayChange;
    private bool _bypassCacheOnNextLoad;
    private string _previousDay = string.Empty;

    // 1 = forward (tapped day is later in the week), -1 = backward
    public int DaySlideDirection { get; private set; } = 1;

    partial void OnSelectedDayChanged(string value)
    {
        if (!_suppressDayChange)
        {
            DayOrder.TryGetValue(_previousDay, out var prevIdx);
            DayOrder.TryGetValue(value, out var newIdx);
            DaySlideDirection = (newIdx >= prevIdx) ? 1 : -1;
            OnPropertyChanged(nameof(DaySlideDirection));
            _previousDay = value;
            _ = RebuildSelectedDayViewAsync();
        }
    }

    // Animate timetable content in/out on day switch
    private bool _isTimetableContentVisible = true;
    public bool IsTimetableContentVisible
    {
        get => _isTimetableContentVisible;
        private set
        {
            if (_isTimetableContentVisible == value) return;
            _isTimetableContentVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTimetableContentHidden));
        }
    }
    public bool IsTimetableContentHidden => !IsTimetableContentVisible;

    // Derived property for hiding main content when Empty is active
    public bool IsNotLoadingAndNotEmpty => IsNotLoading && !IsEmpty && !HasError;
    partial void OnIsEmptyChanged(bool value) => OnPropertyChanged(nameof(IsNotLoadingAndNotEmpty));
    partial void OnHasErrorChanged(bool value) => OnPropertyChanged(nameof(IsNotLoadingAndNotEmpty));
    partial void OnIsOfflineChanged(bool value)
    {
        OnPropertyChanged(nameof(CacheTimestamp));
        OnPropertyChanged(nameof(CacheTimestampMessage));
    }
    // Hook IsLoading changes too
    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotLoading));
        OnPropertyChanged(nameof(IsNotLoadingAndNotEmpty));
        OnPropertyChanged(nameof(IsInitialLoading));
    }

    /// <summary>
    /// True only during the initial load when there is no data yet.
    /// The full-page loading overlay binds to this — refreshes use the button spinner instead.
    /// </summary>
    public bool IsInitialLoading => IsLoading && Courses.Count == 0;

    [ObservableProperty]
    private AcademicYearOption? _selectedAcademicYear;

    private bool _suppressAcademicYearChange;

    async partial void OnSelectedAcademicYearChanged(AcademicYearOption? value)
    {
        if (_suppressAcademicYearChange || value == null) return;
        _scheduleService.SaveAcademicYear(value.YearCode, value.SemesterCode);

        _bypassCacheOnNextLoad = true;
        await LoadScheduleAsync();
    }

    public ObservableCollection<AcademicYearOption> AcademicYearOptions { get; } = new();

    [RelayCommand]
    public async Task LoadAcademicYearsAsync()
    {
        // Append to the same log file the service uses
#if DEBUG
        var logFile = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "TY4EHelper", "academic_years_debug.log");
        try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] [VM] LoadAcademicYearsAsync invoked\n"); } catch { }
#endif

        var options = await _scheduleService.FetchAcademicYearsAsync();
#if DEBUG
        try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] [VM] options count={options?.Count ?? -1}\n"); } catch { }
#endif

        AcademicYearOptions.Clear();
        if (options == null || options.Count == 0)
        {
            var (fallbackYear, fallbackSem) = _scheduleService.GetSavedAcademicYear();
            if (!string.IsNullOrEmpty(fallbackYear) && !string.IsNullOrEmpty(fallbackSem))
            {
                var fallback = new AcademicYearOption
                {
                    Value = $"{fallbackYear}/{fallbackSem}",
                    Text = $"{FormatAcademicYearLabel(fallbackYear, fallbackSem)} (Current) (Cached)",
                    YearCode = fallbackYear,
                    SemesterCode = fallbackSem,
                    IsSelected = true
                };

                AcademicYearOptions.Add(fallback);
                _suppressAcademicYearChange = true;
                SelectedAcademicYear = fallback;
                _suppressAcademicYearChange = false;
#if DEBUG
                try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] [VM] Using cached academic year fallback '{fallback.Text}'\n"); } catch { }
#endif
            }
            else
            {
#if DEBUG
                try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] [VM] No options and no saved year to show\n"); } catch { }
#endif
            }
            return;
        }

        foreach (var opt in options)
            AcademicYearOptions.Add(opt);

        // Prefer the user's saved preference; fall back to iGracias current.
        var (savedYear, savedSem) = _scheduleService.GetSavedAcademicYear();

        AcademicYearOption? toSelect = null;
        if (!string.IsNullOrEmpty(savedYear) && !string.IsNullOrEmpty(savedSem))
            toSelect = options.FirstOrDefault(o =>
                string.Equals(o.YearCode,     savedYear, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(o.SemesterCode, savedSem,  StringComparison.OrdinalIgnoreCase));

        toSelect ??= options.FirstOrDefault(o => o.IsSelected) ?? options.FirstOrDefault();

        if (toSelect != null)
        {
            // Restore the selection without triggering a schedule reload.
            _suppressAcademicYearChange = true;
            SelectedAcademicYear = toSelect;
            _suppressAcademicYearChange = false;
#if DEBUG
            try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] [VM] SelectedAcademicYear set to '{toSelect.Text}'\n"); } catch { }
#endif
        }
        else
        {
#if DEBUG
            try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] [VM] No year selected (toSelect was null)\n"); } catch { }
#endif
        }
    }

    private List<CourseItem> _allSorted = new();

    public ObservableCollection<CourseItem>      Courses              { get; } = new();
    public ObservableCollection<DayGroup>        CoursesByDay         { get; } = new();
    public ObservableCollection<string>          AvailableDays        { get; } = new();
    public ObservableCollection<DaySegmentItem>  AllDayItems          { get; } = new();
    public ObservableCollection<string>             TimetableTimeHeaders { get; } = new();
    public ObservableCollection<TimetableCourseRow> TimetableCourseRows  { get; } = new();

    [ObservableProperty]
    private DaySegmentItem? _selectedDayItem;

    partial void OnSelectedDayItemChanged(DaySegmentItem? value)
    {
        if (value != null && value.HasCourses)
            SelectedDay = value.Day;
    }

    public ScheduleViewModel(IScheduleService scheduleService, IBrowserLoginService browserLoginService)
    {
        _scheduleService     = scheduleService;
        _browserLoginService = browserLoginService;

        WeakReferenceMessenger.Default.Register(this);
        // Chain sequentially: populate the dropdown first, then load the schedule.
        // Running both concurrently against the same session can cause one to fail.
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        // 1. Fire cached schedule load immediately so UI populates
        var scheduleTask = LoadScheduleAsync();

        // 2. Fetch academic years in parallel (network bound)
        var yearsTask = LoadAcademicYearsAsync();

        await Task.WhenAll(scheduleTask, yearsTask);
    }

    [RelayCommand(CanExecute = nameof(CanLoginWithBrowser))]
    private async Task LoginWithBrowserAsync()
    {
        if (_browserLoginService.IsRunning) return;

        // Confirm before launching the browser so the user knows what to expect.
        var confirm = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title             = "Get ready!",
            Content           = "We'll open iGracias in a browser.\nPlease log in normally. After you sign in, the browser will close automatically and you're all set!",
            PrimaryButtonText = "Got it",
            CloseButtonText   = "Cancel",
            DefaultButton     = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            XamlRoot          = App.MainWindow.Content.XamlRoot,
        };

        App.GetService<AccentColorService>()?.ApplyToContentDialog(confirm);

        if (await confirm.ShowAsync() != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
            return;

        IsBrowserLoginRunning = true;
        LoginWithBrowserCommand.NotifyCanExecuteChanged();
        try
        {
            // Launches Edge, waits for login, captures cookies, fires SessionCookiesSavedMessage.
            await _browserLoginService.StartLoginAsync();
        }
        catch (Exception ex)
        {
            HasError     = true;
            ErrorMessage = $"Browser login failed: {ex.Message}";
        }
        finally
        {
            IsBrowserLoginRunning = false;
            LoginWithBrowserCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    public void TriggerRelogin()
    {
        ClearDisplayedScheduleData();
        IsOffline = false;
        NeedsLogin = true;
    }

    private bool CanLoginWithBrowser() => !IsBrowserLoginRunning;

    private CancellationTokenSource? _loadCts;

    /// <summary>
    /// Cancels any in-progress load and immediately marks the page as not-loading.
    /// Call this when navigating back to the page mid-refresh so the existing
    /// cached data is visible instead of an overlay blocking interaction.
    /// </summary>
    public void CancelLoad()
    {
        if (!IsLoading) return;
        _loadCts?.Cancel();
        IsLoading = false;
        // Restore the offline indicator so the InfoBar reappears if we're still offline.
        if (Schedule != null)
            IsOffline = !System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
    }

    [RelayCommand]
    public async Task LoadScheduleAsync()
    {
        // Cancel any pending load (e.g. user switched academic year while loading)
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsLoading = true;
        HasError = false;
        NeedsLogin = false;
        IsEmpty = false;
        IsOffline = false;
        ErrorMessage = string.Empty;

        try
        {
            bool bypass = _bypassCacheOnNextLoad;
            _bypassCacheOnNextLoad = false;

            // ── 1. No session → show login immediately (do NOT serve stale cache) ──
            // Run on background thread — CookieStore.Load() hits PasswordVault (synchronous crypto).
            bool hasSavedSession = await Task.Run(() => _scheduleService.HasSavedSession);
            if (!hasSavedSession)
            {
                if (token.IsCancellationRequested) return;
                ClearDisplayedScheduleData();
                NeedsLogin = true;
                return;
            }

            // ── 2. Serve from cache immediately (no network needed) ─────────────────
            // Only if not bypassed and we have cache.
            // Note: If user bypassed cache (refresh), we skip this.
            if (!bypass && _scheduleService.HasCachedSchedule)
            {
                if (token.IsCancellationRequested) return;
                // Run on background thread — SecureFileStore.Load() calls ProtectedData.Unprotect.
                var cached = await Task.Run(() => _scheduleService.GetCachedSchedule());
                if (cached != null)
                {
                    // Re-check after async gap: a CancelLoad() call could have fired while we
                    // were reading the cache file on the background thread.
                    if (token.IsCancellationRequested) return;
                    IsOffline = !_scheduleService.IsNetworkAvailable();
                    PopulateScheduleData(cached);
                    // If offline, we are done. If online, user might want fresh data eventually, but typically we stop here unless forced refresh.
                    return;   
                }
            }

            // ── 3. No cache (or bypassed): need live fetch ─────────────────────

            var sessionStatus = await _scheduleService.ValidateSessionAsync();
            if (token.IsCancellationRequested) return;

            if (sessionStatus == SessionValidationResult.NoSession)
            {
                // If we have cached content, keep showing it and let the View prompt the user.
                if (_scheduleService.HasCachedSchedule)
                {
                    var cached = await Task.Run(() => _scheduleService.GetCachedSchedule());
                    if (cached != null)
                    {
                        IsOffline = true;
                        PopulateScheduleData(cached);
                        WeakReferenceMessenger.Default.Send(new SessionExpiredMessage());
                        return;
                    }
                }
                // No cache — nothing to show, must relog.
                ClearDisplayedScheduleData();
                NeedsLogin = true;
                return;
            }
            if (sessionStatus == SessionValidationResult.NetworkError)
            {
                // Explicit or implicit offline: fall back to cache and show the InfoBar.
                if (_scheduleService.HasCachedSchedule)
                {
                    var cached = await Task.Run(() => _scheduleService.GetCachedSchedule());
                    if (cached != null)
                    {
                        IsOffline = true;
                        PopulateScheduleData(cached);
                        return;
                    }
                }

                HasError = true;
                ErrorMessage = "You’re offline and there’s no cached schedule yet. Connect and try again.";
                return;
            }

            var schedule = await _scheduleService.GetScheduleAsync(token);
            if (token.IsCancellationRequested) return;

            if (schedule != null)
                PopulateScheduleData(schedule);
            else
            {
                // If cancelled inside service (returning null), catch it here
                if (token.IsCancellationRequested) return;
                
                HasError = true;
                ErrorMessage = "Could not load schedule data. Try refreshing.";
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            // Only clear IsLoading if this is the active CTS
            if (!token.IsCancellationRequested)
                IsLoading = false;
        }
    }

    /// <summary>
    /// Explicit user-triggered refresh. Checks actual connectivity first.
    /// </summary>
    [RelayCommand]
    private async Task RefreshScheduleAsync()
    {
        _bypassCacheOnNextLoad = true;
        await LoadScheduleAsync();

        // Always refresh the dropdown — offline path collapses it to the cached entry.
        if (!NeedsLogin)
            _ = LoadAcademicYearsAsync();
    }

    /// <summary>Populates all VM collections from a <see cref="ScheduleResponse"/>.</summary>
    private void PopulateScheduleData(ScheduleResponse schedule)
    {
        Courses.Clear();
        CoursesByDay.Clear();
        AvailableDays.Clear();
        AllDayItems.Clear();
        SelectedDayItem = null;
        TimetableTimeHeaders.Clear();
        TimetableCourseRows.Clear();
        _allSorted = new();
        _suppressDayChange = true;
        SelectedDay = string.Empty;
        _suppressDayChange = false;

        Schedule = schedule;
        OnPropertyChanged(nameof(CacheTimestamp));

        EnsureAcademicYearFallbackFromSchedule(schedule);

        var rawCourses = schedule.Courses ?? new List<CourseItem>();
        IsEmpty = rawCourses.Count == 0;

        var sorted = rawCourses
            .OrderBy(c => DayOrder.TryGetValue(c.Day ?? string.Empty, out var d) ? d : 99)
            .ThenBy(c => c.Time)
            .ToList();

        // ── Compute today's course status badges ────────────────────────
        // Badges are only meaningful for the current academic year; past years
        // have all courses permanently "finished" from the calendar perspective.
        bool isCurrentAcademicYear = SelectedAcademicYear?.IsSelected == true;
        var nowTime = DateTime.Now.TimeOfDay;
        DayOrder.TryGetValue(DateTime.Now.DayOfWeek.ToString(), out var todayOrderIdx);
        foreach (var course in sorted)
        {
            if (!isCurrentAcademicYear)
            {
                course.BadgeLabel = string.Empty;
                continue;
            }

            bool isToday = DayOrder.TryGetValue(course.Day ?? string.Empty, out var courseIdx)
                           && courseIdx == todayOrderIdx;
            if (isToday)
            {
                var start = TryParseTime(course.TimeStart);
                var end   = TryParseTime(course.TimeEnd);
                if (start.HasValue && end.HasValue)
                {
                    course.BadgeLabel = nowTime >= start.Value && nowTime < end.Value ? "ONGOING"
                                      : nowTime < start.Value                        ? "UPCOMING"
                                      :                                                "FINISHED";
                }
            }
            else
            {
                course.BadgeLabel = string.Empty;
            }
        }

        foreach (var course in sorted)
            Courses.Add(course);

        _allSorted = sorted;

        var days = sorted
            .Select(c => c.Day ?? "")
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => DayOrder.TryGetValue(d, out var o) ? o : 99)
            .ToList();

        foreach (var d in days) AvailableDays.Add(d);

        bool isIndonesian = days.Any(d =>
            new[] { "Senin", "Selasa", "Rabu", "Kamis", "Jumat", "Sabtu", "Minggu" }
            .Contains(d, StringComparer.OrdinalIgnoreCase));

        var canonicalDays = isIndonesian
            ? new[] { "Senin", "Selasa", "Rabu", "Kamis", "Jumat", "Sabtu" }
            : new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

        foreach (var cd in canonicalDays)
            AllDayItems.Add(new DaySegmentItem
            {
                Day        = cd,
                HasCourses = days.Contains(cd, StringComparer.OrdinalIgnoreCase)
            });

        foreach (var cd in canonicalDays)
            CoursesByDay.Add(new DayGroup
            {
                Day     = cd,
                Courses = sorted
                    .Where(c => string.Equals(c.Day, cd, StringComparison.OrdinalIgnoreCase))
                    .ToList()
            });

        var todayIdx = DayOrder.TryGetValue(DateTime.Now.DayOfWeek.ToString(), out var ti) ? ti : -1;
        var todayName = days.FirstOrDefault(d => DayOrder.TryGetValue(d, out var i) && i == todayIdx);
        var targetDay = todayName ?? days.FirstOrDefault() ?? string.Empty;
        SelectedDay = targetDay;
        SelectedDayItem = AllDayItems.FirstOrDefault(d =>
            string.Equals(d.Day, targetDay, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureAcademicYearFallbackFromSchedule(ScheduleResponse schedule)
    {
        // If options already exist, keep them.
        if (AcademicYearOptions.Count > 0) return;

        var academicYear = schedule.AcademicYear?.Trim();
        if (string.IsNullOrWhiteSpace(academicYear)) return;

        var parts = academicYear.Split('/');
        if (parts.Length != 2) return;

        var fallback = new AcademicYearOption
        {
            Value = academicYear,
            Text = $"{FormatAcademicYearLabel(parts[0], parts[1])} (Current) (Cached)",
            YearCode = parts[0],
            SemesterCode = parts[1],
            IsSelected = true
        };

        AcademicYearOptions.Add(fallback);
        _suppressAcademicYearChange = true;
        SelectedAcademicYear = fallback;
        _suppressAcademicYearChange = false;
    }

    private static string FormatAcademicYearLabel(string yearCode, string semesterCode)
    {
        string yearPart = yearCode;
        if (!string.IsNullOrWhiteSpace(yearCode) && yearCode.Length == 4 && yearCode.All(char.IsDigit))
        {
            var start = int.Parse(yearCode.Substring(0, 2));
            var end = int.Parse(yearCode.Substring(2, 2));
            yearPart = $"20{start:D2}/20{end:D2}";
        }

        var semPart = semesterCode switch
        {
            "1" => "GANJIL",
            "2" => "GENAP",
            "3" => "PENDEK",
            _ => semesterCode
        };

        return $"{yearPart} - {semPart}";
    }

    private void ClearDisplayedScheduleData()
    {
        Schedule = null;
        IsOffline = false;
        IsEmpty = false;

        Courses.Clear();
        CoursesByDay.Clear();
        AvailableDays.Clear();
        AllDayItems.Clear();
        SelectedDayItem = null;
        TimetableTimeHeaders.Clear();
        TimetableCourseRows.Clear();
        _allSorted = new();
        _suppressDayChange = true;
        SelectedDay = string.Empty;
        _suppressDayChange = false;
    }

    // ── Timetable helpers ─────────────────────────────────────────────────

    // 180 px per hour — must match the header cell width in XAML
    private const double HourWidth = 180.0;

    private async Task RebuildSelectedDayViewAsync()
    {
        // Play hide animation (120 ms matches HideTransitions duration)
        IsTimetableContentVisible = false;
        await Task.Delay(120);

        TimetableTimeHeaders.Clear();
        TimetableCourseRows.Clear();

        if (string.IsNullOrEmpty(SelectedDay) || !_allSorted.Any()) return;

        var dayCourses = _allSorted
            .Where(c => string.Equals(c.Day, SelectedDay, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.TimeStart)
            .ToList();

        if (!dayCourses.Any()) return;

        var slots = BuildHourSlots(dayCourses);
        foreach (var s in slots) TimetableTimeHeaders.Add(s.Label);

        var minStart = slots.First().Start;

        foreach (var course in dayCourses)
        {
            var start = TryParseTime(course.TimeStart) ?? minStart;
            var end   = TryParseTime(course.TimeEnd)   ?? start.Add(TimeSpan.FromHours(1));

            TimetableCourseRows.Add(new TimetableCourseRow
            {
                Course     = course,
                LeftOffset = (start - minStart).TotalHours * HourWidth,
                CardWidth  = Math.Max((end - start).TotalHours * HourWidth, HourWidth),
            });
        }

        // Play show animation
        IsTimetableContentVisible = true;
    }

    private record HourSlot(string Label, TimeSpan Start, TimeSpan End);

    private static List<HourSlot> BuildHourSlots(List<CourseItem> courses)
    {
        var starts = courses.Select(c => TryParseTime(c.TimeStart)).Where(t => t.HasValue).Select(t => t!.Value).ToList();
        var ends   = courses.Select(c => TryParseTime(c.TimeEnd)).Where(t => t.HasValue).Select(t => t!.Value).ToList();
        if (!starts.Any() || !ends.Any()) return new();

        var minStart = starts.Min();
        var maxEnd   = ends.Max();
        var slots    = new List<HourSlot>();
        var cur      = minStart;
        while (cur < maxEnd)
        {
            var next = cur.Add(TimeSpan.FromHours(1));
            slots.Add(new HourSlot($"{cur:hh\\:mm}-{next:hh\\:mm}", cur, next));
            cur = next;
        }
        return slots;
    }

    private static TimeSpan? TryParseTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (TimeSpan.TryParseExact(s.Trim(), @"hh\:mm", null, out var t)) return t;
        if (TimeSpan.TryParseExact(s.Trim(), @"h\:mm",  null, out var t2)) return t2;
        return null;
    }

    public void Receive(SessionCookiesSavedMessage message)
    {
        // Session cookies saved (browser login completed) — reload everything.
        // InitializeAsync first fetches academic years, then the schedule.
        _ = InitializeAsync();
    }
}
