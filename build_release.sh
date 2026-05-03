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

APP_NAME="FFmPlayer.app"
PUBLISH_DIR="publish_output"
APP_DIR="$PUBLISH_DIR/$APP_NAME/Contents"

echo "Running dotnet publish for $RID (Self-Contained)..."
rm -rf "$PUBLISH_DIR"
dotnet publish -c Release -r "$RID" --self-contained true -o "$APP_DIR/MacOS"

if [ $? -eq 0 ]; then
    mkdir -p "$APP_DIR/Resources"
    cat > "$APP_DIR/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>FFmPlayer</string>
    <key>CFBundleDisplayName</key>
    <string>FFmPlayer</string>
    <key>CFBundleIdentifier</key>
    <string>com.satofd.ffmplayer</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>FFmPlayer</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF
    chmod +x "$APP_DIR/MacOS/FFmPlayer"

    echo "Codesigning the app bundle (ad-hoc)..."
    codesign --force --deep --sign - "$PUBLISH_DIR/$APP_NAME"

    echo ""
    echo "Build complete! App bundle created at: FFmPlayer/$PUBLISH_DIR/$APP_NAME"
else
    echo ""
    echo "Build failed!"
    exit 1
fi
