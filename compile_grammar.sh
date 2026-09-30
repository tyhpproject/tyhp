#!/bin/bash

cd "$(dirname "$0")"

GREEN="\033[32m"
RED="\033[31m"
RESET="\033[0m"

GRAMMAR_DIR=./Tyhp/TyhpLang/Grammar
PARSER_DIR=./Tyhp/TyhpLang/Parser

echo "checking for antlr-ng command..."
if command -v antlr-ng &> /dev/null; then
    echo -e "${GREEN}**  antlr-ng command found${RESET}"
else
    echo -e "${RED}**  antlr-ng command not found, install it with: npm install -g antlr-ng${RESET}"
    exit 1
fi

EXPAND_DIR=$(mktemp -d "${TMPDIR:-/tmp}/tyhp-grammar.XXXXXX")
echo "expanding includes into ${EXPAND_DIR}"

python3 - "$GRAMMAR_DIR" "$EXPAND_DIR" << 'PY'
import re
import sys
from pathlib import Path

grammar_dir = Path(sys.argv[1]).resolve()
expand_dir = Path(sys.argv[2]).resolve()
include_re_prefix = "// #include \""

def expand(path: Path, stack: tuple[Path, ...]) -> str:
    if path in stack:
        cycle = " -> ".join(str(p) for p in stack + (path,))
        raise SystemExit(f"include cycle: {cycle}")
    text = path.read_text(encoding="utf-8")
    out = []
    for line in text.splitlines(keepends=True):
        stripped = line.strip()
        if stripped.startswith(include_re_prefix) and stripped.endswith('"'):
            rel = stripped[len(include_re_prefix):-1]
            part = None
            if re.fullmatch(r".+#\d+", rel):
                rel, part_s = rel.rsplit("#", 1)
                part = int(part_s)
            inc = (path.parent / rel).resolve()
            if not inc.is_file():
                raise SystemExit(f"{path}: include not found: {rel}")
            included = expand(inc, stack + (path,))
            if part is not None:
                parts = re.split(r"(?m)^// #part\n", included)
                if part < 1 or part > len(parts):
                    raise SystemExit(f"{path}: {rel} has no part {part} ({len(parts)} parts)")
                included = parts[part - 1]
            if included and not included.endswith("\n"):
                included += "\n"
            out.append(included)
        else:
            out.append(line)
    return "".join(out)

def brace_end(text: str, open_at: int) -> int:
    depth = 0
    j = open_at
    while j < len(text):
        if text[j] == "{":
            depth += 1
        elif text[j] == "}":
            depth -= 1
            if depth == 0:
                return j + 1
        j += 1
    raise SystemExit("unbalanced brace in lexer prequel")

def extract_prequel(head: str):
    """Split the text before the first mode into prequel blocks and default-mode rules."""
    i = 0
    n = len(head)
    pieces = []
    while i < n:
        if head[i] in " \t\r\n":
            i += 1
            continue
        if head.startswith("//", i):
            eol = head.find("\n", i)
            i = n if eol < 0 else eol + 1
            continue
        if head.startswith("/*", i):
            end = head.find("*/", i)
            i = end + 2
            continue
        if head[i] == "@":
            pieces.append(("header", head[i:brace_end(head, head.find("{", i))]))
            i = brace_end(head, head.find("{", i))
            continue
        matched = False
        for key in ("channels", "options", "tokens"):
            if head.startswith(key, i) and (i + len(key) == n or head[i + len(key)] in " \t\r\n{"):
                end = brace_end(head, head.find("{", i))
                pieces.append((key, head[i:end]))
                i = end
                matched = True
                break
        if matched:
            continue
        if head.startswith("lexer grammar", i):
            eol = head.find("\n", i)
            i = n if eol < 0 else eol + 1
            continue
        return pieces, head[i:]
    return pieces, ""

def inner_brace(block: str) -> str:
    return block[block.find("{") + 1:block.rfind("}")]

def ensure_trailing_comma(inner: str) -> str:
    stripped = inner.rstrip()
    if stripped.endswith(","):
        return inner
    return stripped + "," + inner[len(stripped):]

