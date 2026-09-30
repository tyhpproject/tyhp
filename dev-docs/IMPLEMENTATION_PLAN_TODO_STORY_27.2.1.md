# Implementation Plan: Story 27.2.1 — Separate tyhpdef lexer and parser

> **Roadmap position:** Story 27.2.1 — child of **27.2**. Grammar-pipeline work. It does not change extension syntax. Land it after 27.2’s grammar edits have settled, and before **27.3**, because both this story and 27.2 edit `Tyhp/TyhpLang/Grammar/TyhpLexer.g4` and `TyhpParser.g4`.
> **Direct dependencies:** the current grammars and `./compile_grammar.sh`. No dependency on optimizer, `internal` enforcement, or `?->` assignment.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`. This story adds no diagnostic codes. See `ROADMAP.md` for the tiered sequence.

> **Source:** Split `.tyhpdef` onto its own lexer and parser so tyhpdef parsing does not build the combined Tyhp lexer DFA. Shared rule text is included by both grammars. PHP token numbers and PHP parse results stay as they are.
> **Branch:** TBD
> **Prerequisites:** `antlr-ng` on `PATH` (`npm install -g antlr-ng`). Regeneration is `./compile_grammar.sh`. `dotnet build` does not run ANTLR.

`.tyhp` and `.php` keep today’s generated `TyhpLexer` / `TyhpParser`. `.tyhpdef` gets `TyhpdefLexer` / `TyhpdefParser`, built from the rules the tyhpdef entries actually reach, plus the lexer rules required to produce the same token stream those files get today. PHP rule text moves only into shared fragments that expand back to the same text, in the same order.

---

## Architecture Overview

### What exists today

ANTLR is not an MSBuild target. `tyhp.csproj` references `Antlr4.Runtime.Standard` 4.13.1 and compiles `Tyhp/**/*.cs` through `<CSFile Include="Tyhp/**/*.cs" />`. Generation is `./compile_grammar.sh`, which runs `antlr-ng` (ANTLR 4.13.2) on two grammars:

| File | Role |
|---|---|
| `Tyhp/TyhpLang/Grammar/PhpLexer.g4` | `lexer grammar PhpLexer`. Not passed to `antlr-ng`. |
| `Tyhp/TyhpLang/Grammar/PhpParser.g4` | `parser grammar PhpParser` with `tokenVocab=PhpLexer`. Not passed to `antlr-ng`. |
| `Tyhp/TyhpLang/Grammar/TyhpLexer.g4` | `lexer grammar TyhpLexer`. `tokens { … }` then `import PhpLexer;`, then more rules in existing modes plus `mode ST_TYHP_TAGLESS`. |
| `Tyhp/TyhpLang/Grammar/TyhpParser.g4` | `parser grammar TyhpParser` with `tokenVocab=TyhpLexer` and `import PhpParser;`. |

`compile_grammar.sh` writes C# into `Tyhp/TyhpLang/Parser/`, then moves `*.tokens` and `*.interp` to `Tyhp/TyhpLang/Grammar/`. Generated types, namespace `Tyhp.TyhpLang.Parser`:

- `TyhpLexer` — `Tyhp/TyhpLang/Parser/TyhpLexer.cs`
- `TyhpParser` — `Tyhp/TyhpLang/Parser/TyhpParser.cs`
- `ITyhpParserVisitor<Result>` — `Tyhp/TyhpLang/Parser/TyhpParserVisitor.cs`
- `TyhpParserBaseVisitor<Result>` — `Tyhp/TyhpLang/Parser/TyhpParserBaseVisitor.cs`

Hand-written partials on those generated types:

- `Tyhp/TyhpLang/Parser/TyhpLexer.GrammarMethods.cs` (`ConfigureTagless`, `streamLA`, `prepareLess`, `doPreparedLess`, `closeTagHandler`, heredoc and nesting helpers)
- `Tyhp/TyhpLang/Parser/TyhpParser.GrammarMethods.cs` (`isLanguageMode`, `isStructKeyword`, `looksLikeStructShape`, `looksLikeAnonymousStruct`, `looksLikeObjectShape`, and the other predicates the grammar actions call)

There is no generated `PhpLexer` or `PhpParser` class. PHP and Tyhp source are both lexed and parsed with `TyhpLexer` / `TyhpParser`.

Hand-written visitors:

- `PhpParserAstVisitor` (`Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.cs` and the `PhpParserAstVisitor.*.cs` partials) extends `TyhpParserBaseVisitor<IBase2Ast?>` and implements `ITyhpParserVisitor<IBase2Ast?>`.
- `TyhpParserAstVisitor` extends `PhpParserAstVisitor`. Tyhpdef visits live in `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.Tyhpdef.cs`.

`Tyhp/TyhpLang/Grammar/TyhpLexer.tokens` is the PHP/Tyhp vocabulary. Tyhp’s `tokens { }` block is 1–24 (`T_TYHP_OPEN_TAG` through `T_NO_GRAMMAR_ADDON_0000`). PhpLexer’s `tokens { }` block follows, starting at `T_ERROR=25`. That order is the import result: the root grammar’s `tokens { }` block is numbered first, then the imported lexer’s tokens, then lexer rules that introduce further names.

### Entries tyhpdef uses

Both entries are live. Tagless mode picks the second.

| Call site | Tagged | Tagless |
|---|---|---|
| `Tyhp/Domain/Services/CompilationService.cs` | `parser.tyhpdefSrcFile()` | `parser.tyhpdefTaglessSrcFile()` |
| `Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.cs` | `parser.tyhpdefSrcFile()` | `parser.tyhpdefTaglessSrcFile()` |
| `Tyhp/CLI/DebugAction.cs` | `parser.tyhpdefSrcFile()` | (tagged path only) |
| `tests/Tyhp.Tests/Parser/TyhpdefExternParseTests.cs`, `TyhpdefPartialFunctionParseTests.cs`, `TyhpdefPartialTypeHeaderParseTests.cs` | `parser.tyhpdefSrcFile()` | |

Grammar:

```
tyhpdefSrcFile
    : tyhpdefBlock EOF                                          #tyhpdefFile

tyhpdefTaglessSrcFile
    : T_TYHPDEF_OPEN_TAG? tyhpdefTopStatementList EOF           #tyhpdefTaglessFile

tyhpdefBlock
    : T_TYHPDEF_OPEN_TAG tyhpdefTopStatementList
