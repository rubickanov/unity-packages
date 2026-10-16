#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PACKAGES_DIR="$SCRIPT_DIR/packages"
REMOTE_BASE="https://github.com/rubickanov/unity-packages.git?path=packages"

# --- Usage ---

usage() {
    echo "Usage: $(basename "$0") <project-path> [status|local|pin <sha>|remote] [package...]"
    echo ""
    echo "  status       Show each package's source: pinned sha, branch head or file path (default)"
    echo "  local        Switch to absolute file: references and add them to testables (development)"
    echo "               --relative before the packages writes paths relative to <project>/Packages"
    echo "  pin <sha>    Switch to the git URL pinned at <sha> and drop them from testables (release)"
    echo "  remote       Switch to the git URL without a sha (follows the default branch; prefer pin)"
    echo ""
    echo "  package...   Only these packages (com.rubickanov.character or just character)."
    echo "               Default: every rubickanov package already in the manifest."
    echo ""
    echo "Examples:"
    echo "  $(basename "$0") ../beach local character"
    echo "  $(basename "$0") ../beach pin 1a2b3c4 character"
    echo "  $(basename "$0") ../beach"
    exit 1
}

if [[ $# -lt 1 ]]; then
    usage
fi

PROJECT_DIR="$(cd "$1" && pwd)"
ACTION="${2:-status}"
shift $(( $# >= 2 ? 2 : 1 ))
MANIFEST="$PROJECT_DIR/Packages/manifest.json"

if [[ ! -f "$MANIFEST" ]]; then
    echo "Error: $MANIFEST not found"
    exit 1
fi

SHA=""
if [[ "$ACTION" == "pin" ]]; then
    if [[ $# -lt 1 || ! "$1" =~ ^[0-9a-f]{7,40}$ ]]; then
        echo "Error: pin needs a commit sha"
        usage
    fi
    SHA="$1"
    shift
fi

RELATIVE=0
if [[ "${1:-}" == "--relative" ]]; then
    RELATIVE=1
    shift
fi

# --- Packages: the named ones, or every package of this repo ---

PACKAGES=()
if [[ $# -gt 0 ]]; then
    for name in "$@"; do
        [[ "$name" == com.rubickanov.* ]] || name="com.rubickanov.$name"
        if [[ ! -d "$PACKAGES_DIR/$name" ]]; then
            echo "Error: no package $name in $PACKAGES_DIR"
            exit 1
        fi
        PACKAGES+=("$name")
    done
else
    for dir in "$PACKAGES_DIR"/com.rubickanov.*/; do
        [[ -d "$dir" ]] || continue
        PACKAGES+=("$(basename "$dir")")
    done
fi

if [[ ${#PACKAGES[@]} -eq 0 ]]; then
    echo "Error: No packages found in $PACKAGES_DIR"
    exit 1
fi

# GNU `realpath --relative-to` is unavailable on macOS (BSD), so derive the
# relative path portably via Python.
REL_PATH="$(python3 -c 'import os,sys; print(os.path.relpath(sys.argv[1], sys.argv[2]))' "$PACKAGES_DIR" "$PROJECT_DIR/Packages")"

# --- Manifest edits (python keeps the JSON valid; testables is a list) ---

# set_source <package> <value> <testable 0|1>
set_source() {
    python3 - "$MANIFEST" "$1" "$2" "$3" <<'PY'
import json, sys
path, pkg, value, testable = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4] == "1"
with open(path) as f:
    manifest = json.load(f)
manifest.setdefault("dependencies", {})[pkg] = value
tests = [t for t in manifest.get("testables", []) if t != pkg]
if testable:
    tests.append(pkg)
if tests:
    manifest["testables"] = tests
else:
    manifest.pop("testables", None)
with open(path, "w") as f:
    json.dump(manifest, f, indent=2)
    f.write("\n")
PY
}

source_of() {
    python3 - "$MANIFEST" "$1" <<'PY'
import json, sys
deps = json.load(open(sys.argv[1])).get("dependencies", {})
print(deps.get(sys.argv[2], ""))
PY
}

# --- Status ---

show_status() {
    for pkg in "${PACKAGES[@]}"; do
        local src
        src="$(source_of "$pkg")"
        if [[ -z "$src" ]]; then
            printf '  %-8s %s\n' "--" "$pkg"
        elif [[ "$src" == file:* ]]; then
            printf '  %-8s %-28s %s\n' "LOCAL" "$pkg" "${src#file:}"
        elif [[ "$src" == *"#"* ]]; then
            printf '  %-8s %-28s %s\n' "PINNED" "$pkg" "${src##*#}"
        else
            printf '  %-8s %-28s %s\n' "REMOTE" "$pkg" "(no sha: default branch head)"
        fi
    done
}

# --- Switch ---

switch_packages() {
    local target="$1"
    local changed=0

    for pkg in "${PACKAGES[@]}"; do
        # Without named packages only the ones already in the manifest switch; a named one is added.
        if [[ $NAMED -eq 0 && -z "$(source_of "$pkg")" ]]; then
            continue
        fi

        case "$target" in
            local)
                if [[ $RELATIVE -eq 1 ]]; then
                    set_source "$pkg" "file:$REL_PATH/$pkg" 1
                else
                    set_source "$pkg" "file:$PACKAGES_DIR/$pkg" 1
                fi
                ;;
            pin)    set_source "$pkg" "$REMOTE_BASE/$pkg#$SHA" 0 ;;
            remote) set_source "$pkg" "$REMOTE_BASE/$pkg" 0 ;;
        esac
        changed=$((changed + 1))
    done

    echo "Switched $changed package(s) to $target${SHA:+ $SHA}"
    echo "Restart Unity (or let it refresh) to apply; commit Packages/manifest.json and packages-lock.json."
}

NAMED=0
[[ $# -gt 0 ]] && NAMED=1

# --- Main ---

case "$ACTION" in
    local)
        echo "Switching to LOCAL ($PACKAGES_DIR)..."
        switch_packages local
        ;;
    pin)
        echo "Pinning to $SHA..."
        switch_packages pin
        ;;
    remote)
        echo "Switching to REMOTE (default branch head)..."
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
