#!/usr/bin/env bash
# Build runtime packages in this repo and publish installable trees to the sibling
# tyhpproject/{core,async,decimal,lambda,php} repositories. Not a submodule workflow.
#
# Each compiled package is tagged once per PHP target (802 / 803 / 804 / 805). Package
# MAJOR is the emit PHP. X.Y comes from that package's own composer.json
# (independent of the compiler version). Libraries require 80N.X.* across PHP majors for
# a given package. See VERSIONING.md and runtime/packages/build-common.sh.
#
# Do not run this until after the compiler history wipe and the package repos are public.
# Packagist submit is a separate HUMAN step after this push.
# Git remotes use HTTPS (not SSH).

set -euo pipefail

readonly PUBLISH_SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly PROJECT_ROOT="$(cd "${PUBLISH_SCRIPT_DIR}/.." && pwd)"
readonly PACKAGES_DIR="${PROJECT_ROOT}/runtime/packages"
readonly GITHUB_ORG="tyhpproject"
readonly COMPILED_PACKAGES=(core async decimal lambda)
readonly ALL_PACKAGES=(core async decimal lambda php)

# build-common.sh expects SCRIPT_DIR = runtime/packages and REPO_ROOT = compiler repo.
SCRIPT_DIR="${PACKAGES_DIR}"
REPO_ROOT="${PROJECT_ROOT}"
# shellcheck source=../runtime/packages/build-common.sh
source "${PACKAGES_DIR}/build-common.sh"

PUBLISH_WORKDIR=""

cleanup_publish_workdir() {
  if [[ -n "${PUBLISH_WORKDIR}" && -d "${PUBLISH_WORKDIR}" ]]; then
    rm -rf "${PUBLISH_WORKDIR}"
  fi
}

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

wipe_published_tree() {
  local dest="$1"
  if [[ -d "${dest}/.git" ]]; then
    find "$dest" -mindepth 1 -maxdepth 1 ! -name '.git' -exec rm -rf {} +
  fi
}

sync_compile_dist() {
  local pkg="$1"
  local dest="$2"
  local dist_version="$3"
  local dist_dir="${PACKAGES_DIR}/dist/tyhp-${pkg}/${dist_version}"

  if [[ ! -d "${dist_dir}/src" ]]; then
    echo "Missing emitted PHP at ${dist_dir}/src. Did runtime/packages/build-all.sh succeed?" >&2
    exit 1
  fi

  wipe_published_tree "$dest"
  rsync -a --delete \
    --exclude '.git/' \
    --exclude 'src/tyhp-build-state.json' \
    "${dist_dir}/" "${dest}/"
}

sync_php_dist() {
  local dest="$1"
  local dist_version="$2"
  local php_major="$3"
  local php_c
  php_c="$(php_constraint_for_major "$php_major")"

  wipe_published_tree "$dest"

  mkdir -p "${dest}/_tyhpdef"
  rsync -a --delete \
    --exclude '.git/' \
    --exclude 'support/' \
    "${PACKAGES_DIR}/php/_tyhpdef/" "${dest}/_tyhpdef/"

  cp "${PACKAGES_DIR}/php/package.tyhp.json" "${dest}/package.tyhp.json"
  cp "${PACKAGES_DIR}/php/README.md" "${dest}/README.md"
  if [[ -f "${PACKAGES_DIR}/php/LICENSE" ]]; then
    cp "${PACKAGES_DIR}/php/LICENSE" "${dest}/LICENSE"
  elif [[ -f "${PROJECT_ROOT}/LICENSE.txt" ]]; then
    cp "${PROJECT_ROOT}/LICENSE.txt" "${dest}/LICENSE"
  fi

  python3 - "${PACKAGES_DIR}/php/composer.json" "${dest}/composer.json" "$dist_version" "$php_c" <<'PY'
import json, sys
src, dest, version, php_c = sys.argv[1:5]
data = json.load(open(src))
data["version"] = version
data.setdefault("require", {})["php"] = php_c
with open(dest, "w") as f:
    json.dump(data, f, indent=4)
    f.write("\n")
PY
}

