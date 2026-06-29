#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PACKAGES_DIR="$SCRIPT_DIR/packages"
REMOTE_BASE="git+ssh://git@github.com/rubickanov-org/unity-packages.git?path=packages"

# --- Usage ---

usage() {
    echo "Usage: $(basename "$0") <project-path> [local|remote|status]"
    echo ""
    echo "  local    Switch to local file: references (for development)"
    echo "  remote   Switch to git remote references (for release)"
    echo "  status   Show current state (default if omitted)"
    echo ""
    echo "Examples:"
    echo "  $(basename "$0") ../time-hunters-v2 local"
    echo "  $(basename "$0") ../time-hunters-v2 remote"
    echo "  $(basename "$0") ../time-hunters-v2"
    exit 1
}

if [[ $# -lt 1 ]]; then
    usage
fi

PROJECT_DIR="$(cd "$1" && pwd)"
ACTION="${2:-status}"
MANIFEST="$PROJECT_DIR/Packages/manifest.json"

if [[ ! -f "$MANIFEST" ]]; then
    echo "Error: $MANIFEST not found"
    exit 1
fi

# --- Discover packages ---

PACKAGES=()
for dir in "$PACKAGES_DIR"/com.rubickanov.*/; do
    [[ -d "$dir" ]] || continue
    PACKAGES+=("$(basename "$dir")")
done

if [[ ${#PACKAGES[@]} -eq 0 ]]; then
    echo "Error: No packages found in $PACKAGES_DIR"
    exit 1
fi

# --- Relative path from project/Packages/ to unity-packages/packages/ ---

# GNU `realpath --relative-to` is unavailable on macOS (BSD), so derive the
# relative path portably via Python.
REL_PATH="$(python3 -c 'import os,sys; print(os.path.relpath(sys.argv[1], sys.argv[2]))' "$PACKAGES_DIR" "$PROJECT_DIR/Packages")"

# --- Status ---

show_status() {
    local local_count=0
    local remote_count=0
    local missing_count=0

    for pkg in "${PACKAGES[@]}"; do
        if grep -q "\"$pkg\": \"file:" "$MANIFEST"; then
            local_count=$((local_count + 1))
            echo "  LOCAL   $pkg"
        elif grep -q "\"$pkg\"" "$MANIFEST"; then
            remote_count=$((remote_count + 1))
            echo "  REMOTE  $pkg"
        else
            missing_count=$((missing_count + 1))
            echo "  --      $pkg"
        fi
    done

    echo ""
    echo "Local: $local_count | Remote: $remote_count | Not in manifest: $missing_count"
}

# --- Switch ---

switch_packages() {
    local target="$1"
    local changed=0

    for pkg in "${PACKAGES[@]}"; do
        if ! grep -q "\"$pkg\"" "$MANIFEST"; then
            continue
        fi

        if [[ "$target" == "local" ]]; then
            local new_val="file:$REL_PATH/$pkg"
        else
            local new_val="$REMOTE_BASE/$pkg"
        fi
        # `sed -i` with a backup suffix is portable across BSD (macOS) and GNU.
        sed -i.bak "s|\"$pkg\": \"[^\"]*\"|\"$pkg\": \"$new_val\"|" "$MANIFEST"
        rm -f "$MANIFEST.bak"
        changed=$((changed + 1))
    done

    echo "Switched $changed package(s) to $target"
    echo "Restart Unity to apply."
}

# --- Main ---

case "$ACTION" in
    local)
        echo "Switching to LOCAL ($REL_PATH)..."
        switch_packages local
        ;;
    remote)
        echo "Switching to REMOTE (git)..."
        switch_packages remote
        ;;
    status)
        echo "Package status in $PROJECT_DIR:"
        echo ""
        show_status
        ;;
    *)
        usage
        ;;
esac
