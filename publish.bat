@echo off
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET 8 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)
dotnet publish "%~dp0MediaDownloader.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "%~dp0publish"
if errorlevel 1 (
    echo Publishing failed.
) else (
    echo.
    echo Executable created at: %~dp0publish\MediaDownloader.exe
)
pause
