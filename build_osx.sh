#!/usr/bin/env bash
#
# build_osx.sh — build & package Infinity-Beyond (BeyondAgent mod + Avalonia launcher)
# for macOS from a single run on a Mac/Linux host.
#
# Produces two ZIPs in the repo root:
#   BeyondLauncher_macos_<rid>_<timestamp>.zip   — BeyondLauncher.app bundle
#
# The agent DLL (BeyondAgent.dll) is platform-agnostic (netstandard2.1), so the
# same file ships in both packages; only the launcher differs per RID.
#
# It also deploys the freshly built mod into THIS machine's game install (the
# managed dir it prompts for), so a Mac dev can build + test in one step.
#
# Usage:
#   ./build_osx.sh
#   AQWI_GAME_DIR="/path/to/AQW Infinity" ./build_osx.sh   # skip the prompt
#   MAC_RID=osx-x64 ./build_osx.sh                          # force the mac RID
#   TARGETS="mac" ./build_osx.sh                            # only build macOS
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SLN="$ROOT/Beyond/Beyond.sln"
LAUNCHER_CSPROJ="$ROOT/Beyond/Launcher/Launcher.csproj"
BUILD_DIR="$ROOT/Beyond/build"
DEST="$ROOT"

echo "========================================================"
echo " Building Infinity-Beyond Launcher + Mod (macOS)"
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
# macOS RIDs to package. Packages both osx-x64 and osx-arm64 by default.
if [ -n "${MAC_RID:-}" ]; then
    MAC_RIDS="$MAC_RID"
else
    MAC_RIDS="${MAC_RIDS:-osx-x64 osx-arm64}"
fi
TARGETS="${TARGETS:-mac}"

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
echo "Targets        : $TARGETS  (mac RIDs: $MAC_RIDS)"
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
# Self-contained single-file, so the output includes the .NET runtime.
publish_launcher() {
    local rid="$1"
    echo "Publishing launcher for $rid..." >&2
    # DebugType=none / DebugSymbols=false: no .pdb in the shipped publish output.
    "$DOTNET" publish "$LAUNCHER_CSPROJ" -c Release -r "$rid" --self-contained true \
        -p:DebugType=none -p:DebugSymbols=false >&2
    echo "$ROOT/Beyond/Launcher/bin/Release/net10.0/$rid/publish"
}

# --- Helper to generate Info.plist and Icon for macOS app bundle -----------
generate_resources() {
    local app="$1"
    
    # Generate an .icns icon from the PNG if the macOS image tools are available
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

    # Strip macOS extended attributes
    command -v xattr >/dev/null 2>&1 && xattr -cr "$app" 2>/dev/null || true
}

