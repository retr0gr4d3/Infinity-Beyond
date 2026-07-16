#!/usr/bin/env bash
#
# build.sh — macOS equivalent of build.bat.
#
# Builds the Infinity-Beyond mod (BeyondAgent) and the Avalonia launcher,
# publishes the launcher as a single-file macOS binary, wraps it in a
# BeyondLauncher.app bundle alongside the agent + Harmony DLLs, and packages the
# whole thing into a timestamped ZIP in the repo root.
#
# NOTE: the launcher re-parents the native game window via Win32 and is only
# functional on Windows. On macOS this script still builds and packages it (so
# the code compiles and the mod is produced), but the launcher itself is not
# expected to run here — its main use on a Mac is compiling/deploying the mod.
#
# Usage:
#   ./build.sh
#   AQWI_GAME_DIR="/path/to/AQW Infinity" ./build.sh     # skip the prompt
#   RID=osx-x64 ./build.sh                                # force a target RID
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SLN="$ROOT/Beyond/Beyond.sln"
LAUNCHER_CSPROJ="$ROOT/Beyond/Launcher/Launcher.csproj"
BUILD_DIR="$ROOT/Beyond/build"
DEST="$ROOT"

echo "========================================================"
echo " Building Infinity-Beyond Standalone Launcher and Mod"
echo "========================================================"
echo

# --- Prerequisites --------------------------------------------------------
# Resolve the dotnet CLI. Prefer PATH, then fall back to the standard macOS
# install locations (the official installer drops it in /usr/local/share/dotnet
# and per-user installs land in ~/.dotnet), which aren't always on PATH.
DOTNET="$(command -v dotnet || true)"
if [ -z "$DOTNET" ]; then
    for candidate in "/usr/local/share/dotnet/dotnet" "$HOME/.dotnet/dotnet" "/opt/homebrew/bin/dotnet"; do
        if [ -x "$candidate" ]; then DOTNET="$candidate"; break; fi
    done
fi
if [ -z "$DOTNET" ]; then
    echo "ERROR: the .NET SDK ('dotnet') was not found on your PATH or in the"
    echo "standard install locations. Install the .NET 10 SDK:"
    echo "  https://dotnet.microsoft.com/download"
    exit 1
fi

# --- Target runtime identifier --------------------------------------------
# Default to the host architecture; allow an explicit override via RID.
if [ -z "${RID:-}" ]; then
    case "$(uname -m)" in
        arm64|aarch64) RID="osx-arm64" ;;
        x86_64)        RID="osx-x64" ;;
        *)             echo "ERROR: unsupported architecture '$(uname -m)'. Set RID=osx-arm64 or RID=osx-x64."; exit 1 ;;
    esac
fi
LAUNCHER_PUBLISH="$ROOT/Beyond/Launcher/bin/Release/net10.0/$RID/publish"

# --- Resolve the game directory -------------------------------------------
# The mod is compiled against the game's managed assemblies, so we need the
# install location. Set AQWI_GAME_DIR to skip the prompt.
GAME_DIR="${AQWI_GAME_DIR:-}"
if [ -z "$GAME_DIR" ]; then
    echo "Enter the path to your AdventureQuest Worlds Infinity install folder."
    echo "(The folder — or .app bundle — that contains the game and its *_Data folder.)"
    read -r -p "Game directory: " GAME_DIR
fi
# Normalize whatever the user gave us. Paths get pasted in a variety of forms —
# wrapped in quotes, with a leading ~, or with backslash-escaped spaces from
# drag-and-drop / tab-completion — and `read -r` keeps all of that literal, which
# makes a perfectly valid folder look like it "doesn't exist". Clean it up so the
# common cases just work.
# 1. Trim surrounding whitespace (incl. a stray trailing space from a paste).
GAME_DIR="${GAME_DIR#"${GAME_DIR%%[![:space:]]*}"}"
GAME_DIR="${GAME_DIR%"${GAME_DIR##*[![:space:]]}"}"
# 2. Strip one layer of surrounding single or double quotes.
case "$GAME_DIR" in
    \"*\") GAME_DIR="${GAME_DIR#\"}"; GAME_DIR="${GAME_DIR%\"}" ;;
    \'*\') GAME_DIR="${GAME_DIR#\'}"; GAME_DIR="${GAME_DIR%\'}" ;;
esac
# 3. Remove backslash escapes (e.g. "AQW\ Worlds" -> "AQW Worlds").
GAME_DIR="${GAME_DIR//\\/}"
# 4. Expand a leading ~ to the home directory.
case "$GAME_DIR" in
    "~")   GAME_DIR="$HOME" ;;
    "~/"*) GAME_DIR="$HOME/${GAME_DIR#\~/}" ;;
esac

if [ ! -d "$GAME_DIR" ]; then
    echo
    echo "ERROR: Game directory not found: \"$GAME_DIR\""
    exit 1
fi

# Discover the "<name>_Data/Managed" folder (release-name agnostic). On macOS the
# game may be an .app bundle, so search recursively for Assembly-CSharp.dll under
# a Managed folder rather than assuming a top-level *_Data layout.
MANAGED_DIR="$(find "$GAME_DIR" -type f -name Assembly-CSharp.dll -path '*/Managed/*' 2>/dev/null | head -n 1 | xargs -0 dirname 2>/dev/null || true)"
if [ -z "$MANAGED_DIR" ]; then
    echo
    echo "ERROR: Could not find \"Managed/Assembly-CSharp.dll\" under:"
    echo "  \"$GAME_DIR\""
    echo "Point this at the game's install folder (or .app bundle)."
    exit 1
fi

