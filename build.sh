#!/usr/bin/env bash
#
# build.sh — build & package Infinity-Beyond (BeyondAgent mod + Avalonia launcher)
# for BOTH macOS and Windows from a single run on a Mac/Linux host.
#
# Produces two ZIPs in the repo root:
#   BeyondLauncher_macos_<rid>_<timestamp>.zip   — BeyondLauncher.app bundle
#   BeyondLauncher_win-x64_<timestamp>.zip       — BeyondLauncher.exe + deps
#
# The agent DLL (BeyondAgent.dll) is platform-agnostic (netstandard2.1), so the
# same file ships in both packages; only the launcher differs per RID. Cross-
# publishing is framework-dependent, so the target machine needs the .NET 10
# runtime installed.
#
# It also deploys the freshly built mod into THIS machine's game install (the
# managed dir it prompts for), so a Mac dev can build + test in one step.
#
# NOTE: the Windows launcher re-parents the native game window via Win32 and is
# only functional on Windows; the macOS launcher floats the game window over its
# tab panel (see MacEmbed). Both are produced here regardless of host.
#
# Usage:
#   ./build.sh
#   AQWI_GAME_DIR="/path/to/AQW Infinity" ./build.sh   # skip the prompt
#   MAC_RID=osx-x64 ./build.sh                          # force the mac RID
#   WIN_RID=win-x64 ./build.sh                          # force the win RID
#   TARGETS="mac" ./build.sh                            # only build macOS
#   TARGETS="win" ./build.sh                            # only build Windows
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SLN="$ROOT/Beyond/Beyond.sln"
LAUNCHER_CSPROJ="$ROOT/Beyond/Launcher/Launcher.csproj"
BUILD_DIR="$ROOT/Beyond/build"
DEST="$ROOT"

echo "========================================================"
echo " Building Infinity-Beyond Launcher + Mod (macOS + Windows)"
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

# --- Target runtime identifiers -------------------------------------------
# macOS RID defaults to the host architecture; Windows RID defaults to win-x64.
# Both are overridable, and TARGETS selects which packages to produce.
if [ -z "${MAC_RID:-}" ]; then
    case "$(uname -m)" in
        arm64|aarch64) MAC_RID="osx-arm64" ;;
        x86_64)        MAC_RID="osx-x64" ;;
        *)             echo "ERROR: unsupported architecture '$(uname -m)'. Set MAC_RID=osx-arm64 or MAC_RID=osx-x64."; exit 1 ;;
    esac
fi
WIN_RID="${WIN_RID:-win-x64}"
TARGETS="${TARGETS:-mac win}"

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
echo "Targets        : $TARGETS  (mac RID: $MAC_RID, win RID: $WIN_RID)"
echo

# --- Build the solution (mod + launcher) ----------------------------------
# Builds BeyondAgent.dll (+ 0Harmony.dll) into Beyond/build; the launcher is
# published per-RID below.
"$DOTNET" build "$SLN" -c Release \
    -p:AqwiGameDir="$GAME_DIR" \
    -p:AqwiManagedDir="$MANAGED_DIR"

DATETIME="$(date +%Y-%m-%d_%H-%M-%S)"
PRODUCED=()

# Publish the launcher for a RID and echo its publish dir (on stdout; all build
# chatter goes to stderr so command substitution captures only the path).
# Framework-dependent single-file, so the output is one BeyondLauncher[.exe]
# plus native libs.
publish_launcher() {
    local rid="$1"
    echo "Publishing launcher for $rid..." >&2
    "$DOTNET" publish "$LAUNCHER_CSPROJ" -c Release -r "$rid" --self-contained false >&2
    echo "$ROOT/Beyond/Launcher/bin/Release/net10.0/$rid/publish"
}

