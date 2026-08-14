#!/usr/bin/env bash
# Tag and upload a GitHub Release for tyhpproject/tyhp.
# Do not run until the public compiler repo exists (after the history wipe).

set -euo pipefail
set -E

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly RELEASE_REPO="tyhpproject/tyhp"
readonly CS_PROJ="${PROJECT_ROOT}/tyhp.csproj"
readonly MAX_RETRY_ATTEMPTS=5
readonly EXPECTED_ASSETS=(
  "tyhp-osx-arm64"
  "tyhp-osx-arm64-fxdependent"
  "tyhp-osx-x64"
  "tyhp-osx-x64-fxdependent"
  "tyhp-linux-x64"
  "tyhp-linux-x64-fxdependent"
  "tyhp-linux-arm64"
  "tyhp-linux-arm64-fxdependent"
  "tyhp-win-x64.exe"
  "tyhp-win-x64-fxdependent.exe"
)
RELEASE_CSPROJ_BACKUP_FILE=""
RELEASE_ABORT_MARKER=""

usage() {
  cat <<'EOF'
Usage: scripts/release.sh <patch|minor|major|X.Y.Z[-prerelease]|vX.Y.Z[-prerelease]>

Examples:
  scripts/release.sh 805.0.0-alpha.1
  scripts/release.sh v805.0.0-alpha.1
  scripts/release.sh patch
EOF
}

require_tool() {
  local tool_name="$1"
  if ! command -v "$tool_name" >/dev/null 2>&1; then
    echo "Missing required tool: ${tool_name}" >&2
    exit 1
  fi
}

is_retryable_error() {
  local error_message="$1"
  local lower
  lower="$(printf '%s' "$error_message" | tr '[:upper:]' '[:lower:]')"

  case "$lower" in
    *"timed out"*|*"timeout"*|*"rate limit"*|*"too many requests"*|*"temporary failure"*|*"connection reset"*|*"connection timed out"*|*"could not resolve host"*|*"econnreset"*|*"econnaborted"*|*"network is unreachable"*|*"bad gateway"*|*"service unavailable"*|*"internal server error"*|*"gateway timeout"*|*"please try again"*)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

run_with_retry() {
  local description="$1"
  shift
  local attempt=0
  local delay=2
  local output
  local rc

  while :; do
    attempt=$((attempt + 1))
    if output="$("$@" 2>&1)"; then
      if [[ -n "$output" ]]; then
        echo "$output"
      fi
      return 0
    fi
    rc=$?
    if [[ $attempt -ge $MAX_RETRY_ATTEMPTS ]] || ! is_retryable_error "$output"; then
      echo "$output" >&2
      return $rc
    fi

    echo "Transient error during ${description}; retrying in ${delay}s (${attempt}/${MAX_RETRY_ATTEMPTS})." >&2
    echo "$output" >&2
    sleep "$delay"
    delay=$((delay * 2))
  done
}

run_with_retry_output() {
  local description="$1"
  shift
  local attempt=0
  local delay=2
  local output
  local rc

  while :; do
    attempt=$((attempt + 1))
    if output="$("$@" 2>&1)"; then
      printf '%s' "$output"
      return 0
    fi
    rc=$?
    if [[ $attempt -ge $MAX_RETRY_ATTEMPTS ]] || ! is_retryable_error "$output"; then
      echo "$output" >&2
      return $rc
    fi

    echo "Transient error during ${description}; retrying in ${delay}s (${attempt}/${MAX_RETRY_ATTEMPTS})." >&2
    echo "$output" >&2
    sleep "$delay"
    delay=$((delay * 2))
  done
}

release_cleanup() {
  local status=$?

  if [[ -n "${RELEASE_CSPROJ_BACKUP_FILE:-}" && -f "$RELEASE_CSPROJ_BACKUP_FILE" ]]; then
    mv -f "$RELEASE_CSPROJ_BACKUP_FILE" "$CS_PROJ"
  fi

  if [[ -n "${RELEASE_ABORT_MARKER:-}" && -f "$RELEASE_ABORT_MARKER" ]]; then
    rm -f "$RELEASE_ABORT_MARKER"
  fi

  if [[ $status -ne 0 ]]; then
    echo "Release sequence failed. ${CS_PROJ} has been restored if it was modified."
  fi

  return "$status"
}

compute_sha256() {
  local file="$1"
  local hash

  if command -v sha256sum >/dev/null 2>&1; then
    read -r hash _ < <(sha256sum "$file")
  elif command -v shasum >/dev/null 2>&1; then
    read -r hash _ < <(shasum -a 256 "$file")
  else
    echo "Unable to compute checksums: sha256sum or shasum is required." >&2
    exit 1
  fi

  echo "$hash"
}