```

The block and tagless rules set the **parser** language mode to `"tyhp"`. The **lexer** sets `_languageMode` to `"tyhpdef"` on `<?tyhpdef`. Those two facts stay. Lexer predicates written `== "tyhpdef"` and parser predicates written `isLanguageMode("tyhp")` must keep meaning the same thing.

### Measured closure

Closure is over the merged grammar: a rule defined in both files uses the `TyhpParser.g4` body. Counted from the current `.g4` files: 306 PHP rules, 164 Tyhp rules. From `tyhpdefSrcFile` and `tyhpdefTaglessSrcFile` the walk reaches **393** rules:

- **239** rules defined only in `PhpParser.g4`
- **56** rules whose Tyhp body wins (addons and the type/new/foreach overrides)
- **98** rules defined only in `TyhpParser.g4`

Names are in the appendix. Recompute the closure as the first implementation step. If the unreached set differs from this appendix, stop and update the appendix before moving any rule. Story 27.2 edits the same grammar; a stale list will drop or keep the wrong rules.

**11 PHP rules are outside the closure.** They stay in `PhpParser.g4` and are not included by the tyhpdef grammar:

`attributedConstStatement`, `codeBlock`, `codeBlockGrammarAddon`, `noGrammarAddon`, `phpBlock`, `phpSrcFile`, `topStatement`, `topStatementGrammarAddon`, `topStatementListWithRequiredFinalTerminal`, `topStatementNeedsTerminal`, `topStatementNoTerminal`.

**10 Tyhp rules are outside the closure.** They stay on `TyhpParser` only:

`topStatementGrammarAddon`, `tyhpBlock`, `tyhpCodeBlock`, `tyhpInlineOutput`, `tyhpInlineOutputStatement`, `tyhpSrcFile`, `tyhpTaglessSrcFile`, `tyhpWithList`, `tyhpdefClassConstDecl`, `tyhpdefClassConstList`.

`tyhpdefClassConstDecl` / `tyhpdefClassConstList` are defined and unreferenced. Do not add them to the tyhpdef grammar. Do not delete them from `TyhpParser.g4` in this story.

The closure is large on purpose. Tyhpdef class bodies, signatures, and default expressions reach `expr`, strings, heredocs, attributes, and the type rules. A tyhpdef lexer that deleted `ST_IN_SCRIPTING` would reject source that parses today.

### Locked decisions

1. **Share rule text with a build-time include, not with ANTLR `import`.** `compile_grammar.sh` expands `// #include "shared/<file>.g4"` before `antlr-ng` runs. The directive sits where the rules sit today. The expanded text of the PHP/Tyhp grammars is the same text in the same order, so `TyhpLexer.tokens` does not move.

   ANTLR `import` is the wrong tool for this split. It pulls in an entire grammar, the `import` statement has to sit before rules, and it cannot splice a fragment into the middle of a mode. Lexer priority is longest match, then earliest rule in the mode. A merged import reorders either that priority or the token numbers. Today’s numbers already show the import effect: Tyhp’s `tokens { }` block occupies 1–24 and PhpLexer’s block starts at 25. A second import inside `PhpLexer` would insert another block into that sequence.

   Tyhpdef is a different lexer, so its own token integers may differ from `TyhpLexer`. PHP input is never lexed with `TyhpdefLexer`. Shared C# must use the constants of the parser that built the tree (`TyhpParser.T_*` or `TyhpdefParser.T_*`), not a copied integer.

2. **PHP and Tyhp stay on `TyhpLexer` / `TyhpParser`.** `TyhpLexer.g4` still has its `tokens { }` block, then the full PHP lexer (via include of the shared body, which replaces today’s `import PhpLexer` only after the expanded text matches). `TyhpParser.g4` still has every PHP rule plus every Tyhp rule, including `.tyhp` file rules. `tokenVocab=TyhpLexer` stays.

3. **Tyhpdef includes only the closure, plus the lexer rules that closure needs.** New roots, passed to `antlr-ng` as their own pair:

   - `Tyhp/TyhpLang/Grammar/TyhpdefLexer.g4` — `lexer grammar TyhpdefLexer;`
   - `Tyhp/TyhpLang/Grammar/TyhpdefParser.g4` — `parser grammar TyhpdefParser;` with `tokenVocab=TyhpdefLexer;`

   Shared fragments live in `Tyhp/TyhpLang/Grammar/shared/` and have no `lexer grammar` / `parser grammar` header. They are not valid ANTLR entry files.

4. **Tyhpdef-only rules stay out of the PHP fragments.** Rules whose names start with `tyhpdef` (appendix “Tyhp rules the closure reaches”, the `tyhpdef*` subset) are included by `TyhpdefParser.g4` and removed from the Tyhp parser’s effective rule set only when `.tyhp` / `.php` never call them. They are not pasted into `PhpParser.g4`. Overrides that the closure uses (the 56 names) are a shared fragment included by both `TyhpParser.g4` and `TyhpdefParser.g4`, at the position those rules occupy in `TyhpParser.g4` today.

5. **Do not drop a lexer rule just because no parser alternative names its token.** `T_COMMENT`, `T_DOC_COMMENT`, `T_WHITESPACE`, `T_ERROR`, and `T_BAD_CHARACTER` are absent from the parser-rule token list and are still required. Doc comments are how `PhpParserAstVisitor.FindPossibleDocComment` works (`TyhpLexer.DocBlockCommentsChannel`). Omitting a scripting-mode keyword rule turns that spelling into `T_STRING` and can make today’s rejected tyhpdef source parse.

   The tyhpdef lexer therefore includes the whole current `PhpLexer.g4` rule body (all of its modes). It does **not** include Tyhp keyword rules whose predicate is `_languageMode == "tyhp"` only. Those alternatives are already dead while lexing tyhpdef, so removing them from `TyhpdefLexer` does not change a tyhpdef token stream, and it is the part of today’s combined DFA that tyhpdef stops paying for.

6. **Visitors split with the grammars.** Generated tyhpdef bases are `ITyhpdefParserVisitor<Result>` and `TyhpdefParserBaseVisitor<Result>` in namespace `Tyhp.TyhpLang.Parser` (same `--package` and `--generate-visitor true` as `compile_grammar.sh` uses now). `TyhpdefParserAstVisitor` extends `TyhpdefParserBaseVisitor<IBase2Ast?>`. It does not extend `TyhpParserBaseVisitor` or `PhpParserAstVisitor`.

   Generated context types differ (`TyhpParser.ExprContext` vs `TyhpdefParser.ExprContext`), so one C# method cannot override both. Shared visit methods have one source file under `Tyhp/TyhpLang/Visitor/shared/`. The same include step emits that source into `PhpParserAstVisitor` / `TyhpParserAstVisitor` unchanged, and into `TyhpdefParserAstVisitor` with `TyhpParser` replaced by `TyhpdefParser` and `TyhpLexer` replaced by `TyhpdefLexer`. PHP method bodies are not rewritten. A shared file exists only for a rule in the closure that both parsers still have.

