@echo off
setlocal
title Scrawl

cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo Scrawl needs the .NET 8 SDK to run from source.
    echo Install it from https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

dotnet run --project "%~dp0Scrawl.csproj" --configuration Release
if errorlevel 1 pause

endlocal
