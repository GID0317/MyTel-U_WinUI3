using System.Text.Json.Serialization;

namespace MyTelU_Launcher.Models;

[WinRT.GeneratedBindableCustomProperty]
public partial class StudyPlanCourse
{
    public string Course { get; set; } = string.Empty;
    public int Credits { get; set; }
    public string Class { get; set; } = string.Empty;
    public string Schedule { get; set; } = string.Empty;
}

public sealed class StudyPlan
{
    public string FullName { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public string StudyProgram { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string FetchTime { get; set; } = string.Empty;
    public List<StudyPlanCourse> Courses { get; set; } = [];
    [JsonIgnore] public bool IsCached { get; set; }
}