7. **Grammar-action helpers are shared the same way.** `TyhpLexer.GrammarMethods.cs` and `TyhpParser.GrammarMethods.cs` stay the PHP/Tyhp partials. Methods that included lexer or parser actions call (`streamLA`, `prepareLess`, `doPreparedLess`, `closeTagHandler`, `ConfigureTagless`, heredoc and nesting, `isLanguageMode`, `isStructKeyword`, `looksLikeStructShape`, `looksLikeAnonymousStruct`, `looksLikeObjectShape`, and any other `this.*` the included actions name) are emitted into `TyhpdefLexer.GrammarMethods.cs` and `TyhpdefParser.GrammarMethods.cs` from that same source, with the class name changed. Predicate behavior is unchanged.

### What does not change

- Token type integers, channel numbers, and mode names in `TyhpLexer.tokens` / the generated `TyhpLexer`.
- Parse results of `phpSrcFile`, `tyhpSrcFile`, and `tyhpTaglessSrcFile`.
- Parser language mode `"tyhp"` inside `tyhpdefBlock` / `tyhpdefTaglessSrcFile`, and lexer language mode `"tyhpdef"` on `<?tyhpdef`.
- Binder and checker behavior. They keep receiving the same AST. The only call-site edit is which lexer, parser, and visitor build that AST for `.tyhpdef`.
- `tyhp.csproj`. Do not add an ANTLR MSBuild item. New C# under `Tyhp/TyhpLang/Parser/` is already on the `Tyhp/**/*.cs` include.
- Precomputing or caching a lexer DFA. That is a later change.

### Lexer modes the tyhpdef lexer still contains

From `PhpLexer.g4`, all of these stay, because the closure reaches expressions and strings and because open-tag recognition uses them:

| Mode | Where |
|---|---|
| default | `PhpLexer.g4` before the first `mode` |
| `ST_INLINE_HTML` | `PhpLexer.g4` |
| `ST_CHECK_FOR_OTHER_OPEN_TAGS_LEXER_ADDON` | `PhpLexer.g4`, plus the `<?tyhpdef` rules from `TyhpLexer.g4` |
| `ST_IN_SCRIPTING` | `PhpLexer.g4`, plus the tyhpdef keyword rules from `TyhpLexer.g4` |
| `ST_LOOKING_FOR_PROPERTY` | `PhpLexer.g4` |
| `ST_DOUBLE_QUOTES` | `PhpLexer.g4` |
| `ST_BACKQUOTE` | `PhpLexer.g4` |
| `ST_HEREDOC` | `PhpLexer.g4` |
| `ST_NOWDOC` | `PhpLexer.g4` |
| `ST_LOOKING_FOR_VARNAME` | `PhpLexer.g4` |
| `ST_VAR_OFFSET` | `PhpLexer.g4` |
| `ST_TYHP_TAGLESS` | `TyhpLexer.g4` — tyhpdef keeps the `<?tyhpdef` alternatives only |

Channels, copied as the same block so the tyhpdef channel integers match the names the visitor already uses: `DocBlockCommentsChannel`, `SimpleCommentsChannel`, `WhiteSpaceChannel`, `ErrorLexemChannel`, `SkipChannel`, `StubTokenChannel`.

Tyhpdef lexer keyword rules that stay (they are predicated on tyhpdef, or on tyhp and tyhpdef): `T_TYHPDEF_OPEN_TAG`, `T_TYHP_EXTENSION`, `T_TYHPDEF_DEPRECATED`, `T_TYHPDEF_OBSOLETE`, `T_TYHPDEF_PARTIAL`, `T_TYHPDEF_EXTERN`, `T_TYHPDEF_OMIT`, `T_TYHP_ASYNC`, `T_TYHP_AWAIT`, `T_TYHP_OPERATOR`, `T_TYHP_VOID`, `T_TYHP_PARENT`, `T_TYHP_IS`, `T_TYHP_TYPE_ALIAS`, `T_DECIMAL_CAST`, and the paired `*_AS_T_STRING` rules next to them in `TyhpLexer.g4`.

Tyhpdef lexer keyword rules that do not move (predicate is `_languageMode == "tyhp"` only, in `TyhpLexer.g4`): `T_TYHP_INTERNAL`, `T_TYHP_WITH`, `T_TYHP_USING`, `T_TYHP_TYPEOF`, `T_TYHP_NAMEOF`, `T_TYHP_VARIABLE_EXISTS`, `T_TYHP_USING_EQUAL`, and the `<?tyhp` open-tag alternatives (`INITIAL_T_OPEN_TYHP_TAG`, `INITIAL_T_OPEN_TYHP_TAG_EOF`, `TAGLESS_TYHP_OPEN_TAG`, `TAGLESS_TYHP_OPEN_TAG_EOF`). `T_TYHP_OPEN_TAG` remains a declared token on `TyhpLexer` only. If a shared parser rule names `T_TYHP_OPEN_TAG` or another tyhp-only token, declare that name in `TyhpdefLexer`’s `tokens { }` block with no lexer rule, the same way `T_NO_GRAMMAR_ADDON_0000` is a declared token. The alternative stays in the shared parser text and still does not match tyhpdef input.

---

## Pipeline

```
Today: TyhpLexer imports PhpLexer; TyhpParser imports PhpParser; one DFA for every file
    │
    ▼
┌──────────────────────────────────────────────────────────────┐
│  STORY 27.2.1                                                │
│                                                              │
│  1. Snapshot TyhpLexer.tokens                                │
│  2. Include-extract PHP lexer/parser text (expanded == now)  │
│  3. TyhpdefLexer / TyhpdefParser from the closure            │
│  4. Visitor + grammar-method includes                        │
│  5. .tyhpdef call sites use the new parser                   │
└──────────────────────────────────────────────────────────────┘
    │
    ▼
PHP and .tyhp still enter TyhpLexer / TyhpParser
.tyhpdef enters TyhpdefLexer / TyhpdefParser
```

### File layout

