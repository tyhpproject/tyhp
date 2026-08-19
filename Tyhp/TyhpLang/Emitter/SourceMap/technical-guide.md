# Emitter SourceMap — Technical Guide

This guide covers sourcemap support under `Tyhp/TyhpLang/Emitter/SourceMap/` and the tracking `EmitItem.emit` overload that feeds `SourceMapCollector`. It is grounded in the code in this folder, `EmitItem.cs`, `PHPOutputFile.cs`, `TyhpEmitter.GenerateAll`, `OutputWriterService`, and the tests under `tests/Tyhp.Tests/Emitter/` plus `tests/Tyhp.Tests/CLI/OutputWriterServiceTests.cs`. Prefer this document for VLQ encoding, `SourceMapping`, `SourceMapCollector`, `SourceMapGenerator`, `SourceMapWriter`, `SourceMapValidator`, tracking emission, and `PHPOutputFile.SourceMap()`; the parent `Emitter/technical-guide.md` only maps the folder.

Sourcemaps follow [Source Map Revision 3](https://github.com/tc39/source-map-spec/blob/main/source-map-rev3.md). They map positions in generated `.php` back to originating `.tyhp` start positions (`Line` / `Column` on AST nodes). End positions are not used.

---

## What exists today

| File | Role |
| --- | --- |
| `VlqEncoder.cs` | Static VLQ Base64 encode/decode used by Source Map v3 `mappings` strings |
| `SourceMapping.cs` | One generated → original mapping segment (`readonly record struct`) |
| `SourceMapCollector.cs` | Position tracker and mapping accumulator used during emission |
| `SourceMapGenerator.cs` | Assembles Source Map v3 JSON from a collector (`internal`) |
| `SourceMapWriter.cs` | Writes `.map` files, `sourceMappingURL` comments, and inline data URLs (`internal`) |
| `SourceMapValidator.cs` | Validates Source Map v3 JSON against generated PHP (`internal`) |

`PHPOutputFile.SourceMap()` is implemented. `.map` file writing is `SourceMapWriter`, called from `OutputWriterService` (not the legacy `OutputFileWriter`). `EmitItem.emit(int, SourceMapCollector)` reports fragments to a collector; `PHPOutputFile.Generate` uses that overload when `SourceMapCollector` is set. `TyhpEmitter.GenerateAll` assigns a collector (and a `SourceRoot` prefix of the registered source path) when `project.Build.GenerateSourcemap` is true.

Visibility split (intentional):

- `VlqEncoder`, `SourceMapGenerator`, `SourceMapWriter`, and `SourceMapValidator` are `internal` — algorithm / I/O / QA helpers, same as `TypeSpellingHelper`.
- `SourceMapping` and `SourceMapCollector` are `public` — they are the output-file data surface (`PHPOutputFile.SourceMapCollector`).

---

## `VlqEncoder`

Namespace: `Tyhp.TyhpLang.Emitter.SourceMap`. Internal static class (matches other self-contained emitter algorithm helpers, e.g. `TypeSpellingHelper`, `OperatorMethodNameGenerator`; visible to `Tyhp.Tests` via `InternalsVisibleTo`). No emitter, AST, or I/O dependencies.

### API

| Method | Behavior |
| --- | --- |
| `Encode(int value)` | One signed integer → VLQ Base64 string |
| `Encode(int[] values)` | Concatenate encodings with no separator (VLQ is self-delimiting) |
| `Decode(string vlq, ref int offset)` | One value starting at `offset`; advances `offset` past consumed characters |
| `DecodeSegment(string vlq)` | Decode the whole string into `int[]` |

### Algorithm

1. Convert the signed integer to unsigned sign-magnitude: LSB is the sign (0 = non-negative, 1 = negative); remaining bits are the absolute value.
2. Split into 5-bit groups from least significant.
3. Set continuation bit 6 (value 32) on every group except the last.
4. Map each 6-bit digit through `ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/`.

`int.MinValue` and `int.MaxValue` both round-trip: encoding uses `ulong` intermediates so `|int.MinValue| << 1` does not overflow `uint`.

### Errors

| Input | Exception |
| --- | --- |
| Null `string` / `int[]` | `ArgumentNullException` |
| `offset` outside the string (including empty input at offset 0) | `ArgumentOutOfRangeException` |
| Character not in the Base64 VLQ alphabet | `FormatException` |
| Continuation bit set with no further characters | `FormatException` |
| Decoded magnitude does not fit in `int` | `FormatException` |

`DecodeSegment("")` returns an empty array. `Encode([])` returns `""`.

### Known encodings (spec / Story 17 table)

| Value | VLQ |
| --- | --- |
| 0 | `A` |
| 1 | `C` |
| -1 | `D` |
| 5 | `K` |
| -5 | `L` |
| 15 | `e` |
| 16 | `gB` |
| -16 | `hB` |
| 100 | `oG` |
| 1000 | `w+B` |

`1000 → w+B` is the Source Map v3 sign-magnitude encoding (`1000 << 1` = 2000 → 5-bit groups 16, 30, 1). Some write-ups (including the Story 17 plan table) list `gxB`, which decodes to 784, not 1000.

### Tests

`tests/Tyhp.Tests/Emitter/VlqEncoderTests.cs` — known encodings, round-trip `[-10000, 10000]`, concatenated segments, `int` extremes, and invalid-input exceptions.

---

## `SourceMapping`

Namespace: `Tyhp.TyhpLang.Emitter.SourceMap`. Public `readonly record struct` so the many per-file mapping entries stay stack-friendly.

| Field | Meaning |
| --- | --- |
| `GeneratedLine` | 0-based line in the generated PHP |
| `GeneratedColumn` | 0-based column in the generated PHP |
| `SourceIndex` | Index into the collector's `sources` array |
| `OriginalLine` | 0-based line in the original `.tyhp` |
| `OriginalColumn` | 0-based column in the original `.tyhp` |
| `NameIndex` | Optional index into the collector's `names` array; `null` when the segment is unnamed |

AST `Line` is 1-based; AST `Column` is already 0-based (ANTLR). The collector converts line as `provider.Line - 1` before constructing a `SourceMapping`. End positions are not stored.

---

## `SourceMapCollector`

Namespace: `Tyhp.TyhpLang.Emitter.SourceMap`. Public sealed class. No I/O and no VLQ encoding — it only tracks generated coordinates and stores `SourceMapping` values. `EmitItem.emit(int, SourceMapCollector)` passes fragments into one of these. `PHPOutputFile.Generate` does the same when `PHPOutputFile.SourceMapCollector` is set.

### Construction

```csharp
public SourceMapCollector(string? defaultSourceFileName = null, int estimatedMappingCount = 256)
```

A non-empty `defaultSourceFileName` is registered immediately as source index 0 (one PHP file from one `.tyhp` file). `estimatedMappingCount` pre-sizes the mapping list.

### Position

Generated coordinates start at `(0, 0)`. `CurrentGeneratedLine` / `CurrentGeneratedColumn` are the cursor **after** the last `AddContent` / `AddNewLine`.

### `AddContent(content, provider, name = null)`

1. If `provider` is non-null, `Line > 0` (AST lines are 1-based; `-1` is the "unknown" sentinel and `0` is never a real line — matches the `Line > 0` validity check used elsewhere, e.g. `AliasConverter.cs`), and `Column >= 0` (already 0-based):
   - Resolve `SourceIndex` from `provider.OwningFile.FileName` (registering the path if new). When `OwningFile` is missing, use index `0` (the default file, if any).
   - Convert original line from 1-based AST to 0-based sourcemap (`Line - 1`); keep `Column` as-is.
   - If `name` is non-empty, `RegisterName` it and store `NameIndex`; otherwise `NameIndex` is `null`.
   - Record a `SourceMapping` at the **current** generated position (start of this fragment).
2. Scan `content` for `\n` only (`\r` is a normal column character). Each `\n` increments the generated line and resets the column to 0. After the last `\n` (or if there is none), the column is the UTF-16 length of the suffix after that newline, **added** to the current column when there was no newline so consecutive same-line fragments accumulate.

A null provider still advances position (indent, inter-fragment newlines, file preamble) but records no mapping. `Line <= 0` or `Column == -1` (unknown AST span) is treated the same as a null provider for mapping purposes.

Multi-line `content` produces **one** mapping at the fragment start, not one per generated line.

### Other API

| Method | Behavior |
| --- | --- |
| `AddNewLine()` | `line++`, column `0`; no mapping (same as `AddContent("\n", null)`) |
| `SetPosition(line, column)` | Force the current position directly; records no mapping and does not touch any collected mapping. For a caller that already reported content via `AddContent` and then discovers it must discard some of that content before writing it out (e.g. `PHPOutputFile.Generate` trimming trailing whitespace from an already-tracked body) and needs the cursor to match what was actually written. Throws `ArgumentOutOfRangeException` for a negative line or column. |
| `RegisterSourceFile(path)` | Idempotent; first path is index 0, second is 1, … Ordinal keys. Blank/null throws `ArgumentException`. |
| `RegisterName(name)` | Same for the `names` array |
| `GetMappings()` | Snapshot sorted by generated line then column (stable — ties keep recording order, e.g. a parent fragment before a nested child fragment at the same position). Later `AddContent` calls do not mutate a previously returned list. |
| `GetSourceFiles()` / `GetNames()` | Snapshots in registration order |
| `Reset()` | Clears mappings, names, sources, and position. Re-registers the constructor default source file when one was supplied. |

Source Map v3 already supports one generated file → many sources via `SourceIndex`. A PHP file that contains code from several `.tyhp` files just registers each `OwningFile.FileName` as it appears.

### Tests

`tests/Tyhp.Tests/Emitter/SourceMapCollectorTests.cs` — field storage, origin mapping `(line 1, col 0) → (0, 0)`, newline position tracking, null/invalid providers, source-file index stability (including `OwningFile`), name indices, `Reset`, `SetPosition`, and snapshot isolation.

---

## `EmitItem.emit(int, SourceMapCollector)`

Defined on `EmitItem` in `Tyhp/TyhpLang/Emitter/EmitItem.cs` (not in this folder). The existing `emit(int indentLevel)` path is unchanged and remains the fast path used by `PHPOutputFile.Generate` when `SourceMapCollector` is null.

```csharp
public string emit(int indentLevel, SourceMapCollector collector)
```

Returns the same PHP string as `emit(indentLevel)`. While building that string it reports fragments in write order:

| Fragment | Provider passed to `AddContent` |
| --- | --- |
| Indent (`"    "` × `indentLevel`) | `null` |
| `\n` between segments (including the PSR-12 blank line after a trait-use group) | `null` |
| Each content line from `StartContent` / `EndContent` | `this.Provider` |
| Nested children | The child's own `emit(indentLevel + 1, collector)` (child's `Provider` on its content lines) |

