#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
TYHP_DLL="${TYHP_DLL:-$REPO_ROOT/bin/Debug/net9.0/tyhp.dll}"

cd "$SCRIPT_DIR"

# shellcheck source=build-common.sh
source "$SCRIPT_DIR/build-common.sh"

if [[ ! -f "$TYHP_DLL" ]]; then
  echo "tyhp compiler not found at: $TYHP_DLL" >&2
  echo "Build the compiler first: dotnet build $REPO_ROOT/tyhp.csproj" >&2
  exit 1
fi

build_package() {
  local pkg="$1"
  local project
  local php_major
  local label
  local rest
  local code

  load_package_release_version "$pkg" || return $?
  sync_package_project_output_paths "$pkg" || return $?

  echo "==> Clearing cache for $pkg"
  set +e
  (cd "$SCRIPT_DIR/$pkg" && dotnet "$TYHP_DLL" clear_cache)
  code=$?
  set -e
  if [[ $code -ne 0 ]]; then
    echo "clear_cache failed for $pkg with exit code $code" >&2
    return "$code"
  fi

  for entry in "${DIST_BUILDS[@]}"; do
    project="${entry%%:*}"
    rest="${entry#*:}"
    php_major="${rest%%:*}"
    label="${rest#*:}"

    run_tyhp_build "$pkg" "$project" "$label" "$php_major" || return $?
  done

  return 0
}

PACKAGES=(core decimal async lambda)

echo "Package source versions (independent of the compiler):"
assert_valid_package_versions "${PACKAGES[@]}"

for pkg in "${PACKAGES[@]}"; do
  build_package "$pkg"
done

echo "All packages built successfully."

echo "Verifying source maps..."
python3 "$SCRIPT_DIR/verify-sourcemaps.py"

echo "Running PHPCS..."
phpcs
