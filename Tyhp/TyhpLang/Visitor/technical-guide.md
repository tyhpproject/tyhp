# Parser → AST Visitors — Technical Guide

Developer guide for the parse-tree visitors under `Tyhp/TyhpLang/Visitor/`. These classes turn ANTLR `ParserRuleContext` trees into `Base2Ast`-derived nodes used by the binder, checker, and emitter.

**Scope:** the C# visitors in this folder (`PhpParserAstVisitor.*`, `TyhpParserAstVisitor.*`, `TyhpdefIncludedPhpVisits.*`, `TyhpdefParserAstVisitor.*`) plus `shared/*.inc`, the source `compile_grammar_emit_partials.py` copies into the `*.Included.cs` partials. Grammar sources live in `Tyhp/TyhpLang/Grammar/`; generated parser types in `Tyhp/TyhpLang/Parser/`; AST types in `Tyhp/TyhpLang/Ast/`.

---

## 1. Role in the compilation pipeline

```
.php / .tyhp
  → TyhpLexer → TyhpParser (phpSrcFile | tyhpSrcFile | tyhpTaglessSrcFile)
  → TyhpParserAstVisitor.Visit(ctx)          ← extends PhpParserAstVisitor

.tyhpdef
  → TyhpdefLexer → TyhpdefParser (tyhpdefSrcFile | tyhpdefTaglessSrcFile)
  → TyhpdefParserAstVisitor.Visit(ctx)       ← extends TyhpdefIncludedPhpVisits
                                               : TyhpdefParserBaseVisitor<IBase2Ast?>

  → SrcFileAst (PhpSrcFileAst | TyhpSrcFileAst | TyhpdefSrcFileAst)
  → Binder → Checker → Emitter
```

### Primary entry: `CompilationService.ParseFile`

`Tyhp/Domain/Services/CompilationService.cs` checks `.tyhpdef` before `.tyhp` in the if/else chain (neither extension is a suffix of the other). A `.tyhpdef` file uses `TyhpdefLexer` / `TyhpdefParser` and `new TyhpdefParserAstVisitor(...)`. A `.tyhp` or `.php` file uses `TyhpLexer` / `TyhpParser` and `new TyhpParserAstVisitor(...)`. Tagless files use the tagless entry rule when `options.Tagless` is set, and `ConfigureTagless` runs on the lexer for that file (`"tyhpdef"` or `"tyhp"`).

1. Calls `visitor.Visit(ctx)`.
2. Casts the result to `SrcFileAst`. A non-null non-`SrcFileAst` result is reported as `MessageCode.VisitorUnexpectedAlternative`.
3. Caches the AST only when the visit produced no new errors for that file (recovery trees are not cached).

### Secondary entry: builtin tyhpdef loading

`Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.cs` uses the same split: `ParseMode.Tyhpdef` visits with `TyhpdefParserAstVisitor`; `ParseMode.Tyhp` and `ParseMode.Php` visit with `TyhpParserAstVisitor`. Both cast `Visit(ctx) as SrcFileAst` inside a try/catch safety net for recovery NREs.

### Historical note

`Tyhp/TyhpLang/TyhpCompiler.cs` is fully commented out; it is not a live entry point. Older snippets that call `VisitTyhpFile` directly are obsolete relative to `CompilationService`.

---

## 2. Class architecture

### Inheritance

`.php` and `.tyhp`:

```
TyhpParserBaseVisitor<IBase2Ast?>     (ANTLR-generated)
        ↑
PhpParserAstVisitor                   (partial; PHP visits + helpers)
        ↑
TyhpParserAstVisitor                  (partial; Tyhp visits)
```

`.tyhpdef`:

```
TyhpdefParserBaseVisitor<IBase2Ast?>  (ANTLR-generated)
        ↑
TyhpdefIncludedPhpVisits              (PHP closure visits for TyhpdefParser contexts)
        ↑
TyhpdefParserAstVisitor               (Tyhp and tyhpdef visits)
```

`TyhpdefParserAstVisitor` does not extend `PhpParserAstVisitor`. Production `.php` and `.tyhp` files instantiate `TyhpParserAstVisitor`. Production `.tyhpdef` files instantiate `TyhpdefParserAstVisitor`.

Shared visit methods have one source under `Visitor/shared/`. `./compile_grammar.sh` runs `compile_grammar_emit_partials.py`, which writes `PhpParserAstVisitor.Included.cs` and `TyhpParserAstVisitor.Included.cs` from those `.inc` files, and writes `TyhpdefIncludedPhpVisits.Included.cs` and `TyhpdefParserAstVisitor.Included.cs` with `TyhpParser` / `TyhpLexer` renamed to `TyhpdefParser` / `TyhpdefLexer`. `PhpParserAstVisitor.GetCurrentLanguageMode` in a shared fragment becomes `TyhpdefIncludedPhpVisits.GetCurrentLanguageMode` on the tyhpdef side. The emit is not split per parser: a rule that both visitors implement has to exist on both grammars. That is why `tyhpdefSrcFile`, `tyhpdefTaglessSrcFile`, and `tyhpdefBlock` remain on `TyhpParser` even though `.tyhpdef` files are parsed with `TyhpdefParser`. `tyhpdefClassConstDecl` / `tyhpdefClassConstList` are unreached, stay only on `TyhpParser`, and are visited only from `TyhpParserAstVisitor.TyhpdefUnreached.cs`.

### Why partial classes

The visitor is split by grammar area so individual files stay reviewable. Naming convention:

| Prefix | Meaning |
|--------|---------|
| `PhpParserAstVisitor.Php*.cs` | Base PHP rule visitors for `TyhpParser` contexts |
| `TyhpParserAstVisitor.Tyhp*.cs` | Tyhp language extensions for `TyhpParser` contexts |
| `*.Included.cs` | Emitted from `shared/`. Do not edit; change the `.inc` and regenerate |
| `TyhpdefIncludedPhpVisits.cs` / `.Helpers.cs` | State, doc comments, language mode for the tyhpdef walk |
| `TyhpdefParserAstVisitor.cs` / `.Helpers.cs` | Ctor and tyhpdef-only helpers |
| `TyhpParserAstVisitor.TyhpdefUnreached.cs` | `tyhpdefClassConstDecl` / `tyhpdefClassConstList` only |
| `PhpParserAstVisitor.cs` | State, doc comments, language mode, VisitChildren shutdown for the PHP/Tyhp walk |
| `TyhpParserAstVisitor.cs` | Thin ctor forwarding to `PhpParserAstVisitor` |
| `PhpParserAstVisitor.Unsorted.cs` | Empty placeholder |