def merge_tyhp_lexer(expanded: str, root_source: str, grammar_name: str) -> str:
    """Reproduce the old `import PhpLexer` mode order after a textual include.

    antlr rejects two options/tokens prequels, and import placed root-grammar
    rules ahead of imported rules in a shared mode. Rule text is unchanged.
    """
    lines = expanded.splitlines(keepends=True)
    mode_at = [i for i, line in enumerate(lines) if re.match(r"^mode\s+[A-Za-z0-9_]+\s*;\s*$", line.rstrip("\n"))]
    if not mode_at:
        raise SystemExit("TyhpLexer expansion has no modes")
    head = "".join(lines[:mode_at[0]])
    modes = []
    for j, i in enumerate(mode_at):
        end = mode_at[j + 1] if j + 1 < len(mode_at) else len(lines)
        name = re.match(r"^mode\s+([A-Za-z0-9_]+)", lines[i]).group(1)
        modes.append((name, "".join(lines[i:end])))
    pieces, default_rules = extract_prequel(head)
    by_mode = {}
    body_order = []
    for name, chunk in modes:
        by_mode.setdefault(name, []).append(chunk)
        if name not in body_order:
            body_order.append(name)
    root_modes = re.findall(r"^mode\s+([A-Za-z0-9_]+)\s*;", root_source, re.M)

    def strip_mode_line(chunk: str) -> str:
        return chunk[chunk.find("\n") + 1:]

    def rules_for(name: str, which: str) -> str:
        chunks = by_mode.get(name, [])
        if name not in root_modes:
            return strip_mode_line(chunks[0]) if which == "body" else ""
        if len(chunks) == 1:
            return strip_mode_line(chunks[0]) if which == "root" else ""
        body, root = chunks[0], chunks[1]
        return strip_mode_line(root if which == "root" else body)

    tokens_blocks = [v for k, v in pieces if k == "tokens"]
    options_blocks = [v for k, v in pieces if k == "options"]
    if len(tokens_blocks) != 2 or len(options_blocks) < 1:
        raise SystemExit(f"expected Tyhp and PHP lexer prequels, got tokens={len(tokens_blocks)} options={len(options_blocks)}")
    merged_tokens = "tokens {" + ensure_trailing_comma(inner_brace(tokens_blocks[0])) + inner_brace(tokens_blocks[1]) + "}"
    out = [f"lexer grammar {grammar_name};\n\n"]
    for kind, block in pieces:
        if kind == "header":
            out.append(block)
            if not block.endswith("\n"):
                out.append("\n")
            out.append("\n")
    for _, block in ((k, v) for k, v in pieces if k == "channels"):
        out.append(block + "\n\n")
    out.append(options_blocks[0] + "\n\n")
    out.append(merged_tokens + "\n\n")
    out.append(default_rules)
    if default_rules and not default_rules.endswith("\n"):
        out.append("\n")
    for mode in root_modes:
        out.append(f"mode {mode};\n")
        out.append(rules_for(mode, "root"))
        out.append(rules_for(mode, "body"))
    for mode in body_order:
        if mode in root_modes:
            continue
        out.append(f"mode {mode};\n")
        out.append(rules_for(mode, "body"))
    return "".join(out)

entries = [
    "TyhpLexer.g4",
    "TyhpParser.g4",
    "PhpLexer.g4",
    "PhpParser.g4",
    "TyhpdefLexer.g4",
    "TyhpdefParser.g4",
]
for name in entries:
    src = grammar_dir / name
    expanded = expand(src, ())
    if name in ("TyhpLexer.g4", "TyhpdefLexer.g4"):
        expanded = merge_tyhp_lexer(expanded, src.read_text(encoding="utf-8"), name[:-3])
    (expand_dir / name).write_text(expanded, encoding="utf-8")
    print(f"expanded {name} ({len(expanded)} bytes)")
PY

if [ $? -ne 0 ]; then
    echo -e "${RED}**  include expansion failed${RESET}"
    exit 1
fi