Content pieces are normalized `\r\n` / `\r` → `\n` and split into lines the same way as `AppendIndentedContent`, so indented multiline fragments (closures, `match`, switch) get emit indent (null provider) then the line (mapped). Spaces already inside the fragment stay part of the mapped content; they are not extra indent. Blank lines inside a piece emit as an empty segment (join `\n` only; no indent, no mapping).

Whitespace-only children are detected with the non-tracking `emit(indentLevel + 1)` peek and skipped without calling the collector overload — otherwise their column advances would not appear in the returned string.

A mapping is recorded only when the provider has a known start (`Line > 0`, `Column >= 0`). Empty items and skipped children produce no mapping even if their `Provider` is valid, because no content fragment is reported.

`tests/Tyhp.Tests/Emitter/EmitItemSourceMapTests.cs` — string equality with `emit(int)`, nested generated positions (mappings start after indent), newline-count vs `CurrentGeneratedLine`, skipped empty/whitespace children, CRLF normalization, trait-use blank lines, and `OwningFile` source indices.

---

## `SourceMapGenerator`

Namespace: `Tyhp.TyhpLang.Emitter.SourceMap`. Internal sealed class. Turns a populated collector into a compact Source Map v3 JSON string via `System.Text.Json.Nodes.JsonObject` (so source content with quotes, backslashes, and newlines is escaped correctly). Uses `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` so VLQ `+` / `/` in `mappings` are not rewritten as `\u002B`.

