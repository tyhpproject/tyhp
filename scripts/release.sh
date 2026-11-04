#!/usr/bin/env bash
# Bump the version, build the tyhp CLI binaries, tag vX.Y.Z, and publish the
# GitHub Release on tyhpproject/tyhp. Run with --help for the version
# arguments, the files it bumps, and how a failed run is resumed.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly SCRIPT_DIR
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly PROJECT_ROOT
readonly RELEASE_REPO="tyhpproject/tyhp"
readonly CS_PROJ="${PROJECT_ROOT}/tyhp.csproj"
readonly CHANGELOG="${PROJECT_ROOT}/CHANGELOG.md"
readonly RUNTIME_VERSIONS_CS="${PROJECT_ROOT}/Tyhp/Domain/Services/RuntimePackageVersions.cs"
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

# Every place the current compiler version is written, as "path|kind":
#   csproj    <Version> in tyhp.csproj
#   text      the full version (805.1.2-beta.4, also inside v805.1.2-beta.4)
#   badge     the shields.io status badge (status-805.1.2--beta.4-)
#   assembly  the compiler stamp compiler: "805.1.2.0" in emitted-PHP fixtures;
#             it carries only X.Y.Z, so it changes only when X.Y.Z changes
# CHANGELOG.md headings and the Makefile error example are history or examples
# and are not listed.
readonly VERSION_FILES=(
  "tyhp.csproj|csproj"
  "README.md|text"
  "README.md|badge"
  "CONTRIBUTING.md|text"
  "VERSIONING.md|text"
  "scripts/install.sh|text"
  "scripts/install.ps1|text"
  "tests/conformance/story11/generics/expected/App/_functions.php|assembly"
  "tests/conformance/story11/generics/expected/App/Container.php|assembly"
  "tests/conformance/story15/generics-erasure/expected/App/Box.php|assembly"
  "tests/Tyhp.Tests/Emitter/CallSiteSpliceEmitterTests.cs|assembly"
)

# RuntimePackageVersions.cs constant | tyhp-runtime-src packages/ directory
readonly RUNTIME_FALLBACK_PACKAGES=(
  "Php|tyhpdef/php"
  "Core|tyhp/core"
  "Async|tyhp/async"
  "Decimal|tyhp/decimal"
  "Lambda|tyhp/lambda"
  "Parallel|tyhp/parallel"
  "Io|tyhp/io"
)

DRY_RUN=0
GENERATE_NOTES=0
DEBUG_BUILD=1
CHECK_RUNTIME_VERSIONS=1
VERSION_ARG=""

PROBLEMS=()
WARNINGS=()

UPSTREAM_REMOTE=""
UPSTREAM_BRANCH=""
UPSTREAM_BEHIND=0

# Progress of a real run, read by the EXIT trap.
STAGE="start"
BACKUP_DIR=""
NOTES_FILE=""
BIN_REMOVED=0
DIST_MATCHES=0
RELEASE_TAG=""
RELEASE_VERSION=""

usage() {
  cat <<'EOF'
Usage: scripts/release.sh [options] <version>

Bumps the compiler version everywhere it is written, builds the ten tyhp CLI
binaries and dist/checksums.txt, commits "Release vX.Y.Z", tags vX.Y.Z, pushes
the branch and that one tag, and publishes the GitHub Release on
tyhpproject/tyhp. The release is created as a draft, every asset's SHA-256 is
checked against dist/checksums.txt, and only then is it published.

<version>
  X.Y.Z[-pre], vX.Y.Z[-pre]
        Release exactly this version. It must not be lower than the current
        <Version> in tyhp.csproj. The current version itself releases without a
        bump commit (the version files must already hold it).
  prerelease
        Next number on the current prerelease: 805.1.2-beta.4 -> 805.1.2-beta.5.
  alpha, beta, rc
        Bump the number when the current prerelease is on that channel, or move
        to it: 805.1.2-beta.4 + beta -> 805.1.2-beta.5,
        805.1.2-beta.4 + rc -> 805.1.2-rc.1. Refused on a stable version and
        when moving back (rc -> beta).
  release
        Finalize the current prerelease: 805.1.2-beta.4 -> 805.1.2.
  patch, minor, major
        Semver bump of a stable version: 805.2.0 + patch -> 805.2.1.
        Refused while the current version is a prerelease, because the result
        would silently drop the suffix and publish a stable release. Use
        prerelease, alpha/beta/rc, or release, or pass the version (beta
        patches so far kept the suffix: 805.1.3-beta.3).

Options:
  --dry-run                     Run every check and print the plan, the
                                version-file diff, and the release notes.
                                Changes nothing: no build, commit, tag, push,
                                or GitHub write. Does not prompt.
  --generate-notes              Append GitHub's generated notes after the
                                CHANGELOG section in the release body.
  --no-debug-build              Skip `dotnet build tyhp.csproj` at the end.
  --skip-runtime-version-check  Do not compare RuntimePackageVersions.cs with
                                the tyhp-runtime-src package versions.
  -h, --help                    Show this help.

Before running:
  - CHANGELOG.md has "## [X.Y.Z] - YYYY-MM-DD" (no "v") with this release's
    entries, below an empty "## [Unreleased]". That section is the release body.
  - The working tree is clean, the branch tracks an upstream on
    tyhpproject/tyhp, and it is not behind that upstream.
  - The fallback versions in Tyhp/Domain/Services/RuntimePackageVersions.cs
    match the composer.json versions in tyhp-runtime-src (TYHP_RUNTIME_SRC,
    the checkout or its packages/ directory, else ../tyhp-runtime-src). The
    check is skipped when no checkout is found.

Resuming a failed run:
  Run the same command again from the "Release vX.Y.Z" commit. When vX.Y.Z
  already points at HEAD, the script skips the bump, reuses dist/ if its
  checksums and version match (rebuilds otherwise), pushes only what origin is
  missing, uploads to or publishes the draft release, and verifies the assets.
  A published release is checked, never re-uploaded.

After the release:
  `make build-all` deletes bin/ and obj/. The script runs
  `dotnet build tyhp.csproj` so bin/Debug/net9.0/tyhp.dll exists again for the
  tyhp-runtime-src scripts, then prints the next steps.
EOF
  echo
  echo "Version files (VERSION_FILES):"
  local entry
  for entry in "${VERSION_FILES[@]}"; do
    printf '  %-70s %s\n' "${entry%%|*}" "${entry#*|}"
  done
}

say() {
  printf '%s\n' "$*"
}

problem() {
  PROBLEMS+=("$*")
  if [[ "$DRY_RUN" -eq 1 ]]; then
    say "  PROBLEM: $*"
  fi
}

warn() {
  WARNINGS+=("$*")
  say "  warning: $*"
}

