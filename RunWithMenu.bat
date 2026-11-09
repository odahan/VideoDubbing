@echo off
setlocal

pushd "%~dp0" || (
    echo Could not open the LocalDub.NET project folder.
    pause
    exit /b 1
)

dotnet run --project src/LocalDub.NET
set "launchExitCode=%errorlevel%"
popd

pause
exit /b %launchExitCode%