| Path | What it is |
|---|---|
| `Tyhp/TyhpLang/Grammar/shared/PhpLexer.body.g4` | Current `PhpLexer.g4` body after the `lexer grammar` line: channels, options, `tokens { }`, every mode and rule. Byte-for-byte that body. |
| `Tyhp/TyhpLang/Grammar/shared/PhpParser.reached.g4` | The 239 PHP rules in the closure, in the order they appear in `PhpParser.g4` today. |
| `Tyhp/TyhpLang/Grammar/shared/TyhpOverrides.reached.g4` | The 56 override bodies, in the order they appear in `TyhpParser.g4` today. |
| `Tyhp/TyhpLang/Grammar/shared/Tyhp.reached.g4` | Closure rules that are Tyhp syntax and not `tyhpdef*` (generics, shapes, extensions, `type`, struct, `using`, and the rest of the appendix “Tyhp rules” minus `tyhpdef*`). Same order as `TyhpParser.g4`. |
| `Tyhp/TyhpLang/Grammar/shared/Tyhpdef.rules.g4` | The `tyhpdef*` rules the closure reaches, including `tyhpdefSrcFile` and `tyhpdefTaglessSrcFile`, in current order. |
| `Tyhp/TyhpLang/Grammar/shared/Tyhpdef.lexer-addons.g4` | The tyhpdef (and tyhp-or-tyhpdef) keyword rules and `ST_TYHP_TAGLESS` `<?tyhpdef` alternatives, cut from `TyhpLexer.g4` without reordering them relative to each other. |
| `Tyhp/TyhpLang/Grammar/PhpLexer.g4` | Header plus `// #include "shared/PhpLexer.body.g4"`. |
| `Tyhp/TyhpLang/Grammar/PhpParser.g4` | Header, the 11 unreached PHP rules, and `// #include "shared/PhpParser.reached.g4"` at the position that keeps rule order. |
| `Tyhp/TyhpLang/Grammar/TyhpLexer.g4` | Existing `tokens { }` block, then the PHP lexer body via include (this replaces `import PhpLexer` once the token diff is empty), then tyhp-only keyword rules, then `// #include "shared/Tyhpdef.lexer-addons.g4"`. |
| `Tyhp/TyhpLang/Grammar/TyhpParser.g4` | `.tyhp` file rules that are outside the closure, `// #include "shared/Tyhp.reached.g4"`, `// #include "shared/TyhpOverrides.reached.g4"`, `// #include "shared/Tyhpdef.rules.g4"`, and `import`/include of the full PHP parser so `.tyhp` still has every PHP rule. |
| `Tyhp/TyhpLang/Grammar/TyhpdefLexer.g4` | New. Own `tokens { }` for tyhpdef keywords and for any token a included parser rule names that this lexer does not emit. Include `PhpLexer.body.g4` and `Tyhpdef.lexer-addons.g4`. No tyhp-only keyword rules. |
| `Tyhp/TyhpLang/Grammar/TyhpdefParser.g4` | New. `tokenVocab=TyhpdefLexer`. Include `Tyhpdef.rules.g4`, `Tyhp.reached.g4`, `TyhpOverrides.reached.g4`, `PhpParser.reached.g4`. Do not include the 11 PHP file rules or the 10 unreached Tyhp rules. |

`TyhpParser` must still contain the tyhpdef rules until every `.tyhpdef` call site has moved. After that move, delete the tyhpdef include from `TyhpParser.g4` only if a token-vocabulary diff and the PHP/Tyhp parser tests are already green. Leaving the rules in place does not change PHP token numbers. Prefer deleting them so `TyhpParser` no longer carries a second copy of the tyhpdef ATN; do it as the last grammar edit, not the first.

`compile_grammar.sh` gains one preprocess step and a second `antlr-ng` pair:

1. Expand includes into a temp directory. Do not write the expanded grammars over the `.g4` sources.
2. Existing pair, pointed at the expanded `TyhpLexer.g4` and `TyhpParser.g4`, same flags: `--define language=CSharp`, `--package Tyhp.TyhpLang.Parser`, `--generate-visitor true`, `--generate-listener false`, `--long-messages true`, `--lib` on the directory that holds the expanded PHP grammars and the previous `.tokens` file.
3. New pair, same flags, on expanded `TyhpdefLexer.g4` and `TyhpdefParser.g4`. Lexer first, then lexer+parser, matching the two-pass comment already in the script (`tokenVocab` must see a fresh `.tokens` file).
4. Move `TyhpdefLexer.tokens`, `TyhpdefParser.tokens`, and the `.interp` files beside the existing ones in `Tyhp/TyhpLang/Grammar/`.

Generated names to expect:

- `TyhpdefLexer`, `TyhpdefParser`
- `ITyhpdefParserVisitor<Result>`, `TyhpdefParserBaseVisitor<Result>`
- Context types `TyhpdefParser.<Rule>Context`, following the same naming `TyhpParser` uses now

---

## Phase 1: Prove the PHP vocabulary, then extract

Before editing a grammar, copy `Tyhp/TyhpLang/Grammar/TyhpLexer.tokens` to a baseline path outside the repo (for example `/tmp/TyhpLexer.tokens.baseline`). That file is the token-type dump. Also record the rule-name order in `Tyhp/TyhpLang/Grammar/TyhpParser.interp`.

Extract `PhpLexer.body.g4` and include it from `PhpLexer.g4`. Expand and run the existing `antlr-ng` pair only. `diff` the new `TyhpLexer.tokens` against the baseline. Any added, removed, or renumbered line fails the phase. Revert is restoring the `.g4` text you just split, then regenerating. Do not start the tyhpdef grammar until this diff is empty.

Then extract the parser fragments the same way. After each extract, the Tyhp token diff stays empty and `tests/Tyhp.Tests/Parser/PhpParseTests.cs` plus `tests/Tyhp.Tests/Parser/TyhpParseTests.cs` pass.

## Phase 2: Tyhpdef grammar

Add `TyhpdefLexer.g4` and `TyhpdefParser.g4` as specified above. Regenerate. Fix include order until `antlr-ng` accepts the grammar. Do not “fix” a conflict by editing a shared fragment’s text; that text is also the PHP grammar. Conflicts mean the tyhpdef-only piece was inserted in the wrong mode or a rule was included twice.

`TyhpLexer.tokens` is diffed again after this phase and is still empty.

## Phase 3: Visitors and actions

- New `Tyhp/TyhpLang/Visitor/TyhpdefParserAstVisitor.cs` extending `TyhpdefParserBaseVisitor<IBase2Ast?>`.
- Move `TyhpParserAstVisitor.Tyhpdef.cs` onto that class. Signatures use `TyhpdefParser` context types. Token comparisons use `TyhpdefParser.T_*` (`T_TYHPDEF_DEPRECATED`, `T_TYHPDEF_OBSOLETE`, `T_TYHPDEF_OMIT`, `T_TYHPDEF_EXTERN`, `T_RETURN`, `T_CLASS`, `T_INTERFACE`, `T_ENUM`, `T_TRAIT`, `T_TYHP_ASYNC`).
- For every other closure rule, the current visit method stays the PHP source. Put that method in `Tyhp/TyhpLang/Visitor/shared/` and include it into both visitors. The tyhpdef copy is the same text with the type prefix replaced. Do not behavior-edit the PHP copy.
- Visit methods for the 11 PHP rules and the `.tyhp`-only rules stay only on `PhpParserAstVisitor` / `TyhpParserAstVisitor`.
- `FindPossibleDocComment` on the tyhpdef visitor reads `TyhpdefLexer.DocBlockCommentsChannel`.

PHP partials that remain PHP-only (they keep visiting `TyhpParser` contexts for `.php` / `.tyhp`):