```csharp
internal sealed class SourceMapGenerator
{
    public SourceMapGenerator(string generatedFileName, string? sourceRoot = null)
    public string Generate(
        SourceMapCollector collector,
        bool includeSourcesContent = false,
        Func<string, string?>? sourceContentProvider = null)
}
```

`generatedFileName` is stored as the JSON `file` field (typically `Path.GetFileName` of the PHP output). `sourceRoot` is stored as JSON `sourceRoot` (empty string when omitted).

### JSON shape

| Field | When present |
| --- | --- |
| `version` | Always `3` (JSON number) |
| `file` | Always the constructor file name |
| `sourceRoot` | Always; empty string when the constructor argument was null |
| `sources` | Always; registration order, paths relative to `sourceRoot` when that prefix applies |
| `sourcesContent` | Only when `includeSourcesContent` is true (omitted otherwise) |
| `names` | Always; registration order (may be empty) |
| `mappings` | Always; VLQ string, possibly empty |

`sourceContentProvider` is invoked only when embedding content, once per original registered source path (not the relativized `sources` entry). A null return becomes a JSON `null` at that index. A null provider with `includeSourcesContent: true` yields an array of nulls.

Path relativization: if `sourceRoot` is set, a source path that lives under that directory (filesystem `GetFullPath` prefix) or that already starts with the `sourceRoot` string prefix is stored relative to it, using `/` separators. Unrelated paths are left as registered. Consumers prepend `sourceRoot` to `sources` entries.

