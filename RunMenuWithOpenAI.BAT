@echo off
setlocal

pushd "%~dp0" || (
    echo Could not open the LocalDub project folder.
    pause
    exit /b 1
)

dotnet run --project "srcOpenAI\LocalDub.OpenAI.csproj" -- %*
set "launchExitCode=%errorlevel%"
popd

if not "%launchExitCode%"=="0" pause
exit /b %launchExitCode%