die() {
  printf 'error: %s\n' "$*" >&2
  exit 1
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      -h|--help)
        usage
        exit 0
        ;;
      --dry-run)
        DRY_RUN=1
        ;;
      --generate-notes)
        GENERATE_NOTES=1
        ;;
      --no-debug-build)
        DEBUG_BUILD=0
        ;;
      --skip-runtime-version-check)
        CHECK_RUNTIME_VERSIONS=0
        ;;
      -*)
        die "unknown option: $1 (see --help)"
        ;;
      *)
        if [[ -n "$VERSION_ARG" ]]; then
          die "only one version argument is allowed (see --help)"
        fi
        VERSION_ARG="$1"
        ;;
    esac
    shift
  done
  if [[ -z "$VERSION_ARG" ]]; then
    usage >&2
    exit 1
  fi
}

is_retryable_error() {
  local error_message="$1"
  local lower
  lower="$(printf '%s' "$error_message" | tr '[:upper:]' '[:lower:]')"

  case "$lower" in
    *"timed out"*|*"timeout"*|*"rate limit"*|*"too many requests"*|*"temporary failure"*|*"connection reset"*|*"could not resolve host"*|*"econnreset"*|*"econnaborted"*|*"network is unreachable"*|*"bad gateway"*|*"service unavailable"*|*"internal server error"*|*"please try again"*)
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
    rc=0
    output="$("$@" 2>&1)" || rc=$?
    if [[ $rc -eq 0 ]]; then
      if [[ -n "$output" ]]; then
        printf '%s\n' "$output"
      fi
      return 0
    fi
    if [[ $attempt -ge $MAX_RETRY_ATTEMPTS ]] || ! is_retryable_error "$output"; then
      printf '%s\n' "$output" >&2
      return $rc
    fi

    echo "Transient error during ${description}; retrying in ${delay}s (${attempt}/${MAX_RETRY_ATTEMPTS})." >&2
    printf '%s\n' "$output" >&2
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
    rc=0
    output="$("$@" 2>&1)" || rc=$?
    if [[ $rc -eq 0 ]]; then
      printf '%s' "$output"
      return 0
    fi
    if [[ $attempt -ge $MAX_RETRY_ATTEMPTS ]] || ! is_retryable_error "$output"; then
      printf '%s\n' "$output" >&2
      return $rc
    fi

    echo "Transient error during ${description}; retrying in ${delay}s (${attempt}/${MAX_RETRY_ATTEMPTS})." >&2
    printf '%s\n' "$output" >&2
    sleep "$delay"
    delay=$((delay * 2))
  done
}

compute_sha256() {
  local file="$1"
  local hash

  if command -v sha256sum >/dev/null 2>&1; then
    read -r hash _ < <(sha256sum "$file")
  else
    read -r hash _ < <(shasum -a 256 "$file")
  fi

  echo "$hash"
}

# --- versions ---------------------------------------------------------------

