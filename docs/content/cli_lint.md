---
title: 'CLI: Lint'
status:
  tier: 1
  story: '12'
  state: complete
---

The lint action checks your Tyhp project for errors and warnings without generating any output files. It runs the compilation pipeline through parse, bind, and check — but skips the emitter and file writing. This makes it faster than a full build and ideal for development feedback and CI/CD integration.

## Usage

```
tyhp lint [options] [files...]
```

## Options

- --tyhp-project=<path> — Path to the tyhp.json project file.
- --format=<format> — Output format: text (default), json, or sarif.
- --file=<path> — Lint a single file instead of the entire project.
- --fix — Apply auto-fixable changes to source files (experimental).
- --max-fix-iterations=<n> — Maximum auto-fix re-run iterations (default: 10; also checker.maxFixIterations).
- --quiet — Suppress the banner and non-diagnostic output.
- --strict — Treat warnings as errors.
- --cache-dir=<path> — Override the AST cache directory.
- --no-cache — Disable the on-disk parse cache for this run.
- --include=<glob> — Include additional source paths.
- --exclude=<glob> — Exclude source paths.

## Output Formats

The --format flag controls how diagnostics are presented. Three formats are supported:

## Text Format (Default)

Human-readable output to the console, showing each diagnostic with file path, line, column, severity, code, and message:

```
src/Models/User.tyhp(42,8): error TYHP4002: Multiple visibility modifiers specified
src/Services/Auth.tyhp(15,10): warning TYHP3003: Symbol 'LegacyUser' is deprecated

Lint complete: 42 files checked, 1 error, 1 warning (parse: 0.4s, bind: 0.3s, check: 0.2s)
```

## JSON Format

Machine-readable JSON output to stdout, suitable for IDE and CI/CD tool consumption. When using --format=json, only the JSON document is written to stdout — all progress and informational messages are suppressed or sent to stderr. Range coordinates are 0-based for both line and column (internal 1-based lines are converted by subtracting 1).

```json
{
    "version": "1.0",
    "tool": { "name": "tyhp", "version": "805.0.0" },
    "diagnostics": [
        {
            "severity": "error",
            "code": "TYHP4002",
            "message": "Multiple visibility modifiers specified",
            "file": "src/Models/User.tyhp",
            "range": {
                "start": { "line": 41, "column": 8 },
                "end": { "line": 41, "column": 25 }
            }
        }
    ],
    "summary": {
        "filesChecked": 42,
        "errorCount": 1,
        "warningCount": 1,
        "infoCount": 0,
        "durations": {
            "parseMs": 400,
            "bindMs": 300,
            "checkMs": 200,
            "totalMs": 900
        }
    }
}
```

## SARIF Format

SARIF (Static Analysis Results Interchange Format) v2.1.0 output for integration with GitHub Code Scanning, Azure DevOps, and other CI platforms that consume SARIF. When using --format=sarif, only the SARIF document is written to stdout — all progress and informational messages are suppressed. Region coordinates are 1-based for both line and column (internal 0-based columns are converted by adding 1). File URIs are relative to the project root.

```json
{
    "$schema": "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/main/sarif-2.1/schema/sarif-schema-2.1.0.json",
    "version": "2.1.0",
    "runs": [
        {
            "tool": {
                "driver": {
                    "name": "tyhp",
                    "version": "805.0.0",
                    "informationUri": "https://tyhp.dev",
                    "rules": [
                        {
                            "id": "TYHP4002",
                            "shortDescription": { "text": "cannot have multiple visibility modifiers." },
                            "defaultConfiguration": { "level": "error" }
                        }
                    ]
                }
            },
            "results": [
                {
                    "ruleId": "TYHP4002",
                    "level": "error",
                    "message": { "text": "property cannot have multiple visibility modifiers." },
                    "locations": [
                        {
                            "physicalLocation": {
                                "artifactLocation": { "uri": "src/Models/User.tyhp" },
                                "region": {
                                    "startLine": 42,
                                    "startColumn": 9,
                                    "endLine": 42,
                                    "endColumn": 26
                                }
                            }
                        }
                    ]
                }
            ]
        }
    ]
}
```

```
tyhp lint --format=sarif > results.sarif
```

## Single-File Mode

Use the --file flag to lint a single file instead of the entire project. This provides faster feedback during development. The compiler still loads tyhpdef files and built-in types so that cross-file type references can be resolved. The text summary and JSON summary.file field include the target path.

```
tyhp lint --file=src/Models/User.tyhp
```

:::note
In single-file mode, references to symbols defined in other project files will produce unresolved-reference diagnostics unless those files are also included in the compilation context. For full cross-file validation, lint the entire project.
:::

## Auto-Fix (Experimental)

The --fix flag enables auto-fixable issue resolution. When specified, the lint action attempts to automatically fix common issues such as:

- Adding missing type annotations (when types can be inferred)
- Adding missing import/use statements
- Removing unused import/use statements
- Sorting import/use statements

Before modifying any source file, the auto-fixer creates a backup. After fixes are applied, the file is re-parsed and re-checked to ensure the fixes did not introduce new issues.

:::warning
The --fix flag is experimental. Always review the changes it makes. Source files are backed up before modification.
:::

## Exit Codes

- 0 (Success) — No errors or warnings found.
- 1 (GenericError) — Lint was cancelled (e.g., Ctrl+C).
- 4 (CompileError) — One or more errors found.
- 5 (CompileWarning) — Warnings found but no errors.

## CI/CD Integration

The lint command is designed for CI/CD pipeline integration. Use the JSON or SARIF output formats for machine-readable results, and check the exit code to determine pass/fail status:

```
# GitHub Actions example
- name: Lint Tyhp
  run: tyhp lint --format=sarif > results.sarif

- name: Upload SARIF
  uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: results.sarif
```
