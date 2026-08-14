# TyhpLang Parser — Developer Technical Guide

This guide covers the C# parser layer under `Tyhp/TyhpLang/Parser/`: how it relates to the ANTLR `.g4` grammars, what is generated vs hand-written, how lex/parse feeds the Visitor → AST pipeline, and the non-obvious behaviors you will hit when changing syntax.

**Scope of this directory (7 files):**

| File | Role | Origin |
|------|------|--------|
| `TyhpLexer.cs` | Lexer recognizer (tokens, modes, ATN) | Generated (`antlr-ng`, ANTLR 4.13.2) |
| `TyhpParser.cs` | Parser recognizer (rules, contexts, ATN) | Generated |
| `TyhpParserVisitor.cs` | `ITyhpParserVisitor<Result>` interface | Generated |
| `TyhpParserBaseVisitor.cs` | Default visitor (VisitChildren) | Generated |
| `TyhpLexer.GrammarMethods.cs` | Lexer helpers + `NextToken` overrides | Hand-written partial |
| `TyhpParser.GrammarMethods.cs` | Parser semantic predicates / lookahead | Hand-written partial |
| `TyhpAntlrErrorListener.cs` | Diagnostics bridge for lexer/parser errors | Hand-written |

Line counts (approx., as of this writing): `TyhpParser.cs` ~33k, `TyhpParserBaseVisitor.cs` ~6k, `TyhpParserVisitor.cs` ~3.7k, `TyhpLexer.cs` ~2.3k, hand-written files a few hundred lines each.

---

## 1. Role in the compiler pipeline

Pipeline order (see also `docs/content/intro_newSyntaxCreation.md`):

1. **Lexer** (`TyhpLexer`) — source chars → token stream  
2. **Parser** (`TyhpParser`) — tokens → ANTLR parse tree (`ParserRuleContext`)  
3. **Visitor** (`TyhpParserAstVisitor` / `PhpParserAstVisitor` in `TyhpLang/Visitor/`) — parse tree → `SrcFileAst`  
4. Binder → Checker → Emitter  

The `Parser/` directory owns steps 1–2 and the **generated visitor contracts**. It does **not** own AST node types or the hand-written visit implementations (those live under `Visitor/` and `Ast/`).

### Primary call sites

- **`CompilationService`** (`Tyhp/Domain/Services/CompilationService.cs`) — production multi-file parse. Uses `ThreadLocal<TyhpLexer>` / `ThreadLocal<TyhpParser>`, resets per file, picks entry rule by extension (`.tyhpdef` before `.tyhp` because `.tyhpdef` ends with `.tyhp`), then `TyhpParserAstVisitor.Visit(ctx)`.
- **`Tyhpdef.ParseContent`** (`Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.cs`) — single-string parse used for package/runtime tyhpdefs and tests (`ParserTestHelper`). Same lexer/parser/visitor pattern with optional AST cache.
- **`DebugAction` / `TokenizeAction` / `DumpAstAction`** — CLI debugging (parse tree dump, tokenize JSON, etc.).

Typical sequence (simplified from `Tyhpdef.ParseContent`):

```csharp
var inputStream = new AntlrInputStream(...);
var lexer = new TyhpLexer(inputStream);
lexer.RemoveErrorListeners();
lexer.AddErrorListener(new TyhpAntlrErrorListener<int>(diagnostics, ...));
lexer.ConfigureTagless(taglessEnabled, taglessLanguageMode, diagnostics, fileName);

var tokenStream = new CommonTokenStream(lexer);
var parser = new TyhpParser(tokenStream, TextWriter.Null, TextWriter.Null);
parser.RemoveErrorListeners();
parser.AddErrorListener(new TyhpAntlrErrorListener<IToken>(diagnostics, ...));

ParserRuleContext ctx = mode switch {
    ParseMode.Tyhpdef => tagless ? parser.tyhpdefTaglessSrcFile() : parser.tyhpdefSrcFile(),
    ParseMode.Tyhp    => tagless ? parser.tyhpTaglessSrcFile()    : parser.tyhpSrcFile(),
    _                 => parser.phpSrcFile(),
};

var visitor = new TyhpParserAstVisitor(tokenStream, fileName, fileHash, diagnostics);
var ast = visitor.Visit(ctx) as SrcFileAst;
```

