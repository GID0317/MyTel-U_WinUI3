using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

using MyTelU_Launcher.Activation;
using MyTelU_Launcher.Contracts.Services;
using MyTelU_Launcher.Core.Contracts.Services;
using MyTelU_Launcher.Core.Services;
using MyTelU_Launcher.Helpers;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.Notifications;
using MyTelU_Launcher.Services;
using MyTelU_Launcher.ViewModels;
using MyTelU_Launcher.Views;
using Windows.ApplicationModel;

namespace MyTelU_Launcher;

public partial class App : Application
{
    public IHost Host
    {
        get;
    }

    public static T GetService<T>() where T : class
    {
        if ((App.Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
        {
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
        }
        return service;
    }

    public static WindowEx MainWindow { get; } = new MainWindow();
    public static Version AppVersion
    {
        get; private set;
    }
    public static UIElement? AppTitlebar
    {
        get; set;
    }

    public App()
    {
        InitializeComponent();

        // Apply the previously-cached accent color synchronously before any window is created.
        // This ensures frame 1 already has the correct color, eliminating the startup flash.
        AccentColorService.ApplyCachedAccentEarly();

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices((context, services) =>
            {
                // Default Activation Handler
                services.AddTransient<ActivationHandler<LaunchActivatedEventArgs>, DefaultActivationHandler>();

                // Other Activation Handlers
                services.AddTransient<IActivationHandler, AppNotificationActivationHandler>();

                // Services
                services.AddSingleton<IAppNotificationService, AppNotificationService>();
                services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
                services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
                services.AddSingleton<AccentColorService>();
                services.AddTransient<IWebViewService, WebViewService>();
                services.AddTransient<INavigationViewService, NavigationViewService>();

                services.AddSingleton<IActivationService, ActivationService>();
                services.AddSingleton<IPageService, PageService>();
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IScheduleService, ScheduleService>();
                services.AddSingleton<IAttendanceService, AttendanceService>();
                services.AddSingleton<IBrowserLoginService, BrowserLoginService>();

                // Core Services
                services.AddSingleton<IFileService, FileService>();

                // Views and ViewModels
                services.AddTransient<InAppBrowserViewModel>();
                services.AddTransient<InAppBrowserPage>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<SettingsPage>();
                services.AddTransient<WebViewerViewModel>();
                services.AddTransient<WebViewerPage>();
                services.AddSingleton<OpenCommunityToolsViewModel>();
                services.AddTransient<OpenCommunityToolsPage>();
                services.AddTransient<HomeViewModel>();
                services.AddTransient<HomePage>();
                services.AddTransient<ScheduleViewModel>();
                services.AddTransient<SchedulePage>();
                services.AddTransient<AttendanceViewModel>();
                services.AddTransient<AttendancePage>();
                services.AddSingleton<IGradeService, GradeService>();
                services.AddTransient<GradeViewModel>();
                services.AddTransient<GradePage>();
                services.AddTransient<ShellPage>();
                services.AddTransient<ShellViewModel>();

                // Configuration
                services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
            })
            .Build();

        try
        {
            App.GetService<IAppNotificationService>().Initialize();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] AppNotificationService.Initialize failed: {ex}");
        }
        UnhandledException += App_UnhandledException;
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        var msg = $"[{DateTime.Now:u}] Unhandled exception: {e.Exception}";
        System.Diagnostics.Debug.WriteLine(msg);
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TY4EHelper");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "crash.log"), msg + Environment.NewLine);
        }
        catch { }
    }

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Retrieve and store the package version once the app is launched.
        try
        {
            var packageVersion = Package.Current.Id.Version;
            AppVersion = new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
        }
        catch (Exception ex)
        {
            // Log error and use a fallback version.
            System.Diagnostics.Debug.WriteLine("Failed to get package version: " + ex.Message);
            AppVersion = new Version(4, 0, 0, 0);
        }

        base.OnLaunched(args);
        //App.GetService<IAppNotificationService>().Show(string.Format("AppNotificationSamplePayload".GetLocalized(), AppContext.BaseDirectory));
        _ = App.GetService<IScheduleService>().StartServerAsync();
        await App.GetService<IActivationService>().ActivateAsync(args);
    }
}
