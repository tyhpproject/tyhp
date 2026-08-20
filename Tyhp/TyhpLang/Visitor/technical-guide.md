# Parser → AST Visitors — Technical Guide

Developer guide for the parse-tree visitors under `Tyhp/TyhpLang/Visitor/`. These classes turn ANTLR `ParserRuleContext` trees into `Base2Ast`-derived nodes used by the binder, checker, and emitter.

**Scope:** the 32 C# files in this folder (`PhpParserAstVisitor.*`, `TyhpParserAstVisitor.*`). Grammar sources live in `Tyhp/TyhpLang/Grammar/`; generated parser types in `Tyhp/TyhpLang/Parser/`; AST types in `Tyhp/TyhpLang/Ast/`.

---

## 1. Role in the compilation pipeline

```
Source file bytes
  → TyhpLexer (token stream, optional tagless mode)
  → TyhpParser entry rule (phpSrcFile | tyhpSrcFile | tyhpdefSrcFile | tagless variants)
  → ParserRuleContext (parse tree)
  → TyhpParserAstVisitor.Visit(ctx)   ← this folder
  → SrcFileAst (PhpSrcFileAst | TyhpSrcFileAst | TyhpdefSrcFileAst)
  → Binder → Checker → Emitter
```

### Primary entry: `CompilationService.ParseFile`

`Tyhp/Domain/Services/CompilationService.cs` chooses the parser entry by file extension (`.tyhpdef` before `.tyhp` because `.tyhpdef` ends with `.tyhp`), optionally uses tagless entry rules when `options.Tagless` is set, then:

1. Constructs `new TyhpParserAstVisitor(tokenStream, filename, fileHash, diagnostics)`.
2. Calls `visitor.Visit(ctx)`.
3. Casts the result to `SrcFileAst`. A non-null non-`SrcFileAst` result is reported as `MessageCode.VisitorUnexpectedAlternative`.
4. Caches the AST only when the visit produced no new errors for that file (recovery trees are not cached).

### Secondary entry: builtin tyhpdef loading

`Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.cs` uses the same visitor construction and `Visit(ctx) as SrcFileAst` pattern (with a try/catch safety net for recovery NREs).

### Historical note

`Tyhp/TyhpLang/TyhpCompiler.cs` is fully commented out; it is not a live entry point. Older snippets that call `VisitTyhpFile` directly are obsolete relative to `CompilationService`.

---

## 2. Class architecture

### Inheritance

```
TyhpParserBaseVisitor<IBase2Ast?>     (ANTLR-generated)
  └── ITyhpParserVisitor<IBase2Ast?>  (ANTLR-generated)
        └── PhpParserAstVisitor       (partial; PHP + shared helpers)
              └── TyhpParserAstVisitor (partial; Tyhp + tyhpdef overrides)
```

Production code **always** instantiates `TyhpParserAstVisitor`, even for `.php` files. PHP-only behavior is the base class; Tyhp/tyhpdef behavior is overrides and GrammarAddon hooks.

### Why partial classes

The visitor is split by grammar area so individual files stay reviewable. Naming convention:

| Prefix | Meaning |
|--------|---------|
| `PhpParserAstVisitor.Php*.cs` | Base PHP rule visitors |
| `TyhpParserAstVisitor.Tyhp*.cs` | Tyhp language extensions |
| `TyhpParserAstVisitor.Tyhpdef.cs` | Entire tyhpdef surface (~1600 lines) |
| `PhpParserAstVisitor.cs` | Shared state, doc comments, language mode, VisitChildren shutdown |
| `TyhpParserAstVisitor.cs` | Thin ctor forwarding to base |
| `PhpParserAstVisitor.Unsorted.cs` | Empty placeholder |

Approximate sizes (lines, as of this writing): Tyhpdef ≫ PhpObjects / PhpDereferenceables / PhpExpressions / PhpStatements / PhpTopStatements ≫ smaller Tyhp area files.

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