`ParseMode` (`Tyhp/TyhpLang/Enum/ParseMode.cs`): `Php`, `Tyhpdef`, `Tyhp`.

---

## 2. Relationship to `Tyhp/TyhpLang/Grammar/` ANTLR sources

### Source of truth

| Grammar file | Role |
|--------------|------|
| `Tyhp/TyhpLang/Grammar/PhpLexer.g4` | Base PHP lexer (modes, channels, PHP tokens). Imported by Tyhp lexer. |
| `Tyhp/TyhpLang/Grammar/PhpParser.g4` | Base PHP parser (LL rewrite of zend_language_parser.y). Imported by Tyhp parser. Defines **grammar addon** extension points. |
| `Tyhp/TyhpLang/Grammar/TyhpLexer.g4` | Tyhp/tyhpdef tokens + tagless mode; `import PhpLexer;` |
| `Tyhp/TyhpLang/Grammar/TyhpParser.g4` | Tyhp/tyhpdef entry rules + addon overrides; `import PhpParser;` `tokenVocab=TyhpLexer;` |

Only **`TyhpLexer.g4` and `TyhpParser.g4`** are passed to the C# generator. Php grammars are pulled in via ANTLR `import`.

### Regeneration

Root script: `./compile_grammar.sh`

- Requires `antlr-ng` (`npm install -g antlr-ng`).
- Emits C# into `./Tyhp/TyhpLang/Parser` with package `Tyhp.TyhpLang.Parser`, visitor on, listener off.
- Moves `*.tokens` / `*.interp` into `./Tyhp/TyhpLang/Grammar/`.
- **Not** part of `dotnet build`. Changing `.g4` without running the script leaves stale generated C#.

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
- Marked `<auto-generated>`, `GeneratedCode("ANTLR", "4.13.2")`, `partial class` / interface
- Regenerating **overwrites** these files entirely

### Hand-written partials (safe to edit; must stay compatible with grammar actions)

**`TyhpLexer.GrammarMethods.cs`** — `partial class TyhpLexer : Lexer`. Implements everything the `.g4` actions call, plus token-stream post-processing:

- State: `_languageMode`, tagless fields, `_pendingTokensQueue`, `_encapsTokensQueue`, `_heredocLabel`, `_nestingStack`, `prepareLess*` marks, `shouldPopList`
- `ConfigureTagless` / `ApplyTaglessStartMode` / `HasLiteralOpenTagAtStart`
- `NextToken` override (heredoc line fix, constant double-quoted string folding, consecutive `T_ENCAPSED_AND_WHITESPACE` / `T_INLINE_HTML` combining)
- Nesting (`enterNesting` / `exitNesting`), heredoc helpers, `closeTagHandler`, stream lookahead (`streamPeek`, `streamLA`, `streamLAEq`, `isFollowedByVarOrVarArg`), `prepareLess` / `doPreparedLess` / `less`

**`TyhpParser.GrammarMethods.cs`** — `partial class TyhpParser : Parser`:

- `_languageMode` + `isLanguageMode(string)`
- `newIsFollowedByArgumentList()` — disambiguate `new X<T>(args)` vs comparison
- `looksLikeGenericTypedLocal()` — disambiguate `Box<int> $x` vs comparison
- `checkIsTopExpr(RuleContext)` — used from `PhpParser.g4` for `&` / `|` / `^` in expression vs top-expr contexts
- Dead-looking counters: `LanguageModeTotalTime`, `LanguageModeTotalCalls` (declared; **no increments found** in the repo)

**`TyhpAntlrErrorListener.cs`** — not generated; not a partial of the recognizers.

### Namespace quirk (visitors)

- `TyhpLexer` / `TyhpParser` are in namespace `Tyhp.TyhpLang.Parser` (as requested by `--package`).
- Generated `ITyhpParserVisitor<T>` and `TyhpParserBaseVisitor<T>` currently have **no** `namespace` declaration (global namespace), despite `--package`. Hand-written visitors under `Tyhp.TyhpLang.Visitor` still resolve them because global types are visible. If `antlr-ng` behavior changes and starts emitting a namespace, regeneration may require visitor `using` / namespace cleanup — verify after each regen.

---

## 4. Lexer behavior (deep dive)

### Modes (from generated `modeNames`)