# Compile lexer first so the token vocabulary in --lib is up-to-date
# before the parser reads it via tokenVocab=TyhpLexer
antlr-ng --define language=CSharp \
    --output-directory "$PARSER_DIR" \
    --package Tyhp.TyhpLang.Parser \
    --generate-visitor true \
    --generate-listener false \
    --long-messages true \
    --lib "$EXPAND_DIR" \
    "$EXPAND_DIR/TyhpLexer.g4"
if [ $? -ne 0 ]; then
    echo -e "${RED}**  antlr-ng lexer pass failed${RESET}"
    exit 1
fi
if [ ! -f "$PARSER_DIR/TyhpLexer.tokens" ]; then
    echo -e "${RED}**  antlr-ng lexer pass did not write TyhpLexer.tokens${RESET}"
    exit 1
fi
mv "$PARSER_DIR"/*.tokens "$GRAMMAR_DIR"
mv "$PARSER_DIR"/*.interp "$GRAMMAR_DIR"
cp "$GRAMMAR_DIR"/*.tokens "$EXPAND_DIR"/

# Now compile both lexer and parser — the parser will see the updated token vocabulary
antlr-ng --define language=CSharp \
    --output-directory "$PARSER_DIR" \
    --package Tyhp.TyhpLang.Parser \
    --generate-visitor true \
    --generate-listener false \
    --long-messages true \
    --lib "$EXPAND_DIR" \
    "$EXPAND_DIR/TyhpLexer.g4" "$EXPAND_DIR/TyhpParser.g4"
status=$?
mv "$PARSER_DIR"/*.tokens "$GRAMMAR_DIR"
mv "$PARSER_DIR"/*.interp "$GRAMMAR_DIR"
if [ $status -ne 0 ]; then
    rm -rf "$EXPAND_DIR"
    echo -e "${RED}**  antlr-ng parser pass failed${RESET}"
    exit $status
fi

# TyhpdefLexer first so tokenVocab=TyhpdefLexer sees a fresh .tokens file in --lib
antlr-ng --define language=CSharp \
    --output-directory "$PARSER_DIR" \
    --package Tyhp.TyhpLang.Parser \
    --generate-visitor true \
    --generate-listener false \
    --long-messages true \
    --lib "$EXPAND_DIR" \
    "$EXPAND_DIR/TyhpdefLexer.g4"
if [ $? -ne 0 ]; then
    rm -rf "$EXPAND_DIR"
    echo -e "${RED}**  antlr-ng tyhpdef lexer pass failed${RESET}"
    exit 1
fi
if [ ! -f "$PARSER_DIR/TyhpdefLexer.tokens" ]; then
    rm -rf "$EXPAND_DIR"
    echo -e "${RED}**  antlr-ng tyhpdef lexer pass did not write TyhpdefLexer.tokens${RESET}"
    exit 1
fi
mv "$PARSER_DIR"/*.tokens "$GRAMMAR_DIR"
mv "$PARSER_DIR"/*.interp "$GRAMMAR_DIR"
cp "$GRAMMAR_DIR"/*.tokens "$EXPAND_DIR"/

antlr-ng --define language=CSharp \
    --output-directory "$PARSER_DIR" \
    --package Tyhp.TyhpLang.Parser \
    --generate-visitor true \
    --generate-listener false \
    --long-messages true \
    --lib "$EXPAND_DIR" \
    "$EXPAND_DIR/TyhpdefLexer.g4" "$EXPAND_DIR/TyhpdefParser.g4"
status=$?
mv "$PARSER_DIR"/*.tokens "$GRAMMAR_DIR"
mv "$PARSER_DIR"/*.interp "$GRAMMAR_DIR"
rm -rf "$EXPAND_DIR"
if [ $status -ne 0 ]; then
    echo -e "${RED}**  antlr-ng tyhpdef parser pass failed${RESET}"
    exit $status
fi

python3 ./compile_grammar_emit_partials.py
if [ $? -ne 0 ]; then
    echo -e "${RED}**  grammar-method / visitor include emit failed${RESET}"
    exit 1
fi

# antlr4 -Dlanguage=CSharp "$GRAMMAR_DIR/TyhpLexer.g4" "$GRAMMAR_DIR/TyhpParser.g4" -o "$PARSER_DIR" -package Tyhp.TyhpLang.Parser -visitor -no-listener -long-messages
