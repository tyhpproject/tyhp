#!/usr/bin/env bash
# Install a tyhp compiler binary from GitHub Releases.
# Does not use /releases/latest (that hides prereleases).

set -euo pipefail

readonly RELEASE_REPO="tyhpproject/tyhp"
readonly DEFAULT_TAG="v805.0.0-alpha.1"
readonly USER_AGENT="tyhp-install-script"
readonly SCRIPT_NAME="$(basename "${BASH_SOURCE[0]}")"

FORCE_SELF_CONTAINED="false"
FORCE_FRAMEWORK_DEPENDENT="false"
HAS_RUNTIME="false"
PLATFORM_OS=""
PLATFORM_ARCH=""
VARIANT=""
REQUESTED_TAG=""
RELEASE_TAG=""
ASSET_NAME=""
ASSET_URL=""
GITHUB_TOKEN_VALUE=""

INSTALL_DIR="${INSTALL_DIR:-${HOME}/.local/bin}"
TMP_HEADER_FILE=""
TMP_DOWNLOAD_FILE=""
INSTALL_SUCCESS="false"
INSTALL_PATH=""

trap 'cleanup_install_artifacts' EXIT

usage() {
  cat <<EOF
Usage: ${SCRIPT_NAME} [options]

Install the tyhp compiler from GitHub Releases (includes prereleases).

Options:
  --tag <tag>               Release tag (default: ${DEFAULT_TAG}, or most recent if --latest)
  --latest                  Use the most recent GitHub release, including prereleases
  -s, --self-contained      Force self-contained install
  -f, --framework-dependent Force framework-dependent install (.NET 9 required)
  -h, --help                Show this help

Environment:
  INSTALL_DIR               Install directory (default: ~/.local/bin)
  GITHUB_TOKEN              Optional; raises API rate limits
EOF
}

require_tool() {
  local tool_name="$1"
  if ! command -v "$tool_name" >/dev/null 2>&1; then
    echo "Missing required tool: ${tool_name}" >&2
    exit 1
  fi
}

cleanup_install_artifacts() {
  if [[ -n "${TMP_HEADER_FILE}" && -f "${TMP_HEADER_FILE}" ]]; then
    rm -f "${TMP_HEADER_FILE}"
  fi
  if [[ -n "${TMP_DOWNLOAD_FILE}" && -f "${TMP_DOWNLOAD_FILE}" ]]; then
    rm -f "${TMP_DOWNLOAD_FILE}"
  fi
  if [[ "${INSTALL_SUCCESS}" != "true" && -n "${INSTALL_PATH}" && -f "${INSTALL_PATH}" ]]; then
    rm -f "${INSTALL_PATH}"
  fi
}

detect_platform() {
  local uname_s
  local uname_m

  uname_s="$(uname -s 2>/dev/null || true)"
  uname_m="$(uname -m 2>/dev/null || true)"

  case "$uname_s" in
    Darwin) PLATFORM_OS="osx" ;;
    Linux) PLATFORM_OS="linux" ;;
    MINGW*|MSYS*|CYGWIN*)
      echo "Windows platform detected. Use scripts/install.ps1 on Windows." >&2
      exit 1
      ;;
    *)
      echo "Unsupported OS: ${uname_s}" >&2
      exit 1
      ;;
  esac

  case "$uname_m" in
    x86_64|amd64) PLATFORM_ARCH="x64" ;;
    arm64|aarch64) PLATFORM_ARCH="arm64" ;;
    *)
      echo "Unsupported architecture: ${uname_m}" >&2
      exit 1
      ;;
  esac
}

detect_runtime() {
  if ! command -v dotnet >/dev/null 2>&1; then
    HAS_RUNTIME="false"
    return 0
  fi

  if dotnet --list-runtimes 2>/dev/null | grep -q "Microsoft.NETCore.App 9\."; then
    HAS_RUNTIME="true"
  else
    HAS_RUNTIME="false"
  fi
}

determine_variant() {
  if [[ "$FORCE_SELF_CONTAINED" == "true" && "$FORCE_FRAMEWORK_DEPENDENT" == "true" ]]; then
    echo "Cannot combine --self-contained and --framework-dependent." >&2
    exit 1
  fi

  if [[ "$FORCE_SELF_CONTAINED" == "true" ]]; then
    VARIANT="self-contained"
  elif [[ "$FORCE_FRAMEWORK_DEPENDENT" == "true" ]]; then
    if [[ "$HAS_RUNTIME" != "true" ]]; then
      echo "Requested framework-dependent install, but .NET 9 runtime was not detected." >&2
      exit 1
    fi
    VARIANT="framework-dependent"
  elif [[ "$HAS_RUNTIME" == "true" ]]; then
    VARIANT="framework-dependent"
  else
    VARIANT="self-contained"
  fi
}

asset_name() {
  local platform_id="${PLATFORM_OS}-${PLATFORM_ARCH}"
  if [[ "$VARIANT" == "framework-dependent" ]]; then
    ASSET_NAME="tyhp-${platform_id}-fxdependent"
  else
    ASSET_NAME="tyhp-${platform_id}"
  fi
}

