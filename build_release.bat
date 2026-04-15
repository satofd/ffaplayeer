@echo off
echo =========================================
echo FFmPlayer - Release Build Script
echo =========================================

cd /d "%~dp0FFmPlayer"
echo Running dotnet publish for win-x64 (Self-Contained, Single-File)...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish_output

echo.
if %ERRORLEVEL% equ 0 (
    echo Build complete! Output placed in: FFmPlayer\publish_output
) else (
    echo Build failed!
    pause
)
