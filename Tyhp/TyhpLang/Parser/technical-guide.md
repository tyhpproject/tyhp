# TyhpLang Parser — Developer Technical Guide

This guide covers the C# parser layer under `Tyhp/TyhpLang/Parser/`: how it relates to the ANTLR `.g4` grammars, what is generated vs hand-written, how lex/parse feeds the Visitor → AST pipeline, and the non-obvious behaviors you will hit when changing syntax.

**Scope of this directory:**

| File | Role | Origin |
|------|------|--------|
| `TyhpLexer.cs` / `TyhpParser.cs` | Recognizers for `.php` and `.tyhp` | Generated (`antlr-ng`, ANTLR 4.13.2) |
| `TyhpParserVisitor.cs` / `TyhpParserBaseVisitor.cs` | `ITyhpParserVisitor<Result>` and default visitor | Generated |
| `TyhpdefLexer.cs` / `TyhpdefParser.cs` | Recognizers for `.tyhpdef` | Generated |
| `TyhpdefParserVisitor.cs` / `TyhpdefParserBaseVisitor.cs` | `ITyhpdefParserVisitor<Result>` and default visitor | Generated |
| `TyhpLexer.GrammarMethods.cs` | Lexer helpers + `NextToken` | Hand-written partial of `TyhpLexer` |
| `TyhpParser.GrammarMethods.cs` | Parser semantic predicates / lookahead | Hand-written partial of `TyhpParser` |
| `TyhpdefLexer.GrammarMethods.cs` / `TyhpdefParser.GrammarMethods.cs` | Same helpers on the tyhpdef recognizers | Emitted by `compile_grammar_emit_partials.py` (class rename of the Tyhp partials) |
| `TyhpAntlrErrorListener.cs` | Diagnostics bridge for lexer/parser errors | Hand-written |

`TyhpLexer` and `TyhpParser` are in namespace `Tyhp.TyhpLang.Parser`. Generated visitor interfaces and base visitors have **no** `namespace` declaration (global namespace) despite `--package`. The same is true of `ITyhpdefParserVisitor<T>` and `TyhpdefParserBaseVisitor<T>`.

---

## 1. Role in the compiler pipeline

Pipeline order (see also `docs/content/intro_newSyntaxCreation.md`):

1. **Lexer** — `.php` / `.tyhp` use `TyhpLexer`; `.tyhpdef` uses `TyhpdefLexer`. Chars → token stream.
2. **Parser** — `TyhpParser` or `TyhpdefParser`. Tokens → ANTLR parse tree (`ParserRuleContext`).
3. **Visitor** — `TyhpParserAstVisitor` (extends `PhpParserAstVisitor`) for `.php` / `.tyhp`; `TyhpdefParserAstVisitor` for `.tyhpdef`. Parse tree → `SrcFileAst`.
4. Binder → Checker → Emitter.

The `Parser/` directory owns steps 1–2 and the **generated visitor contracts**. It does **not** own AST node types or the hand-written visit implementations (those live under `Visitor/` and `Ast/`).

### Primary call sites

- **`CompilationService`** (`Tyhp/Domain/Services/CompilationService.cs`) — production multi-file parse. Keeps a `ThreadLocal` pair for `TyhpLexer` / `TyhpParser` and a second pair for `TyhpdefLexer` / `TyhpdefParser`. A path ending in `.tyhpdef` uses the tyhpdef pair (`tyhpdefSrcFile` or `tyhpdefTaglessSrcFile`) and `TyhpdefParserAstVisitor`. `.tyhp` uses `tyhpSrcFile` / `tyhpTaglessSrcFile` and `TyhpParserAstVisitor`. Anything else uses `phpSrcFile` and `TyhpParserAstVisitor`. `.tyhpdef` is checked before `.tyhp` in the if/else chain (neither extension is a suffix of the other, so the order is not load-bearing today, but keep it consistent with the other call sites below).
- **`Tyhpdef.ParseContent`** (`Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.cs`) — single-string parse for package/runtime tyhpdefs and tests (`ParserTestHelper`). `ParseMode.Tyhpdef` constructs `TyhpdefLexer`, `TyhpdefParser`, and `TyhpdefParserAstVisitor`. `ParseMode.Tyhp` and `ParseMode.Php` construct `TyhpLexer`, `TyhpParser`, and `TyhpParserAstVisitor`.
- **`DebugAction`** — `.tyhpdef` uses `GetTyhpdefLexerAndParser` and `tyhpdefSrcFile`. Other files use `TyhpLexer` / `TyhpParser`.
- **`TokenizeAction`** — `.tyhpdef` lexes with `TyhpdefLexer`; other files use `TyhpLexer`.
- **`TyhpdefOutputWriter`** — keyword probing of tyhpdef source uses `TyhpdefLexer`.

`ConfigureTagless` is called on whichever lexer was constructed. For a tagless `.tyhpdef` file the language-mode argument is `"tyhpdef"`. For a tagless `.tyhp` file it is `"tyhp"`.

