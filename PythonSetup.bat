@echo off
echo Checking dependencies for Decky Loader...
echo.

:: Check if Python is installed
python --version >nul 2>&1
if %errorlevel% neq 0 (
    echo Python is not installed. Downloading Python installer...
    curl -L "https://www.python.org/ftp/python/3.11.8/python-3.11.8-amd64.exe" --output "%temp%\python-installer.exe"
    
    :: Check if download was successful
    if not exist "%temp%\python-installer.exe" (
        echo Failed to download Python installer. Please check your internet connection and try again.
        pause
        exit /b 1
    )
    
    :: Check file size to ensure proper download
    for %%I in ("%temp%\python-installer.exe") do if %%~zI LSS 1000000 (
        echo Downloaded file seems too small. Download may have failed.
        del "%temp%\python-installer.exe"
        pause
        exit /b 1
    )
    
    echo Installing Python...
    "%temp%\python-installer.exe" /quiet InstallAllUsers=1 PrependPath=1 Include_test=0 Include_pip=1
    
    :: Wait a moment for installation to complete
    timeout /t 5 /nobreak > nul
    
    :: Clean up
    del "%temp%\python-installer.exe"
    
    :: Refresh environment variables using PowerShell
    echo Refreshing environment variables...
    powershell -Command "$env:Path = [System.Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path','User')"
    
    :: Also set for current session
    for /f "tokens=*" %%a in ('powershell -Command "[System.Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path','User')"') do set "PATH=%%a"
) else (
    echo Python is already installed.
)

:: Verify installations
echo.
echo Verifying installations...
python --version
if errorlevel 1 (
    echo Python installation may have failed or PATH is not updated.
    echo Please restart your computer and run this script again.
) else (
    echo Python installation successful!
)
echo.
echo Setup complete! You can now run the Decky Loader installer.
pause 