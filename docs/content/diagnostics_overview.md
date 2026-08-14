---
title: 'Compiler Diagnostics Overview'
status:
  tier: 0
  story: '01'
  state: complete
---

The Tyhp compiler uses a structured diagnostic system to report errors, warnings, and informational messages during compilation. Every diagnostic has a unique code, a severity level, and a human-readable message that includes the source file location. This page explains how to read and interpret compiler diagnostics.

## Diagnostic Format

Each diagnostic message follows a consistent format that includes the file path, line and column numbers, severity, diagnostic code, and a descriptive message:

```tyhp
filename(line,column): severity TYHPXXXX: message
```

For example, a type mismatch error would appear as:

```tyhp
src/Models/User.tyhp(42,5): error TYHP4008: Cannot assign type 'string' to type 'int'
```

The components are:

- <code>src/Models/User.tyhp</code> -- the source file where the issue was detected
- <code>(42,5)</code> -- line 42, column 5 (1-indexed)
- <code>error</code> -- the severity level (error, warning, or info)
- <code>TYHP4008</code> -- the unique diagnostic code
- <code>Cannot assign type 'string' to type 'int'</code> -- a human-readable description of the problem

## Severity Levels

The Tyhp compiler uses three severity levels for diagnostics:

:::member[Error]
A problem that prevents successful compilation. The build will fail and no PHP output will be produced. Errors must be fixed before the code can compile. Examples include type mismatches, missing return statements, and unresolved symbols.
:::

:::member[Warning]
A potential issue that does not prevent compilation but may indicate a bug or questionable code. The build succeeds, but warnings should be investigated. When the `--strict` flag is used, warnings are treated as errors. Examples include unreachable code, possibly-null variables, and deprecated symbol usage.
:::

:::member[Info]
An informational message or style suggestion. These do not affect compilation and are purely advisory. Examples include unnecessary null-safe operator usage and eval() detection.
:::

## Diagnostic Code Numbering Scheme

Every diagnostic code follows the pattern <code>TYHPXXXX</code> where the first digit identifies the compiler phase that produced the diagnostic. This makes it easy to understand where in the compilation pipeline an issue was detected:

:::member[1000-1999: Parser/Lexer Errors]
Errors from the ANTLR4 parsing phase. These occur when the source code contains syntax that the parser cannot understand -- missing semicolons, unmatched braces, invalid token sequences, etc. Parser errors prevent AST construction for the affected file.
:::

:::member[2000-2999: Visitor/AST Errors]
Errors from the parse tree to AST conversion phase. These occur when the parser produces a valid parse tree but the visitor encounters an unexpected grammar structure or unsupported language construct during AST construction.
:::

:::member[3000-3999: Binder Errors]
Errors from the symbol resolution and scope management phase. These occur when the binder cannot resolve names, finds duplicate declarations, detects circular inheritance, or encounters invalid scope configurations.
:::

:::member[4000-4999: Checker Errors]
Errors from the type checking and semantic analysis phase. This is the largest category and includes type mismatches, missing implementations, invalid modifier usage, accessibility violations, and all other semantic validation. Checker errors are the most common diagnostics encountered during development.
:::

:::member[5000-5999: Emitter Errors]
Errors from the PHP code generation phase. These occur when the emitter cannot produce valid PHP output for a given AST construct, encounters output path conflicts, or fails to write files to disk.
:::

:::member[6000-6999: Configuration Errors]
Errors related to project configuration. These occur when the `tyhp.json` configuration file contains invalid values, missing required fields, or references nonexistent paths.
:::

:::member[7000-7999: CLI Errors]
Errors specific to the CLI build and lint actions. These include file-not-found errors for lint targets, output path conflicts during build, file write failures, and unsupported output format specifications.
:::

:::member[8000-8999: Tyhpdef Errors]
Errors related to tyhpdef type definition files. These occur when tyhpdef files fail to parse, contain duplicate declarations, reference nonexistent paths, or encounter errors during tyhpdef generation.
:::

:::member[9000-9999: Internal Compiler Errors (Reserved)]
Reserved for internal compiler errors that indicate bugs in the compiler itself. If you encounter an error in this range, please report it as a bug.
:::

## Example Error Output

Here is an example of what a typical build output looks like when there are errors and warnings:

```tyhp
src/Models/User.tyhp(15,10): error TYHP3003: Symbol 'InvalidUser' not found
src/Models/User.tyhp(42,5): error TYHP4008: Cannot assign type 'string' to type 'int'
src/Services/Auth.tyhp(23,1): error TYHP4002: Multiple visibility modifiers specified
src/Services/Auth.tyhp(67,12): warning TYHP4012: Unreachable code detected

Build failed with 3 errors and 1 warning.

  Files:     12 source files
  Duration:  0.91s (parse: 0.45s, bind: 0.12s, check: 0.34s)
  Errors:    3
  Warnings:  1
```

## Enhanced Error Messages

```status
tier: 1
story: '14'
state: complete
```

Beyond the basic <code>file(line,column): severity CODE: message</code> format, the Tyhp compiler renders diagnostics with rich, developer-focused detail. The renderer reuses the same diagnostic data carried by every phase, so text, JSON, and SARIF output stay consistent.

