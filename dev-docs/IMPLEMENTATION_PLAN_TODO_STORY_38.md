# Implementation Plan: Story 38 — Compiler Plugins v1 (Tyhp/PHP Host, `p"ns:…"`, Backtick Operators)

> **Roadmap position:** Story 38 — **Tier 4 — Future plans**
> **Direct dependencies (new numbering):** 06, 08, 09, 10, 11, 13, 20
> **New story:** split from Story 31 Idea 9 on 2026-11-03. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`; new
> `tyhp.json` keys proposed here are provisional and must be registered in `CONVENTIONS.md` §4 when the story is
> scheduled. See `ROADMAP.md` for the full tiered sequence.

> **Documentation requirement.** When scheduled, this story must carry a user documentation & AIDevGuide
> phase: the `tyhp-docs-src/content/` pages that describe the shipped surface, plus the affected
> `tyhp-ai-dev-guide` `guide/` and `handbook/` sections *and* the matching item in its `REGEN.md` (the guide is
> regenerated from that prompt, so a change missing from it is reverted by the next regeneration).

> **Status:** **v1** of the plugin surface; **v2** is [Story 39](IMPLEMENTATION_PLAN_TODO_STORY_39.md).
> **Prerequisites:** Story 06 (grammar/lexer + GrammarAddon), Story 08 (checker), Story 09/11 (emitter), Story 10/13
> (`tyhp.json` / CLI), Story 20 (packaging / Composer + Tyhp library layout). Story 03 operator-overload emit is a
> useful pattern for lowering backtick ops — not a hard dependency for DSL islands.
> **Related:** brainstorm 2026-08-05 — plugin surface without arbitrary ANTLR extension; PHP/Tyhp out-of-process host.
> **Explicitly out of scope for v1** (see Story 39 or deferred notes): post-binder/typed transform; statement-form
> islands; TextMate/IDE injection; diagnostic fix-its; sandbox policy; shared plugin cache; optimizer hooks;
> multi-round `.tyhp` re-entry into the compile pipeline (considered, **not rejected**, not planned until demand);
> third-party ANTLR mutation; PHP VM embed in C#; `#"` prefixes; unnamespaced first-wins dispatch; per-file
> `use plugin operator …`; custom unary operators (use ordinary calls).

## Summary

Add a **small, intentional compiler-plugin surface** so ecosystem authors can extend Tyhp without forking the
compiler or regenerating ANTLR grammars:

1. **Plugins authored in Tyhp and/or PHP**, executed by the **host PHP process** against a stable IR/API (C# owns
   parse + orchestration; plugins never require a Dotnet SDK).
2. **`p'ns:…'` / `p"ns:…"` / `p<<<ns` / `p<<<'ns'`** — namespaced DSL **islands** as base expressions (v1: expr-only).
   Quotes match PHP; heredoc interpolates, nowdoc does not; heredoc/nowdoc **label = namespace**. One plugin may
   register **multiple** namespaces; project aliases notify the plugin of source↔canonical bindings.
3. **`` `op` ``** — binary, left-associative **custom operators** (opaque compile-time ids). `` `$a `+` $b` `` ≠
   `$a + $b`.
4. **Hooks (not the visitor):** `AstTransform` (full AST, **pre-binder**, with activation preconditions), `Check`,
   `Emit`. Syntax islands optional.
5. **v1 packaging essentials:** plugins are **Composer packages** whose `composer.json` carries the manifest under
   **`extra.tyhp.plugin`**; discovery from **project + global** Composer installs; compile-time code on an
   **autoload-excluded** path;
   runtime helpers in-package or via Composer `require`; explicit **process order**; **`plugins.options` JSON
   Schema**; **plugin test harness**; Composer **`"tyhp": ">=…"`** for host compatibility.
6. **Config hybrid:** auto-register namespaces/operators on enable; app config for aliases/disables/conflicts;
   **attributes are not exclusively owned** — conflicts resolved by order + plugin options.

This is *not* “PHP C-extensions for arbitrary syntax.” It is closer to Roslyn analyzers/source generators + a fixed
island/operator spelling — including plugins that use **no** new syntax at all.

## Motivation

- Full ANTLR extensibility from external packages is impractical (compile-time monolithic grammar, generated
  `TyhpParser.*Context` coupling, fixed GrammarAddon slots, manual `compile_grammar.sh`).
- Checker/emitter-only plugins already cover most ecosystem value (arch rules, framework awareness, security,
  attribute-driven codegen) — but authors need a **blessed syntax hook** for embedded languages and custom ops
  without inventing new keywords per feature in core.
- Tyhp’s audience writes PHP/Tyhp, not C#. An out-of-process PHP host matches packaging and skills.

## Non-goals (keep the surface small)

| Non-goal | Why |
|----------|-----|
| Arbitrary new tokens/productions from plugins | ANTLR is closed; islands + backticks are the escape hatches |
| Plugin strings in type-name / member-name positions (v1) | Binder/tooling cost; defer until a real need |
| `p"…"` as everyday infix operators | Too verbose; backticks own that job |
| In-process PHP embed in C# | Portability/sandbox nightmare vs `php` worker |
| Parallel type-reporting API (“plugin returns `int`”) as primary | Lowered AST + normal checker is the source of truth |
| Hooks into the ANTLR visitor / parse-tree walk | Visitor stays core-owned; plugins see **AST**, not `*Context` |
| Mutating the tree during checker as the primary rewrite API | Structural rewrites belong in AstTransform (v1) / post-binder transform (Story 39) |
| Post-binder typed transform, statement islands, IDE TextMate, fix-its, sandbox, shared cache, optimizer hooks | **Story 39 (v2)** |
| Multi-round codegen (emit new `.tyhp` that re-enters compile) | Considered; **not rejected**; **not planned** until clear demand |
| Exclusive attribute ownership by a plugin | Conflicts use **load order + plugin options** (rename/disable), not exclusive claim |
| Custom unary operators | Use ordinary function/method calls around the operand |

