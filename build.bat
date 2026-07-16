@echo off
setlocal EnableDelayedExpansion

rem ===========================================================================
rem build.bat - build & package Infinity-Beyond (BeyondAgent mod + Avalonia
rem launcher) for BOTH Windows and macOS from a single run on a Windows host.
rem
rem Produces two ZIPs in the repo root:
rem   BeyondLauncher_win-x64_<timestamp>.zip      - BeyondLauncher.exe + deps
rem   BeyondLauncher_macos_<rid>_<timestamp>.zip  - BeyondLauncher.app bundle
rem
rem The agent DLL (BeyondAgent.dll) is platform-agnostic (netstandard2.1), so the
rem same file ships in both packages; only the launcher differs per RID. Cross-
rem publishing is framework-dependent, so the target machine needs the .NET 10
rem runtime installed.
rem
rem NOTE: building the macOS package on Windows cannot set the Unix executable
rem bit on BeyondLauncher (Windows ZIPs carry no POSIX perms). After unzipping on
rem a Mac, run:  chmod +x BeyondLauncher.app/Contents/MacOS/BeyondLauncher
rem (The macOS icon is also skipped - it needs Apple's sips/iconutil.)
rem
rem Overrides:  set WIN_RID / MAC_RID / TARGETS (e.g. set TARGETS=win) before run.
rem ===========================================================================

set ROOT=%~dp0
set SLN=%ROOT%Beyond\Beyond.sln
set LAUNCHER_CSPROJ=%ROOT%Beyond\Launcher\Launcher.csproj
set BUILD_DIR=%ROOT%Beyond\build
set DEST=%ROOT:~0,-1%

if not defined WIN_RID set "WIN_RID=win-x64"
if not defined MAC_RID set "MAC_RID=osx-arm64"
if not defined TARGETS set "TARGETS=win mac"

set "PS="
where powershell >nul 2>&1 && set "PS=powershell"
if not defined PS where pwsh >nul 2>&1 && set "PS=pwsh"
if not defined PS (
    echo ERROR: Neither "powershell" nor "pwsh" was found on PATH.
    echo PowerShell is required to package the build into a ZIP archive.
    pause
    exit /b 1
)

echo ========================================================
echo  Building Infinity-Beyond Launcher + Mod (Windows + macOS)
echo ========================================================
echo.

set "GAME_DIR=%AQWI_GAME_DIR%"
if not defined GAME_DIR (
    echo Enter the path to your AdventureQuest Worlds Infinity install folder.
    echo ^(The folder that contains the game's .exe and its *_Data folder.^)
    set /p "GAME_DIR=Game directory: "
)
set "GAME_DIR=%GAME_DIR:"=%"

if not exist "%GAME_DIR%\" (
    echo.
    echo ERROR: Game directory not found: "%GAME_DIR%"
    pause
    exit /b 1
)

set "MANAGED_DIR="
for /d %%D in ("%GAME_DIR%\*_Data") do (
    if exist "%%D\Managed\Assembly-CSharp.dll" set "MANAGED_DIR=%%D\Managed"
)
if not defined MANAGED_DIR (
    echo.
    echo ERROR: Could not find "*_Data\Managed\Assembly-CSharp.dll" under:
    echo   "%GAME_DIR%"
    echo Point this at the game's install folder.
    pause
    exit /b 1
)

echo Game directory : %GAME_DIR%
echo Managed folder : %MANAGED_DIR%
echo Targets        : %TARGETS%  (win RID: %WIN_RID%, mac RID: %MAC_RID%)
echo.

rem --- Build the solution (mod + launcher) --------------------------------
rem Builds BeyondAgent.dll (+ 0Harmony.dll) into Beyond\build and deploys the mod
rem into the local game; the launcher is published per-RID in the subroutines.
dotnet build "%SLN%" -c Release -p:AqwiGameDir="%GAME_DIR%" -p:AqwiManagedDir="%MANAGED_DIR%"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo BUILD FAILED. Check errors above.
    pause
    exit /b 1
)

rem Timestamp shared by both packages, and the launcher version for Info.plist.
for /f "usebackq tokens=*" %%i in (`%PS% -NoProfile -Command "Get-Date -Format yyyy-MM-dd_HH-mm-ss"`) do set "DATETIME=%%i"
if not defined DATETIME set "DATETIME=build"
rem Pull the launcher <Version> tag; delims=<> splits "<Version>0.2.0</Version>"
rem into tokens, the 3rd being the value. Kept in batch (no PowerShell) so the
rem literal angle brackets can't be mistaken for cmd redirection.
for /f "tokens=3 delims=<>" %%v in ('findstr /I /C:"<Version>" "%LAUNCHER_CSPROJ%"') do set "VERSION=%%v"
if not defined VERSION set "VERSION=0.1.0"

