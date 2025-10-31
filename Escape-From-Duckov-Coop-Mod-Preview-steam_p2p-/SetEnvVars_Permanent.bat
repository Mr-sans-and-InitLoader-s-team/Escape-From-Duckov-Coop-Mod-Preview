@echo off
setlocal EnableDelayedExpansion
chcp 65001 >nul

echo ========================================
echo Duckov Mod Environment Setup (Permanent)
echo ========================================
echo.

echo This script will permanently set DUCKOV_GAME_DIRECTORY user environment variable.
echo.
echo Please enter the full path to your game's folder.
echo Example: C:\Steam\steamapps\common\Escape from Duckov
echo.

set /p "DUCKOV_GAME_DIRECTORY=Enter path: "

REM Check if input is empty
if "!DUCKOV_GAME_DIRECTORY!"=="" (
    echo.
    echo [ERROR] Path cannot be empty!
    echo.
    pause
    exit /b 1
)

echo.
echo ========================================
echo Setting environment variable...
echo ========================================
echo.
echo Path to set: !DUCKOV_GAME_DIRECTORY!
echo.
echo Setting permanent user environment variable...

REM Use setx to set user environment variable (no admin rights needed for user variables)
setx DUCKOV_GAME_DIRECTORY "!DUCKOV_GAME_DIRECTORY!"
if !errorLevel! equ 0 (
    echo.
    echo [SUCCESS] DUCKOV_GAME_DIRECTORY set successfully
) else (
    echo.
    echo [FAILED] DUCKOV_GAME_DIRECTORY failed to set
    echo Error code: !errorLevel!
)

echo.
echo ========================================
echo Environment variable set successfully!
echo ========================================
echo.
echo Variable set:
echo   DUCKOV_GAME_DIRECTORY=!DUCKOV_GAME_DIRECTORY!
echo.
echo IMPORTANT:
echo 1. Restart any open programs to load new environment variable
echo 2. Visual Studio must be completely closed and reopened
echo 3. Command windows must be closed and reopened
echo.

pause
endlocal
