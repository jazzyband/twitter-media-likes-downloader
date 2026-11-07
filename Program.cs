using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace MediaDownloader;

internal static class Program
{
    private const int MaxNoMediaScrolls = 20;
    private static readonly CancellationTokenSource Stop = new();

    [STAThread]
    private static async Task<int> Main()
    {
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Stop.Cancel();
        };

        try
        {
            Console.Clear();
            PrintWelcome();

            var settings = PromptForSetup();
            if (settings is null)
                return 1;

            // Decide whether to use a visible browser BEFORE opening any browser windows
            var previouslySignedIn = PromptForPreviousSignIn();
            if (previouslySignedIn is null)
                return 1;

            var profileDirectory = BrowserSignIn.GetProfileDirectory(settings.BraveExecutable is not null);
            var hasVerifiedSession = BrowserSignIn.HasVerifiedSession(profileDirectory);
            var runHeadless = previouslySignedIn.Value && hasVerifiedSession;

            if (previouslySignedIn.Value && !hasVerifiedSession)
            {
                ConsoleUi.Warning("\nThere is no previously verified Twitter login for this browser profile.");
                Console.WriteLine("Switching to visible mode so you can sign in. The browser will remain visible while downloading.");
            }

            // The initial sign in uses a regular browser so sign in providers can work. Automation then attaches to a separate instance using the same saved profile
            if ((!previouslySignedIn.Value || !hasVerifiedSession) && !BrowserSignIn.OpenRegularBrowserForLogin(settings.BraveExecutable, profileDirectory))
                return 1;

            ChromeDriver? driver = null;
            BrowserAutomationSession? browserSession = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                browserSession = await BrowserSignIn.LaunchAutomationBrowserAsync(settings.BraveExecutable, profileDirectory, runHeadless, Stop.Token);
                if (browserSession is null)
                    return 1;

                var options = new ChromeOptions { DebuggerAddress = browserSession.DebuggerAddress };
                var driverDirectory = Path.GetDirectoryName(settings.ChromeDriverExecutable)!;
                var driverName = Path.GetFileName(settings.ChromeDriverExecutable);
                var service = ChromeDriverService.CreateDefaultService(driverDirectory, driverName);
                service.HideCommandPromptWindow = true;

                try
                {
                    driver = new ChromeDriver(service, options);
                }
                catch
                {
                    browserSession.Dispose();
                    throw;
                }

                try
                {
                    ConsoleUi.Progress("\nChecking saved Twitter login...");
                    driver.Navigate().GoToUrl("https://twitter.com/home");
                    if (await WaitForLoginAsync(driver, TimeSpan.FromSeconds(30)))
                    {
                        BrowserSignIn.MarkSessionVerified(profileDirectory);
                        break;
                    }
                }
                catch
                {
                    driver.Dispose();
                    browserSession.Dispose();
                    throw;
                }

                driver.Dispose();
                driver = null;
                browserSession.Dispose();
                browserSession = null;

                if (attempt == 1)
                {
                    ConsoleUi.Warning("\nTwitter did not recognize a signed-in session in the downloader's profile.");
                    Console.WriteLine("Verify that the login works in the ordinary sign-in browser, then retry.");
                    return 1;
                }

                ConsoleUi.Warning("\nYour Twitter login is missing or expired. Sign in using the ordinary browser window.");
                Console.WriteLine("Switching to visible mode for the remainder of this run.");
                runHeadless = false;
                if (!BrowserSignIn.OpenRegularBrowserForLogin(settings.BraveExecutable, profileDirectory))
                    return 1;
            }

            if (driver is null || browserSession is null)
                return 1;

