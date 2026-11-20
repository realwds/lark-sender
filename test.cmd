@echo off
rem ============================================================
rem  Lark Sender - run unit tests
rem  Compiles library code + tests (UI forms excluded), then runs.
rem ============================================================
setlocal enabledelayedexpansion

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found. Requires .NET Framework 4.x.
    exit /b 1
)

set ROOT=%~dp0
set OUT=%ROOT%LarkSender.Tests.exe

set HTTPDLL=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Net.Http.dll
if not exist "%HTTPDLL%" set HTTPDLL=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\System.Net.Http.dll
if not exist "%HTTPDLL%" (
    echo [ERROR] System.Net.Http.dll not found.
    exit /b 1
)

rem Build source list: everything under src\ except Forms\ and src\Program.cs
set SOURCES=
for /r "%ROOT%src" %%f in (*.cs) do (
    set "FP=%%f"
    set "SKIP="
    if not "!FP:\Forms\=!"=="!FP!" set "SKIP=1"
    if not "!FP:\src\Program.cs=!"=="!FP!" set "SKIP=1"
    if not defined SKIP set SOURCES=!SOURCES! "%%f"
)

if "!SOURCES!"=="" (
    echo [ERROR] no source files found
    exit /b 1
)

echo Compiling tests...
"%CSC%" /nologo /target:exe /optimize+ ^
  /out:"%OUT%" ^
  /r:System.dll ^
  /r:System.Core.dll ^
  /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll ^
  /r:"%HTTPDLL%" ^
  !SOURCES!

if errorlevel 1 (
    echo.
    echo [FAILED] test compile error
    exit /b 1
)

echo.
echo Running tests...
echo.
"%OUT%"
set TESTRESULT=%ERRORLEVEL%

if exist "%OUT%" del /q "%OUT%"

exit /b %TESTRESULT%