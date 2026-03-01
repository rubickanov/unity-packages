#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PACKAGES_DIR="$REPO_ROOT/packages"
DOCS_DIR="$SCRIPT_DIR"

SERVE=false
if [[ "${1:-}" == "--serve" ]]; then
    SERVE=true
fi

# --- Discover runtime .asmdef files ---

src_entries=()
assembly_count=0

while IFS= read -r asmdef_path; do
    # Skip editor-only assemblies (includePlatforms contains "Editor")
    if grep -q '"Editor"' <(grep -A 20 '"includePlatforms"' "$asmdef_path"); then
        continue
    fi

    asmdef_dir="$(dirname "$asmdef_path")"
    name="$(grep '"name"' "$asmdef_path" | head -1 | sed 's/.*: *"\(.*\)".*/\1/')"

    # Relative path from docs/ to the asmdef folder
    rel_path="$(realpath --relative-to="$DOCS_DIR" "$asmdef_dir")"

    echo "  Found: $name ($rel_path)"

    src_entries+=("{ \"files\": [\"**/*.cs\"], \"src\": \"$rel_path\" }")
    assembly_count=$((assembly_count + 1))
done < <(find "$PACKAGES_DIR" -name "*.asmdef" -type f | sort)

if [[ ${#src_entries[@]} -eq 0 ]]; then
    echo "Error: No runtime .asmdef files found in $PACKAGES_DIR"
    exit 1
fi

echo "Discovered $assembly_count runtime assemblies"

# --- Generate docfx.json ---

# Join src entries with commas
src_json=""
for i in "${!src_entries[@]}"; do
    if [[ $i -gt 0 ]]; then
        src_json+=","$'\n'"        "
    fi
    src_json+="${src_entries[$i]}"
done

cat > "$DOCS_DIR/docfx.json" <<EOF
{
  "metadata": [
    {
      "src": [
        $src_json
      ],
      "dest": "api",
      "allowCompilationErrors": true
    }
  ],
  "build": {
    "content": [
      { "files": ["api/**.yml", "api/index.md"] },
      { "files": ["**.md", "**/toc.yml"], "exclude": ["_site/**"] }
    ],
    "dest": "_site",
    "globalMetadata": {
      "_appTitle": "Rubickanov Unity Packages — API",
      "_enableSearch": true
    }
  }
}
EOF

echo "Generated docfx.json"

# --- Collect package READMEs ---

mkdir -p "$DOCS_DIR/guides"

guide_toc_entries=()

while IFS= read -r readme_path; do
    pkg_dir="$(dirname "$readme_path")"
    pkg_name="$(basename "$pkg_dir")"

    # Strip com.rubickanov. prefix for slug
    slug="${pkg_name#com.rubickanov.}"

    # Read displayName from package.json, fall back to slug
    display_name="$slug"
    if [[ -f "$pkg_dir/package.json" ]]; then
        dn="$(grep '"displayName"' "$pkg_dir/package.json" | head -1 | sed 's/.*: *"\(.*\)".*/\1/')"
        if [[ -n "$dn" ]]; then
            display_name="$dn"
        fi
    fi

    cp "$readme_path" "$DOCS_DIR/guides/$slug.md"
    guide_toc_entries+=("- name: $display_name"$'\n'"  href: $slug.md")

    echo "  Guide: $display_name ($slug.md)"
done < <(find "$PACKAGES_DIR" -maxdepth 2 -name "README.md" -type f | sort)

if [[ ${#guide_toc_entries[@]} -gt 0 ]]; then
    printf "%s\n" "${guide_toc_entries[@]}" > "$DOCS_DIR/guides/toc.yml"
    echo "Generated guides/toc.yml (${#guide_toc_entries[@]} guides)"
fi

# --- Generate index.md ---

cat > "$DOCS_DIR/index.md" <<'EOF'
---
_layout: landing
---

# Rubickanov Unity Packages — API

Auto-generated documentation for all packages.

Browse the [API Reference](api/index.md) or [Guides](guides/toc.yml) for details.
EOF

echo "Generated index.md"

# --- Generate toc.yml ---

{
    echo "- name: API Reference"
    echo "  href: api/"
    if [[ ${#guide_toc_entries[@]} -gt 0 ]]; then
        echo "- name: Guides"
        echo "  href: guides/"
    fi
    echo "- name: Home"
    echo "  href: index.md"
} > "$DOCS_DIR/toc.yml"

echo "Generated toc.yml"

# --- Generate api/index.md ---

mkdir -p "$DOCS_DIR/api"

cat > "$DOCS_DIR/api/index.md" <<'EOF'
# API Reference

This section contains auto-generated API documentation for all packages.
EOF

echo "Generated api/index.md"

# --- Run DocFX ---

echo ""
echo "Running docfx metadata..."
docfx metadata "$DOCS_DIR/docfx.json"

echo ""
echo "Running docfx build..."
docfx build "$DOCS_DIR/docfx.json"

echo ""
echo "Documentation generated at $DOCS_DIR/_site/"

if [[ "$SERVE" == true ]]; then
    echo "Starting local server at http://localhost:8080 ..."
    docfx serve "$DOCS_DIR/_site"
fi
