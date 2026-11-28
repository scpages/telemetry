@echo off
setlocal

set OUT=publish

echo =============================================================
echo  SCTelemetry Integrator - Build
echo =============================================================
echo.
echo  NOTE: If the application is currently running, please close
echo  it before continuing, otherwise the build may fail.
echo.
pause

echo Building SCTelemetry-Integrator.exe ...
echo.
dotnet publish Integrator.csproj ^
  -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none ^
  -p:DebugSymbols=false ^
  -o %OUT%

if errorlevel 1 (
    echo.
    echo Build failed.
    echo.
    pause
    exit /b 1
)

move /y %OUT%\Integrator.exe %OUT%\SCTelemetry-Integrator.exe >nul

powershell -NoProfile -Command "Get-FileHash '%OUT%\SCTelemetry-Integrator.exe' -Algorithm SHA256 | Select-Object -ExpandProperty Hash" > %OUT%\SCTelemetry-Integrator.exe.sha256

echo.
echo =============================================================
echo  Build complete!
echo.
echo  Executable:  %~dp0%OUT%\SCTelemetry-Integrator.exe
echo  Checksum:    %~dp0%OUT%\SCTelemetry-Integrator.exe.sha256
echo.
echo  SHA256:
type %OUT%\SCTelemetry-Integrator.exe.sha256
echo.
echo  You can close this window.
echo =============================================================
echo.
pause >nul
