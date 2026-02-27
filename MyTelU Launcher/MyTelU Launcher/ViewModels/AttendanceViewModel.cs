using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MyTelU_Launcher.Helpers;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.Services;
using MyTelU_Launcher.Contracts.Services;

namespace MyTelU_Launcher.ViewModels;

public partial class AttendanceViewModel : ObservableRecipient,
    IRecipient<SessionCookiesSavedMessage>
{
    private readonly IAttendanceService _attendanceService;
    private readonly IBrowserLoginService _browserLoginService;
    private readonly INavigationService _navigationService;

    // ── Observable state ────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isLoading;

    public bool IsNotLoading => !IsLoading;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _needsLogin;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isOffline;

    [ObservableProperty]
    private bool _isBrowserLoginRunning;

    [ObservableProperty]
    private AttendanceResponse? _attendanceData;

    // ── Summary stats (bound to XAML) ────────────────────────────────────────

    [ObservableProperty]
    private int _totalCourses;

    [ObservableProperty]
    private int _totalAttended;

    [ObservableProperty]
    private int _totalSessionsCount;

    [ObservableProperty]
    private int _atRiskCount;

    [ObservableProperty]
    private int _warningCount;

    // ── Derived ─────────────────────────────────────────────────────────────

    public bool IsNotLoadingAndNotEmpty => IsNotLoading && !IsEmpty && !HasError;

    public string CacheTimestamp
    {
        get
        {
            if (!IsOffline || AttendanceData is null) return string.Empty;
            if (DateTime.TryParse(AttendanceData.FetchTime, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            {
                var ago = DateTime.Now - dt;
                if (ago.TotalMinutes < 2) return "just now";
                if (ago.TotalHours < 1) return $"{(int)ago.TotalMinutes} min ago";
                if (ago.TotalDays < 1) return $"{(int)ago.TotalHours} hr ago";
                if (ago.TotalDays < 7) return $"{(int)ago.TotalDays} day{((int)ago.TotalDays == 1 ? "" : "s")} ago";
                return dt.ToString("d MMM yyyy");
            }
            return "cached";
        }
    }

    public string CacheTimestampMessage =>
        IsOffline ? $"Viewing cached attendance, last synced {CacheTimestamp}." : string.Empty;

    /// <summary>
    /// True only during the initial load when there is no data yet.
    /// The full-page loading overlay binds to this — refreshes use the button spinner instead.
    /// </summary>
    public bool IsInitialLoading => IsLoading && Courses.Count == 0;

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotLoading));
        OnPropertyChanged(nameof(IsNotLoadingAndNotEmpty));
        OnPropertyChanged(nameof(IsInitialLoading));
    }

    partial void OnIsEmptyChanged(bool value) => OnPropertyChanged(nameof(IsNotLoadingAndNotEmpty));
    partial void OnHasErrorChanged(bool value) => OnPropertyChanged(nameof(IsNotLoadingAndNotEmpty));

    partial void OnIsOfflineChanged(bool value)
    {
        OnPropertyChanged(nameof(CacheTimestamp));
        OnPropertyChanged(nameof(CacheTimestampMessage));
    }

    // ── Academic year selection ──────────────────────────────────────────────

    [ObservableProperty]
    private AcademicYearOption? _selectedAcademicYear;

    private bool _suppressAcademicYearChange;

    async partial void OnSelectedAcademicYearChanged(AcademicYearOption? value)
    {
        if (_suppressAcademicYearChange || value == null) return;
        _bypassCacheOnNextLoad = true;
        await LoadAttendanceAsync();
    }

    public ObservableCollection<AcademicYearOption> AcademicYearOptions { get; } = new();

    // ── Courses collection ──────────────────────────────────────────────────

    public ObservableCollection<AttendanceCourseItem> Courses { get; } = new();

    // ── Constructor ─────────────────────────────────────────────────────────

    public AttendanceViewModel(IAttendanceService attendanceService, IBrowserLoginService browserLoginService, INavigationService navigationService)
    {
        _attendanceService = attendanceService;
        _browserLoginService = browserLoginService;
        _navigationService = navigationService;

        WeakReferenceMessenger.Default.Register(this);
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        var attendanceTask = LoadAttendanceAsync();
        var yearsTask = LoadAcademicYearsAsync();
        await Task.WhenAll(attendanceTask, yearsTask);
    }

    // ── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task BeginLoginFromOnboardingAsync() => await LoginWithBrowserAsync();

    [RelayCommand(CanExecute = nameof(CanLoginWithBrowser))]
    private async Task LoginWithBrowserAsync()
    {
        if (_browserLoginService.IsRunning) return;

        IsBrowserLoginRunning = true;
        LoginWithBrowserCommand.NotifyCanExecuteChanged();
        try
        {
            await _browserLoginService.StartLoginAsync();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Browser login failed: {ex.Message}";
        }
        finally
        {
            IsBrowserLoginRunning = false;
            LoginWithBrowserCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanLoginWithBrowser() => !IsBrowserLoginRunning;

    [RelayCommand]
    private void OpenAttendanceInBrowser()
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://igracias.telkomuniversity.ac.id/presence/?pageid=3942"));
    }

    [RelayCommand]
    public async Task RequestClearSessionAsync()
    {
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "Confirm Sign Out",
            Content = "Are you sure you want to sign out and clear your attendance data?",
            PrimaryButtonText = "Sign out",
            CloseButtonText = "Cancel",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var accentService = App.GetService<AccentColorService>();
        accentService?.ApplyToContentDialog(dialog);

        var result = await dialog.ShowAsync();
        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
        {
            // Clear cookies and reload → shows login screen
            App.GetService<IScheduleService>().ClearSession();
            await LoadAttendanceAsync();
        }
    }

    private CancellationTokenSource? _loadCts;
    private bool _bypassCacheOnNextLoad;

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
        if (AttendanceData != null)
            IsOffline = !System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
    }

    [RelayCommand]
    public async Task LoadAttendanceAsync()
    {
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

            // 1. No session → show login
            bool hasSavedSession = await Task.Run(() =>
            {
                try
                {
                    var json = CookieStore.Load();
                    if (json == null) return false;
                    var d = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    return d != null && d.ContainsKey("PHPSESSID");
                }
                catch { return false; }
            });

            if (!hasSavedSession)
            {
                if (token.IsCancellationRequested) return;
                ClearDisplayed();
                NeedsLogin = true;
                return;
            }

            // 2. Serve from cache if not bypassed
            if (!bypass && _attendanceService.HasCachedAttendance)
            {
                if (token.IsCancellationRequested) return;
                var cached = await Task.Run(() => _attendanceService.GetCachedAttendance());
                if (cached != null)
                {
                    IsOffline = !System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
                    PopulateData(cached);
                    return;
                }
            }

            // 3. Live fetch
            var schoolYear = SelectedAcademicYear?.Value;
            var data = await _attendanceService.GetAttendanceAsync(schoolYear, token);
            if (token.IsCancellationRequested) return;

            if (data != null)
            {
                PopulateData(data);
            }
            else
            {
                if (token.IsCancellationRequested) return;

                // Fall back to cache
                if (_attendanceService.HasCachedAttendance)
                {
                    var cached = await Task.Run(() => _attendanceService.GetCachedAttendance());
                    if (cached != null)
                    {
                        IsOffline = true;
                        PopulateData(cached);
                        return;
                    }
                }

                HasError = true;
                ErrorMessage = "Could not load attendance data. Check your connection or try refreshing.";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAttendanceAsync()
    {
        _bypassCacheOnNextLoad = true;
        await LoadAttendanceAsync();

        // Always refresh the dropdown — offline path collapses it to the cached entry.
        if (!NeedsLogin)
            _ = LoadAcademicYearsAsync();
    }

    /// <summary>
    /// Fetches per-session detail for a course. Called directly from the page
    /// code-behind so that the page controls the ContentDialog lifecycle.
    /// </summary>
    public Task<AttendanceCourseDetail?> LoadCourseDetailAsync(AttendanceCourseItem course,
        CancellationToken ct = default)
    {
        Debug.WriteLine($"[AttendanceViewModel] LoadCourseDetailAsync: {course.CourseCode} id={course.CourseId}");
        if (course.CourseId == null)
            return Task.FromResult<AttendanceCourseDetail?>(null);

        return _attendanceService.GetCourseDetailAsync(course.CourseId.Value, ct);
    }

    // ── Data population ─────────────────────────────────────────────────────

    private void PopulateData(AttendanceResponse data)
    {
        Courses.Clear();
        AttendanceData = data;

        foreach (var course in data.Courses)
            Courses.Add(course);

        IsEmpty = data.Courses.Count == 0;

        // Summary
        TotalCourses = data.Summary.TotalCourses;
        TotalAttended = data.Summary.TotalAttended;
        TotalSessionsCount = data.Summary.TotalSessions ?? 0;
        AtRiskCount = data.Summary.CoursesBelow75Pct;
        WarningCount = data.Summary.CoursesBelow80Pct - data.Summary.CoursesBelow75Pct;

        OnPropertyChanged(nameof(CacheTimestamp));
        OnPropertyChanged(nameof(CacheTimestampMessage));

        EnsureAcademicYearFallback(data);
    }

    private void ClearDisplayed()
    {
        AttendanceData = null;
        IsOffline = false;
        IsEmpty = false;
        Courses.Clear();
        TotalCourses = 0;
        TotalAttended = 0;
        TotalSessionsCount = 0;
        AtRiskCount = 0;
        WarningCount = 0;
    }

    // ── Academic year helpers ────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadAcademicYearsAsync()
    {
        var options = await _attendanceService.FetchAvailableSemestersAsync();

        AcademicYearOptions.Clear();
        if (options.Count == 0)
        {
            // Fallback from currently loaded data
            if (AttendanceData != null && !string.IsNullOrWhiteSpace(AttendanceData.AcademicYear))
            {
                var parts = AttendanceData.AcademicYear.Split('/');
                if (parts.Length == 2)
                {
                    var fb = new AcademicYearOption
                    {
                        Value = AttendanceData.AcademicYear,
                        Text = $"{FormatAcademicYearLabel(parts[0], parts[1])} (Cached)",
                        YearCode = parts[0],
                        SemesterCode = parts[1],
                        IsSelected = true,
                    };
                    AcademicYearOptions.Add(fb);
                    _suppressAcademicYearChange = true;
                    SelectedAcademicYear = fb;
                    _suppressAcademicYearChange = false;
                }
            }
            return;
        }

        foreach (var opt in options)
            AcademicYearOptions.Add(opt);

        // Select current or first
        var toSelect = options.FirstOrDefault(o => o.IsSelected) ?? options.FirstOrDefault();

        // If we have loaded data, try to match it
        if (AttendanceData != null && !string.IsNullOrWhiteSpace(AttendanceData.AcademicYear))
        {
            var match = options.FirstOrDefault(o => o.Value == AttendanceData.AcademicYear);
            if (match != null) toSelect = match;
        }

        if (toSelect != null)
        {
            _suppressAcademicYearChange = true;
            SelectedAcademicYear = toSelect;
            _suppressAcademicYearChange = false;
        }
    }

    private void EnsureAcademicYearFallback(AttendanceResponse data)
    {
        if (AcademicYearOptions.Count > 0) return;

        var ay = data.AcademicYear?.Trim();
        if (string.IsNullOrWhiteSpace(ay)) return;

        var parts = ay.Split('/');
        if (parts.Length != 2) return;

        var fb = new AcademicYearOption
        {
            Value = ay,
            Text = $"{FormatAcademicYearLabel(parts[0], parts[1])} (Cached)",
            YearCode = parts[0],
            SemesterCode = parts[1],
            IsSelected = true,
        };

        AcademicYearOptions.Add(fb);
        _suppressAcademicYearChange = true;
        SelectedAcademicYear = fb;
        _suppressAcademicYearChange = false;
    }

    private static string FormatAcademicYearLabel(string yearCode, string semesterCode)
    {
        var yearPart = yearCode;
        if (!string.IsNullOrWhiteSpace(yearCode) && yearCode.Length == 4 && yearCode.All(char.IsDigit))
        {
            var start = int.Parse(yearCode[..2]);
            var end = int.Parse(yearCode[2..]);
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

    // ── Messaging ───────────────────────────────────────────────────────────

    public void Receive(SessionCookiesSavedMessage message)
    {
        _ = InitializeAsync();
    }
}