set "PRODUCED="

for %%t in (%TARGETS%) do (
    if /I "%%t"=="win"     call :package_windows "%WIN_RID%"
    if /I "%%t"=="windows" call :package_windows "%WIN_RID%"
    if /I "%%t"=="mac"     call :package_macos "%MAC_RID%"
    if /I "%%t"=="macos"   call :package_macos "%MAC_RID%"
    if /I "%%t"=="osx"     call :package_macos "%MAC_RID%"
    rem A packager exits /b 1 on failure; abort the whole script rather than
    rem falling through to the success summary.
    if errorlevel 1 exit /b 1
)

echo.
echo Standalone Launcher and Mod packaged successfully!
echo.
echo Packages:
for %%p in (%PRODUCED%) do echo   %%p
echo (The BeyondAgent mod was also deployed into: %MANAGED_DIR%)
echo.
echo Closing in 3 seconds...
timeout /t 3 /nobreak >nul
exit /b 0

rem ===========================================================================
rem :publish_launcher <rid>  - framework-dependent single-file publish.
rem ===========================================================================
:publish_launcher
echo.
echo Publishing launcher for %~1...
dotnet publish "%LAUNCHER_CSPROJ%" -c Release -r %~1 --self-contained false
exit /b %ERRORLEVEL%

rem ===========================================================================
rem :package_windows <rid>  - BeyondLauncher.exe + native deps + mod, zipped.
rem ===========================================================================
:package_windows
set "RID=%~1"
set "PUBLISH=%ROOT%Beyond\Launcher\bin\Release\net10.0\%RID%\publish"
set "BUILD_OUT=%ROOT%Beyond\Launcher\bin\Release\net10.0\%RID%"
call :publish_launcher "%RID%"
if errorlevel 1 (
    echo.
    echo PUBLISH FAILED for %RID%. Check errors above.
    pause
    exit /b 1
)
if not exist "%PUBLISH%\BeyondLauncher.exe" (
    echo ERROR: published windows launcher not found at:
    echo   %PUBLISH%\BeyondLauncher.exe
    pause
    exit /b 1
)

echo Assembling Windows package (%RID%)...
set "STAGE=%DEST%\BeyondLauncher"
if exist "%STAGE%" rmdir /S /Q "%STAGE%"
mkdir "%STAGE%"

rem Single-file publish emits BeyondLauncher.exe plus its native libs beside it;
rem copy the whole publish folder, then backfill natives from the build output.
xcopy /E /I /Y "%PUBLISH%\*" "%STAGE%\" >nul
for %%f in (av_libglesv2.dll libHarfBuzzSharp.dll libSkiaSharp.dll) do (
    if exist "%BUILD_OUT%\%%f" copy /Y "%BUILD_OUT%\%%f" "%STAGE%\" >nul
)
if not exist "%STAGE%\libSkiaSharp.dll" (
    echo WARNING: libSkiaSharp.dll not found for %RID% - the Windows launcher will
    echo          crash on startup without it. Check the publish output.
)

if exist "%BUILD_DIR%\BeyondAgent.dll" copy /Y "%BUILD_DIR%\BeyondAgent.dll" "%STAGE%\" >nul
if exist "%BUILD_DIR%\0Harmony.dll"    copy /Y "%BUILD_DIR%\0Harmony.dll"    "%STAGE%\" >nul

set "ZIP_NAME=BeyondLauncher_%RID%_%DATETIME%.zip"
echo Packaging %ZIP_NAME%...
if exist "%DEST%\%ZIP_NAME%" del /F /Q "%DEST%\%ZIP_NAME%"
%PS% -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; try { Compress-Archive -Path '%STAGE%' -DestinationPath '%DEST%\%ZIP_NAME%' -Force; exit 0 } catch { Write-Host ('Compress-Archive failed: ' + $_.Exception.Message); exit 1 }"
if errorlevel 1 (
    echo ERROR: Failed to create %ZIP_NAME%. Staged files left in: %STAGE%
    pause
    exit /b 1
)
rmdir /S /Q "%STAGE%"
set "PRODUCED=%PRODUCED% %DEST%\%ZIP_NAME%"
exit /b 0

