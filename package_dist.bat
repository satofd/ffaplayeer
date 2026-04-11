@echo off
echo =========================================
echo FFmPlayer - Distribution Packaging Script
echo =========================================

cd /d "%~dp0"
set DIST_DIR=FFmPlayer_Release
set ZIP_FILE=FFmPlayer_Release.zip

if not exist "FFmPlayer\publish_output\FFmPlayer.exe" (
    echo Error: FFmPlayer.exe not found. Please run build_release.bat first.
    pause
    exit /b 1
)

echo Cleaning up old distribution folders...
if exist "%DIST_DIR%" rmdir /s /q "%DIST_DIR%"
if exist "%ZIP_FILE%" del /q "%ZIP_FILE%"
mkdir "%DIST_DIR%"

echo.
echo Copying application files...
xcopy /E /H /Y /Q "FFmPlayer\publish_output\*" "%DIST_DIR%\"

echo Copying FFmpeg runtime libraries...
xcopy /Y /Q "ffmpeg\*.dll" "%DIST_DIR%\"

echo.
echo Creating ZIP archive (%ZIP_FILE%)...
powershell -Command "Compress-Archive -Path '%DIST_DIR%\*' -DestinationPath '%ZIP_FILE%' -Force"

if %ERRORLEVEL% equ 0 (
    echo.
    echo Success! Distribution packaged into %ZIP_FILE%
) else (
    echo.
    echo Failed to create ZIP archive.
)
pause
