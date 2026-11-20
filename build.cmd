@echo off
rem ============================================================
rem  Lark Sender - build script
rem  Requires: .NET Framework 4.x (csc.exe ships with Windows)
rem  No Visual Studio / .NET SDK needed.
rem
rem  Usage:
rem    build.cmd            Build LarkSender.exe + icon.ico
rem    build.cmd clean      Remove build artifacts
rem    build.cmd test       Build and run unit tests
rem ============================================================
setlocal enabledelayedexpansion

if /I "%~1"=="clean" goto :clean
if /I "%~1"=="test" goto :test

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found. Requires .NET Framework 4.x.
    exit /b 1
)

set HTTPDLL=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Net.Http.dll
if not exist "%HTTPDLL%" set HTTPDLL=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\System.Net.Http.dll
if not exist "%HTTPDLL%" (
    echo [ERROR] System.Net.Http.dll not found.
    exit /b 1
)

set ROOT=%~dp0
set ICON=%ROOT%icon.ico
set EXE=%ROOT%LarkSender.exe
set OBJ=%TEMP%\larksender-obj

if exist "%OBJ%" rmdir /s /q "%OBJ%" >nul 2>&1
mkdir "%OBJ%" >nul 2>&1

rem Build source list: everything under src\ except tests\
set SOURCES=
for /r "%ROOT%src" %%f in (*.cs) do (
    set "FP=%%f"
    set "SKIP="
    if not "!FP:\tests\=!"=="!FP!" set "SKIP=1"
    if not defined SKIP set SOURCES=!SOURCES! "%%f"
)

if "!SOURCES!"=="" (
    echo [ERROR] no .cs files found under src\
    exit /b 1
)

echo Compiler : %CSC%
echo Output   : %EXE%
echo.

rem ---- step 1: generate icon if missing ----
if not exist "%ICON%" (
    echo [1/2] Generating icon.ico ...
    "%CSC%" /nologo /target:exe /out:"%OBJ%\IconGen.exe" /r:System.Drawing.dll "%ROOT%tools\IconGen.cs"
    if errorlevel 1 (
        echo [ERROR] failed to build IconGen
        exit /b 1
    )
    "%OBJ%\IconGen.exe" "%ICON%"
    if errorlevel 1 (
        echo [ERROR] failed to generate icon.ico
        exit /b 1
    )
) else (
    echo [1/2] icon.ico exists, skipping
)

rem ---- step 2: build the app ----
echo [2/2] Building LarkSender.exe ...
"%CSC%" /nologo /target:winexe /optimize+ ^
  /out:"%EXE%" ^
  /win32icon:"%ICON%" ^
  /r:System.dll ^
  /r:System.Core.dll ^
  /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll ^
  /r:"%HTTPDLL%" ^
  !SOURCES!

if errorlevel 1 (
    echo.
    echo [FAILED] compile error
    exit /b 1
)

echo.
echo [OK] %EXE%
exit /b 0

:clean
echo Cleaning...
if exist "%~dp0LarkSender.exe" del /q "%~dp0LarkSender.exe"
if exist "%~dp0icon.ico" del /q "%~dp0icon.ico"
if exist "%~dp0LarkSender.Tests.exe" del /q "%~dp0LarkSender.Tests.exe"
if exist "%TEMP%\larksender-obj" rmdir /s /q "%TEMP%\larksender-obj" >nul 2>&1
echo [OK] done
exit /b 0

:test
call "%~dp0build.cmd"
if errorlevel 1 exit /b 1
call "%~dp0test.cmd"
exit /b %ERRORLEVEL%