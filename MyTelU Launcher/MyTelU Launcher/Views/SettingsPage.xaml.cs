using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using MyTelU_Launcher.Contracts.Services;
using MyTelU_Launcher.Helpers;
using Windows.ApplicationModel;
using Windows.Storage;
using Microsoft.Windows.Storage.Pickers;

namespace MyTelU_Launcher.ViewModels
{
    public partial class SettingsViewModel : ObservableRecipient
    {
        private readonly IThemeSelectorService _themeSelectorService;
        private readonly ILocalSettingsService _localSettingsService;

        [ObservableProperty]
        private ElementTheme _elementTheme;

        [ObservableProperty]
        private string _versionDescription;

        [ObservableProperty]
        private bool _isCustomImageBGEnabled;

        // Explicit backing field and property for the custom image path.
        private string _customImagePath;
        public string CustomImagePath
        {
            get => _customImagePath;
            set => SetProperty(ref _customImagePath, value);
        }

        // Computed property for the description.
        public string CustomImagePathDescription => string.IsNullOrEmpty(CustomImagePath)
            ? "Path: Not selected"
            : $"Path: {CustomImagePath}";

        public IEnumerable<ElementTheme> Themes
        {
            get;
        } = new List<ElementTheme>
        {
            ElementTheme.Light,
            ElementTheme.Dark,
            ElementTheme.Default
        };

        public SettingsViewModel(IThemeSelectorService themeSelectorService, ILocalSettingsService localSettingsService)
        {
            _themeSelectorService = themeSelectorService;
            _localSettingsService = localSettingsService;
            _elementTheme = _themeSelectorService.Theme;
            _versionDescription = GetVersionDescription();
            // Initialize with default value.
            _customImagePath = string.Empty;
            InitializeSettingsAsync();
        }

        private async void InitializeSettingsAsync()
        {
            _isCustomImageBGEnabled = await _localSettingsService.ReadSettingAsync<bool>("IsCustomImageBGEnabled");
            // Load any previously saved custom image path.
            CustomImagePath = await _localSettingsService.ReadSettingAsync<string>("CustomBackgroundImagePath");
            OnPropertyChanged(nameof(CustomImagePathDescription));
        }

        partial void OnElementThemeChanged(ElementTheme value)
        {
            _ = _themeSelectorService.SetThemeAsync(value);
        }

        partial void OnIsCustomImageBGEnabledChanged(bool value)
        {
            OnIsCustomImageBGEnabledChangedAsync(value);
        }

        private async void OnIsCustomImageBGEnabledChangedAsync(bool value)
        {
            await _localSettingsService.SaveSettingAsync("IsCustomImageBGEnabled", value);

            if (value)
            {
                // When enabled, retrieve the stored image path and send it.
                var path = await _localSettingsService.ReadSettingAsync<string>("CustomBackgroundImagePath");
                WeakReferenceMessenger.Default.Send(new BackgroundImageChangedMessage(path));
            }
            else
            {
                // When disabled, signal default image usage.
                WeakReferenceMessenger.Default.Send(new BackgroundImageChangedMessage(string.Empty));
            }
        }

        public async Task PickBackgroundImageAsync()
        {
            // Use new WinAppSDK 1.8+ picker that supports elevated processes (Microsoft.Windows.Storage.Pickers)
            var picker = new FileOpenPicker(App.MainWindow.AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                ViewMode = PickerViewMode.Thumbnail
            };

            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".jfif");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".tiff");
            picker.FileTypeFilter.Add(".tif");
            picker.FileTypeFilter.Add(".webp");

            var result = await picker.PickSingleFileAsync();
            if (result != null)
            {
                // PickFileResult exposes Path directly
                var filePath = result.Path;
                await _localSettingsService.SaveSettingAsync("CustomBackgroundImagePath", filePath);
                CustomImagePath = filePath;
                OnPropertyChanged(nameof(CustomImagePathDescription));
                WeakReferenceMessenger.Default.Send(new BackgroundImageChangedMessage(filePath));
            }
        }

        private static string GetVersionDescription()
        {
            Version version;
            if (RuntimeHelper.IsMSIX)
            {
                var packageVersion = Package.Current.Id.Version;
                version = new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
            }
            else
            {
                version = Assembly.GetExecutingAssembly().GetName().Version!;
            }

            return $"{"AppDisplayName".GetLocalized()} - {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
    }
}
