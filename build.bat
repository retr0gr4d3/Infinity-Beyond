@echo off
setlocal enabledelayedexpansion

set ROOT=%~dp0
set SLN=%ROOT%Infinity_TestMod.sln
set OUT=%ROOT%Infinity-Beyond\bin\Release
set BUILD=%ROOT%build

echo ========================================
echo  Building Infinity-Beyond Mod
echo ========================================
echo.

dotnet build "%SLN%" -c Release

if errorlevel 1 (
    echo.
    echo BUILD FAILED
    pause
    exit /b 1
)

set "DLL="

for /f "delims=" %%F in ('
    powershell -NoProfile -ExecutionPolicy Bypass ^
    -Command "param($p) Get-ChildItem -LiteralPath $p -Filter 'Beyond_*.dll' -ErrorAction SilentlyContinue ^
              | Sort-Object LastWriteTime -Descending ^
              | Select-Object -First 1 -ExpandProperty Name" ^
    -ArgumentList "%OUT%"
') do (
    set "DLL=%%F"
)

if not defined DLL (
    echo ERROR: No DLL found in output folder
    echo %OUT%
    pause
    exit /b 1
)

if not exist "%BUILD%" mkdir "%BUILD%"

copy /Y "%OUT%\!DLL!" "%BUILD%\" >nul

if errorlevel 1 (
    echo ERROR: Copy failed
    pause
    exit /b 1
)

echo.
echo Build complete.
echo Output: %BUILD%\!DLL!
echo.
timeout /t 2 >nul
exit /b 0