echo "Game directory : $GAME_DIR"
echo "Managed folder : $MANAGED_DIR"
echo "Target runtime : $RID"
echo

# --- Build the solution (mod + launcher) ----------------------------------
"$DOTNET" build "$SLN" -c Release \
    -p:AqwiGameDir="$GAME_DIR" \
    -p:AqwiManagedDir="$MANAGED_DIR"

echo
echo "Bundling launcher dependencies into single file..."
echo

"$DOTNET" publish "$LAUNCHER_CSPROJ" -c Release -r "$RID" --self-contained false

LAUNCHER_BIN="$LAUNCHER_PUBLISH/BeyondLauncher"
if [ ! -f "$LAUNCHER_BIN" ]; then
    echo "ERROR: Published launcher executable not found at:"
    echo "  $LAUNCHER_BIN"
    exit 1
fi

# --- Assemble the .app bundle ---------------------------------------------
echo
echo "Assembling BeyondLauncher.app..."
echo

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$LAUNCHER_CSPROJ" | head -n 1)"
VERSION="${VERSION:-0.1.0}"

APP="$DEST/BeyondLauncher.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# Copy the full publish output (executable + managed assemblies + deps.json).
cp -R "$LAUNCHER_PUBLISH/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/BeyondLauncher"

# Copy the native .dylib dependencies next to the executable.
#
# A framework-dependent publish with a RID ("dotnet publish -r $RID
# --self-contained false") does NOT copy the RID-specific native assets into
# the publish folder — only the plain "dotnet build" output gets them. Without
# libSkiaSharp/libHarfBuzzSharp/libAvaloniaNative the launcher aborts on the
# first frame (Avalonia's Skia renderer throws in a static initializer, which
# reads as an instant crash on double-click). .NET probes the executable's own
# directory for these, so copy them flat into Contents/MacOS.
LAUNCHER_BUILD_OUT="$ROOT/Beyond/Launcher/bin/Release/net10.0/$RID"
dylib_count=0
for dylib in "$LAUNCHER_BUILD_OUT"/*.dylib; do
    [ -f "$dylib" ] || continue
    cp "$dylib" "$APP/Contents/MacOS/"
    dylib_count=$((dylib_count + 1))
done
if [ "$dylib_count" -eq 0 ]; then
    echo "ERROR: no native .dylib files found in:"
    echo "  $LAUNCHER_BUILD_OUT"
    echo "The launcher will crash on startup without them (missing libSkiaSharp)."
    echo "Make sure the 'dotnet build' step above completed for $RID."
    exit 1
fi
echo "Bundled $dylib_count native .dylib dependencies."

# Copy the agent + Harmony mod files next to the launcher.
[ -f "$BUILD_DIR/BeyondAgent.dll" ] && cp "$BUILD_DIR/BeyondAgent.dll" "$APP/Contents/MacOS/"
[ -f "$BUILD_DIR/0Harmony.dll" ]    && cp "$BUILD_DIR/0Harmony.dll"    "$APP/Contents/MacOS/"

# Generate an .icns icon from the PNG if the macOS image tools are available.
ICON_NAME=""
SRC_PNG="$ROOT/Beyond/Launcher/Assets/Beyond.png"
if [ -f "$SRC_PNG" ] && command -v sips >/dev/null 2>&1 && command -v iconutil >/dev/null 2>&1; then
    ICONSET="$(mktemp -d)/Beyond.iconset"
    mkdir -p "$ICONSET"
    for sz in 16 32 128 256 512; do
        sips -z "$sz" "$sz"       "$SRC_PNG" --out "$ICONSET/icon_${sz}x${sz}.png"       >/dev/null 2>&1 || true
        sips -z $((sz*2)) $((sz*2)) "$SRC_PNG" --out "$ICONSET/icon_${sz}x${sz}@2x.png" >/dev/null 2>&1 || true
    done
    if iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/Beyond.icns" >/dev/null 2>&1; then
        ICON_NAME="Beyond"
    fi
    rm -rf "$(dirname "$ICONSET")"
fi

# Info.plist
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>            <string>Beyond</string>
    <key>CFBundleDisplayName</key>     <string>Beyond Launcher</string>
    <key>CFBundleIdentifier</key>      <string>com.retr0gr4d3.beyond</string>
    <key>CFBundleExecutable</key>      <string>BeyondLauncher</string>
    <key>CFBundleVersion</key>         <string>$VERSION</string>
    <key>CFBundleShortVersionString</key><string>$VERSION</string>
    <key>CFBundlePackageType</key>     <string>APPL</string>
    <key>LSMinimumSystemVersion</key>  <string>11.0</string>
    <key>NSHighResolutionCapable</key> <true/>${ICON_NAME:+
    <key>CFBundleIconFile</key>        <string>$ICON_NAME</string>}
</dict>
</plist>
PLIST

# --- Package into a timestamped ZIP ----------------------------------------
echo "Packaging build into a ZIP archive..."
echo

DATETIME="$(date +%Y-%m-%d_%H-%M-%S)"
ZIP_NAME="BeyondLauncher_${DATETIME}.zip"

# ditto preserves the bundle structure and resource forks correctly.
( cd "$DEST" && ditto -c -k --keepParent "BeyondLauncher.app" "$ZIP_NAME" )

# Leave only the ZIP in the repo root (mirrors build.bat's cleanup).
rm -rf "$APP"

echo
echo "Standalone Launcher and Mod packaged successfully!"
echo
echo "ZIP package location: $DEST/$ZIP_NAME"
echo "(The BeyondAgent mod was also deployed into: $MANAGED_DIR)"
echo
