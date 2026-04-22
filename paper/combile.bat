@echo off
setlocal

REM Usage:
REM   paper\combile.bat                  -> compiles paper\paper.tex
REM   paper\combile.bat paper\other.tex -> compiles paper\other.tex

set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..") do set "REPO_DIR=%%~fI"

set "TEXFILE=%~1"
if "%TEXFILE%"=="" set "TEXFILE=paper\paper.tex"
set "OUTDIR=paper"

pushd "%REPO_DIR%" >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Could not switch to repository directory: %REPO_DIR%
    exit /b 1
)

if not exist "%TEXFILE%" (
    echo [ERROR] File not found: %TEXFILE%
    popd
    exit /b 1
)

echo [INFO] Compiling %TEXFILE% with output directory "%OUTDIR%"

where latexmk >nul 2>nul
if not errorlevel 1 (
    echo [INFO] Using latexmk...
    latexmk -pdf -interaction=nonstopmode -synctex=1 -outdir="%OUTDIR%" "%TEXFILE%"
    if errorlevel 1 (
        echo [ERROR] latexmk failed.
        popd
        exit /b 1
    )
) else (
    echo [INFO] latexmk not found. Falling back to pdflatex ^(3 passes^)...
    pdflatex -interaction=nonstopmode -output-directory="%OUTDIR%" "%TEXFILE%"
    if errorlevel 1 (
        popd
        exit /b 1
    )

    pdflatex -interaction=nonstopmode -output-directory="%OUTDIR%" "%TEXFILE%"
    if errorlevel 1 (
        popd
        exit /b 1
    )

    pdflatex -interaction=nonstopmode -output-directory="%OUTDIR%" "%TEXFILE%"
    if errorlevel 1 (
        popd
        exit /b 1
    )
)


for %%F in (
    "paper.aux"
    "paper.log"
    "paper.fls"
    "paper.fdb_latexmk"
    "paper.synctex.gz"
    "paper.toc"
    "paper.out"
    "paper.lof"
    "paper.lot"
    @REM "paper.bbl"
    "paper.blg"
    "paper.xdv"
    "paper.fls"
    "paper.log"
) do if exist "%%~fF" del /q "%%~fF"

echo [SUCCESS] PDF build completed.
popd
endlocal
start paper.pdf
exit /b 0
