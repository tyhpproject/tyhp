# AIDevGuide — teaching an AI agent to write Tyhp

This folder teaches an AI coding agent to write and maintain **Tyhp** application code by building on
its existing PHP knowledge, so it stops guessing at Tyhp syntax/semantics. Copy the whole `AIDevGuide/`
folder into your Tyhp project (e.g. at the repo root or under `docs/`).

## Layout

| Path | What it is |
|------|-----------|
| `AGENTS.md` | Entry point for agents (auto-read by many tools). Explains the docs + the few PHP habit-breakers. |
| `CLAUDE.md` | Same entry point for Claude-based tools (points to `AGENTS.md`). |
| `QUICK_GUIDE.md` | One-line-per-feature **index** with cross-language analogies (C#/TS/…) and `→` pointers to the exact section file. ~1.3k tokens — cheap enough to keep always-on. |
| `guide/` | The core language guide, **one file per section** (`01-mental-model.md` … `29-php-mapping.md`), plus `00-index.md`. The PHP→Tyhp delta. |
| `handbook/` | Setup / autoloading / CLI / interop / testing / examples / runtime-API, **one file per section** (`01`…`07`), plus `00-index.md`. |
| `README.md` | This file. |
| `REGEN.md` | Maintainer-only: prompt the Tyhp project uses to regenerate this folder alongside compiler changes. End users don't run it. |

**Reading model:** an agent starts from `QUICK_GUIDE.md` (or `AGENTS.md`), then opens **only** the
one section file it needs via the `→` pointer. The `00-index.md` in each folder (or a plain folder
listing) shows everything available, so the agent never has to grep or load an entire document.

## Install it in your Tyhp project

### Option A — Cursor rule (always-on index, deeper files on demand) — recommended
Create `.cursor/rules/tyhp.mdc`:
```mdc
---
description: How to write Tyhp (typed PHP superset). Applies when editing Tyhp files.
globs: ["**/*.tyhp", "**/*.tyhpdef"]
alwaysApply: false
---
@AIDevGuide/QUICK_GUIDE.md
```
The `@`-reference pulls the ~1.3k-token index in only when a `.tyhp`/`.tyhpdef` file is in play; the
agent then opens individual `guide/`/`handbook/` files as the task requires. Don't attach the whole
`guide/` — that defeats the split.

### Option B — `AGENTS.md`
This folder's `AGENTS.md` is already an agent entry point. Either place `AIDevGuide/` at your repo
root (many tools read a nested `AGENTS.md` when working in that subtree) or add a pointer to your
project-root `AGENTS.md`:
```md
This is a **Tyhp** project. Before writing/editing `.tyhp` / `.tyhpdef`, read `AIDevGuide/AGENTS.md`.
```

### Option C — any other agent/tool
Paste `QUICK_GUIDE.md` into the system prompt / project instructions, and make the `guide/` and
`handbook/` files reachable so the agent can open the referenced section.

## Token-efficiency tips

- **Load only what's needed (highest yield).** Keep `QUICK_GUIDE.md` as the always-on index and let
  the agent open one section file at a time — far cheaper than loading a monolith.
- **Lean on prompt caching.** If your tool/provider caches static prompt prefixes (Anthropic/OpenAI
  do), the stable index is nearly free after the first call.
- **Don't run automated token-pruners** (LLMLingua, etc.) — they strip "low-information" tokens and
  will corrupt the verbatim Tyhp/PHP code. Only prose is safe to compress.
- Don't hardcode line numbers in pointers — filenames are the stable anchor; sections move on edit.

## Keep it accurate

- `guide/28-availability-gotchas.md` reflects what the current Tyhp toolchain actually compiles;
  the "use the PHP form instead" table keeps the agent from emitting code that won't build. The
  `handbook/` marks ⚠️ for unimplemented tooling (`tyhp init`, source maps, a test runner, `psr4`
  folder remapping, etc.).
- **Pin to your Tyhp version.** This `AIDevGuide/` folder ships with Tyhp and is updated in the same
  commit as the compiler changes — don't regenerate it yourself. When you upgrade Tyhp, take the
  matching `AIDevGuide/` for that version and re-copy it into your project.
- **Never translate or reformat the code, keywords, type names, or identifiers** — they are literal
  Tyhp/PHP syntax.