| Extension / mode | Parser rule | Visitor root | Result type |
|------------------|-------------|--------------|-------------|
| `.php` (default) | `phpSrcFile` → `#phpSrcFile` | `VisitPhpSrcFile` | `PhpSrcFileAst` |
| `.tyhp` tagged | `tyhpSrcFile` → `#tyhpFile` | `VisitTyhpFile` | `TyhpSrcFileAst` |
| `.tyhp` tagless | `tyhpTaglessSrcFile` → `#tyhpTaglessFile` | `VisitTyhpTaglessFile` | `TyhpSrcFileAst` |
| `.tyhpdef` tagged | `tyhpdefSrcFile` → `#tyhpdefFile` | `VisitTyhpdefFile` | `TyhpdefSrcFileAst` |
| `.tyhpdef` tagless | `tyhpdefTaglessSrcFile` → `#tyhpdefTaglessFile` | `VisitTyhpdefTaglessFile` | `TyhpdefSrcFileAst` |

All roots inherit `SrcFileAst`.

### Language mode strings on AST nodes

`GetCurrentLanguageMode` walks parents and returns:

| Context found | Mode string |
|---------------|-------------|
| `TyhpdefBlockContext` / `TyhpdefTaglessFileContext` | `"tyhpdef"` |
| `TyhpBlockContext` / `TyhpTaglessFileContext` | `"tyhp"` |
| `PhpBlockContext` | `"php"` |
| File root / null | `""` |

`Base2Ast.SetContext` uses this when constructing nodes so binder/checker can distinguish modes.

### Parser semantic predicates vs AST language mode (important)

Grammar actions for `tyhpBlock`, `tyhpdefBlock`, and tagless entry rules set **`this._languageMode = "tyhp"`** (including tyhpdef). That makes `{this.isLanguageMode("tyhp")}?` succeed inside tyhpdef files so shared Tyhp GrammarAddon rules apply.

AST `LanguageMode` for nodes under a tyhpdef block is still **`"tyhpdef"`** via `GetCurrentLanguageMode`, because it keys off context *types*, not the parser’s `_languageMode` field.

So: predicates see `"tyhp"`; recorded AST mode under tyhpdef is `"tyhpdef"`. Do not assume these are the same string.

### What each layer owns

**Php (`PhpParserAstVisitor.*`)** — full PHP surface: expressions/precedence, statements, top statements, objects, dereferenceables, types, attributes, parameters, try/catch, functions, root/inline HTML.

**Tyhp (`TyhpParserAstVisitor.Tyhp*.cs`)** — overrides GrammarAddons and adds Tyhp-only rules: generics, structs, extensions, typed vars, using blocks, operator overloads, type aliases, return type guards, compile-time builtins (`typeof` / `nameof` / `default` / `variable_exists`), `await`/`with`/`is` expression tokens, async modifiers, required parameter types in Tyhp mode, anonymous `new struct {…}`.

**Tyhpdef (`TyhpParserAstVisitor.Tyhpdef.cs`)** — separate top-statement grammar (`tyhpdefTopStatement*`), import-shaped declarations (classes/traits/interfaces/enums/functions/consts/variables), deprecated/obsolete markers, inline extension decls in tyhpdef, specialized error helpers (`ReportMissingRequired`, `CreateErrorImportObjectDecl`, `HandleUnexpectedAlternativeSpecial`).

---

## 5. Conventions and patterns

### Naming

- Override generated methods: `VisitXxx([NotNull] TyhpParser.XxxContext context)`.
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

### Doc comments