## Architecture & plugin hook surfaces

Plugins **do not** plug into the visitor. The visitor always produces a core AST (including transient
`PluginStringAst` / `PluginInfixAst` when those forms appear). The host then invokes plugins at well-defined
pipeline stages:

```text
parse (core ANTLR — only new forms: p'…'/p"…" + `op`)
  → visitor (core only — no plugin visitor hooks;
             may create transient PluginStringAst / PluginInfixAst;
             PluginStringAst body = constant string or encaps list)
  → ★ AstTransform (plugins) — full per-file AST pass, pre-binder
  → binder (core)
  → ★ Check (plugins) — alongside / into the checker
  → ★ Emit (plugins) — alongside / into the emitter for custom nodes & side outputs
```

A plugin may implement **any subset** of the hooks. Registering `p"ns:…"` or backtick ops is optional.

### Hook 1 — `AstTransform` (post-visitor, pre-binder) — primary mutation surface

- Host passes the **complete file AST** (or a stable IR serialization of it) to each enabled plugin that
  implements this hook, in a deterministic order (document when scheduled; e.g. `plugins.enabled` order).
- Plugin may **inspect and mutate anywhere** in that tree — not only plugin-string / backtick nodes. Examples:
  - lower `p"sql:…"` / `` `cross` `` to core or custom AST;
  - rewrite/expand `#[Dto]` / `#[Route]` / other attributes into methods, properties, or companion decls;
  - inject traits, strip or rewrite calls, desugar framework patterns;
  - insert synthetic helpers the binder will see as ordinary declarations.
- **Must** eliminate every `PluginStringAst` / `PluginInfixAst` the plugin owns (or the host errors after the
  pass if any remain unhandled).
- After all transform plugins finish, binder runs on the resulting tree as if it had been authored that way.

This is the hook that makes **syntax-free plugins** first-class: arch/framework/codegen packages never need a
namespace or operator; they only implement `AstTransform` and/or `Check` / `Emit`.

#### AstTransform activation preconditions (skip when idle)

Spinning up a PHP plugin worker and shipping a full AST is expensive when the plugin would no-op. Each
`AstTransform` plugin may declare **extra** activation preconditions in its manifest (`extra.tyhp.plugin`). The host evaluates the
**effective** precondition set against a cheap **file-level AST index** *before* invoking the plugin. If nothing
matches, the plugin is **skipped** for that file.

Illustrative author-declared kinds (optional extras):

| Kind | Meaning | Example |
|------|---------|---------|
| `astNodeTypeExists` | At least one node of a given AST type is present | `"PhpAttributeAst"` |
| `astNodeTypeWithValue` | A node of a given type carries a specific value | attribute name `Dto` (literal or from config) |
| `configEquals` | Plugin effective config key equals a value (gating) | `enableDtoAttributeHandler` == `true` |
| `all` | AND-group of predicates (needed for gate + match) | config gate ∧ attribute match |
| `always` | Unconditional (rare; prefer specific predicates) | full-project rewriters |

**Config resolution for predicates:** each plugin has an **effective options object** =
plugin-package defaults (from options schema / manifest defaults) overlaid by
`tyhp.json` → `plugins.options.<package>`. Preconditions may **gate** on those values and may **read string
(and other scalar) values** from them (e.g. which attribute name to match). Missing keys use schema defaults;
unknown keys already fail schema validation at project load.

**Config-aware `when` (v1) — gating + value refs:**

