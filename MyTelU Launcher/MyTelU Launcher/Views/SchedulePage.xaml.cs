using System;
using System.ComponentModel;
using System.Numerics;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.Animations;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using MyTelU_Launcher.Helpers;
using MyTelU_Launcher.ViewModels;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.Services;

namespace MyTelU_Launcher.Views
{
    /// <summary>Disables SegmentedItem containers for days that have no courses.</summary>
    public class DaySegmentStyleSelector : StyleSelector
    {
        public Style? DisabledStyle { get; set; }

        protected override Style SelectStyleCore(object item, DependencyObject container)
        {
            if (item is DaySegmentItem d && !d.HasCourses && DisabledStyle != null)
                return DisabledStyle;
            return base.SelectStyleCore(item, container);
        }
    }
    public sealed partial class SchedulePage : Page
    {
        public ScheduleViewModel ViewModel
        {
            get;
        }

        public SchedulePage()
        {
            ViewModel = App.GetService<ScheduleViewModel>();
            this.InitializeComponent();

            // Set default directional animations on the timetable content panel
            Implicit.SetShowAnimations(TimetableContent, SlideShowFromRight);
            Implicit.SetHideAnimations(TimetableContent, SlideHideToLeft);

            // Scale login overlay from center using Composition CenterPoint
            LoginOverlay.SizeChanged += (s, e) =>
            {
                var visual = ElementCompositionPreview.GetElementVisual(LoginOverlay);
                visual.CenterPoint = new Vector3(
                    (float)(LoginOverlay.ActualWidth  / 2),
                    (float)(LoginOverlay.ActualHeight / 2),
                    0f);
            };

            // Safety net: CommunityToolkit implicit show animations (ShowTransitions)
            // use a Composition KeyFrameAnimation starting at opacity 0. On the first
            // Collapsed→Visible transition of a newly created element the animation can
            // complete without committing its final value, leaving the visual transparent.
            CourseContentGrid.RegisterPropertyChangedCallback(
                UIElement.VisibilityProperty,
                (sender, dp) =>
                {
                    if (CourseContentGrid.Visibility == Visibility.Visible)
                        _ = SnapContentOpacityAfterAnimationAsync();
                });

            // Listen for direction changes to swap animations before each transition
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            Loaded   += (_, _) => RegisterSessionExpiredHandler();
            Unloaded += (_, _) =>
            {
                WeakReferenceMessenger.Default.Unregister<SessionExpiredMessage>(this);
                // The ViewModel is a singleton held by DI, so we must explicitly remove this
                // handler otherwise each navigation creates a leaked page instance that is
                // kept alive forever by the strong delegate reference on the VM.
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            };
        }

        /// <summary>
        /// Waits for the ShowTransitions animation to finish (350 ms + margin), then
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
                visual.Offset  = System.Numerics.Vector3.Zero;
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
            // content grid, firing both HideTransitions and ShowTransitions implicit animations
            // on an off-screen Composition tree. If the user navigates here before the
            // animation finishes, the grid is caught at a partial opacity and stays frozen.
            // Snap the composition visual to its final fully-visible state.
            if (ViewModel.IsNotLoadingAndNotEmpty)
            {
                var visual = ElementCompositionPreview.GetElementVisual(CourseContentGrid);
                visual.Opacity = 1f;
                visual.Offset  = System.Numerics.Vector3.Zero;
            }

            // Only trigger a reload when there is genuinely nothing to show.
            // Do NOT reload just because HasSavedSession is false while we already have
            // courses rendered — FetchAcademicYearsAsync can run concurrently and may finish
            // after the schedule cache was already served; forcing a reload in that situation
            // clears the display and shows the login onboarding unexpectedly.
            // Session-expiry will be surfaced to the user the next time they hit Refresh.
            if (!ViewModel.IsLoading && !ViewModel.NeedsLogin && ViewModel.Courses.Count == 0)
            {
                _ = ViewModel.LoadScheduleAsync();
            }
        }

