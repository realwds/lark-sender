@echo off
rem ============================================================
rem  Lark Sender - build script
rem  Requires: .NET Framework 4.x (csc.exe ships with Windows)
rem  No Visual Studio / .NET SDK needed.
rem
rem  Usage:
rem    build.cmd            Build LarkSender.exe + icon.ico
rem    build.cmd clean      Remove build artifacts
rem ============================================================
setlocal

if /I "%~1"=="clean" goto :clean

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

echo Compiler : %CSC%
echo Output   : %EXE%
echo.

rem ---- 1) generate icon if missing ----
if not exist "%ICON%" (
    echo [1/2] Generating icon.ico ...
    "%CSC%" /nologo /target:exe /out:"%TEMP%\lark-icongen.exe" /r:System.Drawing.dll "%ROOT%src\IconGen.cs"
    if errorlevel 1 (
        echo [ERROR] failed to build IconGen
        exit /b 1
    )
    "%TEMP%\lark-icongen.exe" "%ICON%"
    if errorlevel 1 (
        echo [ERROR] failed to generate icon.ico
        exit /b 1
    )
    del /q "%TEMP%\lark-icongen.exe" >nul 2>&1
) else (
    echo [1/2] icon.ico already exists, skipping
)

rem ---- 2) build the app ----
echo [2/2] Building LarkSender.exe ...
"%CSC%" /nologo /target:winexe /optimize+ ^
  /out:"%EXE%" ^
  /win32icon:"%ICON%" ^
  /r:System.dll ^
  /r:System.Core.dll ^
  /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll ^
  /r:System.Web.Extensions.dll ^
  /r:"%HTTPDLL%" ^
  "%ROOT%src\LarkSender.cs"

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
echo [OK] done
exit /b 0