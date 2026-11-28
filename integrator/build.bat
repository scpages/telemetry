@echo off
setlocal

set OUT=publish

echo Building SCTelemetry-Integrator.exe ...
dotnet publish Integrator.csproj ^
  -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o %OUT%

if errorlevel 1 (
    echo.
    echo Build failed.
    exit /b 1
)

copy /y %OUT%\Integrator.exe %OUT%\SCTelemetry-Integrator.exe >nul

echo.
echo Done: %OUT%\SCTelemetry-Integrator.exe
