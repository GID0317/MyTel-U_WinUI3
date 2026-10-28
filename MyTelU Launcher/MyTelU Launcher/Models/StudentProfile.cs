using System.Text.Json.Serialization;

namespace MyTelU_Launcher.Models;

public sealed class StudentProfile
{
    public string StudentId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string StudyProgram { get; set; } = string.Empty;
    public string Faculty { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string SchoolYear { get; set; } = string.Empty;
    public string StudentType { get; set; } = string.Empty;
    public string Photo { get; set; } = string.Empty;
    public string FetchTime { get; set; } = string.Empty;
    [JsonIgnore] public bool IsCached { get; set; }
}

public sealed class TakSummary
{
    public int Score { get; set; }
    public string Ikk { get; set; } = string.Empty;
    public string FetchTime { get; set; } = string.Empty;
    [JsonIgnore] public bool IsCached { get; set; }
}
