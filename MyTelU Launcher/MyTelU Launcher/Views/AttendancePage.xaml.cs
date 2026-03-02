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

    public AttendancePage()
    {
        ViewModel = App.GetService<AttendanceViewModel>();
        InitializeComponent();

        // Scale login overlay from center
        LoginOverlay.SizeChanged += (s, e) =>
        {
            var visual = ElementCompositionPreview.GetElementVisual(LoginOverlay);
            visual.CenterPoint = new Vector3(
                (float)(LoginOverlay.ActualWidth / 2),
                (float)(LoginOverlay.ActualHeight / 2),
                0f);
        };

        // Safety net: CommunityToolkit implicit show animations (ShowList) use a
        // Composition KeyFrameAnimation that starts at opacity 0. On the very first
        // Collapsed→Visible transition of a newly created element the animation can
        // complete without committing its final value, leaving the visual transparent.
        // After the animation duration (+buffer), snap the visual to fully opaque.
        CourseContentGrid.RegisterPropertyChangedCallback(
            UIElement.VisibilityProperty,
            (sender, dp) =>
            {
                if (CourseContentGrid.Visibility == Visibility.Visible)
                    _ = SnapContentOpacityAfterAnimationAsync();
            });
    }

    /// <summary>
    /// Waits for the ShowList animation to finish (350 ms + margin), then
    /// force-sets the Composition visual's opacity to 1 and offset to zero.
    /// When the animation played correctly this is a harmless no-op.
    /// </summary>
    private async Task SnapContentOpacityAfterAnimationAsync()
    {
        await Task.Delay(400);
        if (CourseContentGrid.Visibility == Visibility.Visible)
        {
            var visual = ElementCompositionPreview.GetElementVisual(CourseContentGrid);
            visual.Opacity = 1f;
            visual.Offset  = Vector3.Zero;
        }
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // If a load is in progress but we already have data, cancel it so the page
        // is immediately usable with cached data instead of being blocked by the overlay.
        if (ViewModel.IsLoading && ViewModel.Courses.Count > 0)
        {
            ViewModel.CancelLoad();
            // Fall through to the composition reset below.
        }

        // When SessionCookiesSavedMessage fires while on another page, InitializeAsync runs
        // off-screen. It toggles IsLoading true→false which collapses then re-shows the
        // content grid, firing both the HideList and ShowList implicit animations on an
        // off-screen Composition tree. If the user navigates here before the 0.25 s
        // ShowList animation has finished, the grid is caught at a partial opacity and
        // stays frozen there (the animation already ended). Snap the composition visual
        // to its final fully-visible state to clear any leftover animated values.
        if (ViewModel.IsNotLoadingAndNotEmpty)
        {
            var visual = ElementCompositionPreview.GetElementVisual(CourseContentGrid);
            visual.Opacity = 1f;
            visual.Offset  = Vector3.Zero;
        }

        // Re-trigger a load if we have a session but no data yet and nothing is running.
        // This handles the case where the VM was created after SessionCookiesSavedMessage
        // already fired (e.g., user logged in from the Schedule page before ever visiting here).
        if (!ViewModel.IsLoading && ViewModel.Courses.Count == 0 && !ViewModel.NeedsLogin)
            _ = ViewModel.LoadAttendanceAsync();
    }

    /// <summary>
    /// Handles SettingsCard click — shows course detail in a ContentDialog.
    /// </summary>
    private async void CourseCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not SettingsCard card || card.Tag is not AttendanceCourseItem course)
        {
            System.Diagnostics.Debug.WriteLine($"[AttendancePage] CourseCard_Click: tag={((sender as SettingsCard)?.Tag?.GetType().Name ?? "null")}");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[AttendancePage] CourseCard_Click: {course.CourseCode} id={course.CourseId}");

        var dialog = new AttendanceCourseDetailDialog { XamlRoot = XamlRoot };
        App.GetService<AccentColorService>()?.ApplyToContentDialog(dialog);
        dialog.SetCourse(course);
        dialog.ShowLoading();

        // Show dialog immediately (user sees spinner while data loads)
        var showTask = dialog.ShowAsync();

        // Fetch in parallel
        var detail = await ViewModel.LoadCourseDetailAsync(course);
        dialog.SetDetail(detail);

        await showTask;
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
