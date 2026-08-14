#!/bin/bash
# Shared helpers for build-all.sh / rebuild-all.sh.
# Sourced by those scripts; not meant to be run directly.

# Dist package semver: MAJOR = PHP (802/803/804/805).
# Each package has its own independent X.Y in composer.json (not the compiler version):
#   source "0.0"              → dist "80N.0.0"
#   source "1.4"              → dist "80N.1.4"
#   source "805.0.0-alpha.1"  → dist "80N.0.0-alpha.1"  (legacy three-part: compiler MAJOR ignored)
PACKAGE_VERSION_MINOR="${PACKAGE_VERSION_MINOR:-}"
PACKAGE_VERSION_PATCH="${PACKAGE_VERSION_PATCH:-}"
PACKAGE_VERSION_SUFFIX="${PACKAGE_VERSION_SUFFIX:-}"

# project file : php major : php label
DIST_BUILDS=(
  "tyhp.json:802:PHP 8.2"
  "tyhp-php8.3.json:803:PHP 8.3"
  "tyhp-php8.4.json:804:PHP 8.4"
  "tyhp-php8.5.json:805:PHP 8.5"
)

# Parse source version into dist X.Y[-prerelease] parts.
# A.B → X=A Y=B. MAJOR.MINOR.PATCH[-pre] → X=MINOR Y=PATCH (PHP encoding is dist MAJOR).
_parse_package_source_version() {
  python3 - "$1" <<'PY'
import json, re, sys
path = sys.argv[1]
raw = str(json.load(open(path)).get("version", ""))
match = re.fullmatch(r"(\d+)\.(\d+)(?:\.(\d+)(?:-([0-9A-Za-z.-]+))?)?", raw)
if not match:
    print(f"Invalid version in {path}: {raw!r} (expected X.Y or MAJOR.MINOR.PATCH[-prerelease])", file=sys.stderr)
    sys.exit(1)
if match.group(3) is None:
    print(f"{match.group(1)}|{match.group(2)}||{raw}")
else:
    print(f"{match.group(2)}|{match.group(3)}|{match.group(4) or ''}|{raw}")
PY
}

# Read version from runtime/packages/<pkg>/composer.json into
# PACKAGE_VERSION_MINOR / PACKAGE_VERSION_PATCH / PACKAGE_VERSION_SUFFIX.
# Always reloads — packages version independently and must not share the first load.
load_package_release_version() {
  local pkg="$1"
  local composer_json="$SCRIPT_DIR/$pkg/composer.json"
  local parsed
  local suffix
  local raw

  if [[ ! -f "$composer_json" ]]; then
    echo "composer.json not found: $composer_json" >&2
    return 1
  fi

  parsed="$(_parse_package_source_version "$composer_json")" || return 1

  IFS='|' read -r PACKAGE_VERSION_MINOR PACKAGE_VERSION_PATCH suffix raw <<< "$parsed"
  if [[ -n "$suffix" ]]; then
    PACKAGE_VERSION_SUFFIX="-${suffix}"
  else
    PACKAGE_VERSION_SUFFIX=""
  fi
}

# Read extra.tyhp.interopContractVersion (Story 15 interop contract) from a package
# composer.json. The source manifest is the single source of truth for the dist stamp so the
# two cannot drift; Tyhp's InteropContractSurfaceTests pin it to InteropContract.CurrentVersion.
read_package_interop_contract_version() {
  local composer_json="$1"
  python3 - "$composer_json" <<'PY'
import json, sys
path = sys.argv[1]
version = json.load(open(path)).get("extra", {}).get("tyhp", {}).get("interopContractVersion")
if not isinstance(version, int):
    print(f"Missing/invalid extra.tyhp.interopContractVersion in {path}: {version!r}", file=sys.stderr)
    sys.exit(1)
print(version)
PY
}

# Read the raw source version string from a package composer.json.
read_package_source_version() {
  local composer_json="$1"
  python3 - "$composer_json" <<'PY'
import json, re, sys
path = sys.argv[1]
raw = str(json.load(open(path)).get("version", ""))
if not re.fullmatch(r"\d+\.\d+(?:\.\d+(?:-[0-9A-Za-z.-]+)?)?", raw):
    print(f"Invalid version in {path}: {raw!r} (expected X.Y or MAJOR.MINOR.PATCH[-prerelease])", file=sys.stderr)
    sys.exit(1)
print(raw)
PY
}

# Matching-X constraint across PHP majors for a dependency package (usually core).
# Uses that package's own X.Y — not the package currently being built.
matching_minor_constraint_for() {
  local dep_pkg="$1"
  local parsed
  local x
  parsed="$(_parse_package_source_version "$SCRIPT_DIR/$dep_pkg/composer.json")" || return 1
  IFS='|' read -r x _ _ _ <<< "$parsed"
  echo "802.${x}.* || 803.${x}.* || 804.${x}.* || 805.${x}.*"
}

core_version_constraint() {
  matching_minor_constraint_for "core"
}