`ParseMode` (`Tyhp/TyhpLang/Enum/ParseMode.cs`): `Php`, `Tyhpdef`, `Tyhp`.

---

## 2. Relationship to `Tyhp/TyhpLang/Grammar/` ANTLR sources

### Source of truth

| Grammar file | Role |
|--------------|------|
| `PhpLexer.g4` / `shared/PhpLexer.body.g4` | PHP lexer body. Included by `TyhpLexer` and `TyhpdefLexer`. |
| `PhpParser.g4` | PHP parser. `import`ed by `TyhpParser`. Defines **grammar addon** hooks. |
| `TyhpLexer.g4` / `TyhpParser.g4` | Entry grammars for `.php` and `.tyhp`. `tokenVocab=TyhpLexer`. |
| `TyhpdefLexer.g4` / `TyhpdefParser.g4` | Entry grammars for `.tyhpdef`. `tokenVocab=TyhpdefLexer`. |
| `shared/Tyhpdef.rules.g4`, `Tyhp.reached.g4`, `TyhpOverrides.reached.g4`, `PhpParser.reached.g4` | Parser fragments included by `TyhpdefParser`, and (as `#N` parts) by `TyhpParser` where `.tyhp` still names those rules. |

### Regeneration

Root script: `./compile_grammar.sh`

- Requires `antlr-ng` (`npm install -g antlr-ng`).
- Expands `// #include "shared/<file>.g4"` (and `#N` part suffixes) into a temp directory. Sources keep the include lines.
- Two `antlr-ng` pairs, each lexer-first then lexer+parser, package `Tyhp.TyhpLang.Parser`, visitor on, listener off: expanded `TyhpLexer.g4` / `TyhpParser.g4`, then expanded `TyhpdefLexer.g4` / `TyhpdefParser.g4`. `--lib` is the temp directory.
- Moves `*.tokens` / `*.interp` for both grammars into `./Tyhp/TyhpLang/Grammar/`.
- Runs `compile_grammar_emit_partials.py`, which writes the tyhpdef GrammarMethods partials and the shared visitor includes.
- **Not** part of `dotnet build`. Changing `.g4` without running the script leaves stale generated C#.

`TyhpLexer.tokens` is the PHP/Tyhp vocabulary. `TyhpdefLexer.tokens` may number the same name differently (`T_ERROR` is 25 on `TyhpLexer` and 23 on `TyhpdefLexer`). Shared C# compares `TyhpParser.T_*` or `TyhpdefParser.T_*` of the parser that built the tree. Channel names are declared in the same order in the shared PHP lexer body; `DocBlockCommentsChannel` is 2 on both lexers.

NuGet runtime: `Antlr4.Runtime.Standard` **4.13.1** (`tyhp.csproj`). Generated headers say **ANTLR Version: 4.13.2**. They work together in practice; treat the version skew as something to keep an eye on when upgrading either side.

### `Tyhp/TyhpLang/Grammar/.antlr/`

This folder holds **Java** artifacts (e.g. `TyhpParser.java`, listeners) typically produced by IDE/ANTLR tooling. The Tyhp compiler build does **not** compile those. Do not confuse them with the C# sources under `TyhpLang/Parser/`.

### Grammar addon pattern (why Tyhp can extend PHP without forking every rule)

`PhpParser.g4` defines many stub rules named `*GrammarAddon` that default to matching the placeholder token `T_NO_GRAMMAR_ADDON_0000` (declared in `TyhpLexer.g4` `tokens { ... }`; it is not a real lexeme). Tyhp **overrides** those rules in `TyhpParser.g4` (often marked `// ! OVERRIDE`) to inject Tyhp productions, usually gated with `{this.isLanguageMode("tyhp")}?`.

Examples of overridden addons: `topStatementGrammarAddon`, `statementWithoutTerminalGrammarAddon`, `typeNameGrammarAddon`, `phpExprBinaryOpGrammarAddon002` (`is` / instanceof alias), `functionDeclarationStatementGrammarAddon`, etc.

`noGrammarAddon : T_NO_GRAMMAR_ADDON_0000;` exists so empty addon alternatives are not silently ε-productions.

---

## 3. Generated vs hand-written

### Generated (do not edit by hand)

- `TyhpLexer.cs`, `TyhpParser.cs`, `TyhpParserVisitor.cs`, `TyhpParserBaseVisitor.cs`
- `TyhpdefLexer.cs`, `TyhpdefParser.cs`, `TyhpdefParserVisitor.cs`, `TyhpdefParserBaseVisitor.cs`
- `TyhpdefLexer.GrammarMethods.cs`, `TyhpdefParser.GrammarMethods.cs` — not ANTLR output; `compile_grammar_emit_partials.py` copies the Tyhp partial and renames the class (`TyhpLexer` → `TyhpdefLexer`, `TyhpParser` → `TyhpdefParser`)
- Recognizers are marked `<auto-generated>`, `GeneratedCode("ANTLR", "4.13.2")`, `partial class` / interface
- Regenerating **overwrites** these files

