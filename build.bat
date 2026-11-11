@echo off
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 goto nodotnet
echo Building Sugrob...
dotnet publish Sugrob.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
if errorlevel 1 goto failed
if not exist "publish\Sugrob.exe" goto failed
echo.
echo DONE: %~dp0publish\Sugrob.exe
start "" "%~dp0publish"
pause
exit /b 0

:nodotnet
echo .NET SDK not found. Install .NET 8 SDK x64: https://dotnet.microsoft.com/download/dotnet/8.0
pause
exit /b 1

:failed
echo.
echo BUILD FAILED - send the error text above.
pause
exit /b 1