- `Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.cs`
- `PhpParserAstVisitor.PhpRoot.cs`, `.PhpTopStatements.cs`, `.PhpStatements.cs`, `.PhpExpressions.cs`, `.PhpFunctions.cs`, `.PhpObjects.cs`, `.PhpTypes.cs`, `.PhpParametersAndArguments.cs`, `.PhpIdentifiers.cs`, `.PhpAttributes.cs`, `.PhpBlocks.cs`, `.PhpTryCatchBlocks.cs`, `.PhpDereferenceables.cs`, `.Unsorted.cs`
- `TyhpParserAstVisitor.cs`, `.TyhpRoot.cs`, `.TyhpExpressions.cs`, `.TyhpFunctions.cs`, `.TyhpObjects.cs`, `.TyhpTopStatements.cs`, `.TyhpDereferenceables.cs`, `.TyhpIdentifiers.cs`, `.TyhpGenerics.cs`, `.TyhpStructs.cs`, `.TyhpExtensions.cs`, `.TyhpInternalFunctions.cs`, `.TyhpTypeAliases.cs`, `.TyhpTypes.cs`, `.TyhpStatements.cs`, `.TyhpReturnTypes.cs`

`TyhpParserAstVisitor.Tyhpdef.cs` does not remain as a `TyhpParserAstVisitor` partial.

## Phase 4: Call sites

Switch only the `.tyhpdef` branch. `.php` and `.tyhp` keep `new TyhpLexer` / `new TyhpParser` / `TyhpParserAstVisitor`.

| File | Change |
|---|---|
| `Tyhp/Domain/Services/CompilationService.cs` | Tyhpdef branch: `TyhpdefLexer`, `TyhpdefParser`, `tyhpdefSrcFile` / `tyhpdefTaglessSrcFile`, `TyhpdefParserAstVisitor`. |
| `Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.cs` | Same. |
| `Tyhp/CLI/DebugAction.cs` | The `tyhpdefSrcFile` arm. |
| `Tyhp/CLI/TokenizeAction.cs` | When the file is `.tyhpdef`, lex with `TyhpdefLexer`. |
| `Tyhp/Domain/Services/TyhpdefOutputWriter.cs` | The `new TyhpLexer` used on tyhpdef source. |
| `tests/Tyhp.Tests/Parser/Tyhpdef*.cs` and `MalformedTyhpdefParseTests.cs` | Construct `TyhpdefLexer` / `TyhpdefParser`. |

`ConfigureTagless` is called on `TyhpdefLexer` for tagless tyhpdef files, with the same arguments the Tyhp lexer receives today.

## Phase 5: Guides

Update the current-behavior sections of:

- `Tyhp/TyhpLang/Grammar/technical-guide.md`
- `Tyhp/TyhpLang/Parser/technical-guide.md`
- `Tyhp/TyhpLang/Visitor/technical-guide.md`

Describe the two entry grammars, the include step, and which visitor walks which file. Do not leave a second, unused procedure in those guides.

---

## Tests

The change fails if any of these fail.

1. **Token vocabulary.** `diff` `Tyhp/TyhpLang/Grammar/TyhpLexer.tokens` against the pre-move baseline. Every `NAME=integer` line matches, including literal tokens. A renumber is a failed story, even if names match.
2. **PHP and Tyhp parse.** Existing lexer tests and parser tests, unmodified except where a test file is `.tyhpdef`:
   - `tests/Tyhp.Tests/Lexer/LineCommentCloseTagTests.cs`
   - `tests/Tyhp.Tests/Lexer/ContextualKeywordTokenTests.cs`
   - `tests/Tyhp.Tests/Lexer/IsOperatorTokenTests.cs`
   - `tests/Tyhp.Tests/Lexer/UnsetCastRemovedTests.cs`
   - `tests/Tyhp.Tests/Lexer/PipeAndVoidCastTokenTests.cs`
   - `tests/Tyhp.Tests/Parser/PhpParseTests.cs`
   - `tests/Tyhp.Tests/Parser/TyhpParseTests.cs`
   - `tests/Tyhp.Tests/Parser/EdgeCaseParseTests.cs`
   - `tests/Tyhp.Tests/Parser/ErrorRecoveryTests.cs`
3. **Tyhpdef accept and reject.** The current tyhpdef parser tests still pass on `TyhpdefParser`, including the fail fixtures (`MalformedTyhpdefParseTests`, extern / partial / omit / hooked-property tests). A file that produces a parse tree today still produces one. A file that is a syntax error today is still a syntax error.
4. **Token-stream parity for tyhpdef.** One test lexes a tagged tyhpdef fixture and a tagless tyhpdef fixture with both the old `TyhpLexer` (before the keyword split, using the baseline behavior) and `TyhpdefLexer`, and compares token **names**, channels, and text. Integer type codes are allowed to differ. Mode transitions that today’s tyhpdef files take (`ST_TYHP_TAGLESS` or the open-tag check, then `ST_IN_SCRIPTING`, and string modes when the fixture has a string) still occur.
5. **No PHP DFA shortcut.** Do not add a test that only constructs `TyhpdefLexer` and treats that as proof PHP is unchanged. PHP proof is the token diff plus the PHP and Tyhp tests above.

Run the lexer and parser test classes with `dotnet test` filtered to those class names. A full `dotnet test` is optional and is not a substitute for the token diff.

---

## Out of scope

- Prebuilding, serializing, or caching a lexer DFA.
- Changing checker, binder, or emitter rules. Call sites may change only enough to invoke `TyhpdefLexer` / `TyhpdefParser` / `TyhpdefParserAstVisitor`.
- Editing `tyhp.csproj`, adding an ANTLR MSBuild task, or changing `Antlr4.Runtime.Standard` 4.13.1.
- Deleting unreferenced `tyhpdefClassConstDecl` / `tyhpdefClassConstList`.
- Rewriting extension syntax (that is story 27.2).
- Making `TyhpdefLexer` token integers match `TyhpLexer`.
- Dropping `ST_IN_SCRIPTING`, string modes, heredoc/nowdoc, comments, or whitespace from the tyhpdef lexer.

---

## Risks