### Hand-written partials (safe to edit; must stay compatible with grammar actions)

Edit `TyhpLexer.GrammarMethods.cs` and `TyhpParser.GrammarMethods.cs`. The next `./compile_grammar.sh` copies them onto the tyhpdef partials. A helper the included grammar actions call has to exist on both; adding it only on the tyhpdef partial is lost on regen.

**`TyhpLexer.GrammarMethods.cs`** — `partial class TyhpLexer : Lexer`. Implements everything the `.g4` actions call, plus token-stream post-processing:

- State: `_languageMode`, tagless fields, `_pendingTokensQueue`, `_encapsTokensQueue`, `_heredocLabel`, `_nestingStack`, `prepareLess*` marks, `shouldPopList`
- `ConfigureTagless` / `ApplyTaglessStartMode` / `HasLiteralOpenTagAtStart`
- `NextToken` override (heredoc line fix, constant double-quoted string folding, consecutive `T_ENCAPSED_AND_WHITESPACE` / `T_INLINE_HTML` combining)
- Nesting (`enterNesting` / `exitNesting`), heredoc helpers, `closeTagHandler`, stream lookahead (`streamPeek`, `streamLA`, `streamLAEq`, `isFollowedByVarOrVarArg`), `prepareLess` / `doPreparedLess` / `less`

**`TyhpParser.GrammarMethods.cs`** — `partial class TyhpParser : Parser`:

- `_languageMode` + `isLanguageMode(string)`
- `isHideKeyword(IToken)` / `isObjectKeyword(IToken)` / `isStructKeyword(IToken)` — contextual `T_STRING` spellings
- `isThisVariable(IToken)` / `extensionReceiverAhead()` / `extensionLegacyReceiverAhead()` — `&$this` receiver annotation vs per-member `extends Type $this` on extension-block callables (`tyhpExtensionCallableParameters`)
- `looksLikeObjectShape()` — `type Name = object { … }` vs bare `object`
- `looksLikeObjectShapeIntersection()` — `type Name = Nominal & object { … }` (or `object { … } & Nominal`) vs a plain nominal intersection (`A & B`, which stays on `typeExpr`); scans the `&`-chain for at least one `object {` item without consuming input, skipping shape bodies via balanced-brace counting. Shape bodies (`tyhpObjectShapeStatementList`) accept PHP `const NAME = expr` in addition to tyhpdef class statements.
- `looksLikeStructShape()` — type-position `struct { … }` / `struct extends` vs a class/type named `struct`
- `looksLikeAnonymousStruct()` — `new struct { … }` vs `new struct()` constructing a class named `struct`
- `callableIsFollowedByOpenParen()` — `callable (` or `callable` plus a PHP builtin-cast token (`T_INT_CAST` / `T_STRING_CAST` / …) vs bare `callable`. `IsBuiltinCastToken` also accepts `T_VOID_CAST` (an unnamed `void` parameter is syntactically valid the same way a named `void $v` parameter is); `typeof` / `default` do not accept `T_VOID_CAST` in their own BuiltinCast alternatives.
- `newIsFollowedByArgumentList()` — disambiguate `new X<T>(args)` vs comparison
- `looksLikeGenericTypedLocal()` — disambiguate `Box<int> $x` vs comparison
- `looksLikeTypedConstDecl()` — disambiguate file-level `const int X = 1` vs `const FOO = 1`
- `checkIsTopExpr(RuleContext)` — used from `PhpParser.g4` for `&` / `|` / `^` in expression vs top-expr contexts
- Dead-looking counters: `LanguageModeTotalTime`, `LanguageModeTotalCalls` (declared; **no increments found** in the repo)

**`TyhpAntlrErrorListener.cs`** — not generated; not a partial of the recognizers.

### Namespace quirk (visitors)

- `TyhpLexer` / `TyhpParser` / `TyhpdefLexer` / `TyhpdefParser` are in namespace `Tyhp.TyhpLang.Parser` (as requested by `--package`).
- Generated `ITyhpParserVisitor<T>`, `TyhpParserBaseVisitor<T>`, `ITyhpdefParserVisitor<T>`, and `TyhpdefParserBaseVisitor<T>` currently have **no** `namespace` declaration (global namespace), despite `--package`. Hand-written visitors under `Tyhp.TyhpLang.Visitor` still resolve them because global types are visible. If `antlr-ng` behavior changes and starts emitting a namespace, regeneration may require visitor `using` / namespace cleanup — verify after each regen.

---

## 4. Lexer behavior (deep dive)

### Modes (from generated `modeNames`)

