@echo off
rem Publish a self-contained single-file build, gate on a packaged self-test, and package the release zip.
rem NOTE: keep this file ASCII-only (cmd.exe reads .cmd files in the OEM codepage).
setlocal
cd /d "%~dp0"

if "%DOTNET_ROOT%"=="" for /f "delims=" %%d in ('where dotnet.exe 2^>nul') do set "DOTNET_ROOT=%%~dpd"
if "%DOTNET_ROOT%"=="" if exist "%LOCALAPPDATA%\Microsoft\dotnet" set "DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet"

rem Require a clean worktree: the exe embeds the HEAD sha, which must match a commit.
git status --porcelain | findstr /r "." >nul
if not errorlevel 1 (echo ERROR: working tree is dirty - commit before publishing & exit /b 1)
for /f "delims=" %%h in ('git rev-parse --short HEAD') do set HEADSHORT=%%h
echo HEAD: %HEADSHORT%

rem Read the version from the csproj (MSBuild 17.8+)
set APPVER=
for /f "usebackq delims=" %%v in (`dotnet msbuild Numlock.csproj -getProperty:Version -nologo`) do set APPVER=%%v
if "%APPVER%"=="" (echo ERROR: cannot read Version from Numlock.csproj & exit /b 1)

if not exist "docs\*.txt" (echo ERROR: docs\*.txt not found & exit /b 1)
if not exist "LICENSE" (echo ERROR: LICENSE not found & exit /b 1)

if exist publish rmdir /s /q publish
call dotnet publish Numlock.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o publish
if errorlevel 1 (echo. & echo ERROR: dotnet publish failed & exit /b 1)

rem Gate: self-test the packaged exe (needs a desktop session and no running instance).
rem Invoke the exe directly: cmd.exe waits for a GUI application in a batch script (PowerShell's call
rem operator does NOT wait for GUI exes, and Start-Process -ArgumentList does not quote array items,
rem which would split a TEMP path containing spaces and pollute an unrelated directory).
set BUILDDIR=%~dp0.build
set PKGTEST=%BUILDDIR%\selftest
if exist "%BUILDDIR%" rmdir /s /q "%BUILDDIR%"
mkdir "%PKGTEST%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$env:DOTNET_ROOT = '%DOTNET_ROOT%'; $p = Start-Process -FilePath '%~dp0publish\Numlock.exe' -ArgumentList @('--selftest', '%PKGTEST%') -WorkingDirectory '%~dp0' -PassThru -Wait; exit $p.ExitCode"
if errorlevel 1 (echo ERROR: packaged selftest failed - quit any running Numlock first and see "%PKGTEST%\selftest.txt" & exit /b 1)
copy /y "%PKGTEST%\selftest.txt" "Numlock-v%APPVER%-selftest.txt" >nul
if errorlevel 1 (echo ERROR: copy selftest report failed & exit /b 1)

copy /y "docs\*.txt" "publish\" >nul
if errorlevel 1 (echo ERROR: copy docs txt failed & exit /b 1)
copy /y "LICENSE" "publish\LICENSE.txt" >nul
if errorlevel 1 (echo ERROR: copy LICENSE failed & exit /b 1)

rem Stage the payload in a folder named after the software (users extract a clean Numlock\ folder).
set STAGE=%BUILDDIR%\stage
if exist "%STAGE%" rmdir /s /q "%STAGE%"
mkdir "%STAGE%\Numlock"
if errorlevel 1 (echo ERROR: staging folder failed & exit /b 1)
copy /y "publish\Numlock.exe" "%STAGE%\Numlock\" >nul
if errorlevel 1 (echo ERROR: stage exe failed & exit /b 1)
copy /y "publish\*.txt" "%STAGE%\Numlock\" >nul
if errorlevel 1 (echo ERROR: stage docs failed & exit /b 1)

rem Release asset name = plain software name (no version/platform/arch suffixes).
set ZIP=Numlock.zip
if exist "%ZIP%" del "%ZIP%"
powershell -NoProfile -Command "Compress-Archive -Path '%STAGE%\Numlock' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 (echo ERROR: packaging failed & exit /b 1)
if not exist "%ZIP%" (echo ERROR: zip not created & exit /b 1)

if exist "%BUILDDIR%" rmdir /s /q "%BUILDDIR%"

for /f "delims=" %%h in ('powershell -NoProfile -Command "(Get-FileHash -Algorithm SHA256 '%ZIP%').Hash.ToLower()"') do set ZIPHASH=%%h
> "%ZIP%.sha256" echo %ZIPHASH%
echo SHA256: %ZIPHASH%

echo.
echo Done: publish\Numlock.exe, %ZIP% (slim framework-dependent) and %ZIP%.sha256
endlocal
