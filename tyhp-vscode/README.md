# Tyhp Language Support for VS Code / Cursor

Syntax highlighting for `.tyhp` and `.tyhpdef` files, plus a standalone PHP grammar.

## Features

- **Tyhp language** (`source.tyhp`) — full standalone grammar for `.tyhp` and `.tyhpdef` files:
  - Optional `<?tyhp` / `<?tyhpdef` open tags (tagless files highlight as Tyhp code from line 1)
  - Classic mode with open/close tags for inline HTML interleaving
  - `async` / `await`
  - `struct`, `extension`, `type` alias declarations
  - `operator` overload declarations (arithmetic, comparison, conversion)
  - `using` blocks and `:=` using-assignment
  - `is` / `isa` / `isan` type checks
  - `typeof`, `nameof`, `variable_exists` expressions
  - `deprecated`, `obsolete` modifiers
  - `decimal` type and `(decimal)` cast
  - `with` keyword
  - Generic type parameters (`<T>`, `<T extends Type>`, nested generics)
  - Constructor delegation (`: parent(...)`, `: void`)
  - Type interpolation in strings (`"${TypeName}"`, `"${A|B}"`, `"${Type+}"`)
- **PHP (Tyhp)** (`source.tyhp.php`) — full standalone PHP grammar, manually selectable for `.php` files
- Comment toggling, bracket matching, auto-closing pairs, folding, and indentation

## Local Installation

### Option 1: Symlink (recommended for development)

Changes to the grammar are picked up on each window reload — no reinstall needed.

**Important:** The symlink target must be an **absolute path** (e.g. `/Users/you/repos/tyhp/tyhp-vscode`). Do not use a relative path like `./tyhp-vscode` — `ln -s` resolves relative targets from the extensions directory, not your current shell directory, which creates a broken self-referential symlink.

The symlink must also use Cursor/VS Code's expected folder name: `{publisher}.{name}-{version}` (from `package.json`). For this extension that is `tyhp-lang.tyhp-language-0.2.1`.

From the repo root:

```bash
rm -rf ~/.cursor/extensions/tyhp-lang.tyhp-language-0.2.1   # Cursor
ln -s "$(pwd)/tyhp-vscode" ~/.cursor/extensions/tyhp-lang.tyhp-language-0.2.1

rm -rf ~/.vscode/extensions/tyhp-lang.tyhp-language-0.2.1  # VS Code
ln -s "$(pwd)/tyhp-vscode" ~/.vscode/extensions/tyhp-lang.tyhp-language-0.2.1
```

Or use an explicit absolute path:

**Cursor:**

```bash
rm -rf ~/.cursor/extensions/tyhp-lang.tyhp-language-0.2.1
ln -s "/path/to/tyhp/tyhp-vscode" ~/.cursor/extensions/tyhp-lang.tyhp-language-0.2.1
```

**VS Code:**

```bash
rm -rf ~/.vscode/extensions/tyhp-lang.tyhp-language-0.2.1
ln -s "/path/to/tyhp/tyhp-vscode" ~/.vscode/extensions/tyhp-lang.tyhp-language-0.2.1
```

Then **fully quit and reopen** Cursor/VS Code (`Cmd+Q`, not just "Reload Window"). If you previously had a broken install, also remove the extension from `~/.cursor/extensions/.obsolete` if it lists `tyhp-lang.tyhp-language-0.2.1`.

### Option 2: Package as VSIX

```bash
npm install -g @vscode/vsce
cd tyhp-vscode
vsce package
```

Then install the generated `.vsix`:

**Cursor:**

```bash
cursor --install-extension tyhp-language-0.2.1.vsix
```

**VS Code:**

```bash
code --install-extension tyhp-language-0.2.1.vsix
```

### Option 3: Copy to extensions directory

**Cursor:**

```bash
cp -r tyhp-vscode ~/.cursor/extensions/tyhp-lang.tyhp-language-0.2.1
```

**VS Code:**

```bash
cp -r tyhp-vscode ~/.vscode/extensions/tyhp-lang.tyhp-language-0.2.1
```

Then fully quit and reopen the editor.

## Uninstalling

If installed via symlink or copy, remove the folder from the extensions directory:

```bash
rm -rf ~/.cursor/extensions/tyhp-lang.tyhp-language-0.2.1
# or
rm -rf ~/.vscode/extensions/tyhp-lang.tyhp-language-0.2.1
```

If installed via VSIX:

```bash
cursor --uninstall-extension tyhp-lang.tyhp-language
# or
code --uninstall-extension tyhp-lang.tyhp-language
```