### Shared instance state (`PhpParserAstVisitor.cs`)

| Member | Role |
|--------|------|
| `_tokens` | `CommonTokenStream` for docblock channel walks |
| `_docCommentLastStop` | Cursor so each docblock is claimed at most once |
| `_filename`, `_fileHash` | Plumbed into `*SrcFileAst.Create(...)` |
| `Diagnostics` | Shared `DiagnosticBag` for visitor-time errors |
| `CurrentTopStatementList` | Active top-level statement list (anon structs, echo blocks, etc.) |

---

## 3. How ANTLR trees become AST nodes

### Explicit visitation (not default tree walking)

`PhpParserAstVisitor` disables ANTLR’s default recursive walk:

- `VisitChildren` → always `null`
- `ShouldVisitNextChild` → always `false`
- `DefaultResult` → `null`
- `AggregateResult` → `null`
- `VisitTerminal` / `VisitErrorNode` → `null`

Every child that should appear in the AST must be visited **explicitly** by the parent’s `Visit*` method (or by a typed dispatch helper). Calling bare `Visit(someChild)` only works when that child’s `Accept` routes to an overridden typed method; unlabeled / unexpected alternatives must not rely on `VisitChildren`.

### Accept → typed override

`visitor.Visit(ctx)` ends up in `ctx.Accept(visitor)`, which calls the generated `VisitXxx(XxxContext)` for that labeled alternative. Overrides return concrete AST types (`PhpFunctionDeclAst`, `IExpression`, …) that are assignable to `IBase2Ast?`.

### Factory construction pattern

Typical shape:

```csharp
return SomeAst.Create(
    /* visited children */,
    context,
    GetCurrentLanguageMode(context)
).WithGrammarAddon("key", addon)
 .WithAttributes(attributes);
```

- `SomeAst.Create(...)` sets children, flags, source span via `Base2Ast.SetContext`.
- `languageMode` is usually `GetCurrentLanguageMode(context)` (see §5).
- Error paths use `SomeAst.CreateError(context, languageMode)` or `ErrorAst.Create(...)`.

### Labeled-alternative dispatch

For rules with `#label` alternatives, visitors pattern-match on the generated context subclass:

```csharp
return context switch
{
    TyhpParser.FooBarContext ctx => this.VisitFooBar(ctx),
    TyhpParser.FooBazContext ctx => this.VisitFooBaz(ctx),
    _ => HandleUnexpectedAlternative<IStatement>(context, "foo"),
};
```

Non-override dispatch helpers are often **non-virtual** methods named `VisitFoo` that switch on subclasses, with a `VisitFooAlt` virtual fallback for extensibility.

### GrammarAddon extension points

Php grammar leaves hooks as stub rules matching `T_NO_GRAMMAR_ADDON_0000` (via `noGrammarAddon`). TyhpParser **overrides** those rules with real syntax gated by `{this.isLanguageMode("tyhp")}?`.

Two related concepts:

1. **Parse-tree GrammarAddon rules** — e.g. `functionNameGrammarAddon`, `classStatementGrammarAddon`. PHP base visitors either return `null` or report `VisitorUnexpectedAlternative` / `VisitorUnsupportedConstruct`. Tyhp overrides fill them in.
2. **AST `GrammarAddons` dictionary** — `Base2Ast` / `WithGrammarAddon` / `AddGrammarAddon` store extra child nodes under string keys (`"identifier"`, `"isOverloadSignature"`, `"GenericArguments"`, `"isAsync"`, …) for the binder/emitter without changing core AST shapes.

Handler vs addon naming in generated trees:

- `…GrammarAddonHandler` — labeled alternative that wraps the addon rule
- `…GrammarAddon` — the overrideable rule itself

Tyhp often overrides the **Handler** (e.g. `VisitReturnTypeGrammarAddonHandler`) when the base Handler calls a non-virtual addon method that always errors.

---

## 4. Php vs Tyhp vs tyhpdef paths

### File → parser entry → visitor root

| Extension / mode | Recognizer | Parser rule | Visitor | Result type |
|------------------|------------|-------------|--------|-------------|
| `.php` (default) | `TyhpParser` | `phpSrcFile` | `TyhpParserAstVisitor.VisitPhpSrcFile` | `PhpSrcFileAst` |
| `.tyhp` tagged | `TyhpParser` | `tyhpSrcFile` → `#tyhpFile` | `TyhpParserAstVisitor.VisitTyhpFile` | `TyhpSrcFileAst` |
| `.tyhp` tagless | `TyhpParser` | `tyhpTaglessSrcFile` → `#tyhpTaglessFile` | `TyhpParserAstVisitor.VisitTyhpTaglessFile` | `TyhpSrcFileAst` |
| `.tyhpdef` tagged | `TyhpdefParser` | `tyhpdefSrcFile` → `#tyhpdefFile` | `TyhpdefParserAstVisitor.VisitTyhpdefFile` | `TyhpdefSrcFileAst` |
| `.tyhpdef` tagless | `TyhpdefParser` | `tyhpdefTaglessSrcFile` → `#tyhpdefTaglessFile` | `TyhpdefParserAstVisitor.VisitTyhpdefTaglessFile` | `TyhpdefSrcFileAst` |

All roots inherit `SrcFileAst`.

### Language mode strings on AST nodes

Two methods walk parents. They key off context *types*, not the parser’s `_languageMode` field.

`PhpParserAstVisitor.GetCurrentLanguageMode` (used while walking a `TyhpParser` tree, and it also recognizes `TyhpdefParser` contexts):