`DEFAULT_MODE`, `ST_CHECK_FOR_OTHER_OPEN_TAGS_LEXER_ADDON`, `ST_IN_SCRIPTING`, `ST_TYHP_TAGLESS`, `ST_INLINE_HTML`, `ST_LOOKING_FOR_PROPERTY`, `ST_DOUBLE_QUOTES`, `ST_BACKQUOTE`, `ST_HEREDOC`, `ST_NOWDOC`, `ST_LOOKING_FOR_VARNAME`, `ST_VAR_OFFSET`.

Default mode handles open tags and inline HTML. Scripting keywords live in `ST_IN_SCRIPTING`.

Open-tag routing:

- `<?php` / `<?=` → PHP / phpEcho language mode (`PhpLexer.g4`)
- `<?` then push `ST_CHECK_FOR_OTHER_OPEN_TAGS_LEXER_ADDON` where Tyhp defines `tyhp` / `tyhpdef` open tags (`TyhpLexer.g4`)
- Tagless: optional start in `ST_TYHP_TAGLESS` to consume a literal `<?tyhp` / `<?tyhpdef`

### Channels

From `PhpLexer.g4`: `DocBlockCommentsChannel`, `SimpleCommentsChannel`, `WhiteSpaceChannel`, `ErrorLexemChannel`, `SkipChannel`, `StubTokenChannel`.

Parser default channel ignores whitespace/comments. Visitors recover docblocks by walking the **token stream** looking for `TyhpLexer.DocBlockCommentsChannel` (`PhpParserAstVisitor.FindPossibleDocComment`).

### Lexer `_languageMode` (string)

Set when an open tag is recognized (or by `ConfigureTagless` when starting tagless without a tag):

| Value | Effect (examples) |
|-------|-------------------|
| `"php"` / `"phpEcho"` | PHP scripting |
| `"tyhp"` | Tyhp keywords: `with`, `using`, `is`/`isa`/…, `typeof`, `nameof`, `variable_exists`, `:=`, plus shared Tyhp keywords |
| `"tyhpdef"` | Shared Tyhp keywords (`struct`, `type`, `async`, …) **plus** `deprecated` / `obsolete`; **not** the Tyhp-only keyword set above |

Contextual keywords (`struct`, `type`, `extension`) use `prepareLess` + `streamLA` so `struct extends` / identifier contexts still produce `T_STRING` when appropriate.

### Tagless lexing

`ConfigureTagless(enabled, languageMode, diagnostics?, fileName?)`:

- If disabled: clear tagless state.
- If enabled: `ApplyTaglessStartMode()`:
  - Literal `<?tyhp` / `<?tyhpdef` at start (optional leading whitespace) → `Mode(ST_TYHP_TAGLESS)` so the tag is consumed.
  - Otherwise → set `_languageMode` to the tagless language mode and `Mode(ST_IN_SCRIPTING)` (no synthetic token).
- `<?php` is **not** treated as a tagless open tag (left for scripting-mode lexing).
- Closing `?>` in tagless: lexer still may emit close-tag behavior; `closeTagHandler` can add `LexerCloseTagNotAllowedInTaglessMode` (1004). Tagless **parser** entry rules omit `T_CLOSE_TAG` / inline HTML so `?>` is not in the expected follow set.

### `NextToken` post-processing (why it exists)

ANTLR’s base lexer is not enough for PHP/Tyhp string/heredoc quirks:

1. **`T_END_HEREDOC`** — bump line/column/start index (closing label is on the next line in PHP semantics).
2. **Constant double-quoted strings** — if `"…"` contains only `T_ENCAPSED_AND_WHITESPACE` then closing `"`, fold into a single `T_CONSTANT_ENCAPSED_STRING` (so the parser sees a scalar, not an encaps list).
3. **Merge consecutive** `T_ENCAPSED_AND_WHITESPACE` or `T_INLINE_HTML` into one token.

Pending/encaps queues + peek helpers exist because folding needs 1-token lookahead without losing tokens.

### Nesting stack

Brace tokens call `enterNesting` / `exitNesting` with `BraceType` (`square` / `round` / `curly`) so interpolated `{…}` / `${…}` can pop back to the correct lexer mode (`popModeBackTo`, `additionalPopMode`). Mismatched braces throw plain `Exception` (not `DiagnosticBag`) from the lexer helpers.

### `closeTagHandler`

