@echo off
setlocal EnableDelayedExpansion

rem ===========================================================================
rem build_win.bat - build & package Infinity-Beyond (BeyondAgent mod + Avalonia
rem launcher) for Windows.
rem
rem Produces a ZIP in the repo root:
rem   BeyondLauncher_win-x64_<timestamp>.zip      - BeyondLauncher.exe + deps
rem
rem Overrides:  set WIN_RID / TARGETS before run.
rem ===========================================================================

set ROOT=%~dp0
set SLN=%ROOT%Beyond\Beyond.sln
set LAUNCHER_CSPROJ=%ROOT%Beyond\Launcher\Launcher.csproj
set BUILD_DIR=%ROOT%Beyond\build
set DEST=%ROOT:~0,-1%

if not defined WIN_RID set "WIN_RID=win-x64"
if not defined TARGETS set "TARGETS=win"

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
echo  Building Infinity-Beyond Launcher + Mod (Windows)
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
echo Targets        : %TARGETS%  (win RID: %WIN_RID%)
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

rem Timestamp for the package name.
for /f "usebackq tokens=*" %%i in (`%PS% -NoProfile -Command "Get-Date -Format yyyy-MM-dd_HH-mm-ss"`) do set "DATETIME=%%i"
if not defined DATETIME set "DATETIME=build"

set "PRODUCED="

for %%t in (%TARGETS%) do (
    if /I "%%t"=="win"     call :package_windows "%WIN_RID%"
    if /I "%%t"=="windows" call :package_windows "%WIN_RID%"
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
rem DebugType=none / DebugSymbols=false: no .pdb in the shipped publish output.
dotnet publish "%LAUNCHER_CSPROJ%" -c Release -r %~1 --self-contained false -p:DebugType=none -p:DebugSymbols=false
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
rem Drop any .pdb debug symbols (e.g. stale ones from an earlier debug build).
del /S /Q "%STAGE%\*.pdb" >nul 2>&1
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
call :zip_folder "%STAGE%" "%DEST%\%ZIP_NAME%"
if errorlevel 1 (
    echo ERROR: Failed to create %ZIP_NAME%. Staged files left in: %STAGE%
    pause
    exit /b 1
)
rmdir /S /Q "%STAGE%"
set "PRODUCED=%PRODUCED% %DEST%\%ZIP_NAME%"
exit /b 0



rem ===========================================================================
rem :zip_folder <folder_path> <zip_name>  - Zips folder preserving UNIX permissions
rem ===========================================================================
:zip_folder
set "SRC_DIR=%~1"
set "ZIP_OUT=%~2"

where python >nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo Using Python to create UNIX-compatible ZIP...
    (
    echo import os, zipfile, sys
    echo def zip_folder^(folder_path, output_zip^):
    echo     base_dir = os.path.dirname^(folder_path^)
    echo     with zipfile.ZipFile^(output_zip, "w", zipfile.ZIP_DEFLATED^) as zipf:
    echo         for root, dirs, files in os.walk^(folder_path^):
    echo             for d in dirs:
    echo                 dir_path = os.path.join^(root, d^)
    echo                 rel_path = os.path.relpath^(dir_path, base_dir^).replace^("\\", "/"^) + "/"
    echo                 zinfo = zipfile.ZipInfo^(rel_path^)
    echo                 zinfo.create_system = 3
    echo                 zinfo.external_attr = 0o40755 ^<^< 16
    echo                 zipf.writestr^(zinfo, ""^)
    echo             for f in files:
    echo                 file_path = os.path.join^(root, f^)
    echo                 rel_path = os.path.relpath^(file_path, base_dir^).replace^("\\", "/"^)
    echo                 is_exe = ^(f == "BeyondLauncher" and "Contents/MacOS" in rel_path^) or ^(f == "BeyondLauncher.exe"^)
    echo                 zinfo = zipfile.ZipInfo^(rel_path^)
    echo                 zinfo.create_system = 3
    echo                 zinfo.compress_type = zipfile.ZIP_DEFLATED
    echo                 zinfo.external_attr = ^(0o100755 if is_exe else 0o100644^) ^<^< 16
    echo                 with open^(file_path, "rb"^) as fp:
    echo                     zipf.writestr^(zinfo, fp.read^(^)^)
    echo zip_folder^(sys.argv[1], sys.argv[2]^)
    ) > "%TEMP%\beyond_zip.py"
    python "%TEMP%\beyond_zip.py" "%SRC_DIR%" "%ZIP_OUT%"
    set "PY_ERR=!ERRORLEVEL!"
    del "%TEMP%\beyond_zip.py"
    if !PY_ERR! equ 0 exit /b 0
    echo WARNING: Python packaging failed, falling back to PowerShell...
)

echo Using PowerShell to create ZIP (permissions may not be preserved)...
%PS% -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; try { Add-Type -AssemblyName System.IO.Compression.FileSystem; $src='%SRC_DIR%'; $base=(Split-Path -LiteralPath $src -Parent); $zip='%ZIP_OUT%'; $z=[System.IO.Compression.ZipFile]::Open($zip,'Create'); try { foreach ($f in Get-ChildItem -LiteralPath $src -Recurse -File) { $rel=$f.FullName.Substring($base.Length).TrimStart('\','/').Replace('\','/'); [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z,$f.FullName,$rel) } } finally { $z.Dispose() }; exit 0 } catch { Write-Host ('Zip failed: ' + $_.Exception.Message); exit 1 }"
exit /b %ERRORLEVEL%