| Context found | String |
|---------------|--------|
| `TyhpParser.TyhpdefBlockContext` or `TyhpdefParser.TyhpdefBlockContext` | `"tyhpdef"` |
| `TyhpParser.TyhpdefTaglessFileContext` or `TyhpdefParser.TyhpdefTaglessFileContext` | `"tyhpdef"` |
| `TyhpParser.TyhpBlockContext` | `"tyhp"` |
| `TyhpParser.TyhpTaglessFileContext` | `"tyhp"` |
| `TyhpParser.PhpBlockContext` | `"php"` |
| `TyhpParser.PhpSrcFileContext`, `TyhpParser.TyhpSrcFileContext`, `TyhpParser.TyhpdefSrcFileContext`, `TyhpdefParser.TyhpdefSrcFileContext`, or null | `""` |

`TyhpdefIncludedPhpVisits.GetCurrentLanguageMode` is what a `.tyhpdef` walk calls (the emit rewrites `PhpParserAstVisitor.GetCurrentLanguageMode` to this method):

| Context found | String |
|---------------|--------|
| `TyhpdefParser.TyhpdefBlockContext` or `TyhpdefParser.TyhpdefTaglessFileContext` | `"tyhpdef"` |
| `TyhpdefParser.TyhpdefSrcFileContext` or null | `""` |

`Base2Ast.SetContext` uses the visitor’s result when constructing nodes so binder/checker can distinguish modes.

### Parser semantic predicates vs AST language mode (important)

`tyhpBlock`, `tyhpTaglessSrcFile`, `tyhpdefBlock`, and `tyhpdefTaglessSrcFile` assign **parser** `_languageMode = "tyhp"`. That makes `{this.isLanguageMode("tyhp")}?` succeed inside tyhpdef files so shared Tyhp GrammarAddon rules apply. Lexer `_languageMode` on `<?tyhpdef` is `"tyhpdef"`.

AST `LanguageMode` for nodes under a tyhpdef block or tagless file is `"tyhpdef"`, from the tables above. The file-root context (`TyhpdefSrcFileContext`) returns `""`. Predicates see `"tyhp"`; the recorded mode under the block is `"tyhpdef"`.

### What each layer owns

**Php (`PhpParserAstVisitor.*`)** — full PHP surface: expressions/precedence, statements, top statements, objects, dereferenceables, types, attributes, parameters, try/catch, functions, root/inline HTML. In Tyhp / tyhpdef mode, `VisitParameter` rejects postfix `T...` glued to a value-parameter type (`int... $x`) — that spelling is a callable-shape parameter (`callable(int ...): R`); PHP variadic remains `int ...$x`.

**Tyhp (`TyhpParserAstVisitor.Tyhp*.cs`)** — overrides GrammarAddons and adds Tyhp-only rules: generics, anonymous `new struct {…}`, type-position `struct { … }` shapes, callable shapes (`callable(…): R`), grouped `(typeExpr)`, extensions, typed vars, using blocks, operator overloads, type aliases, return type guards, compile-time builtins (`typeof` / `nameof` / `default` / `variable_exists`), `await`/`with`/`is` expression tokens, async modifiers, required parameter types in Tyhp mode.

**Tyhpdef (`TyhpdefParserAstVisitor`, emitted `Included.cs` plus `Helpers.cs`)** — walks `TyhpdefParser` trees: top statements (`tyhpdefTopStatement*`), import-shaped declarations (classes/traits/interfaces/enums/functions/consts/variables), deprecated/obsolete markers, name-only `extern class` / `extern interface` / `extern enum` / `extern function` / `extern const` / kind-unspecified `extern \Name;` placeholders (`VisitTyhpdefExternClassDecl` / `VisitTyhpdefExternInterfaceDecl` / `VisitTyhpdefExternEnumDecl` / `VisitTyhpdefExternFunctionDecl` / `VisitTyhpdefExternConstDecl` / `VisitTyhpdefExternDecl` → `VisitTyhpdefExtern*DeclarationStatement`; identifier via `VisitName`; `IsExtern` + optional `@provided-by`; DeclType `"extern"` for the bare form), name-only overlay `partial function` (`VisitTyhpdefPartialFunctionDecl` / `VisitTyhpdefPartialClassMethod`; `IsPartial`, no params/return), inline extension decls in tyhpdef, specialized error helpers (`ReportMissingRequired`, `CreateErrorImportObjectDecl`, `HandleUnexpectedAlternativeSpecial`). `#tyhpdefClassPropertyAccessors` is dispatched from `VisitTyhpdefClassStatement` and maps onto the same `PhpPropertyAst.Hooks` path as PHP `#classPropertyAccessors`: one hooked `PhpPropertyAst` whose hooks are bodyless `PhpPropertyHookAst` nodes (`body: null`, like PHP interface `get;`), with `ReturnsRef` / modifiers / attributes attached the same way. Unhooked `#tyhpdefClassProperty` still passes `hooks: null`. Optional `?? expr` on `tyhpdefProperty` / `tyhpdefHookedProperty` is stored as `TyhpdefPropertyAst.CoalesceExpr` and copied onto `PhpPropertyAst.DefaultValue` (same child slot PHP `= expr` uses), so binder/IDE see the expected start value without treating tyhpdef as defining it. The same visit text is also emitted onto `TyhpParserAstVisitor` for the tyhpdef rules `TyhpParser` still contains; the `.tyhpdef` file walk does not use that copy. Doc comments on this walk read `TyhpdefLexer.DocBlockCommentsChannel`. Token switches use `TyhpdefParser.T_*`.

---

## 5. Conventions and patterns

### Naming

- Override generated methods: `VisitXxx([NotNull] TyhpParser.XxxContext context)` on the PHP/Tyhp visitor. The emitted tyhpdef copy uses `TyhpdefParser.XxxContext`. Write the `.inc` against `TyhpParser`; regen renames it.
- Dispatch helpers (non-override): same name without always matching ANTLR’s virtual set; return interface types.
- Fallbacks: `VisitXxxAlt` / `VisitXxxAlternative` for unknown subclasses.
- Error helpers: `IsErrorRecoveryContext`, `ReportUnexpectedAlternative`, `HandleUnexpectedAlternative<T>`, `HandleUnexpectedAlternativeSpecial`, `HandleFailedCast`, `HandleWithStatementTerminal`.

### Prefer diagnostics + error AST over throws

