using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyTelU_Launcher.Models;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ScheduleResponse))]
[JsonSerializable(typeof(StudentProfile))]
[JsonSerializable(typeof(TakSummary))]
[JsonSerializable(typeof(StudyPlan))]
[JsonSerializable(typeof(AttendanceResponse))]
[JsonSerializable(typeof(AttendanceCourseDetail))]
[JsonSerializable(typeof(GradeResponse))]
[JsonSerializable(typeof(Dictionary<string, List<GradeComponentScore>>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(IDictionary<string, string>))]
[JsonSerializable(typeof(List<ToolItem>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(ClassReminderSettings))]
[JsonSerializable(typeof(List<CourseItem>))]
[JsonSerializable(typeof(Dictionary<string, List<CourseItem>>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(double))]
internal partial class AppJsonContext : JsonSerializerContext;