Manifest snippets in this section show only the `extra.tyhp.plugin` object from the plugin package's
`composer.json`; the package it belongs to is named in the lead-in. The full `composer.json` shape is under
[Plugin packaging](#plugin-packaging--composer-packages--extratyhpplugin-v1).

`acme/tyhp-dto` → `extra.tyhp.plugin` (illustrative):

```json
{
  "hooks": ["astTransform", "emit"],
  "optionsSchema": "plugin/options.schema.json",
  "optionsDefaults": {
    "enableDtoAttributeHandler": true,
    "matchDtoAttributeName": "Dto"
  },
  "astTransform": {
    "when": [
      {
        "all": [
          { "configEquals": { "path": "enableDtoAttributeHandler", "value": true } },
          {
            "astNodeTypeWithValue": {
              "type": "PhpAttributeAst",
              "name": { "$config": "matchDtoAttributeName" }
            }
          }
        ]
      }
    ]
  }
}
```

Project override example (`tyhp.json`):

```json
{
  "plugins": {
    "enabled": ["acme/tyhp-dto"],
    "options": {
      "acme/tyhp-dto": {
        "enableDtoAttributeHandler": true,
        "matchDtoAttributeName": "DataTransfer"
      }
    }
  }
}
```

Then the predicate matches `#[DataTransfer]` (not `#[Dto]`), and if the user sets
`"enableDtoAttributeHandler": false` the whole `all` group fails → that wake-up path does not fire (host-injected
ns/op predicates, if any, still can).

Conventions:

- **`configEquals`:** `{ "path": "<dot.path>", "value": <json-scalar> }` — compare effective options at `path` with
  `value` (strict JSON equality for bool/number/string/null). Primary **gating** form.
- **`{ "$config": "<dot.path>" }`:** placeholder anywhere a string (or scalar) literal is allowed in a predicate
  (e.g. `name`, and later other fields). Resolved from effective options before matching.
- **`all`:** AND. Top-level `when` entries remain OR’d with each other and with host-injected ns/op predicates.
- Host evaluates config refs **before** AST matching (cheap); no plugin PHP process needed to decide skip vs run
  for these gates.
- Same effective options object is what the plugin receives at invoke time (so runtime handler logic and
  preconditions stay consistent).

**Host-injected predicates (v1 — mandatory, not configurable):**

For every plugin that registers `namespaces` and/or `operators` in `extra.tyhp.plugin`, the host **always** unions
the following into that plugin’s effective `when` set — authors do **not** list these, and cannot turn them off:

- `pluginStringNamespace` → every **source spelling** of each registered namespace (identity + project aliases from
  `plugins.namespaces`)
- `pluginInfixOperator` → every **source spelling** of each registered operator id (identity + project aliases from
  `plugins.operators`)

So a plugin that declares `namespaces.sql` / `operators.cross` is guaranteed to wake up for `p"sql:…"`,
`p<<<sql`, `` `cross` ``, and any aliased spellings (`acmesql`, `x`, …). There is no footgun where a package
registers a dialect/op but forgets the matching precondition, and **manifest `when` never needs to restate
aliases** — the host patches from the binding table. These injections are **not** gated by author `configEquals`
unless we later add an explicit opt-out (v1: none).

Manifest sketch (ns/op plugins — no manual `pluginStringNamespace` / `pluginInfixOperator` in `when`).

`acme/tyhp-sql` → `extra.tyhp.plugin`:

```json
{
  "hooks": ["astTransform", "check"],
  "namespaces": {
    "sql": { "forms": ["expr_rvalue"] },
    "orm": { "forms": ["expr_rvalue"] }
  },
  "astTransform": {
    "when": []
  }
}
```

Effective `when` after host injection (if project aliases `sql` → source `acmesql`, `orm` unaliased):

```json
[
  { "pluginStringNamespace": ["acmesql", "orm"] }
]
```

`acme/tyhp-vec` → `extra.tyhp.plugin`:

```json
{
  "hooks": ["astTransform", "check", "emit"],
  "operators": { "cross": {}, "dot": {}, "hadamard": {} },
  "astTransform": {
    "when": []
  }
}
```

Effective `when` if `` `+` `` aliases to `hadamard` and `` `x` `` aliases to `cross`:

```json
[
  { "pluginInfixOperator": ["cross", "x", "dot", "hadamard", "+"] }
]
```

Semantics:

- Top-level effective preconditions are OR’d (any match → run). Use `all` for AND (gate ∧ match).
- Host builds the AST index once per file (including source namespace labels / backtick ids), then filters plugins.
- **Injected ns/op predicates are mandatory** whenever the plugin registers namespaces/operators. Author `when`
  entries are **additional** ORs (config-gated attribute scans, etc.).
- Plugins with **no** namespaces, **no** operators, and **empty/omitted** `when` are treated as `always` (run every
  file) — discouraged except for deliberate whole-project rewriters. Prefer explicit config-gated or
  `astNodeTypeExists` predicates instead.
- If a plugin registers namespaces/ops **and** also writes redundant `pluginStringNamespace` /
  `pluginInfixOperator` in `when`, host may ignore/dedupe them; canonical behavior is injection from the registry +
  alias table only.
- `Check` / `Emit` may grow similar skip-logic later; v1 focuses on `AstTransform` (expensive full-tree round-trip).

### Hook 2 — `Check` (checker stage)

- Runs with binder output available (scopes, symbols, types) — what `AstTransform` deliberately does not have yet.
- Emit diagnostics; enforce layering, forbidden APIs, framework container rules, taint-style patterns, etc.
- Supply **checker rules for any custom AST nodes** the plugin introduced in `AstTransform`.
- Prefer **read-only** analysis here. Large structural rewrites belong in `AstTransform`; typed/post-binder
  rewrites are **Story 39** (`TypedAstTransform` + rebind flag).

### Hook 3 — `Emit` (emitter stage)

- Lower custom nodes to PHP; emit companion/side files (OpenAPI, serializers, stubs).
- Ordinary core AST needs no plugin emit — the core emitter handles it after a successful transform.

### What plugins intentionally do *not* hook

| Stage | Plugin access? |
|-------|----------------|
| Lexer / grammar / parser | No (except core-owned `p` / `` ` `` forms) |
| Visitor / parse-tree `*Context` walk | **No** |
| Binder internals | No direct hooks in v1 — transform earlier so binder sees normal/custom AST |
| Optimizer (Stories 23–24) | **Story 39 (v2)** — not v1 |
| Post-binder / typed AST rewrite | **Story 39 (v2)** |

**Locals / `$this` / class scope (v1):** double-quoted `p"…"` already surfaces captures as normal encaps AST
(variables / `${…}` / `{…}` exprs) inside the transient plugin node — plugins do **not** re-lex interpolation.
`AstTransform` replaces those with ordinary references / calls before binder; binder resolves; checker types.
Single-quoted `p'…'` has no interpolation (PHP semantics). Type-aware DSLs either encode types in helper
signatures or keep a custom node and implement `Check`.

## Syntax

### Plugin string islands — `p'ns:…'` / `p"ns:…"`

```tyhp
// Single-quoted: literal body (no $ / encaps interpolation) — like PHP '…' / b'…'
$q1 = p'sql:SELECT id, name FROM users WHERE id = 1';

// Double-quoted: full PHP encapsed-string semantics — like PHP "…" / b"…"
// $userId, {$expr}, ${…} are already parsed to encaps AST nodes for the plugin
$row = p"sql:SELECT id, name FROM users WHERE id = $userId LIMIT 1";
$msg = p"sql:SELECT * FROM users WHERE name = {$user->name}";

return p"sql:SELECT COUNT(*) FROM users" + 1;
```

- Lexical form analogous to **`b'…'` / `b"…"`** (binary strings): **`p` prefix** + normal PHP quote rules.
  Never `#"` (PHP `#` line-comment collision).
- **Quote semantics match PHP (and existing Tyhp string lexing):**
  - **`p'…'`** — constant encapsed string; no variable interpolation; escapes follow single-quoted rules.
  - **`p"…"`** — enters the same double-quote / encaps path as ordinary `"…"` (`ST_DOUBLE_QUOTES`,
    `encapsList` / `encapsVar`): `$var`, `$obj->prop`, `{$expr}`, `${…}`, etc. are **already AST** when the
    plugin runs (literal segments + `IEncapsVarOrString` children), not a raw unparsed blob the plugin must
    re-scan for `$`.
- Transient **plugin expression AST** wraps: registered **namespace**, plus the **body** as either a constant
  string or an encaps list (same shapes the visitor already builds for normal scalars). Plugins consume that
  structured body when lowering.
- Payload **requires** a statically discoverable `namespace:` prefix (e.g. leading constant segment
  `sql:…`). Host dispatches by registered namespace — no scan-until-handled. Interpolation must not obscure the
  namespace (e.g. `p"$x:SELECT"` is invalid for dispatch).
- **Namespace identifier rules (v1):** the `ns` in `p"ns:…"`, `p'ns:…'`, and the heredoc/nowdoc label must be a
  PHP/Tyhp **`LABEL`** — the same fragment as variable/identifier names in `PhpLexer.g4`:

  ```text
  LABEL: [a-zA-Z_\u0080-\u00ff][a-zA-Z0-9_\u0080-\u00ff]*
  ```

  So namespaces are simple identifiers (`sql`, `gql`, `jsonschema`), not dotted paths, URLs, or arbitrary
  punctuation. That keeps quoted forms and **unquoted** heredoc/nowdoc labels (`p<<<sql` / `p<<<'sql'`) on the
  same spelling (PHP heredoc labels without quotes are already `LABEL`s). Manifest `namespaces` keys must obey
  the same rule; invalid keys fail at plugin load.
- Unknown namespace → error. Two plugins claiming the same namespace → install/config error unless the app
  explicitly resolves it (alias / disable — see Configuration).
- **Multi-namespace plugins (v1):** a single plugin **may register many namespaces** in one manifest (e.g. `sql`,
  `orm`, `ddl`). Complex packages expose multiple island dialects without shipping one Composer package per
  dialect. Host dispatches each source `ns` to the owning plugin; the plugin’s hooks see which namespace (and any
  project alias) applied.
- **v1 grammar placement:** base expression only (`phpExprPrecBaseGrammarAddon` → anywhere `expr` is legal).
- **Not** statement / lvalue / type-name in v1 (statement-form islands → Story 39).
- Multi-statement expansion in an expr slot must lower to a **single expression** (helper call, IIFE, etc.) or be
  rejected.

#### Heredoc / nowdoc islands (v1) — `p<<<ns` / `p<<<'ns'`

Long DSLs must not force a one-line `p"…"`. Mirror PHP / `b<<<` style:

```tyhp
// Heredoc — interpolates (same encaps rules as p"…"); LABEL is the plugin namespace
$row = p<<<sql
SELECT id, name
FROM users
WHERE id = $userId
LIMIT 1
sql;

// Nowdoc — no interpolation (same as p'…')
$q = p<<<'sql'
SELECT id, name FROM users WHERE id = 1
sql;
```

- **Label = namespace** (no repeated `sql:` prefix in the body). Host dispatches on the label exactly as for
  `p"sql:…"`. The label is an unquoted (or nowdoc-quoted) **`LABEL`**, so it must already satisfy the namespace
  identifier rules above — no crazy names that would require a double-quoted heredoc label.
- Heredoc → encaps AST children; nowdoc → constant body. Closing label rules follow PHP heredoc/nowdoc.
- Still **expr-only** in v1.

### Backtick operators — `` `$a `op` $b` ``

```tyhp
$n = ($a `cross` $b) `dot` $c;
$hadamard = $a `+` $b;   // plugin op id "+", NOT bare +
$sum = $a + $b;          // normal PHP/Tyhp + / operator overloads
```

- Binary, **left-associative**, **one fixed precedence** for all custom ops (document when scheduled; do not
  invent per-op precedence in v1).
- Interior text is an **opaque compile-time operator id** (not dynamic/interpolated). May be `+`, `cross`, `×`,
  etc., as long as it contains no `` ` ``.
- Enabled project-wide via plugin registration — **not** per-file `use plugin operator …`.

## Plugin packaging — Composer packages + `extra.tyhp.plugin` (v1)

**Plugins are Composer packages.** There is no parallel plugin-install channel and no separate manifest file. A
plugin package is a normal Composer package whose own `composer.json` carries an **`extra.tyhp.plugin`** object —
that object **is the plugin manifest**. This follows the same pattern as `extra.tyhp.package` (the tyhpdef package
manifest): `composer.json` is the only file the compiler reads for the contract, and **presence of
`extra.tyhp.plugin` as a JSON object** is the sentinel that the package is a Tyhp compiler plugin.

When scheduled, register `extra.tyhp.plugin` in the `extra.tyhp` key table in `CONVENTIONS.md` §8 alongside
`extra.tyhp.package` and `extra.tyhp.require`.

### Discovery

When Tyhp runs a project build/check, it discovers plugins from:

1. **Project** — Composer packages installed for the project (typically `vendor/*/*/composer.json`, the same scan
   that finds `extra.tyhp.package`) whose `composer.json` has an `extra.tyhp.plugin` object.
2. **Global** — Composer packages installed in the user’s global Composer home whose `composer.json` likewise has
   `extra.tyhp.plugin` (exact discovery path follows Composer’s global vendor layout when scheduled).

`extra.tyhp` without a `plugin` object (for example a package that only carries `package`, `require`, or
`interopContractVersion`) is not a plugin. A non-object `extra.tyhp.plugin` is a plugin-load error naming the
package.

`tyhp.json` `plugins.enabled` (and order/options) selects and configures which of the discovered packages actually
run. A package present but not enabled is inert.

### Layout inside the plugin package

Split **compile-time plugin host code** from **runtime library code** so the app never autoloads the compiler
hooks:

```text
acme/tyhp-sql/                          # Composer package root
  composer.json                         # name, require (incl. "tyhp": ">=…"), autoload for RUNTIME only,
                                        #   extra.tyhp.plugin = MANIFEST (hooks, namespaces, entrypoint, …)
  plugin/                               # compile-time only (AstTransform/Check/Emit PHP)
    Plugin.php                          # entrypoint class loaded by Tyhp host, NOT by app
    …
  src/                                  # runtime library used by emitted / rewritten app code
    Sql.php                             # e.g. \Acme\TyhpSql\Sql::query
  tyhp_src/                             # optional Tyhp sources for the runtime library
  …
```

- **`composer.json` autoload** maps only the **runtime** tree (`src/`, emitted PHP, etc.) — the same surface the
  consuming app needs at request time.
- **Compile-time `plugin/` (or path named in the manifest)** is **excluded** from Composer autoload (omit from
  `autoload` / `autoload-dev` for production consumers; do not PSR-4 it into the app). Tyhp’s host loads that
  path explicitly when invoking the plugin worker.
- Runtime helpers the rewrite targets may live **in this same package** (`src/`) **or** in a **separate Composer
  dependency** listed under `require` (e.g. `acme/tyhp-sql` requires `acme/sql-runtime`). No special “companion”
  install step beyond Composer.

### `extra.tyhp.plugin` — the manifest

Lives in the plugin package's `composer.json`. Declares everything Tyhp needs that is not already expressed by
standard Composer keys:

```json
{
  "name": "acme/tyhp-sql",
  "type": "library",
  "autoload": {
    "psr-4": { "Acme\\TyhpSql\\": "src/" }
  },
  "extra": {
    "tyhp": {
      "plugin": {
        "entrypoint": "plugin/Plugin.php",
        "entrypointClass": "Acme\\TyhpSql\\Plugin\\Plugin",
        "hooks": ["astTransform", "check"],
        "dependsOn": [],
        "optionsSchema": "plugin/options.schema.json",
        "namespaces": {
          "sql": {
            "forms": ["expr_rvalue"],
            "captureLocals": true
          },
          "orm": {
            "forms": ["expr_rvalue"],
            "captureLocals": true
          },
          "ddl": {
            "forms": ["expr_rvalue"]
          }
        },
        "astTransform": {
          "when": []
        }
      }
    }
  }
}
```

A single plugin may list **one or many** `namespaces` (and many `operators`). Namespace/op wake-ups are
**host-injected** from this registry (+ aliases); do not re-list them under `when`.

The remaining examples show only the `extra.tyhp.plugin` object.

`acme/tyhp-arch`:

```json
{
  "entrypoint": "plugin/Plugin.php",
  "entrypointClass": "Acme\\TyhpArch\\Plugin\\Plugin",
  "hooks": ["check"]
}
```

`acme/tyhp-dto`:

```json
{
  "entrypoint": "plugin/Plugin.php",
  "entrypointClass": "Acme\\TyhpDto\\Plugin\\Plugin",
  "hooks": ["astTransform", "emit"],
  "optionsSchema": "plugin/options.schema.json",
  "optionsDefaults": {
    "enableDtoAttributeHandler": true,
    "matchDtoAttributeName": "Dto"
  },
  "astTransform": {
    "when": [
      {
        "all": [
          { "configEquals": { "path": "enableDtoAttributeHandler", "value": true } },
          {
            "astNodeTypeWithValue": {
              "type": "PhpAttributeAst",
              "name": { "$config": "matchDtoAttributeName" }
            }
          }
        ]
      }
    ]
  }
}
```

`acme/tyhp-vec`:

```json
{
  "entrypoint": "plugin/Plugin.php",
  "entrypointClass": "Acme\\TyhpVec\\Plugin\\Plugin",
  "hooks": ["astTransform", "check", "emit"],
  "operators": {
    "cross": {},
    "dot": {},
    "hadamard": {}
  },
  "astTransform": {
    "when": []
  }
}
```

- **Plugin identity is the Composer `name`** of the package that carries `extra.tyhp.plugin`; the manifest has no
  `name` key of its own. `plugins.enabled`, `plugins.options.<package>`, alias targets (`package:canonical`), and
  `dependsOn` all use that Composer name.
- **Paths** (`entrypoint`, `optionsSchema`, and any later path-valued keys) are relative to the directory that
  contains this `composer.json`, the same rule as `extra.tyhp.package` globs.
- **`entrypoint` / `entrypointClass`** — compile-time class under the excluded plugin path; implements the hooks
  subset it lists.
- **Other `extra.tyhp` keys are independent.** A plugin whose runtime library ships tyhpdefs also carries
  `extra.tyhp.package`; ambient tyhpdef pins stay in `extra.tyhp.require`. Each key is read on its own.
- **Not a Composer plugin.** Plugin packages stay ordinary Composer packages (`"type": "library"`); the Tyhp
  compiler reads `extra.tyhp.plugin`, Composer does not. Composer plugins (`"type": "composer-plugin"` +
  `extra.class`, as on `tyhp/core`) are a separate mechanism.
- Syntax-free plugins omit `namespaces` / `operators`.
- Manifest `forms` gate where `p'ns:…'` / `p"ns:…"` / `p<<<` may appear (`expr_rvalue` in v1).
- Preferred transform target: **core AST**; custom nodes require `Check` / `Emit` as needed.
- Runtime code location is **Composer’s problem** (`autoload` + `require`) — the manifest does not duplicate PSR-4
  roots, and runtime tyhpdefs are declared through `extra.tyhp.package`, not the plugin manifest.

### App `composer.json` sketch (consumer)

```json
{
  "require": {
    "acme/tyhp-sql": "^1.0",
    "acme/tyhp-vec": "^1.0"
  }
}
```

After `composer install`, runtime classes from those packages are available to the app; Tyhp reads
`extra.tyhp.plugin` from each installed package's `composer.json` and runs compile-time hooks only for packages
listed in `tyhp.json` `plugins.enabled`.

## Configuration (`tyhp.json`) — hybrid auto-register

> **Provisional keys** — register in `CONVENTIONS.md` §4 when this story is scheduled. Names below are illustrative.

Tyhp discovers Composer packages (project + global) whose `composer.json` has `extra.tyhp.plugin`. Listing a package under
`plugins.enabled` **activates** it and **auto-registers** its manifest namespaces and operator ids.

**App config is only required for:**

- which discovered plugins to enable (and their **process order**),
- package-specific **options** (validated against the plugin’s options schema),
- **aliases** for namespaces and operators when resolving conflicts (or for nicer spellings),
- **disables** / other conflict resolution.

```json
{
  "include": ["./src/**/*.tyhp"],
  "output": { "path": "./build", "phpVersion": "8.2" },
  "plugins": {
    "enabled": [
      "acme/tyhp-sql",
      "other/tyhp-sqlish",
      "acme/tyhp-vec"
    ],
    "options": {
      "acme/tyhp-sql": {
        "connection": "default"
      }
    },
    "namespaces": {
      "acmesql": "acme/tyhp-sql:sql",
      "acmeorm": "acme/tyhp-sql:orm",
      "othersql": "other/tyhp-sqlish:sql"
    },
    "operators": {
      "+": "acme/tyhp-vec:hadamard",
      "cross": "acme/tyhp-vec:cross",
      "x": "acme/tyhp-vec:cross"
    }
  }
}
```

Reading of alias maps:

- **`plugins.namespaces`:** **source spelling → `package:canonicalNamespace`**. Here the app writes
  `p"acmesql:…"`, `p<<<acmesql`, etc.; host routes those to `acme/tyhp-sql`’s canonical `sql`. The other
  package’s `sql` is exposed as `othersql`. Canonical names that were remapped must **not** remain claimable in
  source by the displaced plugin.
- **`plugins.operators`:** **source backtick id → `package:canonicalOpId`** (or a short form when unambiguous).
  `` `$a `x` $b` `` and `` `$a `cross` $b` `` can both map to the same canonical `cross`.

Collision policy:

1. Auto-register on enable (a plugin may contribute **multiple** namespaces and operators).
2. Two plugins claiming the same **source** `p` namespace or backtick id → **config/install error** naming both;
   resolve via alias map and/or disable.
3. **Attributes are not exclusively owned.** Conflicts use **process order** + each plugin’s **options**
   (retarget / disable). No exclusive attribute registry.

#### Alias notification to plugins (v1 — required)

When the host invokes a plugin, it passes an **effective binding table** for that plugin, e.g.:

```json
{
  "namespaces": {
    "sql": { "sourceSpellings": ["acmesql"] },
    "orm": { "sourceSpellings": ["acmeorm"] },
    "ddl": { "sourceSpellings": ["ddl"] }
  },
  "operators": {
    "cross": { "sourceSpellings": ["cross", "x"] },
    "hadamard": { "sourceSpellings": ["+"] },
    "dot": { "sourceSpellings": ["dot"] }
  }
}
```

Rules:

- Plugins **must** use this table (or equivalent API) — not hard-coded assumption that source spelling ===
  manifest key — when deciding which islands/ops they own in *this* project.
- After aliasing `sql` → source `acmesql`, that plugin must **not** still handle bare `p"sql:…"` if `sql` was
  given to another package (or left unowned). Host dispatch is authoritative; the table keeps plugin-local logic
  (logging, diagnostics, TextMate later, multi-dialect switches) honest.
- Same for operators: if `` `+` `` is aliased to `hadamard`, the vec plugin treats source `+` as canonical
  `hadamard` and does not assume bare `` `hadamard` `` exists unless listed.
- Unaliased entries appear with `sourceSpellings` equal to the canonical id (identity mapping).
- **AstTransform preconditions** always include host-injected `pluginStringNamespace` /
  `pluginInfixOperator` matches for those **source spellings** (see above) — not something authors configure per
  alias.

## Process / load order (v1)

From day one, authors must control which plugin runs before which:

- Manifest may declare `dependsOn: ["acme/tyhp-dto"]` and/or numeric `order` hints.
- App `tyhp.json` **`plugins.enabled` array order** is the default process order; optional
  `plugins.order` / dependency overrides win when specified.
- Host topologically sorts by dependencies, then applies explicit order; cycles → config error.
- Same order applies across `AstTransform` → `Check` → `Emit` for a given plugin relative to others (document
  exact per-hook sequencing when scheduled).

## Runtime library code (v1) — same Composer package or a dependency

Rewritten/emitted code may call helpers such as `\Acme\TyhpSql\Sql::query`. Those helpers are ordinary Composer
autoload surface:

- **In-package runtime** — `src/` (or equivalent) of the plugin package, listed in that package’s `composer.json`
  `autoload` (and optional `tyhp_src/` compiled into it).
- **External runtime** — another package in the plugin’s `require` (or the app’s `require`).

Compile-time `plugin/` code stays **out** of app autoload. There is no separate “install companion” step beyond
`composer require` of the plugin (and whatever it depends on). Emitted code must only reference symbols that are
reachable via the app’s Composer dependency graph.

## Host compatibility (v1)

No separate `tyhp-plugin-host` version channel. Plugin `composer.json` constrains the Tyhp toolchain like any other
package, e.g. `"tyhp": ">=804.4.3"`. Host rejects plugins whose constraint does not satisfy the running compiler.

## `plugins.options` config schema (v1)

Each plugin may ship a **JSON Schema** (and optional **`optionsDefaults`**) for its options object. Host builds
**effective options** = defaults ← `tyhp.json` `plugins.options.<package>`, validates against the schema at project
load, and fails with path-accurate config diagnostics on mismatch.

Effective options are used for:

- plugin invoke payload (handler logic),
- **AstTransform precondition gating** (`configEquals`) and **value refs** (`{ "$config": "…" }`) — e.g. enable a
  DTO attribute handler and choose the attribute name without hard-coding `Dto` in the manifest,
- author-facing knobs (connections, strictness, attribute rename/disable for conflict resolution, etc.).

## Plugin test harness (v1)

First-class author testing (parallel to conformance fixtures):

- Fixture: input `.tyhp` (+ optional `tyhp.json` plugin enablement) → expected diagnostics and/or expected
  post-transform AST / emitted PHP snippets.
- Runner invokes the real host↔plugin protocol with the plugin under test — no Dotnet SDK required for authors.
- Golden fixtures live with the plugin package; Tyhp may also ship a few reference plugins under
  `tests/conformance/` when the story is scheduled.

## End-to-end usage sketch

**Syntax plugin (SQL + vec ops + heredoc):**

```tyhp
<?tyhp
namespace App;

function loadUser(\PDO $pdo, int $userId): User {
    $row = p<<<sql
SELECT id, name FROM users WHERE id = $userId LIMIT 1
sql;
    return User::fromRow($row);
}

function physics(Vec3 $a, Vec3 $b, Vec3 $c): Vec3 {
    $n = ($a `cross` $b) `dot` $c;
    $hadamard = $a `+` $b;
    $sum = $a + $b;
    return $hadamard;
}
```

Conceptual `AstTransform` result (binder/checker see ordinary AST):

```tyhp
$row = \Acme\TyhpSql\Sql::query(
    $pdo,
    'SELECT id, name FROM users WHERE id = ? LIMIT 1',
    [$userId],
);

$n = \Acme\TyhpVec\Ops::dot(\Acme\TyhpVec\Ops::cross($a, $b), $c);
$hadamard = \Acme\TyhpVec\Ops::hadamard($a, $b);
```

**Syntax-free plugins (still first-class):**

```tyhp
#[Dto]
class UserRow {
    public function __construct(
        public readonly int $id,
        public readonly string $name,
    ) {}
}

// Arch/check plugin (Check hook only): Domain must not reference Infrastructure
namespace App\Domain {
    // use App\Infrastructure\Db;  → diagnostic from acme/tyhp-arch
}
```

`acme/tyhp-dto` uses `AstTransform` to expand `#[Dto]`. If another plugin also watches `Dto`, the user sets
process order and/or options (`attributeName`, `enabled: false`) — not exclusive ownership.

## Phased delivery (v1, when scheduled)

| Phase | Ship |
|-------|------|
| 1 | Host↔PHP protocol + Composer discovery (project + global `extra.tyhp.plugin`) + **order/deps** + **`tyhp` constraint** + **options JSON Schema** + **test harness** |
| 2 | **`AstTransform`** (preconditions + full-file mutate) + runtime via Composer autoload (in-package or dependency) |
| 3 | `p'…'` / `p"…"` + **`p<<<` / `p<<<'…'`** expr islands + namespace dispatch |
| 4 | **`Emit`** + custom-node check/emit |
| 5 | Backtick binary ops + hybrid auto-register operator map |

v2 items → [Story 39](IMPLEMENTATION_PLAN_TODO_STORY_39.md).

## Decisions (defaults chosen) — v1

1. **Tyhp/PHP out-of-process plugins** — not C# NuGet-only, not embedded PHP in the compiler.
2. **No third-party ANTLR mutation** — only core-owned `p` / `` ` `` forms.
3. **No visitor hooks** — plugins see **AST** after visit.
4. **Primary mutation surface = pre-binder `AstTransform`** (full file; activation preconditions).
5. **Host always injects alias-aware `pluginStringNamespace` / `pluginInfixOperator` preconditions** for every
   registered namespace/op (source spellings). Not optional; authors do not configure these in `when`.
6. **Author `when` may reference effective plugin config:** `configEquals` for gating;
   `{ "$config": "path" }` for string/scalar fields (e.g. attribute names); `all` for AND. Defaults from the
   plugin package, overridden by `plugins.options` in `tyhp.json`. Extra `when` entries are for non-ns/op
   triggers (attributes, etc.).
7. **`Check` / `Emit` optional**; any subset OK.
8. **Process order is v1** — `dependsOn` / `enabled` order / overrides; cycles fail.
9. **`p'…'` / `p"…"` / heredoc / nowdoc** = DSL islands (expr only); label = namespace for `p<<<`.
10. **Plugin namespaces are `LABEL` identifiers** (`PhpLexer` `LABEL` / variable-name rules) — same spelling for
   `p"ns:…"`, manifest keys, and unquoted heredoc/nowdoc labels; no dotted/URL/punctuation namespaces.
11. **One plugin may register multiple namespaces and multiple operators.**
12. **Project aliases are v1:** `plugins.namespaces` / `plugins.operators` map source spellings →
    `package:canonical`; host **always notifies** the plugin of its effective binding table so it does not handle
    displaced spellings or miss aliased ones.
13. **PHP quote/encapsed semantics**; backticks = opaque binary op ids (op ids are *not* required to be `LABEL`s —
    they may be `+`, `×`, etc., since they are not heredoc labels).
14. **Namespaces/operators:** single owner per **source spelling** (error on clash unless config resolves).
15. **Attributes:** no exclusive ownership — order + options resolve conflicts.
16. **Plugins are Composer packages**; manifest = **`extra.tyhp.plugin`** in the package's `composer.json` (no
    separate manifest file; presence as an object is the sentinel; identity is the Composer `name`); discover
    project + global installs.
17. **Compile-time plugin path excluded from Composer autoload**; runtime helpers via package `autoload` and/or
    Composer dependencies — not a separate companion install channel.
18. **Host compat via Composer `"tyhp": ">=…"`** — not a parallel host semver channel.
19. **`plugins.options` validated** against plugin-shipped JSON Schema (with defaults).
20. **Plugin test harness** ships with v1.
21. **No custom unary ops**; no multi-round `.tyhp` re-entry (deferred, not rejected).

## Risks & Edge Cases — v1

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | Process-per-phase overhead | Preconditions skip idle plugins; batch transforms |
| 2 | IR / API breakages | Composer `tyhp` constraint; semver discipline |
| 3 | Backtick vs future `` ` `` uses | Reserve infix-op position; fixtures |
| 4 | SQL/local capture / injection | Encaps → bound params; plugin owns parameterization |
| 5 | Namespace/op collisions | Hard error + alias/disable; **alias table passed into plugin** |
| 6 | Plugin still handles old spelling after alias | Host dispatch + binding table; conformance that displaced `sql` is not handled |
| 7 | Attribute double-handling | Document order + options; conformance |
| 8 | Custom nodes without Check/Emit | Host error |
| 9 | Heredoc label ≠ registered/aliased namespace | Unknown-namespace error |
| 10 | Dependency cycles in plugin order | Config error at load |
| 11 | False-negative / forgotten ns-op `when` | **Mandatory host injection** from registry + aliases |
| 12 | IDE highlighting inside islands | Opaque in v1; **Story 39** TextMate injection |

## Golden Fixtures / Tests (Acceptance) — v1

- Check-only and AstTransform syntax-free plugins (arch / `#[Dto]`).
- AstTransform skip when author extras miss **and** no registered ns/op source spelling is present; run when an
  injected ns/op spelling or an author `when` hits.
- Injected preconditions: plugin with `namespaces.sql` wakes on `p"sql:…"` without listing it in `when`; after
  alias to `acmesql`, wakes on `p"acmesql:…"` and not on displaced bare `sql` if another owner has it.
- Config-gated `when`: `enableDtoAttributeHandler: false` skips the DTO attribute wake path; renaming
  `matchDtoAttributeName` to `DataTransfer` matches `#[DataTransfer]` via `{ "$config": "…" }`.
- `p'sql:…'`, `p"sql:…"`, `p<<<sql` (heredoc encaps), `p<<<'sql'` (nowdoc) lower correctly; unknown ns fails.
- Invalid namespace spellings (e.g. `sql.v2`, `my-sql`) rejected at manifest load and/or parse — must match `LABEL`.
- **Multi-namespace:** one plugin registers `sql` + `orm`; both `p"sql:…"` and `p"orm:…"` dispatch to it.
- **Namespace alias:** `acmesql` → `acme/tyhp-sql:sql`; `p"acmesql:…"` handled; bare `p"sql:…"` not handled by that
  plugin when displaced; plugin invocation receives binding table reflecting the alias.
- **Operator alias:** source `` `x` `` → canonical `cross`; plugin sees mapping; does not require `` `cross` `` in
  source if only `x` is configured.
- Double-quoted encaps / heredoc interpolations yield encaps AST children before transform.
- Process order: plugin A before B changes transform outcome; `dependsOn` cycle fails at config load.
- Options schema: invalid `plugins.options` fails with config diagnostic.
- Runtime via Composer: in-package `src/` autoload and/or a `require`d dependency; compile-time `plugin/` not in app
  autoload.
- Composer `tyhp` constraint: incompatible plugin rejected.
- Discovery: project `vendor/` plugin and a global-install plugin both found via `extra.tyhp.plugin` when enabled.
- Discovery sentinel: a package with `extra.tyhp.package` / `extra.tyhp.require` but no `extra.tyhp.plugin` is not
  a plugin; a non-object `extra.tyhp.plugin` fails plugin load naming the package; `entrypoint` / `optionsSchema`
  resolve relative to the plugin's `composer.json`.
- Test harness: sample plugin package passes its own golden fixtures via the harness.
- Backtick ops / `` `+` `` vs bare `+`; leftover PluginString/Infix after transform → error.
- Attribute conflict: two plugins + order/options resolve without exclusive-ownership API.
- No Dotnet SDK required for plugin authors.