github_get() {
  local url="$1"
  local args=(-fsSL -A "$USER_AGENT" -H "Accept: application/vnd.github+json")
  if [[ -n "${GITHUB_TOKEN_VALUE}" ]]; then
    args+=(-H "Authorization: token ${GITHUB_TOKEN_VALUE}")
  fi
  curl "${args[@]}" "$url"
}

resolve_release() {
  local json
  local parsed

  if [[ "${REQUESTED_TAG}" == "latest-including-prerelease" ]]; then
    json="$(github_get "https://api.github.com/repos/${RELEASE_REPO}/releases?per_page=20")"
    if command -v jq >/dev/null 2>&1; then
      parsed="$(printf '%s' "$json" | jq -r --arg name "$ASSET_NAME" '
        ([.[] | select(.draft == false)][0] // {}) as $r
        | ($r.tag_name // "") as $tag
        | (($r.assets // []) | map(select(.name == $name)) | .[0].browser_download_url // "") as $url
        | [$tag, $url] | @tsv
      ')"
    else
      parsed="$(printf '%s' "$json" | python3 - "$ASSET_NAME" <<'PY'
import json, sys
name = sys.argv[1]
releases = json.load(sys.stdin)
release = next((r for r in releases if not r.get("draft")), {})
tag = release.get("tag_name") or ""
url = ""
for asset in release.get("assets") or []:
    if asset.get("name") == name:
        url = asset.get("browser_download_url") or ""
        break
print(f"{tag}\t{url}")
PY
)"
    fi
  else
    json="$(github_get "https://api.github.com/repos/${RELEASE_REPO}/releases/tags/${REQUESTED_TAG}")"
    if command -v jq >/dev/null 2>&1; then
      parsed="$(printf '%s' "$json" | jq -r --arg name "$ASSET_NAME" '
        (.tag_name // "") as $tag
        | ((.assets // []) | map(select(.name == $name)) | .[0].browser_download_url // "") as $url
        | [$tag, $url] | @tsv
      ')"
    else
      parsed="$(printf '%s' "$json" | python3 - "$ASSET_NAME" <<'PY'
import json, sys
name = sys.argv[1]
release = json.load(sys.stdin)
tag = release.get("tag_name") or ""
url = ""
for asset in release.get("assets") or []:
    if asset.get("name") == name:
        url = asset.get("browser_download_url") or ""
        break
print(f"{tag}\t{url}")
PY
)"
    fi
  fi

  IFS=$'\t' read -r RELEASE_TAG ASSET_URL <<< "$parsed"

  if [[ -z "${RELEASE_TAG}" || "${RELEASE_TAG}" == "null" ]]; then
    echo "Unable to determine a GitHub release tag. The compiler repo may not have a public release yet." >&2
    exit 1
  fi

  if [[ -z "${ASSET_URL}" || "${ASSET_URL}" == "null" ]]; then
    echo "Unable to find asset '${ASSET_NAME}' in release ${RELEASE_TAG}." >&2
    exit 1
  fi
}

download_binary() {
  local install_path="${INSTALL_DIR}/tyhp"
  local tmp_file
  local args=(-fsSL -A "$USER_AGENT" --output)

  tmp_file="$(mktemp "${TMPDIR:-/tmp}/tyhp-download.XXXXXX")"
  TMP_DOWNLOAD_FILE="${tmp_file}"
  curl "${args[@]}" "${tmp_file}" "${ASSET_URL}"

  if [[ ! -s "$tmp_file" ]]; then
    echo "Downloaded artifact is empty." >&2
    return 1
  fi

  mkdir -p "${INSTALL_DIR}"
  mv -f "${tmp_file}" "${install_path}"
  TMP_DOWNLOAD_FILE=""
  INSTALL_PATH="${install_path}"
  chmod +x "${install_path}"
  printf '%s' "${install_path}"
}

print_success() {
  local install_path="$1"

  echo
  echo "Installed tyhp ${RELEASE_TAG} (${VARIANT})."
  echo "Path: ${install_path}"
  echo
  echo "Verify with: tyhp version"
  echo "If that fails, add ${INSTALL_DIR} to PATH."
  INSTALL_SUCCESS="true"
}

main() {
  local use_latest="false"

  while [[ $# -gt 0 ]]; do
    case "$1" in
      --tag)
        REQUESTED_TAG="${2:-}"
        shift 2
        ;;
      --latest)
        use_latest="true"
        shift
        ;;
      -s|--self-contained)
        FORCE_SELF_CONTAINED="true"
        shift
        ;;
      -f|--framework-dependent)
        FORCE_FRAMEWORK_DEPENDENT="true"
        shift
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        echo "Unknown argument: $1" >&2
        usage
        exit 1
        ;;
    esac
  done

  if [[ "$use_latest" == "true" ]]; then
    REQUESTED_TAG="latest-including-prerelease"
  elif [[ -z "${REQUESTED_TAG}" ]]; then
    REQUESTED_TAG="${TYHP_RELEASE_TAG:-$DEFAULT_TAG}"
  fi

  if [[ -n "${GITHUB_TOKEN:-}" ]]; then
    GITHUB_TOKEN_VALUE="${GITHUB_TOKEN}"
  fi

  require_tool curl
  detect_platform
  detect_runtime
  determine_variant
  asset_name
  resolve_release

  local installed_path
  installed_path="$(download_binary)"
  print_success "$installed_path"
}

main "$@"