assert_valid_version() {
  local value="$1"
  if ! [[ "$value" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
    die "invalid version format: ${value}. Expected X.Y.Z or X.Y.Z-prerelease (e.g. 805.2.0-beta.1)."
  fi
}

core_version() {
  echo "${1%%-*}"
}

prerelease_part() {
  if [[ "$1" == *-* ]]; then
    echo "${1#*-}"
  fi
}

# Prints -1, 0, or 1 by semver precedence.
semver_compare() {
  local LC_ALL=C
  local a="$1"
  local b="$2"
  local a_major a_minor a_patch b_major b_minor b_patch
  local a_pre b_pre
  local i
  local x y

  IFS=. read -r a_major a_minor a_patch <<< "$(core_version "$a")"
  IFS=. read -r b_major b_minor b_patch <<< "$(core_version "$b")"
  for i in 0 1 2; do
    case "$i" in
      0) x="$a_major"; y="$b_major" ;;
      1) x="$a_minor"; y="$b_minor" ;;
      2) x="$a_patch"; y="$b_patch" ;;
    esac
    if ((10#$x < 10#$y)); then echo -1; return; fi
    if ((10#$x > 10#$y)); then echo 1; return; fi
  done

  a_pre="$(prerelease_part "$a")"
  b_pre="$(prerelease_part "$b")"
  if [[ -z "$a_pre" && -z "$b_pre" ]]; then echo 0; return; fi
  if [[ -z "$a_pre" ]]; then echo 1; return; fi
  if [[ -z "$b_pre" ]]; then echo -1; return; fi

  local a_ids b_ids
  IFS=. read -r -a a_ids <<< "$a_pre"
  IFS=. read -r -a b_ids <<< "$b_pre"
  i=0
  while :; do
    if [[ $i -ge ${#a_ids[@]} && $i -ge ${#b_ids[@]} ]]; then echo 0; return; fi
    if [[ $i -ge ${#a_ids[@]} ]]; then echo -1; return; fi
    if [[ $i -ge ${#b_ids[@]} ]]; then echo 1; return; fi
    x="${a_ids[$i]}"
    y="${b_ids[$i]}"
    if [[ "$x" =~ ^[0-9]+$ && "$y" =~ ^[0-9]+$ ]]; then
      if ((10#$x < 10#$y)); then echo -1; return; fi
      if ((10#$x > 10#$y)); then echo 1; return; fi
    elif [[ "$x" =~ ^[0-9]+$ ]]; then
      echo -1; return
    elif [[ "$y" =~ ^[0-9]+$ ]]; then
      echo 1; return
    elif [[ "$x" < "$y" ]]; then
      echo -1; return
    elif [[ "$x" > "$y" ]]; then
      echo 1; return
    fi
    i=$((i + 1))
  done
}

# beta.3 -> beta.4; beta -> beta.1; 3 -> 4
next_prerelease_number() {
  local pre="$1"
  local last="${pre##*.}"
  if [[ "$pre" =~ ^[0-9]+$ ]]; then
    echo "$((10#$pre + 1))"
  elif [[ "$pre" == *.* && "$last" =~ ^[0-9]+$ ]]; then
    echo "${pre%.*}.$((10#$last + 1))"
  else
    echo "${pre}.1"
  fi
}

channel_rank() {
  case "$1" in
    alpha) echo 1 ;;
    beta) echo 2 ;;
    rc) echo 3 ;;
    *) echo 0 ;;
  esac
}

resolve_version() {
  local input="$1"
  local current="$2"
  local current_core
  local current_pre
  local major minor patch
  local channel
  local target_rank
  local current_rank

  current_core="$(core_version "$current")"
  current_pre="$(prerelease_part "$current")"

  case "$input" in
    patch|minor|major)
      if [[ -n "$current_pre" ]]; then
        cat >&2 <<EOF
error: '${input}' would turn the prerelease ${current} into a stable version and drop '-${current_pre}'.
Use one of:
  scripts/release.sh prerelease   # ${current_core}-$(next_prerelease_number "$current_pre")
  scripts/release.sh release      # ${current_core} (stable)
  scripts/release.sh X.Y.Z-pre    # explicit, e.g. a beta patch that keeps the suffix
EOF
        exit 1
      fi
      IFS='.' read -r major minor patch <<< "$current_core"
      case "$input" in
        patch) patch=$((patch + 1)) ;;
        minor) minor=$((minor + 1)); patch=0 ;;
        major) major=$((major + 1)); minor=0; patch=0 ;;
      esac
      echo "${major}.${minor}.${patch}"
      ;;
    prerelease)
      if [[ -z "$current_pre" ]]; then
        die "'prerelease' needs a prerelease current version; ${current} is stable. Pass the next prerelease explicitly (e.g. ${current_core%.*}.$(( ${current_core##*.} + 1 ))-beta.1)."
      fi
      echo "${current_core}-$(next_prerelease_number "$current_pre")"
      ;;
    alpha|beta|rc)
      if [[ -z "$current_pre" ]]; then
        die "'${input}' needs a prerelease current version; ${current} is stable. Pass the version explicitly (e.g. ${current_core%.*}.$(( ${current_core##*.} + 1 ))-${input}.1)."
      fi
      channel="${current_pre%%.*}"
      current_rank="$(channel_rank "$channel")"
      target_rank="$(channel_rank "$input")"
      if [[ "$current_rank" -eq 0 ]]; then
        die "the current prerelease '-${current_pre}' is not alpha, beta, or rc. Pass the version explicitly."
      fi
      if [[ "$current_rank" -eq "$target_rank" ]]; then
        echo "${current_core}-$(next_prerelease_number "$current_pre")"
      elif [[ "$current_rank" -lt "$target_rank" ]]; then
        echo "${current_core}-${input}.1"
      else
        die "'${input}' would move ${current} back from ${channel} to ${input}. Pass the version explicitly."
      fi
      ;;
    release)
      if [[ -z "$current_pre" ]]; then
        die "'release' finalizes a prerelease; ${current} is already stable."
      fi
      echo "$current_core"
      ;;
    v*)
      assert_valid_version "${input#v}"
      echo "${input#v}"
      ;;
    *)
      assert_valid_version "$input"
      echo "$input"
      ;;
  esac
}

read_current_version() {
  local version
  version="$(perl -ne 'if (m{<Version>([^<]+)</Version>}) { print $1; exit }' "$CS_PROJ")"
  if [[ -z "$version" ]]; then
    die "unable to read <Version> from ${CS_PROJ}"
  fi
  echo "$version"
}

# version_edit MODE KIND FILE OLD NEW
#   MODE count: print how often the OLD form occurs
#   MODE print: print the file with OLD replaced by NEW
#   MODE apply: rewrite the file and print the replacement count
# For the assembly kind, OLD and NEW are X.Y.Z (the stamp is X.Y.Z.0).
version_edit() {
  MODE="$1" KIND="$2" OLD="$4" NEW="$5" perl -e '
    my ($mode, $kind, $old, $new) = @ENV{qw(MODE KIND OLD NEW)};
    my $file = shift;
    open(my $in, "<", $file) or die "$file: $!\n";
    my $text = do { local $/; <$in> };
    close($in);
    my ($re, $rep);
    if ($kind eq "csproj") {
      $re = qr{<Version>\Q$old\E</Version>};
      $rep = "<Version>$new</Version>";
    } elsif ($kind eq "text") {
      $re = qr{(?:(?<=v)|(?<![0-9A-Za-z.\-]))\Q$old\E(?![0-9A-Za-z]|[.\-][0-9A-Za-z])};
      $rep = $new;
    } elsif ($kind eq "badge") {
      (my $o = $old) =~ s/-/--/g;
      (my $n = $new) =~ s/-/--/g;
      $re = qr{\Qstatus-$o-\E};
      $rep = "status-$n-";
    } elsif ($kind eq "assembly") {
      $re = qr{\Qcompiler: "$old.0"\E};
      $rep = "compiler: \"$new.0\"";
    } else {
      die "unknown version kind: $kind\n";
    }
    my $count = () = $text =~ /$re/g;
    if ($mode eq "count") { print $count; exit 0; }
    (my $out = $text) =~ s/$re/$rep/g;
    if ($mode eq "print") { print $out; exit 0; }
    open(my $fh, ">", $file) or die "$file: $!\n";
    print {$fh} $out;
    close($fh);
    print $count;
  ' "$3"
}

# Prints "OLD NEW" for one VERSION_FILES kind, or nothing when it does not change.
version_forms_for_kind() {
  local kind="$1"
  local old="$2"
  local new="$3"
  case "$kind" in
    assembly)
      if [[ "$(core_version "$old")" != "$(core_version "$new")" ]]; then
        echo "$(core_version "$old") $(core_version "$new")"
      fi
      ;;
    *)
      if [[ "$old" != "$new" ]]; then
        echo "$old $new"
      fi
      ;;
  esac
}

# Fresh bump: every listed file must hold the old version.
# Same version: every listed file must already hold it.
check_version_files() {
  local old="$1"
  local new="$2"
  local entry file kind forms count expected
  for entry in "${VERSION_FILES[@]}"; do
    file="${entry%%|*}"
    kind="${entry#*|}"
    if [[ ! -f "${PROJECT_ROOT}/${file}" ]]; then
      problem "VERSION_FILES lists ${file}, which does not exist. Update the list in scripts/release.sh."
      continue
    fi
    forms="$(version_forms_for_kind "$kind" "$old" "$new")"
    if [[ -n "$forms" ]]; then
      expected="${forms%% *}"
    elif [[ "$kind" == "assembly" ]]; then
      expected="$(core_version "$new")"
    else
      expected="$new"
    fi
    count="$(version_edit count "$kind" "${PROJECT_ROOT}/${file}" "$expected" "$expected")"
    if [[ "$count" -eq 0 ]]; then
      if [[ -n "$forms" ]]; then
        problem "${file} has no ${kind} occurrence of ${expected}. Bump it by hand or fix VERSION_FILES in scripts/release.sh."
      else
        problem "${file} does not hold ${expected} (${kind}). The csproj is already at ${new}, so the other version files must be too."
      fi
    fi
  done
}

preview_version_files() {
  local old="$1"
  local new="$2"
  local path entry kind forms tmp
  local any=0
  tmp="$(mktemp "${TMPDIR:-/tmp}/tyhp-release-preview.XXXXXX")"
  while IFS= read -r path; do
    [[ -f "${PROJECT_ROOT}/${path}" ]] || continue
    cp "${PROJECT_ROOT}/${path}" "$tmp"
    for entry in "${VERSION_FILES[@]}"; do
      [[ "${entry%%|*}" == "$path" ]] || continue
      kind="${entry#*|}"
      forms="$(version_forms_for_kind "$kind" "$old" "$new")"
      [[ -n "$forms" ]] || continue
      version_edit apply "$kind" "$tmp" "${forms%% *}" "${forms#* }" >/dev/null
    done
    if ! cmp -s "${PROJECT_ROOT}/${path}" "$tmp"; then
      diff -u --label "a/${path}" --label "b/${path}" "${PROJECT_ROOT}/${path}" "$tmp" || true
      any=1
    fi
  done < <(version_file_paths)
  rm -f "$tmp"
  if [[ "$any" -eq 0 ]]; then
    say "  (no version-file changes)"
  fi
}

apply_version_files() {
  local old="$1"
  local new="$2"
  local entry file kind forms count
  [[ -n "$BACKUP_DIR" && -d "$BACKUP_DIR" ]] || die "internal: BACKUP_DIR is not set before bumping."
  for entry in "${VERSION_FILES[@]}"; do
    file="${entry%%|*}"
    kind="${entry#*|}"
    forms="$(version_forms_for_kind "$kind" "$old" "$new")"
    [[ -n "$forms" ]] || continue
    if [[ ! -f "${BACKUP_DIR}/${file}" ]]; then
      mkdir -p "$(dirname "${BACKUP_DIR}/${file}")"
      cp -p "${PROJECT_ROOT}/${file}" "${BACKUP_DIR}/${file}"
    fi
    count="$(version_edit apply "$kind" "${PROJECT_ROOT}/${file}" "${forms%% *}" "${forms#* }")"
    say "  ${file}: ${count} ${kind} replacement(s)"
  done
}

restore_version_files() {
  local entry file
  [[ -n "$BACKUP_DIR" && -d "$BACKUP_DIR" ]] || return 0
  for entry in "${VERSION_FILES[@]}"; do
    file="${entry%%|*}"
    if [[ -f "${BACKUP_DIR}/${file}" ]]; then
      cp -p "${BACKUP_DIR}/${file}" "${PROJECT_ROOT}/${file}"
    fi
  done
}

version_file_paths() {
  local entry
  local seen=" "
  for entry in "${VERSION_FILES[@]}"; do
    case "$seen" in
      *" ${entry%%|*} "*) ;;
      *)
        seen+="${entry%%|*} "
        printf '%s\n' "${entry%%|*}"
        ;;
    esac
  done
}

