using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Win32;

namespace MediaDownloader;

internal static class BrowserSignIn
{
    private const string VerifiedSessionFile = ".session-verified";

    public static string GetProfileDirectory(bool useBrave)
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Path.Combine(baseDirectory, "MediaDownloader", "BrowserProfiles", useBrave ? "Brave" : "Chrome");
        Directory.CreateDirectory(profile);
        return profile;
    }

    public static bool HasVerifiedSession(string profileDirectory) =>
        File.Exists(Path.Combine(profileDirectory, VerifiedSessionFile));

    public static void MarkSessionVerified(string profileDirectory)
    {
        try
        {
            File.WriteAllText(Path.Combine(profileDirectory, VerifiedSessionFile),
                "The downloader confirmed a Twitter session in this browser profile.\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static bool OpenRegularBrowserForLogin(string? braveExecutable, string profileDirectory)
    {
        var executable = ResolveBrowserExecutable(braveExecutable);
        if (executable is null)
            return false;

        ConsoleUi.Progress("\nOpening a normal browser window for Twitter sign-in.");
        Console.WriteLine("This browser uses a dedicated profile so your normal browser profile is not changed.");

        try
        {
            using var browser = StartBrowser(executable, profileDirectory, null, "https://x.com/login", headless: false);
            Console.WriteLine("\n1. Sign in to Twitter in the browser.");
            Console.WriteLine("2. Confirm your Twitter home page loads successfully.");
            Console.WriteLine("3. Close all windows opened during this sign-in session.");
            Console.WriteLine("4. Return here and press ENTER to continue.");
            ConsoleUi.Warning("\nDon't close your usual Chrome/Brave windows! Only close the downloader's sign-in window.");

            while (true)
            {
                ConsoleUi.Prompt("Press ENTER when the sign-in window has been CLOSED (or Q to cancel): ");
                var answer = Console.ReadLine();
                if (answer is null || answer.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
                    return false;

                // Chromium may take a moment to flush cookies and fully release the profile
                try
                {
                    if (browser.WaitForExit(8000))
                        return true;
                }
                catch (InvalidOperationException)
                {
                    // A browser launcher can forward to an existing instance and exit early

                    return true;
                }

                ConsoleUi.Warning("That sign-in browser is still running. Close its windows before continuing.");
                Console.WriteLine("If all windows are closed, check Brave/Chrome's background app setting or Task Manager.");
            }
        }
        catch (Exception ex)
        {
            ConsoleUi.Error($"Could not open the browser: {ex.Message}");
            return false;
        }
    }

    public static async Task<BrowserAutomationSession?> LaunchAutomationBrowserAsync(
        string? braveExecutable, string profileDirectory, bool headless, CancellationToken cancellationToken)
    {
        var executable = ResolveBrowserExecutable(braveExecutable);
        if (executable is null)
            return null;

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Process process;
        try
        {
            process = StartBrowser(executable, profileDirectory, port, "https://x.com/home", headless);
        }
        catch (Exception ex)
        {
            ConsoleUi.Error($"Could not open {Path.GetFileName(executable)}: {ex.Message}");
            return null;
        }

        var session = new BrowserAutomationSession(process, port);
        ConsoleUi.Progress(headless
            ? "\nStarting the downloader browser in headless mode and connecting ChromeDriver..."
            : "\nOpening the visible downloader browser and connecting ChromeDriver...");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(25))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var response = await http.GetAsync($"http://127.0.0.1:{port}/json/version", cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync(cancellationToken);
                        if (content.Contains("webSocketDebuggerUrl", StringComparison.Ordinal))
                        {
                            session.ConnectionReady = true;
                            return session;
                        }
                    }
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }

                if (process.HasExited)
                    break;

                await Task.Delay(300, cancellationToken);
            }

            ConsoleUi.Error("\nThe browser opened, but its debugging connection was unavailable.");
            Console.WriteLine("Make sure all windows using the downloader's dedicated browser profile are closed,");
            Console.WriteLine("then restart the application. Do not close your normal browser windows.");
            return null;
        }
        finally
        {
            // After a successful return the process belongs to the caller, so keep it running. A failed or canceled connection must not leave a stray browser behind
            if (!session.ConnectionReady)
                session.Dispose();
        }
    }

    private static Process StartBrowser(string executable, string profileDirectory, int? debugPort, string url, bool headless)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
        };
        start.ArgumentList.Add($"--user-data-dir={profileDirectory}");
        start.ArgumentList.Add("--no-first-run");
        start.ArgumentList.Add("--no-default-browser-check");
        if (debugPort is not null)
        {
            // Headless must be selected at startup. Hiding a visible window mid-run prevents twitter from reliably loading more posts during infinite scrolling
            if (headless)
            {
                start.ArgumentList.Add("--headless=new");
                start.ArgumentList.Add("--window-size=1920,1080");
            }
            start.ArgumentList.Add($"--remote-debugging-port={debugPort.Value}");
            start.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
            start.ArgumentList.Add("--disable-background-timer-throttling");
            start.ArgumentList.Add("--disable-renderer-backgrounding");
            start.ArgumentList.Add("--disable-backgrounding-occluded-windows");
            start.ArgumentList.Add("--disable-features=CalculateNativeWinOcclusion");
        }
        start.ArgumentList.Add("--new-window");
        start.ArgumentList.Add(url);
        return Process.Start(start) ?? throw new InvalidOperationException("Browser process could not be started.");
    }

    private static string? ResolveBrowserExecutable(string? braveExecutable)
    {
        if (braveExecutable is not null)
            return braveExecutable;

        var chrome = FindChromeExecutable();
        if (chrome is not null)
            return chrome;

        ConsoleUi.Warning("Chrome's installation path could not be detected.");
        return PromptForChromeExecutable();
    }

    private static string? PromptForChromeExecutable()
    {
        while (true)
        {
            ConsoleUi.Prompt("Enter the full path to chrome.exe, press ENTER to browse, or Q to cancel: ");
            var answer = Console.ReadLine()?.Trim().Trim('"');
            if (answer is null || answer.Equals("q", StringComparison.OrdinalIgnoreCase))
                return null;
            if (answer.Length == 0)
            {
                try { answer = WindowsDialogs.PickExecutable("Select Chrome Executable", suggestedFileName: "chrome.exe"); }
                catch (Exception ex) { ConsoleUi.Warning($"File picker error: {ex.Message}"); continue; }
            }
            if (string.IsNullOrWhiteSpace(answer))
                continue;
            answer = Environment.ExpandEnvironmentVariables(answer);
            if (File.Exists(answer) && Path.GetFileName(answer).Equals("chrome.exe", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(answer);
            ConsoleUi.Warning("Please select an existing chrome.exe executable.");
        }
    }

    private static string? FindChromeExecutable()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
                if (key?.GetValue(null) is string registered && File.Exists(registered))
                    return registered;
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
        }

        var locations = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };
        return locations.FirstOrDefault(File.Exists);
    }
}

// ChromeDriver is attached to this already running browser, not responsible for launching it
internal sealed class BrowserAutomationSession : IDisposable
{
    private readonly Process process;

    public string DebuggerAddress { get; }
    internal bool ConnectionReady { get; set; }

    public BrowserAutomationSession(Process process, int port)
    {
        this.process = process;
        DebuggerAddress = $"127.0.0.1:{port}";
    }

    public void Dispose()
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally { process.Dispose(); }
    }
}
