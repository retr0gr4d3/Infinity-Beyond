#!/usr/bin/env bash
#
# clean.sh — macOS equivalent of clean.bat.
#
# Removes build outputs, deployed binaries, packaged artifacts, and runtime data
# to return the repository to a pristine, ready-to-build state.
#
# Usage:
#   ./clean.sh
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEST="$ROOT"

echo "========================================================"
echo " Cleaning Infinity-Beyond build outputs and temp files"
echo "========================================================"
echo

# 1. Delete deployed / packaged artifacts in the repo root.
echo "Cleaning root directory deployed artifacts..."
remove() {
    # Remove a path (file, dir, or glob match) and report it if it existed.
    for target in "$@"; do
        if [ -e "$target" ]; then
            rm -rf "$target"
            echo "  - Removed $(basename "$target")"
        fi
    done
}

remove "$DEST"/BeyondLauncher.app
remove "$DEST"/BeyondLauncher.exe
remove "$DEST"/BeyondLauncher.pdb
remove "$DEST"/Beyond.exe
remove "$DEST"/av_libglesv2.dll
remove "$DEST"/libHarfBuzzSharp.dll "$DEST"/libHarfBuzzSharp.dylib
remove "$DEST"/libSkiaSharp.dll "$DEST"/libSkiaSharp.dylib
remove "$DEST"/libAvaloniaNative.dylib
remove "$DEST"/BeyondAgent.dll
remove "$DEST"/0Harmony.dll
remove "$DEST"/*.deps.json
remove "$DEST"/*.runtimeconfig.json
remove "$DEST"/runtimes
remove "$DEST"/BeyondLauncher_*.zip

# 2. Delete build/bin/obj folders recursively.
echo
echo "Cleaning C# build artifacts and directories..."
if [ -d "$ROOT/Beyond/build" ]; then
    rm -rf "$ROOT/Beyond/build"
    echo "  - Removed Beyond/build"
fi
find "$ROOT/Beyond" -type d \( -name bin -o -name obj \) -prune -print -exec rm -rf {} + 2>/dev/null | while read -r d; do
    echo "  - Removed ${d#"$ROOT"/}"
done

# 3. Delete dynamic runtime data.
echo
echo "Cleaning dynamic runtime data..."
remove "$DEST"/UserData
remove "$DEST"/*.log

# 4. Clean IDE temp directories.
echo
echo "Cleaning IDE temporary files..."
remove "$DEST"/.vs

echo
echo "Cleanup complete! Repository is in a pristine, ready-to-build state."
echo