Story 01 migrated most `throw` paths to:

```csharp
ReportUnexpectedAlternative(context, ruleName);
return ErrorAst.Create(context, GetCurrentLanguageMode(context));
// or typed CreateError(...)
```

`ReportUnexpectedAlternative` (and the `HandleUnexpectedAlternative*` helpers that call it)
suppresses `VisitorUnexpectedAlternative` when `IsErrorRecoveryContext(context)` is true —
i.e. the rule's `exception` is set or a direct child is an `IErrorNode`. Those stubs are
ANTLR recovery artifacts; the parser already emitted the real syntax diagnostic (TYHP1002).
Walking them and reporting TYHP2002 would leak internal context class names (e.g.
`StatementRequiringTerminalContext`, `MemberNameContext`) and inflate the error count.

`VisitStatementRequiringTerminal` / `VisitStatementWithoutTerminal` also short-circuit to
`ErrorAst` for recovery stubs before the labeled-alt switch, so bare base contexts never
fall through to the unexpected-alternative default. Alt fallbacks (and equivalent inline
fallthroughs) across the PHP visitor partials — including `memberName` /
`memberConstantName` / `memberInstanceName`, type/identifier/object/block/statement/top-
statement Alts, and GrammarAddon stubs that historically used `VisitorUnexpectedAlternative`
— route through `ReportUnexpectedAlternative` for the same reason. Chained malformed access
like `$x->y->;` or a truncated trait adaptation like `use T { Foo::bar as ; }` must not emit
a duplicate TYHP2002 naming an internal ANTLR context class.

`DiagnosticBag` de-duplicates identical diagnostics (same severity, code, file, span, and
format params), so double-reported TYHP1002 from recovery also collapses to one finding.

Some paths still throw `InvalidOperationException` (e.g. certain tyhpdef / using-resource switches, failed generic casts). Treat remaining throws as intentional hard failures, not the default style.

Message codes used here:

- `VisitorUnexpectedAlternative` (2002) — unknown labeled alternative / cast failure (not for recovery stubs)
- `VisitorUnsupportedConstruct` (2004) — GrammarAddon hit in PHP base without Tyhp override
- `VisitorMissingRequiredNode` (2003) — required child left null after truncation / recovery

PHP `TraitName { addPaths as private; }` has no `traitAliasNameGrammarAddon` child. `VisitTraitAliasVisibility` / `VisitTraitAliasRename` visit that addon only when it is present.

### Doc comments

`FindPossibleDocComment(IToken beforeToken)` walks `_tokens` backward on `TyhpLexer.DocBlockCommentsChannel` from the token before a declaration (often a labeled `FindDocComment=` token such as `(` or `{`).

`FindPossibleOverlayAgainst` uses the same backward walk on `SimpleCommentsChannel` for `// @overlay-against:` immediately preceding a tyhpdef declaration. The compact stamp is stored on the AST as grammar addon `overlayAgainst`. Top-level attributes live on `tyhpdefAttributedTopStatement`, not on the inner function/class rule, so a stamp written before `#[…]` is not visible from the keyword; `VisitTyhpdefAttributedTopStatement` retries the walk from the attribute start when the inner declaration has no stamp yet (canonical stamp-after-attribute still wins). Class-member attributes are on the member rule, so `context.Start` is the `#[` token and that backward walk misses a stamp between the attribute and the keyword. `AttachOverlayAgainst` reads that gap when the rule's first child is `attributes`, and uses it ahead of a stamp that precedes the attribute.

`FindPossibleProvidedBy` uses the same channel walk for `// @provided-by:` immediately preceding an `extern` type, function, or const. Unknown `@` tags are skipped; the nearest matching comment wins (trimmed package name). Empty remainder is treated as absent. Stored as grammar addon `providedBy` on the declaration AST (exposed as `ProvidedBy`).

**Order matters:** look up the declaration’s docblock **before** visiting children. Visiting nested declarations advances `_docCommentLastStop` and can steal the parent’s docblock. `ResetDocComment` exists to reposition the cursor when needed.

Absence is `null`, not `""`, so serialization treats “no docblock” like “never had one.”

### `CurrentTopStatementList`

Set when entering php/tyhp blocks and echo blocks. Used to hoist declarations that appear in expression position (notably anonymous structs via `VisitTyhpNewAnonStructInstance`). Inline output visitors save/restore the previous list to avoid clobbering.

### Statement terminals

`HandleWithStatementTerminal` appends an inline-output / close-tag terminal as a sibling in a `PhpStatementBlockAst` or `PhpTopStatementListAst` when present; otherwise returns the statement unchanged.

### Desugaring at visit time

Some Tyhp constructs are lowered into PHP-shaped AST immediately:

| Source sugar | Visit-time shape |
|--------------|------------------|
| `fn name(...) => expr;` (named short function) | `PhpFunctionDeclAst` body = `return expr;` — **no** `isOverloadSignature` addon; `IsShortSyntax` flag set |
| `function name(...): T;` overload signature | Bodyless `PhpFunctionDeclAst` + `isOverloadSignature` addon |
| Operator / extension `=> expr` bodies | Same `return expr;` wrapping; `IsShortSyntax` on `TyhpOperatorOverloadAst` / `PhpMethodDeclAst` / `PhpFunctionDeclAst` |
| Anonymous `new struct {…}` | Struct decl added to `CurrentTopStatementList`; expression is `new GeneratedName` |

Do not confuse named short functions with anonymous PHP arrows (`fn($x) => …`), which use the inline-function expression path.

### GrammarAddon string keys (non-exhaustive, from visitor code)

| Key | Typical payload | Where set |
|-----|-----------------|-----------|
| `identifier` | Generics on names / methods | Functions, objects, tyhpdef methods |
| `modifiers` | Extra modifier lists | Functions, traits, enums |
| `parameters` | Extension `extends` marker on first param | Functions |
| `isOverloadSignature` | Semicolon token | Overload signatures |
| `genericTypeArguments` | Call-site type args | `VisitCallArgumentList` (Tyhp) |
| `GenericArguments` / `GenericParameters` | Type arg/param lists | Imports, names, tyhpdef |
| `isAsync` | `async` token | Member modifiers, tyhpdef methods |
| `isInternal` | `internal` token | Member modifiers, class/trait/interface/enum modifiers, top-level `internal type` / `internal const`, extensions, operator overloads |
| `ctorReturnType` | `TyhpCtorReturnTypeAst` (absent when the ctor return type is omitted) | Tyhp ctors |
| `deprecatedOrObsolete` | Token | Tyhpdef members |
| `typeExpr` / `typeName` | Type fragments | Type visitors |
| `aliasOf` / `aliasedAs` | Alias relationships | Tyhpdef identifiers |