`DEFAULT_MODE`, `ST_CHECK_FOR_OTHER_OPEN_TAGS_LEXER_ADDON`, `ST_IN_SCRIPTING`, `ST_TYHP_TAGLESS`, `ST_INLINE_HTML`, `ST_LOOKING_FOR_PROPERTY`, `ST_DOUBLE_QUOTES`, `ST_BACKQUOTE`, `ST_HEREDOC`, `ST_NOWDOC`, `ST_LOOKING_FOR_VARNAME`, `ST_VAR_OFFSET`.

Default mode handles open tags and inline HTML. Scripting keywords live in `ST_IN_SCRIPTING`.

Open-tag routing:

- `<?php` / `<?=` → PHP / phpEcho language mode (shared PHP lexer body)
- `<?` then push `ST_CHECK_FOR_OTHER_OPEN_TAGS_LEXER_ADDON`. `TyhpLexer` recognizes `tyhp` and `tyhpdef`. `TyhpdefLexer` recognizes `tyhpdef`.
- Tagless: optional start in `ST_TYHP_TAGLESS`. `TyhpLexer` consumes `<?tyhp` or `<?tyhpdef`. `TyhpdefLexer` consumes `<?tyhpdef`.

### Channels

From `PhpLexer.g4`: `DocBlockCommentsChannel`, `SimpleCommentsChannel`, `WhiteSpaceChannel`, `ErrorLexemChannel`, `SkipChannel`, `StubTokenChannel`.

Parser default channel ignores whitespace/comments. `PhpParserAstVisitor.FindPossibleDocComment` walks `TyhpLexer.DocBlockCommentsChannel`. `TyhpdefIncludedPhpVisits.FindPossibleDocComment` walks `TyhpdefLexer.DocBlockCommentsChannel`. Both constants are channel 2.

### Lexer `_languageMode` (string)

Set when an open tag is recognized (or by `ConfigureTagless` when starting tagless without a tag):

| Value | Effect (examples) |
|-------|-------------------|
| `"php"` / `"phpEcho"` | PHP scripting |
| `"tyhp"` | Tyhp keywords: `with`, `using`, `is`, `typeof`, `nameof`, `variable_exists`, `:=`, plus shared Tyhp keywords |
| `"tyhpdef"` | Shared Tyhp keywords (`struct`, `type`, `async`, …) **plus** `deprecated` / `obsolete` / `partial` / `extern` / `omit`; **not** the Tyhp-only keyword set above |

Contextual keywords (`type`, `extension`, `async`, `operator`, `void`, `partial`, `extern`, `omit`, `deprecated`, `obsolete`) use `prepareLess` + `streamLA` so identifier contexts still produce `T_STRING` when the word is not being used as a declaration keyword. `struct` (like `object` / `hide`) is always `T_STRING`; parser predicates (`isStructKeyword`, `looksLikeStructShape`, `looksLikeAnonymousStruct`) decide keyword vs name. `parent` stays a hard keyword (`: parent(...)` vs `function parent()` are both followed by `(`).

### Tagless lexing

`ConfigureTagless(enabled, languageMode, diagnostics?, fileName?)` is defined on `TyhpLexer` and emitted onto `TyhpdefLexer`. Call it on the lexer that will read the file. Compilation of a tagless `.tyhpdef` file passes `"tyhpdef"`; a tagless `.tyhp` file passes `"tyhp"`.

- If disabled: clear tagless state.
- If enabled: `ApplyTaglessStartMode()`:
  - Literal `<?tyhp` (`TyhpLexer`) or `<?tyhpdef` (both lexers) at start (optional leading whitespace) → `Mode(ST_TYHP_TAGLESS)` so the tag is consumed.
  - Otherwise → set `_languageMode` to the tagless language mode and `Mode(ST_IN_SCRIPTING)` (no synthetic token).
- `<?php` is **not** treated as a tagless open tag (left for scripting-mode lexing).
- Closing `?>` in tagless: lexer still may emit close-tag behavior; `closeTagHandler` can add `LexerCloseTagNotAllowedInTaglessMode` (1004). Tagless **parser** entry rules omit `T_CLOSE_TAG` / inline HTML so `?>` is not in the expected follow set.

### `NextToken` post-processing (why it exists)

ANTLR’s base lexer is not enough for PHP/Tyhp string/heredoc quirks:

1. **`T_END_HEREDOC`** — bump line/column/start index (closing label is on the next line in PHP semantics).
2. **Constant double-quoted strings** — if `"…"` contains only `T_ENCAPSED_AND_WHITESPACE` then closing `"`, fold into a single `T_CONSTANT_ENCAPSED_STRING` (so the parser sees a scalar, not an encaps list).
3. **Merge consecutive** `T_ENCAPSED_AND_WHITESPACE` or `T_INLINE_HTML` into one token.

`//` and `#` comments end at `?>` (PHP closes the comment and leaves scripting). Newline and end-of-file comment rules call `lineCommentExcludesCloseTag()` so the closer stays outside the comment. Close-tag comment rules rewind to just before `?>`, and `T_INLINE_HTML_IN_PHP_CODE` / `T_CLOSE_TAG` then take the template.

