#!/bin/bash
echo "========================================="
echo "FFmPlayer - Release Build Script (macOS)"
echo "========================================="

cd "$(dirname "$0")/FFmPlayer" || exit

# Determine architecture
ARCH=$(uname -m)
if [ "$ARCH" = "arm64" ]; then
    RID="osx-arm64"
else
    RID="osx-x64"
fi

echo "Running dotnet publish for $RID (Self-Contained, Single-File)..."
dotnet publish -c Release -r "$RID" --self-contained true -p:PublishSingleFile=true -o publish_output

if [ $? -eq 0 ]; then
    echo ""
    echo "Build complete! Output placed in: FFmPlayer/publish_output"
else
    echo ""
    echo "Build failed!"
    exit 1
fi