- **Mode rule order.** An include inserted at the wrong line inside `ST_IN_SCRIPTING` changes which rule wins on a tie. The token diff will not catch a tie-break change if both rules already have explicit token names. PHP lexer tests and the tyhpdef name-level token compare are the backstop. Do not sort rules while moving them.
- **Predicates and actions.** Included actions call `this.streamLA`, `this.prepareLess`, `this._languageMode`, and parser `isLanguageMode`. The tyhpdef partial must define every method the included text calls. A missing method is a compile error; a stub that returns a different bool is a silent accept/reject change.
- **Channels.** `FindPossibleDocComment` keys off the doc-comment channel. The tyhpdef channels block is the same six channels, in the same order, declared once.
- **Imaginary tokens.** A shared parser alternative that names `T_TYHP_WITH` or `T_TYHP_OPEN_TAG` needs that name in `TyhpdefLexer`’s `tokens { }` block even though no tyhpdef lexer rule emits it. Omitting the name is an `antlr-ng` error. Emitting it with a real lexer rule would make tyhpdef accept keyword uses it currently lexes as `T_STRING`.
- **Parser rule indexes.** Removing tyhpdef rules from `TyhpParser` renumbers `RULE_*` constants. PHP tests compare trees, not raw rule indexes. Grep for hardcoded `TyhpParser.RULE_` integers before deleting the tyhpdef include from `TyhpParser.g4`.
- **Language mode mix-up.** Lexer `"tyhpdef"` versus parser `"tyhp"` is current behavior. “Simplifying” either one changes which contextual keywords fire.
- **Accept/reject drift.** The failure this story cares about is a `.tyhpdef` file that parses today and fails after the split, or the reverse. The existing tyhpdef parser tests are the gate. Do not weaken an assertion to make the new parser pass.
- **Include expanded in the wrong directory.** `antlr-ng --lib` must see the expanded PHP grammar and the `.tokens` file from the lexer pass. Pointing `--lib` at `Grammar/` while the expanded files live in a temp directory makes `tokenVocab` read a stale vocabulary and shifts numbers.

---

## Implementation checklist

Do these in order. The appendix is the rule list. Recompute only in step 1.

1. Copy `Tyhp/TyhpLang/Grammar/TyhpLexer.tokens` to a baseline outside the repo. Record `TyhpParser.interp` rule order.
2. Recompute the closure from `tyhpdefSrcFile` and `tyhpdefTaglessSrcFile` on the merged grammar (Tyhp body wins on a duplicate name). If the unreached sets differ from the appendix, stop and update the appendix. Do not move rules against a stale list.
3. Add include expansion to `compile_grammar.sh`. Includes expand into a temp directory. Source `.g4` files keep the `// #include` lines.
4. Move the `PhpLexer.g4` body into `shared/PhpLexer.body.g4` and include it from `PhpLexer.g4` and from `TyhpLexer.g4` at the current `import PhpLexer` site. Regenerate. `diff` `TyhpLexer.tokens` against the baseline. Empty diff is required before step 5.
5. Move the appendix’s 239 PHP rules into `shared/PhpParser.reached.g4`, the 56 overrides into `shared/TyhpOverrides.reached.g4`, and the non-`tyhpdef` Tyhp closure rules into `shared/Tyhp.reached.g4`. Include them so expanded `TyhpParser` rule order matches the interp snapshot. Token diff stays empty. `PhpParseTests` and `TyhpParseTests` pass.
6. Move the closure’s `tyhpdef*` rules into `shared/Tyhpdef.rules.g4` and the tyhpdef lexer addons into `shared/Tyhpdef.lexer-addons.g4`. Add `TyhpdefLexer.g4` and `TyhpdefParser.g4` with `tokenVocab=TyhpdefLexer`. Include the shared PHP lexer body, the reached parser fragments, the overrides, and the tyhpdef pieces. Do not include the 11 PHP file rules, the 10 unreached Tyhp rules, or the tyhp-only keyword rules. Regenerate both grammars. Token diff for `TyhpLexer.tokens` stays empty.
7. Emit `TyhpdefLexer.GrammarMethods.cs` and `TyhpdefParser.GrammarMethods.cs` from the existing grammar-method source, class name changed, so every included action still resolves. PHP partials keep their current method text.
8. Add `TyhpdefParserAstVisitor`. Move `TyhpParserAstVisitor.Tyhpdef.cs` onto it (`TyhpdefParser` contexts and token constants). Include shared visit methods for the other closure rules into both visitors; the PHP copy is unedited text. Leave visits for unreached rules on the PHP/Tyhp visitors only.
9. Point the `.tyhpdef` branches in `CompilationService.cs`, `Binder/BuiltIn/Tyhpdef.cs`, `DebugAction.cs`, `TokenizeAction.cs`, `TyhpdefOutputWriter.cs`, and the tyhpdef parser tests at `TyhpdefLexer` / `TyhpdefParser` / `TyhpdefParserAstVisitor`.
10. Run the token diff, the lexer tests, `PhpParseTests`, `TyhpParseTests`, `EdgeCaseParseTests`, `ErrorRecoveryTests`, and the tyhpdef parser tests. Add the tyhpdef token-name parity check from the test section.
11. Remove the tyhpdef rule include from `TyhpParser.g4` after step 10 is green, regenerate, and repeat the token diff and the PHP/Tyhp tests. Grep for `TyhpParser.RULE_` literals first.
12. Update `Grammar/technical-guide.md`, `Parser/technical-guide.md`, and `Visitor/technical-guide.md` to the two-grammar layout.

---

## Definition of Done

- [ ] `TyhpLexer.tokens` matches the pre-move baseline line for line.
- [ ] `.php` and `.tyhp` still parse with `TyhpLexer` / `TyhpParser`. Lexer and PHP/Tyhp parser tests pass.
- [ ] `.tyhpdef` parses with `TyhpdefLexer` / `TyhpdefParser` / `TyhpdefParserAstVisitor`. Existing tyhpdef accept and reject tests pass.
- [ ] Shared rule text is an include, not a second paste. Tyhpdef does not include the 11 unreached PHP file rules.
- [ ] `TyhpdefParserAstVisitor` does not extend `TyhpParserBaseVisitor`.
- [ ] No DFA prebuild. No checker or binder logic change beyond the parser construction sites.
- [ ] `tyhp.csproj` unchanged.

---

## Relationships

- **Parent:** Story **27.2**. This story does not implement block-target extensions. Sequence it after 27.2’s grammar edits so the closure is taken on the grammar that will ship.
- **Requires:** `./compile_grammar.sh`, `PhpLexer.g4`, `PhpParser.g4`, `TyhpLexer.g4`, `TyhpParser.g4`.
- **Unblocks:** a later change that prebuilds a lexer DFA, which can then target `TyhpdefLexer` and `TyhpLexer` separately.
- **Does not change:** story 27.3’s repo split, except that 27.3 should see this layout if both land.

---

## Appendix: closure (current grammars)

Recompute in checklist step 1. Tyhp body wins where both files define the rule.

### PHP rules the closure reaches (239)