Pending/encaps queues + peek helpers exist because folding needs 1-token lookahead without losing tokens.

### Nesting stack

Brace tokens call `enterNesting` / `exitNesting` with `BraceType` (`square` / `round` / `curly`) so interpolated `{…}` / `${…}` can pop back to the correct lexer mode (`popModeBackTo`, `additionalPopMode`). Mismatched braces throw plain `Exception` (not `DiagnosticBag`) from the lexer helpers.

### `closeTagHandler`

On `?>`, enqueues a synthetic `T_SYM_SEMICOLON` so the parser can treat close-tag as a statement terminator without duplicating every statement rule. In tagless mode with diagnostics attached, also reports `LexerCloseTagNotAllowedInTaglessMode`.

---

## 5. Parser behavior (deep dive)

### Entry rules

| Rule | Recognizer | When used |
|------|------------|-----------|
| `phpSrcFile` | `TyhpParser` | `.php` / other / `ParseMode.Php` |
| `tyhpSrcFile` | `TyhpParser` | `.tyhp` (tagged; allows inline HTML / close tags) |
| `tyhpTaglessSrcFile` | `TyhpParser` | `.tyhp` + `source.tagless` |
| `tyhpdefSrcFile` | `TyhpdefParser` | `.tyhpdef` (tagged) |
| `tyhpdefTaglessSrcFile` | `TyhpdefParser` | `.tyhpdef` + `source.tagless` |

`TyhpParser` still contains `tyhpdefSrcFile`, `tyhpdefTaglessSrcFile`, and `tyhpdefBlock`, plus `tyhpdefDeprecatedOrObsolete`, `tyhpdefClassStatement`, and the class-body rules `.tyhp` object shapes call. `.tyhpdef` files do not enter `TyhpParser`. `tyhpdefClassConstDecl` / `tyhpdefClassConstList` exist only on `TyhpParser`.

Tagged files: the open-tag block (`tyhpBlock` or `tyhpdefBlock`) sets **parser** language mode, then the statement list. Tagless: optional open tag token, one statement list, no inline output.

### Parser `_languageMode` vs lexer `_languageMode` (critical asymmetry)

These are **separate fields** on separate objects.

Grammar actions assign **parser** `_languageMode = "tyhp"` (and copy it to `_localctx._languageMode`) in:

- `tyhpBlock`, `tyhpTaglessSrcFile` (`TyhpParser.g4`)
- `tyhpdefBlock`, `tyhpdefTaglessSrcFile` (`shared/Tyhpdef.rules.g4`, included by both parsers)

So a `.tyhpdef` file parsed by `TyhpdefParser` still has `isLanguageMode("tyhp") == true`. That is how Tyhp grammar addons (generics, typed locals, etc.) apply inside tyhpdefs.

The **lexer** sets `_languageMode = "tyhpdef"` on `<?tyhpdef`, so tyhpdef-only tokens (`deprecated`, `obsolete`, `partial`, `extern`, `omit`) match and Tyhp-only lexer keywords (`with`, `using`, `typeof`, …) do not. `TyhpdefLexer` does not include those tyhp-only keyword rules; the names are declared in its `tokens { }` block so a shared parser alternative can mention them.

`PhpParserAstVisitor.GetCurrentLanguageMode` does not read parser `_languageMode`. For a `TyhpdefParser` tree it returns:

| Context | String |
|---------|--------|
| `TyhpdefParser.TyhpdefBlockContext` | `"tyhpdef"` |
| `TyhpdefParser.TyhpdefTaglessFileContext` | `"tyhpdef"` |
| `TyhpdefParser.TyhpdefSrcFileContext` | `""` |

`TyhpdefIncludedPhpVisits.GetCurrentLanguageMode` (the method the `.tyhpdef` visitor calls) returns those same three strings for those same contexts. Other contexts walk parents until one of those matches.

If you add a feature that must be Tyhp-source-only at parse time, gating only on lexer tokens is not enough — parser mode is `"tyhp"` for tyhpdef. No grammar predicate calls `isLanguageMode("tyhpdef")`.

### Semantic predicates in GrammarMethods

**`newIsFollowedByArgumentList`** — used by overridden `newNonDereferenceable` so `new Foo<T>(…)` is not parsed as `(new Foo) < T > (…)`.

**`looksLikeGenericTypedLocal`** — used to disable the bare `phpTopExpr` / for-init expression alternatives when lookahead looks like `Type<Arg> $var` (including unions and parenthesized forms). Cap: `MaxGenericArgumentLookahead = 256`.

**`checkIsTopExpr`** — walks parent contexts; used in `PhpParser.g4` so bitwise `&`/`|`/`^` alternatives that are illegal at “top expression” depth are rejected via `!this.checkIsTopExpr(_localctx)`.

### Prediction / profiling

