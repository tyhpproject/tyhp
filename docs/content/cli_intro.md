---
title: 'CLI: Introduction'
status:
  tier: 1
  story: '13'
  state: complete
---

The Tyhp CLI is the primary tool for working with Tyhp projects. In this alpha it covers building, linting, project init, version, help, and Composer-aware project wiring.

## Usage

```
tyhp <action> [options]
```

## Available in this alpha

- `build` — Compile `.tyhp` source files into `.php` output. Runs parse, bind, check, emit.
- `lint` — Check the project for errors and warnings without producing output files. Supports text, JSON, and SARIF formats.
- `init` — Initialize a new Tyhp project with a default `tyhp.json`.
- `version` — Display the compiler version.
- `help` — Display help. `tyhp help --subject=<action>` for action-specific help.
- `composer` — Run Composer with Tyhp-aware processing when configured.

## Not in this alpha

These actions are planned (Tier 2) and are **not** available in 805.0.0-alpha.1:

- `language_server` — Language Server Protocol
- `xdebug_proxy` — Xdebug + sourcemap debugging
- `generate_tyhpdef` — Reflection-based tyhpdef generation

## Global Options

- `--help` — Display help. Use `tyhp help --subject=<action>` for action-specific help.
- `--version` — Display the Tyhp compiler version number.
- `--tyhp-project=<path>` — Path to the `tyhp.json` project file (defaults to `./tyhp.json`).
- `--quiet` — Suppress the banner and non-diagnostic output.
- `--verbose` — Enable detailed output for each compilation phase.
- `--locale=<locale>` — Set the locale for diagnostic messages (default: en-US).

## Project File

Most actions require a `tyhp.json` project configuration file. The CLI looks for this file in the current directory by default, or you can specify a path with `--tyhp-project`. If no `tyhp.json` is found, the compiler uses sensible defaults and displays an informational message.

## Exit Codes

- 0 (Success) — No errors; build completed successfully.
- 4 (CompileError) — One or more errors during compilation.
- 5 (CompileWarning) — Warnings but no errors (strict mode for build; always for lint).

## Getting Help

```
tyhp help --subject=build
tyhp help --subject=lint
tyhp help --subject=init
```