`absoluteTraitMethodReference`, `absoluteTraitPropertyReference`, `altIfStmt`, `altIfStmtWithoutElse`, `ampersand`, `anonymousClass`, `argument`, `argumentList`, `arrayPair`, `arrayPairList`, `attribute`, `attributeDecl`, `attributeGroup`, `attributedClassStatement`, `attributedCtorParameter`, `attributedParameter`, `attributedStatement`, `attributes`, `callArgumentList`, `caseDefault`, `caseExpr`, `caseItem`, `caseList`, `caseSeparator`, `catchBlock`, `catchBlockGrammarAddon`, `catchList`, `catchNameList`, `classConstDecl`, `classConstList`, `classConstModifiers`, `classConstModifiersGrammarAddon`, `classConstant`, `classDeclarationStatement`, `classDeclarationStatementGrammarAddon`, `classModifier`, `classModifiers`, `classModifiersOptional`, `className`, `classNameList`, `classNameReference`, `classStatement`, `classStatementList`, `cloneArgumentList`, `cloneArgumentNoExpr`, `constList`, `constant`, `constantTokenValue`, `constantTokenValueGrammarAddon`, `ctorArguments`, `ctorParameterList`, `declareStatement`, `dereferenceableArrayAccessSuffix`, `dereferenceableBaseGrammarAddon`, `dereferenceableClassConstantAccessSuffix`, `dereferenceableMemberAccessSuffix`, `dereferenceableScalar`, `dereferenceableStaticMemberAccessSuffix`, `dereferenceableSuffixGrammarAddon`, `echoExpr`, `echoExprList`, `encapsList`, `encapsVar`, `encapsVarOffset`, `encapsVarOrWhitespace`, `enumBackingType`, `enumCase`, `enumCaseExpr`, `enumDeclarationStatement`, `enumDeclarationStatementGrammarAddon`, `expr`, `extendsFrom`, `finallyStatement`, `fn`, `forCondExprs`, `forExprItem`, `forExprs`, `forStatement`, `foreachStatement`, `fullyDereferenceable`, `fullyDereferenceableSuffix`, `function`, `functionDeclarationStatement`, `functionName`, `globalVar`, `globalVarList`, `groupUseDeclaration`, `hookedProperty`, `hookedPropertyGrammarAddon`, `identifier`, `identifierWithoutConstructor`, `ifStmt`, `ifStmtWithoutElse`, `implementsList`, `inlineFunction`, `inlineUseDeclaration`, `inlineUseDeclarations`, `innerStatement`, `innerStatementList`, `interfaceDeclarationStatement`, `interfaceDeclarationStatementGrammarAddon`, `interfaceExtendsList`, `internalFunctions`, `intersectionType`, `intersectionTypeWithoutStatic`, `isReference`, `isVariadic`, `issetVariable`, `issetVariables`, `legacyNamespaceName`, `lexicalVar`, `lexicalVarList`, `lexicalVars`, `matchArm`, `matchArmCondList`, `matchArmList`, `matchCheck`, `memberConstantName`, `memberInstanceName`, `memberModifier`, `memberName`, `methodBody`, `methodModifiers`, `methodModifiersGrammarAddon`, `mixedGroupUseDeclaration`, `name`, `namespaceDeclarationName`, `namespaceName`, `newNonDereferenceableGrammarAddon`, `newVariable`, `nonEmptyArgumentList`, `nonEmptyCloneArgumentList`, `nonEmptyCtorParameterList`, `nonEmptyForCondExprs`, `nonEmptyForExprs`, `nonEmptyMatchArmList`, `nonEmptyMemberModifiers`, `nonEmptyParameterList`, `optionalCppModifiers`, `optionalExpr`, `optionalParameterList`, `optionalPropertyHookList`, `optionalVariable`, `parameter`, `parameterList`, `phpEchoBlock`, `phpExprAssignmentOps`, `phpExprBase`, `phpExprBinaryAddSubOps`, `phpExprBinaryAddSubOpsGrammarAddon`, `phpExprBinaryConcatOps`, `phpExprBinaryConcatOpsGrammarAddon`, `phpExprBinaryMulDivOps`, `phpExprBinaryMulDivOpsGrammarAddon`, `phpExprBinaryOpGrammarAddon003`, `phpExprBinaryOpGrammarAddon004`, `phpExprBinaryOpGrammarAddon005`, `phpExprBinaryOpGrammarAddon006`, `phpExprBinaryShiftOps`, `phpExprBinaryShiftOpsGrammarAddon`, `phpExprCompareEqualityOps`, `phpExprCompareEqualityOpsGrammarAddon`, `phpExprCompareSizeOps`, `phpExprCompareSizeOpsGrammarAddon`, `phpExprPrec`, `phpExprUnaryPostOpGrammarAddon001`, `phpExprUnaryPostOpGrammarAddon002`, `phpExprUnaryPostOpGrammarAddon003`, `phpExprUnaryPostOpGrammarAddon004`, `phpExprUnaryPostOpGrammarAddon005`, `phpExprUnaryPostOpGrammarAddon006`, `phpExprUnaryPostOps`, `phpExprUnaryPostOpsGrammarAddon`, `phpExprUnaryPreOpGrammarAddon001`, `phpExprUnaryPreOpGrammarAddon002`, `phpExprUnaryPreOpGrammarAddon003`, `phpExprUnaryPreOpGrammarAddon004`, `phpExprUnaryPreOpGrammarAddon005`, `phpExprUnaryPreOpGrammarAddon006`, `phpExprUnaryPreOps`, `phpInlineOutput`, `phpInlineOutputStatement`, `phpInlineOutputStatementGrammarAddon`, `possibleArrayPair`, `possibleComma`, `property`, `propertyHook`, `propertyHookBody`, `propertyHookBodyGrammarAddon`, `propertyHookList`, `propertyHookModifiers`, `propertyHookModifiersGrammarAddon`, `propertyList`, `propertyModifiers`, `propertyModifiersGrammarAddon`, `realScalar`, `reservedNonModifiers`, `reservedNonModifiersBase`, `reservedNonModifiersWithoutConstructor`, `returnType`, `returnsRef`, `scalar`, `semiReserved`, `semiReservedBase`, `semiReservedWithoutConstructor`, `simpleVariable`, `simpleVariableGrammarAddon`, `statement`, `statementRequiringTerminal`, `statementTerminal`, `statementWithoutTerminal`, `staticVar`, `staticVarList`, `switchCaseList`, `traitAdaptation`, `traitAdaptationList`, `traitAdaptations`, `traitAlias`, `traitDeclarationStatement`, `traitDeclarationStatementGrammarAddon`, `traitMethodReference`, `traitPrecedence`, `traitPropertyReference`, `type`, `typeExprGrammarAddon`, `typeExprWithoutStaticGrammarAddon`, `unionType`, `unionTypeWithoutStatic`, `unprefixedUseDeclaration`, `unprefixedUseDeclarations`, `unsetVariable`, `unsetVariables`, `useDeclaration`, `useDeclarations`, `useType`, `useTypeGrammarAddon`, `variable`, `variableClassName`, `whileStatement`.

### Overrides whose Tyhp body is the one in the closure (56)

