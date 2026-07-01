@echo off
echo === FontAI: Train + Generate ===
echo.

rem Font folder: "fonts" next to this bat file. Drop .ttf/.ttc files there.
set "FONTDIR=%~dp0fonts"
if not exist "%FONTDIR%" (
    echo Creating fonts folder: %FONTDIR%
    mkdir "%FONTDIR%"
    echo.
    echo DROP YOUR .ttf AND .ttc FILES INTO: %FONTDIR%
    echo Then run this bat file again.
    echo.
    pause
    exit /b 0
)

echo [1/2] Training model on %FONTDIR%...
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0train.ps1" -FontDir "%FONTDIR%" -OutDir "%~dp0build-output"
if errorlevel 1 (echo TRAINING FAILED & pause & exit /b 1)
echo.
echo [2/2] Generating font family...
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0generate.ps1" -WeightsFile "%~dp0build-output\FontAiWeights.codex" -OutDir "%~dp0build-output\generated" -Upem 1024
if errorlevel 1 (echo GENERATION FAILED & pause & exit /b 1)
echo.
echo === Done! ===
copy /y "%~dp0build-output\generated\*.ttf" "%~dp0" >nul 2>&1
start "" "%~dp0CodexAI-Regular.ttf"
echo.
echo Generated fonts are next to this bat file.
pause
