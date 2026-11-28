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

powershell -NoProfile -Command "Get-FileHash '%OUT%\SCTelemetry-Integrator.exe' -Algorithm SHA256 | Select-Object -ExpandProperty Hash" > %OUT%\SCTelemetry-Integrator.exe.sha256

echo.
echo Done: %OUT%\SCTelemetry-Integrator.exe
type %OUT%\SCTelemetry-Integrator.exe.sha256