### `mappings` encoding

Segments are 4 VLQ fields (generated column, source index, original line, original column) or 5 when `NameIndex` is set. Values are **relative** to the previous segment across the whole file, except `previousGeneratedColumn`, which resets to 0 at the start of each generated line. Lines are separated by `;`; segments on one line by `,`. Empty generated lines between mapped lines are empty groups (`;;`). The string covers generated line 0 through the last mapped line; it is not padded to `CurrentGeneratedLine`.

`EncodeMappingSegment` updates the running `previous*` state after computing deltas. Unnamed segments do not encode or update `previousNameIndex`.

### Tests

`tests/Tyhp.Tests/Emitter/SourceMapGeneratorTests.cs` — JSON shape, `AAAA` origin mapping, empty-line `;;`, comma-separated same-line segments, named 5-field segments, relative VLQ round-trip (including `1000 → w+B` through JSON), `sourcesContent` embed/omit/null, escaping, and `sourceRoot` relativization (provider still receives the original path).

---

## `PHPOutputFile.SourceMap()` and tracking `Generate()`

Defined on `PHPOutputFile` in `Tyhp/TyhpLang/Emitter/PHPOutputFile.cs`. There is exactly one collector reference: the public `SourceMapCollector` property (no private duplicate field).

| Property | Role |
| --- | --- |
| `SourceMapCollector` | Non-null before `Generate()` ⇒ tracking emit. `null` ⇒ fast `emit(int)` path. `SourceMap()` reads this same instance. |
| `SourceFileName` | Original `.tyhp` path. Set by `FromAstTree` / `PHPOutputFileSplitter` from `SrcFileAst.FileName`. |
| `SourceRoot` | Passed through to `SourceMapGenerator` as JSON `sourceRoot`. |

```csharp
public string SourceMap(
    bool includeSourcesContent = false,
    Func<string, string?>? sourceContentProvider = null)
```

When the collector is null, returns `""` (sourcemap was not enabled during `Generate()`). Otherwise constructs `SourceMapGenerator(Path.GetFileName(OutputFilePath), SourceRoot)` and returns its JSON.

Tracking `Generate()`:

1. **Reset first.** If a collector is set, `Generate()` calls `Reset()` before anything else, then `RegisterSourceFile(SourceFileName)` when the name is non-empty. A second `Generate()` (duplicate output paths / merge-then-re-emit) therefore cannot append onto a stale cursor or duplicate mappings. `RegisterSourceFile` is idempotent with the constructor default source.
2. Preamble (`<?php`, file docblock, `declare`, namespace, `use`, autoloader `require_once`) is reported with a **null** provider.
3. The body is peeked once **without** the collector (`EmitBody(..., collector: null)`) purely to decide whether the PSR-12 blank-line separator before the body is needed. That separator, when needed, is appended (to `sb` and the collector) *before* the tracked body pass — appending it after would leave the collector's cursor one line behind where the body text actually lands, shifting every body mapping up by one line. The body is then emitted for real via `emit(indentLevel, collector)` on each item; `\n\n` between top-level declarations is also null-provider. Prune-time `EmitBody` calls do **not** pass the collector.
4. `Generate()` still calls `body.TrimEnd()` before writing it (trailing whitespace should not reach the file). Because the collector already advanced through that trailing whitespace while tracking, `Generate()` recomputes the correct end position from the trimmed text (starting from the position captured just before the tracked body pass) and applies it via `SourceMapCollector.SetPosition` — trimmed content is always pure whitespace, so no mapping is ever affected, only the cursor.

`TyhpEmitter.GenerateAll` assigns tracking state when `context.Project?.Build.GenerateSourcemap` is true: a fresh empty `SourceMapCollector` and `SourceRoot ??= SourceMapWriter.SourceRootPrefixFor(SourceFileName)` (the directory prefix of the registered path, e.g. `src/` for `src/App.tyhp`). `Generate()` then registers `SourceFileName`. Tests and callers without a `Project` leave the collector null (fast path).

`PHPOutputFile.Merge` reconciles sourcemap fields: keeps this file's `SourceFileName` / `SourceRoot` when set (otherwise takes the other's), and if either file had a collector, replaces it with a fresh empty collector so a later `Generate()` still tracks without inheriting pre-merge mappings.

### `SourceRoot` vs registered paths

`SourceMapGenerator.RelativizeSourcePath` only strips a prefix when the registered path is under `sourceRoot` as a filesystem directory (`Path.GetFullPath`) **or** already starts with that string prefix. Those two strategies do not treat URL-style roots relative to the output file.

Typical mismatch: `SourceRoot = "../src/"` (from the `.php` directory) with collector paths `src/App.tyhp` (project-relative) or an absolute path. Neither strategy matches, so `sources` stays `src/App.tyhp`. Pair them instead:

- String prefix: `SourceRoot = "src/"`, `SourceFileName = "src/App.tyhp"` → `sources: ["App.tyhp"]`
- Filesystem: `SourceRoot` = absolute source directory, `SourceFileName` = a file under that directory

`SourceMapWriter.SourceRootPrefixFor` implements the string-prefix pairing (directory of the registered path, with a trailing `/`). The pipeline never derives `../src/` from the output directory.

### Diagnostics

`MessageCode` 5020–5022 are non-fatal **warnings** emitted by `SourceMapWriter` (never fail the build by themselves):

| Code | Name | When |
| --- | --- | --- |
| 5020 | `EmitterSourceMapGenerationFailed` | `PHPOutputFile.SourceMap()` throws while producing JSON |
| 5021 | `EmitterSourceMapWriteFailed` | Writing `{phpFilePath}.map` hits IO / access errors |
| 5022 | `EmitterSourceMapInvalidMapping` | A collected mapping has a negative coordinate, or `SourceIndex` / `NameIndex` is out of range of `sources` / `names` |

A mapping with `SourceIndex` 0 and an empty `sources` array (collector never received `RegisterSourceFile`, typical when `SourceFileName` is unset and providers have no `OwningFile`) is reported as 5022; JSON is still produced.

### Tests

`tests/Tyhp.Tests/Emitter/PHPOutputFileSourceMapTests.cs` — empty `SourceMap()` without a collector, `SourceFileName` from `FromAstTree`, tracking vs fast-path PHP equality, v3 JSON shape, unmapped preamble, `sourcesContent` original-path provider, filesystem vs URL-style `sourceRoot`, 5020–5022 allocation, collector position staying in sync after trailing-whitespace trim, body mappings aligning with actual lines when a namespace preamble separator is present, `Generate()` twice not duplicating mappings, `Merge` reconciling collector / `SourceFileName` / `SourceRoot`, and `TyhpEmitter.GenerateAll` assigning a collector when `build.generateSourcemap` is true.