`CompilationService` defaults `PredictionMode.SLL`, optionally `LL_EXACT_AMBIG_DETECTION` when reporting ambiguities. `DebugAction` can attach `DiagnosticErrorListener` and enable `parser.Profile`.

---

## 6. How parsing feeds Visitor / AST

```
.php / .tyhp
  chars → TyhpLexer → TyhpParser.<entry>()
        → TyhpParserAstVisitor.Visit   // extends PhpParserAstVisitor

.tyhpdef
  chars → TyhpdefLexer → TyhpdefParser.tyhpdefSrcFile / tyhpdefTaglessSrcFile
        → TyhpdefParserAstVisitor.Visit
        // extends TyhpdefIncludedPhpVisits : TyhpdefParserBaseVisitor<IBase2Ast?>
```

### Visitor hierarchy

`.php` and `.tyhp`:

```
TyhpParserBaseVisitor<IBase2Ast?>
        ↑
PhpParserAstVisitor
        ↑
TyhpParserAstVisitor
```

`.tyhpdef`:

```
TyhpdefParserBaseVisitor<IBase2Ast?>
        ↑
TyhpdefIncludedPhpVisits
        ↑
TyhpdefParserAstVisitor
```

`TyhpdefParserAstVisitor` does not extend `PhpParserAstVisitor` or `TyhpParserBaseVisitor`. Context types differ (`TyhpParser.ExprContext` vs `TyhpdefParser.ExprContext`), so one C# method cannot override both. `Visitor/shared/*.inc` is the source. `compile_grammar_emit_partials.py` emits it into `PhpParserAstVisitor` / `TyhpParserAstVisitor` unchanged, and into `TyhpdefIncludedPhpVisits` / `TyhpdefParserAstVisitor` with `TyhpParser` replaced by `TyhpdefParser` and `TyhpLexer` replaced by `TyhpdefLexer`. That emit is not split per parser, which is why `tyhpdefSrcFile` / `tyhpdefTaglessSrcFile` / `tyhpdefBlock` remain on `TyhpParser`: the shared fragment is typed against `TyhpParser` contexts and compiled into `TyhpParserAstVisitor` as well. The file walk for `.tyhpdef` is still `TyhpdefParserAstVisitor`.

`tyhpdefClassConstDecl` / `tyhpdefClassConstList` are not in that shared emit. Their visits stay on `TyhpParserAstVisitor` (`TyhpParserAstVisitor.TyhpdefUnreached.cs`).

Labeled alternatives produce distinct context classes (`#tyhpFile` → `VisitTyhpFile`, `#tyhpdefFile` → `VisitTyhpdefFile`). `.tyhp` roots live in `TyhpParserAstVisitor.TyhpRoot.cs`. `.tyhpdef` roots are the emitted `VisitTyhpdefFile` / `VisitTyhpdefTaglessFile` on `TyhpdefParserAstVisitor`.

### Token stream after parse

Callers often pass the same `CommonTokenStream` into the visitor for doc-comment lookup.

Tagless paths in `CompilationService` / `Tyhpdef.ParseContent` call `tokenStream.Fill()` after the entry rule returns (and before visit). `TokenizeAction` always `Fill()`s. Exact reason tagless-only post-parse Fill is required (vs tagged) is **not documented in source** — see open questions.

### Error recovery and AST caching

ANTLR recovery can still yield a non-null tree. Callers snapshot `diagnostics.CountErrorsForFile` before parse and **refuse to cache** ASTs when new errors appeared (`CompilationService`, `Tyhpdef.ParseContent`). Visitor null-guards malformed children; catastrophic Visit exceptions become `ParserCompileAborted`. A tyhpdef AST cache read or write failure is a `ParserUnknownError` warning (`WARNING_TYHP1001`, placeholder filled with the exception text). The bad or partial cache file is deleted and parsing continues with the source AST.

### Listeners

Not generated (`--generate-listener false`). Prefer visitors; do not add listener-based pipeline code unless regeneration flags change.

---

## 7. Error reporting (`TyhpAntlrErrorListener<TType>`)

- Implements `IAntlrErrorListener<TType>`; writes to `DiagnosticBag`.
- `TType == int` → lexer (character code); default `MessageCode.ParserUnknownError` (1001), or override (e.g. `TyhpdefParseError` = 8001).
- Otherwise → parser token; default `ParserUnexpectedError` (1002).
- `SetFileName` uses `ThreadLocal<string>` for concurrent parses.
- Filters ANTLR chatter containing `reportAttemptingFullContext`, `reportContextSensitivity`, `failed predicate`.
- Formats lexer offending symbols as printable char, `<EOF>`, or hex.
- Implements `IDisposable` (disposes the `ThreadLocal`).

Related lexer diagnostic: `LexerCloseTagNotAllowedInTaglessMode` (1004) from `closeTagHandler`, not from this listener.

---

## 8. Conventions

### When adding syntax