On `?>`, enqueues a synthetic `T_SYM_SEMICOLON` so the parser can treat close-tag as a statement terminator without duplicating every statement rule. In tagless mode with diagnostics attached, also reports `LexerCloseTagNotAllowedInTaglessMode`.

---

## 5. Parser behavior (deep dive)

### Entry rules

| Rule | When used |
|------|-----------|
| `tyhpSrcFile` | `.tyhp` (tagged; allows inline HTML / close tags) |
| `tyhpdefSrcFile` | `.tyhpdef` (tagged) |
| `tyhpTaglessSrcFile` | `.tyhp` + `source.tagless` |
| `tyhpdefTaglessSrcFile` | `.tyhpdef` + `source.tagless` |
| `phpSrcFile` | `.php` / other / `ParseMode.Php` |

Tagged Tyhp files: open tag → `tyhpBlock` / `tyhpdefBlock` sets **parser** language mode, then statement lists. Tagless: optional open tag token, single statement list, no inline output.

### Parser `_languageMode` vs lexer `_languageMode` (critical asymmetry)

These are **separate fields** on separate objects.

Grammar actions in `TyhpParser.g4` set **parser** `_languageMode = "tyhp"` for:

- `tyhpBlock`, `tyhpdefBlock`
- `tyhpTaglessSrcFile`, `tyhpdefTaglessSrcFile`

So even **tyhpdef** files run parser predicates with `isLanguageMode("tyhp") == true`. That is how Tyhp grammar addons (generics, typed locals, etc.) apply inside tyhpdefs.

Meanwhile the **lexer** uses `"tyhpdef"` for `<?tyhpdef` so tyhpdef-only tokens (`deprecated`, `obsolete`) work and Tyhp-only lexer keywords (`with`, `using`, `is`, …) do **not** fire in tyhpdef mode.

If you add a feature that must be Tyhp-source-only at parse time, gating only on lexer tokens is not enough — also check whether parser mode being `"tyhp"` for tyhpdef is acceptable, or introduce an explicit `isLanguageMode("tyhpdef")` (none found in current grammar predicates).

### Semantic predicates in GrammarMethods

**`newIsFollowedByArgumentList`** — used by overridden `newNonDereferenceable` so `new Foo<T>(…)` is not parsed as `(new Foo) < T > (…)`.

**`looksLikeGenericTypedLocal`** — used to disable the bare `phpTopExpr` / for-init expression alternatives when lookahead looks like `Type<Arg> $var` (including unions and parenthesized forms). Cap: `MaxGenericArgumentLookahead = 256`.

**`checkIsTopExpr`** — walks parent contexts; used in `PhpParser.g4` so bitwise `&`/`|`/`^` alternatives that are illegal at “top expression” depth are rejected via `!this.checkIsTopExpr(_localctx)`.

### Prediction / profiling

`CompilationService` defaults `PredictionMode.SLL`, optionally `LL_EXACT_AMBIG_DETECTION` when reporting ambiguities. `DebugAction` can attach `DiagnosticErrorListener` and enable `parser.Profile`.

---

## 6. How parsing feeds Visitor / AST

```
chars → TyhpLexer → CommonTokenStream → TyhpParser.<entryRule>()
      → ParserRuleContext tree
      → TyhpParserAstVisitor.Visit(tree)  // extends PhpParserAstVisitor
      → SrcFileAst (+ nested Ast nodes)
```

### Visitor hierarchy

```
TyhpParserBaseVisitor<IBase2Ast?>     // generated defaults (global namespace)
        ↑
PhpParserAstVisitor                   // PHP rule Visit* implementations
        ↑
TyhpParserAstVisitor                  // Tyhp / tyhpdef Visit* overrides
```

- Implements `ITyhpParserVisitor<IBase2Ast?>`.
- Labeled alternatives in the grammar produce distinct context classes and Visit methods (e.g. `#tyhpFile` → `VisitTyhpFile`, `#tyhpTaglessFile` → `VisitTyhpTaglessFile`).
- Dispatch helpers in visitor partials sometimes `switch` on context type when a rule has multiple labeled alts (e.g. `VisitTyhpCodeBlock`).

Entry Visit methods live in `Visitor/TyhpParserAstVisitor.TyhpRoot.cs` and `TyhpParserAstVisitor.Tyhpdef.cs`; they construct `TyhpSrcFileAst` / tyhpdef equivalents and recurse into statement lists.

