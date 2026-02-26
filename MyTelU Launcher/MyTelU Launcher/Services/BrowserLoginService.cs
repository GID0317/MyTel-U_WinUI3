using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Playwright;
using MyTelU_Launcher.Helpers;

namespace MyTelU_Launcher.Services;

public interface IBrowserLoginService
{
    Task<bool> StartLoginAsync(CancellationToken cancellationToken = default);
    bool IsRunning { get; }
}

public class BrowserLoginService : IBrowserLoginService
{
    private static readonly string _appDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TY4EHelper");

    private static readonly string _settingsFile = Path.Combine(_appDataDir, "settings.json");

    private const string IgraciasUrl = "https://igracias.telkomuniversity.ac.id/";

    // Cookies required for session (same as Python script)
    private static readonly HashSet<string> _wantedCookies = new()
    {
        "PHPSESSID",
        "BIGipServerpool_iGracias",
        "perf_dv6Tr4n"
    };

    // Success indicators for login (same as Python script)
    private static readonly string[] _successKeywords =
        { "dashboard", "home", "registration", "pageid" };

    private volatile bool _isRunning;
    public bool IsRunning => _isRunning;

    public async Task<bool> StartLoginAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning) return false;
        _isRunning = true;

        try
        {
            // Create Playwright instance
            using var playwright = await Playwright.CreateAsync();

            // Launch Edge (system installed) in headed mode so user can see/interact
            // Channel = "msedge" uses the Windows installation of Edge.
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Channel  = "msedge",
                Headless = false,
                Args     = new[] { "--start-maximized" },
                Timeout  = 20000 // 20s timeout for launch
            });

            try
            {
                var context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = ViewportSize.NoViewport
                });

                var page = await context.NewPageAsync();
                
                Debug.WriteLine("[BrowserLogin] Navigating to iGracias...");
                await page.GotoAsync(IgraciasUrl);

                // Wait for login loop (max 5 mins)
                var deadline = DateTime.UtcNow.AddMinutes(5);
                bool loggedIn = false;

                while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
                {
                    var url = page.Url.ToLowerInvariant();
                    
                    // Check for cookies FIRST
                    var currentCookies = await context.CookiesAsync();
                    var hasSession = currentCookies.Any(c => c.Name == "PHPSESSID");

                    bool urlLooksSuccess = _successKeywords.Any(k => url.Contains(k));
                    
                    // If we have the session cookie AND the URL looks like a logged-in page
                    if (hasSession && urlLooksSuccess)
                    {
                        // Double check: is there a logout button? 
                        // If not, we might be on a transition page.
                        // (iGracias usually has a logout link/button)
                        try 
                        {
                            // "Sign out", "Logout", "Keluar", or typical profile icons
                            // We can just wait a bit to be sure it's stable.
                            Debug.WriteLine($"[BrowserLogin] Session cookie found on success URL: {url}");
                            
                            // 2024 Fix: Wait for stability. If it redirects again, the loop continues.
                            // We wait 2 seconds, then check if we are still on a success URL.
                            await Task.Delay(2000, cancellationToken);
                            
                            if (page.Url.ToLowerInvariant().Contains("login"))
                            {
                                Debug.WriteLine("[BrowserLogin] Redirected back to login! Continuing...");
                                continue;
                            }
                            
                            loggedIn = true;
                            break; 
                        }
                        catch { /* ignore, keep looping */ }
                    }

                    await Task.Delay(1000, cancellationToken);
                }

                if (!loggedIn)
                {
                    Debug.WriteLine("[BrowserLogin] Timeout or cancellation.");
                    await browser.CloseAsync(); // Explicitly close on failure too
                    return false;
                }

                Debug.WriteLine("[BrowserLogin] Login confirmed stable. Saving cookies...");
                
                // Final capture
                var allCookies = await context.CookiesAsync();
                var dict = allCookies
                    .Where(c => _wantedCookies.Contains(c.Name))
                    .ToDictionary(c => c.Name, c => c.Value);

                if (!dict.ContainsKey("PHPSESSID"))
                {
                    Debug.WriteLine("[BrowserLogin] Login seemed successful but PHPSESSID missing.");
                }

                // Save cookies even if PHPSESSID is missing, sometimes it's under a different name or path?
                // No, Python script requires PHPSESSID.
                // But we'll save what we have.

                // Store in Windows Credential Manager (PasswordVault, CurrentUser scope)
                // so the session cookie never touches disk as plain text.
                await CookieStore.SaveAsync(
                    JsonSerializer.Serialize(dict),
                    cancellationToken);

                Debug.WriteLine($"[BrowserLogin] Successfully saved {dict.Count} cookies.");

                // Attempt to capture Student ID
                await TryCaptureStudentIdAsync(page, cancellationToken);

                // Notify UI to refresh logic
                WeakReferenceMessenger.Default.Send(new SessionCookiesSavedMessage());
                
                return true;
            }
            finally
            {
                // Ensure browser closes
                await browser.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BrowserLogin] Error: {ex.Message}");
            return false;
        }
        finally
        {
            _isRunning = false;
        }
    }

    private async Task TryCaptureStudentIdAsync(IPage page, CancellationToken ct)
    {
        try
        {
            // If already have ID, don't overwrite blindly
            if (File.Exists(_settingsFile))
            {
                try {
                    var text = await File.ReadAllTextAsync(_settingsFile, ct);
                    if (text.Contains("studentId")) return;
                } catch {}
            }

            // Expanded regex to catch 10-digit ID or 12-digit or similar
            // e.g. 101032300012
            var result = await page.EvaluateAsync<string>(@"() => {
                try {
                    // Search for 10 to 14 digit number that starts with 1
                    var m = document.body.innerText.match(/\b1\d{9,13}\b/);
                    return m ? m[0] : null;
                } catch(e) { return null; }
            }");

            if (!string.IsNullOrWhiteSpace(result))
            {
                var settings = new Dictionary<string, string>();
                if (File.Exists(_settingsFile))
                {
                    try {
                         settings = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(_settingsFile, ct)) ?? new();
                    } catch {}
                }
                
                settings["studentId"] = result;
                await File.WriteAllTextAsync(_settingsFile, JsonSerializer.Serialize(settings), ct);
                Debug.WriteLine($"[BrowserLogin] Captured StudentID: {result}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BrowserLogin] ID capture error: {ex.Message}");
        }
    }
}