1. Prefer extending **addon** rules, or a `shared/` fragment both parsers include, over editing every PHP rule.
2. Gate Tyhp parser alts with `{this.isLanguageMode("tyhp")}?`. `tyhpdefBlock` and `tyhpdefTaglessSrcFile` assign parser `_languageMode = "tyhp"`, so that predicate is true in `.tyhpdef` files.
3. Gate lexer keywords with the appropriate lexer `_languageMode` check (`"tyhp"`, `"tyhpdef"`, or both). `<?tyhpdef` sets the lexer to `"tyhpdef"`.
4. Put C# called from grammar actions into the Tyhp `*.GrammarMethods.cs` partials. Regen copies them onto `TyhpdefLexer` / `TyhpdefParser`.
5. Run `./compile_grammar.sh`, then update `Visitor/shared/` (both visitors) or the one-parser visitor, and AST nodes.
6. Add tests via `ParserTestHelper` / checker-emitter tests as appropriate. Token comparisons in tyhpdef visitors use `TyhpdefParser.T_*`.

### Editing rules of thumb

| Change | Edit |
|--------|------|
| New token / mode / lexer predicate | Shared or entry `.g4` + maybe `TyhpLexer.GrammarMethods.cs` + regen |
| New parse rule / ambiguity lookahead | Shared or entry `.g4` + maybe `TyhpParser.GrammarMethods.cs` + regen |
| AST shape for a rule both parsers have | `Visitor/shared/*.inc` + regen |
| AST shape for a one-parser rule | That visitor + `Ast/` |
| Diagnostic mapping for syntax errors | `TyhpAntlrErrorListener` / `MessageCode` |

### Naming

- Tokens: `T_…` (Tyhp-specific often `T_TYHP_…` / `T_TYHPDEF_…`).
- Addon stubs: `*GrammarAddon`.
- Labeled alts: `#camelCaseHandler` style names that become `Visit…` methods.
- Partial method files: `*.GrammarMethods.cs` next to generated recognizers.

### Threading

`CompilationService` reuses one `TyhpLexer`/`TyhpParser` pair and one `TyhpdefLexer`/`TyhpdefParser` pair **per thread**. Always `Reset` + `ConfigureTagless` on the lexer for that file + refresh error listeners. Listener `SetFileName` is mandatory under concurrency.

---

## 9. Weirdness / WHY

1. **Synthetic semicolon on `?>`** — keeps statement termination consistent without exploding the grammar (`closeTagHandler`).
2. **`T_NO_GRAMMAR_ADDON_0000`** — forces overridden addon rules to be non-empty stubs in the base grammar.
3. **Parser `_languageMode` is `"tyhp"` inside tyhpdef** — `tyhpdefBlock` and `tyhpdefTaglessSrcFile` assign that string so Tyhp addon predicates apply. `GetCurrentLanguageMode` still returns `"tyhpdef"` for a `TyhpdefParser` block or tagless-file context.
4. **Lexer `_languageMode` is `"tyhpdef"` on `<?tyhpdef`** — tyhpdef-only keywords match. Tyhp-only keyword rules are not in `TyhpdefLexer`.
5. **`prepareLess` / `doPreparedLess`** — speculative consume whitespace/comments for contextual keyword decisions, then rewind input index/line/column.
6. **Constant string folding in `NextToken`** — parser grammar expects `T_CONSTANT_ENCAPSED_STRING` for simple `"…"`, but the lexer modes naturally emit quote + encaps pieces.
7. **Tagless start-mode peek** — avoids injecting a fake open-tag token and keeps line/column accurate when no tag is present.
8. **Visitors in global namespace** — current `antlr-ng` emit quirk with `--package`.
9. **`LanguageModeTotalTime` / `LanguageModeTotalCalls`** — unused counters; likely leftover profiling hooks.

---

## 10. Pitfalls

