@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

rem Die App braucht .NET 10 (TargetFramework net10.0). Systeme mit nur .NET 8
rem reichen nicht. Wir suchen daher ein SDK, das 10.x meldet - zuerst im PATH,
rem dann an bekannten lokalen Orten (Gremium Runde 1, Mitnahme).
set "DOTNET="

for %%C in (
    "dotnet"
    "%LOCALAPPDATA%\Temp\opencode\dotnet10\dotnet.exe"
    "%USERPROFILE%\dotnet\dotnet.exe"
    "%ProgramFiles%\dotnet\dotnet.exe"
    "%ProgramFiles(x86)%\dotnet\dotnet.exe"
) do (
    if not defined DOTNET (
        for /f "usebackq tokens=1 delims= " %%V in (`%%~C --list-sdks 2^>nul`) do (
            if not defined DOTNET (
                for /f "tokens=1 delims=." %%M in ("%%V") do (
                    if "%%M"=="10" set "DOTNET=%%~C"
                )
            )
        )
    )
)

if not defined DOTNET (
    echo Kein .NET 10 SDK gefunden. Bitte .NET 10 installieren oder den
    echo lokalen SDK-Pfad in dieser Datei ergaenzen.
    goto :failed
)

echo Verwende SDK: !DOTNET!
set "APP=src\MaxCube.UI.Desktop\bin\Debug\net10.0\MaxCube.UI.Desktop.dll"

rem Immer inkrementell bauen: eine vorhandene DLL kann veraltet sein.
echo Baue Solution ...
"!DOTNET!" build MaxCube.slnx --nologo -v:minimal
if errorlevel 1 goto :failed

echo Starte MAX! Cube Local App ...
"!DOTNET!" "!APP!" %*
if errorlevel 1 goto :failed
exit /b 0

:failed
echo.
echo Start fehlgeschlagen. Bitte die Ausgabe oben pruefen.
pause
exit /b 1