rem ===========================================================================
rem :package_macos <rid>  - BeyondLauncher.app bundle + native deps + mod, zipped.
rem ===========================================================================
:package_macos
set "RID=%~1"
set "PUBLISH=%ROOT%Beyond\Launcher\bin\Release\net10.0\%RID%\publish"
set "BUILD_OUT=%ROOT%Beyond\Launcher\bin\Release\net10.0\%RID%"
call :publish_launcher "%RID%"
if errorlevel 1 (
    echo.
    echo PUBLISH FAILED for %RID%. Check errors above.
    pause
    exit /b 1
)
if not exist "%PUBLISH%\BeyondLauncher" (
    echo ERROR: published mac launcher not found at:
    echo   %PUBLISH%\BeyondLauncher
    pause
    exit /b 1
)

echo Assembling BeyondLauncher.app (%RID%)...
set "APP=%DEST%\BeyondLauncher.app"
if exist "%APP%" rmdir /S /Q "%APP%"
mkdir "%APP%\Contents\MacOS"
mkdir "%APP%\Contents\Resources"

rem Full publish output (single-file binary + deps.json), then native .dylibs.
xcopy /E /I /Y "%PUBLISH%\*" "%APP%\Contents\MacOS\" >nul
set "DYLIB_COUNT=0"
for %%f in ("%BUILD_OUT%\*.dylib") do (
    copy /Y "%%f" "%APP%\Contents\MacOS\" >nul
    set /a DYLIB_COUNT+=1
)
if "%DYLIB_COUNT%"=="0" (
    echo ERROR: no native .dylib files found in: %BUILD_OUT%
    echo The launcher will crash on startup without them (missing libSkiaSharp).
    pause
    exit /b 1
)
echo Bundled %DYLIB_COUNT% native .dylib dependencies.

if exist "%BUILD_DIR%\BeyondAgent.dll" copy /Y "%BUILD_DIR%\BeyondAgent.dll" "%APP%\Contents\MacOS\" >nul
if exist "%BUILD_DIR%\0Harmony.dll"    copy /Y "%BUILD_DIR%\0Harmony.dll"    "%APP%\Contents\MacOS\" >nul

rem Info.plist (no icon - .icns needs Apple's sips/iconutil, unavailable here).
rem Written with plain echo lines; ^< ^> escape the XML angle brackets for cmd.
(
echo ^<?xml version="1.0" encoding="UTF-8"?^>
echo ^<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd"^>
echo ^<plist version="1.0"^>
echo ^<dict^>
echo     ^<key^>CFBundleName^</key^>^<string^>Beyond^</string^>
echo     ^<key^>CFBundleDisplayName^</key^>^<string^>Beyond Launcher^</string^>
echo     ^<key^>CFBundleIdentifier^</key^>^<string^>com.retr0gr4d3.beyond^</string^>
echo     ^<key^>CFBundleExecutable^</key^>^<string^>BeyondLauncher^</string^>
echo     ^<key^>CFBundleVersion^</key^>^<string^>%VERSION%^</string^>
echo     ^<key^>CFBundleShortVersionString^</key^>^<string^>%VERSION%^</string^>
echo     ^<key^>CFBundlePackageType^</key^>^<string^>APPL^</string^>
echo     ^<key^>LSMinimumSystemVersion^</key^>^<string^>11.0^</string^>
echo     ^<key^>NSHighResolutionCapable^</key^>^<true/^>
echo ^</dict^>
echo ^</plist^>
)>"%APP%\Contents\Info.plist"

set "ZIP_NAME=BeyondLauncher_macos_%RID%_%DATETIME%.zip"
echo Packaging %ZIP_NAME%...
if exist "%DEST%\%ZIP_NAME%" del /F /Q "%DEST%\%ZIP_NAME%"
%PS% -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; try { Compress-Archive -Path '%APP%' -DestinationPath '%DEST%\%ZIP_NAME%' -Force; exit 0 } catch { Write-Host ('Compress-Archive failed: ' + $_.Exception.Message); exit 1 }"
if errorlevel 1 (
    echo ERROR: Failed to create %ZIP_NAME%. Staged bundle left in: %APP%
    pause
    exit /b 1
)
rmdir /S /Q "%APP%"
set "PRODUCED=%PRODUCED% %DEST%\%ZIP_NAME%"
echo NOTE: on macOS, after unzipping run:  chmod +x BeyondLauncher.app/Contents/MacOS/BeyondLauncher
exit /b 0