is_expected_asset() {
  local candidate="$1"
  local asset

  for asset in "${EXPECTED_ASSETS[@]}"; do
    if [[ "$asset" == "$candidate" ]]; then
      return 0
    fi
  done

  return 1
}

check_dependencies() {
  require_tool "dotnet"
  require_tool "gh"
  require_tool "git"
  require_tool "make"
}

ensure_clean_tree() {
  local dirty
  dirty="$(git status --porcelain)"
  if [[ -n "$dirty" ]]; then
    echo "Git working tree is not clean. Commit or stash changes first."
    echo "$dirty"
    exit 1
  fi
}

read_current_version() {
  local version_line
  version_line="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$CS_PROJ" | head -n 1 || true)"
  if [[ -z "$version_line" ]]; then
    echo "Unable to read <Version> from ${CS_PROJ}" >&2
    exit 1
  fi
  echo "$version_line"
}

assert_valid_version() {
  local value="$1"
  if ! [[ "$value" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
    echo "Invalid version format: ${value}. Expected X.Y.Z or X.Y.Z-prerelease (e.g. 805.0.0-alpha.1)." >&2
    exit 1
  fi
}

core_version() {
  local value="$1"
  echo "${value%%-*}"
}

resolve_version() {
  local input="$1"
  local current_version="$2"
  local mode="$input"
  local is_explicit="false"
  local new_version
  local major minor patch
  local current_core

  case "$mode" in
    patch|minor|major)
      current_core="$(core_version "$current_version")"
      IFS='.' read -r major minor patch <<< "$current_core"
      case "$mode" in
        patch) patch=$((patch + 1)) ;;
        minor) minor=$((minor + 1)); patch=0 ;;
        major) major=$((major + 1)); minor=0; patch=0 ;;
      esac
      new_version="${major}.${minor}.${patch}"
      ;;
    v*)
      new_version="${mode#v}"
      assert_valid_version "$new_version"
      is_explicit="true"
      ;;
    *)
      new_version="$mode"
      assert_valid_version "$new_version"
      is_explicit="true"
      ;;
  esac

  echo "${is_explicit}|${new_version}"
}

confirm_release() {
  local current_version="$1"
  local new_version="$2"

  echo "Version:"
  echo "  current: ${current_version}"
  echo "  release: ${new_version}"
  printf 'Continue? [y/N] '
  read -r confirm
  if [[ "${confirm}" != "y" && "${confirm}" != "Y" ]]; then
    echo "Release canceled."
    exit 0
  fi
}

bump_csproj_version() {
  local version="$1"

  perl -0pi -e "s#<Version>[^<]+</Version>#<Version>${version}</Version>#g" "$CS_PROJ"
  if ! grep -q "<Version>${version}</Version>" "$CS_PROJ"; then
    echo "Failed to update version in ${CS_PROJ}" >&2
    exit 1
  fi
}