`FindPossibleDocComment(IToken beforeToken)` walks `_tokens` backward on `TyhpLexer.DocBlockCommentsChannel` from the token before a declaration (often a labeled `FindDocComment=` token such as `(` or `{`).

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
| `fn name(...) => expr;` (named short function) | `PhpFunctionDeclAst` body = `return expr;` — **no** `isOverloadSignature` addon |
| `function name(...): T;` overload signature | Bodyless `PhpFunctionDeclAst` + `isOverloadSignature` addon |
| Operator / extension `=> expr` bodies | Same `return expr;` wrapping |
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
| `ctorReturnType` | `TyhpCtorReturnTypeAst` | Tyhp ctors |
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
| `HandleUnexpectedAlternativeSpecial` | `Tyhpdef.cs` (partial) | Diagnostic + custom error factory (also used from TyhpGenerics/Objects) |
| `HandleFailedCast` | `PhpTopStatements.cs` | When visit result isn’t `ITopStatement` |
| `WithGrammarAddon` / `WithAttributes` | `Ast/Base2AstExtensions.cs` | Fluent AST decoration |
| `ReportMissingRequired` / `CreateErrorImportObjectDecl` / `CreateErrorParameter` | `Tyhpdef.cs` | Tyhpdef recovery (also used by type-alias / extension / struct / import / generics / typed-var visitors) |
| `VisitClassStatementListOrEmpty` / `CreateObjectTypeToken` | `PhpObjects.cs` | Object-type decl recovery (null `StatementList` / `ObjectType`) |
| Null-guarded `VisitTyhpTypeAlias` / extension / struct decls | `TyhpTypeAliases.cs`, `TyhpExtensions.cs`, `TyhpStructs.cs` | Truncated `type`/`extension`/`struct` recovery (placeholders + `VisitorMissingRequiredNode`) |
| Null-guarded Tyhp declaration recovery | `TyhpTopStatements.cs`, `TyhpFunctions.cs`, `TyhpGenerics.cs`, `TyhpStatements.cs`, `TyhpDereferenceables.cs`, `TyhpObjects.cs`, `Tyhpdef.cs`, `PhpParametersAndArguments.cs` | Truncated `use extension` / overloads / generics / typed-var / anon-struct / class-body operator overload (`VisitTyhpClassOperatorOverloadDecl`) / tyhpdef class-member sites report `VisitorMissingRequiredNode` instead of TYHP1003 |

---

## 7. File map

### `PhpParserAstVisitor`

| File | Responsibility |
|------|----------------|
| `PhpParserAstVisitor.cs` | State, doc comments, language mode, VisitChildren shutdown |
| `PhpRoot.cs` | `phpSrcFile`, code blocks, php/echo blocks, inline output |
| `PhpTopStatements.cs` | Top statement lists, namespaces, uses, const, halt_compiler, GrammarAddon handler stub |
| `PhpStatements.cs` | Inner/top statements, control flow wrappers, internal functions dispatch |
| `PhpBlocks.cs` | if/for/foreach/while/switch/match/declare |
| `PhpExpressions.cs` | Precedence ladder (`VisitPhpExprPrec`), ops, include/require, match expr, base expr |
| `PhpDereferenceables.cs` | Variables, members, calls, `new`, scalars, arrays, encaps strings |
| `PhpObjects.cs` | class/trait/interface/enum, members, properties/hooks, trait adaptations |
| `PhpFunctions.cs` | Function decls, inline functions, GrammarAddon stubs |
| `PhpParametersAndArguments.cs` | Parameters, ctor params, arguments, global/static vars |
| `PhpTypes.cs` | Type expressions, return types, GrammarAddon stubs |
| `PhpIdentifiers.cs` | Names, namespaces, reserved/semi-reserved, class name refs |
| `PhpAttributes.cs` | Attributes + attributed declaration dispatch |
| `PhpTryCatchBlocks.cs` | try/catch/finally |
| `Unsorted.cs` | Empty |

### `TyhpParserAstVisitor`

| File | Responsibility |
|------|----------------|
| `TyhpParserAstVisitor.cs` | Ctor only |
| `TyhpRoot.cs` | `tyhpFile` / tagless / blocks / inline output |
| `TyhpTopStatements.cs` | Top GrammarAddon: type alias, struct, extension, `use extension`, generic use aliases |
| `TyhpStatements.cs` | Typed var expr, using blocks |
| `TyhpFunctions.cs` | Overloads, async/generics/extends GrammarAddons, call-site generics |
| `TyhpObjects.cs` | Generic type names, Tyhp methods/ctors, type aliases, operator overloads, trait property rename, async modifiers |
| `TyhpStructs.cs` | Named/anonymous structs + properties (string or integer array-key aliases: `'key' as $name` / `0 as $name`); named structs attach declaration-site generics on `AstGrammarAddons["identifier"]` |
| `TyhpExtensions.cs` | Extension decls + extension operator overloads |
| `TyhpGenerics.cs` | Generic identifiers, type params/args |
| `TyhpIdentifiers.cs` | Tyhp reserved words, generic namespace/type/member name addons |
| `TyhpTypes.cs` | Tyhp scalar / template string types via type GrammarAddon |
| `TyhpTypeAliases.cs` | `type` alias declarations |
| `TyhpExpressions.cs` | Unary pre/post GrammarAddons (`await`, decimal cast), `tyhpWithList` |
| `TyhpDereferenceables.cs` | `new struct {…}` |
| `TyhpReturnTypes.cs` | `: $x is T` return type guards |
| `TyhpInternalFunctions.cs` | `variable_exists` / `typeof` / `default` / `nameof` (+ cast-token default form) |
| `Tyhpdef.cs` | Full tyhpdef file/block/statement surface |

