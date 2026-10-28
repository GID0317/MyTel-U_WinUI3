using System.Numerics;
using CommunityToolkit.WinUI.Animations;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.Services;
using MyTelU_Launcher.ViewModels;

namespace MyTelU_Launcher.Views;

public sealed partial class AttendancePage : Page
{
    public AttendanceViewModel ViewModel { get; }

    private bool _hasNavigated;

    public AttendancePage()
    {
        ViewModel = App.GetService<AttendanceViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.Initialization;
        if (_hasNavigated)
            await ViewModel.LoadAttendanceAsync();
        _hasNavigated = true;
    }

    /// <summary>
    /// Handles SettingsCard click — shows course detail in a ContentDialog.
    /// </summary>
    private async void CourseCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement card || card.Tag is not AttendanceCourseItem course)
        {
            System.Diagnostics.Debug.WriteLine($"[AttendancePage] CourseCard_Click: tag={((sender as SettingsCard)?.Tag?.GetType().Name ?? "null")}");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[AttendancePage] CourseCard_Click: {course.CourseCode} id={course.CourseId}");

        var dialog = new AttendanceCourseDetailDialog { XamlRoot = XamlRoot };
        App.GetService<AccentColorService>()?.ApplyToContentDialog(dialog);
        dialog.SetCourse(course);
        dialog.ShowLoading();

        var showTask = dialog.ShowAsync();

        var detail = await ViewModel.LoadCourseDetailAsync(course);
        dialog.SetDetail(detail);

        await showTask;
    }

    private async void PrintAttendance_Click(object sender, RoutedEventArgs e)
    {
        string subtitle = $"{ViewModel.AttendanceData?.AcademicYear} | {(ViewModel.IsOffline ? "Cached data" : "Loaded data")} | {ViewModel.AttendanceData?.FetchTime}";
        await App.GetService<AcademicPrintService>().PrintAsync(XamlRoot, "Attendance", subtitle,
            ["Code", "Course", "Class", "Attended", "Sessions", "Presence"], [1.2, 3, 1, 0.8, 0.8, 0.8],
            ViewModel.Courses.Select(c => new[] { c.CourseCode, c.CourseName, c.ClassCode ?? "", c.Attended.ToString(), c.TotalSessions?.ToString() ?? "-", c.PercentageStr }));
    }

    private void MascotContainer_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement mascot)
        {
            void SetupMascot()
            {
                if (mascot.ActualWidth <= 0 || mascot.ActualHeight <= 0) return;

                var visual = ElementCompositionPreview.GetElementVisual(mascot);
                var compositor = visual.Compositor;

                var surface = Microsoft.UI.Xaml.Media.LoadedImageSurface.StartLoadFromUri(new Uri("ms-appx:///Assets/Galih_EmptyScedule_Icon.png"));
                var imageBrush = compositor.CreateSurfaceBrush(surface);
                imageBrush.Stretch = CompositionStretch.Uniform;

                var gradientBrush = compositor.CreateLinearGradientBrush();
                gradientBrush.StartPoint = Vector2.Zero;
                gradientBrush.EndPoint = new Vector2(0f, 1f);
                gradientBrush.ColorStops.Add(compositor.CreateColorGradientStop(0.0f, Microsoft.UI.Colors.Black));
                gradientBrush.ColorStops.Add(compositor.CreateColorGradientStop(0.6f, Microsoft.UI.Colors.Black));
                gradientBrush.ColorStops.Add(compositor.CreateColorGradientStop(1.0f, Microsoft.UI.Colors.Transparent));

                var maskBrush = compositor.CreateMaskBrush();
                maskBrush.Source = imageBrush;
                maskBrush.Mask = gradientBrush;

                var sprite = compositor.CreateSpriteVisual();
                sprite.Brush = maskBrush;
                sprite.Size = new Vector2((float)mascot.ActualWidth, (float)mascot.ActualHeight);

                ElementCompositionPreview.SetElementChildVisual(mascot, sprite);
            }

            if (mascot.ActualWidth > 0 && mascot.ActualHeight > 0)
                SetupMascot();
            else
                mascot.SizeChanged += (s, args) => SetupMascot();
        }
    }
}
