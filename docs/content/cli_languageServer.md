---
title: 'CLI: Language Server (LSP)'
status:
  tier: 2
  story: '19'
  state: planned
---

:::warning Not in this alpha
This feature is **not included** in Tyhp 805.0.0-alpha.1 (roadmap Tier 2/3). The rest of this page describes the planned design. Do not expect these commands or syntax to work yet.
:::


The language_server action starts the Tyhp Language Server Protocol (LSP) server. This enables rich IDE integration features including real-time diagnostics, code completion, hover information, go-to-definition, find references, rename, and code actions.

## Usage

```
tyhp language_server [options]
```

## How It Works

The language server is a long-running process that communicates with your IDE using the Language Server Protocol (LSP) over standard input/output. It uses the same compilation pipeline as the build and lint commands — parsing, binding, and type checking — but runs continuously and updates as you edit files.

When the language server starts, it loads your tyhp.json project configuration, discovers source and tyhpdef files, and builds an initial representation of your project. As you edit files, it incrementally re-processes changed files to provide real-time feedback.

## Features

The Tyhp language server provides the following capabilities:

- Diagnostics — Real-time error and warning reporting as you type. Shows the same diagnostics as tyhp lint but updated live.
- Completions — Context-aware code completion for variables, functions, classes, methods, properties, types, keywords, and import suggestions.
- Hover — Type information and documentation on hover. Shows the resolved type of variables, function signatures, class members, and tyhpdef documentation.
- Go to Definition — Navigate to the declaration of any symbol: classes, functions, methods, properties, variables, type aliases, and imported symbols.
- Find References — Find all usages of a symbol across the project.
- Rename — Rename a symbol and update all references consistently across the project.
- Code Actions — Quick fixes and refactoring suggestions, such as adding missing imports, organizing use statements, and adding type annotations.
- Document Symbols — Outline view showing all declarations (classes, functions, constants) in the current file.
- Workspace Symbols — Search for any symbol across the entire project by name.
- Signature Help — Parameter hints shown while typing function and method calls.

## IDE Integration

The Tyhp language server can be used with any IDE or editor that supports the Language Server Protocol. Below are setup instructions for popular editors.

## VS Code

Install the Tyhp VS Code extension from the marketplace, or configure a custom language server client in your settings.json:

```json
{
    "tyhp.languageServer.path": "tyhp",
    "tyhp.languageServer.args": ["language_server"],
    "tyhp.projectPath": "./tyhp.json"
}
```

## JetBrains IDEs (PhpStorm, IntelliJ)

JetBrains IDEs support LSP via the built-in LSP support (2023.2+) or through third-party plugins. Configure the language server by adding a custom LSP server definition pointing to the tyhp language_server command.

## Other Editors

Any editor with LSP support can integrate with the Tyhp language server. Configure it to launch tyhp language_server as the server process, communicating over stdin/stdout. Editors known to support LSP include Sublime Text (via LSP package), Neovim (via nvim-lspconfig), Emacs (via lsp-mode or eglot), and Helix.

## Options

- --tyhp-project=<path> — Path to the tyhp.json project file. The language server uses this to discover source files and configuration.
- --quiet — Suppress startup banner output (useful when the IDE captures stderr).

:::note
The language server is a long-running process. It should be started by your IDE automatically and does not need to be run manually. Use Ctrl+C to stop it if running from the command line.
:::
