using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyTelU_Launcher.Contracts.Services;
using MyTelU_Launcher.Helpers;
using MyTelU_Launcher.ViewModels;
using Windows.System;

namespace MyTelU_Launcher.Views
{
    public sealed partial class ShellPage : Page
    {
        // Static reference to the current ShellPage instance.
        public static ShellPage Current
        {
            get; private set;
        }
        public ShellViewModel ViewModel
        {
            get;
        }

        public ShellPage(ShellViewModel viewModel)
        {
            Current = this;
            ViewModel = viewModel;
            InitializeComponent();

            // Set up main navigation.
            ViewModel.NavigationService.Frame = NavigationFrame;
            ViewModel.NavigationViewService.Initialize(NavigationViewControl);

            App.MainWindow.ExtendsContentIntoTitleBar = true;
            App.MainWindow.SetTitleBar(AppTitleBar);
            App.MainWindow.Activated += MainWindow_Activated;
            AppTitleBarText.Text = "AppDisplayName".GetLocalized();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            TitleBarHelper.UpdateTitleBar(RequestedTheme);

            KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.Left, VirtualKeyModifiers.Menu));
            KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.GoBack));

            // Optional: Trigger the update check on page load.
            await ViewModel.CheckForUpdatesAsync();
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            App.AppTitlebar = AppTitleBarText as UIElement;
        }

        private void NavigationViewControl_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
        {
            AppTitleBar.Margin = new Thickness()
            {
                Left = sender.CompactPaneLength * (sender.DisplayMode == NavigationViewDisplayMode.Minimal ? 2 : 1),
                Top = AppTitleBar.Margin.Top,
                Right = AppTitleBar.Margin.Right,
                Bottom = AppTitleBar.Margin.Bottom
            };
        }

        private static KeyboardAccelerator BuildKeyboardAccelerator(VirtualKey key, VirtualKeyModifiers? modifiers = null)
        {
            var keyboardAccelerator = new KeyboardAccelerator() { Key = key };

            if (modifiers.HasValue)
            {
                keyboardAccelerator.Modifiers = modifiers.Value;
            }

            keyboardAccelerator.Invoked += OnKeyboardAcceleratorInvoked;

            return keyboardAccelerator;
        }

        private static void OnKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            var navigationService = App.GetService<INavigationService>();
            var result = navigationService.GoBack();
            args.Handled = result;
        }

        /// <summary>
        /// Shows the overlay UI by navigating the overlay frame to the specified page,
        /// passing an optional parameter (e.g. a URL).
        /// </summary>
        /// <param name="pageType">The type of page to show in the overlay (e.g. InAppBrowserPage).</param>
        /// <param name="parameter">Optional navigation parameter.</param>
        public void ShowOverlay(System.Type pageType, object parameter = null)
        {
            if (pageType == null)
            {
                return;
            }

            // Navigate the overlay frame with the parameter.
            OverlayFrame.Navigate(pageType, parameter);
            OverlayContainer.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Hides the overlay UI.
        /// </summary>
        public void HideOverlay()
        {
            OverlayContainer.Visibility = Visibility.Collapsed;
            // Optionally clear the overlay frame’s content.
            OverlayFrame.Content = null;
        }

        private async void DownloadNowButton_Click(object sender, RoutedEventArgs e)
        {
            var uri = new Uri("http://link.denesis.net/OR1CRDSK10");
            bool success = await Launcher.LaunchUriAsync(uri);
            if (!success)
            {
                System.Diagnostics.Debug.WriteLine("Failed to launch the URL.");
            }
        }

        // Handle selection changes to implement custom navigation for "Atendance"
        private void NavigationViewControl_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem selectedItem)
            {
                if (selectedItem == ScheduleNavItem)
                {
                    // Navigate to the web viewer using your custom method.
                    NavigateToWebViewer("https://igracias.telkomuniversity.ac.id/registration/index.php?pageid=2901");
                }
                // Check if the selected item is the "Atendance" item.
                if (selectedItem == AttendanceNavItem)
                {
                    // Navigate to the web viewer using your custom method.
                    NavigateToWebViewer("https://igracias.telkomuniversity.ac.id/presence/index.php?pageid=3942");
                }
            }
        }

        // Use the named NavigationFrame for navigation instead of the Page.Frame property.
        private void NavigateToWebViewer(string url)
        {
            NavigationFrame.Navigate(typeof(WebViewerPage), url);
        }
    }
}
