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

public partial class GradeViewModel : ObservableRecipient,
    IRecipient<SessionCookiesSavedMessage>,
    IRecipient<BrowserLoginStateMessage>
{
    private readonly IGradeService _gradeService;
    private readonly IBrowserLoginService _browserLoginService;
    private readonly INavigationService _navigationService;
    private readonly ILocalSettingsService _localSettingsService;
    private string _savedPeriodKey = string.Empty; // "SchoolYear:Semester" persisted across restarts

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
    [NotifyCanExecuteChangedFor(nameof(LoginWithBrowserCommand))]
    [NotifyCanExecuteChangedFor(nameof(BeginLoginFromOnboardingCommand))]
    private bool _isBrowserLoginRunning;

    [ObservableProperty]
    private bool _isBackgroundRefreshing;

    partial void OnIsBackgroundRefreshingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRefreshing));
        OnPropertyChanged(nameof(IsNotRefreshing));
    }

    public bool IsRefreshing    => IsLoading || IsBackgroundRefreshing;
    public bool IsNotRefreshing => !IsRefreshing;

    public bool IsInitialLoading => IsLoading && Grades.Count == 0;

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotLoading));
        OnPropertyChanged(nameof(IsNotLoadingAndNotEmpty));
        OnPropertyChanged(nameof(IsInitialLoading));
        OnPropertyChanged(nameof(IsRefreshing));
        OnPropertyChanged(nameof(IsNotRefreshing));
    }

    partial void OnIsEmptyChanged(bool value)   => OnPropertyChanged(nameof(IsNotLoadingAndNotEmpty));
    partial void OnHasErrorChanged(bool value)  => OnPropertyChanged(nameof(IsNotLoadingAndNotEmpty));
    partial void OnIsOfflineChanged(bool value)
    {
        OnPropertyChanged(nameof(CacheTimestamp));
        OnPropertyChanged(nameof(CacheTimestampMessage));
    }

    public bool IsNotLoadingAndNotEmpty => IsNotLoading && !IsEmpty && !HasError;

    // ── Cache timestamp ──────────────────────────────────────────────────────

    [ObservableProperty]
    private GradeResponse? _gradeData;

    public string CacheTimestamp
    {
        get
        {
            if (!IsOffline || GradeData is null) return string.Empty;
            if (DateTime.TryParse(GradeData.FetchTime, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            {
                var ago = DateTime.Now - dt;
                if (ago.TotalMinutes < 2) return "just now";
                if (ago.TotalHours < 1)  return $"{(int)ago.TotalMinutes} min ago";
                if (ago.TotalDays < 1)   return $"{(int)ago.TotalHours} hr ago";
                if (ago.TotalDays < 7)   return $"{(int)ago.TotalDays} day{((int)ago.TotalDays == 1 ? "" : "s")} ago";
                return dt.ToString("d MMM yyyy");
            }
            return "cached";
        }
    }

    public string CacheTimestampMessage =>
        IsOffline ? $"Viewing cached grades, last synced {CacheTimestamp}." : string.Empty;

    // ── Summary stats ────────────────────────────────────────────────────────

    [ObservableProperty] private int _totalCourses;
    [ObservableProperty] private int _completedCourses;
    [ObservableProperty] private int _inProgressCourses;
    [ObservableProperty] private int _totalCredits;
    [ObservableProperty] private string _currentGpa = "-";

    public string CurrentGpaLabel =>
        SelectedPeriod == null || string.IsNullOrEmpty(SelectedPeriod.SchoolYear) ? "IPK" : "IP";

    // ── Filter: combined period picker ─────────────────────────────────────

    public ObservableCollection<GradePeriodOption> PeriodOptions { get; } = new();

    [ObservableProperty]
    private GradePeriodOption? _selectedPeriod;

    private bool _suppressPeriodChange;
    private bool _userHasSelectedPeriod;

    private static string BuildPeriodKey(GradePeriodOption? period)
        => (period?.SchoolYear ?? string.Empty) + ":" + (period?.Semester ?? string.Empty);

    private async Task PersistSelectedPeriodAsync(GradePeriodOption? period)
    {
        _savedPeriodKey = BuildPeriodKey(period);
        await _localSettingsService.SaveSettingAsync("GradeSelectedPeriod", _savedPeriodKey);
    }

    private GradePeriodOption? FindPeriodByKey(string key)
    {
        var parts = key.Split(':');
        if (parts.Length != 2) return null;

        return PeriodOptions.FirstOrDefault(
            p => p.SchoolYear == parts[0] && p.Semester == parts[1]);
    }

    async partial void OnSelectedPeriodChanged(GradePeriodOption? value)
    {
        if (_suppressPeriodChange || value == null) return;
        // Mark that the user explicitly changed the period so future async
        // updates won't overwrite their selection.
        _userHasSelectedPeriod = true;
        // Persist across app restarts.
        await PersistSelectedPeriodAsync(value);
        FeatureFlowLogger.Write("Grade", $"period selected: key={BuildPeriodKey(value)}, persisted={_savedPeriodKey}, label={value.Label}");
        OnPropertyChanged(nameof(CurrentGpaLabel));
        _bypassCacheOnNextLoad = true;
        await LoadGradesAsync();
    }

    // ── Grades collection ────────────────────────────────────────────────────

    public ObservableCollection<GradeItem> Grades { get; } = new();

    public bool IsInitializing { get; private set; }

    // ── Constructor ──────────────────────────────────────────────────────────

    public GradeViewModel(IGradeService gradeService, IBrowserLoginService browserLoginService, INavigationService navigationService, ILocalSettingsService localSettingsService)
    {
        _gradeService = gradeService;
        _browserLoginService = browserLoginService;
        _navigationService = navigationService;
        _localSettingsService = localSettingsService;

        WeakReferenceMessenger.Default.RegisterAll(this);
        // Seed state in case login was already started before this VM was constructed.
        IsBrowserLoginRunning = _browserLoginService.IsRunning;
        // already has a value selected on the first render, before any async work.
        PrePopulatePeriodsFromCache();

        IsInitializing = true;
        _ = InitializeAsync();
    }

    /// <summary>Preloads period options from cache so the ComboBox has a stable initial selection.</summary>
    private void PrePopulatePeriodsFromCache()
    {
        var cached = _gradeService.GetCachedGrades();
        if (cached == null || cached.Grades.Count == 0) return;

        _suppressPeriodChange = true;

        var allPeriods = new GradePeriodOption("", "", "All Periods");
        PeriodOptions.Add(allPeriods);

        var periods = cached.Grades
            .Where(g => !string.IsNullOrEmpty(g.SchoolYear))
            .Select(g => (g.SchoolYear, g.Semester))
            .Distinct()
            .OrderByDescending(p => p.SchoolYear)
            .ThenBy(p => p.Semester)
            .ToList();

        foreach (var (sy, sem) in periods)
        {
            var semLabel = _semesters.FirstOrDefault(s => s.Code == sem).Label ?? sem;
            PeriodOptions.Add(new GradePeriodOption(sy, sem, $"{FormatYearCode(sy)} - {semLabel} (Cached)"));
        }

        // Match the first render to the scope that produced the cached data.
        if (!string.IsNullOrEmpty(cached.SchoolYear) || !string.IsNullOrEmpty(cached.Semester))
        {
            SelectedPeriod = PeriodOptions.FirstOrDefault(
                p => p.SchoolYear == cached.SchoolYear && p.Semester == cached.Semester) ?? allPeriods;
            _suppressPeriodChange = false;
            return;
        }

        if (string.IsNullOrEmpty(cached.SchoolYear) && string.IsNullOrEmpty(cached.Semester))
        {
            SelectedPeriod = allPeriods;
            _suppressPeriodChange = false;
            return;
        }

        // Prefer the most recent in-progress period, but do not infer across filtered snapshots.
        bool cacheIsFiltered = !string.IsNullOrEmpty(cached.SchoolYear);
        var (targetSy, targetSem) =
            MostRecentInProgressPeriod(cached.Grades)
            ?? (cacheIsFiltered ? null : MostRecentAnyPeriod(cached.Grades))
            ?? (null, null);

        SelectedPeriod = (!string.IsNullOrEmpty(targetSy)
            ? PeriodOptions.FirstOrDefault(p => p.SchoolYear == targetSy && p.Semester == targetSem)
            : null) ?? allPeriods;

        _suppressPeriodChange = false;
    }

    private async Task InitializeAsync()
    {
        try
        {
            // Load persisted period selection before running LoadAcademicYearsAsync so it
            // can restore the user's choice instead of auto-selecting the current period.
            _savedPeriodKey = await _localSettingsService.ReadSettingAsync<string>("GradeSelectedPeriod") ?? string.Empty;

            // Match Schedule/Attendance behavior: resolve the final selection first,
            // then load the content for that selection. This avoids the ComboBox and
            // displayed grade list drifting out of sync during startup.
            await LoadAcademicYearsAsync();
            await LoadGradesAsync();
        }
        finally
        {
            IsInitializing = false;
        }
    }

    // ── IRecipient ───────────────────────────────────────────────────────────

    public void Receive(SessionCookiesSavedMessage message)
    {
        _bypassCacheOnNextLoad = true;
        _ = LoadGradesAsync();
        _ = LoadAcademicYearsAsync();
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshGradesAsync()
    {
        _userHasSelectedPeriod = true;
        await PersistSelectedPeriodAsync(SelectedPeriod);
        FeatureFlowLogger.Write("Grade", $"refresh start: key={BuildPeriodKey(SelectedPeriod)}, persisted={_savedPeriodKey}, label={SelectedPeriod?.Label ?? "(null)"}");

        _bypassCacheOnNextLoad = true;
        await LoadGradesAsync();

        // Refresh academic year labels — offline path collapses them to "(Cached)" entries.
        if (!NeedsLogin)
        {
            await LoadAcademicYearsAsync();

            var restored = FindPeriodByKey(_savedPeriodKey);
            if (restored != null
                && (SelectedPeriod?.SchoolYear != restored.SchoolYear
                    || SelectedPeriod?.Semester != restored.Semester))
            {
                _suppressPeriodChange = true;
                SelectedPeriod = restored;
                _suppressPeriodChange = false;
                FeatureFlowLogger.Write("Grade", $"refresh restored selection: key={BuildPeriodKey(restored)}, label={restored.Label}");
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoginWithBrowser))]
    private async Task BeginLoginFromOnboardingAsync() => await LoginWithBrowserAsync();

    [RelayCommand(CanExecute = nameof(CanLoginWithBrowser))]
    private async Task LoginWithBrowserAsync()
    {
        if (_browserLoginService.IsRunning) return;

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
    private void OpenGradesInBrowser()
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://igracias.telkomuniversity.ac.id/score/?pageid=11"));
    }

    [RelayCommand]
    public async Task RequestClearSessionAsync()
    {
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title             = "Confirm Sign Out",
            Content           = "Are you sure you want to sign out and clear your academic data?",
            PrimaryButtonText = "Sign out",
            CloseButtonText   = "Cancel",
            DefaultButton     = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            XamlRoot          = App.MainWindow.Content.XamlRoot,
        };

        App.GetService<AccentColorService>()?.ApplyToContentDialog(dialog);

        var result = await dialog.ShowAsync();
        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
        {
            // Clear cookies + saved credentials → shows login screen, no auto-reconnect
            App.GetService<IScheduleService>().ClearSession();
            _browserLoginService.ClearCredentials();
            await LoadGradesAsync();
        }
    }

    // ── Core load logic ──────────────────────────────────────────────────────

    private CancellationTokenSource? _loadCts;
    private bool _bypassCacheOnNextLoad;

    public void CancelLoad()
    {
        if (!IsLoading) return;
        _loadCts?.Cancel();
        IsLoading = false;
        if (GradeData != null)
            IsOffline = !System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
    }

    /// <summary>
    /// Returns a copy of <paramref name="data"/> filtered to <see cref="SelectedPeriod"/>.
    /// When no specific period is selected (null SchoolYear) returns the data unchanged.
    /// </summary>
    private GradeResponse FilterCacheByPeriod(GradeResponse data)
    {
        if (SelectedPeriod == null || string.IsNullOrEmpty(SelectedPeriod.SchoolYear))
            return data;

        var filtered = data.Grades
            .Where(g => g.SchoolYear == SelectedPeriod.SchoolYear
                     && (string.IsNullOrEmpty(SelectedPeriod.Semester) || g.Semester == SelectedPeriod.Semester))
            .ToList();

        return new GradeResponse { FetchTime = data.FetchTime, Grades = filtered };
    }

    [RelayCommand]
    public async Task LoadGradesAsync()
    {
        FeatureFlowLogger.Write("Grade", $"load start: key={BuildPeriodKey(SelectedPeriod)}, label={SelectedPeriod?.Label ?? "(null)"}, bypass={_bypassCacheOnNextLoad}, online={System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()}");
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsLoading     = true;
        HasError      = false;
        NeedsLogin    = false;
        IsEmpty       = false;
        IsOffline     = false;
        ErrorMessage  = string.Empty;

        try
        {
            bool bypass = _bypassCacheOnNextLoad;
            _bypassCacheOnNextLoad = false;

            // 1. Check for saved session
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

                if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()
                    && _browserLoginService.HasSavedCredentials)
                {
                    if (_gradeService.HasCachedGrades)
                    {
                        var cached = await Task.Run(() => _gradeService.GetCachedGrades());
                        if (cached != null && !token.IsCancellationRequested)
                        {
                            IsLoading = false;
                            PopulateData(FilterCacheByPeriod(cached));
                            FeatureFlowLogger.Write("Grade", "served cached grades, starting background reconnect");
                            _ = BackgroundReconnectAsync(token);
                            return;
                        }
                    }

                    IsBrowserLoginRunning = true;
                    var ok = await _browserLoginService.TrySilentLoginAsync(token);
                    IsBrowserLoginRunning = false;

                    if (token.IsCancellationRequested) return;

                    if (!ok)
                    {
                        ClearDisplayed();
                        NeedsLogin = true;
                        FeatureFlowLogger.Write("Grade", "silent login failed: showing NeedsLogin");
                        return;
                    }
                    FeatureFlowLogger.Write("Grade", "silent login succeeded: continuing with live fetch");
                    bypass = true;
                    _ = LoadAcademicYearsAsync();
                }
                else
                {
                    // Only serve offline cache when the network is actually unavailable.
                    // If network is available but credentials are gone (explicit logout), go straight to login.
                    if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()
                        && _gradeService.HasCachedGrades)
                    {
                        var cached = await Task.Run(() => _gradeService.GetCachedGrades());
                        if (cached != null)
                        {
                            if (token.IsCancellationRequested) return;
                            IsOffline = true;
                            PopulateData(FilterCacheByPeriod(cached));
                            FeatureFlowLogger.Write("Grade", "offline with cache: showing cached grades");
                            return;
                        }
                    }
                    ClearDisplayed();
                    NeedsLogin = true;
                    return;
                }
            }

            if (!bypass && _gradeService.HasCachedGrades)
            {
                if (token.IsCancellationRequested) return;
                var cached = await Task.Run(() => _gradeService.GetCachedGrades());
                if (cached != null)
                {
                    if (token.IsCancellationRequested) return;
                    PopulateData(FilterCacheByPeriod(cached));
                    FeatureFlowLogger.Write("Grade", "served cached grades, starting background validation");
                    if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()
                        && _browserLoginService.HasSavedCredentials)
                        _ = BackgroundValidateAndRefreshAsync(token);
                    else
                        IsOffline = !System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
                    return;
                }
            }

            // 3. Live fetch
            var schoolYear = SelectedPeriod?.SchoolYear ?? "";
            var semester   = SelectedPeriod?.Semester ?? "";
            var data = await _gradeService.GetGradesAsync(schoolYear, semester, token);
            if (token.IsCancellationRequested) return;

            if (data != null)
            {
                PopulateData(data);
                FeatureFlowLogger.Write("Grade", $"live fetch success: key={schoolYear}:{semester}, grades={data.Grades.Count}");
            }
            else
            {
                if (_gradeService.HasCachedGrades)
                {
                    var cached = await Task.Run(() => _gradeService.GetCachedGrades());
                    if (cached != null)
                    {
                        // A failed live fetch while online is usually temporary, not true offline mode.
                        IsOffline = !System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
                        PopulateData(FilterCacheByPeriod(cached));
                        FeatureFlowLogger.Write("Grade", "live fetch failed: fell back to cached grades");
                        return;
                    }
                }
                HasError     = true;
                ErrorMessage = "Could not load grades. Check your connection or try refreshing.";
                FeatureFlowLogger.Write("Grade", "live fetch failed: no cache available");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                HasError     = true;
                ErrorMessage = $"Unexpected error: {ex.Message}";
                Debug.WriteLine($"[GradeViewModel] LoadGradesAsync error: {ex}");
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsLoading = false;
        }
    }

    private async Task BackgroundReconnectAsync(CancellationToken token)
    {
        IsBackgroundRefreshing = true;
        FeatureFlowLogger.Write("Grade", "background reconnect start");
        try
        {
            var ok = await _browserLoginService.TrySilentLoginAsync(token);
            if (ok && !token.IsCancellationRequested)
            {
                await LoadAcademicYearsAsync();
                var schoolYear = SelectedPeriod?.SchoolYear ?? "";
                var semester   = SelectedPeriod?.Semester ?? "";
                var data = await _gradeService.GetGradesAsync(schoolYear, semester, token);
                if (data != null && !token.IsCancellationRequested)
                {
                    PopulateData(data);
                    IsOffline = false;
                    FeatureFlowLogger.Write("Grade", $"background reconnect success: key={schoolYear}:{semester}");
                }
                else if (!token.IsCancellationRequested)
                {
                    IsOffline = true; // re-auth succeeded but grade fetch failed
                    FeatureFlowLogger.Write("Grade", "background reconnect failed after relogin");
                }
            }
            else if (!token.IsCancellationRequested)
            {
                IsOffline = true; // silent login failed; showing stale cached data
                FeatureFlowLogger.Write("Grade", "background reconnect failed");
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[GradeViewModel] BackgroundReconnect: {ex.Message}"); if (!token.IsCancellationRequested) IsOffline = true; }
        finally { IsBackgroundRefreshing = false; }
    }

    private async Task BackgroundValidateAndRefreshAsync(CancellationToken token)
    {
        IsBackgroundRefreshing = true;
        FeatureFlowLogger.Write("Grade", "background validate start");
        try
        {
            var schoolYear = SelectedPeriod?.SchoolYear ?? "";
            var semester   = SelectedPeriod?.Semester ?? "";
            var data = await _gradeService.GetGradesAsync(schoolYear, semester, token);
            if (token.IsCancellationRequested) return;

            if (data != null)
            {
                PopulateData(data);
                IsOffline = false;
                FeatureFlowLogger.Write("Grade", $"background validate success without relogin: key={schoolYear}:{semester}");
                _ = LoadAcademicYearsAsync();
                return;
            }

            // null — session expired (or offline). Try silent re-login if credentials exist.
            if (!_browserLoginService.HasSavedCredentials)
            {
                if (Grades.Count > 0)
                    IsOffline = true;
                else
                    NeedsLogin = true;
                FeatureFlowLogger.Write("Grade", "background validate failed: no saved credentials");
                return;
            }

            var ok = await _browserLoginService.TrySilentLoginAsync(token);
            if (!ok || token.IsCancellationRequested)
            {
                if (!token.IsCancellationRequested)
                {
                    if (Grades.Count > 0)
                        IsOffline = true;
                    else
                        NeedsLogin = true;
                }
                FeatureFlowLogger.Write("Grade", "background validate relogin failed");
                return;
            }

            _ = LoadAcademicYearsAsync();
            data = await _gradeService.GetGradesAsync(schoolYear, semester, token);
            if (data != null && !token.IsCancellationRequested)
            {
                PopulateData(data);
                IsOffline = false;
                FeatureFlowLogger.Write("Grade", $"background validate success after relogin: key={schoolYear}:{semester}");
            }
            else if (!token.IsCancellationRequested)
            {
                if (Grades.Count > 0)
                    IsOffline = true;
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex) { Debug.WriteLine($"[GradeViewModel] BackgroundValidate: {ex.Message}"); if (!token.IsCancellationRequested) IsOffline = true; }
        finally { IsBackgroundRefreshing = false; }
    }

    /// <summary>Returns the most recent period that still has in-progress grades.</summary>
    private static (string SchoolYear, string Semester)? MostRecentInProgressPeriod(IEnumerable<GradeItem>? grades)
    {
        if (grades == null) return null;
        var key = grades
            .Where(g => g.InProgress && !string.IsNullOrEmpty(g.SchoolYear))
            .Select(g => g.SchoolYear + "/" + g.Semester)
            .Distinct()
            .OrderByDescending(p => p)
            .FirstOrDefault();
        if (key == null) return null;
        var parts = key.Split('/');
        return (parts[0], parts.Length > 1 ? parts[1] : "");
    }

    /// <summary>Falls back to the most-recent period that has any grade at all.</summary>
    private static (string SchoolYear, string Semester)? MostRecentAnyPeriod(IEnumerable<GradeItem>? grades)
    {
        if (grades == null) return null;
        var key = grades
            .Where(g => !string.IsNullOrEmpty(g.SchoolYear))
            .Select(g => g.SchoolYear + "/" + g.Semester)
            .Distinct()
            .OrderByDescending(p => p)
            .FirstOrDefault();
        if (key == null) return null;
        var parts = key.Split('/');
        return (parts[0], parts.Length > 1 ? parts[1] : "");
    }

    private static readonly (string Code, string Label)[] _semesters =
    {
        ("1", "GANJIL"),
        ("2", "GENAP"),
        ("3", "ANTARA"),
    };

    /// <summary>
    /// Converts the raw iGracias year code (e.g. "2526") to the display format ("2025/2026").
    /// Falls back to the code itself if it can't be parsed.
    /// </summary>
    private static string FormatYearCode(string code)
    {
        if (code.Length == 4
            && int.TryParse(code[..2], out var y1)
            && int.TryParse(code[2..], out var y2))
            return $"20{y1:D2}/20{y2:D2}";
        return code;
    }

    private async Task LoadAcademicYearsAsync()
    {
        try
        {
            var years = await _gradeService.FetchAcademicYearsAsync();
            FeatureFlowLogger.Write("Grade", $"academic-years load: live-count={years.Count}, saved={_savedPeriodKey}");

            _suppressPeriodChange = true;

            if (years.Count == 0)
            {
                // Keep the cached period list if we already built it during startup.
                if (PeriodOptions.Count == 0)
                {
                    var allPeriods = new GradePeriodOption("", "", "All Periods");
                    PeriodOptions.Add(allPeriods);

                    var cached = GradeData ?? await Task.Run(() => _gradeService.GetCachedGrades());
                    if (cached != null && cached.Grades.Count > 0)
                    {
                        var periods = cached.Grades
                            .Where(g => !string.IsNullOrEmpty(g.SchoolYear))
                            .Select(g => (g.SchoolYear, g.Semester))
                            .Distinct()
                            .OrderByDescending(p => p.SchoolYear)
                            .ThenBy(p => p.Semester)
                            .ToList();

                        foreach (var (sy, sem) in periods)
                        {
                            var semLabel = _semesters.FirstOrDefault(s => s.Code == sem).Label ?? sem;
                            PeriodOptions.Add(new GradePeriodOption(sy, sem, $"{sy} - {semLabel} (Cached)"));
                        }

                        var savedPeriod = string.IsNullOrEmpty(_savedPeriodKey)
                            ? null
                            : FindPeriodByKey(_savedPeriodKey);

                        if (savedPeriod != null)
                        {
                            _userHasSelectedPeriod = true;
                            SelectedPeriod = savedPeriod;
                            FeatureFlowLogger.Write("Grade", $"academic-years offline restore: key={BuildPeriodKey(savedPeriod)}, label={savedPeriod.Label}");
                        }
                        else
                        {
                            SelectedPeriod = PeriodOptions.Skip(1).FirstOrDefault() ?? allPeriods;
                            FeatureFlowLogger.Write("Grade", $"academic-years offline fallback: key={BuildPeriodKey(SelectedPeriod)}, label={SelectedPeriod?.Label ?? "(null)"}");
                        }
                    }
                    else
                    {
                        SelectedPeriod = allPeriods;
                        FeatureFlowLogger.Write("Grade", "academic-years offline: no cache, selecting All Periods");
                    }
                }
                else if (!string.IsNullOrEmpty(_savedPeriodKey))
                {
                    var savedPeriod = FindPeriodByKey(_savedPeriodKey);
                    if (savedPeriod != null)
                    {
                        _userHasSelectedPeriod = true;
                        SelectedPeriod = savedPeriod;
                        FeatureFlowLogger.Write("Grade", $"academic-years offline keep-existing: key={BuildPeriodKey(savedPeriod)}, label={savedPeriod.Label}");
                    }
                }
            }
            else
            {
                // Update labels in place when the period set did not change.
                var liveKeys = years
                    .SelectMany(y => _semesters.Select(s => (y.Value, s.Code)))
                    .ToHashSet();
                var existingKeys = PeriodOptions.Skip(1)
                    .Select(p => (p.SchoolYear, p.Semester))
                    .ToHashSet();

                // Filtered snapshots only tell us about one period, so do not infer "current" from them.
                var snap = GradeData ?? _gradeService.GetCachedGrades();
                bool snapIsFiltered = snap != null && !string.IsNullOrEmpty(snap.SchoolYear);
                var (currentYear, currentSem) =
                    MostRecentInProgressPeriod(snap?.Grades)
                    ?? (snapIsFiltered ? null : MostRecentAnyPeriod(snap?.Grades))
                    ?? (null, (DateTime.Now.Month >= 2 && DateTime.Now.Month <= 7) ? "2" : "1");

                var selectedYear = years.FirstOrDefault(y => y.IsSelected)   // grade-page JS never sets this, but guard
                                ?? (!string.IsNullOrEmpty(currentYear)
                                    ? years.FirstOrDefault(y => y.Value == currentYear)
                                    : null)
                                ?? years.MaxBy(y => y.Value);

                GradePeriodOption? currentPeriod = null;

                if (liveKeys.SetEquals(existingKeys))
                {
                    for (int i = 1; i < PeriodOptions.Count; i++)
                    {
                        var p = PeriodOptions[i];
                        var liveYear = years.FirstOrDefault(y => y.Value == p.SchoolYear);
                        if (liveYear == null) continue;

                        var semEntry = _semesters.FirstOrDefault(s => s.Code == p.Semester);
                        bool isCurrent = selectedYear != null
                                      && liveYear.Value == selectedYear.Value
                                      && p.Semester == currentSem;
                        var newLabel = $"{liveYear.Text} - {semEntry.Label ?? p.Semester}"
                                       + (isCurrent ? " (Current)" : "");

                        if (p.Label != newLabel)
                            PeriodOptions[i] = p with { Label = newLabel };

                        if (isCurrent)
                            currentPeriod = PeriodOptions[i];
                    }
                }
                else
                {
                    PeriodOptions.Clear();
                    PeriodOptions.Add(new GradePeriodOption("", "", "All Periods"));

                    foreach (var y in years)
                    {
                        foreach (var (code, label) in _semesters)
                        {
                            bool isCurrent = selectedYear != null
                                          && y.Value == selectedYear.Value
                                          && code == currentSem;
                            var opt = new GradePeriodOption(y.Value, code,
                                $"{y.Text} - {label}" + (isCurrent ? " (Current)" : ""));
                            PeriodOptions.Add(opt);
                            if (isCurrent)
                                currentPeriod = opt;
                        }
                    }
                }

                // Fall back to cached grades if the live year list does not identify a current period.
                if (currentPeriod == null)
                {
                    var fallbackSnap = GradeData ?? await Task.Run(() => _gradeService.GetCachedGrades());
                    bool fallbackIsFiltered = fallbackSnap != null && !string.IsNullOrEmpty(fallbackSnap.SchoolYear);
                    var fallbackGrades = fallbackSnap?.Grades;
                    var (fbYear, fbSem) =
                        MostRecentInProgressPeriod(fallbackGrades)
                        ?? (fallbackIsFiltered ? null : MostRecentAnyPeriod(fallbackGrades))
                        ?? (null, null);
                    if (fbYear != null && fbSem != null)
                    {
                        currentPeriod = PeriodOptions.Skip(1)
                            .FirstOrDefault(p => p.SchoolYear == fbYear && p.Semester == fbSem);

                        if (currentPeriod != null && !currentPeriod.Label.Contains("(Current)"))
                        {
                            var idx = PeriodOptions.IndexOf(currentPeriod);
                            var updated = currentPeriod with { Label = currentPeriod.Label
                                .Replace(" (Cached)", "") + " (Current)" };
                            if (idx >= 0) PeriodOptions[idx] = updated;
                            currentPeriod = updated;
                        }
                    }
                }

                if (!_userHasSelectedPeriod && !string.IsNullOrEmpty(_savedPeriodKey))
                {
                    var saved = FindPeriodByKey(_savedPeriodKey);
                    if (saved != null)
                    {
                        _userHasSelectedPeriod = true;
                        SelectedPeriod = saved;
                        FeatureFlowLogger.Write("Grade", $"academic-years live restore: key={BuildPeriodKey(saved)}, label={saved.Label}");
                    }
                }

                if (currentPeriod != null && !_userHasSelectedPeriod)
                {
                    var previousPeriod = SelectedPeriod;
                    SelectedPeriod = currentPeriod;
                    FeatureFlowLogger.Write("Grade", $"academic-years live auto-select current: key={BuildPeriodKey(currentPeriod)}, label={currentPeriod.Label}");

                    // Re-filter locally if the selected period changed after the initial data load.
                    if (GradeData != null
                        && (previousPeriod?.SchoolYear != currentPeriod.SchoolYear
                            || previousPeriod?.Semester != currentPeriod.Semester))
                    {
                        PopulateData(FilterCacheByPeriod(GradeData));
                    }
                }
                // Rebind SelectedPeriod to the current list entry after any label rewrite.
                if (SelectedPeriod != null)
                {
                    var synced = PeriodOptions.FirstOrDefault(
                        p => p.SchoolYear == SelectedPeriod.SchoolYear && p.Semester == SelectedPeriod.Semester);
                    if (synced != null && synced != SelectedPeriod)
                        SelectedPeriod = synced;
                }
            }
            _suppressPeriodChange = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GradeViewModel] LoadAcademicYearsAsync error: {ex.Message}");
            _suppressPeriodChange = false;
        }
    }

    private void PopulateData(GradeResponse data)
    {
        GradeData = data;

        Grades.Clear();
        foreach (var g in data.Grades)
            Grades.Add(g);

        TotalCourses      = data.Grades.Count;
        CompletedCourses  = data.Grades.Count(g => g.IsCompleted);
        InProgressCourses = data.Grades.Count(g => g.InProgress);
        TotalCredits      = data.Grades.Where(g => g.IsCompleted).Sum(g => g.Credits);

        // Compute IP/IPK: weighted average over completed courses with a known grade point.
        var completedWithGrade = data.Grades.Where(g => g.IsCompleted && g.GradePointValue >= 0).ToList();
        int gpaCredits = completedWithGrade.Sum(g => g.Credits);
        double weightedSum = completedWithGrade.Sum(g => g.Credits * g.GradePointValue);
        CurrentGpa = gpaCredits > 0 ? (weightedSum / gpaCredits).ToString("F2") : "-";

        IsEmpty = Grades.Count == 0;
    }

    private void ClearDisplayed()
    {
        Grades.Clear();
        GradeData        = null;
        TotalCourses     = 0;
        CompletedCourses = 0;
        InProgressCourses = 0;
        TotalCredits     = 0;
        IsEmpty          = false;
    }
}

/// <summary>Combined academic year + semester option for the single period filter ComboBox.</summary>
public record GradePeriodOption(string SchoolYear, string Semester, string Label);