### Token stream after parse

Callers often pass the same `CommonTokenStream` into the visitor for doc-comment lookup.

Tagless paths in `CompilationService` / `Tyhpdef.ParseContent` call `tokenStream.Fill()` after the entry rule returns (and before visit). `TokenizeAction` always `Fill()`s. Exact reason tagless-only post-parse Fill is required (vs tagged) is **not documented in source** — see open questions.

### Error recovery and AST caching

ANTLR recovery can still yield a non-null tree. Callers snapshot `diagnostics.CountErrorsForFile` before parse and **refuse to cache** ASTs when new errors appeared (`CompilationService`, `Tyhpdef.ParseContent`). Visitor null-guards malformed children; catastrophic Visit exceptions become `ParserCompileAborted`.

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

1. Prefer extending **addon** rules in `TyhpParser.g4` / tokens in `TyhpLexer.g4` over editing every PHP rule.
2. Gate Tyhp-only parser alts with `{this.isLanguageMode("tyhp")}?` (remember tyhpdef parser mode is also `"tyhp"`).
3. Gate lexer keywords with the appropriate `_languageMode` check (`tyhp`, `tyhpdef`, or both).
4. Put C# called from grammar actions into `*.GrammarMethods.cs` partials — never into generated files.
5. Run `./compile_grammar.sh`, then update `Visitor/` Visit methods and AST nodes.
6. Add tests via `ParserTestHelper` / checker-emitter tests as appropriate.

### Editing rules of thumb

| Change | Edit |
|--------|------|
| New token / mode / lexer predicate | `.g4` + maybe `TyhpLexer.GrammarMethods.cs` + regen |
| New parse rule / ambiguity lookahead | `.g4` + maybe `TyhpParser.GrammarMethods.cs` + regen |
| AST shape | `Visitor/` + `Ast/` only |
| Diagnostic mapping for syntax errors | `TyhpAntlrErrorListener` / `MessageCode` |

### Naming

- Tokens: `T_…` (Tyhp-specific often `T_TYHP_…` / `T_TYHPDEF_…`).
- Addon stubs: `*GrammarAddon`.
- Labeled alts: `#camelCaseHandler` style names that become `Visit…` methods.
- Partial method files: `*.GrammarMethods.cs` next to generated recognizers.

### Threading

`CompilationService` reuses one lexer/parser pair **per thread**. Always `Reset` + `ConfigureTagless` + refresh error listeners per file. Listener `SetFileName` is mandatory under concurrency.

---

## 9. Weirdness / WHY

1. **Synthetic semicolon on `?>`** — keeps statement termination consistent without exploding the grammar (`closeTagHandler`).
2. **`T_NO_GRAMMAR_ADDON_0000`** — forces overridden addon rules to be non-empty stubs in the base grammar.
3. **Parser mode `"tyhp"` inside tyhpdef** — reuses Tyhp parser extensions for definition files without duplicating every addon predicate for `"tyhpdef"`.
4. **Lexer mode still `"tyhpdef"`** — keeps tyhpdef-only keywords and excludes Tyhp-only surface syntax at token level.
5. **`prepareLess` / `doPreparedLess`** — speculative consume whitespace/comments for contextual keyword decisions, then rewind input index/line/column.
6. **Constant string folding in `NextToken`** — parser grammar expects `T_CONSTANT_ENCAPSED_STRING` for simple `"…"`, but the lexer modes naturally emit quote + encaps pieces.
7. **`.tyhpdef` checked before `.tyhp`** — extension suffix overlap.
8. **Tagless start-mode peek** — avoids injecting a fake open-tag token and keeps line/column accurate when no tag is present.
9. **Visitors in global namespace** — current `antlr-ng` emit quirk with `--package`.
10. **`LanguageModeTotalTime` / `LanguageModeTotalCalls`** — unused counters; likely leftover profiling hooks.

---

## 10. Pitfalls

