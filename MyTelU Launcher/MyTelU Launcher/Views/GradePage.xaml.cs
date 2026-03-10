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

    private bool _pendingContentAnimation;
    private int _layoutPassCount;

    public GradePage()
    {
        ViewModel = App.GetService<GradeViewModel>();
        InitializeComponent();

        // Scale the login overlay from its center so the DrillIn animation looks correct.
        LoginOverlay.SizeChanged += (s, e) =>
        {
            var visual = ElementCompositionPreview.GetElementVisual(LoginOverlay);
            visual.CenterPoint = new Vector3(
                (float)(LoginOverlay.ActualWidth / 2),
                (float)(LoginOverlay.ActualHeight / 2),
                0f);
        };

        // CTKI implicit animations and ItemsRepeater layout both race to control
        // visual.Opacity when GradeContentGrid becomes Visible. For large datasets
        // ("All Periods") the CTKI animation ends before layout finishes, leaving
        // the visual frozen at opacity 0. Fix: no implicit animation in XAML;
        // instead snap to 0 here, then start a Composition animation only AFTER
        // LayoutUpdated fires (i.e., after ItemsRepeater layout is complete).
        // Pre-set opacity=0 when loading begins so that the very first rendered frame
        // after the content grid becomes Visible again is already transparent.
        // This prevents a one-frame blank between the loading overlay hiding and the
        // entrance animation starting.
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.IsLoading) && ViewModel.IsLoading)
            {
                var v = ElementCompositionPreview.GetElementVisual(GradeContentGrid);
                v.Opacity = 0f;
                v.Offset  = new Vector3(0, 16, 0);
            }
            else if (e.PropertyName == nameof(ViewModel.IsLoading) && !ViewModel.IsLoading)
            {
                _ = SnapContentOpacityAfterAnimationAsync();
            }
        };

        GradeContentGrid.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (s, dp) =>
        {
            var visual = ElementCompositionPreview.GetElementVisual(GradeContentGrid);
            if (GradeContentGrid.Visibility == Visibility.Visible)
            {
                // Always play the entrance animation.
                // opacity/offset were pre-set to 0/16 by the PropertyChanged handler above
                // so the first rendered frame is already transparent — no blank flash.
                _pendingContentAnimation = true;
                _layoutPassCount = 0;

                if (ViewModel.Grades.Count > 0)
                {
                    // Layout is already known (refreshing existing data) — start immediately
                    // without waiting for LayoutUpdated, to minimise blank frames.
                    _pendingContentAnimation = false;
                    StartContentEntranceAnimation();
                }
                else
                {
                    // First load: ItemsRepeater needs to measure before we animate.
                    GradeContentGrid.LayoutUpdated += OnGradeContentGridLayoutUpdated;
                }
            }
            else
            {
                _pendingContentAnimation = false;
                GradeContentGrid.LayoutUpdated -= OnGradeContentGridLayoutUpdated;
            }
        });
    }

    private void OnGradeContentGridLayoutUpdated(object? sender, object e)
    {
        if (!_pendingContentAnimation || GradeContentGrid.Visibility != Visibility.Visible)
        {
            GradeContentGrid.LayoutUpdated -= OnGradeContentGridLayoutUpdated;
            _layoutPassCount = 0;
            return;
        }

        // Wait for 2 layout passes to ensure ItemsRepeater has finished measuring.
        if (++_layoutPassCount < 2) return;

        GradeContentGrid.LayoutUpdated -= OnGradeContentGridLayoutUpdated;
        _layoutPassCount = 0;
        _pendingContentAnimation = false;

        StartContentEntranceAnimation();
    }

    private void StartContentEntranceAnimation()
    {
        var visual = ElementCompositionPreview.GetElementVisual(GradeContentGrid);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new(0.1f, 0.9f), new(0.2f, 1f));

        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
        opacityAnim.InsertKeyFrame(0f, 0f);
        opacityAnim.InsertKeyFrame(1f, 1f, ease);
        opacityAnim.Duration = TimeSpan.FromMilliseconds(250);

        var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
        offsetAnim.InsertKeyFrame(0f, new Vector3(0, 16, 0));
        offsetAnim.InsertKeyFrame(1f, Vector3.Zero, ease);
        offsetAnim.Duration = TimeSpan.FromMilliseconds(350);

        // Set base values to final state BEFORE starting animations.
        // Composition animations override the base value during playback; when they
        // finish the base value takes effect, so this guarantees opacity stays at 1.
        visual.Opacity = 1f;
        visual.Offset = Vector3.Zero;

        visual.StartAnimation("Opacity", opacityAnim);
        visual.StartAnimation("Offset", offsetAnim);
        _ = SnapContentOpacityAfterAnimationAsync();
    }

    private async Task SnapContentOpacityAfterAnimationAsync()
    {
        await Task.Delay(450);

        if (GradeContentGrid.Visibility == Visibility.Visible)
        {
            var visual = ElementCompositionPreview.GetElementVisual(GradeContentGrid);
            visual.Opacity = 1f;
            visual.Offset = Vector3.Zero;
        }
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (ViewModel.IsLoading && ViewModel.Grades.Count > 0)
            ViewModel.CancelLoad();

        // If data is already ready (navigating back to cached page), ensure the
        // visual is at its final state in case any previous animation left it partial.
        if (ViewModel.IsNotLoadingAndNotEmpty)
        {
            var visual = ElementCompositionPreview.GetElementVisual(GradeContentGrid);
            visual.Opacity = 1f;
            visual.Offset = Vector3.Zero;
        }

        if (!ViewModel.IsInitializing && !ViewModel.IsLoading && ViewModel.Grades.Count == 0 && !ViewModel.NeedsLogin)
            _ = ViewModel.LoadGradesAsync();
    }

    private async void GradeCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not SettingsCard card || card.Tag is not GradeItem grade) return;

        var dialog = new GradeComponentDetailDialog(grade);
        dialog.XamlRoot = XamlRoot;

        _ = dialog.LoadComponentsAsync(App.GetService<IGradeService>());

        App.GetService<AccentColorService>()?.ApplyToContentDialog(dialog);
        await dialog.ShowAsync();
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

