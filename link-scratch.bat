@echo off
setlocal enabledelayedexpansion

echo ================================================
echo    TeBot Scratch Link Script
echo ================================================
echo This script just does the linking steps for faster development
echo.

:: Get the current directory
set CURRENT_DIR=%cd%

:: Define paths
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
    echo ERROR: scratch-vm not found.
    pause
    exit /b 1
)

if not exist "%SCRATCH_GUI_PATH%" (
    echo ERROR: scratch-gui not found.
    pause
    exit /b 1
)

echo Found scratch-vm at: %SCRATCH_VM_PATH%
echo Found scratch-gui at: %SCRATCH_GUI_PATH%
echo.

:: Step 1: Link scratch-vm
echo ================================================
echo Linking scratch-vm
echo ================================================
cd /d "%SCRATCH_VM_PATH%"

echo Creating npm link for scratch-vm...
call npm link
if errorlevel 1 (
    echo ERROR: npm link failed for scratch-vm
    pause
    exit /b 1
)

echo ✓ scratch-vm linked successfully
echo.

:: Step 2: Link scratch-vm to scratch-gui
echo ================================================
echo Linking scratch-vm to scratch-gui
echo ================================================
cd /d "%SCRATCH_GUI_PATH%"

echo Linking scratch-vm to scratch-gui...
call npm link scratch-vm
if errorlevel 1 (
    echo ERROR: npm link scratch-vm failed in scratch-gui
    pause
    exit /b 1
)

echo ✓ scratch-gui linked to scratch-vm successfully
echo.

:: Step 3: Start development server
echo ================================================
echo Starting Development Server
echo ================================================

echo.
echo Starting scratch-gui development server...
echo Server: http://localhost:8601
echo.

:: Start the development server
call npm start

:: Return to original directory
cd /d "%CURRENT_DIR%"

echo.
echo Development session ended.
pause
