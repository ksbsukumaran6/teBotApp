@echo off
setlocal enabledelayedexpansion

echo ================================================
echo    TeBot Scratch Quick Development Script
echo ================================================
echo This script rebuilds scratch-vm and starts the development server
echo.

:: Get the current directory
set CURRENT_DIR=%cd%

:: Define paths (assuming scratch-vm and scratch-gui are in parent or sibling directories)
set SCRATCH_VM_PATH=
set SCRATCH_GUI_PATH=

:: Try to locate scratch-vm
if exist "scratch-vm" (
    set SCRATCH_VM_PATH=%CURRENT_DIR%\scratch-vm
) else if exist "..\scratch-vm" (
    set SCRATCH_VM_PATH=%CURRENT_DIR%\..\scratch-vm
) else if exist "..\..\scratch-vm" (
    set SCRATCH_VM_PATH=%CURRENT_DIR%\..\..\scratch-vm
)

:: Try to locate scratch-gui
if exist "scratch-gui" (
    set SCRATCH_GUI_PATH=%CURRENT_DIR%\scratch-gui
) else if exist "..\scratch-gui" (
    set SCRATCH_GUI_PATH=%CURRENT_DIR%\..\scratch-gui
) else if exist "..\..\scratch-gui" (
    set SCRATCH_GUI_PATH=%CURRENT_DIR%\..\..\scratch-gui
)

:: Verify paths exist
if not exist "%SCRATCH_VM_PATH%" (
    echo ERROR: scratch-vm not found. Please run setup-scratch-dev.bat first.
    pause
    exit /b 1
)

if not exist "%SCRATCH_GUI_PATH%" (
    echo ERROR: scratch-gui not found. Please run setup-scratch-dev.bat first.
    pause
    exit /b 1
)

echo Found scratch-vm at: %SCRATCH_VM_PATH%
echo Found scratch-gui at: %SCRATCH_GUI_PATH%
echo.

:: Step 1: Update TeBot extension if it exists
set TEBOT_EXTENSION_SOURCE=%CURRENT_DIR%\scratch-tebot-extension.js
set SCRATCH_VM_EXTENSIONS_DIR=%SCRATCH_VM_PATH%\src\extensions\scratch3_tebot

if exist "%TEBOT_EXTENSION_SOURCE%" (
    echo ================================================
    echo Updating TeBot Extension
    echo ================================================
    
    if not exist "%SCRATCH_VM_EXTENSIONS_DIR%" (
        mkdir "%SCRATCH_VM_EXTENSIONS_DIR%"
    )
    
    echo Copying latest TeBot extension...
    copy "%TEBOT_EXTENSION_SOURCE%" "%SCRATCH_VM_EXTENSIONS_DIR%\index.js"
    if errorlevel 1 (
        echo ERROR: Failed to copy TeBot extension
    ) else (
        echo ✓ TeBot extension updated
    )
    echo.
)

:: Step 2: Build scratch-vm
echo ================================================
echo Building scratch-vm
echo ================================================
cd /d "%SCRATCH_VM_PATH%"

echo Building scratch-vm...
call npm run build
if errorlevel 1 (
    echo ERROR: Build failed for scratch-vm
    pause
    exit /b 1
)

echo ✓ scratch-vm built successfully
echo.

:: Step 3: Start scratch-gui
echo ================================================
echo Starting Scratch GUI Development Server
echo ================================================
cd /d "%SCRATCH_GUI_PATH%"

echo.
echo Starting development server...
echo This will open Scratch in your browser with your TeBot extension.
echo.
echo Press Ctrl+C to stop the server when done.
echo Server: http://localhost:8601
echo.

:: Start the development server
call npm start

:: Return to original directory
cd /d "%CURRENT_DIR%"

echo.
echo Development session ended.
pause
