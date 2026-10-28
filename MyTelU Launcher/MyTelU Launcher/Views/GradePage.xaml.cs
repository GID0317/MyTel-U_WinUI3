using System.Numerics;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.Services;
using MyTelU_Launcher.ViewModels;

namespace MyTelU_Launcher.Views;

public sealed partial class GradePage : Page
{
    public GradeViewModel ViewModel { get; }

    private bool _hasNavigated;

    public GradePage()
    {
        ViewModel = App.GetService<GradeViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.Initialization;
        if (_hasNavigated)
            await ViewModel.LoadGradesAsync();
        _hasNavigated = true;
    }

    private async void GradeCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement card || card.Tag is not GradeItem grade) return;

        var dialog = new GradeComponentDetailDialog(grade);
        dialog.XamlRoot = XamlRoot;

        _ = dialog.LoadComponentsAsync(App.GetService<IGradeService>());

        App.GetService<AccentColorService>()?.ApplyToContentDialog(dialog);
        await dialog.ShowAsync();
    }

    private async void PrintGrades_Click(object sender, RoutedEventArgs e)
    {
        string subtitle = $"{ViewModel.SelectedPeriod?.Label} | {(ViewModel.IsOffline ? "Cached data" : "Loaded data")} | {ViewModel.GradeData?.FetchTime}";
        await App.GetService<AcademicPrintService>().PrintAsync(XamlRoot, "Grades", subtitle,
            ["Code", "Course", "SKS", "Period", "Grade", "Points"], [1.2, 3, 0.5, 1.3, 0.7, 0.7],
            ViewModel.Grades.Select(g => new[] { g.CourseCode, g.CourseName, g.Credits.ToString(), g.Period, g.GradeDisplay, g.GradePoint }));
    }

    private void GradeMascotContainer_Loaded(object sender, RoutedEventArgs e)
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
            gradientBrush.StartPoint = System.Numerics.Vector2.Zero;
            gradientBrush.EndPoint = new System.Numerics.Vector2(0f, 1f);
            gradientBrush.ColorStops.Add(compositor.CreateColorGradientStop(0.0f, Microsoft.UI.Colors.Black));
            gradientBrush.ColorStops.Add(compositor.CreateColorGradientStop(0.6f, Microsoft.UI.Colors.Black));
            gradientBrush.ColorStops.Add(compositor.CreateColorGradientStop(1.0f, Microsoft.UI.Colors.Transparent));

            var maskBrush = compositor.CreateMaskBrush();
            maskBrush.Source = imageBrush;
            maskBrush.Mask = gradientBrush;

            var sprite = compositor.CreateSpriteVisual();
            sprite.Brush = maskBrush;
            sprite.Size = new System.Numerics.Vector2((float)mascot.ActualWidth, (float)mascot.ActualHeight);

            ElementCompositionPreview.SetElementChildVisual(mascot, sprite);
        }

        if (mascot.ActualWidth > 0 && mascot.ActualHeight > 0)
            SetupMascot();
        else
            mascot.SizeChanged += (s, args) => SetupMascot();
    }
}

