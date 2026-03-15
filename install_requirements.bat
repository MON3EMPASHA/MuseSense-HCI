@echo off
echo [1/3] Creating virtual environment...
python -m venv "%~dp0venv"
if errorlevel 1 (
    echo ERROR: Failed to create venv. Make sure Python is installed and in PATH.
    pause
    exit /b 1
)

echo [2/3] Upgrading pip...
call "%~dp0venv\Scripts\python.exe" -m pip install --upgrade pip
if errorlevel 1 (
    echo ERROR: Failed to upgrade pip.
    pause
    exit /b 1
)

echo [3/3] Installing packages...
call "%~dp0venv\Scripts\pip.exe" install -r "%~dp0requirements.txt"
if errorlevel 1 (
    echo ERROR: Failed to install packages.
    pause
    exit /b 1
)

echo Done. Run "venv\Scripts\activate" to activate the environment.
pause