        private void RegisterSessionExpiredHandler()
        {
            WeakReferenceMessenger.Default.Unregister<SessionExpiredMessage>(this);
            WeakReferenceMessenger.Default.Register<SessionExpiredMessage>(this, async (_, _) =>
            {
                var dialog = new ContentDialog
                {
                    XamlRoot            = XamlRoot,
                    Title               = "Session expired",
                    Content             = "Your session has expired. The schedule shown may be outdated.\n\nWould you like to relog to get the latest data?",
                    PrimaryButtonText   = "Relog",
                    CloseButtonText     = "Later",
                    DefaultButton       = ContentDialogButton.Primary,
                };

                App.GetService<AccentColorService>()?.ApplyToContentDialog(dialog);

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                    ViewModel.TriggerReloginCommand.Execute(null);
            });
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.DaySlideDirection))
            {
                if (ViewModel.DaySlideDirection >= 0)
                {
                    Implicit.SetShowAnimations(TimetableContent, SlideShowFromRight);
                    Implicit.SetHideAnimations(TimetableContent, SlideHideToLeft);
                }
                else
                {
                    Implicit.SetShowAnimations(TimetableContent, SlideShowFromLeft);
                    Implicit.SetHideAnimations(TimetableContent, SlideHideToRight);
                }
            }
        }

        private void LoginToIGracias_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(WebViewerPage), "https://igracias.telkomuniversity.ac.id/");
        }

        private async void ConfigureAcademicYear_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Configure Academic Year",
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var stackPanel = new StackPanel { Spacing = 10 };
            var progressRing = new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Center };
            var comboBox = new ComboBox { Header = "Select Academic Year", HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
            var errorText = new TextBlock { Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red), Visibility = Visibility.Collapsed };

            stackPanel.Children.Add(progressRing);
            stackPanel.Children.Add(comboBox);
            stackPanel.Children.Add(errorText);
            dialog.Content = stackPanel;

            // Fetch options
            var scheduleService = App.GetService<MyTelU_Launcher.Services.IScheduleService>();
            var options = await scheduleService.FetchAcademicYearsAsync();

            progressRing.IsActive = false;
            progressRing.Visibility = Visibility.Collapsed;

            if (options == null || options.Count == 0)
            {
                errorText.Text = "Failed to fetch academic years. Please ensure you are logged in.";
                errorText.Visibility = Visibility.Visible;
                dialog.PrimaryButtonText = ""; // Disable save
            }
            else
            {
                comboBox.ItemsSource = options;
                comboBox.DisplayMemberPath = "Text";
                comboBox.Visibility = Visibility.Visible;

                // Select current
                foreach (var opt in options)
                {
                    if (opt.IsSelected)
                    {
                        comboBox.SelectedItem = opt;
                        break;
                    }
                }
            }

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && comboBox.SelectedItem is AcademicYearOption selectedOption)
            {
                scheduleService.SaveAcademicYear(selectedOption.YearCode, selectedOption.SemesterCode);
                // Use RefreshScheduleCommand — handles network check and bypasses cache.
                ViewModel.RefreshScheduleCommand.Execute(null);
            }
        }

        private void OpenDebugLog_Click(object sender, RoutedEventArgs e)
        {
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TY4EHelper", "schedule_debug.log");

            if (System.IO.File.Exists(logPath))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(logPath) { UseShellExecute = true });
            else
            {
                // Show path so user knows where it will appear
                var dialog = new ContentDialog
                {
                    Title = "Debug Log",
                    Content = $"Log file not found yet.\nIt will be created at:\n{logPath}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                _ = dialog.ShowAsync();
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

                    // 1. Create SurfaceBrush
                    var surface = Microsoft.UI.Xaml.Media.LoadedImageSurface.StartLoadFromUri(new Uri("ms-appx:///Assets/Galih_EmptyScedule_Icon.png"));
                    var imageBrush = compositor.CreateSurfaceBrush(surface);
                    imageBrush.Stretch = CompositionStretch.Uniform;

                    // 2. Linear Gradient Mask (Black=Visible, Transparent=Hidden)
                    var gradientBrush = compositor.CreateLinearGradientBrush();
                    gradientBrush.StartPoint = Vector2.Zero;
                    gradientBrush.EndPoint = new Vector2(0f, 1f);
                    gradientBrush.ColorStops.Add(compositor.CreateColorGradientStop(0.0f, Microsoft.UI.Colors.Black)); 
                    gradientBrush.ColorStops.Add(compositor.CreateColorGradientStop(0.6f, Microsoft.UI.Colors.Black)); 
                    gradientBrush.ColorStops.Add(compositor.CreateColorGradientStop(1.0f, Microsoft.UI.Colors.Transparent)); 

                    // 3. MaskBrush
                    var maskBrush = compositor.CreateMaskBrush();
                    maskBrush.Source = imageBrush; 
                    maskBrush.Mask = gradientBrush; 

                    // 4. SpriteVisual
                    var sprite = compositor.CreateSpriteVisual();
                    sprite.Brush = maskBrush;
                    sprite.Size = new Vector2((float)mascot.ActualWidth, (float)mascot.ActualHeight);

                    ElementCompositionPreview.SetElementChildVisual(mascot, sprite);
                }

                if (mascot.ActualWidth > 0 && mascot.ActualHeight > 0)
                {
                    SetupMascot();
                }
                else
                {
                    mascot.SizeChanged += (s, args) => SetupMascot();
                }
            }
        }
    }
}