# --- Assembles a single-architecture app bundle at a given path ------------
assemble_app_bundle() {
    local app="$1"
    local publish="$2"
    local build_out="$3"

    echo "Assembling app bundle at $app..."
    rm -rf "$app"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

    # Copy the full publish output (single-file binary + deps.json)
    cp -R "$publish/." "$app/Contents/MacOS/"
    # Drop any .pdb debug symbols
    find "$app/Contents/MacOS" -name '*.pdb' -delete 2>/dev/null || true
    chmod +x "$app/Contents/MacOS/BeyondLauncher"

    # Backfill native dylibs from build output
    local dylib_count=0 dylib
    for dylib in "$build_out"/*.dylib; do
        [ -f "$dylib" ] || continue
        cp "$dylib" "$app/Contents/MacOS/"
        dylib_count=$((dylib_count + 1))
    done

    # Backfill native dylibs from publish folder if any are there
    for dylib in "$publish"/*.dylib; do
        [ -f "$dylib" ] || continue
        if [ ! -f "$app/Contents/MacOS/$(basename "$dylib")" ]; then
            cp "$dylib" "$app/Contents/MacOS/"
            dylib_count=$((dylib_count + 1))
        fi
    done

    # Agent + Harmony mod files next to the launcher.
    [ -f "$BUILD_DIR/BeyondAgent.dll" ] && cp "$BUILD_DIR/BeyondAgent.dll" "$app/Contents/MacOS/"
    [ -f "$BUILD_DIR/0Harmony.dll" ]    && cp "$BUILD_DIR/0Harmony.dll"    "$app/Contents/MacOS/"

    generate_resources "$app"
}

# --- Packages macOS launcher for production in 1 ZIP ------------------------
package_macos_production() {
    local rids=($MAC_RIDS)
    local rid_count=${#rids[@]}

    if [ $rid_count -eq 0 ]; then
        echo "ERROR: No macOS RIDs specified."
        exit 1
    fi

    local publish_dirs=()
    local build_out_dirs=()
    local rid

    for rid in "${rids[@]}"; do
        local pub; pub="$(publish_launcher "$rid")"
        publish_dirs+=("$pub")
        build_out_dirs+=("$ROOT/Beyond/Launcher/bin/Release/net10.0/$rid")
    done

    local zip_name=""
    if [ $rid_count -eq 1 ]; then
        # Only one architecture specified - no merging needed.
        local rid="${rids[0]}"
        local app="$DEST/BeyondLauncher.app"
        assemble_app_bundle "$app" "${publish_dirs[0]}" "${build_out_dirs[0]}"
        
        zip_name="BeyondLauncher_macos_${rid}_${DATETIME}.zip"
        echo "Packaging $zip_name..."
        if command -v ditto >/dev/null 2>&1; then
            ( cd "$DEST" && ditto --norsrc --noextattr -c -k --keepParent "BeyondLauncher.app" "$zip_name" )
        else
            ( cd "$DEST" && zip -qry "$zip_name" "BeyondLauncher.app" )
        fi
        rm -rf "$app"
        PRODUCED+=("$DEST/$zip_name")

    elif command -v lipo >/dev/null 2>&1; then
        # Multiple architectures and lipo is available -> Build universal binary app bundle
        echo "lipo found. Creating universal macOS binary..."
        local app="$DEST/BeyondLauncher.app"
        rm -rf "$app"
        mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

        # 1. Copy the contents of the first RID to initialize structure
        cp -R "${publish_dirs[0]}/." "$app/Contents/MacOS/"
        find "$app/Contents/MacOS" -name '*.pdb' -delete 2>/dev/null || true
        
        # 2. Merge the main executables
        local exe_paths=()
        local pub
        for pub in "${publish_dirs[@]}"; do
            exe_paths+=("$pub/BeyondLauncher")
        done
        lipo -create "${exe_paths[@]}" -output "$app/Contents/MacOS/BeyondLauncher"
        chmod +x "$app/Contents/MacOS/BeyondLauncher"

        # 3. Find and merge native dylibs from all build and publish directories
        local dylib_names=()
        local dir
        for dir in "${build_out_dirs[@]}" "${publish_dirs[@]}"; do
            [ -d "$dir" ] || continue
            local dylib
            for dylib in "$dir"/*.dylib; do
                [ -f "$dylib" ] || continue
                local name; name="$(basename "$dylib")"
                local exists=0
                if [ ${#dylib_names[@]} -gt 0 ]; then
                    local check_name
                    for check_name in "${dylib_names[@]}"; do
                        if [ "$check_name" == "$name" ]; then
                            exists=1
                            break
                        fi
                    done
                fi
                if [ $exists -eq 0 ]; then
                    dylib_names+=("$name")
                fi
            done
        done

        # Merge each dylib
        if [ ${#dylib_names[@]} -gt 0 ]; then
            local name
            for name in "${dylib_names[@]}"; do
                local paths_to_merge=()
                local idx
                for idx in "${!rids[@]}"; do
                    local bdir="${build_out_dirs[$idx]}"
                    local pdir="${publish_dirs[$idx]}"
                    if [ -f "$bdir/$name" ]; then
                        paths_to_merge+=("$bdir/$name")
                    elif [ -f "$pdir/$name" ]; then
                        paths_to_merge+=("$pdir/$name")
                    fi
                done

                if [ ${#paths_to_merge[@]} -eq $rid_count ]; then
                    echo "Merging universal library: $name"
                    lipo -create "${paths_to_merge[@]}" -output "$app/Contents/MacOS/$name"
                else
                    echo "Copying library (not present in all architectures): $name"
                    cp "${paths_to_merge[0]}" "$app/Contents/MacOS/"
                fi
            done
        fi

        # 4. Agent + Harmony mod files
        [ -f "$BUILD_DIR/BeyondAgent.dll" ] && cp "$BUILD_DIR/BeyondAgent.dll" "$app/Contents/MacOS/"
        [ -f "$BUILD_DIR/0Harmony.dll" ]    && cp "$BUILD_DIR/0Harmony.dll"    "$app/Contents/MacOS/"

        # 5. Generate plist and resources
        generate_resources "$app"

        # 6. Package
        zip_name="BeyondLauncher_macos_universal_${DATETIME}.zip"
        echo "Packaging universal ZIP $zip_name..."
        if command -v ditto >/dev/null 2>&1; then
            ( cd "$DEST" && ditto --norsrc --noextattr -c -k --keepParent "BeyondLauncher.app" "$zip_name" )
        else
            ( cd "$DEST" && zip -qry "$zip_name" "BeyondLauncher.app" )
        fi
        rm -rf "$app"
        PRODUCED+=("$DEST/$zip_name")

    else
        # Multiple architectures and lipo is NOT available -> Package both separate apps into 1 ZIP
        echo "lipo not found. Packaging both architecture bundles into a single ZIP..."
        local stage_dir="$DEST/BeyondLauncher_all"
        rm -rf "$stage_dir"
        mkdir -p "$stage_dir"

        local idx
        for idx in "${!rids[@]}"; do
            local rid="${rids[$idx]}"
            local app_name="BeyondLauncher_${rid}.app"
            assemble_app_bundle "$stage_dir/$app_name" "${publish_dirs[$idx]}" "${build_out_dirs[$idx]}"
        done

        zip_name="BeyondLauncher_macos_all_${DATETIME}.zip"
        echo "Packaging $zip_name..."
        if command -v ditto >/dev/null 2>&1; then
            ( cd "$stage_dir" && ditto --norsrc --noextattr -c -k . "$DEST/$zip_name" )
        else
            ( cd "$stage_dir" && zip -qry "$DEST/$zip_name" . )
        fi
        rm -rf "$stage_dir"
        PRODUCED+=("$DEST/$zip_name")
    fi
}

# --- Build the requested packages -----------------------------------------
for target in $TARGETS; do
    case "$target" in
        mac|macos|osx)
            package_macos_production
            ;;
        *) echo "WARNING: unknown target '$target' (expected mac) — skipping." ;;
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
