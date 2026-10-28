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
    IRecipient<SessionCookiesSavedMessage>,
    IRecipient<BrowserLoginStateMessage>
{
    private readonly IAttendanceService _attendanceService;
    private readonly IBrowserLoginService _browserLoginService;
    private readonly INavigationService _navigationService;

    // ── Observable state ────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public bool IsNotLoading => !IsLoading;

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool NeedsLogin { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool IsOffline { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginWithBrowserCommand))]
    [NotifyCanExecuteChangedFor(nameof(BeginLoginFromOnboardingCommand))]
    public partial bool IsBrowserLoginRunning { get; set; }

    /// <summary>True while refreshing an expired session.</summary>
    [ObservableProperty]
    public partial bool IsBackgroundRefreshing { get; set; }

    partial void OnIsBackgroundRefreshingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRefreshing));
        OnPropertyChanged(nameof(IsNotRefreshing));
    }

    /// <summary>True while a load or session refresh is in progress.</summary>
    public bool IsRefreshing    => IsLoading || IsBackgroundRefreshing;
    public bool IsNotRefreshing => !IsRefreshing;

    [ObservableProperty]
    public partial AttendanceResponse? AttendanceData { get; set; }

    // ── Summary stats (bound to XAML) ────────────────────────────────────────

    [ObservableProperty]
    public partial int TotalCourses { get; set; }

    [ObservableProperty]
    public partial int TotalAttended { get; set; }

    [ObservableProperty]
    public partial int TotalSessionsCount { get; set; }

    [ObservableProperty]
    public partial int AtRiskCount { get; set; }

    [ObservableProperty]
    public partial int WarningCount { get; set; }

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
        OnPropertyChanged(nameof(IsRefreshing));
        OnPropertyChanged(nameof(IsNotRefreshing));
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
    public partial AcademicYearOption? SelectedAcademicYear { get; set; }

    private bool _suppressAcademicYearChange;

    async partial void OnSelectedAcademicYearChanged(AcademicYearOption? value)
    {
        if (_suppressAcademicYearChange || value == null) return;
        FeatureFlowLogger.Write("Attendance", $"academic-year selected: {value.Value} | {value.Text}");
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

        WeakReferenceMessenger.Default.Register<SessionCookiesSavedMessage>(this);
        WeakReferenceMessenger.Default.Register<BrowserLoginStateMessage>(this);
        // Seed state in case login was already started before this VM was constructed.
        IsBrowserLoginRunning = _browserLoginService.IsRunning;
        Initialization = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        IsLoading = true;
        HasError = false;
        NeedsLogin = false;
        try
        {
            await LoadAcademicYearsAsync();
            await LoadAttendanceAsync();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Could not initialize attendance: {ex.Message}";
            IsLoading = false;
        }
    }

    public Task Initialization { get; private set; }

    // ── Commands ────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanLoginWithBrowser))]
    private async Task BeginLoginFromOnboardingAsync() => await LoginWithBrowserAsync();

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
        WeakReferenceMessenger.Default.Send(new BrowserLoginStateMessage(true));
        try
        {
            var ok = await _browserLoginService.StartLoginAsync();
            _ = ok; // login triggers SessionCookiesSavedMessage on success
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
            WeakReferenceMessenger.Default.Send(new BrowserLoginStateMessage(false));
        }
    }

    private bool CanLoginWithBrowser() => !IsBrowserLoginRunning;

    public void Receive(BrowserLoginStateMessage message)
    {
        IsBrowserLoginRunning = message.Value;
        LoginWithBrowserCommand.NotifyCanExecuteChanged();
    }

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
            Content = "Are you sure you want to sign out and clear your academic data?",
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
            // Clear cookies + saved credentials → shows login screen, no auto-reconnect
            App.GetService<IScheduleService>().ClearSession();
            _browserLoginService.ClearCredentials();
            await LoadAttendanceAsync();
        }
    }

    private CancellationTokenSource? _loadCts;

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
            string? year = SelectedAcademicYear?.Value;
            AttendanceResponse? data = null;
            if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
            {
                data = await _attendanceService.GetAttendanceAsync(year, token);
                if (data == null && _browserLoginService.HasSavedCredentials
                    && await _browserLoginService.TrySilentLoginAsync(token))
                    data = await _attendanceService.GetAttendanceAsync(year, token);
            }
            token.ThrowIfCancellationRequested();
            if (data == null)
            {
                var cached = await Task.Run(_attendanceService.GetCachedAttendance, token);
                if (cached != null && (year == null || cached.AcademicYear == year))
                {
                    data = cached;
                    IsOffline = true;
                }
            }
            token.ThrowIfCancellationRequested();
            if (data != null)
                PopulateData(data);
            else
            {
                NeedsLogin = !_browserLoginService.HasSavedCredentials;
                HasError = !NeedsLogin;
                ErrorMessage = "Could not load attendance for this period. Check your connection or sign in again.";
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) { HasError = true; ErrorMessage = $"Could not load attendance: {ex.Message}"; }
        }
        finally
        {
            if (!token.IsCancellationRequested) IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAttendanceAsync()
    {
        FeatureFlowLogger.Write("Attendance", $"refresh start: selected={SelectedAcademicYear?.Value ?? "(null)"}");
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
        _loadCts?.Cancel();
        Initialization = InitializeAsync();
    }
}
