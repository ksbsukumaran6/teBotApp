@echo off
setlocal enabledelayedexpansion

echo ================================================
echo    TeBot Scratch Development Setup Script
echo ================================================
echo.

:: Get the current directory
set CURRENT_DIR=%cd%

:: Check if we're in the right directory (should contain TeBot folder)
if not exist "TeBot" (
    echo ERROR: TeBot folder not found in current directory.
    echo Please run this script from the directory containing the TeBot project.
    echo Current directory: %CURRENT_DIR%
    pause
    exit /b 1
)

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
) else (
    echo ERROR: scratch-vm directory not found.
    echo Please ensure scratch-vm is in one of these locations:
    echo   - %CURRENT_DIR%\scratch-vm
    echo   - %CURRENT_DIR%\..\scratch-vm  
    echo   - %CURRENT_DIR%\..\..\scratch-vm
    echo.
    echo Or manually set the path below:
    set /p SCRATCH_VM_PATH="Enter full path to scratch-vm: "
)

:: Try to locate scratch-gui
if exist "scratch-gui" (
    set SCRATCH_GUI_PATH=%CURRENT_DIR%\scratch-gui
) else if exist "..\scratch-gui" (
    set SCRATCH_GUI_PATH=%CURRENT_DIR%\..\scratch-gui
) else if exist "..\..\scratch-gui" (
    set SCRATCH_GUI_PATH=%CURRENT_DIR%\..\..\scratch-gui
) else (
    echo ERROR: scratch-gui directory not found.
    echo Please ensure scratch-gui is in one of these locations:
    echo   - %CURRENT_DIR%\scratch-gui
    echo   - %CURRENT_DIR%\..\scratch-gui
    echo   - %CURRENT_DIR%\..\..\scratch-gui
    echo.
    echo Or manually set the path below:
    set /p SCRATCH_GUI_PATH="Enter full path to scratch-gui: "
)

:: Verify paths exist
if not exist "%SCRATCH_VM_PATH%" (
    echo ERROR: scratch-vm path does not exist: %SCRATCH_VM_PATH%
    pause
    exit /b 1
)

if not exist "%SCRATCH_GUI_PATH%" (
    echo ERROR: scratch-gui path does not exist: %SCRATCH_GUI_PATH%
    pause
    exit /b 1
)

echo Found scratch-vm at: %SCRATCH_VM_PATH%
echo Found scratch-gui at: %SCRATCH_GUI_PATH%
echo.

:: Check if Node.js and npm are available
echo Checking Node.js and npm...
node --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: Node.js is not installed or not in PATH
    echo Please install Node.js from https://nodejs.org/
    pause
    exit /b 1
)

npm --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: npm is not available
    echo Please ensure npm is installed with Node.js
    pause
    exit /b 1
)

echo ✓ Node.js and npm are available
echo.

:: Step 1: Build scratch-vm
echo ================================================
echo Step 1: Building scratch-vm
echo ================================================
echo Changing to scratch-vm directory: %SCRATCH_VM_PATH%
cd /d "%SCRATCH_VM_PATH%"

if not exist "package.json" (
    echo ERROR: package.json not found in scratch-vm directory
    echo This doesn't appear to be a valid scratch-vm project
    pause
    exit /b 1
)

echo Installing npm dependencies for scratch-vm...
call npm install
if errorlevel 1 (
    echo ERROR: npm install failed for scratch-vm
    pause
    exit /b 1
)

echo Building scratch-vm...
call npm run build
if errorlevel 1 (
    echo ERROR: npm run build failed for scratch-vm
    pause
    exit /b 1
)

echo Creating npm link for scratch-vm...
call npm link
if errorlevel 1 (
    echo ERROR: npm link failed for scratch-vm
    pause
    exit /b 1
)

echo ✓ scratch-vm built and linked successfully
echo.

:: Step 2: Setup scratch-gui
echo ================================================
echo Step 2: Setting up scratch-gui
echo ================================================
echo Changing to scratch-gui directory: %SCRATCH_GUI_PATH%
cd /d "%SCRATCH_GUI_PATH%"

if not exist "package.json" (
    echo ERROR: package.json not found in scratch-gui directory
    echo This doesn't appear to be a valid scratch-gui project
    pause
    exit /b 1
)

echo Installing npm dependencies for scratch-gui...
call npm install
if errorlevel 1 (
    echo ERROR: npm install failed for scratch-gui
    pause
    exit /b 1
)

echo Linking scratch-vm to scratch-gui...
call npm link scratch-vm
if errorlevel 1 (
    echo ERROR: npm link scratch-vm failed in scratch-gui
    pause
    exit /b 1
)

echo ✓ scratch-gui setup and linked to scratch-vm successfully
echo.

:: Step 3: Copy TeBot extension to scratch-vm
echo ================================================
echo Step 3: Installing TeBot Extension
echo ================================================

set TEBOT_EXTENSION_SOURCE=%CURRENT_DIR%\scratch-tebot-extension.js
set SCRATCH_VM_EXTENSIONS_DIR=%SCRATCH_VM_PATH%\src\extensions\scratch3_tebot

if not exist "%TEBOT_EXTENSION_SOURCE%" (
    echo WARNING: TeBot extension file not found: %TEBOT_EXTENSION_SOURCE%
    echo Skipping extension installation...
) else (
    echo Creating extension directory...
    if not exist "%SCRATCH_VM_EXTENSIONS_DIR%" (
        mkdir "%SCRATCH_VM_EXTENSIONS_DIR%"
    )
    
    echo Copying TeBot extension...
    copy "%TEBOT_EXTENSION_SOURCE%" "%SCRATCH_VM_EXTENSIONS_DIR%\index.js"
    if errorlevel 1 (
        echo ERROR: Failed to copy TeBot extension
    ) else (
        echo ✓ TeBot extension copied to scratch-vm
    )
    
    :: Check if extension registry needs updating
    set EXTENSION_REGISTRY=%SCRATCH_VM_PATH%\src\extension-support\extension-manager.js
    if exist "%EXTENSION_REGISTRY%" (
        echo.
        echo NOTE: You may need to manually register the TeBot extension in:
        echo %EXTENSION_REGISTRY%
        echo.
        echo Add this line to the builtinExtensions array:
        echo     'tebot': () =^> require('../extensions/scratch3_tebot'),
        echo.
    )
)

:: Step 4: Rebuild scratch-vm with extension
echo ================================================
echo Step 4: Rebuilding scratch-vm with extension
echo ================================================
cd /d "%SCRATCH_VM_PATH%"

echo Rebuilding scratch-vm...
call npm run build
if errorlevel 1 (
    echo ERROR: Rebuild failed for scratch-vm
    pause
    exit /b 1
)

echo ✓ scratch-vm rebuilt successfully
echo.

:: Step 5: Start scratch-gui development server
echo ================================================
echo Step 5: Starting Scratch GUI Development Server
echo ================================================
cd /d "%SCRATCH_GUI_PATH%"

echo.
echo Starting scratch-gui development server...
echo This will start the development server and open Scratch in your browser.
echo.
echo Press Ctrl+C to stop the server when you're done developing.
echo.
echo Server will be available at: http://localhost:8601
echo.
pause

:: Start the development server
call npm start

:: If we get here, the server was stopped
echo.
echo ================================================
echo Development server stopped.
echo ================================================
echo.

:: Return to original directory
cd /d "%CURRENT_DIR%"

echo Setup completed. You can run this script again anytime to rebuild and restart.
echo.
pause
