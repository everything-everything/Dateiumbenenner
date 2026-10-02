@echo off
echo ====================================================
echo   Hunspell Dictionary Downloader
echo   fuer OCR-Rechtschreibkorrektur
echo ====================================================
echo.

REM Pruefe ob PowerShell verfuegbar ist
where powershell >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo FEHLER: PowerShell nicht gefunden!
    echo Bitte installieren Sie PowerShell oder laden Sie die Woerterbuecher manuell herunter.
    echo Siehe: Dictionaries\README.md
    pause
    exit /b 1
)

echo Starte Download...
echo.

REM Fuehre PowerShell-Skript aus
powershell.exe -ExecutionPolicy Bypass -File "%~dp0Download-Dictionaries.ps1"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Download erfolgreich abgeschlossen!
) else (
    echo.
    echo FEHLER beim Download!
    echo Bitte pruefen Sie Ihre Internetverbindung.
)

pause