---

## `SourceMapWriter`

Namespace: `Tyhp.TyhpLang.Emitter.SourceMap`. Internal static class plus `SourceMapOptions`. This is the only sourcemap I/O surface; `OutputWriterService` delegates to it (it no longer appends URL comments or writes `.map` files itself).

`SourceMapOptions`: `Enabled`, `IncludeSourcesContent`, `AppendSourceMappingUrl` (default true), `InlineSourceMap` (default false), `SourceContentProvider`, `ValidateSourceMaps` (default false; the live writer sets it true when `GenerateSourcemap` is on).

| Method | Behavior |
| --- | --- |
| `WriteSourceMapFile(phpFilePath, json, diagnostics)` | Writes `{phpFilePath}.map` as UTF-8 without BOM. IO failures → warning 5021, no throw. Creates the parent directory if needed. |
| `AppendSourceMappingUrl(phpContent, mapFileName)` | Appends `//# sourceMappingURL={mapFileName}` on its own line using `\n`. Adds a leading newline only when the PHP does not already end with one. Leaves content unchanged if a `sourceMappingURL=` comment is already present. |
| `CreateInlineSourceMap(json)` | Returns `//# sourceMappingURL=data:application/json;charset=utf-8;base64,{base64}`. |
| `TryGenerateSourceMapJson(file, options, diagnostics)` | Reports 5022 for invalid mappings, then calls `file.SourceMap(includeSourcesContent, provider)`. Empty collector (null property) → null (no map). Exceptions → warning 5020 and null. |
| `ApplySourceMappingComment(php, phpPath, json, options)` | Inline data URL when `InlineSourceMap`; otherwise the external-file comment when `AppendSourceMappingUrl`. |
| `WriteAllSourceMaps(files, outputDirectory, options, diagnostics)` | No-op when `Enabled` is false. For each file with JSON: optionally validates (when `ValidateSourceMaps`) against the existing `GeneratedContent`, mutates `GeneratedContent` with the comment, and writes a `.map` unless inlining. Relative `OutputFilePath` is combined with `outputDirectory`. |
| `SourceRootPrefixFor(path)` | Directory prefix of a registered source path (`src/App.tyhp` → `src/`). |
| `CreateFileContentProvider(projectPath)` | Resolves project-relative registered paths against `projectPath` for `sourcesContent` embedding. |

**Write order in `OutputWriterService`:** generate JSON → append `sourceMappingURL` to the in-memory PHP string → write the `.php` file → write `{php}.map` (skipped for inline maps and dry-run). Map write failures do not roll back the PHP file and do not use `BuildFileWriteError` (that stays for PHP writes).

Config: `project.Build.GenerateSourcemap` (`build:generateSourcemap`, default false) is the master switch for both collector assignment and writing. `project.Build.SourceMapIncludeContent` (`build:sourcemapIncludeContent`, default false) is passed through as `IncludeSourcesContent`. There is no `tyhp.json` key for inline maps; `InlineSourceMap` is available on `SourceMapOptions` for callers that want it.

`tests/Tyhp.Tests/Emitter/SourceMapWriterTests.cs` — file write, 5021 on IO failure, URL append, inline data URL, `WriteAllSourceMaps`, 5020 on provider throw, 5022 on out-of-range `SourceIndex`, `SourceRootPrefixFor`, content provider. `tests/Tyhp.Tests/CLI/OutputWriterServiceTests.cs` — no dangling URL when the collector is null, `.map` + URL when tracking is on, merge-then-re-`Generate` cursor sync, `sourcesContent` embedding from a project-relative path.

---

## `SourceMapValidator`

Namespace: `Tyhp.TyhpLang.Emitter.SourceMap`. Internal static class plus `SourceMapValidationResult`. Round-trip QA for a Source Map v3 JSON string against the PHP it describes. Does not throw on malformed JSON, truncated VLQ, or illegal segment widths — those become `result.Errors`. Null arguments throw `ArgumentNullException` (same contract as the writer).

