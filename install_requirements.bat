@echo off
echo Installing packages into venv...
call "%~dp0venv\Scripts\pip.exe" install -r "%~dp0requirements.txt"
echo Done.
pause
