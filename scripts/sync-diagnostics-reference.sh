#!/usr/bin/env bash
# Write content/diagnostics_reference.md in a tyhp-docs-src checkout from
# MessageCode.cs and the .resx short-message catalog.
# Codes are added in the compiler first; this script only copies the generated page.

set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly COMPILER_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

if [[ -n "${TYHP_DOCS_SRC:-}" ]]; then
  DOCS_SRC="$(cd "${TYHP_DOCS_SRC}" && pwd)"
elif [[ -d "${COMPILER_ROOT}/../tyhp-docs-src/content" ]]; then
  DOCS_SRC="$(cd "${COMPILER_ROOT}/../tyhp-docs-src" && pwd)"
else
  echo "Set TYHP_DOCS_SRC to the tyhp-docs-src checkout (expected a content/ directory)." >&2
  exit 1
fi

readonly OUT="${DOCS_SRC}/content/diagnostics_reference.md"
mkdir -p "${DOCS_SRC}/content"

export TYHP_DIAGNOSTICS_REFERENCE_OUT="${OUT}"
dotnet test "${COMPILER_ROOT}/tests/Tyhp.Tests/Tyhp.Tests.csproj" \
  --filter "FullyQualifiedName~DiagnosticsReferenceGeneratorTests" \
  --nologo

if [[ ! -f "${OUT}" ]]; then
  echo "Generator did not write ${OUT}" >&2
  exit 1
fi

echo "Wrote ${OUT}"
