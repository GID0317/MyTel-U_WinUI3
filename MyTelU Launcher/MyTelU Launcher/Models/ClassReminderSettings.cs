namespace MyTelU_Launcher.Models;

public class ClassReminderSettings
{
    public bool IsEnabled { get; set; }

    public int MinutesBefore { get; set; } = 30;

    public string SoundEvent { get; set; } = "ms-winsoundevent:Notification.Reminder";
}
