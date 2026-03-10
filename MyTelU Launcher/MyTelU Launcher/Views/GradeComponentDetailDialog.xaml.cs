using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.Services;

namespace MyTelU_Launcher.Views;

public sealed partial class GradeComponentDetailDialog : ContentDialog
{
    private readonly GradeItem _grade;

    public GradeComponentDetailDialog(GradeItem grade)
    {
        _grade = grade;
        InitializeComponent();
        CourseCodeText.Text = grade.CourseCode;
        CourseMetaText.Text = grade.Period;
        CourseNameText.Text = grade.CourseName;
    }

    private void DialogMascotContainer_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement mascot) return;

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
            gradientBrush.ColorStops.Add(compositor.CreateColorGradientStop(0.0f, Colors.Black));
            gradientBrush.ColorStops.Add(compositor.CreateColorGradientStop(0.6f, Colors.Black));
            gradientBrush.ColorStops.Add(compositor.CreateColorGradientStop(1.0f, Colors.Transparent));

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

    /// <summary>Fetches component scores asynchronously and populates the dialog.</summary>
    public async Task LoadComponentsAsync(IGradeService gradeService)
    {
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingRing.IsActive = true;
        NoDataPanel.Visibility = Visibility.Collapsed;
        CourseExpander.Visibility = Visibility.Collapsed;

        if (_grade.InProgress)
        {
            LoadingRing.IsActive = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
            NoDataPanel.Visibility  = Visibility.Visible;
            NoDataMessage.Text      = "Scores not yet released for this course!";
            return;
        }

        try
        {
            var components = await gradeService.GetCourseDetailAsync(_grade.InternalId);

            LoadingRing.IsActive = false;
            LoadingPanel.Visibility = Visibility.Collapsed;

            if (components.Count == 0)
            {
                NoDataPanel.Visibility = Visibility.Visible;
                NoDataMessage.Text     = "No component score data available for this course!";
                return;
            }

            ComponentList.ItemsSource = components;
            WeightedTotalText.Text = components.Sum(c => c.Weighted).ToString("F2");
            CourseExpander.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            LoadingRing.IsActive = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
            NoDataPanel.Visibility  = Visibility.Visible;
            NoDataMessage.Text      = $"Could not load component scores: {ex.Message}";
        }
    }
}