Binder code (outside this folder) reads these keys; changing key names is a cross-layer break.

### Virtual GrammarAddon methods intended for override

PHP marks many addon visitors `virtual` and erroring so Tyhp can override, including:

- `VisitFunctionDeclarationStatementGrammarAddon`
- `VisitClassStatementGrammarAddon`, `VisitTraitAliasGrammarAddon`
- `VisitNewDereferenceableGrammarAddon`
- `VisitStatementWithoutTerminalGrammarAddon`, `VisitStatementRequiringTerminalGrammarAddon`
- `VisitInternalFunctionsGrammarAddon`
- Various `Visit*NameGrammarAddon` returning `null` in PHP

When adding a new Tyhp syntax hook: extend the GrammarAddon rule in `TyhpParser.g4`, then override the matching virtual method on `TyhpParserAstVisitor`.

---

## 6. Helpers (cheat sheet)

| Helper | Location | Purpose |
|--------|----------|---------|
| `GetCurrentLanguageMode` | `PhpParserAstVisitor.cs` | Walk parents → `"php"` / `"tyhp"` / `"tyhpdef"` / `""` |
| `FindPossibleDocComment` / `ResetDocComment` | same | Docblock channel scan |
| `GetTokenValueAst` | same | Token → `TokenValueAst` (null token → null / optional GrammarAddon fallback) |
| `HandleWithStatementTerminal` | same | Attach statement terminal sibling |
| `HandleUnexpectedAlternative<T>` | `PhpStatements.cs` | Diagnostic + `ErrorAst` cast to `T` |
| `HandleUnexpectedAlternativeSpecial` | `TyhpdefParserAstVisitor.Helpers.cs` | Diagnostic + custom error factory (also used from TyhpGenerics/Objects) |
| `HandleFailedCast` | `PhpTopStatements.cs` | When visit result isn’t `ITopStatement` |
| `WithGrammarAddon` / `WithAttributes` | `Ast/Base2AstExtensions.cs` | Fluent AST decoration |
| `ReportMissingRequired` / `CreateErrorImportObjectDecl` / `CreateErrorParameter` | `TyhpdefParserAstVisitor.Helpers.cs` (and the PHP-side copies the emit places on `TyhpdefIncludedPhpVisits`) | Tyhpdef recovery (also used by type-alias / extension / struct / import / generics / typed-var visitors) |
| `VisitClassStatementListOrEmpty` / `CreateObjectTypeToken` | `PhpObjects.cs` | Object-type decl recovery (null `StatementList` / `ObjectType`) |
| Null-guarded `VisitTyhpTypeAlias` / extension / struct decls | `TyhpTypeAliases.cs`, `TyhpExtensions.cs`, `TyhpStructs.cs` | Truncated `type`/`extension`/`struct` recovery (placeholders + `VisitorMissingRequiredNode`) |
| Null-guarded Tyhp declaration recovery | `TyhpTopStatements.cs`, `TyhpFunctions.cs`, `TyhpGenerics.cs`, `TyhpStatements.cs`, `TyhpDereferenceables.cs`, `TyhpObjects.cs`, emitted tyhpdef visits, `PhpParametersAndArguments.cs` | Truncated `use extension` / overloads / generics / typed-var / anon-struct / class-body operator overload (`VisitTyhpClassOperatorOverloadDecl`) / tyhpdef class-member sites report `VisitorMissingRequiredNode` instead of TYHP1003 |

---

## 7. File map

### `PhpParserAstVisitor`

| File | Responsibility |
|------|----------------|
| `PhpParserAstVisitor.cs` | State, doc comments, `GetCurrentLanguageMode`, VisitChildren shutdown |
| `PhpParserAstVisitor.Included.cs` | Emitted shared PHP visits. Edit `shared/PhpParserAstVisitor/*.inc` |
| `PhpRoot.cs` | `phpSrcFile`, code blocks, php/echo blocks, inline output |
| `PhpTopStatements.cs` | Top statement lists, namespaces, uses, const, halt_compiler, GrammarAddon handler stub |
| `PhpStatements.cs` | Inner/top statements, control flow wrappers, internal functions dispatch |
| `PhpBlocks.cs` | if/for/foreach/while/switch/match/declare |
| `PhpExpressions.cs` | Precedence ladder (`VisitPhpExprPrec`), ops, include/require, match expr, base expr |
| `PhpDereferenceables.cs` | Variables, members, calls, `new`, scalars, arrays, encaps strings |
| `PhpObjects.cs` | class/trait/interface/enum, members, properties/hooks, trait adaptations |
| `PhpFunctions.cs` | Function decls, inline functions, GrammarAddon stubs |
| `PhpParametersAndArguments.cs` | Parameters, ctor params, arguments, global/static vars |
| `PhpTypes.cs` | Type expressions, return types, GrammarAddon stubs. Emitted `VisitTypeExpr` / `VisitTypeExprWithoutStatic` keep `?T` as a nullable simple type, and promote a `?` prefix on a grouped union or intersection to that compound type with `IsNullable` set |
| `PhpIdentifiers.cs` | Names, namespaces, reserved/semi-reserved, class name refs |
| `PhpAttributes.cs` | Attributes + attributed declaration dispatch |
| `PhpTryCatchBlocks.cs` | try/catch/finally |
| `Unsorted.cs` | Empty |

### `TyhpParserAstVisitor`