1. **Edit generated files → lost on regen.** Put logic in GrammarMethods or Visitor.
2. **Forget `./compile_grammar.sh` after `.g4` changes** — `dotnet build` will compile stale parser sources.
3. **Ambiguous `<`** — generics vs comparison. Use / extend `newIsFollowedByArgumentList` and `looksLikeGenericTypedLocal`; wrong prediction yields nonsense trees and bad PHP emit.
4. **Wrong entry rule or tagless flag** — tagless files parsed with tagged rules (or vice versa) fail open/close/inline assumptions.
5. **Assuming parser `_languageMode == "tyhpdef"` for tyhpdef files** — it does not; it is `"tyhp"`.
6. **Doc comments** — must call `FindPossibleDocComment` before visiting children that might advance `_docCommentLastStop`.
7. **Lexer exceptions from nesting** — mismatched braces throw; may abort outside the DiagnosticBag path depending on caller.
8. **Thread-local Reset vs custom lexer state** — `TyhpLexer.Reset` (GrammarMethods) resets base lexer, clears `_languageMode`, then `ApplyTaglessStartMode`. It does **not** explicitly clear `_nestingStack`, pending/encaps queues, `_heredocLabel`, or `shouldPopList`. Healthy files usually leave those empty at EOF; a previous file that threw mid-nest could theoretically poison the next file on the same thread. Treat as a hazard when debugging “impossible” lexer failures under parallel compile.
9. **Filtering `failed predicate` messages** — predicate failures may be silent in diagnostics; use ambiguity/profile CLI flags when hunting prediction bugs.
10. **Do not hand-edit `Tyhp/TyhpLang/Grammar/.antlr` Java** — irrelevant to the C# compiler.

---

## 11. File-by-file cheat sheet

### `TyhpLexer.cs` (generated)

Token/mode/channel constants, ATN, `partial class TyhpLexer`.

### `TyhpLexer.GrammarMethods.cs` (hand-written)

All lexer actions + `NextToken` + tagless configuration. First place to look for “why did this tokenize oddly?”

### `TyhpParser.cs` (generated)

Rule methods (`tyhpSrcFile()`, …), context classes, labeled-alt subclasses, Accept → visitor dispatch.

### `TyhpParser.GrammarMethods.cs` (hand-written)

`isLanguageMode`, generic/`new` lookahead, `checkIsTopExpr`.

### `TyhpParserVisitor.cs` / `TyhpParserBaseVisitor.cs` (generated)

Contracts and default VisitChildren implementations. Extend via `PhpParserAstVisitor` / `TyhpParserAstVisitor`, not by editing these (except understanding new Visit* names after regen).

### `TyhpAntlrErrorListener.cs` (hand-written)

Syntax errors → `DiagnosticBag`.

---

## 12. Open questions

Grounded gaps — do not assume answers without further investigation:

1. **Why `Fill()` only on tagless paths** after parse in `CompilationService` / `Tyhpdef.ParseContent`, while `TokenizeAction` always fills? Is tagged mode relying on incidental buffering, or is tagless hitting a specific CommonTokenStream edge case?
2. **Should `TyhpLexer.Reset` clear** nesting stack, token queues, heredoc label, and `shouldPopList` for thread-local reuse safety?
3. **`LanguageModeTotalTime` / `LanguageModeTotalCalls`** — intended to wrap `isLanguageMode`, abandoned, or used by an external profiler not in-repo?
4. **Visitor global namespace** — intentional `antlr-ng` limitation, or should compile_grammar post-process / switch generator flags?
5. **ANTLR 4.13.1 runtime vs 4.13.2-generated sources** — any known incompatibilities worth pinning together?
6. **Is there any scenario where parser `_languageMode` should be `"tyhpdef"`** (e.g. future tyhpdef-only syntax that must not activate `isLanguageMode("tyhp")` addons)?
7. **Cached AST + tagless `Seek(0); Fill()`** branch in `CompilationService` when cache hits — what consumer needs the refilled stream if Visit is skipped?

---

## 13. Related reading

- `docs/content/intro_newSyntaxCreation.md` — pipeline and syntax-design principles  
- `Tyhp/TyhpLang/Grammar/TyhpLexer.g4`, `Tyhp/TyhpLang/Grammar/TyhpParser.g4`, `Tyhp/TyhpLang/Grammar/PhpLexer.g4`, `Tyhp/TyhpLang/Grammar/PhpParser.g4`  
- `./compile_grammar.sh`  
- `Tyhp/TyhpLang/Visitor/` — AST construction  
- `Tyhp/Domain/Services/CompilationService.cs` — production parse orchestration  
- `tests/Tyhp.Tests/TestHelpers/ParserTestHelper.cs` — test entry point  
