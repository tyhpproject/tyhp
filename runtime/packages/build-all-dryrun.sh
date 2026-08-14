#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
TYHP_DLL="${TYHP_DLL:-$REPO_ROOT/bin/Debug/net9.0/tyhp.dll}"

if [[ ! -f "$TYHP_DLL" ]]; then
  echo "tyhp compiler not found at: $TYHP_DLL" >&2
  echo "Build the compiler first: dotnet build $REPO_ROOT/tyhp.csproj" >&2
  exit 1
fi

FAILED=()

build_package() {
  local pkg="$1"
  echo "==> Building (DRY RUN) $pkg"
  set +e
  (cd "$SCRIPT_DIR/$pkg" && dotnet "$TYHP_DLL" build --dry-run)
  local code=$?
  set -e

  echo "Build (DRY RUN) returned: $code"

  # Exit code 5 (ExitCode.CompileWarning) is a clean build with warnings; only 0 and 5 are OK.
  # Every package is attempted even after a failure, so one broken package still reports the
  # state of the rest.
  if [[ $code -ne 0 && $code -ne 5 ]]; then
    FAILED+=("$pkg")
  fi
}

PACKAGES=(core decimal async lambda)

for pkg in "${PACKAGES[@]}"; do
  build_package "$pkg"
done

if [[ ${#FAILED[@]} -gt 0 ]]; then
  echo "Build (DRY RUN) FAILED for: ${FAILED[*]}" >&2
  exit 1
fi

echo "All packages built (DRY RUN) successfully."
