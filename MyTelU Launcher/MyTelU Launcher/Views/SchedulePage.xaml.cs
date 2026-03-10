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

        private bool _isSubscribedToAccentUpdates = false;

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

            // Listen for direction changes to swap animations before each transition
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Listen for accent color changes to refresh Segmented pill colors.
            // Static event creates a strong reference, preventing GC issues that affected WeakReferenceMessenger.
            if (!_isSubscribedToAccentUpdates)
            {
                AccentColorService.AccentColorsUpdated += OnAccentColorsUpdated;
                _isSubscribedToAccentUpdates = true;
            }

            // Listen for offline-mode blocked-action notifications.
            WeakReferenceMessenger.Default.Register<OfflineModeMessage>(this, async (_, msg) =>
            {
                var dialog = new ContentDialog
                {
                    XamlRoot        = XamlRoot,
                    Title           = "You\u2019re in offline mode",
                    DefaultButton   = ContentDialogButton.Close,
                    CloseButtonText = "OK",
                    Content         = new StackPanel { Spacing = 12, Children =
                    {
                        new TextBlock
                        {
                            Text         = "The schedule shown is the last cached version and it may not reflect the latest data. Please connect to the internet and try again later.",
                            TextWrapping = TextWrapping.WrapWholeWords,
                            Opacity      = 0.85
                        }
                    }}
                };

                // Apply dynamic accent colors to fix ContentDialog button hover states
                var accentService = App.GetService<AccentColorService>();
                accentService?.ApplyToContentDialog(dialog);

                await dialog.ShowAsync();
            });

            // Don't unsubscribe on Unloaded - it fires when switching List/Timeline views within the page!
            // NavigationCacheMode="Required" keeps the page alive. Cleanup when actually disposed.
            // Static event subscription is acceptable here since page lifetime matches app lifetime in practice.
        }

        private void OnAccentColorsUpdated(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (DaySegmented == null) return;
                
                var accentService = App.GetService<AccentColorService>();
                accentService?.ApplyToSegmented(DaySegmented);
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