assert_valid_package_versions() {
  local pkgs=("$@")
  local pkg
  local parsed
  local x
  local y
  local suffix
  local raw

  for pkg in "${pkgs[@]}"; do
    parsed="$(_parse_package_source_version "$SCRIPT_DIR/$pkg/composer.json")" || return 1
    IFS='|' read -r x y suffix raw <<< "$parsed"
    if [[ -n "$suffix" ]]; then
      suffix="-${suffix}"
    fi
    echo "  ${pkg}: ${raw} → dist 80N.${x}.${y}${suffix}"
  done
}

package_version() {
  local php_major="$1"
  if [[ -z "${PACKAGE_VERSION_MINOR}" || -z "${PACKAGE_VERSION_PATCH}" ]]; then
    echo "PACKAGE_VERSION_MINOR/PATCH not loaded; call load_package_release_version first" >&2
    return 1
  fi
  echo "${php_major}.${PACKAGE_VERSION_MINOR}.${PACKAGE_VERSION_PATCH}${PACKAGE_VERSION_SUFFIX}"
}

dist_package_dir() {
  local pkg="$1"
  local php_major="$2"
  echo "$SCRIPT_DIR/dist/tyhp-${pkg}/$(package_version "$php_major")"
}

# Keep tyhp*.json output.path in sync with the current release version.
sync_package_project_output_paths() {
  local pkg="$1"
  local entry
  local project
  local php_major
  local rest
  local version
  local project_path
  local new_path

  for entry in "${DIST_BUILDS[@]}"; do
    project="${entry%%:*}"
    rest="${entry#*:}"
    php_major="${rest%%:*}"
    version="$(package_version "$php_major")"
    project_path="$SCRIPT_DIR/$pkg/$project"
    new_path="../dist/tyhp-${pkg}/${version}/src"

    python3 - "$project_path" "$new_path" <<'PY'
import json, sys
path, new_out = sys.argv[1], sys.argv[2]
data = json.load(open(path))
data.setdefault("output", {})["path"] = new_out
with open(path, "w") as f:
    json.dump(data, f, indent=4)
    f.write("\n")
PY
  done
}

php_constraint_for_major() {
  case "$1" in
    802) echo "~8.2.0" ;;
    803) echo "~8.3.0" ;;
    804) echo "~8.4.0" ;;
    805) echo "~8.5.0" ;;
    *)
      echo "unknown PHP major: $1" >&2
      return 1
      ;;
  esac
}

php_label_for_major() {
  case "$1" in
    802) echo "8.2" ;;
    803) echo "8.3" ;;
    804) echo "8.4" ;;
    805) echo "8.5" ;;
    *)
      echo "unknown PHP major: $1" >&2
      return 1
      ;;
  esac
}


write_dist_composer_json() {
  local pkg="$1"
  local php_major="$2"
  local out_dir
  local version
  local php_c
  local php_label
  local composer_name="tyhp/${pkg}"
  local core_constraint
  local interop_version

  out_dir="$(dist_package_dir "$pkg" "$php_major")"
  version="$(package_version "$php_major")"
  php_c="$(php_constraint_for_major "$php_major")"
  php_label="$(php_label_for_major "$php_major")"
  core_constraint="$(core_version_constraint)"
  interop_version="$(read_package_interop_contract_version "$SCRIPT_DIR/$pkg/composer.json")" || return 1

  mkdir -p "$out_dir"

  case "$pkg" in
    core)
      cat > "$out_dir/composer.json" <<EOF
{
    "name": "${composer_name}",
    "description": "Tyhp runtime core (PHP ${php_label}) — type system, generics, typed variables, property accessors",
    "type": "library",
    "license": "Apache-2.0",
    "version": "${version}",
    "extra": {
        "tyhp": {
            "interopContractVersion": ${interop_version}
        }
    },
    "require": {
        "php": "${php_c}"
    },
    "autoload": {
        "psr-4": {
            "Tyhp\\\\": "src/Tyhp/"
        }
    }
}
EOF
      ;;
    async)
      cat > "$out_dir/composer.json" <<EOF
{
    "name": "${composer_name}",
    "description": "Tyhp runtime async (PHP ${php_label}) — Promise, event loop, CancellationToken, async iteration",
    "type": "library",
    "license": "Apache-2.0",
    "version": "${version}",
    "extra": {
        "tyhp": {
            "interopContractVersion": ${interop_version}
        }
    },
    "require": {
        "php": "${php_c}",
        "tyhp/core": "${core_constraint}"
    },
    "autoload": {
        "psr-4": {
            "Tyhp\\\\": "src/Tyhp/"
        }
    }
}
EOF
      ;;
    decimal)
      cat > "$out_dir/composer.json" <<EOF
{
    "name": "${composer_name}",
    "description": "Tyhp runtime decimal (PHP ${php_label}) — arbitrary-precision decimal arithmetic",
    "type": "library",
    "license": "Apache-2.0",
    "version": "${version}",
    "extra": {
        "tyhp": {
            "interopContractVersion": ${interop_version}
        }
    },
    "require": {
        "php": "${php_c}",
        "tyhp/core": "${core_constraint}"
    },
    "suggest": {
        "ext-decimal": "Preferred backend (php-decimal / mpdecimal) for arbitrary-precision decimal arithmetic",
        "ext-bcmath": "Alternative backend for arbitrary-precision decimal arithmetic",
        "ext-gmp": "Alternative backend for arbitrary-precision decimal arithmetic"
    },
    "autoload": {
        "psr-4": {
            "Tyhp\\\\": "src/Tyhp/"
        },
        "files": [
            "src/Tyhp/_functions.php"
        ]
    }
}
EOF
      ;;
    lambda)
      cat > "$out_dir/composer.json" <<EOF
{
    "name": "${composer_name}",
    "description": "Tyhp runtime parsable lambdas (PHP ${php_label}): PropertyPath and Expression tree runtime classes",
    "type": "library",
    "license": "Apache-2.0",
    "version": "${version}",
    "extra": {
        "tyhp": {
            "interopContractVersion": ${interop_version}
        }
    },
    "require": {
        "php": "${php_c}",
        "tyhp/core": "${core_constraint}"
    },
    "autoload": {
        "psr-4": {
            "Tyhp\\\\": "src/Tyhp/"
        }
    }
}
EOF
      ;;
    *)
      echo "unknown package: $pkg" >&2
      return 1
      ;;
  esac
}