            var openFolderWhenFinished = false;
            try
            {
                using (browserSession)
                using (driver)
                {
                    ConsoleUi.Success("Login detected! You can now configure your download.");

                    var username = PromptForUsername();
                    if (username is null)
                        return 1;

                    var maxScrolls = PromptForMaxScrolls();
                    var stopTweetId = PromptForStopTweetId();

                    ConsoleUi.Warning("\n== DISCLAIMER ==");
                    Console.WriteLine("Automated access may violate Twitter's terms of service and could affect your account.");
                    Console.WriteLine("Large downloads may require significant storage space and a stable internet connection.");
                    Console.WriteLine(runHeadless ? "The browser is running headless and will remain invisible while downloading." : "The browser will stay visible while scrolling and downloading.");
                    Console.WriteLine("Keep this console open to monitor progress or stop the download.");
                    ConsoleUi.Prompt("\nPress ENTER to start: ");
                    Console.ReadLine();

                    driver.Navigate().GoToUrl($"https://x.com/{username}/likes");
                    await Task.Delay(2000, Stop.Token);

                    ConsoleUi.Progress(runHeadless ? "\nDownloading in headless mode." : "\nDownloading with the browser visible.");

                    using var downloads = new MediaDownloadService();
                    var timer = Stopwatch.StartNew();

                    _ = Task.Run(() =>
                    {
                        Console.WriteLine("Press ENTER anytime to stop downloading...\n");
                        if (Console.ReadLine() is not null)
                            Stop.Cancel();
                    });

                    var result = await ScrollAndDownloadAsync(driver, downloads, settings.DownloadFolder, maxScrolls, stopTweetId);
                    timer.Stop();

                    ConsoleUi.Success($"\nDone! Total media downloaded: {result.Downloaded}/{result.Attempted}");
                    if (result.SkippedTweets > 0)
                        ConsoleUi.Warning($"Skipped {result.SkippedTweets} tweet(s) due to errors.");
                    Console.WriteLine($"Time elapsed: {timer.Elapsed.TotalSeconds:F2} seconds\n");
                    openFolderWhenFinished = true;
                    return 0;
                }
            }
            finally
            {
                if (openFolderWhenFinished)
                    OpenDownloadFolder(settings.DownloadFolder);
            }
        }
        catch (OperationCanceledException) when (Stop.IsCancellationRequested)
        {
            ConsoleUi.Warning("\nStopped.");
            return 0;
        }
        catch (WebDriverException ex)
        {
            ConsoleUi.Error($"\nThe browser could not be started or was closed: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            ConsoleUi.Error($"\nError: {ex.Message}");
            return 1;
        }
    }

    private static void PrintWelcome()
    {
        ConsoleUi.Heading("=== TWITTER MEDIA LIKES DOWNLOADER v1.2 (11/06/26) ===\n");
        Console.WriteLine("      :::    ::: :::    :::     :::     ::::::::: ::::::::: ::::::::::: :::::::::: \n     :+:   :+:  :+:    :+:   :+: :+:        :+:       :+:      :+:     :+:         \n    +:+  +:+   +:+    +:+  +:+   +:+      +:+       +:+       +:+     +:+          \n   +#++:++    +#++:++#++ +#++:++#++:    +#+       +#+        +#+     +#++:++#      \n  +#+  +#+   +#+    +#+ +#+     +#+   +#+       +#+         +#+     +#+            \n #+#   #+#  #+#    #+# #+#     #+#  #+#       #+#          #+#     #+#             \n###    ### ###    ### ###     ### ######### ######### ########### ##########       \n");
        Console.WriteLine("A Chrome/Brave C# tool that bulk-downloads all pieces of media from your Twitter likes.\n");
    }

    private static bool? PromptForPreviousSignIn()
    {
        ConsoleUi.Heading("\nHave you already signed in to Twitter using this downloader before?");
        Console.WriteLine("Y - Download in the background, with no browser window (uses your saved login).");
        Console.WriteLine("N - Sign in with a visible browser and keep the browser visible while downloading.");

        while (true)
        {
            ConsoleUi.Prompt("Enter Y or N (Q to cancel): ");
            var answer = Console.ReadLine()?.Trim();
            if (answer is null || answer.Equals("q", StringComparison.OrdinalIgnoreCase))
                return null;
            if (answer.Equals("y", StringComparison.OrdinalIgnoreCase))
                return true;
            if (answer.Equals("n", StringComparison.OrdinalIgnoreCase))
                return false;
            ConsoleUi.Warning("Please enter Y or N.");
        }
    }

    private static AppSettings? PromptForSetup()
    {
        string? braveExecutable = null;
        ConsoleUi.Heading("To begin, select your browser:");
        Console.WriteLine("1 - Chrome\n2 - Brave");

        while (true)
        {
            ConsoleUi.Prompt("Enter 1 for Chrome or 2 for Brave: ");
            var choice = Console.ReadLine()?.Trim();
            if (choice == "1")
            {
                Console.WriteLine("Using Chrome with its default installation path.");
                break;
            }

            if (choice == "2")
            {
                var detected = FindBraveExecutable();
                if (detected is not null)
                {
                    ConsoleUi.Success($"Brave detected: {detected}");
                    ConsoleUi.Prompt("Press ENTER to use this executable or B to browse for another: ");
                    var answer = Console.ReadLine()?.Trim();
                    if (answer is null)
                        return null;
                    if (!answer.Equals("b", StringComparison.OrdinalIgnoreCase))
                    {
                        braveExecutable = detected;
                        break;
                    }
                }

                Console.WriteLine("Select the Brave executable by entering its path or opening the file picker.");
                braveExecutable = PromptForExecutable("Select Brave Executable", "brave.exe", detected is null ? @"C:\Program Files\BraveSoftware\Brave-Browser\Application" : Path.GetDirectoryName(detected));
                if (braveExecutable is null)
                {
                    ConsoleUi.Warning("No Brave executable selected.");
                    return null;
                }
                break;
            }

            if (choice is null)
                return null;
            ConsoleUi.Warning("Please enter 1 or 2.");
        }

        ConsoleUi.Heading("\nSelect your ChromeDriver executable:");
        var chromeDriver = PromptForExecutable("Select ChromeDriver Executable", "chromedriver.exe");
        if (chromeDriver is null)
        {
            ConsoleUi.Warning("No ChromeDriver executable selected.");
            return null;
        }

        ConsoleUi.Heading("\nSelect the folder where media will be saved:");
        var downloadFolder = PromptForDownloadFolder();
        if (downloadFolder is null)
            return null;

        return new AppSettings(braveExecutable, chromeDriver, downloadFolder);
    }

    private static string? FindBraveExecutable()
    {
        var locations = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        foreach (var directory in locations)
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;

            var executable = Path.Combine(directory, "BraveSoftware", "Brave-Browser", "Application", "brave.exe");
            if (File.Exists(executable))
                return executable;
        }

        return null;
    }

    private static string? PromptForExecutable(string title, string executableName, string? initialDirectory = null)
    {
        while (true)
        {
            ConsoleUi.Prompt($"Enter the full path to {executableName}, press ENTER to browse, or Q to cancel: ");
            var answer = Console.ReadLine()?.Trim().Trim('"');
            if (answer is null || answer.Equals("q", StringComparison.OrdinalIgnoreCase))
                return null;

            if (answer.Length == 0)
            {
                try
                {
                    answer = WindowsDialogs.PickExecutable(title, initialDirectory, executableName);
                }
                catch (Exception ex)
                {
                    ConsoleUi.Warning($"File picker could not open: {ex.Message}");
                    Console.WriteLine("You can still paste the complete executable path here instead.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(answer))
                {
                    ConsoleUi.Warning("No file selected. You can paste the full path or try again.");
                    continue;
                }
            }

            answer = Environment.ExpandEnvironmentVariables(answer);
            if (File.Exists(answer) && Path.GetFileName(answer).Equals(executableName, StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(answer);

            ConsoleUi.Warning($"Please select an existing {executableName} file, not a different executable.");
        }
    }

    private static string? PromptForDownloadFolder()
    {
        while (true)
        {
            ConsoleUi.Prompt("Enter the full download folder path, press ENTER to browse, or Q to cancel: ");
            var answer = Console.ReadLine()?.Trim().Trim('"');
            if (answer is null || answer.Equals("q", StringComparison.OrdinalIgnoreCase))
                return null;

            if (answer.Length == 0)
            {
                try
                {
                    answer = WindowsDialogs.PickFolder("Select Download Folder");
                }
                catch (Exception ex)
                {
                    ConsoleUi.Warning($"Folder picker could not open: {ex.Message}");
                    Console.WriteLine("You can still paste the complete folder path here instead.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(answer))
                {
                    ConsoleUi.Warning("No folder selected. You can paste the full path or try again.");
                    continue;
                }
            }

            try
            {
                var folder = Path.GetFullPath(Environment.ExpandEnvironmentVariables(answer));
                Directory.CreateDirectory(folder);
                return folder;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                ConsoleUi.Error($"Could not use that folder: {ex.Message}");
            }
        }
    }

    private static bool IsLoggedIn(IWebDriver driver)
    {
        // The address alone isn't reliable.. Twitter can briefly show /home before redirecting to login
        return driver.Manage().Cookies.GetCookieNamed("auth_token") is not null;
    }

    private static async Task<bool> WaitForLoginAsync(IWebDriver driver, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!Stop.IsCancellationRequested && stopwatch.Elapsed < timeout)
        {
            try
            {
                if (IsLoggedIn(driver))
                    return true;
            }
            catch (WebDriverException)
            {
                // Page navigation can temporarily invalidate the active page or cookie domain
            }

            await Task.Delay(1000, Stop.Token);
        }
        return false;
    }

    private static string? PromptForUsername()
    {
        while (true)
        {
            ConsoleUi.Prompt("\nLogin detected! Enter your Twitter username (without @): ");
            var input = Console.ReadLine();
            if (input is null)
                return null;

            var username = input.Trim().TrimStart('@');
            if (!Regex.IsMatch(username, @"^[a-zA-Z0-9_]{1,15}$"))
            {
                ConsoleUi.Warning("Please enter a valid username.");
                continue;
            }

            ConsoleUi.Prompt($"Is '{username}' correct? (Y/N): ");
            if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true)
                return username;
        }
    }

    private static int PromptForMaxScrolls()
    {
        while (true)
        {
            ConsoleUi.Prompt("\nMaximum number of scrolls (ENTER for no limit): ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input))
                return 0;
            if (int.TryParse(input, out var count) && count >= 0)
                return count;
            ConsoleUi.Warning("Please enter a non-negative whole number or press ENTER.");
        }
    }

    private static string? PromptForStopTweetId()
    {
        while (true)
        {
            ConsoleUi.Prompt("\nStop scrolling when a specific tweet is reached? (Y/N): ");
            var choice = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (choice == "n" || choice is null)
                return null;
            if (choice != "y")
            {
                ConsoleUi.Warning("Please answer Y or N.");
                continue;
            }

            ConsoleUi.Prompt("Enter the full tweet URL: ");
            var input = Console.ReadLine() ?? string.Empty;
            var match = Regex.Match(input, @"/status/(\d+)");
            if (match.Success)
            {
                ConsoleUi.Success($"Will stop when tweet ID {match.Groups[1].Value} is reached.");
                return match.Groups[1].Value;
            }
            ConsoleUi.Warning("Please enter a valid tweet URL.");
        }
    }

    private static async Task<DownloadResult> ScrollAndDownloadAsync(IWebDriver driver, MediaDownloadService downloads, string downloadFolder, int maxScrolls, string? stopTweetId)
    {
        var seenUrls = new HashSet<string>(StringComparer.Ordinal);
        var downloaded = 0;
        var attempted = 0;
        var skippedTweets = 0;
        var noMediaScrolls = 0;

        for (var scroll = 0; !Stop.IsCancellationRequested && (maxScrolls == 0 || scroll < maxScrolls); scroll++)
        {
            ConsoleUi.Progress(maxScrolls > 0 ? $"Scrolling... ({scroll + 1}/{maxScrolls})\n" : $"Scrolling... ({scroll + 1})\n");

            var mediaThisScroll = 0;
            IReadOnlyCollection<IWebElement> tweets = driver.FindElements(By.XPath("//article[@role='article']"));

            foreach (var tweet in tweets)
            {
                if (Stop.IsCancellationRequested)
                    break;

                try
                {
                    if (stopTweetId is not null && TweetInspector.HasStatusId(tweet, stopTweetId))
                    {
                        ConsoleUi.Success($"\nReached target tweet (ID: {stopTweetId}). Stopping scroll!");
                        Stop.Cancel();
                        break;
                    }

                    if (!TweetInspector.TryGetMetadata(tweet, out var author, out var date, out var tweetId))
                        continue;

                    foreach (var url in TweetInspector.GetImageUrls(tweet))
                    {
                        if (Stop.IsCancellationRequested)
                            break;

                        var fullUrl = MediaUrls.GetFullQualityImageUrl(url);
                        await TryDownloadAsync(fullUrl, "jpg");
                    }

                    foreach (var url in TweetInspector.GetVideoUrls(tweet))
                    {
                        if (Stop.IsCancellationRequested)
                            break;

                        await TryDownloadAsync(url, "mp4");
                    }

                    async Task TryDownloadAsync(string url, string fallbackExtension)
                    {
                        if (!seenUrls.Add(url))
                            return;

                        var filename = MediaUrls.CreateFilename(url, author, date, fallbackExtension, tweetId);
                        if (await downloads.DownloadAsync(url, filename, downloadFolder))
                        {
                            downloaded++;
                            mediaThisScroll++;
                            ConsoleUi.Success($"> Downloaded! [{downloaded}]\n");
                        }
                        attempted++;
                    }
                }
                catch (WebDriverException ex) when (ex is not NoSuchWindowException)
                {
                    ConsoleUi.Error($"Skipped a tweet due to error: {ex.Message}");
                    skippedTweets++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ConsoleUi.Error($"Skipped a tweet due to error: {ex.Message}");
                    skippedTweets++;
                }
            }

            if (Stop.IsCancellationRequested)
            {
                ConsoleUi.Warning("Stop requested! Finishing downloads and exiting.");
                break;
            }

            if (mediaThisScroll == 0)
            {
                noMediaScrolls++;
                Console.WriteLine($"No media found this scroll. ({noMediaScrolls}/{MaxNoMediaScrolls})");
                if (noMediaScrolls >= MaxNoMediaScrolls)
                {
                    ConsoleUi.Warning($"\nReached {MaxNoMediaScrolls} consecutive scrolls without new downloads. Assuming the bottom has been reached.");
                    break;
                }
            }
            else
            {
                noMediaScrolls = 0;
            }

            ((IJavaScriptExecutor)driver).ExecuteScript("window.scrollBy(0, window.innerHeight);");
            await Task.Delay(1500);
        }

        return new DownloadResult(downloaded, attempted, skippedTweets);
    }

    private static void OpenDownloadFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ConsoleUi.Error($"Could not open download folder: {ex.Message}");
        }
    }
}

internal sealed record AppSettings(string? BraveExecutable, string ChromeDriverExecutable, string DownloadFolder);
internal sealed record DownloadResult(int Downloaded, int Attempted, int SkippedTweets);
