#!/usr/bin/env bash
# Build runtime packages in this repo and publish installable trees to the sibling
# tyhpproject/{core,async,decimal,lambda,php} repositories. Not a submodule workflow.
#
# Do not run this until after the compiler history wipe and the package repos are public.
# Packagist submit is a separate HUMAN step after this push.

set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly PACKAGES_DIR="${PROJECT_ROOT}/runtime/packages"
readonly GITHUB_ORG="tyhpproject"
readonly ALL_PACKAGES=(core async decimal lambda php)
# PHP 8.2 emit is the Packagist artifact (php: >=8.2). Dist MAJOR 802 encodes that target.
readonly PUBLISH_PHP_MAJOR="802"

require_tool() {
  local tool_name="$1"
  if ! command -v "$tool_name" >/dev/null 2>&1; then
    echo "Missing required tool: ${tool_name}" >&2
    exit 1
  fi
}

read_source_version() {
  python3 - "$1" <<'PY'
import json, sys
print(json.load(open(sys.argv[1])).get("version", ""))
PY
}

dist_version_for_php_major() {
  local source_version="$1"
  python3 - "$PUBLISH_PHP_MAJOR" "$source_version" <<'PY'
import re, sys
php_major, source = sys.argv[1], sys.argv[2]
match = re.fullmatch(r"\d+\.(\d+\.\d+(?:-[0-9A-Za-z.-]+)?)", source)
if not match:
    raise SystemExit(f"Cannot derive dist version from {source!r}")
print(f"{php_major}.{match.group(1)}")
PY
}

sync_compile_package() {
  local pkg="$1"
  local dest="$2"
  local source_version="$3"
  local dist_version
  local src_dir

  dist_version="$(dist_version_for_php_major "$source_version")"
  src_dir="${PACKAGES_DIR}/dist/tyhp-${pkg}/${dist_version}/src"
  if [[ ! -d "$src_dir" ]]; then
    echo "Missing emitted PHP at ${src_dir}. Did runtime/packages/build-all.sh succeed?" >&2
    exit 1
  fi

  # Wipe the published tree except .git, then copy only installable files.
  if [[ -d "${dest}/.git" ]]; then
    find "$dest" -mindepth 1 -maxdepth 1 ! -name '.git' -exec rm -rf {} +
  fi

  cp "${PACKAGES_DIR}/${pkg}/composer.json" "${dest}/composer.json"
  if [[ -f "${PACKAGES_DIR}/${pkg}/package.tyhp.json" ]]; then
    cp "${PACKAGES_DIR}/${pkg}/package.tyhp.json" "${dest}/package.tyhp.json"
  fi
  if [[ -f "${PACKAGES_DIR}/${pkg}/package.tyhpdef" ]]; then
    cp "${PACKAGES_DIR}/${pkg}/package.tyhpdef" "${dest}/package.tyhpdef"
  fi
  if [[ -f "${PACKAGES_DIR}/${pkg}/README.md" ]]; then
    cp "${PACKAGES_DIR}/${pkg}/README.md" "${dest}/README.md"
  elif [[ -f "${PACKAGES_DIR}/dist/tyhp-${pkg}/${dist_version}/README.md" ]]; then
    cp "${PACKAGES_DIR}/dist/tyhp-${pkg}/${dist_version}/README.md" "${dest}/README.md"
  else
    printf '# tyhp/%s\n\nTyhp runtime package. See https://tyhplang.com.\n' "$pkg" > "${dest}/README.md"
  fi
  if [[ -f "${PROJECT_ROOT}/LICENSE.txt" ]]; then
    cp "${PROJECT_ROOT}/LICENSE.txt" "${dest}/LICENSE"
  fi

  mkdir -p "${dest}/src"
  rsync -a "${src_dir}/" "${dest}/src/"
}