---

## 8. Weirdness and WHY

### Dual language-mode story for tyhpdef

Parser `_languageMode` is `"tyhp"` inside tyhpdef blocks so Tyhp GrammarAddon predicates fire. AST `LanguageMode` is `"tyhpdef"` from context-type walking. Both are intentional; conflating them causes wrong binder behavior or broken predicates.

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

- Grammars: `Tyhp/TyhpLang/Grammar/PhpParser.g4`, `Tyhp/TyhpLang/Grammar/TyhpParser.g4` (`import PhpParser`).
- Generated: `Tyhp/TyhpLang/Parser/TyhpParser.cs`, `TyhpParserVisitor.cs`, `TyhpParserBaseVisitor.cs`.
- After grammar changes: regenerate parser, then update matching `Visit*` methods. Labeled alternative renames break switch patterns.
- Semantic predicates (`isLanguageMode`) are evaluated during parse, not visit. Wrong mode at parse time means the alternative never appears in the tree.

### Ast

- Nodes live under `Tyhp/TyhpLang/Ast/`; interfaces under `Ast/Interfaces/`.
- Visitors should not bind symbols (`BoundSymbol` / `OwningFile` are binder-owned).
- Prefer existing `Create` / `CreateError` factories; they set span and language mode consistently.
- GrammarAddons are part of the serialized AST story (`Base2Ast`); keys must stay stable for cache compatibility.

### Downstream consumers

Binder/checker/emitter assume visitor shapes (e.g. overload signatures, generic addons, tyhpdef import decls). Visitor changes that alter addon keys or desugaring usually need coordinated binder/emitter updates — those live outside this folder.

---

## 10. Pitfalls

1. **Instantiating `PhpParserAstVisitor` alone** — Tyhp/tyhpdef GrammarAddons will error; always use `TyhpParserAstVisitor`.
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
   `VisitTyhpdefClassOperatorDecl`), tyhpdef class const / trait-use / extension
   function+operator builders, plus `VisitParameter` when `Variable` is missing) report
   `VisitorMissingRequiredNode` and build placeholders when
   required trailing children are null after recovery. Type-expression fallthroughs follow
   the same rule: `VisitTypeExpr` / `VisitTypeExprWithoutStatic` / `VisitTypeWithoutStatic`
   must not call GrammarAddon visitors with a null child (use `ReportUnexpectedAlternative`
   + `CreateError` / `ErrorAst`). Expression recovery must also tolerate null
   `phpExprPrec` children — `VisitPhpExprPrec` / `VisitPhpExprPrecAlt` and
   `VisitPhpExprAmpersand` guard null `Op`/`R` because Antlr's `Visit(null)` throws NRE
   on this runtime (truncated `|` / `&` return-type slots).
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
| New tyhpdef-only construct | `TyhpParser.g4` tyhpdef rules + `Tyhpdef.cs` |
| New expression operator | Expression GrammarAddon in `TyhpParser.g4`; token visitor in `TyhpExpressions.cs` / PhpExpressions handlers |
| Generics plumbing | `TyhpGenerics.cs` + name GrammarAddons in `TyhpIdentifiers.cs` / `TyhpObjects.cs` / `TyhpFunctions.cs` |
| Doc comment bugs | `FindPossibleDocComment` call order at the declaration site |
| Wrong language mode on nodes | `GetCurrentLanguageMode` + grammar `_languageMode` actions |
| Parse succeeds, AST wrong shape | Explicit Visit* children; check desugar / GrammarAddon keys |

---

*Grounded in the Visitor sources, `CompilationService.ParseFile`, `Binder/BuiltIn/Tyhpdef.cs`, `Base2Ast` / `Base2AstExtensions`, and `Tyhp/TyhpLang/Grammar/{Php,Tyhp}Parser.g4` as of the guide’s authoring. Prefer the code when this document and the repo diverge.*