# Old version strings in tracked files outside VERSION_FILES, CHANGELOG.md, and this script's help.
report_unlisted_version_mentions() {
  local old="$1"
  local badge="${old//-/--}"
  local excludes=(":(exclude)CHANGELOG.md" ":(exclude)scripts/release.sh")
  local path
  local hits
  while IFS= read -r path; do
    excludes+=(":(exclude)${path}")
  done < <(version_file_paths)
  hits="$(git -C "$PROJECT_ROOT" grep -n -F -e "$old" -e "$badge" -- . "${excludes[@]}" || true)"
  if [[ -n "$hits" ]]; then
    say "  Other tracked files mention ${old} and are not bumped (examples or history; add real version references to VERSION_FILES):"
    printf '%s\n' "$hits" | sed 's/^/    /'
  fi
}

# --- checks -----------------------------------------------------------------

check_tools() {
  local tool
  for tool in dotnet gh git make perl diff cmp; do
    if ! command -v "$tool" >/dev/null 2>&1; then
      problem "missing required tool: ${tool}"
    fi
  done
  if ! command -v sha256sum >/dev/null 2>&1 && ! command -v shasum >/dev/null 2>&1; then
    problem "missing required tool: sha256sum or shasum"
  fi
}

check_clean_tree() {
  local dirty
  dirty="$(git -C "$PROJECT_ROOT" status --porcelain)"
  if [[ -n "$dirty" ]]; then
    problem "the working tree is not clean ($(printf '%s\n' "$dirty" | wc -l | tr -d ' ') path(s)). Commit the changes that ship, or release from a separate clean clone; do not stash or discard other people's work."
  fi
}

check_upstream() {
  local upstream
  local url
  local behind
  if ! upstream="$(git -C "$PROJECT_ROOT" rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>/dev/null)"; then
    problem "the current branch has no upstream. Check out main (tracking origin/main) before releasing."
    return
  fi
  UPSTREAM_REMOTE="${upstream%%/*}"
  UPSTREAM_BRANCH="${upstream#*/}"
  url="$(git -C "$PROJECT_ROOT" remote get-url "$UPSTREAM_REMOTE" 2>/dev/null || true)"
  url="${url%.git}"
  url="${url%/}"
  if [[ "$url" != *"/${RELEASE_REPO}" && "$url" != *":${RELEASE_REPO}" ]]; then
    problem "remote ${UPSTREAM_REMOTE} is ${url:-unset}, not ${RELEASE_REPO}."
  fi
  behind="$(git -C "$PROJECT_ROOT" rev-list --count "HEAD..${upstream}" 2>/dev/null || echo 0)"
  UPSTREAM_BEHIND="$behind"
  say "  branch: $(git -C "$PROJECT_ROOT" rev-parse --abbrev-ref HEAD) -> ${upstream} (${url})"
}

# A fresh release must start from the tip of the upstream branch. A resumed
# release may sit behind it when its commit is already on that branch.
check_upstream_freshness() {
  local mode="$1"
  local upstream="${UPSTREAM_REMOTE}/${UPSTREAM_BRANCH}"
  [[ -n "$UPSTREAM_REMOTE" && "$UPSTREAM_BEHIND" -gt 0 ]] || return 0
  if [[ "$mode" == "resume" ]] && git -C "$PROJECT_ROOT" merge-base --is-ancestor HEAD "$upstream" 2>/dev/null; then
    say "  ${upstream} is ${UPSTREAM_BEHIND} commit(s) ahead and already contains HEAD"
    return 0
  fi
  problem "HEAD is ${UPSTREAM_BEHIND} commit(s) behind ${upstream} (as of the last fetch). Pull first."
}

check_gh_auth() {
  if ! gh auth status --hostname github.com >/dev/null 2>&1; then
    problem "gh is not logged in to github.com (gh auth login)."
  fi
}