```csharp
internal static SourceMapValidationResult Validate(
    string sourceMapJson,
    string generatedContent,
    DiagnosticBag diagnostics,
    double coverageThreshold = 50.0,
    Func<string, string?>? sourceContentProvider = null)
```

`Generate()` must be called exactly once per output file. The validator reuses that PHP string; it never calls `Generate()` itself. `OutputWriterService` validates **before** appending `sourceMappingURL`, so the extra comment line is not in the content. When validating on-disk PHP (which does contain the comment), the validator strips any line containing `sourceMappingURL=` before counting lines.

### Line count vs unpadded `mappings`

The generator writes mapping groups from generated line 0 through the **last mapped line**. It does not pad with trailing `;` for unmapped PHP after that (blank line at EOF, etc.). The `sourceMappingURL` comment is also extra and unmapped.

The validator therefore:

- Counts PHP lines after stripping `sourceMappingURL` comments (`CountLines`: empty → 0; otherwise 1 + number of `\n`).
- Treats `mappings` group count (`split(';').Length`, or 0 when the string is empty) **exceeding** that PHP line count as an error.
- Treats fewer groups than PHP lines as **coverage** (trailing unmapped lines), not a structural mismatch.
- Empty groups between mapped lines (`;;`) are valid and count as unmapped lines for coverage.

### Other checks

| Check | Failure |
| --- | --- |
| JSON object with `version === 3`, `file` string, `sources` array, `mappings` string | Error |
| Each VLQ segment has 1, 4, or 5 fields | Error (2/3-field illegal) |
| Decoded generated column ≥ 0 | Error |
| Decoded source index in `[0, sources.length)` (4/5-field only) | Error |
| Decoded name index in `[0, names.length)` (5-field only) | Error |
| Decoded original line/column ≥ 0 | Error |
| Mapping generated line past the PHP line count | Error |
| `sourcesContent` length matches `sources`; optional provider content matches (tries the `sources` entry, then `sourceRoot` + entry) | Error |
| `MappedLineCount / GeneratedLineCount * 100` below `coverageThreshold` (default 50%) | **Warning** only — `IsValid` stays true |

`IsValid` is `Errors.Count == 0`. Coverage shortfalls never fail validation. Preamble (`<?php`, docblock, `declare`, namespace, `use`, blank lines) is unmapped, so small files often sit under 50%; that is expected.

Structural failures (invalid JSON, bad VLQ, illegal field counts, mappings past the file) add warning `TYHP5022`. Mapping-level index/position failures are recorded on `result.Errors` only — `SourceMapWriter.TryGenerateSourceMapJson` already reports those as 5022 from the collector, so the validator does not double-emit them.

### Pipeline

`OutputWriterService.WriteAll` sets `SourceMapOptions.ValidateSourceMaps` when `build.generateSourcemap` is true and calls `Validate` on the JSON + pre-URL PHP. `SourceMapWriter.WriteAllSourceMaps` does the same when the option is set, still using `GeneratedContent` from the single `Generate()` call (before it mutates that string with the URL comment).

### Tests

`tests/Tyhp.Tests/Emitter/SourceMapValidatorTests.cs` — JSON shape, VLQ errors, field counts, 1-field segments, index/position bounds, unpadded trailing lines, `sourceMappingURL` stripping, empty-line `;;`, coverage warnings, `sourcesContent` match/mismatch.

`tests/Tyhp.Tests/Emitter/SourceMapEndToEndTests.cs` — `.tyhp` → emit → validate (class / method / assignment lines), and a full `TestProjectBuilder` build that writes `.php` + `.php.map` and validates the on-disk files (including the URL comment).

---

## Open questions

1. **Optimizer `OriginalAst`** — Story 23's `OriginalAst` is not on AST nodes yet. Tracking emit reports `this.Provider` as-is; inlined/folded nodes should pass the original call-site node (or the collector should be taught to follow `OriginalAst`) when that property exists.