# --- macOS package: BeyondLauncher.app ------------------------------------
package_macos() {
    local rid="$1"
    local publish build_out app
    publish="$(publish_launcher "$rid")"
    build_out="$ROOT/Beyond/Launcher/bin/Release/net10.0/$rid"

    local bin="$publish/BeyondLauncher"
    if [ ! -f "$bin" ]; then
        echo "ERROR: published mac launcher not found at: $bin"
        exit 1
    fi

    echo "Assembling BeyondLauncher.app ($rid)..."
    app="$DEST/BeyondLauncher.app"
    rm -rf "$app"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

    # Copy the full publish output (single-file binary + deps.json), then the
    # native .dylib deps. A framework-dependent RID publish does NOT copy the
    # RID-specific native assets into the publish folder — only the plain build
    # output gets them. Without libSkiaSharp/libHarfBuzzSharp/libAvaloniaNative
    # the launcher aborts on the first frame (Skia throws in a static init, which
    # reads as an instant crash on double-click). .NET probes the executable's
    # own directory for these, so copy them flat into Contents/MacOS.
    cp -R "$publish/." "$app/Contents/MacOS/"
    chmod +x "$app/Contents/MacOS/BeyondLauncher"

    local dylib_count=0 dylib
    for dylib in "$build_out"/*.dylib; do
        [ -f "$dylib" ] || continue
        cp "$dylib" "$app/Contents/MacOS/"
        dylib_count=$((dylib_count + 1))
    done
    if [ "$dylib_count" -eq 0 ]; then
        echo "ERROR: no native .dylib files found in: $build_out"
        echo "The launcher will crash on startup without them (missing libSkiaSharp)."
        exit 1
    fi
    echo "Bundled $dylib_count native .dylib dependencies."

    # Agent + Harmony mod files next to the launcher.
    [ -f "$BUILD_DIR/BeyondAgent.dll" ] && cp "$BUILD_DIR/BeyondAgent.dll" "$app/Contents/MacOS/"
    [ -f "$BUILD_DIR/0Harmony.dll" ]    && cp "$BUILD_DIR/0Harmony.dll"    "$app/Contents/MacOS/"

    # Generate an .icns icon from the PNG if the macOS image tools are available
    # (they aren't on Linux — the icon is optional, the app runs without it).
    local icon_name="" src_png="$ROOT/Beyond/Launcher/Assets/Beyond.png"
    if [ -f "$src_png" ] && command -v sips >/dev/null 2>&1 && command -v iconutil >/dev/null 2>&1; then
        local iconset; iconset="$(mktemp -d)/Beyond.iconset"
        mkdir -p "$iconset"
        local sz
        for sz in 16 32 128 256 512; do
            sips -z "$sz" "$sz"         "$src_png" --out "$iconset/icon_${sz}x${sz}.png"    >/dev/null 2>&1 || true
            sips -z $((sz*2)) $((sz*2)) "$src_png" --out "$iconset/icon_${sz}x${sz}@2x.png" >/dev/null 2>&1 || true
        done
        if iconutil -c icns "$iconset" -o "$app/Contents/Resources/Beyond.icns" >/dev/null 2>&1; then
            icon_name="Beyond"
        fi
        rm -rf "$(dirname "$iconset")"
    fi

    local version
    version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$LAUNCHER_CSPROJ" | head -n 1)"
    version="${version:-0.1.0}"

    cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>            <string>Beyond</string>
    <key>CFBundleDisplayName</key>     <string>Beyond Launcher</string>
    <key>CFBundleIdentifier</key>      <string>com.retr0gr4d3.beyond</string>
    <key>CFBundleExecutable</key>      <string>BeyondLauncher</string>
    <key>CFBundleVersion</key>         <string>$version</string>
    <key>CFBundleShortVersionString</key><string>$version</string>
    <key>CFBundlePackageType</key>     <string>APPL</string>
    <key>LSMinimumSystemVersion</key>  <string>11.0</string>
    <key>NSHighResolutionCapable</key> <true/>${icon_name:+
    <key>CFBundleIconFile</key>        <string>$icon_name</string>}
</dict>
</plist>
PLIST

    # Strip macOS extended attributes (com.apple.provenance/quarantine, etc.) so
    # they don't end up as ._AppleDouble sidecar files inside the archive.
    command -v xattr >/dev/null 2>&1 && xattr -cr "$app" 2>/dev/null || true

    local zip_name="BeyondLauncher_macos_${rid}_${DATETIME}.zip"
    echo "Packaging $zip_name..."
    if command -v ditto >/dev/null 2>&1; then
        # ditto preserves the bundle structure and the executable bit correctly;
        # --norsrc --noextattr keep resource forks / xattrs out of the zip.
        ( cd "$DEST" && ditto --norsrc --noextattr -c -k --keepParent "BeyondLauncher.app" "$zip_name" )
    else
        # Linux fallback: zip preserves the +x bit we set above.
        ( cd "$DEST" && zip -qry "$zip_name" "BeyondLauncher.app" )
    fi
    rm -rf "$app"
    PRODUCED+=("$DEST/$zip_name")
}

# --- Windows package: BeyondLauncher.exe + deps ---------------------------
package_windows() {
    local rid="$1"
    local publish build_out stage
    publish="$(publish_launcher "$rid")"
    build_out="$ROOT/Beyond/Launcher/bin/Release/net10.0/$rid"

    if [ ! -f "$publish/BeyondLauncher.exe" ]; then
        echo "ERROR: published windows launcher not found at: $publish/BeyondLauncher.exe"
        exit 1
    fi

    echo "Assembling Windows package ($rid)..."
    stage="$DEST/BeyondLauncher"
    rm -rf "$stage"
    mkdir -p "$stage"

    # Single-file publish emits BeyondLauncher.exe plus its native libs beside it
    # (native libs aren't embedded by default). Copy the whole publish folder, then
    # backfill the native dlls from the plain build output in case this host's
    # publish left them there instead.
    cp -R "$publish/." "$stage/"
    local dll
    for dll in av_libglesv2.dll libHarfBuzzSharp.dll libSkiaSharp.dll; do
        [ -f "$build_out/$dll" ] && cp "$build_out/$dll" "$stage/"
    done
    if [ ! -f "$stage/libSkiaSharp.dll" ]; then
        echo "WARNING: libSkiaSharp.dll not found for $rid — the Windows launcher"
        echo "         will crash on startup without it. Check the publish output."
    fi

    # Agent + Harmony mod files.
    [ -f "$BUILD_DIR/BeyondAgent.dll" ] && cp "$BUILD_DIR/BeyondAgent.dll" "$stage/"
    [ -f "$BUILD_DIR/0Harmony.dll" ]    && cp "$BUILD_DIR/0Harmony.dll"    "$stage/"

    # Strip macOS xattrs so they don't become ._AppleDouble noise on Windows.
    command -v xattr >/dev/null 2>&1 && xattr -cr "$stage" 2>/dev/null || true

    local zip_name="BeyondLauncher_${rid}_${DATETIME}.zip"
    echo "Packaging $zip_name..."
    if command -v ditto >/dev/null 2>&1; then
        ( cd "$DEST" && ditto --norsrc --noextattr -c -k --keepParent "BeyondLauncher" "$zip_name" )
    else
        ( cd "$DEST" && zip -qry "$zip_name" "BeyondLauncher" )
    fi
    rm -rf "$stage"
    PRODUCED+=("$DEST/$zip_name")
}

# --- Build the requested packages -----------------------------------------
for target in $TARGETS; do
    case "$target" in
        mac|macos|osx) package_macos "$MAC_RID" ;;
        win|windows)   package_windows "$WIN_RID" ;;
        *) echo "WARNING: unknown target '$target' (expected mac|win) — skipping." ;;
    esac
done

# --- Deploy the mod into THIS machine's game install ----------------------
# So a Mac dev can build + test in one step. Platform-agnostic agent DLL, so it
# works regardless of which packages were produced above.
echo
echo "Deploying mod into local game: $MANAGED_DIR"
[ -f "$BUILD_DIR/BeyondAgent.dll" ] && cp "$BUILD_DIR/BeyondAgent.dll" "$MANAGED_DIR/"
[ -f "$BUILD_DIR/0Harmony.dll" ]    && cp "$BUILD_DIR/0Harmony.dll"    "$MANAGED_DIR/"

echo
echo "Standalone Launcher and Mod packaged successfully!"
echo
echo "Packages:"
for p in "${PRODUCED[@]}"; do
    echo "  $p"
done
echo "(The BeyondAgent mod was also deployed into: $MANAGED_DIR)"
echo