- Source spans and underlines — The text renderer prints the offending source line with a caret/underline beneath the primary span, plus labeled secondary spans that point at related locations (similar to the Rust compiler). Output degrades gracefully to a single line when <code>--quiet</code> is set.
- "Did you mean" suggestions — When the binder or checker reports an unknown symbol, type, or member, it attaches a Levenshtein-based suggestion drawn from the in-scope symbol table, surfaced as a <code>help:</code> hint in text output and as a machine-applicable fix in JSON and SARIF.
- Actionable fixes — Suggestions carry a span and replacement text, providing the data contract that drives <code>tyhp lint --fix</code>. Language server code actions are planned.
- The <code>--explain</code> command — Run <code>tyhp --explain TYHP4008</code> to print the long-form explanation for any diagnostic code. The error index is generated directly from the compiler's code registry, so it always matches the codes the compiler emits.

Diagnostic messages follow a consistent style: present tense, the offending symbol or type named in backticks, and "expected X, found Y" framing. A build-time consistency gate enforces that every diagnostic code has conforming message text and vice versa.

```
tyhp --explain TYHP4008
```

## How the Compilation Pipeline Works

Understanding the compilation pipeline helps in diagnosing errors. The Tyhp compiler processes your code through these sequential phases:

1. Parse -- The ANTLR4 lexer and parser read your source files and produce parse trees. Syntax errors (TYHP1xxx) are detected here. Parsing continues for other files even if one file has syntax errors.
2. Visit -- The visitor converts parse trees into Abstract Syntax Trees (AST). Unexpected grammar structures (TYHP2xxx) are detected here.
3. Bind -- The binder walks the AST to build a symbol table, resolve names, and establish scope hierarchies. Duplicate declarations and unresolved symbols (TYHP3xxx) are detected here.
4. Check -- The checker performs type checking and semantic analysis on the bound AST. Type mismatches, missing implementations, and all semantic violations (TYHP4xxx) are detected here.
5. Emit -- The emitter transforms the checked AST into PHP source code. Unsupported constructs and output conflicts (TYHP5xxx) are detected here.
6. Write -- The generated PHP files are written to disk. File system errors (TYHP7xxx) may occur at this stage.

If errors occur in an earlier phase, the compiler may skip later phases. For example, if there are parser errors in a file, that file will not proceed through binding and checking. However, other files in the project continue to be processed, so you can see as many errors as possible in a single build.

## Common Error Categories and How to Fix Them

Here are the most common categories of errors you will encounter and general strategies for resolving them:

:::member[Type Mismatch Errors (TYHP4008-4010)]
These are the most common checker errors. They occur when you try to assign, return, or pass a value of an incompatible type. Fix by ensuring the types align: add type casts where appropriate, change the declared type, or update the value. Check union types and nullable types carefully.
:::

:::member[Unresolved Symbol Errors (TYHP3003)]
These occur when the compiler cannot find a class, function, constant, or variable you referenced. Common causes: missing `use`/`import` statement, typo in the name, the symbol is in a tyhpdef file that is not loaded, or the symbol is defined in a file that failed to parse.
:::

:::member[Missing Implementation Errors (TYHP4017-4018)]
These occur when a concrete class does not implement all abstract methods from its parent class or all methods from its interfaces. Fix by implementing the missing methods with the correct signatures.
:::

:::member[Variable Errors (TYHP4013-4016)]
These occur when variables are used before assignment, may be undefined on some code paths, or lack type information. Fix by initializing variables before use, adding null checks for conditional paths, or adding explicit type annotations.
:::

:::member[Modifier Errors (TYHP4002-4007)]
These occur when visibility or member modifiers are used incorrectly. Examples: two visibility keywords on the same member, `static` on an interface method, or an accessor that is more visible than its property. Fix by correcting the modifier combinations.
:::

## Tips for Debugging Compiler Errors

1. Fix errors in order -- Earlier-phase errors (parser, binder) can cause cascading false positives in later phases. Fix TYHP1xxx and TYHP3xxx errors first, then rebuild to see if TYHP4xxx errors remain.
2. Use the lint command for faster feedback -- Run <code>tyhp lint</code> instead of <code>tyhp build</code> when you only need to check for errors. Lint skips the emit and write phases, making it faster.
3. Lint a single file -- Use <code>tyhp lint --file src/MyClass.tyhp</code> to check a single file while you are actively editing it.
4. Look up the error code -- Use the Diagnostic Code Reference page to find detailed explanations and fix guidance for any error code.
5. Check your tyhpdef files -- If you see TYHP3003 (symbol not found) for a PHP library class, ensure the corresponding tyhpdef files are loaded. Check your <code>tyhp.json</code> tyhpdef include paths.
6. Use strict mode judiciously -- The <code>--strict</code> flag treats warnings as errors. This is useful for CI pipelines but may be noisy during active development.
7. Check the target PHP version -- Some features require specific PHP versions (e.g., property hooks require PHP 8.4+, readonly properties require PHP 8.1+). Verify your <code>output.phpVersion</code> in <code>tyhp.json</code>.

## JSON Output Format

For CI/CD integration, the <code>tyhp lint</code> command supports JSON output via the <code>--format json</code> flag. Each diagnostic is serialized as a JSON object:

JSON `range` coordinates are **0-based lines** (text diagnostics are 1-based). Column is 0-based in both. Line 42 in text output is `"line": 41` here:

```json
{
  "severity": "error",
  "code": "TYHP4008",
  "file": "src/Models/User.tyhp",
  "range": {
    "start": { "line": 41, "column": 5 },
    "end": { "line": 41, "column": 15 }
  },
  "message": "Cannot assign type 'string' to type 'int'"
}
```

This format is compatible with common CI reporting tools and IDE integrations.
