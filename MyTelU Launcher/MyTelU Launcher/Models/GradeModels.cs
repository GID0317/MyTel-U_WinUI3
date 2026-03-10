using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MyTelU_Launcher.Models;

/// <summary>
/// One grade row from iGracias — mirrors the Python GradesFetcher aaData output.
/// aaData column layout: [0]=course_code, [1]=course_name, [2]=credits, [3]=period,
///   [4]=grade, [5]=flag, [6]=active("Y"/"T"), [7]=internal_id
/// </summary>
public class GradeItem
{
    [JsonPropertyName("course_code")]
    public string CourseCode { get; set; } = string.Empty;

    [JsonPropertyName("course_name")]
    public string CourseName { get; set; } = string.Empty;

    [JsonPropertyName("credits")]
    public int Credits { get; set; }

    [JsonPropertyName("period")]
    public string Period { get; set; } = string.Empty;

    [JsonPropertyName("school_year")]
    public string SchoolYear { get; set; } = string.Empty;

    [JsonPropertyName("semester")]
    public string Semester { get; set; } = string.Empty;

    /// <summary>Letter grade e.g. "A", "AB", "B", "BC", "C", "D", "E". Empty if in-progress.</summary>
    [JsonPropertyName("grade")]
    public string Grade { get; set; } = string.Empty;

    /// <summary>False when aaData[6]=="T" (non-active/repeated).</summary>
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    /// <summary>True when grade is empty (not yet released).</summary>
    [JsonPropertyName("in_progress")]
    public bool InProgress { get; set; }

    [JsonPropertyName("internal_id")]
    public string InternalId { get; set; } = string.Empty;

    // ── Display helpers (not persisted) ─────────────────────────────────────

    [JsonIgnore] public string GradeDisplay => InProgress ? "-" : (string.IsNullOrEmpty(Grade) ? "-" : Grade);
    [JsonIgnore] public bool IsCompleted => !InProgress && Active;
    [JsonIgnore] public bool IsRepeated => !Active && !InProgress;
    [JsonIgnore] public double GradePointOpacity => IsCompleted ? 1.0 : 0.35;

    /// <summary>Grade point for display convenience (A=4, AB=3.5, B=3, …).</summary>
    [JsonIgnore]
    public string GradePoint => Grade switch
    {
        "A"  => "4.00",
        "AB" => "3.50",
        "B"  => "3.00",
        "BC" => "2.50",
        "C"  => "2.00",
        "D"  => "1.00",
        "E"  => "0.00",
        _    => "-",
    };

    /// <summary>Grade point as double for GPA calculations.</summary>
    [JsonIgnore]
    public double GradePointValue => Grade switch
    {
        "A"  => 4.00,
        "AB" => 3.50,
        "B"  => 3.00,
        "BC" => 2.50,
        "C"  => 2.00,
        "D"  => 1.00,
        "E"  => 0.00,
        _    => -1.0, // unknown / in-progress: exclude from GPA
    };

    [JsonIgnore]
    public string SemesterLabel => Semester switch
    {
        "1" => "Ganjil",
        "2" => "Genap",
        "3" => "Pendek",
        _   => Semester,
    };
}

/// <summary>
/// Component score for one course from the getcomponentscore endpoint.
/// JSON fields: COMPONENTNAME, TSCORE, PERCENTAGE.
/// </summary>
public class GradeComponentScore
{
    [JsonPropertyName("component")]
    public string Component { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("percentage")]
    public int Percentage { get; set; }

    [JsonIgnore] public double Weighted => Math.Round(Score * Percentage / 100.0, 2);
}

/// <summary>Top-level grade fetch result.</summary>
public class GradeResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("grades")]
    public List<GradeItem> Grades { get; set; } = new();

    [JsonPropertyName("fetch_time")]
    public string FetchTime { get; set; } = string.Empty;

    [JsonPropertyName("school_year")]
    public string SchoolYear { get; set; } = string.Empty;

    [JsonPropertyName("semester")]
    public string Semester { get; set; } = string.Empty;
}
