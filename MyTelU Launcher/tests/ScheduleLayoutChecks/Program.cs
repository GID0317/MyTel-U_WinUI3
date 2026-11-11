using System.Runtime.CompilerServices;
using System.Xml.Linq;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.ViewModels;

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
    Console.WriteLine($"PASS {description}");
}

// Skip initialization deliberately: layout commands must work without services,
// UI, login, cached data, or any network access.
var viewModel = (ScheduleViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ScheduleViewModel));
Check(viewModel.IsListLayout, "List remains the default view");
var notifications = new HashSet<string>();
viewModel.PropertyChanged += (_, e) => notifications.Add(e.PropertyName!);
var layouts = new[]
{
    (ScheduleLayout.List, viewModel.SetListLayoutCommand, "\uE8FD"),
    (ScheduleLayout.Table, viewModel.SetTableLayoutCommand, "\uE8A1"),
    (ScheduleLayout.Week, viewModel.SetWeekLayoutCommand, "\uE8F5"),
    (ScheduleLayout.Timeline, viewModel.SetTimetableLayoutCommand, "\uECA5")
};
string[] expectedNotifications = ["SelectedLayout", "IsListLayout", "IsTableLayout", "IsWeekLayout", "IsTimetableLayout", "CurrentLayoutIcon"];
foreach (var from in layouts)
{
    foreach (var to in layouts)
    {
        from.Item2.Execute(null);
        notifications.Clear();
        to.Item2.Execute(null);
        var active = new[] { viewModel.IsListLayout, viewModel.IsTableLayout, viewModel.IsWeekLayout, viewModel.IsTimetableLayout };
        Check(viewModel.SelectedLayout == to.Item1 && active.Count(value => value) == 1,
            $"{from.Item1} to {to.Item1}: exactly one active view");
        Check(viewModel.CurrentLayoutIcon == to.Item3, $"{from.Item1} to {to.Item1}: correct toolbar icon");
        Check(from.Item1 == to.Item1 ? notifications.Count == 0 : notifications.SetEquals(expectedNotifications),
            $"{from.Item1} to {to.Item1}: complete layout notifications only");
    }
}

var xaml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "SchedulePage.xaml"));
XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
var options = xaml.Descendants().Where(e => (string?)e.Attribute("GroupName") == "LayoutGroup").ToList();
Check(options.Select(e => (string)e.Attribute("Text")!).SequenceEqual(new[] { "List", "Table", "Week", "Timeline" }),
    "View menu retains all existing modes and adds Week beside Table");
foreach (var option in options)
{
    string label = (string)option.Attribute("Text")!;
    string name = label == "Timeline" ? "Timetable" : label;
    Check(option.Name.LocalName == "RadioMenuFlyoutItem"
        && (string?)option.Attribute("IsChecked") == $"{{x:Bind ViewModel.Is{name}Layout, Mode=OneWay}}"
        && (string?)option.Attribute("Command") == $"{{x:Bind ViewModel.Set{name}LayoutCommand}}",
        $"{label}: native radio option bound to view state and command");
}
var week = xaml.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "WeekView");
Check((string?)week.Attribute(x + "Load") == "{x:Bind ViewModel.IsWeekLayout, Mode=OneWay}",
    "Week cards are deferred while the layout is inactive");
Check(week.Descendants().Any(e => (string?)e.Attribute("ItemsSource") == "{x:Bind ViewModel.CoursesByDay, Mode=OneWay}"),
    "Week reuses existing grouped schedule data");
Check((string?)week.Attribute("HorizontalScrollBarVisibility") == "Auto"
    && (string?)week.Attribute("VerticalScrollBarVisibility") == "Auto",
    "Week supports both horizontal and vertical overflow");
Check((string?)week.Attribute("VerticalScrollMode") == "Auto",
    "Week keeps automatic vertical overflow support");
Check((string?)week.Attribute("Loaded") == "WeekView_Loaded",
    "Week attaches mouse-wheel routing when its deferred viewer loads");
var courseCard = week.Descendants().Single(e => e.Name.LocalName == "Border" && (string?)e.Attribute("Padding") == "8");
Check((string?)courseCard.Attribute("Margin") == "0,0,0,4" && courseCard.Attribute("MinHeight") == null,
    "Week cards use compact padding and gaps without forced blank height");
Check(week.Descendants().Any(e => e.Name.LocalName == "StackPanel"
    && (string?)e.Attribute("Orientation") == "Horizontal" && (string?)e.Attribute("Spacing") == "4"),
    "Week columns use a compact four-pixel gap");
foreach (string field in new[] { "CourseCode", "CourseName", "Time", "Room", "Class", "Lecturer", "HasNoCourses", "IsOngoing", "IsUpcoming", "IsFinished", "HasConflict" })
    Check(week.Descendants().Attributes().Any(a => a.Value.StartsWith($"{{x:Bind {field}}}", StringComparison.Ordinal)
        || a.Value.StartsWith($"{{x:Bind {field},", StringComparison.Ordinal)), $"Week displays {field}");
Console.WriteLine($"Verified {checks} schedule layout checks.");
await AcademicRefreshChecks.RunAsync();
