using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MyTelU_Launcher.Models
{
    public class CourseItem
    {
        [JsonPropertyName("day")]
        public string Day { get; set; }

        [JsonPropertyName("time")]
        public string Time { get; set; }

        [JsonPropertyName("time_start")]
        public string TimeStart { get; set; }

        [JsonPropertyName("time_end")]
        public string TimeEnd { get; set; }

        [JsonPropertyName("room")]
        public string Room { get; set; }

        [JsonPropertyName("course_code")]
        public string CourseCode { get; set; }

        [JsonPropertyName("course_name")]
        public string CourseName { get; set; }

        [JsonPropertyName("lecturer")]
        public string Lecturer { get; set; }

        [JsonPropertyName("class")]
        public string Class { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("has_conflict")]
        public bool HasConflict { get; set; }

        [JsonPropertyName("raw_text")]
        public string RawText { get; set; }

        // ── Computed at display-time by the ViewModel (not persisted) ────────
        [JsonIgnore] public string BadgeLabel { get; set; } = string.Empty;
        [JsonIgnore] public bool IsOngoing  => BadgeLabel == "ONGOING";
        [JsonIgnore] public bool IsUpcoming => BadgeLabel == "UPCOMING";
        [JsonIgnore] public bool IsFinished => BadgeLabel == "FINISHED";
    }

    public class ScheduleResponse
    {
        [JsonPropertyName("student_id")]
        public string StudentId { get; set; }

        [JsonPropertyName("fetch_time")]
        public string FetchTime { get; set; }

        [JsonPropertyName("academic_year")]
        public string AcademicYear { get; set; }

        [JsonPropertyName("courses")]
        public List<CourseItem> Courses { get; set; }

        [JsonPropertyName("timetable")]
        public Dictionary<string, object> Timetable { get; set; }
    }

    public class SessionStatus
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }
    }

    public class AcademicYearOption
    {
        public string Value { get; set; }
        public string Text { get; set; }
        public string YearCode { get; set; }
        public string SemesterCode { get; set; }
        public bool IsSelected { get; set; }
    }

    /// <summary>Used by the timetable (column-per-day) layout.</summary>
    public class DayGroup
    {
        public string Day { get; set; } = string.Empty;
        public List<CourseItem> Courses { get; set; } = new();
        public bool HasNoCourses => Courses.Count == 0;
    }

    /// <summary>One cell in the timetable grid — may be empty.</summary>
    public class TimetableCell
    {
        public CourseItem? Course { get; set; }
        public bool HasCourse => Course != null;
    }

    /// <summary>One row in the timetable grid = one day across all time-slot columns.</summary>
    public class TimetableDayRow
    {
        public string Day { get; set; } = string.Empty;
        public List<TimetableCell> Cells { get; set; } = new();
    }

    /// <summary>One Gantt-style row — a single course indented to its start time.</summary>
    public class TimetableCourseRow
    {
        public CourseItem Course    { get; set; } = new();
        /// <summary>Pixels to push the card right from the timeline origin.</summary>
        public double LeftOffset   { get; set; }
        /// <summary>Width of the card in pixels (proportional to course duration).</summary>
        public double CardWidth    { get; set; }
    }

    /// <summary>One item in the day-picker Segmented control.</summary>
    public class DaySegmentItem
    {
        public string Day       { get; set; } = string.Empty;
        public bool   HasCourses { get; set; }
    }
}

namespace MyTelU_Launcher.Services
{
    /// <summary>Result of a session validation check against iGracias.</summary>
    public enum SessionValidationResult
    {
        /// <summary>Session is active and valid.</summary>
        Valid,
        /// <summary>No session cookies, or the session has expired / been invalidated.</summary>
        NoSession,
        /// <summary>A network error occurred — could not reach iGracias at all.</summary>
        NetworkError,
    }
}