| File | Responsibility |
|------|----------------|
| `TyhpParserAstVisitor.cs` | Ctor only |
| `TyhpParserAstVisitor.Included.cs` | Emitted shared Tyhp and tyhpdef visits for `TyhpParser` contexts. Edit `shared/TyhpParserAstVisitor/*.inc` |
| `TyhpdefUnreached.cs` | `VisitTyhpdefClassConstDecl` / `VisitTyhpdefClassConstList` (`TyhpParser` only) |
| `TyhpRoot.cs` | `tyhpFile` / tagless / blocks / inline output |
| `TyhpTopStatements.cs` | Top GrammarAddon: type alias, extension, `use extension`, `global use` / `global use extension`, generic use aliases |
| `TyhpStatements.cs` | Typed var expr, using blocks |
| `TyhpFunctions.cs` | Overloads, async/generics/extends GrammarAddons, call-site generics |
| `TyhpObjects.cs` | Generic type names, Tyhp methods/ctors, type aliases, operator overloads, trait property rename, postfix `hide`, operator method-refs (`traitMethodReferenceGrammarAddon` / `absoluteTraitMethodReferenceGrammarAddon`), async modifiers |
| `TyhpStructs.cs` | Anonymous `new struct {…}` + type-position `tyhpStructShape` + properties (string or integer array-key aliases: `'key' as $name` / `0 as $name`) |
| `TyhpExtensions.cs` | Extension decls (header target, nested groups), extension operators, `&$this`, TYHP4361 on the per-member target spellings |
| `TyhpGenerics.cs` | Generic identifiers, type params/args (`tyhpGenericTypeArgument` is `typeExpr` with optional postfix `T_ELLIPSIS` → `TyhpPostfixEllipsisTypeAst`, or bare `T_ELLIPSIS` → `TyhpEllipsisTypeAst`) |
| `TyhpIdentifiers.cs` | Tyhp reserved words, generic namespace/type/member name addons |
| `TyhpTypes.cs` | Tyhp scalar / template string types via type GrammarAddon; callable shapes (`VisitCallableType`); grouped `(typeExpr)`. Unnamed `callable(int): R` is a PHP `T_INT_CAST` (and the other builtin-cast tokens); the visitor maps that token to a single unnamed `PhpBuiltinTypeAst` parameter, the same mapping `typeof(int)` / `default(int)` use. |
| `TyhpTypeAliases.cs` | `type` alias declarations. Bare `object { … }` becomes `TyhpObjectShapeAst` (members retained). PHP class-const spelling inside the shape (`const NAME = expr` / typed `const T NAME = expr`) is `PhpConstDeclListAst`; other members stay tyhpdef class statements. An alias-RHS intersection (`LoggerInterface & object { … }`) is `PhpTypeKind.Intersection` whose items are nominal types plus `TyhpObjectShapeAst` — not builtin `object`. |
| `TyhpExpressions.cs` | Unary pre/post GrammarAddons (`await`, decimal cast), `tyhpWithList` |
| `TyhpDereferenceables.cs` | `new struct {…}` |
| `TyhpReturnTypes.cs` | `: $x is T` / `: $array[$key] is T` return type guards |
| `TyhpInternalFunctions.cs` | `variable_exists` / `typeof` / `default` / `nameof`. `typeof` and `default` take `typeExpr` (`VisitTypeExpr`). Both also have a builtin-cast alternative (`typeof(int)` / `default(int)`) because the PHP lexer tokenizes `(int)` as `T_INT_CAST`. |
The `.tyhpdef` walk is not a `TyhpParserAstVisitor` partial:

| File | Responsibility |
|------|----------------|
| `TyhpdefIncludedPhpVisits.cs` | State, `FindPossibleDocComment` (`TyhpdefLexer.DocBlockCommentsChannel`), `GetCurrentLanguageMode` |
| `TyhpdefIncludedPhpVisits.Included.cs` | Emitted PHP closure visits (`TyhpdefParser` contexts) |
| `TyhpdefIncludedPhpVisits.Helpers.cs` | `IsInTyhpOrTyhpdefSource`, `ReportMissingRequired`, modifier `FromToken` via `TyhpdefParser.T_*` |
| `TyhpdefParserAstVisitor.cs` | Ctor |
| `TyhpdefParserAstVisitor.Included.cs` | Emitted tyhpdef and Tyhp visits: file/block/statements, `global use`, `partial` / `omit`, name-only overlay `partial function`, name-only `extern`, standalone `extension { }`, class-body thin `extension fn` / `extension operator`, FQN import names |
| `TyhpdefParserAstVisitor.Helpers.cs` | Extension-member and tyhpdef recovery helpers |

`.tyhp` object shapes still call `VisitTyhpdefClassStatement` on `TyhpParserAstVisitor` (the emitted copy). That rule remains on `TyhpParser` because the shape grammar names it.

---

## 8. Weirdness and WHY

### Dual language-mode story for tyhpdef

`tyhpdefBlock` and `tyhpdefTaglessSrcFile` assign parser `_languageMode = "tyhp"` so Tyhp GrammarAddon predicates fire. Lexer `_languageMode` on `<?tyhpdef` is `"tyhpdef"`. `PhpParserAstVisitor.GetCurrentLanguageMode` returns `"tyhpdef"` for `TyhpdefParser.TyhpdefBlockContext` and `TyhpdefParser.TyhpdefTaglessFileContext`, and `""` for `TyhpdefParser.TyhpdefSrcFileContext`. `TyhpdefIncludedPhpVisits.GetCurrentLanguageMode` returns those same strings for those same contexts. Conflating the parser field with the visitor result breaks predicates or binder mode checks.

### Yield expression vs unary `yield from` / bare `yield;`

`phpExprYieldValue` is `T_YIELD ( (KeyValue=phpExprPrec T_DOUBLE_ARROW)? R=phpExprPrec )?`. `VisitPhpExprYieldValue` builds `PhpYieldAst` with `KeyExpr` (null when the `=>` clause is absent) and `ValueExpr` from `R` when an operand is present. Valueless `yield` (`$x = yield;` and statement `yield;` parsed as a top expression) is prefix `PhpUnaryOpAst` with a null operand — same shape as `VisitInnerStatementYield`. A unary `yield` with only the value operand would drop the key, so keyed `yield $k => $v` would be unrecoverable for checker/inferrer/emitter.

