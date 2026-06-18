@echo off
setlocal

set ROOT=%~dp0
set DEST=%ROOT:~0,-1%

echo ========================================================
echo  Cleaning Infinity-Beyond build outputs and temp files
echo ========================================================
echo.

:: 1. Delete deployed files in root
echo Cleaning root directory deployed binaries...
if exist "%DEST%\BeyondLauncher.exe" (
    del /F /Q "%DEST%\BeyondLauncher.exe"
    echo   - Deleted BeyondLauncher.exe
)
if exist "%DEST%\BeyondLauncher.pdb" (
    del /F /Q "%DEST%\BeyondLauncher.pdb"
    echo   - Deleted BeyondLauncher.pdb
)
if exist "%DEST%\Beyond.exe" (
    del /F /Q "%DEST%\Beyond.exe"
    echo   - Deleted Beyond.exe
)
if exist "%DEST%\av_libglesv2.dll" (
    del /F /Q "%DEST%\av_libglesv2.dll"
    echo   - Deleted av_libglesv2.dll
)
if exist "%DEST%\libHarfBuzzSharp.dll" (
    del /F /Q "%DEST%\libHarfBuzzSharp.dll"
    echo   - Deleted libHarfBuzzSharp.dll
)
if exist "%DEST%\libSkiaSharp.dll" (
    del /F /Q "%DEST%\libSkiaSharp.dll"
    echo   - Deleted libSkiaSharp.dll
)
if exist "%DEST%\*.deps.json" (
    del /F /Q "%DEST%\*.deps.json"
    echo   - Deleted *.deps.json
)
if exist "%DEST%\*.runtimeconfig.json" (
    del /F /Q "%DEST%\*.runtimeconfig.json"
    echo   - Deleted *.runtimeconfig.json
)
if exist "%DEST%\runtimes" (
    rmdir /S /Q "%DEST%\runtimes"
    echo   - Removed runtimes folder
)

:: 2. Delete build/bin/obj folders recursively
echo.
echo Cleaning C# build artifacts and directories...

if exist "%ROOT%Beyond\build" (
    rmdir /S /Q "%ROOT%Beyond\build"
    echo   - Removed Beyond\build
)

for /d /r "%ROOT%Beyond" %%D in (bin obj) do (
    if exist "%%D" (
        rmdir /S /Q "%%D"
        echo   - Removed "%%D"
    )
)

:: 3. Delete dynamic runtime data
echo.
echo Cleaning dynamic runtime data...
if exist "%DEST%\UserData" (
    rmdir /S /Q "%DEST%\UserData"
    echo   - Removed UserData folder
)
if exist "%DEST%\*.log" (
    del /F /Q "%DEST%\*.log"
    echo   - Deleted local logs
)

:: 4. Clean IDE temp directories
echo.
echo Cleaning IDE temporary files...
if exist "%DEST%\.vs" (
    rmdir /S /Q "%DEST%\.vs"
    echo   - Removed .vs directory
)

echo.
echo Cleanup complete! Repository is in a pristine, ready-to-build state.
echo.
timeout /t 3 /nobreak >nul
exit /b 0