sync_php_package() {
  local dest="$1"

  if [[ -d "${dest}/.git" ]]; then
    find "$dest" -mindepth 1 -maxdepth 1 ! -name '.git' -exec rm -rf {} +
  fi

  mkdir -p "${dest}/_tyhpdef"
  rsync -a --delete \
    --exclude '.git/' \
    --exclude 'support/' \
    "${PACKAGES_DIR}/php/_tyhpdef/" "${dest}/_tyhpdef/"

  cp "${PACKAGES_DIR}/php/composer.json" "${dest}/composer.json"
  cp "${PACKAGES_DIR}/php/package.tyhp.json" "${dest}/package.tyhp.json"
  cp "${PACKAGES_DIR}/php/README.md" "${dest}/README.md"
  if [[ -f "${PACKAGES_DIR}/php/LICENSE" ]]; then
    cp "${PACKAGES_DIR}/php/LICENSE" "${dest}/LICENSE"
  elif [[ -f "${PROJECT_ROOT}/LICENSE.txt" ]]; then
    cp "${PROJECT_ROOT}/LICENSE.txt" "${dest}/LICENSE"
  fi

  rm -rf "${dest}/tyhp_src" "${dest}/tests" "${dest}/vendor"
  rm -f "${dest}/tyhp.json" "${dest}"/tyhp-php8.*.json
}

publish_package() {
  local pkg="$1"
  local source_version="$2"
  local workdir="$3"
  local dest="${workdir}/${pkg}"
  local tag="$source_version"

  echo "==> Publishing tyhp/${pkg} to ${GITHUB_ORG}/${pkg} (tag ${tag})"

  git clone "git@github.com:${GITHUB_ORG}/${pkg}.git" "$dest"
  cd "$dest"

  if git rev-parse --verify HEAD >/dev/null 2>&1; then
    git checkout main 2>/dev/null || git checkout -B main
  else
    git checkout -B main
  fi

  if git rev-parse "${tag}" >/dev/null 2>&1; then
    echo "Tag already exists in ${GITHUB_ORG}/${pkg}: ${tag}" >&2
    exit 1
  fi

  if [[ "$pkg" == "php" ]]; then
    sync_php_package "$dest"
  else
    sync_compile_package "$pkg" "$dest" "$source_version"
  fi

  git add -A
  if git diff --cached --quiet; then
    echo "No file changes for ${pkg}; creating tag on current tree if needed."
  else
    git commit -m "Release ${tag}"
  fi

  git tag "$tag"
  git push -u origin main
  git push origin "$tag"
  cd "$PROJECT_ROOT"
}

main() {
  require_tool git
  require_tool rsync
  require_tool python3
  require_tool php
  require_tool dotnet

  cd "$PROJECT_ROOT"

  local source_version
  source_version="$(read_source_version "${PACKAGES_DIR}/core/composer.json")"
  if [[ -z "$source_version" ]]; then
    echo "Could not read version from runtime/packages/core/composer.json" >&2
    exit 1
  fi

  local pkg
  for pkg in "${ALL_PACKAGES[@]}"; do
    local other
    other="$(read_source_version "${PACKAGES_DIR}/${pkg}/composer.json")"
    if [[ "$other" != "$source_version" ]]; then
      echo "Package versions must match: core is ${source_version}, ${pkg} is ${other}" >&2
      exit 1
    fi
  done

  echo "Building runtime packages (emitted PHP for Packagist)..."
  "${PACKAGES_DIR}/build-all.sh"

  local workdir
  workdir="$(mktemp -d "${TMPDIR:-/tmp}/tyhp-pkg-publish.XXXXXX")"
  trap 'rm -rf "${workdir}"' EXIT

  for pkg in "${ALL_PACKAGES[@]}"; do
    publish_package "$pkg" "$source_version" "$workdir"
  done

  echo "Published ${ALL_PACKAGES[*]} at ${source_version}."
  echo "Next HUMAN step: submit each https://github.com/${GITHUB_ORG}/{package} URL on Packagist."
}

main "$@"
