# Implementation Plan: Story 17 — Sourcemap Generation

> **Roadmap position:** Story 17 — **Tier 2 — DX & Ecosystem**
> **Direct dependencies (new numbering):** 01, 09
> **Renumbered from:** legacy Story 9
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Scope:** Story 17 of the Tyhp compiler TODO
> **Branch:** TBD
> **Prerequisite:** Story 09 (Emitter) must be functional — `PHPOutputFile.Generate()` produces PHP output, `EmitItem.emit()` produces formatted code, and `TyhpEmitter` walks the AST to produce `EmitItem` trees. Story 01 (Foundation) provides `DiagnosticBag`, `CompilationResult`, `CompilationService`, and `BuildAction`.
> **Spec:** [Source Map Revision 3](https://github.com/tc39/source-map-spec/blob/main/source-map-rev3.md)

---

## Architecture Overview

### What Sourcemaps Do in the Tyhp Compiler

The Tyhp compiler transpiles `.tyhp` source files into `.php` output files. Sourcemaps provide a bidirectional mapping between positions in the generated PHP code and their originating positions in the Tyhp source. This enables:

1. **Debugging** — The XDebug Proxy (Story 18) uses sourcemaps to translate breakpoints and stack traces between `.tyhp` and `.php` files.
2. **Error Reporting** — PHP runtime errors reference `.php` lines; sourcemaps let tooling map these back to `.tyhp` lines for developer-facing output.
3. **IDE Integration** — The Language Server (Story 19) can use sourcemaps to correlate compiled output with source for navigation features.

### Source Map v3 Format Summary

The Source Map v3 specification defines a JSON format:

```json
{
  "version": 3,
  "file": "output.php",
  "sourceRoot": "../src/",
  "sources": ["MyClass.tyhp"],
  "sourcesContent": ["<?tyhp\nclass MyClass { ... }"],
  "names": ["MyClass", "myMethod", "$myVar"],
  "mappings": "AAAA;AACA,SAAS,..."
}
```

The `mappings` field is the critical component — a series of Base64 VLQ-encoded segments that map each position in the generated file back to a position in the original source. Each segment encodes up to 5 fields:
1. Generated column (relative to previous segment in same line)
2. Source file index (relative to previous occurrence)
3. Original line (relative to previous occurrence)
4. Original column (relative to previous occurrence)
5. Name index (optional, relative to previous occurrence)

Lines in the generated file are separated by `;` in the mappings string. Segments within a line are separated by `,`.

### How Sourcemaps Integrate with the Existing Emitter

The emitter pipeline (from Story 09) works as follows:

```
SrcFileAst → Optimizer (Story 23) → TyhpEmitter → EmitItem tree → PHPOutputFile → Generate() → PHP string
                                                                                  → SourceMap() → JSON string (THIS STORY)
```

The key integration point is the `EmitItem` class. Each `EmitItem` already stores:
- `Provider` (`IBase2Ast`) — the source AST node that generated this emit item
- `StartContent` / `EndContent` (`IList<string>`) — the PHP code fragments
- `Children` (`List<EmitItem>`) — nested emit items
- `EmitType` — the kind of emitted construct

The `Provider` property is the bridge to source positions: every AST node (`Base2Ast`) has `Line`, `Column`, and `StartIndex` properties recording its **start** position in the original `.tyhp` source. And `SrcFileAst` has `FileName` identifying which source file the AST came from.

**Note:** At the time Story 17 executes, AST nodes only have start positions (`Line`, `Column`, `StartIndex`). End-position properties (`EndLine`, `EndColumn`, `EndIndex`) are added later by Story 19 Phase 1 (Language Server), but they are **not needed for sourcemaps**: the Source Map v3 spec only maps start positions of segments, and all major consumers (debuggers, error mappers, IDE integrations) only need start positions to function correctly. The sourcemap implementation should use only start positions.

The `emit()` method on `EmitItem` currently produces the final PHP string by recursively walking the tree and concatenating content with indentation. The sourcemap generation needs to intercept this process to track what generated line/column each piece of content ends up at, while recording which source AST node (`Provider`) it came from.

### Optimizer Interaction (Story 23)

The optimizer (Story 23) runs before the emitter and may transform the AST by inlining extension method/operator calls, eliminating dead code, or folding constants. When the optimizer replaces an AST node, the replacement node carries an `OriginalAst` property (of type `IBase2Ast?`, added by Story 23) pointing to the original call site in the Tyhp source.

The sourcemap generator must handle these optimizer-replaced nodes:

1. **Inlined extension calls:** When an extension operator call (`$a + $b`) is inlined to a direct method call (`$a->add($b)`), the emitted PHP code should map back to the **original `$a + $b` expression** in the Tyhp source — not to the extension operator's body. Check for `OriginalAst` on AST nodes and use it as the source position when present: `var sourceNode = (provider as Base2Ast)?.OriginalAst ?? provider;`

2. **Eliminated code:** Dead code removed by the optimizer simply produces no emit items, so no mapping is needed.

3. **Folded constants:** A folded constant expression should map back to the original expression in the Tyhp source (e.g., `2 + 3` folded to `5` should map to the original `2 + 3` position).

4. **Stack trace reconstruction:** When a PHP error occurs on a line that was an inlined call, the sourcemap correctly maps to the Tyhp call site. The Tyhp Reflection API (Story 29) uses sourcemaps and reflection metadata to reconstruct the full original Tyhp call stack, including calls that were inlined away in the compiled PHP, via `\Tyhp\Reflection\StackTrace`.

### Architectural Strategy

The implementation introduces:

1. **`SourceMapping`** — A lightweight data structure recording a single mapping segment (generated position → original position).
2. **`SourceMapCollector`** — Collects mappings during the `emit()` process. Passed to a new overload of `emit()` that reports positions as it builds the output string.
3. **`VlqEncoder`** — Encodes integers as Base64 VLQ strings per the source map spec.
4. **`SourceMapGenerator`** — Combines collected mappings into the final Source Map v3 JSON document.
5. **`PHPOutputFile.SourceMap()`** — Implemented on the `PHPOutputFile` class (rebuilt by Story 09) to orchestrate sourcemap generation using the above components.

### File Organization

```
Tyhp/TyhpLang/Emitter/
├── EmitItem.cs                    (~130 lines, existing — modified to add tracking emit overload)
├── PHPOutputFile.cs               (existing, rebuilt by Story 09 — modified to implement SourceMap())
├── TyhpEmitter.cs                 (~10 lines, existing — not modified in this story)
├── readme.md                      (existing — not modified)
├── SourceMap/
│   ├── SourceMapping.cs           (~40 lines, new)
│   ├── SourceMapCollector.cs      (~180 lines, new)
│   ├── VlqEncoder.cs              (~120 lines, new)
│   ├── SourceMapGenerator.cs      (~200 lines, new)
│   └── SourceMapWriter.cs         (~100 lines, new)
```

### MessageCode Numbering

Sourcemap-related error codes belong in the 5000s range (Emitter). Story 17 is allocated **`5020–5022`** (the earlier `5010–5012` collided with Story 09's emitter-core block `5002–5011` and Story 11's expansion block `5012–5019`):

| Code | Name | Description |
|------|------|-------------|
| 5020 | `EmitterSourceMapGenerationFailed` | Failed to generate source map for an output file |
| 5021 | `EmitterSourceMapWriteFailed` | Failed to write `.map` file to disk |
| 5022 | `EmitterSourceMapInvalidMapping` | A mapping references an invalid source position |

### Safety Notes

- **Before any file replacement or major rewrite**, create a timestamped backup: `<filename>.bak.<timestamp>` (e.g. `<filename>.bak.20260216_143000`)
- **Never use** `git reset`, `git revert`, `git checkout .`, `git clean`, or similar destructive commands
- **Prefer incremental edits** over wholesale file replacement
- **Backup files are sacred** — never delete or modify them

---

## Phase 1: VLQ Base64 Encoder

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Overview

Implement the Variable Length Quantity (VLQ) Base64 encoding algorithm required by the Source Map v3 specification. VLQ encoding compresses integer values into compact base64 strings, supporting both positive and negative numbers. This is a standalone, self-contained utility with no dependencies on the rest of the emitter — it can be implemented and tested in isolation.

### Deliverables

- `Tyhp/TyhpLang/Emitter/SourceMap/VlqEncoder.cs` — Static utility class for VLQ Base64 encoding and decoding

### Implementation Details

**`VlqEncoder.cs`**

Create in namespace `Tyhp.TyhpLang.Emitter.SourceMap`:

The VLQ Base64 encoding works as follows:
1. Convert the signed integer to an unsigned value using sign-magnitude representation: the least significant bit stores the sign (0 = positive, 1 = negative), and the remaining bits store the absolute value.
2. Break the unsigned value into 5-bit groups.
3. For each group except the last, set the continuation bit (bit 6, value 32).
4. Encode each 6-bit value using the base64 character set: `ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/`.

Methods:

- `static string Encode(int value)` — Encode a single signed integer to a VLQ Base64 string. This is the primary method used during sourcemap generation.
- `static string Encode(int[] values)` — Encode an array of integers, concatenating the VLQ strings (no separator — VLQ is self-delimiting).
- `static int Decode(string vlq, ref int offset)` — Decode a single VLQ value starting at `offset` in the string, advancing `offset` past the consumed characters. Used for validation and testing.
- `static int[] DecodeSegment(string vlq)` — Decode an entire segment string into its component integers.

Internal constants:
- `VLQ_BASE_SHIFT = 5`
- `VLQ_BASE = 1 << VLQ_BASE_SHIFT` (32)
- `VLQ_BASE_MASK = VLQ_BASE - 1` (31)
- `VLQ_CONTINUATION_BIT = VLQ_BASE` (32)
- `BASE64_CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"`
- A reverse lookup `char → int` dictionary for decoding.

**Key encoding examples for verification:**

| Value | VLQ Base64 |
|-------|-----------|
| 0 | `A` |
| 1 | `C` |
| -1 | `D` |
| 5 | `K` |
| -5 | `L` |
| 15 | `e` |
| 16 | `gB` |
| -16 | `hB` |
| 100 | `oG` |
| 1000 | `gxB` |

### Acceptance Criteria

- `VlqEncoder.Encode(0)` returns `"A"`
- `VlqEncoder.Encode(1)` returns `"C"`
- `VlqEncoder.Encode(-1)` returns `"D"`
- `VlqEncoder.Encode(16)` returns `"gB"`
- Round-trip: `VlqEncoder.Decode(VlqEncoder.Encode(n))` returns `n` for all values in range `[-10000, 10000]`
- `VlqEncoder.Encode(new[] { 0, 0, 1, 0 })` produces a valid concatenated VLQ string that decodes back to `[0, 0, 1, 0]`
- No external dependencies — pure algorithmic implementation
- File compiles without errors

### Dependencies

- **Requires:** Nothing — standalone utility
- **Provides:** VLQ encoding for `SourceMapGenerator` (Phase 4)

---

## Phase 2: SourceMapping Data Structure and SourceMapCollector

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Overview

Create the `SourceMapping` record that represents a single mapping segment (one generated position mapped to one original position), and the `SourceMapCollector` that accumulates mappings during the emission process. The collector tracks the generated file's line/column position as PHP content is appended, and records a mapping entry for each piece of content that has a known source AST node.

### Deliverables

- `Tyhp/TyhpLang/Emitter/SourceMap/SourceMapping.cs` — Mapping data structure
- `Tyhp/TyhpLang/Emitter/SourceMap/SourceMapCollector.cs` — Mapping accumulator

### Implementation Details

**`SourceMapping.cs`**

Create in namespace `Tyhp.TyhpLang.Emitter.SourceMap`:

A record (or lightweight class) representing a single mapping entry:

- `int GeneratedLine { get; }` — 0-based line in the generated PHP file
- `int GeneratedColumn { get; }` — 0-based column in the generated PHP file
- `int SourceIndex { get; }` — index into the `sources` array (which `.tyhp` file)
- `int OriginalLine { get; }` — 0-based line in the original `.tyhp` file
- `int OriginalColumn { get; }` — 0-based column in the original `.tyhp` file
- `int? NameIndex { get; }` — optional index into the `names` array (for symbol name mappings)

Use a `readonly record struct` for minimal allocation overhead — there will be many mapping entries per file.

**`SourceMapCollector.cs`**

Create in namespace `Tyhp.TyhpLang.Emitter.SourceMap`:

The collector is passed into the emission process and tracks position as content is written:

State:
- `int _currentGeneratedLine` — current line in the generated output (0-based), starts at 0
- `int _currentGeneratedColumn` — current column in the generated output (0-based), starts at 0
- `List<SourceMapping> _mappings` — all collected mapping entries
- `Dictionary<string, int> _sourceFileIndices` — maps source file paths to their index in the `sources` array
- `List<string> _sourceFiles` — ordered list of source file paths (the `sources` array)
- `Dictionary<string, int> _nameIndices` — maps symbol names to their index in the `names` array
- `List<string> _names` — ordered list of symbol names

Public methods:

- `void AddContent(string content, IBase2Ast? provider)` — The primary method called during emission. Appends `content` to the tracked output position. If `provider` is non-null and has valid `Line`/`Column` (not -1), records a mapping from the current generated position to the provider's source position. Then advances `_currentGeneratedLine` and `_currentGeneratedColumn` based on newlines in `content`.

- `void AddNewLine()` — Advance to the next generated line. Increment `_currentGeneratedLine`, reset `_currentGeneratedColumn` to 0.

- `int RegisterSourceFile(string filePath)` — Register a source file and return its index. If already registered, return the existing index.

- `int RegisterName(string name)` — Register a symbol name and return its index.

- `IReadOnlyList<SourceMapping> GetMappings()` — Return all collected mappings, sorted by generated line then generated column.

- `IReadOnlyList<string> GetSourceFiles()` — Return the ordered source file list.

- `IReadOnlyList<string> GetNames()` — Return the ordered name list.

- `void Reset()` — Clear all state for reuse.

**Position tracking logic in `AddContent`:**

When `AddContent(content, provider)` is called:
1. If `provider` is non-null with valid positions (`provider.Line >= 0`):
   a. Determine the source file from the provider's owning `SrcFileAst.FileName`. If this file is not yet registered, call `RegisterSourceFile()` to add it to the `sources` array. Use the returned index as the `SourceIndex` for this mapping.
   b. Create a `SourceMapping` with the current generated position and the provider's original line/column (converting from 1-based AST lines to 0-based sourcemap lines).
   c. Add to `_mappings`.
2. Scan `content` for newline characters:
   - For each `\n`, increment `_currentGeneratedLine` and reset `_currentGeneratedColumn` to 0.
   - After the last `\n` (or if no newlines), set `_currentGeneratedColumn` to the number of characters after the last newline.

**Source file resolution:**

The Source Map v3 spec natively supports mapping one generated file to **multiple** source files via the `sources` array. Each mapping segment includes a `SourceIndex` field that identifies which source file the segment maps to. This means the sourcemap naturally handles the case where one PHP output file contains code from multiple `.tyhp` files — no special workarounds are needed.

The collector resolves the source file for each AST node as follows:

1. The collector accepts a `defaultSourceFileName` when emission starts for a `PHPOutputFile`. This is registered via `RegisterSourceFile()` and covers the common case where the output corresponds to one `.tyhp` source.
2. When `AddContent(content, provider)` is called, the source file is accessed via `astNode.OwningFile` (the `OwningFile` property on `IBase2Ast`, added in Story 05). This provides direct access to `SrcFileAst.FileName` without needing parent-chain traversal or external lookup dictionaries. If the provider belongs to a different source file than the default, that file is registered via `RegisterSourceFile()` and the mapping uses the correct `SourceIndex`.

This approach requires no modifications to `EmitItem` — the source file information is derived from the existing AST structure.

### Acceptance Criteria

- `SourceMapping` record compiles and stores all five mapping fields
- `SourceMapCollector.AddContent("hello", provider)` with a provider at line 1, column 0 records a mapping at generated position (0, 0) → original position (0, 0)
- After `AddContent("line1\nline2", provider)`, the collector's current position is line 1, column 5
- After `AddContent("abc\ndef\nghi", null)`, the collector tracks position correctly but adds no mapping (null provider)
- `RegisterSourceFile()` returns 0 for the first file, 1 for the second, and 0 again for a duplicate
- `GetMappings()` returns mappings sorted by generated line then column
- File compiles without errors

### Dependencies

- **Requires:** Nothing (`IBase2Ast` interface already exists in the codebase)
- **Provides:** Mapping data for `SourceMapGenerator` (Phase 4) and the emission tracking for Phase 3

---

## Phase 3: Extend EmitItem with Source-Tracking Emission

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Overview

Add a new `emit()` overload to `EmitItem` that accepts a `SourceMapCollector` and feeds it content+provider information as the PHP string is built. The existing `emit(int indentLevel)` method remains unchanged for backward compatibility. The new overload mirrors the same logic but additionally reports each content fragment and its originating AST node to the collector.

### Deliverables

- Modified `Tyhp/TyhpLang/Emitter/EmitItem.cs` — New `emit(int indentLevel, SourceMapCollector collector)` overload

### Implementation Details

**New `emit()` overload on `EmitItem`**

Add a second `emit` method that parallels the existing one but feeds the collector:

```csharp
public string emit(int indentLevel, SourceMapCollector collector)
```

The method follows the same structure as the existing `emit(int indentLevel)`:

1. **Start content:** Build the indent string and join `StartContent` with `"\n" + indent`. Before writing each content fragment from `StartContent`, call `collector.AddContent(fragment, this.Provider)` so the collector records the mapping between the generated position and the source AST node.

2. **Children:** Recursively call `child.value.emit(indentLevel + 1, collector)` on each sorted child. Each child's emission will report its own content and provider to the collector.

3. **End content:** Same as start content — join `EndContent` with `"\n" + indent` and report each fragment to the collector with `this.Provider`.

**Key differences from the existing `emit()`:**

- Instead of building substrings independently and concatenating at the end, the new method must write content in order, calling `collector.AddContent()` for each piece as it goes.
- The method still returns the full PHP string (same as the existing method) for backward compatibility and for `PHPOutputFile.Generate()` to use.
- Indentation strings are reported to the collector with a `null` provider (indent whitespace has no source mapping).
- Newlines between sections are reported via `collector.AddContent("\n", null)`.

**Detailed fragment-level tracking:**

The existing `emit()` joins `StartContent` list items with `"\n" + indent`. The tracking version needs to report each item individually:

```
For each item in StartContent (indexed by i):
    If i == 0:
        Report indent string with null provider
    Else:
        Report "\n" + indent with null provider
    Report the content item with this.Provider
```

Similarly for the middle (children) and end sections.

**Important: do not change the existing `emit(int indentLevel)` method.** It must remain exactly as-is for backward compatibility. The new overload is purely additive.

### Acceptance Criteria

- `EmitItem.emit(0, collector)` returns the same string as `EmitItem.emit(0)` for any given `EmitItem` tree
- After calling `emit(0, collector)`, the collector contains mapping entries for every `EmitItem` node that has a non-null `Provider` with valid line/column
- The collector's final generated line count matches the number of `\n` characters in the returned string
- Nested `EmitItem` trees (parent → children → grandchildren) correctly produce mappings at the right generated positions
- The existing `emit(int indentLevel)` method is unchanged and continues to work
- File compiles without errors and existing tests/functionality are not affected

### Dependencies

- **Requires:** Phase 2 (`SourceMapCollector`)
- **Provides:** Source-tracked emission for `PHPOutputFile.SourceMap()` (Phase 5)

---

## Phase 4: Source Map v3 JSON Generator

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Overview

Implement the `SourceMapGenerator` class that takes collected mappings from a `SourceMapCollector` and produces the Source Map v3 JSON document. This class handles the VLQ encoding of the `mappings` field, assembly of the `sources` array, optional embedding of source content, and JSON serialization.

### Deliverables

- `Tyhp/TyhpLang/Emitter/SourceMap/SourceMapGenerator.cs` — Generates Source Map v3 JSON from collected mappings

### Implementation Details

**`SourceMapGenerator.cs`**

Create in namespace `Tyhp.TyhpLang.Emitter.SourceMap`:

Constructor:
- `SourceMapGenerator(string generatedFileName, string? sourceRoot = null)` — `generatedFileName` is the name of the generated PHP file (e.g., `"MyClass.php"`), `sourceRoot` is the optional relative path prefix for source files.

Methods:

- `string Generate(SourceMapCollector collector, bool includeSourcesContent = false, Func<string, string?>? sourceContentProvider = null)` — Main entry point. Returns the complete Source Map v3 JSON string.

  Parameters:
  - `collector` — The collector populated during emission
  - `includeSourcesContent` — If true, embed original `.tyhp` source content in the `sourcesContent` array
  - `sourceContentProvider` — Callback `(filePath) => fileContent` to retrieve source content for embedding; called only if `includeSourcesContent` is true

Internal logic of `Generate()`:

1. **Build the `sources` array** from `collector.GetSourceFiles()`. If `sourceRoot` is set, make the paths relative to `sourceRoot`.

2. **Build the `names` array** from `collector.GetNames()`.

3. **Build the `sourcesContent` array** (optional). For each source file, call `sourceContentProvider(filePath)` to get the content. If the provider returns null, use `null` in the array.

4. **Build the `mappings` string** by encoding all mapping segments:

   a. Get all mappings sorted by generated line then column.
   b. Group mappings by generated line.
   c. For each generated line:
      - Encode each mapping segment as VLQ relative values.
      - Segments within a line are separated by `,`.
   d. Lines are separated by `;`.
   e. Empty lines (no mappings) produce an empty string between `;` delimiters.

   **Relative encoding state:**
   The VLQ values in each segment are relative to the previous segment's values (across the entire file, not just within a line). Track:
   - `previousGeneratedColumn` — reset to 0 at the start of each generated line
   - `previousSourceIndex` — persists across lines
   - `previousOriginalLine` — persists across lines
   - `previousOriginalColumn` — persists across lines
   - `previousNameIndex` — persists across lines

   For each mapping segment, encode:
   - `mapping.GeneratedColumn - previousGeneratedColumn`
   - `mapping.SourceIndex - previousSourceIndex`
   - `mapping.OriginalLine - previousOriginalLine`
   - `mapping.OriginalColumn - previousOriginalColumn`
   - (if `mapping.NameIndex` has value) `mapping.NameIndex.Value - previousNameIndex`

   Then update the `previous*` values.

5. **Assemble JSON object.** Use `System.Text.Json.Nodes.JsonObject` for dynamic JSON construction with proper escaping:

   ```json
   {
     "version": 3,
     "file": "<generatedFileName>",
     "sourceRoot": "<sourceRoot or empty>",
     "sources": ["file1.tyhp", "file2.tyhp"],
     "sourcesContent": ["content1", "content2"],
     "names": ["name1", "name2"],
     "mappings": "AAAA;AACA,..."
   }
   ```

   `JsonObject` handles proper JSON escaping of source content strings (which may contain special characters, newlines, quotes, backslashes, etc.) and does not require defining a separate DTO class.

**Helper: `EncodeMappingSegment()`**

```csharp
private static string EncodeMappingSegment(SourceMapping mapping, ref int prevGenCol, ref int prevSrcIdx, ref int prevOrigLine, ref int prevOrigCol, ref int prevNameIdx)
```

Encodes a single segment, updates the `prev*` state, and returns the VLQ string.

### Acceptance Criteria

- `SourceMapGenerator.Generate()` returns valid JSON that parses as a JSON object
- The JSON contains `"version": 3`
- The `file` field matches the provided generated file name
- The `sources` array contains the registered source file paths
- The `mappings` string decodes (via `VlqEncoder.Decode`) back to the original mapping data
- When `includeSourcesContent` is true, the `sourcesContent` array contains the original file content
- When `includeSourcesContent` is false, `sourcesContent` is omitted or null
- Empty generated lines produce `;;` in the mappings string (consecutive semicolons)
- The relative encoding is correct: decoding all VLQ segments with running state produces the absolute mapping values
- The JSON is well-formed with properly escaped strings (handles `.tyhp` content with quotes, backslashes, newlines)
- File compiles without errors

### Dependencies

- **Requires:** Phase 1 (`VlqEncoder`), Phase 2 (`SourceMapping`, `SourceMapCollector`)
- **Provides:** JSON generation for `PHPOutputFile.SourceMap()` (Phase 5)

---

## Phase 5: Implement PHPOutputFile.SourceMap() and Wire into Emission

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Overview

Implement the existing `PHPOutputFile.SourceMap()` stub method to orchestrate sourcemap generation using the components built in Phases 1-4. Modify `PHPOutputFile.Generate()` to optionally use the source-tracking `emit()` overload so that mappings are collected during code generation. Add a configuration flag for enabling/disabling sourcemap generation.

### Deliverables

- Modified `Tyhp/TyhpLang/Emitter/PHPOutputFile.cs` — `SourceMap()` implemented, `Generate()` enhanced with optional tracking
- Modified `Tyhp/Domain/Exceptions/MessageCode.cs` — New emitter sourcemap error codes (5020-5022)

### Implementation Details

**`PHPOutputFile` Enhancements**

Story 09 rebuilds `PHPOutputFile` with `Generate()`, `FilePath`, `FromAstTree()`, and the class infrastructure. This phase adds sourcemap-specific properties and implements `SourceMap()`.

Add new properties to `PHPOutputFile`. **Use a single mechanism — the public `SourceMapCollector` property — for both enabling tracking and storing the collected mappings. Do NOT also introduce a private `_sourceMapCollector` field; there is exactly one collector reference.**

- `public SourceMapCollector? SourceMapCollector { get; set; }` — The single source of truth for sourcemap state. When set to a non-null collector before `Generate()` runs, sourcemap tracking is active during generation and `SourceMap()` reads the populated collector afterward. Left `null` ⇒ no sourcemap (fast path).
- `public string? SourceFileName { get; set; }` — The original `.tyhp` source file path that this output file was generated from. Set by `FromAstTree()` when creating `PHPOutputFile` instances.
- `public string? SourceRoot { get; set; }` — Relative path from the output directory to the source directory. Set from configuration.

**`Generate()` Enhancement**

Story 09 (prerequisite) provides a working `Generate()` that calls `EmitItem.emit(0)` to produce the PHP string. This phase modifies it to optionally collect sourcemap data.

Use the property-based approach described above: the caller enables sourcemaps by assigning a fresh `SourceMapCollector` to `this.SourceMapCollector` before calling `Generate()`. This avoids changing the `Generate()` method signature and is consistent with the collector-based architecture. `Generate()` checks `this.SourceMapCollector != null` to decide whether to track source positions.

- If `this.SourceMapCollector != null` (sourcemap generation enabled):
  1. Use the already-assigned collector (`this.SourceMapCollector`) — do not allocate a second one.
  2. Register the source file: `this.SourceMapCollector.RegisterSourceFile(this.SourceFileName)`.
  3. For the preamble (`<?php`, `declare(strict_types=1);`, namespace, use statements): call `this.SourceMapCollector.AddContent(...)` with `null` provider (these are generated boilerplate, not from user source).
  4. For each statement's `EmitItem` tree: call `emitItem.emit(indentLevel, this.SourceMapCollector)` instead of `emitItem.emit(indentLevel)`.
- If `this.SourceMapCollector == null` (disabled):
  - Use the normal `emit(indentLevel)` path (no collector overhead).

**`SourceMap()` Implementation**

Implement `SourceMap()` (which Story 09 leaves as a `// PLACEHOLDER_STORY_17` stub):

```csharp
public string SourceMap(bool includeSourcesContent = false, Func<string, string?>? sourceContentProvider = null)
```

Internal logic:
1. If `this.SourceMapCollector` is null, throw or return empty (sourcemap was not enabled during `Generate()`).
2. Create a `SourceMapGenerator` with `Path.GetFileName(this.FilePath)` as the generated file name and `this.SourceRoot` as the source root.
3. Call `generator.Generate(this.SourceMapCollector, includeSourcesContent, sourceContentProvider)`.
4. Return the JSON string.

**`MessageCode` Additions**

Add to `Tyhp/Domain/Exceptions/MessageCode.cs` in the 5000s emitter range (Story 17's allocation is `5020–5022`):

- `EmitterSourceMapGenerationFailed = 5020`
- `EmitterSourceMapWriteFailed = 5021`
- `EmitterSourceMapInvalidMapping = 5022`

Add corresponding resource strings if the `.resx` infrastructure from Story 01 is available:
- `ERROR_TYHP5020` = `"Failed to generate source map for file '{0}': {1}"`
- `ERROR_TYHP5021` = `"Failed to write source map file '{0}': {1}"`
- `ERROR_TYHP5022` = `"Invalid source mapping in file '{0}': generated position ({1},{2}) references invalid source position ({3},{4})"`

### Acceptance Criteria

- `PHPOutputFile.SourceMap()` is fully implemented (replacing the `PLACEHOLDER_STORY_17` stub left by Story 09)
- `PHPOutputFile.SourceMap()` returns a valid Source Map v3 JSON string when called after `Generate()` with sourcemap enabled
- The returned JSON contains correct `file`, `sources`, and `mappings` fields
- If `Generate()` was called without sourcemap enabled, `SourceMap()` returns an appropriate result (empty string, or throws a descriptive exception)
- The `SourceFileName` property is settable on `PHPOutputFile`
- New `MessageCode` values are added (5020, 5021, 5022)
- File compiles without errors
- Existing emitter functionality is not broken — Story 09's `Generate()` still works without sourcemaps when the flag is disabled

### Dependencies

- **Requires:** Phases 1-4 (VLQ, SourceMapping, SourceMapCollector, SourceMapGenerator)
- **Requires:** Story 09 (Emitter) — `Generate()` must be implemented
- **Provides:** Sourcemap JSON for file writing (Phase 6)

---

## Phase 6: Source Map File Writer and Build Pipeline Integration

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Overview

Create the `SourceMapWriter` utility that writes `.map` files alongside PHP output files, optionally appends the `sourceMappingURL` comment to PHP files, and respects the `build.generateSourcemap` configuration option. Wire sourcemap generation into the `BuildAction` pipeline and the `TyhpEmitter` output writing step.

**Relationship to OutputWriterService:** Story 10 Phase 3 includes initial/placeholder sourcemap handling in `OutputWriterService`. Story 17 refactors this: the sourcemap logic is extracted OUT of `OutputWriterService` into the dedicated `SourceMapWriter` class. `OutputWriterService` should be updated to delegate all sourcemap operations to `SourceMapWriter`. Remove the duplicate sourcemap URL appending and .map file writing code from `OutputWriterService` and replace with calls to `SourceMapWriter`.

### Deliverables

- `Tyhp/TyhpLang/Emitter/SourceMap/SourceMapWriter.cs` — Writes `.map` files and manages sourcemap output
- (No changes to `Tyhp/Config/Project.cs` — sourcemap configuration is read from `BuildConfig.GenerateSourcemap`, defined in Story 10)
- Modified `Tyhp/CLI/BuildAction.cs` — Wire sourcemap generation into the build pipeline (or update the `// PLACEHOLDER_STORY_09` for emitter output)

### Implementation Details

**`SourceMapWriter.cs`**

Create in namespace `Tyhp.TyhpLang.Emitter.SourceMap`:

Static utility class for writing sourcemap output:

- `static void WriteSourceMapFile(string phpFilePath, string sourceMapJson, DiagnosticBag diagnostics)` — Write the source map JSON to `{phpFilePath}.map`. Handle IO errors by adding a diagnostic rather than throwing.

- `static string AppendSourceMappingUrl(string phpContent, string mapFileName)` — Append the `//# sourceMappingURL={mapFileName}` comment to the end of the PHP content string. The comment should be on its own line. If the PHP content already ends with a newline, append directly; otherwise, add a newline first. **Important:** This must be called **before** the PHP file is written to disk, since the URL comment needs to be part of the PHP file content. The `.map` file is written separately after the PHP file.

- `static string CreateInlineSourceMap(string sourceMapJson)` — Convert the JSON to a base64 data URL for inline sourcemaps: `//# sourceMappingURL=data:application/json;charset=utf-8;base64,{base64data}`. This is a lower-priority feature but simple to implement.

- `static void WriteAllSourceMaps(IEnumerable<PHPOutputFile> outputFiles, string outputDirectory, SourceMapOptions options, DiagnosticBag diagnostics)` — Batch operation that processes all output files:
  1. For each `PHPOutputFile` where sourcemap data is available:
     a. Generate the source map JSON via `file.SourceMap(options.IncludeSourcesContent, options.SourceContentProvider)`.
     b. Write the `.map` file via `WriteSourceMapFile()`.
     c. If `options.AppendSourceMappingUrl` is true, modify the PHP output to include the URL comment.
     d. If `options.InlineSourceMap` is true, embed the sourcemap as a data URL instead of a separate file.

**`SourceMapOptions`** (can be a nested class or separate small file):

- `bool Enabled { get; set; }` — master switch
- `bool IncludeSourcesContent { get; set; }` — embed original source in the map
- `bool AppendSourceMappingUrl { get; set; }` — append URL comment to PHP output (default: true)
- `bool InlineSourceMap { get; set; }` — embed as data URL instead of separate file (default: false)
- `Func<string, string?>? SourceContentProvider { get; set; }` — callback to read source file content

**Sourcemap Configuration**

Sourcemap configuration properties are defined in `BuildConfig` (added by Story 10 Phase 1). Access them via `project.Build.GenerateSourcemap` and `project.Build.SourceMapIncludeContent`. The `SourceMapIncludeContent` property is defined in `BuildConfig` (added by Story 10 Phase 1). Access it via `project.Build.SourceMapIncludeContent`.

**`BuildAction` Pipeline Integration**

The `BuildAction` (from Story 01) has placeholder steps for the emitter. When Story 09 implements the emitter and output writing, sourcemap generation should be wired in as follows:

After emitter produces `PHPOutputFile` instances and before/during file writing:

```
// In the emit/write step:
if (project.Build.GenerateSourcemap) {
    var sourceMapOptions = new SourceMapOptions {
        Enabled = true,
        IncludeSourcesContent = project.Build.SourceMapIncludeContent,
        AppendSourceMappingUrl = true,
        SourceContentProvider = (path) => File.Exists(path) ? File.ReadAllText(path) : null
    };
    SourceMapWriter.WriteAllSourceMaps(result.OutputFiles, outputDirectory, sourceMapOptions, result.Diagnostics);
}
```

If Story 09's emitter is not yet implemented, add this as:

```csharp
// PLACEHOLDER_STORY_09: Wire sourcemap generation after emitter produces output files
// When Story 09 emitter is complete, add:
// SourceMapWriter.WriteAllSourceMaps(result.OutputFiles, outputDir, sourceMapOptions, result.Diagnostics);
```

**File naming convention:**

| PHP Output File | Source Map File |
|----------------|-----------------|
| `output/App/Models/User.php` | `output/App/Models/User.php.map` |
| `output/App/index.php` | `output/App/index.php.map` |

The `.map` extension is appended to the full PHP filename (not replacing `.php`), following the widespread convention used by JavaScript/TypeScript sourcemaps.

### Acceptance Criteria

- `SourceMapWriter.WriteSourceMapFile()` writes a `.map` file alongside the PHP file
- `SourceMapWriter.AppendSourceMappingUrl()` correctly appends the comment to PHP content
- `SourceMapWriter.CreateInlineSourceMap()` produces a valid base64 data URL
- `SourceMapWriter.WriteAllSourceMaps()` processes a collection of output files
- IO errors during file writing produce diagnostics rather than exceptions
- `project.Build.GenerateSourcemap` configuration property exists (defined in `BuildConfig` by Story 10 Phase 1) and defaults to false
- `project.Build.SourceMapIncludeContent` configuration property exists (defined in `BuildConfig` by Story 10 Phase 1) and defaults to false
- Configuration values are read from `build:generateSourcemap` and `build:sourcemapIncludeContent` keys in `BuildConfig`
- The `BuildAction` has a placeholder or actual wiring for sourcemap generation in the emit step
- File compiles without errors
- No regressions in existing functionality

### Dependencies

- **Requires:** Phase 5 (`PHPOutputFile.SourceMap()` implemented)
- **Requires:** Story 01 (`DiagnosticBag`, `BuildAction`, `Project` config)
- **Provides:** Complete sourcemap output capability for the build pipeline

---

## Phase 7: Validation and End-to-End Verification

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Overview

Create validation logic to verify that generated sourcemaps are correct and complete. Implement a round-trip validation utility that checks VLQ encoding correctness, mapping coverage, and JSON format compliance. Establish manual and automated verification paths for the sourcemap pipeline.

### Deliverables

- `Tyhp/TyhpLang/Emitter/SourceMap/SourceMapValidator.cs` — Validation utility for generated sourcemaps
- End-to-end verification: a `.tyhp` file → emit → sourcemap → validate roundtrip

### Implementation Details

**`SourceMapValidator.cs`**

Create in namespace `Tyhp.TyhpLang.Emitter.SourceMap`:

Static utility class for validating sourcemaps:

- `static SourceMapValidationResult Validate(string sourceMapJson, string generatedContent, DiagnosticBag diagnostics)` — Perform comprehensive validation of a source map against its generated content.

**`SourceMapValidationResult`** (nested class or small separate file):

- `bool IsValid { get; }` — overall pass/fail
- `int TotalMappings { get; }` — number of mapping segments
- `int GeneratedLineCount { get; }` — number of lines in generated content
- `int MappedLineCount { get; }` — number of generated lines that have at least one mapping
- `double CoveragePercentage { get; }` — `MappedLineCount / GeneratedLineCount * 100`
- `List<string> Errors { get; }` — validation error descriptions
- `List<string> Warnings { get; }` — validation warning descriptions

**Validation checks:**

1. **JSON structure:** Parse the JSON and verify `version == 3`, `file` is present, `sources` is an array, `mappings` is a string.

2. **VLQ integrity:** Decode every segment in the `mappings` string. Verify that each segment has 1, 4, or 5 fields (per spec: a segment with only 1 field maps a generated column with no source; 4 fields map to a source position; 5 fields include a name).

3. **Generated line count:** Count the number of `;` separators in `mappings` (plus one for the first line). Verify this matches the number of lines in `generatedContent`.

4. **Source index bounds:** Every decoded source index must be within range `[0, sources.length)`.

5. **Name index bounds:** Every decoded name index must be within range `[0, names.length)`.

6. **Original line/column non-negative:** All decoded original line and column values must be >= 0.

7. **Generated column non-negative:** All decoded generated column values must be >= 0 (after applying relative decoding).

8. **Mapping coverage:** Warn if coverage is below a configurable threshold (e.g., 50% of generated lines should have at least one mapping).

9. **Source content consistency (if `sourcesContent` present):** If `sourcesContent` array is present and a `sourceContentProvider` is available, verify the content matches.

**Integration with build pipeline:**

The validator can optionally run after sourcemap generation in debug or CI builds. **Important:** `Generate()` must be called exactly once per output file. It both produces the PHP string and (when a `SourceMapCollector` is set) populates the collector; calling it again would re-run emission and double-count mappings into the same collector. The validator must therefore reuse the PHP content produced by that single `Generate()` call rather than invoking `Generate()` a second time:

```csharp
if (options.ValidateSourceMaps) {
    foreach (var file in outputFiles) {
        // Reuse the PHP string already produced by the build's single Generate() call
        // (e.g. cached when the build first generated this file). Do NOT call
        // file.Generate() again here — re-emitting would re-feed the SourceMapCollector
        // and double its mappings.
        string phpContent = cachedGeneratedContent[file]; // captured from the one Generate() call
        string mapJson = file.SourceMap(...);              // reads the already-populated collector
        var result = SourceMapValidator.Validate(mapJson, phpContent, diagnostics);
        if (!result.IsValid) {
            diagnostics.AddWarning(MessageCode.EmitterSourceMapInvalidMapping, file.FilePath, 0, 0, result.Errors.First());
        }
    }
}
```

**End-to-End Verification Path**

Until Story 07 (Testing Infrastructure) is complete, verification is manual:

1. Create a simple `.tyhp` test file with known content (e.g., a class with a method).
2. Run the build pipeline with `build.generateSourcemap = true`.
3. Verify the `.map` file is created alongside the `.php` file.
4. Parse the `.map` JSON and decode the VLQ mappings.
5. Verify specific known mappings:
   - The Tyhp class declaration line maps to the PHP class declaration line.
   - The Tyhp method declaration line maps to the PHP method declaration line.
   - Variable assignments map correctly.
6. Optionally use an external sourcemap visualization tool (e.g., [source-map-visualization](https://sokra.github.io/source-map-visualization/)) to inspect the mappings.

**Placeholder for automated tests:**

```csharp
// PLACEHOLDER_STORY_07: Add unit tests for VlqEncoder round-trip
// PLACEHOLDER_STORY_07: Add unit tests for SourceMapCollector position tracking
// PLACEHOLDER_STORY_07: Add unit tests for SourceMapGenerator JSON output
// PLACEHOLDER_STORY_07: Add integration test: .tyhp → emit → sourcemap → validate
```

### Acceptance Criteria

- `SourceMapValidator.Validate()` parses a sourcemap JSON and checks all specified validation rules
- VLQ decoding errors are caught and reported
- Out-of-bounds source/name indices are caught and reported
- Line count mismatch between mappings and generated content is caught
- Coverage percentage is computed correctly
- `SourceMapValidationResult` provides actionable error/warning messages
- The validator does not crash on malformed input (handles invalid JSON, truncated VLQ, etc.)
- An end-to-end verification path is documented (even if not automated yet)
- File compiles without errors

### Dependencies

- **Requires:** Phases 1-6 (complete sourcemap pipeline)
- **Provides:** Quality assurance for sourcemap output; foundation for automated tests in Story 07

---

## Cross-Cutting Concerns

### Performance Considerations

Sourcemap generation adds overhead to the emission process. To minimize impact:

1. **Only collect mappings when enabled.** The `emit(int indentLevel)` path (without collector) remains the fast path. The collector-based `emit(int indentLevel, SourceMapCollector collector)` is only called when `build.generateSourcemap` is configured.

2. **Use `StringBuilder` in the collector** for tracking position rather than allocating intermediate strings.

3. **Use `readonly record struct` for `SourceMapping`** to minimize GC pressure from the many small mapping objects.

4. **Pre-size collections** in `SourceMapCollector` based on estimated mapping count (e.g., number of AST nodes in the emit tree).

5. **VLQ encoding is cheap** — the algorithm is O(n) where n is the number of digits. No optimization needed beyond avoiding allocation (use `Span<char>` or `stackalloc` if profile shows it matters).

### Error Handling Strategy

All sourcemap-related errors should be non-fatal:

- If sourcemap generation fails for a file, log a warning diagnostic and continue. The PHP output file should still be written without the sourcemap.
- If `.map` file writing fails (permissions, disk full), log a warning diagnostic and continue.
- If VLQ encoding encounters an unexpectedly large value, log a warning and skip that mapping segment.
- The build should never fail solely because of a sourcemap error.

### Placeholder Convention

**Within this implementation plan** — for future phases:
```csharp
// PLACEHOLDER_PHASE_N: description
```

**Cross-story references** — for work from other TODO.md stories:
```csharp
// PLACEHOLDER_STORY_09: Wire sourcemap into emitter output writing
// PLACEHOLDER_STORY_07: Add sourcemap unit tests
// PLACEHOLDER_STORY_18: XDebug proxy uses sourcemap for breakpoint translation
```

### Relationship to Other Stories

| Story | Relationship |
|-------|-------------|
| **Story 09 (Emitter)** | Prerequisite. Story 09 rebuilds `PHPOutputFile` with `Generate()` and the class infrastructure, and leaves `SourceMap()` as a `PLACEHOLDER_STORY_17` stub. Sourcemap generation hooks into `EmitItem.emit()` and `PHPOutputFile.Generate()`. |
| **Story 10 (Build Action)** | Configuration wiring. `build.generateSourcemap` config option is read by `BuildAction` to enable/disable sourcemap generation. |
| **Story 07 (Testing)** | Testing. Automated tests for VLQ encoding, mapping collection, JSON generation, and end-to-end validation belong in Story 07. |
| **Story 18 (XDebug Proxy)** | Consumer. The XDebug Proxy reads `.map` files to translate breakpoints and stack traces between `.tyhp` and `.php` positions. |
| **Story 29 (Tyhp Reflection)** | Consumer. Sourcemap-backed runtime reflection loads `.php.map` files for source location mapping. Story 29 uses external `.map` files (not inline sourcemaps), but inline sourcemaps should be available as an option for environments where separate `.map` files are inconvenient. |

### File Size Summary

| File | Estimated Lines | Notes |
|------|----------------|-------|
| `SourceMap/VlqEncoder.cs` | ~120 | Self-contained algorithm |
| `SourceMap/SourceMapping.cs` | ~40 | Record struct |
| `SourceMap/SourceMapCollector.cs` | ~180 | Position tracking + mapping collection |
| `SourceMap/SourceMapGenerator.cs` | ~200 | JSON assembly with VLQ encoding |
| `SourceMap/SourceMapWriter.cs` | ~100 | File I/O and URL comment |
| `SourceMap/SourceMapValidator.cs` | ~200 | Validation logic |
| `EmitItem.cs` (additions) | ~80 | New emit overload added to existing ~130 lines |
| `PHPOutputFile.cs` (additions) | ~50 | SourceMap() implementation added to existing ~83 lines |
| `MessageCode.cs` (additions) | ~5 | Three new error codes |

All new files are well under the 500-800 line guideline.

---

*Last updated: 2026-03-23 — Source: TODO.md Story 17*

---

## Human Testing and Verification

> **Note:** These steps are for a human developer to manually verify the sourcemap generation pipeline works end-to-end. Steps can be skipped, reordered, or adapted as needed. All commands assume you are in the project root directory and the project builds successfully.

### Step 1: Verify Sourcemap Generation Is Configurable

Check that the `build.generateSourcemap` configuration option works. Create or update your `tyhp.json` to enable sourcemaps:

```json
{
    "build": {
        "generateSourcemap": true
    }
}
```

Run a build:

```bash
dotnet run -- build
```

Expected:
- For each generated `.php` file in the output directory, a corresponding `.php.map` file should exist alongside it
- For example, `output/App/Models/User.php` should have `output/App/Models/User.php.map`

Now set `generateSourcemap` to `false` and rebuild:

```json
{
    "build": {
        "generateSourcemap": false
    }
}
```

```bash
dotnet run -- build
```

Expected:
- No `.php.map` files are generated
- The PHP output files do not contain a `//# sourceMappingURL=...` comment at the end

### Step 2: Verify `.map` File Contains Valid JSON

With sourcemaps enabled, inspect a generated `.map` file:

```bash
python3 -m json.tool output/App/Models/User.php.map
```

Expected:
- The file parses as valid JSON
- The JSON object contains:
  - `"version": 3`
  - `"file"` — the name of the generated PHP file (e.g., `"User.php"`)
  - `"sources"` — an array of `.tyhp` source file paths
  - `"names"` — an array of symbol names (may be empty)
  - `"mappings"` — a string of VLQ-encoded segments separated by `;` and `,`
- Optionally `"sourceRoot"` and `"sourcesContent"` depending on configuration

### Step 3: Verify sourceMappingURL Comment in PHP Output

With sourcemaps enabled, inspect the end of a generated PHP file:

```bash
tail -3 output/App/Models/User.php
```

Expected:
- The last line (or second-to-last if there's a trailing newline) should be:
  ```
  //# sourceMappingURL=User.php.map
  ```
- The URL is relative (just the filename), not an absolute path

### Step 4: Verify VLQ Encoding Round-Trip

This step validates the VLQ encoder by checking specific known values. If you have access to a C# REPL or can write a quick test script, verify:

| Input Value | Expected VLQ Base64 |
|-------------|-------------------|
| 0           | `A`               |
| 1           | `C`               |
| -1          | `D`               |
| 5           | `K`               |
| 16          | `gB`              |
| -16         | `hB`              |
| 100         | `oG`              |

You can verify by decoding the `mappings` string from a generated `.map` file using an online Source Map visualizer (e.g., https://sokra.github.io/source-map-visualization/) or a JavaScript-based decoder:

```bash
node -e "
const fs = require('fs');
const map = JSON.parse(fs.readFileSync('output/App/Models/User.php.map', 'utf8'));
console.log('Version:', map.version);
console.log('File:', map.file);
console.log('Sources:', map.sources);
console.log('Mappings (first 100 chars):', map.mappings.substring(0, 100));
console.log('Line count (semicolons + 1):', map.mappings.split(';').length);
"
```

### Step 5: Verify Mapping Line Count Matches Generated PHP

Count the number of lines in the generated PHP file and compare with the sourcemap:

```bash
wc -l < output/App/Models/User.php
```

```bash
node -e "
const map = JSON.parse(require('fs').readFileSync('output/App/Models/User.php.map', 'utf8'));
console.log('Sourcemap line count:', map.mappings.split(';').length);
"
```

Expected: The number of `;`-separated groups in the `mappings` string (plus one) should equal the number of lines in the generated PHP file.

### Step 6: Verify Source Content Embedding

Enable source content embedding in `tyhp.json`:

```json
{
    "build": {
        "generateSourcemap": true,
        "sourcemapIncludeContent": true
    }
}
```

Rebuild and inspect the `.map` file:

```bash
dotnet run -- build
python3 -c "
import json
with open('output/App/Models/User.php.map') as f:
    m = json.load(f)
print('Has sourcesContent:', 'sourcesContent' in m)
if 'sourcesContent' in m:
    for i, src in enumerate(m['sources']):
        content = m['sourcesContent'][i]
        print(f'  {src}: {len(content)} chars, starts with: {repr(content[:30])}')
"
```

Expected:
- The `sourcesContent` array is present and contains the original `.tyhp` source code for each entry in `sources`
- The content should start with `<?tyhp` (or whatever the source file starts with)

### Step 7: Verify Sourcemap Validator (If Implemented)

If the `SourceMapValidator` was integrated into the build pipeline (debug/CI builds), check for any sourcemap-related warnings in the build output:

```bash
dotnet run -- build 2>&1 | grep -i "sourcemap\|source.map\|TYHP502"
```

Expected:
- No `TYHP5020` (generation failed), `TYHP5021` (write failed), or `TYHP5022` (invalid mapping) errors
- If validation warnings appear, they should describe the issue clearly

### Step 8: Verify Inline Sourcemap Support (Optional)

If inline sourcemaps are supported, test them by configuring the project (the exact config key may vary):

Expected behavior for inline sourcemaps:
- No separate `.php.map` file is created
- The PHP file ends with a `//# sourceMappingURL=data:application/json;charset=utf-8;base64,...` comment
- The base64 content decodes to the same JSON as the external `.map` file

### Step 9: Verify Sourcemap Works with an External Tool

Use a sourcemap visualization tool to verify the mappings are meaningful:

1. Open https://sokra.github.io/source-map-visualization/ in a browser
2. Paste the generated PHP content into the "Generated" field
3. Upload or paste the `.map` file content
4. Verify that:
   - Hovering over generated PHP code highlights the corresponding original `.tyhp` source
   - Class declaration lines map back to the Tyhp class declaration
   - Method declaration lines map back to the Tyhp method declaration
   - Variable assignments map correctly

### Step 10: Verify Optimizer-Transformed Code Maps Correctly

If the optimizer (Story 23) has inlined extension methods or folded constants, verify that the sourcemap maps the optimized output back to the **original** call site in the Tyhp source, not to the extension method body:

1. Write a `.tyhp` file that uses an extension method or operator overload
2. Build with both optimizer and sourcemaps enabled
3. In the sourcemap visualization, find the inlined code in the PHP output
4. Verify it maps back to the original operator expression (e.g., `$a + $b`) in the `.tyhp` source

### Step 11: Verify Build Doesn't Fail on Sourcemap Errors

Sourcemap errors should be non-fatal. To test resilience:
- The build should succeed even if a `.map` file cannot be written (e.g., permissions issue)
- The build should produce a warning diagnostic (`TYHP5021`) but still emit the PHP files
- The PHP output file should still be usable without the sourcemap

### Step 12: Cleanup

Remove test configuration changes:

```bash
# Restore tyhp.json to its original state (remove sourcemap config if it was added)
```

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
