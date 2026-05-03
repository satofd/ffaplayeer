#!/bin/bash
echo "========================================="
echo "FFmPlayer - Distribution Packaging Script (macOS)"
echo "========================================="

cd "$(dirname "$0")" || exit

DIST_DIR="FFmPlayer_Release_Mac"
ZIP_FILE="FFmPlayer_Release_Mac.zip"

if [ ! -f "FFmPlayer/publish_output/FFmPlayer" ]; then
    echo "Error: FFmPlayer executable not found. Please run build_release.sh first."
    exit 1
fi

echo "Cleaning up old distribution folders..."
rm -rf "$DIST_DIR"
rm -f "$ZIP_FILE"
mkdir -p "$DIST_DIR"

echo ""
echo "Copying application files..."
cp -a FFmPlayer/publish_output/* "$DIST_DIR/"

# Note: FFmpeg libraries on Mac are typically installed via Homebrew and dynamically linked,
# so we do not package the ffmpeg DLLs for Mac. 
# The user needs `brew install ffmpeg` and `brew install sdl2`.

echo ""
echo "Creating ZIP archive ($ZIP_FILE)..."
zip -r "$ZIP_FILE" "$DIST_DIR"

if [ $? -eq 0 ]; then
    echo ""
    echo "Success! Distribution packaged into $ZIP_FILE"
else
    echo ""
    echo "Failed to create ZIP archive."
    exit 1
fi
