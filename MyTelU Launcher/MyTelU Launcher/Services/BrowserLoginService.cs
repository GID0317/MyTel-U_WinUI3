using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Playwright;
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

public class BrowserLoginService : IBrowserLoginService
{
    private static readonly string _appDataDir = AppDataStore.DirectoryPath;

    private static readonly string _settingsFile = Path.Combine(_appDataDir, "settings.json");

    private const string IgraciasUrl = "https://igracias.telkomuniversity.ac.id/";

    // Keep only the cookies needed for authenticated iGracias requests.
    private static readonly HashSet<string> _wantedCookies = new()
    {
        "PHPSESSID",
        "BIGipServerpool_iGracias",
        "perf_dv6Tr4n"
    };

    // These path fragments only appear after a successful login.
    private static readonly string[] _successKeywords =
        { "dashboard", "home", "registration", "pageid" };

    private volatile bool _isRunning;
    public bool IsRunning => _isRunning;
    public bool HasSavedCredentials => CredentialStore.HasCredentials();
    public void ClearCredentials() => CredentialStore.Clear();

    public async Task<bool> StartLoginAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning) return false;
        _isRunning = true;

        try
        {
            using var playwright = await Playwright.CreateAsync();

            // Use the installed Edge build so the user gets the normal login flow.
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Channel  = "msedge",
                Headless = false,
                Args     = new[] { "--start-maximized" },
                Timeout  = 20000
            });

            try
            {
                var context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = ViewportSize.NoViewport
                });

                var page = await context.NewPageAsync();

                // Limit routing to iGracias traffic so external assets bypass the handler.
                string? capturedUser = null, capturedPass = null;
                await page.RouteAsync("**/igracias.telkomuniversity.ac.id/**", async route =>
                {
                    var req = route.Request;
#if DEBUG
                    if (req.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                    {
                        SilentLog($"[Route] POST intercepted: {req.Url}");
                        SilentLog($"[Route]   PostData: {(req.PostData?.Length > 200 ? req.PostData[..200] : req.PostData) ?? "(null)"}");
                    }
#endif
                    if (req.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                        && req.PostData is { } body
                        && body.Contains("password", StringComparison.OrdinalIgnoreCase))
                    {
                        var qs = ParseQueryString(body);
                        var u = qs.GetValueOrDefault("textUsername") ?? qs.GetValueOrDefault("username") ?? qs.GetValueOrDefault("user");
                        var p = qs.GetValueOrDefault("textPassword") ?? qs.GetValueOrDefault("password") ?? qs.GetValueOrDefault("pass");
                        if (!string.IsNullOrEmpty(u) && !string.IsNullOrEmpty(p))
                        {
                            capturedUser = u;
                            capturedPass = p;
                            SilentLog($"[Route] Captured credentials for user: {u} from {req.Url}");
                        }
                        else
                        {
                            SilentLog($"[Route] POST has 'password' but parse failed. Keys found: {string.Join(",", qs.Keys)}");
                        }
                    }
                    await route.ContinueAsync();
                });

                Debug.WriteLine("[BrowserLogin] Navigating to iGracias...");

                // page.Close catches the common case where the window closes but the process stays alive.
                bool browserDisconnected = false;
                var disconnectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void MarkDisconnected(object? s, object? e)
                {
                    browserDisconnected = true;
                    disconnectedTcs.TrySetResult();
                }
                browser.Disconnected  += MarkDisconnected;
                page.Close            += MarkDisconnected;
                context.Close         += MarkDisconnected;

                await Task.WhenAny(page.GotoAsync(IgraciasUrl), disconnectedTcs.Task);
                if (browserDisconnected) return false;

                var deadline = DateTime.UtcNow.AddMinutes(5);
                bool loggedIn = false;

                while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested && !browserDisconnected)
                {
                    IReadOnlyList<Microsoft.Playwright.BrowserContextCookiesResult> currentCookies;
                    try
                    {
                        // CookiesAsync can stall briefly after a window close, so race it against disconnect.
                        var cookiesTask = context.CookiesAsync();
                        var completed = await Task.WhenAny(cookiesTask, disconnectedTcs.Task);
                        if (completed == disconnectedTcs.Task || browserDisconnected) break;
                        currentCookies = await cookiesTask.WaitAsync(TimeSpan.FromSeconds(3));
                    }
                    catch
                    {
                        break;
                    }

                    var url = page.Url.ToLowerInvariant();
                    var hasSession = currentCookies.Any(c => c.Name == "PHPSESSID");
                    bool urlLooksSuccess = _successKeywords.Any(k => url.Contains(k));
                    
                    if (hasSession && urlLooksSuccess)
                    {
                        try 
                        {
                            Debug.WriteLine($"[BrowserLogin] Session cookie found on success URL: {url}");
                            
                            // A short delay filters out redirects that bounce back to the login page.
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

                    await Task.WhenAny(Task.Delay(1000, cancellationToken), disconnectedTcs.Task);
                    if (browserDisconnected) break;
                }

                if (!loggedIn)
                {
                    Debug.WriteLine("[BrowserLogin] Timeout, cancellation, or browser closed by user.");
                    try { await browser.CloseAsync().WaitAsync(TimeSpan.FromSeconds(3)); } catch { /* already closed */ }
                    return false;
                }

                Debug.WriteLine("[BrowserLogin] Login confirmed stable. Saving cookies...");
                
                var allCookies = await context.CookiesAsync();
                var dict = allCookies
                    .Where(c => _wantedCookies.Contains(c.Name))
                    .ToDictionary(c => c.Name, c => c.Value);

                if (!dict.ContainsKey("PHPSESSID"))
                {
                    Debug.WriteLine("[BrowserLogin] Login seemed successful but PHPSESSID missing.");
                }

                // Keep the raw capture for debugging even if the login ultimately turns out incomplete.
                await CookieStore.SaveAsync(
                    JsonSerializer.Serialize(dict),
                    cancellationToken);

                Debug.WriteLine($"[BrowserLogin] Successfully saved {dict.Count} cookies.");

                // Only persist credentials when they came from a real login form submission.
                SilentLog($"[BrowserLogin] capturedUser={capturedUser ?? "(null)"}, capturedPass={(capturedPass != null ? "***" : "(null)")}");
                if (!string.IsNullOrEmpty(capturedUser) && !string.IsNullOrEmpty(capturedPass))
                {
                    CredentialStore.Save(capturedUser, capturedPass);
                    SilentLog("[BrowserLogin] Credentials saved to CredentialStore.");
                }
                else
                {
                    SilentLog("[BrowserLogin] WARNING: No credentials were captured — silent login will not work.");
                }

                await TryCaptureStudentIdAsync(page, cancellationToken);

                WeakReferenceMessenger.Default.Send(new SessionCookiesSavedMessage());
                
                return true;
            }
            finally
            {
                try { await browser.CloseAsync().WaitAsync(TimeSpan.FromSeconds(3)); } catch { /* already disconnected */ }
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

    /// <summary>
    /// Silently re-authenticates using stored DPAPI-encrypted credentials via headless Playwright.
    /// Returns true and saves fresh cookies on success; returns false on any failure.
    /// </summary>
    public async Task<bool> TrySilentLoginAsync(CancellationToken cancellationToken = default)
    {
        var (username, password) = CredentialStore.Load();
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            SilentLog("No credentials in CredentialStore — aborting silent login.");
            FeatureFlowLogger.Write("SilentLogin", "aborted: no saved credentials");
            return false;
        }

        SilentLog($"=== TrySilentLoginAsync started for user: {username} ===");
        FeatureFlowLogger.Write("SilentLogin", $"start: user={username}");

        // Try the lightweight HTTP path first and only start Playwright if it fails.
        if (await TrySilentLoginHttpAsync(username, password, cancellationToken))
        {
            FeatureFlowLogger.Write("SilentLogin", "success: http path");
            return true;
        }

        SilentLog("HTTP path failed, falling back to headless Playwright...");
        FeatureFlowLogger.Write("SilentLogin", "http failed: falling back to playwright");
        var ok = await TrySilentLoginPlaywrightAsync(username, password, cancellationToken);
        FeatureFlowLogger.Write("SilentLogin", ok ? "success: playwright path" : "failed: playwright path");
        return ok;
    }

    /// <summary>
    /// Attempts login via a plain HttpClient form POST — no browser process spawned.
    /// Returns true and saves cookies on success; returns false on any failure so the
    /// caller can fall back to Playwright headless.
    /// </summary>
    private async Task<bool> TrySilentLoginHttpAsync(
        string username, string password, CancellationToken ct)
    {
        try
        {
            var cookieJar = new System.Net.CookieContainer();
            var handler = new System.Net.Http.HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies        = true,
                CookieContainer   = cookieJar,
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0");
            http.DefaultRequestHeaders.TryAddWithoutValidation(
                "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Connection", "keep-alive");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-User", "?1");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");

            SilentLog("[HTTP] Step 1: GET iGracias landing page...");
            var getResp = await http.GetAsync(IgraciasUrl, ct);
            var html    = await getResp.Content.ReadAsStringAsync(ct);
            SilentLog($"[HTTP] Step 1 done, status={getResp.StatusCode}");

            // Reuse hidden form fields from the landing page so the POST matches a browser submit.
            var formData = new Dictionary<string, string>
            {
                ["textUsername"] = username,
                ["textPassword"] = password,
                ["submit"]       = "Login",
            };
            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(
                    html,
                    @"<input[^>]+type=[""']hidden[""'][^>]*>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                var nameM  = System.Text.RegularExpressions.Regex.Match(m.Value, @"name=[""']([^""']+)[""']");
                var valueM = System.Text.RegularExpressions.Regex.Match(m.Value, @"value=[""']([^""']*)[""']");
                if (nameM.Success)
                    formData.TryAdd(nameM.Groups[1].Value, valueM.Success ? valueM.Groups[1].Value : "");
            }

            SilentLog("[HTTP] Step 2: POST login form...");
            var postRequest = new HttpRequestMessage(HttpMethod.Post, IgraciasUrl)
            {
                Content = new FormUrlEncodedContent(formData),
            };
            postRequest.Headers.TryAddWithoutValidation("Referer", IgraciasUrl);
            postRequest.Headers.TryAddWithoutValidation("Origin", "https://igracias.telkomuniversity.ac.id");
            var postResp = await http.SendAsync(postRequest, ct);
            var finalUrl = (postResp.RequestMessage?.RequestUri?.ToString() ?? "").ToLowerInvariant();
            SilentLog($"[HTTP] Step 2 done, status={postResp.StatusCode}, finalUrl={finalUrl}");

            bool success = _successKeywords.Any(k => finalUrl.Contains(k));
            if (!success)
            {
                SilentLog($"[HTTP] FAILED — final URL not a success page: {finalUrl}");
                return false;
            }

            var siteUri = new Uri(IgraciasUrl);
            var dict    = new Dictionary<string, string>();
            foreach (System.Net.Cookie c in cookieJar.GetCookies(siteUri))
            {
                if (_wantedCookies.Contains(c.Name))
                    dict[c.Name] = c.Value;
            }

            SilentLog($"[HTTP] Cookies captured: {string.Join(", ", dict.Keys)}");

            if (!dict.ContainsKey("PHPSESSID"))
            {
                SilentLog("[HTTP] FAILED — PHPSESSID missing from captured cookies.");
                return false;
            }

            await CookieStore.SaveAsync(JsonSerializer.Serialize(dict), ct);
            SilentLog($"[HTTP] SUCCESS — saved {dict.Count} cookies.");
            return true;
        }
        catch (Exception ex)
        {
            SilentLog($"[HTTP] EXCEPTION — {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Headless Playwright fallback for cases where the plain HTTP login path is rejected.
    /// </summary>
    private async Task<bool> TrySilentLoginPlaywrightAsync(
        string username, string password, CancellationToken cancellationToken)
    {
        SilentLog($"=== TrySilentLoginPlaywrightAsync (headless) started for user: {username} ===");
        try
        {
            using var playwright = await Playwright.CreateAsync();

            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Channel  = "msedge",
                Headless = true,
                Timeout  = 20000
            });

            try
            {
                var context = await browser.NewContextAsync();
                var page    = await context.NewPageAsync();

                SilentLog("Step 1: navigating to iGracias…");
                await page.GotoAsync(IgraciasUrl, new PageGotoOptions { Timeout = 20000 });
                SilentLog($"Step 1 done, URL: {page.Url}");

                SilentLog("Step 1b: triggering menu clicks to open login panel…");
                await page.EvaluateAsync("() => document.getElementById('login_button')?.click()");
                await page.WaitForTimeoutAsync(600);
                await page.EvaluateAsync("() => document.getElementById('login_button_general')?.click()");
                await page.WaitForTimeoutAsync(600);

                SilentLog("Step 2: filling credentials via JS…");
                await page.EvaluateAsync(@"([u, p]) => {
                    var uf = document.getElementById('textUsername')
                          || document.querySelector('[name=textUsername],[name=username]');
                    var pf = document.getElementById('textPassword')
                          || document.querySelector('[name=textPassword],[name=password]');
                    if (!uf || !pf) throw new Error('Login fields not found');
                    uf.value = u;
                    pf.value = p;
                }", new[] { username, password });

                SilentLog("Step 3: submitting via button click…");
                var navTask = page.WaitForURLAsync(
                    url => _successKeywords.Any(k => url.Contains(k)),
                    new PageWaitForURLOptions { Timeout = 15000 });
                await page.EvaluateAsync(
                    "() => (document.getElementById('submit')"
                    + " ?? document.querySelector('[name=submit],button[type=submit]'))?.click()");

                try { await navTask; } catch (TimeoutException) { }

                var finalUrl = page.Url.ToLowerInvariant();
                SilentLog($"Step 3 done, final URL: {finalUrl}");

                bool success = _successKeywords.Any(k => finalUrl.Contains(k));
                if (!success)
                {
                    SilentLog("RESULT: FAILED — no success URL indicator after submit.");
                    return false;
                }

                var allCookies = await context.CookiesAsync();
                SilentLog($"Cookies in context: {string.Join(", ", allCookies.Select(c => c.Name))}");

                var cookieDict = allCookies
                    .Where(c => _wantedCookies.Contains(c.Name))
                    .ToDictionary(c => c.Name, c => c.Value);

                if (!cookieDict.ContainsKey("PHPSESSID"))
                {
                    SilentLog("RESULT: FAILED — PHPSESSID missing from context cookies.");
                    return false;
                }

                await CookieStore.SaveAsync(JsonSerializer.Serialize(cookieDict), cancellationToken);
                SilentLog($"RESULT: SUCCESS — saved {cookieDict.Count} cookies: {string.Join(", ", cookieDict.Keys)}");
                return true;
            }
            finally
            {
                await browser.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            SilentLog($"RESULT: EXCEPTION — {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void SilentLog(string msg)
    {
#if DEBUG
        Debug.WriteLine($"[SilentLogin] {msg}");
        DiagnosticLogging.AppendLine(
            AppDataStore.GetFilePath("silent_login.log"),
            $"[{DateTime.Now:HH:mm:ss.fff}] {msg}",
            200_000);
#endif
    }

    /// <summary>Parses an application/x-www-form-urlencoded string into a dictionary.</summary>
    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return result;
        foreach (var part in query.Split('&'))
        {
            var idx = part.IndexOf('=');
            if (idx < 0) continue;
            var key = Uri.UnescapeDataString(part[..idx].Replace('+', ' '));
            var val = Uri.UnescapeDataString(part[(idx + 1)..].Replace('+', ' '));
            result[key] = val;
        }
        return result;
    }

    private async Task TryCaptureStudentIdAsync(IPage page, CancellationToken ct)
    {
        try
        {
            // Preserve an earlier successful capture.
            if (File.Exists(_settingsFile))
            {
                try {
                    var text = await File.ReadAllTextAsync(_settingsFile, ct);
                    if (text.Contains("studentId")) return;
                } catch {}
            }

            var result = await page.EvaluateAsync<string>(@"() => {
                try {
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
