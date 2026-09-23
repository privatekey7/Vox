@echo off
REM Build script for Vox: PyInstaller + Inno Setup.
REM
REM Builds in %TEMP%\VoxBuild because PyInstaller fails on paths with non-ASCII
REM characters (this project's source folder contains Cyrillic).
REM
REM Result: vox-setup.exe is copied next to this script.

setlocal EnableDelayedExpansion

set "SRC=%~dp0"
if "%SRC:~-1%"=="\" set "SRC=%SRC:~0,-1%"
set "BUILDDIR=%TEMP%\VoxBuild"
set "VENV=%SRC%\.venv"

REM --- Locate ISCC ---------------------------------------------------------
REM Honor an explicit ISCC env var; otherwise probe common install locations.
if defined ISCC if exist "%ISCC%" goto :iscc_found
for %%P in (
    "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
    "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
    "%ProgramFiles%\Inno Setup 6\ISCC.exe"
) do (
    if exist "%%~P" (
        set "ISCC=%%~P"
        goto :iscc_found
    )
)
where iscc.exe >nul 2>&1 && for /f "delims=" %%P in ('where iscc.exe') do (
    set "ISCC=%%P"
    goto :iscc_found
)
echo [ERROR] Inno Setup compiler (ISCC.exe) not found.
echo         Install Inno Setup 6 from https://jrsoftware.org/isdl.php
echo         Or set the ISCC env var to ISCC.exe path.
exit /b 1
:iscc_found

echo ============================================
echo  Vox build
echo ============================================
echo  Source:    %SRC%
echo  Build dir: %BUILDDIR%
echo  ISCC:      %ISCC%
echo ============================================
echo.

REM --- Ensure venv and dependencies -----------------------------------------
where uv >nul 2>&1
if errorlevel 1 (
    echo [ERROR] uv not found in PATH.
    echo         Install from https://docs.astral.sh/uv/getting-started/installation/
    exit /b 1
)
REM uv sync needs pypi.org to re-resolve packages.  On a flaky DNS / network
REM this fails even when the packages are ALREADY installed in .venv.  So: try to sync, but if it
REM fails, continue as long as the required packages are present in .venv.
echo [0/5] Syncing dependencies (uv sync) ...
set "SYNC_OK=0"
pushd "%SRC%"
uv sync --frozen 2>nul && set "SYNC_OK=1"
if "%SYNC_OK%"=="1" goto :synced
uv sync && set "SYNC_OK=1"
:synced
popd

REM Verify venv exists at all
if not exist "%VENV%\Scripts\python.exe" (
    echo [ERROR] .venv not found. Run "uv sync" once with a working network.
    exit /b 1
)

if "%SYNC_OK%"=="1" goto :deps_ok
echo.
echo [WARN] uv sync failed - likely a network/DNS hiccup to pypi.org.
echo        Checking whether required packages are already in .venv ...
"%VENV%\Scripts\python.exe" -c "import sherpa_onnx" >nul 2>&1
if errorlevel 1 (
    echo [ERROR] uv sync failed AND required packages are missing from .venv.
    echo         Fix network/DNS to pypi.org and re-run build.bat.
    exit /b 1
)
echo [WARN] Network sync skipped - using packages already installed in .venv.
echo.
:deps_ok

REM --- Install PyInstaller inside the venv ----------------------------------
REM PyInstaller MUST run from the project venv so it sees all packages
REM (sherpa-onnx, etc.).  A system-level or user-level
REM pyinstaller uses a different sys.path and misses venv packages.
echo [1/5] Ensuring PyInstaller is in venv ...
"%VENV%\Scripts\python.exe" -c "import PyInstaller" >nul 2>&1
if errorlevel 1 (
    echo       Installing pyinstaller into .venv ...
    pushd "%SRC%"
    uv pip install pyinstaller pyinstaller-hooks-contrib
    if errorlevel 1 (
        echo [ERROR] Could not install pyinstaller in venv.
        popd
        exit /b 1
    )
    popd
)

REM Final check: venv must have pyinstaller
if not exist "%VENV%\Scripts\pyinstaller.exe" (
    echo [ERROR] pyinstaller.exe not found in %VENV%\Scripts
    echo         Run: uv pip install pyinstaller
    exit /b 1
)

REM --- Reset build dir ------------------------------------------------------
if exist "%BUILDDIR%" (
    echo [2/5] Cleaning %BUILDDIR% ...
    rmdir /s /q "%BUILDDIR%"
)
mkdir "%BUILDDIR%" || (echo [ERROR] Cannot create %BUILDDIR% & exit /b 1)

REM --- Stage sources --------------------------------------------------------
echo [3/5] Staging sources ...
copy /y "%SRC%\vox.py"   "%BUILDDIR%\" >nul || (echo [ERROR] copy vox.py & exit /b 1)
copy /y "%SRC%\vox.ico"  "%BUILDDIR%\" >nul || (echo [ERROR] copy vox.ico & exit /b 1)
copy /y "%SRC%\vox.iss"  "%BUILDDIR%\" >nul || (echo [ERROR] copy vox.iss & exit /b 1)
copy /y "%SRC%\vox.spec" "%BUILDDIR%\" >nul || (echo [ERROR] copy vox.spec & exit /b 1)
xcopy /e /i /y /q "%SRC%\voxapp" "%BUILDDIR%\voxapp" >nul || (echo [ERROR] copy voxapp & exit /b 1)

REM --- PyInstaller ----------------------------------------------------------
REM Run pyinstaller from the venv so sys.path includes venv site-packages.
echo [4/5] Running PyInstaller ...
pushd "%BUILDDIR%"
"%VENV%\Scripts\pyinstaller.exe" --noconfirm vox.spec
if errorlevel 1 (
    echo [ERROR] PyInstaller failed.
    popd
    exit /b 1
)
popd

REM --- Inno Setup -----------------------------------------------------------
echo [5/5] Running Inno Setup ...
"%ISCC%" "%BUILDDIR%\vox.iss"
if errorlevel 1 (
    echo [ERROR] Inno Setup failed.
    exit /b 1
)

REM --- Copy artifact back ---------------------------------------------------
copy /y "%BUILDDIR%\installer\vox-setup.exe" "%SRC%\vox-setup.exe" >nul || (
    echo [ERROR] Cannot copy vox-setup.exe back to project.
    exit /b 1
)

echo.
echo ============================================
echo  Build OK
echo ============================================
for %%I in ("%SRC%\vox-setup.exe") do echo   %%~fI  ^(%%~zI bytes^)
echo.
echo  Build dir kept at: %BUILDDIR%
echo  Run "rmdir /s /q %%TEMP%%\VoxBuild" to clean.
echo ============================================

endlocal
exit /b 0
