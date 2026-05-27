using MyTelU_Launcher.Models;

namespace MyTelU_Launcher.Contracts.Services;

public interface IClassReminderService
{
    Task<ClassReminderSettings> GetSettingsAsync();

    Task SaveSettingsAsync(ClassReminderSettings settings);

    Task RescheduleAsync(IEnumerable<CourseItem> courses);

    void ClearScheduledReminders();
}
