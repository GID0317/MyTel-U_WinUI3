using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MyTelU_Launcher.Models;

/// <summary>
/// One course row in the attendance summary table.
/// Maps to the Python AttendanceFetcher output.
/// </summary>
public class AttendanceCourseItem
{
    [JsonPropertyName("course_id")]
    public int? CourseId { get; set; }

    [JsonPropertyName("course_code")]
    public string CourseCode { get; set; } = string.Empty;

    [JsonPropertyName("course_name")]
    public string CourseName { get; set; } = string.Empty;

    [JsonPropertyName("class_code")]
    public string? ClassCode { get; set; }

    [JsonPropertyName("lecturer")]
    public string Lecturer { get; set; } = string.Empty;

    [JsonPropertyName("student_name")]
    public string StudentName { get; set; } = string.Empty;

    [JsonPropertyName("attended")]
    public int Attended { get; set; }

    [JsonPropertyName("total_sessions")]
    public int? TotalSessions { get; set; }

    [JsonPropertyName("percentage")]
    public double Percentage { get; set; }

    [JsonPropertyName("percentage_str")]
    public string PercentageStr { get; set; } = "0%";

    // ── Computed at display-time (not persisted) ────────
    [JsonIgnore] public string StatusLabel => Percentage < 75 ? "AT RISK" : Percentage < 80 ? "WARNING" : "OK";
    [JsonIgnore] public bool IsAtRisk => Percentage < 75;
    [JsonIgnore] public bool IsWarning => Percentage >= 75 && Percentage < 80;
    [JsonIgnore] public bool IsOk => Percentage >= 80;
}

/// <summary>
/// Aggregate statistics across all courses.
/// </summary>
public class AttendanceSummary
{
    [JsonPropertyName("total_courses")]
    public int TotalCourses { get; set; }

    [JsonPropertyName("total_attended")]
    public int TotalAttended { get; set; }

    [JsonPropertyName("total_sessions")]
    public int? TotalSessions { get; set; }

    [JsonPropertyName("courses_below_75pct")]
    public int CoursesBelow75Pct { get; set; }

    [JsonPropertyName("courses_below_80pct")]
    public int CoursesBelow80Pct { get; set; }

    [JsonPropertyName("at_risk_courses")]
    public List<string> AtRiskCourses { get; set; } = new();
}

/// <summary>
/// Top-level response for an attendance fetch — mirrors the Python JSON output.
/// </summary>
public class AttendanceResponse
{
    [JsonPropertyName("student_id")]
    public string StudentId { get; set; } = string.Empty;

    [JsonPropertyName("academic_year")]
    public string AcademicYear { get; set; } = string.Empty;

    [JsonPropertyName("fetch_time")]
    public string FetchTime { get; set; } = string.Empty;

    [JsonPropertyName("courses")]
    public List<AttendanceCourseItem> Courses { get; set; } = new();

    [JsonPropertyName("summary")]
    public AttendanceSummary Summary { get; set; } = new();
}

/// <summary>
/// One per-session row from the course detail endpoint.
/// </summary>
public class AttendanceSessionDetail
{
    public int No { get; set; }
    public string Date { get; set; } = string.Empty;
    public string Day { get; set; } = string.Empty;
    public string Lecturer { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public string Rfid { get; set; } = string.Empty;
    public string SessionType { get; set; } = string.Empty;
    public string Attendance { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    // ── Computed ────────
    [JsonIgnore] public bool IsPresent => string.Equals(Attendance, "Hadir", System.StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool IsAbsent => Attendance.Contains("Alpa", System.StringComparison.OrdinalIgnoreCase)
                                       || Attendance.Contains("Alpha", System.StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool IsExcused => string.Equals(Attendance, "Izin", System.StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool HasContent => !string.IsNullOrWhiteSpace(Content);
}

/// <summary>
/// Parsed result of the per-course detail endpoint.
/// </summary>
public class AttendanceCourseDetail
{
    public string CourseCode { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public List<AttendanceSessionDetail> Sessions { get; set; } = new();
}
