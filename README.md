# Twitter Media Likes Downloader (v1.2)

A Windows C# tool ran in Command Prompt that uses ChromeDriver and Selenium to bulk-download all pieces of media from your Twitter likes, in the highest quality possible.

## Requirements

- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build from source (not needed for the standalone published app)
- [Chrome](https://www.google.com/chrome/) or [Brave](https://brave.com/)
- A compatible [ChromeDriver](https://googlechromelabs.github.io/chrome-for-testing/) executable

## Run

1. Run `start.bat`
2. Follow the instructions!

## Build

Open `MediaDownloader.csproj` in Visual Studio, or run:

```powershell
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

To produce a self-contained Windows executable, use `publish.bat`. It creates `publish\MediaDownloader.exe`

## Details/Troubleshooting

- The first sign in uses a normal browser without ChromeDriver to avoid common sign in provider restrictions. Close all windows from that dedicated sign in session before continuing, as the browser profile can't be opened by two different browser processes at once. There's no need to close normal Chrome/Brave windows.
- Ensure ChromeDriver is compatible with the Chromium version used by your installed Chrome or Brave, an incompatible driver or a profile still held by another browser process can prevent startup.
- Downloads are retried up to three times, and JPG files may be retried as PNG when unavailable. Existing files are skipped, and filenames include the post date, author, and media identifier.
- The downloader stops after 20 consecutive scrolls without any new media. A maximum scroll count or stopping tweet URL can also limit a run.
- Some videos are streamed in ways that don't expose directly downloadable MP4 URLs, so not all video posts can be captured. GIF posts may be saved as MP4 when a direct video URL is available.
- Twitter's interface and policies can change. Automated access may conflict with Twitter's TOS.

I mainly made this for myself as I constantly like tweets with images/videos, and I'd like to keep them preserved personally - but I figured maybe there's someone out there who'll use this. If you're that person, I hope this serves you well!