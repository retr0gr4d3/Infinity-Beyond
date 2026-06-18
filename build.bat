@echo off
setlocal

set ROOT=%~dp0
set SLN=%ROOT%Beyond\Beyond.sln
set LAUNCHER_PUBLISH=%ROOT%Beyond\Launcher\bin\Release\net10.0\win-x64\publish
set DEST=%ROOT:~0,-1%

echo ========================================================
echo  Building Infinity-Beyond Standalone Launcher and Mod
echo ========================================================
echo.

:: --- Resolve the game directory (needed to compile the mod against the game's
::     managed assemblies). Set AQWI_GAME_DIR to skip the prompt. -------------
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

:: Discover the "<name>_Data\Managed" folder by pattern (release-name agnostic).
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
echo.

dotnet build "%SLN%" -c Release -p:AqwiGameDir="%GAME_DIR%" -p:AqwiManagedDir="%MANAGED_DIR%"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo BUILD FAILED. Check errors above.
    pause
    exit /b 1
)

echo.
echo Bundling launcher dependencies into single file...
echo.

dotnet publish "%ROOT%Beyond\Launcher\Launcher.csproj" -c Release -r win-x64 --self-contained false

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo PUBLISH FAILED. Check errors above.
    pause
    exit /b 1
)

echo.
echo Deploying launcher...
echo.

if not exist "%LAUNCHER_PUBLISH%\BeyondLauncher.exe" (
    echo WARNING: Published launcher executable not found at:
    echo %LAUNCHER_PUBLISH%\BeyondLauncher.exe
    pause
    exit /b 1
)

:: Clean up old dll/pdb/json clutter from root folder
del /Q "%DEST%\*.dll" >nul 2>&1
del /Q "%DEST%\*.pdb" >nul 2>&1
del /Q "%DEST%\*.json" >nul 2>&1
del /Q "%DEST%\Beyond.exe" >nul 2>&1
del /Q "%DEST%\BeyondLauncher.exe" >nul 2>&1
if exist "%DEST%\runtimes" rmdir /S /Q "%DEST%\runtimes"

:: Copy published files from publish dir
copy /Y "%LAUNCHER_PUBLISH%\BeyondLauncher.exe" "%DEST%\" >nul
if exist "%LAUNCHER_PUBLISH%\BeyondLauncher.pdb" copy /Y "%LAUNCHER_PUBLISH%\BeyondLauncher.pdb" "%DEST%\" >nul

:: Copy native dependencies directly to root
set NATIVE_SRC=%ROOT%Beyond\Launcher\bin\Release\net10.0\win-x64
if exist "%NATIVE_SRC%\libSkiaSharp.dll" (
    copy /Y "%NATIVE_SRC%\av_libglesv2.dll" "%DEST%\" >nul
    copy /Y "%NATIVE_SRC%\libHarfBuzzSharp.dll" "%DEST%\" >nul
    copy /Y "%NATIVE_SRC%\libSkiaSharp.dll" "%DEST%\" >nul
)

echo.
echo Standalone Launcher and Mod deployed successfully!
echo.
echo Launcher location: %DEST%\BeyondLauncher.exe
echo.
echo Closing in 3 seconds...
timeout /t 3 /nobreak
exit /b 0