write_dist_readme() {
  local pkg="$1"
  local php_major="$2"
  local out_dir
  local version
  local php_label
  local composer_name="tyhp/${pkg}"

  out_dir="$(dist_package_dir "$pkg" "$php_major")"
  version="$(package_version "$php_major")"
  php_label="$(php_label_for_major "$php_major")"

  cat > "$out_dir/README.md" <<EOF
# ${composer_name}

Tyhp runtime package \`${composer_name}\` (version \`${version}\`, compiled for PHP ${php_label}).

For documentation, guides, and more information, visit **https://tyhplang.com**.
EOF
}

write_dist_license() {
  local pkg="$1"
  local php_major="$2"
  local out_dir
  local license_src="${REPO_ROOT}/LICENSE.txt"

  out_dir="$(dist_package_dir "$pkg" "$php_major")"

  if [[ ! -f "$license_src" ]]; then
    echo "LICENSE.txt not found at: $license_src" >&2
    return 1
  fi

  cp "$license_src" "$out_dir/LICENSE"
}

# Copy package.tyhpdef + package.tyhp.json into the dist package root.
# Track C auto-generation into output.path is still a Story 20 placeholder, so these
# currently live in each package's source root and must be copied per PHP target.
copy_dist_tyhpdefs() {
  local pkg="$1"
  local php_major="$2"
  local out_dir
  local src_dir="$SCRIPT_DIR/$pkg"

  out_dir="$(dist_package_dir "$pkg" "$php_major")"

  if [[ -f "$src_dir/package.tyhp.json" ]]; then
    cp "$src_dir/package.tyhp.json" "$out_dir/package.tyhp.json"
  else
    echo "warning: missing $src_dir/package.tyhp.json (not copied)" >&2
  fi

  if [[ -f "$src_dir/package.tyhpdef" ]]; then
    cp "$src_dir/package.tyhpdef" "$out_dir/package.tyhpdef"
  else
    echo "warning: missing $src_dir/package.tyhpdef (not copied)" >&2
  fi
}

# Drop compiler build-state metadata — not part of the published package.
cleanup_dist_build_artifacts() {
  local pkg="$1"
  local php_major="$2"
  local out_dir
  out_dir="$(dist_package_dir "$pkg" "$php_major")"
  rm -f "$out_dir/src/tyhp-build-state.json"
}

finalize_dist_package() {
  local pkg="$1"
  local php_major="$2"

  write_dist_composer_json "$pkg" "$php_major"
  write_dist_readme "$pkg" "$php_major"
  write_dist_license "$pkg" "$php_major"
  copy_dist_tyhpdefs "$pkg" "$php_major"
  cleanup_dist_build_artifacts "$pkg" "$php_major"
}

run_tyhp_build() {
  local pkg="$1"
  local project="$2"
  local label="$3"
  local php_major="$4"
  local code

  echo "==> Building $pkg ($label) -> dist/tyhp-${pkg}/$(package_version "$php_major")/"
  set +e
  (cd "$SCRIPT_DIR/$pkg" && dotnet "$TYHP_DLL" build --clean --tyhp-project="$project")
  code=$?
  set -e

  # 0 = success, 5 = success with warnings (Tyhp ExitCode.CompileWarning)
  if [[ $code -ne 0 && $code -ne 5 ]]; then
    echo "Build failed for $pkg ($label) with exit code $code" >&2
    return "$code"
  fi

  finalize_dist_package "$pkg" "$php_major"
}