`absoluteTraitMethodReferenceGrammarAddon`, `attributedClassStatementGrammarAddon`, `classModifierGrammarAddon`, `classNameGrammarAddon`, `classNameIdentifierGrammarAddon`, `classStatementGrammarAddon`, `constDecl`, `enumModifiersGrammarAddon`, `enumNameGrammarAddon`, `forSyntax`, `foreachVariable`, `functionCallGrammarAddon`, `functionDeclarationStatementGrammarAddon`, `functionModifiersGrammarAddon`, `functionNameGrammarAddon`, `functionParametersGrammarAddon`, `innerStatementGrammarAddon`, `interfaceModifiersGrammarAddon`, `interfaceNameGrammarAddon`, `internalFunctionsGrammarAddon`, `legacyNamespaceNameGrammarAddon`, `memberModifierGrammarAddon`, `memberNameIdentifierGrammarAddon`, `nameTokenValueGrammarAddon`, `namespaceNameGrammarAddon`, `newDereferenceable`, `newDereferenceableGrammarAddon`, `newNonDereferenceable`, `optionalTypeWithoutStatic`, `parameterTypeExpressionGrammarAddon`, `phpExprAssignmentOpsGrammarAddon`, `phpExprBinaryOpGrammarAddon001`, `phpExprBinaryOpGrammarAddon002`, `phpExprPrecBaseGrammarAddon`, `phpExprUnaryPreOpsGrammarAddon`, `phpTopExpr`, `reservedNonModifiersGrammarAddon`, `returnTypeGrammarAddon`, `semiReservedGrammarAddon`, `statementRequiringTerminalGrammarAddon`, `statementWithoutTerminalGrammarAddon`, `traitAliasGrammarAddon`, `traitAliasNameGrammarAddon`, `traitMethodIdentifierGrammarAddon`, `traitMethodReferenceGrammarAddon`, `traitModifiersGrammarAddon`, `traitNameGrammarAddon`, `typeExpr`, `typeExprWithoutStatic`, `typeNameGrammarAddon`, `typeWithoutStatic`, `typeWithoutStaticGrammarAddon`, `unionTypeElement`, `unionTypeWithoutStaticElement`, `unprefixedUseDeclarationGrammarAddon`, `useDeclarationGrammarAddon`.

### Tyhp rules the closure reaches (98)

`callableShapeParameter`, `callableShapeParameterList`, `callableType`, `groupedType`, `tyhpAnonymousStruct`, `tyhpClassMethodDefinition`, `tyhpClassOperatorOverload`, `tyhpClassOperatorOverloadOp`, `tyhpCtorReturnType`, `tyhpExtensionCallableMember`, `tyhpExtensionCallableParameters`, `tyhpExtensionDeclarationStatement`, `tyhpExtensionFunctionList`, `tyhpExtensionGroupMember`, `tyhpExtensionGroupMemberList`, `tyhpExtensionMember`, `tyhpExtensionOperatorLegacyTarget`, `tyhpExtensionOperatorOverload`, `tyhpExtensionTargetGroup`, `tyhpForInitExpr`, `tyhpForInitExprs`, `tyhpGenericIdentifier`, `tyhpGenericIdentifierWithoutConstructor`, `tyhpGenericParameterDeclaration`, `tyhpGenericParameterDeclarationList`, `tyhpGenericParameterDeclarations`, `tyhpGenericTypeArgument`, `tyhpGenericTypeArgumentList`, `tyhpGenericTypeArguments`, `tyhpMethodDefinition`, `tyhpObjectShape`, `tyhpObjectShapeIntersection`, `tyhpObjectShapeIntersectionItem`, `tyhpObjectShapeStatement`, `tyhpObjectShapeStatementList`, `tyhpOptionalGenericIdentifier`, `tyhpOptionalGenericIdentifierWithoutConstructor`, `tyhpReservedNonModifiers`, `tyhpScalarType`, `tyhpSemiReserved`, `tyhpStringWithOptionalGeneric`, `tyhpStructProperty`, `tyhpStructPropertyList`, `tyhpStructShape`, `tyhpTypeAlias`, `tyhpTypedVarExpr`, `tyhpUsingBlock`, `tyhpUsingResource`, `tyhpUsingResourceList`, `tyhpdefAttributedStatement`, `tyhpdefBlock`, `tyhpdefClassNameWithOptionalAlias`, `tyhpdefClassOperator`, `tyhpdefClassStatement`, `tyhpdefClassStatementList`, `tyhpdefDeclareBody`, `tyhpdefDeprecatedOrObsolete`, `tyhpdefExtensionFunction`, `tyhpdefExtensionOperator`, `tyhpdefExternClassDeclarationStatement`, `tyhpdefExternClassIllegalHeader`, `tyhpdefExternConstDeclarationStatement`, `tyhpdefExternDeclarationStatement`, `tyhpdefExternEnumDeclarationStatement`, `tyhpdefExternFunctionDeclarationStatement`, `tyhpdefExternInterfaceDeclarationStatement`, `tyhpdefFunctionNameWithOptionalAlias`, `tyhpdefHookedProperty`, `tyhpdefIdentifierWithAlias`, `tyhpdefIdentifierWithOptionalAlias`, `tyhpdefImportClassConstDecl`, `tyhpdefImportClassConstList`, `tyhpdefImportClassDeclarationStatement`, `tyhpdefImportConstStatement`, `tyhpdefImportEnumDeclarationStatement`, `tyhpdefImportFunctionDeclarationStatement`, `tyhpdefImportInterfaceDeclarationStatement`, `tyhpdefImportTraitDeclarationStatement`, `tyhpdefImportVariableStatement`, `tyhpdefPartialFunctionDeclarationStatement`, `tyhpdefPartialFunctionName`, `tyhpdefProperty`, `tyhpdefPropertyHook`, `tyhpdefPropertyHookList`, `tyhpdefPropertyList`, `tyhpdefSrcFile`, `tyhpdefStandaloneExtensionDeclarationStatement`, `tyhpdefStandaloneExtensionFunction`, `tyhpdefStandaloneExtensionGroupMember`, `tyhpdefStandaloneExtensionGroupMemberList`, `tyhpdefStandaloneExtensionMember`, `tyhpdefStandaloneExtensionMemberList`, `tyhpdefStandaloneExtensionOperator`, `tyhpdefStandaloneExtensionTargetGroup`, `tyhpdefStatement`, `tyhpdefTaglessSrcFile`, `tyhpdefTopStatement`, `tyhpdefTopStatementList`.

### Not in the tyhpdef grammar

PHP: `attributedConstStatement`, `codeBlock`, `codeBlockGrammarAddon`, `noGrammarAddon`, `phpBlock`, `phpSrcFile`, `topStatement`, `topStatementGrammarAddon`, `topStatementListWithRequiredFinalTerminal`, `topStatementNeedsTerminal`, `topStatementNoTerminal`.

Tyhp: `topStatementGrammarAddon`, `tyhpBlock`, `tyhpCodeBlock`, `tyhpInlineOutput`, `tyhpInlineOutputStatement`, `tyhpSrcFile`, `tyhpTaglessSrcFile`, `tyhpWithList`, `tyhpdefClassConstDecl`, `tyhpdefClassConstList`.
