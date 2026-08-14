#!/usr/bin/env bash
# Build docs in this repo and publish HTML to tyhpproject/tyhp-docs (GitHub Pages / tyhplang.com).
# Do not run this until honest-docs content is ready. This script pushes to a public repo.
# Git remotes use HTTPS (not SSH).

set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly DOCS_REPO="tyhpproject/tyhp-docs"
readonly DOCS_CNAME="tyhplang.com"

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

main() {
  require_tool php
  require_tool composer
  require_tool git
  require_tool rsync
  require_tool sass

  cd "$PROJECT_ROOT/docs"
  composer install --no-interaction
  php generate_docs.php

  local output_dir="${PROJECT_ROOT}/docs/output"
  if [[ ! -d "$output_dir" ]]; then
    echo "Docs generator did not write ${output_dir}" >&2
    exit 1
  fi

  PUBLISH_WORKDIR="$(mktemp -d "${TMPDIR:-/tmp}/tyhp-docs-publish.XXXXXX")"
  trap cleanup_publish_workdir EXIT

  git clone --depth 1 "https://github.com/${DOCS_REPO}.git" "${PUBLISH_WORKDIR}/tyhp-docs"
  cd "${PUBLISH_WORKDIR}/tyhp-docs"

  local cname_backup=""
  if [[ -f CNAME ]]; then
    cname_backup="$(mktemp "${TMPDIR:-/tmp}/tyhp-docs-cname.XXXXXX")"
    cp CNAME "$cname_backup"
  fi

  # Replace the published tree with generated HTML + assets. Keep .git.
  find . -mindepth 1 -maxdepth 1 ! -name '.git' -exec rm -rf {} +

  rsync -a --exclude 'output.zip' "${output_dir}/" ./

  if [[ -n "$cname_backup" ]]; then
    cp "$cname_backup" CNAME
    rm -f "$cname_backup"
  else
    printf '%s\n' "$DOCS_CNAME" > CNAME
  fi

  touch .nojekyll

  git add -A
  if git diff --cached --quiet; then
    echo "No docs changes to publish."
    return 0
  fi

  git commit -m "Publish Tyhp docs from compiler repo"

  local branch
  branch="$(git rev-parse --abbrev-ref HEAD)"
  git push origin "$branch"
  echo "Published docs to https://github.com/${DOCS_REPO} (https://${DOCS_CNAME})"
}

main "$@"
