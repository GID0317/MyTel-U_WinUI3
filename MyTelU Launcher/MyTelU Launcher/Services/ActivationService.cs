using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using MyTelU_Launcher.Activation;
using MyTelU_Launcher.Contracts.Services;
using MyTelU_Launcher.Views;

namespace MyTelU_Launcher.Services;

public class ActivationService : IActivationService
{
    private readonly ActivationHandler<LaunchActivatedEventArgs> _defaultHandler;
    private readonly IEnumerable<IActivationHandler> _activationHandlers;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly AccentColorService _accentColorService;
    private UIElement? _shell = null;

    public ActivationService(ActivationHandler<LaunchActivatedEventArgs> defaultHandler, IEnumerable<IActivationHandler> activationHandlers, IThemeSelectorService themeSelectorService, AccentColorService accentColorService)
    {
        _defaultHandler = defaultHandler;
        _activationHandlers = activationHandlers;
        _themeSelectorService = themeSelectorService;
        _accentColorService = accentColorService;
    }

    public async Task ActivateAsync(object activationArgs)
    {
        // Execute tasks before activation.
        await InitializeAsync();

        // Set the MainWindow Content.
        if (App.MainWindow.Content == null)
        {
            _shell = App.GetService<ShellPage>();
            App.MainWindow.Content = _shell ?? new Frame();
        }

        // Apply the stored accent color NOW — the FrameworkElement exists but the window
        // is not yet visible, so this is the last moment before frame-1 is painted.
        // This eliminates the system-accent flash on startup.
        AccentColorService.ApplyCachedAccentEarly();

        // Handle activation via ActivationHandlers.
        await HandleActivationAsync(activationArgs);

        // Activate the MainWindow.
        App.MainWindow.Activate();

        // Execute tasks after activation.
        await StartupAsync();
    }

    private async Task HandleActivationAsync(object activationArgs)
    {
        var activationHandler = _activationHandlers.FirstOrDefault(h => h.CanHandle(activationArgs));

        if (activationHandler != null)
        {
            await activationHandler.HandleAsync(activationArgs);
        }

        if (_defaultHandler.CanHandle(activationArgs))
        {
            await _defaultHandler.HandleAsync(activationArgs);
        }
    }

    private async Task InitializeAsync()
    {
        await _themeSelectorService.InitializeAsync().ConfigureAwait(false);
        await Task.CompletedTask;
    }

    private async Task StartupAsync()
    {
        await _themeSelectorService.SetRequestedThemeAsync();
        // Re-apply after theme change: SetRequestedThemeAsync can trigger WinUI to
        // re-evaluate themed brushes from the XAML dictionaries, momentarily reverting
        // to the system accent.  Forcing a re-apply here keeps the custom colour intact.
        AccentColorService.ApplyCachedAccentEarly();
        await Task.CompletedTask;
    }
}
