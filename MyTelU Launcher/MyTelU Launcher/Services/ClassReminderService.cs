using System.Security;
using System.Text;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

using MyTelU_Launcher.Contracts.Services;
using MyTelU_Launcher.Helpers;
using MyTelU_Launcher.Models;

namespace MyTelU_Launcher.Services;

public class ClassReminderService : IClassReminderService
{
    private const string SettingsKey = "ClassReminderSettings";
    private const string ReminderGroup = "class-reminders";
    private const int DefaultMinutesBefore = 30;
    private const int ScheduleAheadDays = 21;
    private const string DefaultSoundEvent = "ms-winsoundevent:Notification.Reminder";

    private static readonly HashSet<string> AllowedSoundEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "ms-winsoundevent:Notification.Reminder",
        "ms-winsoundevent:Notification.Alarm",
        "ms-winsoundevent:Notification.IM",
        "ms-winsoundevent:Notification.Mail",
        "ms-winsoundevent:Notification.SMS",
        "ms-winsoundevent:Notification.Looping.Alarm",
        "ms-winsoundevent:Notification.Looping.Alarm2",
        "ms-winsoundevent:Notification.Looping.Alarm3",
        "ms-winsoundevent:Notification.Looping.Call",
    };

    private static readonly Dictionary<string, DayOfWeek> DayMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Monday", DayOfWeek.Monday }, { "Senin", DayOfWeek.Monday },
        { "Tuesday", DayOfWeek.Tuesday }, { "Selasa", DayOfWeek.Tuesday },
        { "Wednesday", DayOfWeek.Wednesday }, { "Rabu", DayOfWeek.Wednesday },
        { "Thursday", DayOfWeek.Thursday }, { "Kamis", DayOfWeek.Thursday },
        { "Friday", DayOfWeek.Friday }, { "Jumat", DayOfWeek.Friday },
        { "Saturday", DayOfWeek.Saturday }, { "Sabtu", DayOfWeek.Saturday },
        { "Sunday", DayOfWeek.Sunday }, { "Minggu", DayOfWeek.Sunday },
    };

    private readonly ILocalSettingsService _localSettingsService;

    public ClassReminderService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    public async Task<ClassReminderSettings> GetSettingsAsync()
    {
        var settings = await _localSettingsService.ReadSettingAsync<ClassReminderSettings>(SettingsKey)
            ?? new ClassReminderSettings { IsEnabled = false, MinutesBefore = DefaultMinutesBefore };

        settings.MinutesBefore = Math.Clamp(settings.MinutesBefore, 1, 23 * 60 + 59);
        if (!IsValidSoundEvent(settings.SoundEvent))
            settings.SoundEvent = DefaultSoundEvent;
        return settings;
    }

    public async Task SaveSettingsAsync(ClassReminderSettings settings)
    {
        settings.MinutesBefore = Math.Clamp(settings.MinutesBefore, 1, 23 * 60 + 59);
        if (!IsValidSoundEvent(settings.SoundEvent))
            settings.SoundEvent = DefaultSoundEvent;
        await _localSettingsService.SaveSettingAsync(SettingsKey, settings);
    }

    public async Task RescheduleAsync(IEnumerable<CourseItem> courses)
    {
        var settings = await GetSettingsAsync().ConfigureAwait(false);
        var courseList = courses.ToList();

        await Task.Run(() => RescheduleCore(settings, courseList)).ConfigureAwait(false);
    }

    private void RescheduleCore(ClassReminderSettings settings, List<CourseItem> courseList)
    {
        ClearScheduledReminders();

        if (!settings.IsEnabled)
            return;

        var notifier = ToastNotificationManager.CreateToastNotifier();
        var now = DateTimeOffset.Now;
        var horizon = now.AddDays(ScheduleAheadDays);

        foreach (var course in courseList)
        {
            foreach (var startTime in EnumerateUpcomingClassStarts(course, now, horizon))
            {
                var reminderTime = startTime.AddMinutes(-settings.MinutesBefore);
                if (reminderTime <= now || reminderTime > horizon)
                    continue;

                var notification = new ScheduledToastNotification(
                    BuildToastXml(course, startTime, settings.MinutesBefore, settings.SoundEvent),
                    reminderTime)
                {
                    Group = ReminderGroup,
                    Tag = BuildTag(course, startTime)
                };

                notifier.AddToSchedule(notification);
            }
        }

        FeatureFlowLogger.Write("Schedule", $"class reminders rescheduled: enabled={settings.IsEnabled}, minutes={settings.MinutesBefore}, courses={courseList.Count}");
    }

    public void ClearScheduledReminders()
    {
        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier();
            var scheduled = notifier.GetScheduledToastNotifications()
                .Where(n => string.Equals(n.Group, ReminderGroup, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var notification in scheduled)
                notifier.RemoveFromSchedule(notification);
        }
        catch (Exception ex)
        {
            FeatureFlowLogger.Write("Schedule", $"class reminders clear failed: {ex.Message}");
        }
    }

    private static IEnumerable<DateTimeOffset> EnumerateUpcomingClassStarts(
        CourseItem course,
        DateTimeOffset now,
        DateTimeOffset horizon)
    {
        if (!DayMap.TryGetValue(course.Day ?? string.Empty, out var day))
            yield break;

        if (!TryParseTime(course.TimeStart, out var time))
            yield break;

        var daysUntil = ((int)day - (int)now.DayOfWeek + 7) % 7;
        var firstDate = DateTime.Today.AddDays(daysUntil);

        for (var week = 0; week <= Math.Ceiling(ScheduleAheadDays / 7d); week++)
        {
            var localStart = firstDate.AddDays(week * 7).Add(time);
            var start = new DateTimeOffset(localStart);
            if (start <= now)
                continue;
            if (start > horizon)
                yield break;

            yield return start;
        }
    }

    private static XmlDocument BuildToastXml(CourseItem course, DateTimeOffset startTime, int minutesBefore, string soundEvent)
    {
        var title = $"Kelas dimulai dalam {FormatReminderOffset(minutesBefore)}";
        var courseLine = JoinNonEmpty(" - ", course.CourseCode, course.CourseName);
        var detailLine = JoinNonEmpty(" \u2022 ", course.Time, course.Room, course.Class);
        var launchArgs = $"action=schedule&course={Uri.EscapeDataString(course.CourseCode ?? string.Empty)}";
        var lecturerLine = course.Lecturer?.Trim();
        var textElements = string.Join(Environment.NewLine, new[] { title, courseLine, lecturerLine, detailLine }
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => $"      <text>{SecurityElement.Escape(line)}</text>"));

        var xml = $$"""
        <toast launch="{{SecurityElement.Escape(launchArgs)}}">
          <visual>
            <binding template="ToastGeneric">
        {{textElements}}
            </binding>
          </visual>
          <audio src="{{SecurityElement.Escape(soundEvent)}}"/>
        </toast>
        """;

        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc;
    }

    private static string BuildTag(CourseItem course, DateTimeOffset startTime)
    {
        var raw = $"{course.CourseCode}-{startTime:yyyyMMddHHmm}";
        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw)
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        return builder.ToString();
    }

    private static string FormatReminderOffset(int minutes)
    {
        if (minutes < 60)
            return $"{minutes} menit";

        var hours = minutes / 60;
        var remainingMinutes = minutes % 60;
        if (remainingMinutes == 0)
            return $"{hours} jam";

        return $"{hours} jam {remainingMinutes} menit";
    }

    private static string JoinNonEmpty(string separator, params string?[] values)
        => string.Join(separator, values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()));

    private static bool IsValidSoundEvent(string? soundEvent)
        => !string.IsNullOrWhiteSpace(soundEvent)
           && (AllowedSoundEvents.Contains(soundEvent)
               || soundEvent.StartsWith("ms-winsoundevent:", StringComparison.OrdinalIgnoreCase));

    private static bool TryParseTime(string? value, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return TimeSpan.TryParseExact(value.Trim(), @"hh\:mm", null, out time)
            || TimeSpan.TryParseExact(value.Trim(), @"h\:mm", null, out time);
    }
}