`yield from $expr` stays prefix `PhpUnaryOpAst` (`VisitPhpExprYieldFrom`, operator `T_YIELD_FROM`). Bare `yield;` as `innerStatementYield` is also unary, with a null operand.

Tyhp `is` (`#phpExprBinaryOpGrammarAddon002Handler`) accepts an optional `?` before the RHS (`$x is ?T`). `VisitPhpExprBinaryOpGrammarAddon002Handler` wraps that RHS in a prefix `PhpUnaryOpAst` whose operator is `?`, so the nullability is not dropped. `$x is T` is unchanged (plain expression RHS). `$x is T ? a : b` still parses as ternary of `is`, because `?` is not immediately after `is`.

### VisitChildren permanently off

Default ANTLR visitation would produce wrong trees (and null aggregates). The visitor is a hand-written recursive descent over the parse tree. New overrides must visit every needed child themselves.

### GrammarAddon as the extension mechanism

Rather than forking every PHP rule, PhpParser leaves `T_NO_GRAMMAR_ADDON_0000` stubs; TyhpParser replaces those rules. PHP visitors error if an addon somehow matches without a Tyhp override. This keeps PHP grammar readable and Tyhp deltas localized.

### Short function vs overload signature

Both go through `functionDeclarationStatementGrammarAddon`, but only the bodyless `function …;` form gets `isOverloadSignature`. The short `fn name =>` form is desugared to a normal body so the binder does not skip it as a signature.

### Anonymous struct registration

`VisitTyhpNewAnonStructInstance` mutates `CurrentTopStatementList`. If that list is null (visitor bug or odd recovery), the decl is dropped from the file’s top level while the `new` expression still references a generated name — a silent structural hole.

### `default(int)` cast tokens

`VisitTyhpInternalFunctionDefaultBuiltinCast` exists because the PHP lexer emits `(int)` as a single cast token, which cannot match `typeExpr`. The visitor maps cast token types to builtin type names for the emitter.

### `VisitReturnTypeGrammarAddon` non-virtual trap

PHP’s Handler calls a non-virtual addon visitor that always errors. Tyhp must override **`VisitReturnTypeGrammarAddonHandler`**, not only the addon method. Comments in `TyhpReturnTypes.cs` document this.

### Empty `Unsorted.cs`

Placeholder left over from splitting; safe to ignore unless someone starts dumping methods there again.

### Commented `TyhpCompiler`

Not part of the live pipeline; do not use as an API reference.

---

## 9. Interactions with Parser and Ast

### Parser / Grammar

- Grammars: `TyhpParser.g4` (`import PhpParser`) for `.php` / `.tyhp`; `TyhpdefParser.g4` (includes `shared/`) for `.tyhpdef`.
- Generated contracts: `TyhpParserVisitor.cs` / `TyhpParserBaseVisitor.cs` and `TyhpdefParserVisitor.cs` / `TyhpdefParserBaseVisitor.cs`.
- After grammar changes: `./compile_grammar.sh`, which also re-emits `*.Included.cs` from `Visitor/shared/`. A visit that both parsers share is edited in the `.inc`, not in `Included.cs`. Labeled alternative renames break switch patterns.
- Semantic predicates (`isLanguageMode`) are evaluated during parse, not visit. Parser `_languageMode` inside a tyhpdef file is `"tyhp"`. Wrong mode at parse time means the alternative never appears in the tree.
- Token integers in a shared `.inc` are written as `TyhpParser.T_*` / `TyhpLexer.*`. The tyhpdef emit renames those to `TyhpdefParser` / `TyhpdefLexer`. Do not paste a `TyhpLexer.tokens` integer into tyhpdef visit code.

### Ast

- Nodes live under `Tyhp/TyhpLang/Ast/`; interfaces under `Ast/Interfaces/`.
- Visitors should not bind symbols (`BoundSymbol` / `OwningFile` are binder-owned).
- Prefer existing `Create` / `CreateError` factories; they set span and language mode consistently.
- GrammarAddons are part of the serialized AST story (`Base2Ast`); keys must stay stable for cache compatibility.

### Downstream consumers

Binder/checker/emitter assume visitor shapes (e.g. overload signatures, generic addons, tyhpdef import decls). Visitor changes that alter addon keys or desugaring usually need coordinated binder/emitter updates — those live outside this folder.

---

## 10. Pitfalls

1. **Instantiating `PhpParserAstVisitor` for `.php` / `.tyhp`** — Tyhp GrammarAddons will error; use `TyhpParserAstVisitor`. A `.tyhpdef` tree is a `TyhpdefParser` tree; walk it with `TyhpdefParserAstVisitor`.
2. **Forgetting explicit child visits** — `VisitChildren` is a no-op; missing calls drop AST structure silently (or yield null children).
3. **Doc comment order** — visit docblock before children.
4. **Clobbering `CurrentTopStatementList`** — save/restore around nested inline output (see Php/Tyhp root visitors).
5. **Assuming `_languageMode ==` AST `LanguageMode`** — false for tyhpdef.
6. **Changing GrammarAddon keys** without binder updates.
7. **Marking overload implementations with `isOverloadSignature`** — binder will skip them.
8. **Relying on `Visit(context)` for unlabeled recovery trees** — prefer typed switches + `CreateError`.
9. **Null children after ANTLR recovery** — many visitors null-guard; new code should too
   (`VisitTyhpdefFile` already guards null `TyhpdefBlock`; object-type decls use
   `GetTokenValueAst` with a nullable token, `VisitClassStatementListOrEmpty`, and
   null-checked `Extends`/`Implements` call sites so reserved keywords as type names
   yield parse diagnostics instead of `NullReferenceException` / TYHP1003). The same
   pattern applies to truncated Tyhp decls: `VisitTyhpTypeAlias`,
   `VisitTyhpExtensionDeclarationStatement`, struct declaration visitors, and the broader
   declaration-site set (`VisitTyhpImportExtension` / tyhpdef siblings, function overload
   GrammarAddons, generic parameter/argument lists, typed-var / anon-struct, class-body and
   tyhpdef operator overload builders (`VisitTyhpClassOperatorOverloadDecl` /
   `VisitTyhpdefClassOperatorDecl`; operands via `VisitAttributedParameter` so
   `#[…]` copies onto `PhpParameterAst`), tyhpdef class const / trait-use / extension
   function+operator builders, plus `VisitParameter` when `Variable` is missing) report
   `VisitorMissingRequiredNode` and build placeholders when
   required trailing children are null after recovery. Type-expression fallthroughs follow
   the same rule: `VisitTypeExpr` / `VisitTypeExprWithoutStatic` / `VisitTypeWithoutStatic`
   must not call GrammarAddon visitors with a null child (use `ReportUnexpectedAlternative`
   + `CreateError` / `ErrorAst`). Expression recovery must also tolerate null
   `phpExprPrec` children — `VisitPhpExprPrec` / `VisitPhpExprPrecAlt` and
   `VisitPhpExprAmpersand` guard null `Op`/`R` because Antlr's `Visit(null)` throws NRE
   on this runtime (truncated `|` / `&` return-type slots). Same rule for an optional
   sub-rule that ANTLR still enters on recovery: `VisitTyhpCtorReturnType` (`ReturnType=
   tyhpCtorReturnType?`) returns null instead of building `TyhpCtorReturnTypeAst` when
   `context.TokenValue` is null (an invalid token after the ctor's `:`, e.g. `: int`,
   still enters the sub-rule without matching `T_TYHP_VOID`/`T_TYHP_PARENT`); the caller
   (`CreateTyhpClassCtor`) treats that null the same as an omitted annotation.
