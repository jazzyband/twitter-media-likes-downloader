# Twitter Media Likes Downloader (v1.105)

A Windows C# console application for downloading images and directly accessible video files from your X/Twitter likes. It supports Chrome and Brave, saved sign-in sessions, visible browsing, and optional headless background downloading. The console stays open to show progress and let you stop a download. Console colors are used sparingly: cyan for prompts and progress, green for successful downloads, yellow for warnings, and red for errors. Most explanatory text stays in the default console color, and redirected output uses plain text.

## Requirements

- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build from source (not needed for the standalone published app)
- [Chrome](https://www.google.com/chrome/) or [Brave](https://brave.com/)
- A compatible [ChromeDriver](https://googlechromelabs.github.io/chrome-for-testing/) executable
- Internet access to restore the Selenium.WebDriver package and download media

## Run

1. Run `start.bat` to restore, build, and start the downloader.
2. Select Chrome or Brave, then select or type the `chromedriver.exe` path and your download folder. Brave is detected automatically in many installations; executable and folder prompts also accept a pasted full path if Windows file dialogs do not work.
3. **Before the browser opens, answer the sign-in question:**
   - **Y — Already signed in using this downloader:** If this browser profile has a verified saved session, the downloader starts **headless** immediately: no browser window appears at all. Scrolling and downloading take place in the background.
   - **N — Not signed in yet, or want to watch the browser:** The app opens a **normal visible sign-in window**. Sign in to X, verify the home page loads, close **only that sign-in window**, and press ENTER. The downloader then starts a new **visible browser window**, which remains visible throughout scrolling and downloading.
4. Enter your X username and download settings (maximum scroll count and optional stopping tweet URL).
5. Press ENTER to start downloading. The chosen visibility mode **does not change mid-run**. In particular, the app no longer hides the browser window after its first scroll.
6. Watch console progress; press ENTER in the console or Ctrl+C to stop. The app opens the download folder after normal completion.

### Saved-login fallback

If you select **Y** but the chosen browser profile does not have a previously verified login, the app explains that a visible sign-in is required, opens a regular sign-in window, and **keeps automation visible for the rest of that run**. If a saved login expires or X does not recognize it, the app offers to sign in visibly again. It does not try to sign in to X through a hidden window.

Saved login data is stored in a dedicated profile at `%LOCALAPPDATA%\MediaDownloader\BrowserProfiles\Chrome` or `%LOCALAPPDATA%\MediaDownloader\BrowserProfiles\Brave`. The app does not use or modify your regular everyday browser profile. Protect the profile folder because it may include authentication cookies.

### Why headless instead of hiding a window?

Earlier builds hid the visible browser window while it was downloading. That can interfere with X's infinite-scroll loading. v1.105 starts Chromium with `--headless=new` and a 1920×1080 viewport **only when you select the already-signed-in background mode**. The original window-hiding code is no longer used. If X behaves differently in headless mode on your computer, rerun and select **N** to download with a visible browser instead.

## Build

Open `MediaDownloader.csproj` in Visual Studio, or run:

```powershell
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

To produce a self-contained Windows executable, use `publish.bat`. It creates `publish\MediaDownloader.exe`.

## Details and troubleshooting

- The first sign-in uses a normal browser **without ChromeDriver** to avoid common sign-in-provider restrictions. Close all windows from **that** dedicated sign-in session before continuing; the browser profile cannot be opened by two different browser processes at once. Do **not** close your normal Chrome or Brave windows.
- The downloader opens its own browser with a local debugging connection, then attaches ChromeDriver to it; ChromeDriver does not launch that browser directly. This is intended to avoid `DevToolsActivePort file doesn't exist` errors seen when opening a saved profile through ChromeDriver.
- Ensure ChromeDriver is compatible with the Chromium version used by your installed Chrome or Brave. An incompatible driver or a profile still held by another browser process can prevent startup.
- Downloads are retried up to three times. JPG files may be retried as PNG when unavailable. Existing files are skipped; filenames include the post date, author, and media identifier.
- The downloader stops after 20 consecutive scrolls without any new media. A maximum scroll count or stopping tweet URL can also limit a run.
- Some videos are streamed in ways that don't expose directly downloadable MP4 URLs, so not all video posts can be captured. GIF posts may be saved as MP4 when a direct video URL is available.
- X's interface and policies can change. Automated access may conflict with X's terms of service.

## What's new in v1.105

- Ask whether you've previously signed in **before any browser is launched**.
- Previously signed-in runs can use headless browsing from startup.
- Visible mode remains visible for the entire download, including scrolling.
- Missing/expired sign-ins use a visible recovery flow.
- Removed the mid-download `ShowWindow(SW_HIDE)` behavior responsible for failed infinite scrolling.