# Requires "## [X.Y.Z] - YYYY-MM-DD" with at least one entry, below "## [Unreleased]".
check_changelog() {
  local version="$1"
  local heading_line
  local unreleased_line
  local date
  local body

  if [[ ! -f "$CHANGELOG" ]]; then
    problem "CHANGELOG.md is missing."
    return
  fi
  heading_line="$(VER="$version" perl -ne 'if (index($_, "## [$ENV{VER}]") == 0) { print "$.:$_"; exit }' "$CHANGELOG")"
  unreleased_line="$(perl -ne 'if (/^## \[Unreleased\]\s*$/) { print $.; exit }' "$CHANGELOG")"
  if [[ -z "$heading_line" ]]; then
    problem "CHANGELOG.md has no '## [${version}] - YYYY-MM-DD' section. Move the [Unreleased] entries under '## [${version}] - $(date +%Y-%m-%d)', leave an empty '## [Unreleased]' above it, and commit (see .cursor/rules/changelog.mdc)."
    return
  fi
  if ! [[ "${heading_line#*:}" =~ ^##\ \[[^]]+\]\ -\ ([0-9]{4}-[0-9]{2}-[0-9]{2})[[:space:]]*$ ]]; then
    problem "CHANGELOG.md heading '${heading_line#*:}' should be '## [${version}] - YYYY-MM-DD' (no v prefix)."
    return
  fi
  date="${BASH_REMATCH[1]}"
  if [[ -z "$unreleased_line" ]]; then
    problem "CHANGELOG.md has no '## [Unreleased]' heading above '## [${version}]'."
  elif [[ "$unreleased_line" -gt "${heading_line%%:*}" ]]; then
    problem "'## [Unreleased]' must come before '## [${version}]' in CHANGELOG.md."
  fi
  body="$(changelog_section "$version" | perl -ne 'print if /\S/')"
  if [[ -z "$body" ]]; then
    problem "the '## [${version}]' section in CHANGELOG.md is empty."
  fi
  if [[ "$date" != "$(date +%Y-%m-%d)" ]]; then
    warn "the CHANGELOG date for ${version} is ${date}, not today."
  fi
  say "  CHANGELOG.md: '## [${version}] - ${date}' found"
}

changelog_section() {
  VER="$1" perl -ne '
    if (index($_, "## [$ENV{VER}]") == 0) { $in = 1; next }
    if ($in && /^## /) { exit }
    print if $in;
  ' "$CHANGELOG"
}

runtime_packages_root() {
  local candidate="${TYHP_RUNTIME_SRC:-${PROJECT_ROOT}/../tyhp-runtime-src}"
  if [[ -d "${candidate}/packages/tyhp" ]]; then
    (cd "${candidate}/packages" && pwd)
  elif [[ -d "${candidate}/tyhp" && -d "${candidate}/tyhpdef" ]]; then
    (cd "$candidate" && pwd)
  fi
}

# The compiler writes RuntimePackageVersions.cs into users' composer.json when it
# cannot read a package's own composer.json, so it ships with this release.
check_runtime_fallback_versions() {
  local packages
  local entry const dir table source
  local stale=()

  if [[ "$CHECK_RUNTIME_VERSIONS" -eq 0 ]]; then
    say "  runtime fallback versions: skipped (--skip-runtime-version-check)"
    return
  fi
  packages="$(runtime_packages_root)"
  if [[ -z "$packages" ]]; then
    warn "no tyhp-runtime-src checkout found (TYHP_RUNTIME_SRC or ../tyhp-runtime-src); RuntimePackageVersions.cs was not compared."
    return
  fi
  for entry in "${RUNTIME_FALLBACK_PACKAGES[@]}"; do
    const="${entry%%|*}"
    dir="${entry#*|}"
    table="$(CONST="$const" perl -ne 'if (/internal const string \Q$ENV{CONST}\E = "([^"]*)";/) { print $1; exit }' "$RUNTIME_VERSIONS_CS")"
    source="$(perl -MJSON::PP -e 'local $/; my $d = JSON::PP->new->decode(<STDIN>); print $d->{version} // ""' < "${packages}/${dir}/composer.json" 2>/dev/null || true)"
    if [[ -z "$table" || -z "$source" ]]; then
      stale+=("${dir}: could not read (table '${table}', composer.json '${source}')")
    elif [[ "$table" != "$source" ]]; then
      stale+=("${const} = ${table}, ${dir} is ${source}")
    fi
  done
  if [[ ${#stale[@]} -gt 0 ]]; then
    problem "RuntimePackageVersions.cs is behind the composer.json versions in ${packages} ($(printf '%s; ' "${stale[@]}" | sed 's/; $//')). Update the constants and commit, or pass --skip-runtime-version-check."
  else
    say "  runtime fallback versions match ${packages}"
  fi
}

# Sets TAG_STATE to fresh, resume, or a blocking problem.
TAG_STATE=""
check_tag_state() {
  local tag="$1"
  local version="$2"
  local current="$3"
  local head local_commit remote_line remote_commit

  head="$(git -C "$PROJECT_ROOT" rev-parse HEAD)"
  local_commit="$(git -C "$PROJECT_ROOT" rev-parse -q --verify "refs/tags/${tag}^{commit}" 2>/dev/null || true)"
  remote_commit=""
  if [[ -n "$UPSTREAM_REMOTE" ]]; then
    remote_line="$(git -C "$PROJECT_ROOT" ls-remote --tags "$UPSTREAM_REMOTE" "refs/tags/${tag}" "refs/tags/${tag}^{}" 2>/dev/null || true)"
    remote_commit="$(printf '%s\n' "$remote_line" | awk -v peeled="refs/tags/${tag}^{}" -v plain="refs/tags/${tag}" '$2 == peeled { p = $1 } $2 == plain { t = $1 } END { print (p != "" ? p : t) }')"
  fi

  if [[ -z "$local_commit" && -z "$remote_commit" ]]; then
    TAG_STATE="fresh"
    say "  tag ${tag}: not created yet"
    return
  fi
  if [[ -z "$local_commit" ]]; then
    problem "${tag} exists on ${UPSTREAM_REMOTE} (${remote_commit:0:12}) but not locally. Fetch it (git fetch ${UPSTREAM_REMOTE} tag ${tag} --no-tags) and rerun from that commit to resume."
    return
  fi
  if [[ "$local_commit" != "$head" ]]; then
    problem "${tag} already points at ${local_commit:0:12}, not HEAD (${head:0:12}). To resume, run from that commit in a clean clone; otherwise pick a new version."
    return
  fi
  if [[ -n "$remote_commit" && "$remote_commit" != "$local_commit" ]]; then
    problem "${tag} is ${local_commit:0:12} locally but ${remote_commit:0:12} on ${UPSTREAM_REMOTE}. Never move a published tag; pick a new version."
    return
  fi
  if [[ "$current" != "$version" ]]; then
    problem "${tag} points at HEAD, but tyhp.csproj there says ${current}."
    return
  fi
  TAG_STATE="resume"
  if [[ -n "$remote_commit" ]]; then
    say "  tag ${tag}: at HEAD, already on ${UPSTREAM_REMOTE} (resuming)"
  else
    say "  tag ${tag}: at HEAD, not pushed yet (resuming)"
  fi
}

# Sets RELEASE_STATE to absent, draft, or published, and RELEASE_ASSETS to
# "name<TAB>sha256<TAB>size" lines.
RELEASE_STATE=""
RELEASE_ASSETS=""
read_release_state() {
  local tag="$1"
  local output
  local rc=0
  output="$(gh release view "$tag" --repo "$RELEASE_REPO" --json isDraft,assets \
    --jq '(if .isDraft then "draft" else "published" end), (.assets[] | [.name, ((.digest // "") | sub("^sha256:"; "")), (.size | tostring)] | @tsv)' 2>&1)" || rc=$?
  if [[ $rc -ne 0 ]]; then
    if [[ "$output" == *"release not found"* || "$output" == *"Not Found"* ]]; then
      RELEASE_STATE="absent"
      RELEASE_ASSETS=""
      return 0
    fi
    printf '%s\n' "$output" >&2
    return 1
  fi
  RELEASE_STATE="$(printf '%s\n' "$output" | sed -n '1p')"
  RELEASE_ASSETS="$(printf '%s\n' "$output" | sed '1d')"
}

# --- dist -------------------------------------------------------------------

dist_dir() {
  echo "${PROJECT_ROOT}/dist"
}

current_rid() {
  case "$(uname -s)/$(uname -m)" in
    Darwin/arm64) echo "osx-arm64" ;;
    Darwin/x86_64) echo "osx-x64" ;;
    Linux/x86_64) echo "linux-x64" ;;
    Linux/aarch64|Linux/arm64) echo "linux-arm64" ;;
    *) echo "" ;;
  esac
}

assert_release_assets() {
  local dir
  local missing=()
  local zero_size=()
  local unexpected_files=()
  local asset
  local entry
  local name

  dir="$(dist_dir)"
  shopt -s nullglob
  for entry in "${dir}"/*; do
    [[ -f "$entry" ]] || continue
    name="${entry##*/}"
    if [[ "$name" == "checksums.txt" ]]; then
      continue
    fi
    if ! printf '%s\n' "${EXPECTED_ASSETS[@]}" | grep -Fxq "$name"; then
      unexpected_files+=("$name")
    fi
  done
  shopt -u nullglob

  for asset in "${EXPECTED_ASSETS[@]}"; do
    if [[ ! -f "${dir}/${asset}" ]]; then
      missing+=("$asset")
    elif [[ ! -s "${dir}/${asset}" ]]; then
      zero_size+=("$asset")
    fi
  done

  if [[ ${#unexpected_files[@]} -gt 0 ]]; then
    die "unexpected files in dist/: ${unexpected_files[*]}"
  fi
  if [[ ${#missing[@]} -gt 0 ]]; then
    die "missing release artifacts in dist/: ${missing[*]}"
  fi
  if [[ ${#zero_size[@]} -gt 0 ]]; then
    die "zero-byte release artifacts in dist/: ${zero_size[*]}"
  fi
}

write_checksums() {
  local dir
  local asset
  dir="$(dist_dir)"
  : > "${dir}/checksums.txt"
  for asset in "${EXPECTED_ASSETS[@]}"; do
    printf '%s  %s\n' "$(compute_sha256 "${dir}/${asset}")" "$asset" >> "${dir}/checksums.txt"
  done
}

# The local-platform binary reports this version.
check_local_binary_version() {
  local version="$1"
  local rid
  local binary
  local reported
  rid="$(current_rid)"
  if [[ -z "$rid" ]]; then
    say "  (no local binary for $(uname -s)/$(uname -m); version not checked)"
    return 0
  fi
  binary="$(dist_dir)/tyhp-${rid}"
  reported="$("$binary" version 2>/dev/null | awk '/^Tyhp compiler:/ { print $3; exit }' || true)"
  if [[ "$reported" != "$version" ]]; then
    echo "dist/tyhp-${rid} reports '${reported}', expected ${version}." >&2
    return 1
  fi
  say "  dist/tyhp-${rid} reports ${version}"
}

# 0 when dist/ holds every asset, checksums.txt matches them, and the local binary reports VERSION.
dist_is_reusable() {
  local version="$1"
  local dir
  local asset
  local expected
  dir="$(dist_dir)"
  [[ -f "${dir}/checksums.txt" ]] || return 1
  for asset in "${EXPECTED_ASSETS[@]}"; do
    [[ -s "${dir}/${asset}" ]] || return 1
    expected="$(awk -v name="$asset" '$2 == name { print $1 }' "${dir}/checksums.txt")"
    [[ -n "$expected" && "$expected" == "$(compute_sha256 "${dir}/${asset}")" ]] || return 1
  done
  check_local_binary_version "$version" >/dev/null 2>&1
}

build_dist() {
  local version="$1"
  rm -rf "$(dist_dir)"
  mkdir -p "$(dist_dir)"
  BIN_REMOVED=1
  (cd "$PROJECT_ROOT" && make build-all)
  assert_release_assets
  write_checksums
  check_local_binary_version "$version"
  DIST_MATCHES=1
}

# --- GitHub release ---------------------------------------------------------

release_asset_paths() {
  local asset
  for asset in "${EXPECTED_ASSETS[@]}"; do
    printf '%s\n' "$(dist_dir)/${asset}"
  done
  printf '%s\n' "$(dist_dir)/checksums.txt"
}

# Every expected asset plus checksums.txt is on the release with the SHA-256 of
# the local dist/ file (size when GitHub reports no digest).
verify_remote_assets() {
  local tag="$1"
  local name local_path local_sha local_size remote_line remote_sha remote_size
  local bad=()
  read_release_state "$tag"
  while IFS= read -r local_path; do
    name="${local_path##*/}"
    remote_line="$(printf '%s\n' "$RELEASE_ASSETS" | awk -F '\t' -v n="$name" '$1 == n { print; exit }')"
    if [[ -z "$remote_line" ]]; then
      bad+=("${name}: missing")
      continue
    fi
    remote_sha="$(printf '%s' "$remote_line" | cut -f2)"
    remote_size="$(printf '%s' "$remote_line" | cut -f3)"
    if [[ -n "$remote_sha" ]]; then
      local_sha="$(compute_sha256 "$local_path")"
      if [[ "$remote_sha" != "$local_sha" ]]; then
        bad+=("${name}: sha256 ${remote_sha:0:12} on GitHub, ${local_sha:0:12} in dist/")
      fi
    else
      local_size="$(wc -c < "$local_path" | tr -d ' ')"
      if [[ "$remote_size" != "$local_size" ]]; then
        bad+=("${name}: ${remote_size} bytes on GitHub, ${local_size} in dist/")
      fi
    fi
  done < <(release_asset_paths)
  if [[ ${#bad[@]} -gt 0 ]]; then
    printf 'Release %s assets do not match dist/:\n' "$tag" >&2
    printf '  %s\n' "${bad[@]}" >&2
    return 1
  fi
  say "  ${tag}: all $(( ${#EXPECTED_ASSETS[@]} + 1 )) assets match dist/checksums.txt"
}

# For a published release without a local dist/: every listed binary has the
# digest that the release's own checksums.txt names.
verify_remote_assets_self_consistent() {
  local tag="$1"
  local tmp
  local asset expected remote_sha
  local bad=()
  tmp="$(mktemp "${TMPDIR:-/tmp}/tyhp-release-checksums.XXXXXX")"
  run_with_retry "gh release download ${tag} checksums.txt" \
    gh release download "$tag" --repo "$RELEASE_REPO" --pattern checksums.txt --output "$tmp" --clobber >/dev/null
  for asset in "${EXPECTED_ASSETS[@]}"; do
    expected="$(awk -v name="$asset" '$2 == name { print $1 }' "$tmp")"
    remote_sha="$(printf '%s\n' "$RELEASE_ASSETS" | awk -F '\t' -v n="$asset" '$1 == n { print $2; exit }')"
    if [[ -z "$expected" || -z "$remote_sha" || "$expected" != "$remote_sha" ]]; then
      bad+=("${asset}: checksums.txt '${expected:0:12}', GitHub '${remote_sha:0:12}'")
    fi
  done
  rm -f "$tmp"
  if [[ ${#bad[@]} -gt 0 ]]; then
    printf 'Release %s is inconsistent:\n' "$tag" >&2
    printf '  %s\n' "${bad[@]}" >&2
    return 1
  fi
  say "  ${tag}: every binary matches the release's checksums.txt"
}

write_release_notes() {
  local version="$1"
  NOTES_FILE="$(mktemp "${TMPDIR:-/tmp}/tyhp-release-notes.XXXXXX")"
  changelog_section "$version" | perl -0777 -pe 's/\A\s+//; s/\s+\z/\n/' > "$NOTES_FILE"
}

create_draft_release() {
  local tag="$1"
  local version="$2"
  local args=(
    gh release create "$tag"
    --repo "$RELEASE_REPO"
    --verify-tag
    --draft
    --title "tyhp ${tag}"
    --notes-file "$NOTES_FILE"
  )
  local path
  if [[ "$version" == *-* ]]; then
    args+=(--prerelease)
  fi
  if [[ "$GENERATE_NOTES" -eq 1 ]]; then
    args+=(--generate-notes)
  fi
  while IFS= read -r path; do
    args+=("$path")
  done < <(release_asset_paths)
  run_with_retry "gh release create ${tag}" "${args[@]}"
}

upload_release_assets() {
  local tag="$1"
  local paths=()
  local path
  while IFS= read -r path; do
    paths+=("$path")
  done < <(release_asset_paths)
  run_with_retry "gh release upload ${tag}" \
    gh release upload "$tag" "${paths[@]}" --repo "$RELEASE_REPO" --clobber
}

publish_draft_release() {
  local tag="$1"
  run_with_retry "gh release edit ${tag} --draft=false" \
    gh release edit "$tag" --repo "$RELEASE_REPO" --draft=false
}

# --- EXIT trap --------------------------------------------------------------

on_exit() {
  local status=$?
  trap - EXIT
  if [[ $status -ne 0 && "$DRY_RUN" -eq 0 ]]; then
    case "$STAGE" in
      bumped)
        restore_version_files
        echo "Release failed before the commit. The version files were restored." >&2
        ;;
      committed|tagged|pushed|released)
        echo "Release ${RELEASE_TAG} failed after the commit (stage: ${STAGE}). The local commit and tag are kept." >&2
        echo "Fix the cause, then resume from this commit with: scripts/release.sh ${RELEASE_VERSION}" >&2
        ;;
    esac
    if [[ "$BIN_REMOVED" -eq 1 ]]; then
      echo "make build-all removed bin/. Rebuild the Debug compiler with: dotnet build tyhp.csproj" >&2
    fi
  fi
  if [[ -n "$BACKUP_DIR" && -d "$BACKUP_DIR" ]]; then
    rm -rf "$BACKUP_DIR"
  fi
  if [[ -n "$NOTES_FILE" && -f "$NOTES_FILE" ]]; then
    rm -f "$NOTES_FILE"
  fi
  exit "$status"
}

confirm_release() {
  local answer
  printf 'Continue? [y/N] '
  read -r answer
  if [[ "$answer" != "y" && "$answer" != "Y" ]]; then
    echo "Release canceled."
    exit 0
  fi
}

print_next_steps() {
  local version="$1"
  local tag="v${version}"
  cat <<EOF

Next steps:
  - Check the install: INSTALL_DIR=/tmp/tyhp-install-check bash scripts/install.sh --tag ${tag}
  - tyhp-runtime-src: set "version": "${version}" in packages/tyhp/compiler/composer.json,
    commit, then scripts/publish-runtime-packages.sh --package compiler
    (it checks that ${tag} and its checksums.txt exist first).
  - tyhp-docs-src: update the current version (content/intro.md, intro_installation.md,
    release_planning.md, release_prevList.md, release_roadmap.md, project_composerPackages.md).
EOF
  if [[ -z "$(prerelease_part "$version")" && -n "$(prerelease_part "$CURRENT_VERSION")" ]]; then
    cat <<EOF
  - First stable release: README.md, CONTRIBUTING.md, and VERSIONING.md still describe a
    beta (badge color, "Public beta", "Tyhp is in beta"); reword them by hand.
EOF
  fi
}

# --- main -------------------------------------------------------------------

CURRENT_VERSION=""

main() {
  parse_args "$@"
  cd "$PROJECT_ROOT"

  if [[ "$DRY_RUN" -eq 1 ]]; then
    say "DRY RUN: nothing is built, committed, tagged, pushed, or uploaded."
  fi

  CURRENT_VERSION="$(read_current_version)"
  local new_version
  new_version="$(resolve_version "$VERSION_ARG" "$CURRENT_VERSION")"
  local tag="v${new_version}"
  RELEASE_TAG="$tag"
  RELEASE_VERSION="$new_version"

  say "Version: ${CURRENT_VERSION} -> ${new_version} (${tag}, $([[ "$new_version" == *-* ]] && echo prerelease || echo stable))"
  if [[ "$(semver_compare "$new_version" "$CURRENT_VERSION")" == "-1" ]]; then
    die "${new_version} is lower than the current version ${CURRENT_VERSION}."
  fi

  say "Checks:"
  check_tools
  if [[ ${#PROBLEMS[@]} -gt 0 && "$DRY_RUN" -eq 0 ]]; then
    printf 'error: %s\n' "${PROBLEMS[@]}" >&2
    exit 1
  fi
  check_clean_tree
  check_upstream
  check_gh_auth
  check_changelog "$new_version"
  check_runtime_fallback_versions
  check_tag_state "$tag" "$new_version" "$CURRENT_VERSION"

  local mode="$TAG_STATE"
  check_upstream_freshness "$mode"
  if [[ "$mode" == "fresh" ]]; then
    check_version_files "$CURRENT_VERSION" "$new_version"
  fi

  if read_release_state "$tag"; then
    say "  GitHub release ${tag}: ${RELEASE_STATE}"
  else
    problem "could not read the GitHub release ${tag} (see the gh output above)."
  fi
  if [[ "$mode" == "fresh" && "$RELEASE_STATE" != "absent" && -n "$RELEASE_STATE" ]]; then
    problem "a ${RELEASE_STATE} GitHub release ${tag} exists but the tag does not. Inspect it on GitHub before releasing."
  fi

  if [[ "$DRY_RUN" -eq 1 ]]; then
    say ""
    say "Version-file changes:"
    if [[ "$mode" == "fresh" ]]; then
      preview_version_files "$CURRENT_VERSION" "$new_version"
      report_unlisted_version_mentions "$CURRENT_VERSION"
    elif [[ "$mode" == "resume" ]]; then
      say "  (resuming: already committed)"
    else
      say "  (not computed: the tag state blocks the release)"
    fi
    say ""
    if [[ "$GENERATE_NOTES" -eq 1 ]]; then
      say "Release notes (CHANGELOG.md section, then GitHub's generated notes):"
    else
      say "Release notes (CHANGELOG.md section):"
    fi
    if [[ -f "$CHANGELOG" ]]; then
      changelog_section "$new_version" | perl -0777 -pe 's/\A\s+//; s/\s+\z/\n/' | sed 's/^/  | /'
    fi
    say ""
    say "Plan:"
    if [[ "$mode" == "fresh" ]]; then
      if [[ "$CURRENT_VERSION" != "$new_version" ]]; then
        say "  1. bump the version files above"
      else
        say "  1. no bump (tyhp.csproj already holds ${new_version})"
      fi
      say "  2. rm -rf dist; make build-all (deletes bin/ and obj/); write dist/checksums.txt"
      say "  3. git commit 'Release ${tag}' (only when files were bumped); git tag ${tag}"
    elif [[ "$mode" == "resume" ]]; then
      say "  1-3. resume: reuse dist/ when it matches ${new_version}, else make build-all (skipped when the release is already published)"
    fi
    if [[ "$mode" == "fresh" || "$mode" == "resume" ]]; then
      say "  4. git push ${UPSTREAM_REMOTE:-<remote>} HEAD:${UPSTREAM_BRANCH:-<branch>} (if needed); git push ${UPSTREAM_REMOTE:-<remote>} refs/tags/${tag} (if needed)"
      case "$RELEASE_STATE" in
        published) say "  5. release already published: verify assets only" ;;
        draft) say "  5. upload assets to the draft (--clobber), verify SHA-256, publish" ;;
        *) say "  5. gh release create ${tag} --verify-tag --draft$([[ "$new_version" == *-* ]] && echo ' --prerelease') with ${#EXPECTED_ASSETS[@]} binaries + checksums.txt; verify SHA-256; publish" ;;
      esac
      if [[ "$DEBUG_BUILD" -eq 1 ]]; then
        say "  6. dotnet build tyhp.csproj (restores bin/Debug/net9.0/tyhp.dll)"
      fi
    else
      say "  (the tag state blocks the release; see the problems below)"
    fi
    say ""
    if [[ ${#PROBLEMS[@]} -gt 0 ]]; then
      say "DRY RUN: a real run would stop with ${#PROBLEMS[@]} problem(s):"
      printf '  - %s\n' "${PROBLEMS[@]}"
      exit 1
    fi
    say "DRY RUN: all checks passed."
    exit 0
  fi

  if [[ ${#PROBLEMS[@]} -gt 0 ]]; then
    printf 'error: %s\n' "${PROBLEMS[@]}" >&2
    exit 1
  fi

  if [[ "$mode" == "resume" ]]; then
    say "Resuming the release of ${tag} from HEAD."
  fi
  confirm_release

  trap on_exit EXIT

  if [[ "$mode" == "fresh" ]]; then
    if [[ "$CURRENT_VERSION" != "$new_version" ]]; then
      BACKUP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/tyhp-release-backup.XXXXXX")"
      STAGE="bumped"
      say "Bumping version files:"
      apply_version_files "$CURRENT_VERSION" "$new_version"
    else
      say "Releasing current version ${new_version} (no bump)."
    fi
    build_dist "$new_version"
    if [[ "$CURRENT_VERSION" != "$new_version" ]]; then
      local paths=()
      local path
      while IFS= read -r path; do
        paths+=("$path")
      done < <(version_file_paths)
      git add -- "${paths[@]}"
      git commit -m "Release ${tag}"
      STAGE="committed"
    fi
    git tag "$tag"
    STAGE="tagged"
  else
    STAGE="tagged"
    if dist_is_reusable "$new_version"; then
      DIST_MATCHES=1
      say "Reusing dist/ (checksums and version match ${new_version})."
    elif [[ "$RELEASE_STATE" == "published" ]]; then
      say "The release is already published; it is checked against its own checksums.txt instead of a rebuild."
    else
      say "dist/ does not match ${new_version}; rebuilding."
      build_dist "$new_version"
    fi
  fi

  local upstream="${UPSTREAM_REMOTE}/${UPSTREAM_BRANCH}"
  if git merge-base --is-ancestor HEAD "$upstream" 2>/dev/null; then
    say "${upstream} already contains HEAD."
  else
    run_with_retry "git push" git push "$UPSTREAM_REMOTE" "HEAD:refs/heads/${UPSTREAM_BRANCH}"
  fi
  if [[ -n "$(git ls-remote --tags "$UPSTREAM_REMOTE" "refs/tags/${tag}")" ]]; then
    say "${UPSTREAM_REMOTE} already has ${tag}."
  else
    run_with_retry "git push ${tag}" git push "$UPSTREAM_REMOTE" "refs/tags/${tag}"
  fi
  STAGE="pushed"

  write_release_notes "$new_version"
  read_release_state "$tag"
  case "$RELEASE_STATE" in
    absent)
      create_draft_release "$tag" "$new_version"
      verify_remote_assets "$tag"
      publish_draft_release "$tag"
      ;;
    draft)
      upload_release_assets "$tag"
      verify_remote_assets "$tag"
      publish_draft_release "$tag"
      ;;
    published)
      if [[ "$DIST_MATCHES" -eq 1 ]]; then
        verify_remote_assets "$tag"
      else
        verify_remote_assets_self_consistent "$tag"
      fi
      ;;
  esac
  read_release_state "$tag"
  if [[ "$RELEASE_STATE" != "published" ]]; then
    die "${tag} is still ${RELEASE_STATE} on GitHub."
  fi
  STAGE="released"

  local release_url
  release_url="$(run_with_retry_output "gh release view ${tag} --json url" \
    gh release view "$tag" --repo "$RELEASE_REPO" --json url --jq .url)"
  say "Release published: ${release_url}"

  if [[ "$DEBUG_BUILD" -eq 1 ]]; then
    say "Rebuilding the Debug compiler (make build-all removed bin/)..."
    dotnet build "$CS_PROJ"
    BIN_REMOVED=0
  elif [[ "$BIN_REMOVED" -eq 1 ]]; then
    say "bin/ was removed by make build-all. Rebuild with: dotnet build tyhp.csproj"
  fi

  STAGE="done"
  print_next_steps "$new_version"
}

main "$@"
