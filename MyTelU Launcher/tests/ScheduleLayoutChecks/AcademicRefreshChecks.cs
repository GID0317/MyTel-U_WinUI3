using System.Collections.Specialized;
using MyTelU_Launcher.Contracts.Services;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.Services;
using MyTelU_Launcher.ViewModels;

internal static class AcademicRefreshChecks
{
    private static int _checks;

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _checks++;
        Console.WriteLine($"PASS {description}");
    }

    public static async Task RunAsync()
    {
        var schedule = new ScheduleFake { Authenticated = false };
        var login = new LoginFake(() => schedule.Authenticated = true);
        var vm = new ScheduleViewModel(schedule, login, new ReminderFake());
        await vm.Initialization;
        Check(login.Count == 1 && schedule.MetadataCalls == 2,
            "Schedule recovers an expired page session even when its data endpoint still works");
        Check(!vm.IsOffline && !vm.HasError && vm.SelectedAcademicYear?.Value == "2627/1",
            "Schedule finishes with live period labels and live data after one sign-in");
        Check(schedule.DataCalls == 1, "Schedule initialization performs only one data fetch");

        // Model ComboBox selection changes raised while its ItemsSource is rebuilt.
        vm.AcademicYearOptions.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) vm.SelectedAcademicYear = null;
            if (e.Action == NotifyCollectionChangedAction.Add)
                vm.SelectedAcademicYear = (AcademicYearOption)e.NewItems![0]!;
        };
        schedule.MetadataPause = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshing = vm.RefreshScheduleCommand.ExecuteAsync(null);
        Check(!refreshing.IsCompleted && vm.IsLoading && schedule.DataCalls == 1,
            "Schedule refresh waits for period resolution before fetching data");
        schedule.MetadataPause.SetResult();
        await refreshing;
        Check(schedule.DataCalls == 2 && schedule.RequestedPeriod == "2627/1",
            "Rebuilding the schedule dropdown does not launch or cancel extra fetches");
        Check(!vm.IsLoading && !vm.IsOffline && login.Count == 1,
            "Schedule refresh completes without background metadata work or unnecessary sign-in");

        schedule.MetadataPause = null;
        schedule.NetworkAvailable = false;
        await vm.RefreshScheduleCommand.ExecuteAsync(null);
        Check(vm.IsOffline && !vm.HasError && login.Count == 1,
            "Offline schedule refresh retains the matching cache without attempting sign-in");

        var attendance = new AttendanceFake { Authenticated = false };
        var attendanceLogin = new LoginFake(() => attendance.Authenticated = true);
        var attendanceVm = new AttendanceViewModel(attendance, attendanceLogin, null!);
        await attendanceVm.Initialization;
        Check(attendanceLogin.Count == 1 && attendance.MetadataCalls == 2 && !attendanceVm.IsOffline,
            "Attendance recovers the session before resolving its period and fetching data");
        attendanceVm.AcademicYearOptions.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) attendanceVm.SelectedAcademicYear = null;
            if (e.Action == NotifyCollectionChangedAction.Add)
                attendanceVm.SelectedAcademicYear = (AcademicYearOption)e.NewItems![0]!;
        };
        attendance.MetadataPause = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var attendanceRefresh = attendanceVm.RefreshAttendanceCommand.ExecuteAsync(null);
        Check(!attendanceRefresh.IsCompleted && attendanceVm.IsLoading && attendance.DataCalls == 1,
            "Attendance refresh waits for metadata before requesting data");
        attendance.MetadataPause.SetResult();
        await attendanceRefresh;
        Check(attendance.DataCalls == 2 && attendance.RequestedPeriod == "2627/1"
            && !attendanceVm.IsLoading && !attendanceVm.HasError,
            "Attendance dropdown rebuild preserves the selected period and fetches once");
        Console.WriteLine($"Verified {_checks} academic refresh checks without reading or changing user data.");
    }

    private static List<AcademicYearOption> Periods() =>
    [
        new() { Value = "2526/2", Text = "Previous", YearCode = "2526", SemesterCode = "2" },
        new() { Value = "2627/1", Text = "Current", YearCode = "2627", SemesterCode = "1", IsSelected = true }
    ];

    private sealed class LoginFake(Action signIn) : IBrowserLoginService
    {
        public int Count { get; private set; }
        public bool IsRunning => false;
        public bool HasSavedCredentials => true;
        public Task<bool> StartLoginAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TrySilentLoginAsync(CancellationToken cancellationToken = default)
        {
            Count++;
            signIn();
            return Task.FromResult(true);
        }
        public void ClearCredentials() => throw new NotSupportedException();
    }

    private sealed class ReminderFake : IClassReminderService
    {
        public Task<ClassReminderSettings> GetSettingsAsync() => Task.FromResult(new ClassReminderSettings());
        public Task SaveSettingsAsync(ClassReminderSettings settings) => Task.CompletedTask;
        public Task RescheduleAsync(IEnumerable<CourseItem> courses) => Task.CompletedTask;
        public void ClearScheduledReminders() { }
    }

    private sealed class ScheduleFake : IScheduleService
    {
        public bool Authenticated { get; set; } = true;
        public bool NetworkAvailable { get; set; } = true;
        public TaskCompletionSource? MetadataPause { get; set; }
        public int MetadataCalls { get; private set; }
        public int DataCalls { get; private set; }
        public string RequestedPeriod { get; private set; } = "";
        private string _year = "2627", _semester = "1";
        public bool IsServerRunning => true;
        public bool HasSavedSession => true;
        public bool HasCachedSchedule => true;
        public bool IsNetworkAvailable() => NetworkAvailable;
        public Task StartServerAsync() => Task.CompletedTask;
        public Task StopServerAsync() => Task.CompletedTask;
        public void ClearSession() => throw new NotSupportedException();
        public (string?, string?) GetSavedAcademicYear() => (_year, _semester);
        public void SaveAcademicYear(string yearCode, string semesterCode) => (_year, _semester) = (yearCode, semesterCode);
        public Task<SessionValidationResult> ValidateSessionAsync() => Task.FromResult(SessionValidationResult.Valid);
        public async Task<List<AcademicYearOption>> FetchAcademicYearsAsync()
        {
            MetadataCalls++;
            if (MetadataPause != null) await MetadataPause.Task;
            return Authenticated && NetworkAvailable ? Periods() : [];
        }
        public ScheduleResponse? GetCachedSchedule() => new()
        {
            StudentId = "test", AcademicYear = $"{_year}/{_semester}", FetchTime = "cached", Courses = []
        };
        public Task<ScheduleResponse?> GetLiveScheduleAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            DataCalls++;
            RequestedPeriod = $"{_year}/{_semester}";
            var result = GetCachedSchedule()!;
            result.FetchTime = "live";
            return Task.FromResult<ScheduleResponse?>(result);
        }
        public Task<ScheduleResponse?> GetScheduleAsync() => GetLiveScheduleAsync(default);
        public Task<ScheduleResponse?> GetScheduleAsync(CancellationToken ct) => GetLiveScheduleAsync(ct);
    }

    private sealed class AttendanceFake : IAttendanceService
    {
        public bool Authenticated { get; set; } = true;
        public TaskCompletionSource? MetadataPause { get; set; }
        public int MetadataCalls { get; private set; }
        public int DataCalls { get; private set; }
        public string? RequestedPeriod { get; private set; }
        public bool HasCachedAttendance => true;
        public async Task<List<AcademicYearOption>> FetchAvailableSemestersAsync(CancellationToken ct = default)
        {
            MetadataCalls++;
            if (MetadataPause != null) await MetadataPause.Task.WaitAsync(ct);
            return Authenticated ? Periods() : [];
        }
        public Task<AttendanceResponse?> GetAttendanceAsync(string? schoolYear = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            DataCalls++;
            RequestedPeriod = schoolYear;
            return Task.FromResult<AttendanceResponse?>(new() { AcademicYear = schoolYear ?? "", Courses = [], Summary = new() });
        }
        public AttendanceResponse? GetCachedAttendance() => null;
        public Task<AttendanceCourseDetail?> GetCourseDetailAsync(int courseId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
