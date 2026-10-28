using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using MyTelU_Launcher.Helpers;

namespace MyTelU_Launcher.Services;

public interface IBrowserLoginService
{
    Task<bool> StartLoginAsync(CancellationToken cancellationToken = default);
    Task<bool> TrySilentLoginAsync(CancellationToken cancellationToken = default);
    bool IsRunning { get; }
    bool HasSavedCredentials { get; }
    void ClearCredentials();
}

public sealed class BrowserLoginService : IBrowserLoginService
{
    private int _running;
    private readonly object _loginGate = new();
    private Task<bool>? _loginTask;
    public bool IsRunning => Volatile.Read(ref _running) != 0;
    public bool HasSavedCredentials => CredentialStore.HasCredentials();
    public void ClearCredentials() => CredentialStore.Clear();

    public Task<bool> StartLoginAsync(CancellationToken cancellationToken = default)
        => LoginAsync("--interactive", cancellationToken);

    public Task<bool> TrySilentLoginAsync(CancellationToken cancellationToken = default)
        => HasSavedCredentials ? LoginAsync("--silent", cancellationToken) : Task.FromResult(false);

    private async Task<bool> LoginAsync(string operation, CancellationToken cancellationToken)
    {
        Task<bool> pending;
        lock (_loginGate)
        {
            if (_loginTask == null || _loginTask.IsCompleted)
                _loginTask = RunAsync(operation, CancellationToken.None);
            pending = _loginTask;
        }
        try { return await pending.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return false; }
    }

    private async Task<bool> RunAsync(string operation, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return false;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(AppContext.BaseDirectory, "AuthenticationHost", "MyTelU.AuthenticationHost.exe"),
                Arguments = operation,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.Combine(AppContext.BaseDirectory, "AuthenticationHost")
            }
        };
        bool started = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            started = process.Start();
            await process.WaitForExitAsync(cancellationToken);
            bool success = process.ExitCode == 0;
            if (success && operation == "--interactive")
                WeakReferenceMessenger.Default.Send(new SessionCookiesSavedMessage());
            return success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (started && !process.HasExited)
                process.Kill(entireProcessTree: true);
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BrowserLogin] Authentication host failed: {ex.GetType().Name}");
            return false;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}