1. **Edit generated files → lost on regen.** Put logic in GrammarMethods or Visitor.
2. **Forget `./compile_grammar.sh` after `.g4` changes** — `dotnet build` will compile stale parser sources.
3. **Ambiguous `<`** — generics vs comparison. Use / extend `newIsFollowedByArgumentList` and `looksLikeGenericTypedLocal`; wrong prediction yields nonsense trees and bad PHP emit.
4. **Wrong entry rule or tagless flag** — tagless files parsed with tagged rules (or vice versa) fail open/close/inline assumptions.
5. **Assuming parser `_languageMode == "tyhpdef"` for tyhpdef files** — `tyhpdefBlock` and `tyhpdefTaglessSrcFile` assign `"tyhp"`. `PhpParserAstVisitor.GetCurrentLanguageMode` returns `"tyhpdef"` for `TyhpdefParser.TyhpdefBlockContext` and `TyhpdefParser.TyhpdefTaglessFileContext`, and `""` for `TyhpdefParser.TyhpdefSrcFileContext`.
6. **Comparing a `TyhpParser.T_*` integer against a token from `TyhpdefLexer`** — the integers differ. Use the constants of the parser that built the tree.
7. **Doc comments** — must call `FindPossibleDocComment` before visiting children that might advance `_docCommentLastStop`. The tyhpdef visitor reads `TyhpdefLexer.DocBlockCommentsChannel`.
8. **Lexer exceptions from nesting** — mismatched braces throw; may abort outside the DiagnosticBag path depending on caller.
9. **Thread-local Reset vs custom lexer state** — `Reset` on the Tyhp lexer partial (and the emitted `TyhpdefLexer` copy) resets the base lexer, then clears `_pendingTokensQueue`, `_encapsTokensQueue`, `_nestingStack`, `_heredocLabel`, `shouldPopList`, and the `prepareLess*` marks before clearing `_languageMode` and calling `ApplyTaglessStartMode`. Those queues hold peeked tokens whose line numbers belong to the file that produced them. Leaving them across `CompilationService`'s per-thread reuse attaches the next file's diagnostics to the previous file's tokens, and harvest then skips a file that parsed.
10. **Filtering `failed predicate` messages** — predicate failures may be silent in diagnostics; use ambiguity/profile CLI flags when hunting prediction bugs.
11. **Hand-edit `Tyhp/TyhpLang/Grammar/.antlr` Java, or the emitted `Tyhpdef*.GrammarMethods.cs`** — the Java tree is irrelevant to the C# compiler. The tyhpdef GrammarMethods files are overwritten from the Tyhp partials.

---

## 11. File-by-file cheat sheet

### `TyhpLexer.cs` (generated)

Token/mode/channel constants, ATN, `partial class TyhpLexer`.

### `TyhpLexer.GrammarMethods.cs` (hand-written)

All lexer actions + `NextToken` + tagless configuration. First place to look for “why did this tokenize oddly?”

### `TyhpParser.cs` (generated)

Rule methods (`tyhpSrcFile()`, …), context classes, labeled-alt subclasses, Accept → visitor dispatch.

### `TyhpParser.GrammarMethods.cs` (hand-written)

`isLanguageMode`, generic/`new` lookahead, `checkIsTopExpr`, `reportExternClassCannotHaveBody`.

### `TyhpParserVisitor.cs` / `TyhpParserBaseVisitor.cs` / `TyhpdefParserVisitor.cs` / `TyhpdefParserBaseVisitor.cs` (generated)

Contracts and default VisitChildren implementations. `.php` / `.tyhp` extend via `PhpParserAstVisitor` / `TyhpParserAstVisitor`. `.tyhpdef` extends via `TyhpdefIncludedPhpVisits` / `TyhpdefParserAstVisitor`. Do not edit the generated contracts.

### `TyhpdefLexer.cs` / `TyhpdefParser.cs` / `Tyhpdef*.GrammarMethods.cs`

Same roles as the Tyhp recognizers, for `.tyhpdef`. GrammarMethods are the class-renamed Tyhp partials. `ConfigureTagless` on `TyhpdefLexer` is that copy.

### `TyhpAntlrErrorListener.cs` (hand-written)

Syntax errors → `DiagnosticBag`.

---

## 12. Open questions

Grounded gaps — do not assume answers without further investigation:

1. **Why `Fill()` only on tagless paths** after parse in `CompilationService` / `Tyhpdef.ParseContent`, while `TokenizeAction` always fills? Is tagged mode relying on incidental buffering, or is tagless hitting a specific CommonTokenStream edge case?
2. **`LanguageModeTotalTime` / `LanguageModeTotalCalls`** — intended to wrap `isLanguageMode`, abandoned, or used by an external profiler not in-repo?
3. **Visitor global namespace** — intentional `antlr-ng` limitation, or should compile_grammar post-process / switch generator flags?
4. **ANTLR 4.13.1 runtime vs 4.13.2-generated sources** — any known incompatibilities worth pinning together?
5. **Is there any scenario where parser `_languageMode` should be `"tyhpdef"`** (e.g. future tyhpdef-only syntax that must not activate `isLanguageMode("tyhp")` addons)?
6. **Cached AST + tagless `Seek(0); Fill()`** branch in `CompilationService` when cache hits — what consumer needs the refilled stream if Visit is skipped?

---

## 13. Related reading

- `docs/content/intro_newSyntaxCreation.md` — pipeline and syntax-design principles  
- `Tyhp/TyhpLang/Grammar/TyhpLexer.g4`, `TyhpParser.g4`, `TyhpdefLexer.g4`, `TyhpdefParser.g4`, `PhpLexer.g4`, `PhpParser.g4`, `shared/`
- `./compile_grammar.sh`, `./compile_grammar_emit_partials.py`  
- `Tyhp/TyhpLang/Visitor/` — AST construction  
- `Tyhp/Domain/Services/CompilationService.cs` — production parse orchestration  
- `tests/Tyhp.Tests/TestHelpers/ParserTestHelper.cs` — test entry point  
