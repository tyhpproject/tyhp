---
title: 'The init Property Modifier (cancelled)'
status:
  tier: 1
  story: '11'
  state: complete
---

This feature is **cancelled**. Tyhp has no C#-style `init` property modifier. The compiler grammar does not accept `init` on properties, and there are no `init`-specific diagnostics.

Use `readonly` properties plus `new ... with` / `clone ... with` for immutable updates. Direct assignment and in-place `$obj with [...]` still cannot mutate `readonly` after construction. See [The with Keyword](tyhp_2200_withKeyword.md) and [New Object Declaration Syntax](tyhp_1300_newObjectDeclSyntax.md).

The rationale is recorded in `dev-docs/DECISIONS.md`.
