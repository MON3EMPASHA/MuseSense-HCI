@echo off
setlocal

set "PYTHON_CMD=python"

rem Prefer Python 3.11 via Python Launcher when available (required by TensorFlow 2.15 stack).
where py >nul 2>&1
if not errorlevel 1 (
    py -3.11 -c "import sys" >nul 2>&1
    if not errorlevel 1 (
        set "PYTHON_CMD=py -3.11"
    )
)

for /f %%v in ('%PYTHON_CMD% -c "import sys; print(str(sys.version_info[0]) + \".\" + str(sys.version_info[1]))"') do set "PY_VER=%%v"

if /I not "%PY_VER%"=="3.10" if /I not "%PY_VER%"=="3.11" (
    echo ERROR: Detected Python %PY_VER%.
    echo This project requires Python 3.10 or 3.11 because TensorFlow 2.15 and ml-dtypes 0.2 are pinned in requirements.
    echo Install Python 3.11 and rerun this script. If both are installed, this script will auto-use: py -3.11
    pause
    exit /b 1
)

echo Using interpreter: %PYTHON_CMD% ^(Python %PY_VER%^)

echo [1/3] Creating virtual environment...
call %PYTHON_CMD% -m venv "%~dp0venv"
if errorlevel 1 (
    echo ERROR: Failed to create venv. Make sure a compatible Python version is installed and in PATH.
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
