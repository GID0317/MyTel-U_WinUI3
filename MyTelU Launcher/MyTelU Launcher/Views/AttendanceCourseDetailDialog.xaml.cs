using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using MyTelU_Launcher.Models;

namespace MyTelU_Launcher.Views;

public sealed partial class AttendanceCourseDetailDialog : ContentDialog
{
    public AttendanceCourseDetailDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Populate header info immediately — call before ShowAsync.
    /// </summary>
    public void SetCourse(AttendanceCourseItem course)
    {
        CourseCodeText.Text = course.CourseCode;
        ClassCodeText.Text = course.ClassCode ?? string.Empty;
        CourseNameText.Text = course.CourseName;
    }

    /// <summary>
    /// Show loading spinner — call while awaiting the network fetch.
    /// </summary>
    public void ShowLoading()
    {
        LoadingPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        LoadingRing.IsActive = true;
        CourseExpander.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        EmptyPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public void SetDetail(AttendanceCourseDetail? detail)
    {
        LoadingRing.IsActive = false;
        LoadingPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

        var sessions = detail?.Sessions;
        if (sessions == null || sessions.Count == 0)
        {
            EmptyPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        else
        {
            CourseExpander.ItemsSource = sessions;
            CourseExpander.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
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
