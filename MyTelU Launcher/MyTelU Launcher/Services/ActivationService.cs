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
        await InitializeAsync();

        if (App.MainWindow.Content == null)
        {
            _shell = App.GetService<ShellPage>();
            App.MainWindow.Content = _shell ?? new Frame();
        }

        // Apply the cached accent before the first frame to avoid the default accent flash.
        AccentColorService.ApplyCachedAccentEarly();

        await HandleActivationAsync(activationArgs);

        App.MainWindow.Activate();

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
        // Theme application can temporarily restore the system accent, so re-apply here.
        AccentColorService.ApplyCachedAccentEarly();
        await Task.CompletedTask;
    }
}
