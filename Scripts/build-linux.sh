#!/usr/bin/env bash
#
# Build the HelixToolkit.Nex solution on Linux.
# Uses the repo's Linux-only configurations (LinuxDebug / LinuxRelease) so the
# Windows-only host projects (WPF, WinUI) are skipped automatically. The DirectX
# interop assembly is cross-platform (managed Vortice bindings) and still builds.
#
# Usage:
#   Scripts/build-linux.sh                 # LinuxDebug build
#   Scripts/build-linux.sh -c Release      # LinuxRelease build
#   Scripts/build-linux.sh --clean         # clean before building
#   Scripts/build-linux.sh -- -v minimal   # pass extra args through to `dotnet build`
#
set -euo pipefail

# Resolve paths relative to this script so it works from any CWD.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLUTION="$SCRIPT_DIR/../Source/HelixToolkit-Nex/HelixToolkit.Nex.slnx"

CONFIG="Debug"
CLEAN=0
EXTRA_ARGS=()

while [[ $# -gt 0 ]]; do
    case "$1" in
        -c|--configuration)
            CONFIG="$2"
            shift 2
            ;;
        --clean)
            CLEAN=1
            shift
            ;;
        --)
            shift
            EXTRA_ARGS+=("$@")
            break
            ;;
        -h|--help)
            # Print the leading comment block (skip the shebang, stop at first code line).
            sed -n '2,/^set /{/^#/!q; s/^# \{0,1\}//; p}' "${BASH_SOURCE[0]}"
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            echo "Run with --help for usage." >&2
            exit 1
            ;;
    esac
done

# Accept Debug/Release (convenient) or the full LinuxDebug/LinuxRelease name.
case "$CONFIG" in
    Debug|debug)                CONFIG="LinuxDebug" ;;
    Release|release)            CONFIG="LinuxRelease" ;;
    LinuxDebug|LinuxRelease)    ;;
    *)
        echo "Invalid configuration: $CONFIG (expected Debug, Release, LinuxDebug or LinuxRelease)" >&2
        exit 1
        ;;
esac

echo "==> Solution:      $SOLUTION"
echo "==> Configuration: $CONFIG"

if [[ "$CLEAN" -eq 1 ]]; then
    echo "==> Cleaning..."
    dotnet clean "$SOLUTION" -c "$CONFIG"
fi

echo "==> Restoring..."
dotnet restore "$SOLUTION"

echo "==> Building..."
dotnet build "$SOLUTION" -c "$CONFIG" --no-restore "${EXTRA_ARGS[@]}"

echo "==> Build succeeded ($CONFIG)."
