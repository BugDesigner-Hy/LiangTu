@echo off
chcp 65001 >nul
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

set "OUT=dist\LiangTu"
set "VER="
for /f "usebackq tokens=2 delims=<>" %%a in (`findstr /c:"<Version>" "src\LiangTu\LiangTu.csproj"`) do (
  set "VER=%%a"
  goto :gotver
)
:gotver
if not defined VER (
  echo [ERROR] could not read Version from src\LiangTu\LiangTu.csproj
  pause
  exit /b 1
)

set "ZIP=dist\LiangTu-win-x64-v%VER%.zip"

echo.
echo === LiangTu package v%VER% ===
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
  echo [ERROR] dotnet not found. Install .NET 8 SDK:
  echo https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)

echo [1/3] Publishing self-contained win-x64 ...
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%" 2>nul

dotnet publish "src\LiangTu\LiangTu.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUT%"
if errorlevel 1 (
  echo [ERROR] publish failed.
  pause
  exit /b 1
)

echo [2/3] Copying readme ...
copy /y "使用说明.txt" "%OUT%\使用说明.txt" >nul
if errorlevel 1 (
  echo [WARN] could not copy 使用说明.txt
)

echo [3/3] Creating versioned zip (keeps older dist\*.zip) ...
if not exist "dist" mkdir "dist"
REM Only replace this version's zip if republishing the same VER; never delete other versions.
if exist "%ZIP%" del /f /q "%ZIP%"

powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 (
  echo [WARN] zip failed. Please zip dist\LiangTu manually.
  explorer "dist\LiangTu"
  pause
  exit /b 0
)

echo.
echo Done.
echo Zip for colleagues:
echo   %CD%\%ZIP%
echo.
echo Kept packages:
dir /b "dist\LiangTu-win-x64-v*.zip" 2>nul
echo.
explorer /select,"%CD%\%ZIP%"
pause