prepare_clone() {
  local pkg="$1"
  local dest="$2"

  git clone "https://github.com/${GITHUB_ORG}/${pkg}.git" "$dest"
  cd "$dest"

  if git rev-parse --verify HEAD >/dev/null 2>&1; then
    git checkout main 2>/dev/null || git checkout -B main
  else
    git checkout -B main
  fi
}

commit_and_tag() {
  local dest="$1"
  local tag="$2"

  if git -C "$dest" rev-parse "${tag}" >/dev/null 2>&1; then
    echo "Tag already exists in ${dest}: ${tag}" >&2
    exit 1
  fi

  git -C "$dest" add -A
  if git -C "$dest" diff --cached --quiet; then
    echo "No file changes for tag ${tag}; creating tag on current tree."
  else
    git -C "$dest" commit -m "Release ${tag}"
  fi
  git -C "$dest" tag "$tag"
}

publish_package() {
  local pkg="$1"
  local dest="$2"
  local php_major
  local dist_version
  local tags=()

  echo "==> Publishing tyhp/${pkg} to ${GITHUB_ORG}/${pkg} (PHP 8.2–8.5)"

  load_package_release_version "$pkg" || exit 1
  prepare_clone "$pkg" "$dest"

  local existing=0
  local needed=0
  for entry in "${DIST_BUILDS[@]}"; do
    php_major="${entry#*:}"
    php_major="${php_major%%:*}"
    dist_version="$(package_version "$php_major")"
    needed=$((needed + 1))
    if git -C "$dest" rev-parse "${dist_version}" >/dev/null 2>&1; then
      existing=$((existing + 1))
    fi
  done
  if [[ "$existing" -eq "$needed" ]]; then
    echo "    already tagged ${needed} PHP targets; skipping"
    cd "$PROJECT_ROOT"
    return 0
  fi
  if [[ "$existing" -gt 0 ]]; then
    echo "Partial publish in ${GITHUB_ORG}/${pkg}: ${existing}/${needed} tags already exist." >&2
    echo "Finish or delete the incomplete tags, then re-run." >&2
    exit 1
  fi

  for entry in "${DIST_BUILDS[@]}"; do
    php_major="${entry#*:}"
    php_major="${php_major%%:*}"
    dist_version="$(package_version "$php_major")"
    tags+=("$dist_version")

    echo "    ${pkg} ${dist_version}"
    if [[ "$pkg" == "php" ]]; then
      sync_php_dist "$dest" "$dist_version" "$php_major"
    else
      sync_compile_dist "$pkg" "$dest" "$dist_version"
    fi
    commit_and_tag "$dest" "$dist_version"
  done

  git -C "$dest" push -u origin main
  git -C "$dest" push origin "${tags[@]}"
  cd "$PROJECT_ROOT"
}

main() {
  require_tool git
  require_tool rsync
  require_tool python3
  require_tool php
  require_tool dotnet

  cd "$PROJECT_ROOT"

  echo "Package source versions (independent of the compiler):"
  local pkg
  for pkg in "${ALL_PACKAGES[@]}"; do
    local source_version
    source_version="$(read_source_version "${PACKAGES_DIR}/${pkg}/composer.json")"
    if [[ -z "$source_version" ]]; then
      echo "Could not read version from runtime/packages/${pkg}/composer.json" >&2
      exit 1
    fi
    echo "  ${pkg}: ${source_version} → dist 80N.${source_version}"
  done

  echo "Building runtime packages for PHP 8.2–8.5..."
  "${PACKAGES_DIR}/build-all.sh"

  PUBLISH_WORKDIR="$(mktemp -d "${TMPDIR:-/tmp}/tyhp-pkg-publish.XXXXXX")"
  trap cleanup_publish_workdir EXIT

  for pkg in "${COMPILED_PACKAGES[@]}"; do
    publish_package "$pkg" "${PUBLISH_WORKDIR}/${pkg}"
  done
  publish_package "php" "${PUBLISH_WORKDIR}/php"

  echo "Published ${ALL_PACKAGES[*]} (802–805 × each package's own X.Y)."
  echo "Next HUMAN step: submit each https://github.com/${GITHUB_ORG}/{package} URL on Packagist."
}

main "$@"