10. **Caching error trees** — CompilationService refuses; don’t bypass that for “faster” reparse of broken files.
11. **Entry rule order** — check `.tyhpdef` before `.tyhp`.
12. **Adding Tyhp syntax only in the visitor** — without a GrammarAddon / rule override, the parse never produces the context type.

---

## 11. Open questions

Items not fully settled from source alone; verify before relying on them:

1. **Is parser `_languageMode = "tyhp"` on tyhpdef blocks documented elsewhere as a long-term contract**, or an accidental coupling that should become `isLanguageMode("tyhp") || isLanguageMode("tyhpdef")`?
2. **`tyhpdefTaglessSrcFile` sets `_languageMode = "tyhp"`** in the grammar action — same dual-mode story; intentional for predicates, but is there any path where AST mode should be empty until the first statement?
3. **Remaining `InvalidOperationException` throws** in using-resource / tyhpdef switches — should they migrate to `HandleUnexpectedAlternativeSpecial` for consistency with Story 01?
4. **`PhpParserAstVisitor.Unsorted.cs`** — keep forever, or delete?
5. **Anonymous class vs anonymous struct** — anon classes use `PhpNewAst.CreateAnonymous`; structs hoist a decl. Is there a plan to unify registration?
6. **`VisitPhpExprPrecBaseGrammarAddon`** — Tyhp overrides this for `async { ... }` (`TyhpAsyncBlockAst`). PHP mode still has no addon.
7. **Coverage of every GrammarAddon stub** — this guide lists patterns; a mechanical audit of all `*GrammarAddon` rules vs Tyhp overrides was not fully enumerated line-by-line. When adding syntax, grep both grammars and both visitor hierarchies.
8. **Thread safety** — visitors are per-file and not shared across threads in `CompilationService`, but nothing in the visitor itself documents that invariant; confirm before reusing a visitor instance.

---

## 12. Quick “where do I edit?” guide

| Task | Start here |
|------|------------|
| New PHP construct | `PhpParser.g4` + matching `PhpParserAstVisitor.Php*.cs` |
| New Tyhp construct on PHP scaffold | Override GrammarAddon in `TyhpParser.g4` + `TyhpParserAstVisitor.Tyhp*.cs` |
| New tyhpdef construct | `shared/Tyhpdef.rules.g4` (included by `TyhpdefParser`, and by `TyhpParser` for the file entries and the class-body rules `.tyhp` shapes reach) + a `Visitor/shared/TyhpParserAstVisitor/*.inc` whose first line is `// #class TyhpParserAstVisitor`. Regen emits that text into `TyhpParserAstVisitor` and, with `TyhpParser`/`TyhpLexer` renamed, into `TyhpdefParserAstVisitor` |
| Standalone tyhpdef `extension` member attributes | `VisitTyhpdefStandaloneExtensionFunctionDecl` / `VisitTyhpdefStandaloneExtensionOperatorDecl` — copy `Attributes` onto the wrapper via `AddAttributes` |
| Tyhp `extension { }` member attributes | `VisitTyhpExtensionMemberAsExtensionMember` — callable, operator, and target-group alts. Function / `fn` visitors copy `Attributes` via `WithAttributes`; names use `VisitTyhpOptionalGenericIdentifierWithoutConstructor`. `&$this` is the `byRefReceiver` addon. Header / group targets use `AttachExtensionBlockTarget`. |
| Operator-overload operand `#[…]` | `VisitAttributedParameter` from class / extension / tyhpdef operator visitors (`TyhpObjects.cs`, `TyhpExtensions.cs`, emitted tyhpdef visits) — same as method parameters |
| New expression operator | Expression GrammarAddon in `TyhpParser.g4`; token visitor in `TyhpExpressions.cs` / PhpExpressions handlers |
| Generics plumbing | `TyhpGenerics.cs` + name GrammarAddons in `TyhpIdentifiers.cs` / `TyhpObjects.cs` / `TyhpFunctions.cs` |
| Doc comment bugs | `FindPossibleDocComment` call order at the declaration site |
| Wrong language mode on nodes | `PhpParserAstVisitor.GetCurrentLanguageMode` or `TyhpdefIncludedPhpVisits.GetCurrentLanguageMode`, plus grammar `_languageMode` actions (`"tyhp"` on `tyhpdefBlock` / `tyhpdefTaglessSrcFile`) |
| Parse succeeds, AST wrong shape | Explicit Visit* children; check desugar / GrammarAddon keys |

---

*Grounded in the Visitor sources, `CompilationService.ParseFile`, `Binder/BuiltIn/Tyhpdef.cs`, `Base2Ast` / `Base2AstExtensions`, and `Tyhp/TyhpLang/Grammar/{Tyhp,Tyhpdef,Php}Parser.g4` as of the guide’s authoring. Prefer the code when this document and the repo diverge.*