assert_release_assets() {
  local dist_dir="${PROJECT_ROOT}/dist"
  local missing=()
  local zero_size=()
  local unexpected_files=()
  local asset
  local asset_path
  local dist_entry

  shopt -s nullglob
  for dist_entry in "${dist_dir}"/*; do
    if [[ ! -f "$dist_entry" ]]; then
      continue
    fi

    if ! is_expected_asset "${dist_entry##*/}"; then
      unexpected_files+=("${dist_entry##*/}")
    fi
  done
  shopt -u nullglob

  for asset in "${EXPECTED_ASSETS[@]}"; do
    asset_path="${dist_dir}/${asset}"
    if [[ ! -f "$asset_path" ]]; then
      missing+=("$asset")
      continue
    fi

    if [[ ! -s "$asset_path" ]]; then
      zero_size+=("$asset")
    fi
  done

  if [[ ${#unexpected_files[@]} -gt 0 ]]; then
    echo "Unexpected files in dist:"
    printf '  %s\n' "${unexpected_files[@]}"
    exit 1
  fi

  if [[ ${#missing[@]} -gt 0 ]]; then
    echo "Missing expected release artifacts:"
    printf '  %s\n' "${missing[@]}"
    exit 1
  fi

  if [[ ${#zero_size[@]} -gt 0 ]]; then
    echo "Zero-byte release artifacts:"
    printf '  %s\n' "${zero_size[@]}"
    exit 1
  fi
}

write_checksums() {
  local dist_dir="${PROJECT_ROOT}/dist"
  local checksums="${dist_dir}/checksums.txt"
  local asset

  : > "$checksums"
  for asset in "${EXPECTED_ASSETS[@]}"; do
    printf '%s  %s\n' "$(compute_sha256 "${dist_dir}/${asset}")" "$asset" >> "$checksums"
  done
}

create_release() {
  local tag="$1"
  local version="$2"
  local title="tyhp ${tag}"
  local release_assets=()
  local asset
  local extra=()

  for asset in "${EXPECTED_ASSETS[@]}"; do
    release_assets+=("${PROJECT_ROOT}/dist/${asset}")
  done
  release_assets+=("${PROJECT_ROOT}/dist/checksums.txt")

  if [[ "$version" == *-* ]]; then
    extra+=(--prerelease)
  fi

  run_with_retry "gh release create ${tag}" \
    gh release create "${tag}" "${release_assets[@]}" \
    --repo "$RELEASE_REPO" \
    --title "$title" \
    --generate-notes \
    "${extra[@]}"
}

assert_remote_release_assets() {
  local tag="$1"
  local release_assets
  local asset
  local missing=()

  release_assets="$(run_with_retry_output "gh release view ${tag} --json assets" \
    gh release view "$tag" --repo "$RELEASE_REPO" --json assets --jq '.assets[].name')"

  for asset in "${EXPECTED_ASSETS[@]}"; do
    if ! printf '%s\n' "$release_assets" | grep -Fxq "$asset"; then
      missing+=("$asset")
    fi
  done

  if [[ ${#missing[@]} -gt 0 ]]; then
    echo "Release ${tag} is missing remote assets:"
    printf '  %s\n' "${missing[@]}"
    exit 1
  fi
}

main() {
  if [[ $# -ne 1 ]]; then
    usage
    exit 1
  fi

  cd "$PROJECT_ROOT"
  check_dependencies
  ensure_clean_tree

  local current_version
  current_version="$(read_current_version)"

  local release_info
  release_info="$(resolve_version "$1" "$current_version")"
  local is_explicit
  local new_version
  is_explicit="${release_info%%|*}"
  new_version="${release_info#*|}"
  local new_tag="v${new_version}"

  assert_valid_version "$new_version"

  if git rev-parse "${new_tag}" >/dev/null 2>&1; then
    echo "Tag already exists: ${new_tag}" >&2
    exit 1
  fi

  confirm_release "$current_version" "$new_version"

  local remote_tag_lookup
  remote_tag_lookup="$(git ls-remote --tags --exit-code origin "refs/tags/${new_tag}" 2>&1 || true)"
  if [[ "$remote_tag_lookup" == *"refs/tags/${new_tag}"* ]]; then
    echo "Tag already exists on origin: ${new_tag}" >&2
    exit 1
  fi

  RELEASE_CSPROJ_BACKUP_FILE="$(mktemp "${PROJECT_ROOT}/.tyhp.csproj.release.XXXXXX")"
  cp "$CS_PROJ" "$RELEASE_CSPROJ_BACKUP_FILE"
  RELEASE_ABORT_MARKER="${PROJECT_ROOT}/.release-in-progress"
  touch "$RELEASE_ABORT_MARKER"
  trap release_cleanup ERR

  local version_changed="false"
  if [[ "$new_version" != "$current_version" ]]; then
    bump_csproj_version "$new_version"
    version_changed="true"
  else
    echo "Releasing current version ${new_version} (no csproj bump)."
  fi

  rm -rf "${PROJECT_ROOT}/dist"
  mkdir -p "${PROJECT_ROOT}/dist"
  (cd "$PROJECT_ROOT" && make build-all)
  assert_release_assets
  write_checksums

  if [[ "$version_changed" == "true" ]]; then
    git add "$CS_PROJ"
    git commit -m "Release ${new_tag}"
  fi

  git tag "$new_tag"
  run_with_retry "git push" git push
  run_with_retry "git push --tags" git push --tags

  create_release "$new_tag" "$new_version"
  assert_remote_release_assets "$new_tag"

  trap - ERR
  rm -f "$RELEASE_ABORT_MARKER"
  rm -f "$RELEASE_CSPROJ_BACKUP_FILE"
  RELEASE_ABORT_MARKER=""
  RELEASE_CSPROJ_BACKUP_FILE=""

  local release_url
  release_url="$(run_with_retry_output "gh release view ${new_tag} --json url" \
    gh release view "$new_tag" --repo "$RELEASE_REPO" --json url --jq .url)"

  echo "Release created: ${release_url}"
  echo "Install:"
  echo "  curl -fsSL https://raw.githubusercontent.com/${RELEASE_REPO}/main/scripts/install.sh | bash -s --"
}

main "$@"
