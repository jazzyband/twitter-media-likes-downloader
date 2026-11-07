@echo off
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET 8 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)
dotnet run --project "%~dp0MediaDownloader.csproj" --configuration Release
pause
