using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Numerics;
using CommunityToolkit.Mvvm.Messaging;
using MyTelU_Launcher.Contracts.Services;
using MyTelU_Launcher.Helpers;
using MyTelU_Launcher.Services;
using MyTelU_Launcher.Models;
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

        private StudentProfile? _accountProfile;
        private CancellationTokenSource? _accountLoad;
        private bool _hideProfilePicture;

        public ShellPage(ShellViewModel viewModel)
        {
            Current = this;
            ViewModel = viewModel;
            InitializeComponent();

            AccountButton.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(AccountButton_PointerPressed), true);
            AccountButton.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(AccountButton_ResetScale), true);
            WeakReferenceMessenger.Default.Register<HideProfilePictureChangedMessage>(this, static (recipient, message) =>
            {
                var shell = (ShellPage)recipient;
                shell._hideProfilePicture = message.Value;
                shell.UpdateAccountPicture();
            });

            // Set up main navigation.
            ViewModel.NavigationService.Frame = NavigationFrame;
            ViewModel.NavigationViewService.Initialize(NavigationViewControl);

            App.MainWindow.ExtendsContentIntoTitleBar = true;
            App.MainWindow.SetTitleBar(AppTitleBar);
            App.MainWindow.Activated += MainWindow_Activated;
            AppTitleBar.Title = "AppDisplayName".GetLocalized();
        }

        private void AccountButton_PointerEntered(object sender, PointerRoutedEventArgs e)
            => AccountButton.Scale = new Vector3(0.94f, 0.94f, 1f);

        private void AccountButton_PointerPressed(object sender, PointerRoutedEventArgs e)
            => AccountButton.Scale = new Vector3(0.88f, 0.88f, 1f);

        private void AccountButton_ResetScale(object sender, PointerRoutedEventArgs e)
            => AccountButton.Scale = Vector3.One;

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Apply the cached accent color synchronously RIGHT NOW before first paint.
            // This is the earliest moment App.MainWindow.Content exists as a FrameworkElement,
            // which is required for the theme toggle inside ApplyAccentColorCore to work.
            AccentColorService.ApplyCachedAccentEarly();

            TitleBarHelper.UpdateTitleBar(RequestedTheme);

            KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.Left, VirtualKeyModifiers.Menu));
            KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.GoBack));

            NavigationFrame.Navigated += NavigationFrame_Navigated;

            _hideProfilePicture = await App.GetService<ILocalSettingsService>().ReadSettingAsync<bool>(AppSettingKeys.HideProfilePicture);
            UpdateAccountPicture();
            var cachedProfile = await Task.Run(App.GetService<MobileAcademicService>().GetCachedProfile);
            if (cachedProfile != null) DisplayAccount(cachedProfile);

            // Optional: Trigger the update check on page load.
            await ViewModel.CheckForUpdatesAsync();
        }

        private void NavigationFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            ContentAcrylicLayer.Visibility = e.SourcePageType == typeof(HomePage)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            App.AppTitlebar = AppTitleBar;
        }

        private void TitleBar_BackRequested(Microsoft.UI.Xaml.Controls.TitleBar sender, object args)
        {
            ViewModel.NavigationService.GoBack();
        }

        private void TitleBar_PaneToggleRequested(Microsoft.UI.Xaml.Controls.TitleBar sender, object args)
        {
            NavigationViewControl.IsPaneOpen = !NavigationViewControl.IsPaneOpen;
        }

        private async void AccountFlyout_Opening(object sender, object e)
        {
            bool signedIn = App.GetService<IBrowserLoginService>().HasSavedCredentials;
            AccountSignOut.IsEnabled = signedIn || _accountProfile != null;
            AccountKtm.IsEnabled = signedIn || _accountProfile != null;
            AccountSignIn.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
            if (!signedIn)
            {
                if (_accountProfile == null) AccountMenuHeader.Description = "Not signed in";
                return;
            }
            if (_accountProfile is { IsCached: false }) return;

            _accountLoad?.Cancel();
            _accountLoad?.Dispose();
            _accountLoad = new CancellationTokenSource();
            var token = _accountLoad.Token;
            AccountMenuHeader.IsBusy = true;
            try
            {
                var profile = await App.GetService<MobileAcademicService>().GetProfileAsync(token);
                if (!token.IsCancellationRequested) DisplayAccount(profile);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or InvalidOperationException or InvalidDataException or TaskCanceledException)
            {
                AccountMenuHeader.Description = "Could not refresh your profile. Connect to the internet or sign in again.";
            }
            finally
            {
                if (!token.IsCancellationRequested) AccountMenuHeader.IsBusy = false;
            }
        }

        private void AccountFlyout_Closed(object sender, object e)
        {
            _accountLoad?.Cancel();
            AccountMenuHeader.IsBusy = false;
        }

        private void DisplayAccount(StudentProfile profile)
        {
            _accountProfile = profile;
            AccountMenuHeader.DisplayName = profile.FullName;
            AccountMenuHeader.Description = $"{profile.StudentId}\n{profile.StudyProgram}";
            UpdateAccountPicture();
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AccountMenuHeader,
                $"Student account, {profile.FullName}, {profile.StudentId}, {profile.StudyProgram}");
            ToolTipService.SetToolTip(AccountMenuHeader, profile.IsCached ? "Saved profile information" : null);
        }

        private void UpdateAccountPicture()
        {
            AccountAvatar.ProfilePicture = AccountMenuHeader.ProfilePicture = null;
            AccountAvatar.Initials = string.Empty;
            AccountAvatar.DisplayName = AccountMenuHeader.AvatarDisplayName =
                _hideProfilePicture ? string.Empty : _accountProfile?.FullName ?? string.Empty;
            if (!_hideProfilePicture && Uri.TryCreate(_accountProfile?.Photo, UriKind.Absolute, out var photo)
                && photo.Scheme == Uri.UriSchemeHttps
                && (photo.Host == "telkomuniversity.ac.id" || photo.Host.EndsWith(".telkomuniversity.ac.id", StringComparison.OrdinalIgnoreCase)))
                AccountAvatar.ProfilePicture = AccountMenuHeader.ProfilePicture = new BitmapImage(photo);
        }

        private async void AccountSignIn_Click(object sender, RoutedEventArgs e)
        {
            AccountFlyout.Hide();
            ViewModel.NavigationService.NavigateTo(typeof(ScheduleViewModel).FullName!);
            await App.GetService<ScheduleViewModel>().LoginWithBrowserCommand.ExecuteAsync(null);
        }

        private async void AccountSignOut_Click(object sender, RoutedEventArgs e)
        {
            AccountFlyout.Hide();
            await App.GetService<ScheduleViewModel>().RequestClearSessionAsync();
            if (!App.GetService<IBrowserLoginService>().HasSavedCredentials)
            {
                _accountProfile = null;
                UpdateAccountPicture();
                AccountMenuHeader.DisplayName = "Student profile";
                AccountMenuHeader.Description = "Not signed in";
                ViewModel.NavigationService.NavigateTo(typeof(ScheduleViewModel).FullName!, clearNavigation: true);
            }
        }

        private async void AccountKtm_Click(object sender, RoutedEventArgs e)
        {
            AccountFlyout.Hide();
            var dialog = new StudentProfileDialog { XamlRoot = XamlRoot, Title = "KTM Digital" };
            App.GetService<AccentColorService>().ApplyToContentDialog(dialog);
            await dialog.ShowAsync();
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
            if (OverlayFrame.Content is InAppBrowserPage browserPage)
            {
                browserPage.Cleanup();
            }

            OverlayContainer.Visibility = Visibility.Collapsed;
            OverlayFrame.BackStack.Clear();
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

        // Selection changes are now handled by the NavigationHelper.NavigateTo attached property
        // on each NavigationViewItem — no custom routing needed for Attendance.
    }
}
