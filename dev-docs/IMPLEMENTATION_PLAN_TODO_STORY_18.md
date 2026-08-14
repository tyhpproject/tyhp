# Implementation Plan: Story 18 — XDebug Proxy

> **Roadmap position:** Story 18 — **Tier 2 — DX & Ecosystem**
> **Direct dependencies (new numbering):** 17
> **Renumbered from:** legacy Story 14
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Story:** 14 — XDebug Proxy
> **Depends on:** Story 17 (Sourcemap Generation)
> **Key files:** New directory `Tyhp/XDebugProxy/`, `Tyhp/CLI/TyhpHostedService.cs`, `Tyhp/Config/Action.cs`

---

## Project Context

The Tyhp compiler transpiles `.tyhp` source files into `.php` output files. When debugging the resulting PHP with XDebug, breakpoints, stack traces, and variable references all point at the compiled `.php` files — not the original `.tyhp` source. The XDebug Proxy bridges this gap by sitting between the IDE's debug adapter (e.g., VS Code PHP Debug) and the XDebug engine. It intercepts DBGp protocol messages in both directions and translates file paths and line/column numbers using the Source Map v3 `.map` files produced by Story 17's emitter.

The proxy is launched via the `tyhp xdebug_proxy` CLI action (already defined in `Tyhp/Config/Action.cs` as `xdebug_proxy` and stubbed in `TyhpHostedService.cs` with `this._isLongRunning = true`). It runs as a long-lived TCP service.

**Optimizer interaction (Story 23):** When the compiler optimizer is enabled, extension method and operator calls may be inlined — meaning the generated PHP will call the underlying method directly rather than going through a synthetic static dispatch method. This means PHP stack traces may be missing frames that exist in the original Tyhp source. The XDebug Proxy must use sourcemaps to reconstruct the original Tyhp call stack, including calls that were inlined away in the compiled PHP. When the optimizer has inlined a call, the sourcemap maps the emitted code back to the original call site expression in the Tyhp source, so the proxy can correctly translate the stack frame. For a comprehensive solution, the Tyhp Reflection API (Story 29) provides runtime access to the full original call structure, including stack trace reconstruction via `\Tyhp\Reflection\StackTrace`.

---

## Architecture Overview

### High-Level Data Flow

```
IDE Debug Adapter  <──TCP──>  Tyhp XDebug Proxy  <──TCP──>  XDebug (PHP runtime)
     (client)                (translate messages)               (server)
```

1. The proxy opens a **listening TCP socket** on a configurable port (the "IDE port", default `9003`). The IDE debug adapter connects here instead of connecting directly to XDebug.
2. When XDebug starts a debug session, it connects to a **separate listening TCP socket** on the proxy (the "XDebug port", default `9004`) — or the proxy connects outbound to XDebug depending on configuration.
3. The proxy pairs the two connections into a **debug session**.
4. Every DBGp XML message flowing in either direction is parsed, inspected, and potentially **translated** before being forwarded.

### Component Breakdown

| Component | Responsibility |
|-----------|---------------|
| `DbgpMessage` | Parse/serialize DBGp XML messages |
| `DbgpMessageTranslator` | Apply sourcemap translations to messages |
| `SourceMapStore` | Load, cache, and query `.map` files |
| `SourceMapDecoder` | Decode VLQ-encoded Source Map v3 mappings |
| `ProxyServer` | TCP listener management, session lifecycle |
| `DebugSession` | Bidirectional message relay for one session |
| `XDebugProxyAction` | CLI action wiring, configuration, startup |
| `XDebugProxyConfig` | Configuration model for proxy settings |

### Technology Choices

- **TCP networking:** `System.Net.Sockets.TcpListener` / `TcpClient` with `async`/`await` using `NetworkStream`.
- **XML parsing:** `System.Xml.Linq` (`XDocument` / `XElement`) — DBGp messages are small XML documents; a DOM approach is appropriate.
- **Source Map v3 parsing:** Custom implementation using `System.Text.Json` for the JSON envelope and a hand-written VLQ base64 decoder for the `mappings` field.
- **Concurrency:** Each debug session runs on its own pair of async tasks. `CancellationToken` propagation from the hosted service enables graceful shutdown.
- **Logging:** Use the existing `Tyhp.CLI.Message` static class for console output, consistent with the rest of the CLI.

### Directory Structure (New Files)

```
Tyhp/XDebugProxy/
    Config/
        XDebugProxyConfig.cs
    Dbgp/
        DbgpCommand.cs
        DbgpResponse.cs
        DbgpMessage.cs
        DbgpMessageParser.cs
        DbgpConstants.cs
    SourceMap/
        SourceMapFile.cs
        SourceMapDecoder.cs
        SourceMapStore.cs
        MappingEntry.cs
    Network/
        ProxyServer.cs
        DebugSession.cs
        TcpConnectionHandler.cs
    Translation/
        BreakpointTranslator.cs
        StackFrameTranslator.cs
        VariableTranslator.cs
        PathMapper.cs
        DbgpMessageTranslator.cs
Tyhp/CLI/
    XDebugProxyAction.cs          (new)
```

### Conventions

- Follow the existing C# style: `namespace Tyhp.XDebugProxy.*`, nullable enabled, `LangVersion` 13.0, .NET 9.0.
- Keep individual files under 300-400 lines. Split large classes into partials if they grow.
- Use `async`/`await` throughout the networking layer — the proxy is inherently I/O-bound.
- All public APIs accept `CancellationToken` for cooperative shutdown.
- Error handling: catch and log transport-level exceptions; never crash the proxy on a single bad message. Use `DiagnosticBag` from Story 01 for structured error tracking and reporting. Story 01 is a prerequisite and `DiagnosticBag` is fully available. Use `Message.*` methods only for console-specific output (startup banners, session lifecycle logs) that should not be tracked as compilation diagnostics.

### Placeholder Strategy

- `DiagnosticBag` is available from Story 01 and should be used directly — no placeholder needed.
- Use the actual Source Map v3 JSON format as produced by Story 17 (`SourceMapGenerator`/`SourceMapWriter` emit the `.php.map` files). **Story 17 provides only the writer side — there is no `SourceMapReader` to reuse.** The proxy therefore implements its own consumer-side decoder (`SourceMapDecoder` + `SourceMapFile`, see Phase 1) that parses `.map` files from disk and resolves Tyhp source locations from PHP file/line positions. This is a deliberate design choice: the proxy is a separate long-running process that reads `.map` files off disk rather than sharing the compiler's in-memory mapping state, so a proxy-side decoder (not a shared reader) is the correct approach. The sourcemap provides line/column mappings and the `names` array for variable/function name translation.
- Use `// PLACEHOLDER_STORY_19: Coordinate with LSP for shared debug adapter protocol` for future LSP integration points.

---

## Phase 1: Source Map Loading and Querying

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Build the foundation layer that loads Source Map v3 JSON files, decodes VLQ-encoded mappings, and provides bidirectional lookup (Tyhp line/col to PHP line/col and vice versa). This is the core data layer that all translation logic depends on.

### Deliverables

- `Tyhp/XDebugProxy/SourceMap/MappingEntry.cs` — Data model for a single mapping segment
- `Tyhp/XDebugProxy/SourceMap/SourceMapDecoder.cs` — VLQ base64 decoder and Source Map v3 `mappings` field parser
- `Tyhp/XDebugProxy/SourceMap/SourceMapFile.cs` — Parsed representation of a single `.map` file with lookup methods
- `Tyhp/XDebugProxy/SourceMap/SourceMapStore.cs` — Cache/store that discovers, loads, and indexes all `.map` files for a project

### Implementation Details

**`MappingEntry`** — a readonly struct or record representing one decoded mapping segment:
- `GeneratedLine` (int) — 0-based line in the PHP output
- `GeneratedColumn` (int) — 0-based column in the PHP output
- `OriginalSourceIndex` (int?) — index into the `sources` array
- `OriginalLine` (int?) — 0-based line in the Tyhp source
- `OriginalColumn` (int?) — 0-based column in the Tyhp source
- `OriginalNameIndex` (int?) — index into the `names` array

**`SourceMapDecoder`** — static utility class:
- `DecodeVlq(string base64VlqSegment)` — decode a single VLQ-encoded value to an array of ints
- `DecodeMappings(string mappingsField)` — decode the full `mappings` string into a list of `MappingEntry` per generated line
- The VLQ base64 alphabet is `ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/`
- Each segment is comma-separated within a line; lines are semicolon-separated
- Fields within a segment: generated column, source index, original line, original column, name index (all relative/delta-encoded)

**`SourceMapFile`** — loaded representation of one `.map` file:
- Properties from the JSON: `Version`, `File`, `SourceRoot`, `Sources` (string[]), `SourcesContent` (string[]?), `Names` (string[]), raw `Mappings` string
- `DecodedMappings` — lazily decoded list-of-lists of `MappingEntry` (one inner list per generated line)
- `FindOriginalPosition(int generatedLine, int generatedColumn)` — forward lookup: given a PHP line/col, return the Tyhp source file path, line, and column. Use binary search within the line's sorted segments.
- `FindGeneratedPosition(string sourceFile, int originalLine, int originalColumn)` — reverse lookup: given a Tyhp file and line/col, return the PHP line and column. Scan all mappings for the matching source index and closest mapped line. "Closest mapped line" means: scan for the closest mapped line **at or after** the target line (forward scan). If no mapped line exists after the target, fall back to the closest mapped line **before** the target (backward scan). This matches standard debugger behavior where breakpoints snap forward to the next executable line. For breakpoints set on Tyhp-only lines (no direct PHP mapping), the same forward-then-backward strategy applies.
- Load from JSON using `System.Text.Json.JsonDocument` (no external dependency needed).

**`SourceMapStore`** — project-level cache:
- Constructor takes a root directory path (output directory where `.php` and `.php.map` files live) and an optional explicit list of `.map` file paths.
- `LoadAll()` — discover all `*.php.map` files recursively under the root directory and parse them into `SourceMapFile` instances.
- `GetMapForPhpFile(string phpFilePath)` — return the `SourceMapFile` for a given PHP file (lookup by the `file` field or by path convention `<phpfile>.map`).
- `GetMapForTyhpFile(string tyhpFilePath)` — return all `SourceMapFile` instances that reference this Tyhp source (scan `sources` arrays).
- Use `ConcurrentDictionary` for thread-safe caching.
- Support file-watching / refresh for long-running proxy sessions (store modification timestamps, reload on change).
- Handle missing `.map` files gracefully — log a warning and fall through (pass messages untranslated for files without sourcemaps).

### Acceptance Criteria

- VLQ decoder correctly decodes known test vectors (e.g., `AAAA` decodes to `[0,0,0,0]`, `AACA` decodes to `[0,0,1,0]`).
- A hand-crafted `.map` JSON file can be loaded and queried in both directions (Tyhp-to-PHP and PHP-to-Tyhp).
- Forward lookup: given PHP `file.php` line 10, correctly returns the mapped Tyhp source and line.
- Reverse lookup: given Tyhp `file.tyhp` line 5, correctly returns the mapped PHP line.
- `SourceMapStore` discovers `.map` files recursively and indexes them.
- Missing or malformed `.map` files produce warnings but do not crash.
- Code compiles without errors.

### Dependencies

- **Previous phases:** None (this is Phase 1).
- **Provides for future phases:** Phases 3, 4, 5 all depend on `SourceMapStore` and `SourceMapFile` for translation.
- **External dependency on Story 17:** The actual `.map` file format is defined there. This phase implements the *consumer* of that format. If Story 17 is not yet complete, create test `.map` files manually following the Source Map v3 spec.

---

## Phase 2: DBGp Protocol Parsing and Serialization

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement the DBGp protocol message model — parsing inbound XML messages from both the IDE and XDebug, and serializing modified messages back to the wire format. This phase builds the protocol layer without any networking; it operates on strings/byte arrays.

### Deliverables

- `Tyhp/XDebugProxy/Dbgp/DbgpConstants.cs` — Protocol constants (command names, status values, feature names)
- `Tyhp/XDebugProxy/Dbgp/DbgpCommand.cs` — Model for IDE-to-XDebug commands
- `Tyhp/XDebugProxy/Dbgp/DbgpResponse.cs` — Model for XDebug-to-IDE responses
- `Tyhp/XDebugProxy/Dbgp/DbgpMessage.cs` — Unified message envelope (command or response)
- `Tyhp/XDebugProxy/Dbgp/DbgpMessageParser.cs` — Parse raw DBGp wire bytes into message objects and serialize back

### Implementation Details

**DBGp Wire Format:**
- Commands (IDE to XDebug): plain text, null-byte terminated (`\0`). Format: `command_name -i transaction_id [arguments]\0`
  - Arguments use flag syntax: `-f filename`, `-n lineno`, `-t type`, `-d data_length`, etc.
  - Some commands include base64-encoded data after a `--` separator.
- Responses (XDebug to IDE): `data_length\0xml_data\0`
  - The `data_length` is the byte length of `xml_data` as an ASCII integer, followed by a null byte.
  - `xml_data` is a well-formed XML document.
- Init packet (XDebug to IDE on connect): same response format, root element is `<init>`.

**`DbgpConstants`** — static class with string constants:
- Command names: `breakpoint_set`, `breakpoint_get`, `breakpoint_remove`, `breakpoint_list`, `breakpoint_update`, `run`, `step_into`, `step_over`, `step_out`, `stop`, `detach`, `stack_get`, `stack_depth`, `context_names`, `context_get`, `property_get`, `property_set`, `property_value`, `source`, `feature_get`, `feature_set`, `eval`, `status`, `typemap_get`
- Breakpoint types: `line`, `call`, `return`, `exception`, `conditional`, `watch`
- Status values: `starting`, `stopping`, `stopped`, `running`, `break`
- Feature names: `language_supports_threads`, `language_name`, `language_version`, `encoding`, `protocol_version`, `supports_async`, `data_encoding`, `breakpoint_languages`, `breakpoint_types`, `multiple_sessions`, `max_children`, `max_data`, `max_depth`

**`DbgpCommand`** — represents an IDE-to-XDebug command:
- `CommandName` (string) — the command verb
- `TransactionId` (string) — the `-i` value
- `Arguments` (Dictionary<string, string>) — flag-value pairs (e.g., `-f` -> filename, `-n` -> line number)
- `Data` (byte[]?) — optional base64-decoded data payload (after `--`)
- `RawText` (string) — the original command text for passthrough

**`DbgpResponse`** — represents an XDebug-to-IDE response:
- `RootElement` (XElement) — the parsed XML root element
- `TransactionId` (string?) — from the `transaction_id` attribute
- `Command` (string?) — from the `command` attribute (echoes the command that triggered this response)
- `Status` (string?) — from the `status` attribute (on status responses)
- `IsInit` (bool) — true if this is the `<init>` packet
- Helper properties for commonly accessed attributes and child elements

**`DbgpMessageParser`** — static utility:
- `ParseCommand(byte[] rawBytes)` or `ParseCommand(string rawText)` — parse an IDE command from the null-terminated text
- `SerializeCommand(DbgpCommand command)` — serialize back to wire bytes
- `ParseResponse(byte[] rawBytes)` — parse an XDebug response from the `length\0xml\0` format
- `SerializeResponse(DbgpResponse response)` — serialize back to `length\0xml\0` wire bytes
- `ReadCommandFromStream(Stream stream, CancellationToken ct)` — async read a complete command from a network stream (read until null byte)
- `ReadResponseFromStream(Stream stream, CancellationToken ct)` — async read a complete response (read length, then XML payload)
- Handle encoding: DBGp uses UTF-8 by default; support base64 data encoding

**`DbgpMessage`** — discriminated union-style wrapper:
- Contains either a `DbgpCommand` or a `DbgpResponse`
- `Direction` enum: `IdeToXDebug`, `XDebugToIde`
- `IsCommand` / `IsResponse` convenience properties

### Acceptance Criteria

- A sample `breakpoint_set` command string can be parsed and its filename, line number, and transaction ID extracted.
- A modified `breakpoint_set` command can be serialized back to valid wire format.
- A sample XDebug `<response>` XML can be parsed and the status, command, and child elements accessed.
- A sample `<init>` packet can be parsed and identified as an init message.
- A modified response XML can be serialized back to valid `length\0xml\0` format.
- Round-trip: `parse -> serialize -> parse` produces identical results for both commands and responses.
- Null bytes, base64 data payloads, and multi-byte UTF-8 characters are handled correctly.
- Code compiles without errors.

### Dependencies

- **Previous phases:** None (this phase is independent of Phase 1).
- **Provides for future phases:** Phase 3 (translation) and Phase 4 (networking) depend on this protocol layer.

---

## Phase 3: Message Translation Logic

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Build the translation layer that modifies DBGp messages in-flight, replacing Tyhp file paths and line numbers with PHP equivalents (for IDE-to-XDebug direction) and PHP paths/lines with Tyhp equivalents (for XDebug-to-IDE direction). This phase connects the sourcemap layer (Phase 1) with the protocol layer (Phase 2).

### Deliverables

- `Tyhp/XDebugProxy/Translation/BreakpointTranslator.cs` — Translate breakpoint-related commands and responses
- `Tyhp/XDebugProxy/Translation/StackFrameTranslator.cs` — Translate stack trace responses
- `Tyhp/XDebugProxy/Translation/VariableTranslator.cs` — Translate variable names in property responses
- `Tyhp/XDebugProxy/Translation/PathMapper.cs` — Shared path-mapping utility (Tyhp source root ↔ PHP output root, `file://` URI handling, path normalization)
- `Tyhp/XDebugProxy/Translation/DbgpMessageTranslator.cs` — Top-level translator that dispatches to specific translators

### Implementation Details

**`DbgpMessageTranslator`** — the main translation entry point:
- Constructor takes a `SourceMapStore` and a `PathMapper` configuration (mapping between Tyhp source root and PHP output root).
- `TranslateIdeToXDebug(DbgpCommand command)` — translate an IDE command before forwarding to XDebug.
- `TranslateXDebugToIde(DbgpResponse response)` — translate an XDebug response before forwarding to IDE.
- Dispatch to specific translators based on the command name.
- Commands/responses that don't need translation are returned unmodified.
- `InterceptCommand(DbgpCommand command)` — returns a `DbgpResponse?`. If non-null, the proxy should send this response directly to the IDE without forwarding the command to XDebug. Returns null for commands that should be forwarded normally. In Phase 3, this always returns null — Phase 6 adds the `source` command interception.

**`BreakpointTranslator`** — handles breakpoint commands:

*IDE-to-XDebug direction (`breakpoint_set`, `breakpoint_update`):*
- Extract the `-f` (filename) and `-n` (line number) arguments.
- If the filename ends in `.tyhp` (or matches a known Tyhp source file):
  - Use `SourceMapStore.GetMapForTyhpFile()` to find the corresponding sourcemap.
  - Use `SourceMapFile.FindGeneratedPosition()` to map the Tyhp line to the PHP line.
  - Replace `-f` with the PHP file URI and `-n` with the PHP line number.
  - Store the original Tyhp file/line in a session-local mapping table keyed by breakpoint ID (so responses can be reverse-mapped).
- If the filename is already a `.php` file, pass through unmodified.
- Handle the `file://` URI scheme that XDebug uses.

*XDebug-to-IDE direction (`breakpoint_set` response, `breakpoint_get` response, `breakpoint_list` response):*
- Extract `<breakpoint>` elements from the response XML.
- For each breakpoint element with a `filename` and `lineno` attribute:
  - Use `SourceMapStore.GetMapForPhpFile()` and `SourceMapFile.FindOriginalPosition()` to reverse-map.
  - Replace `filename` and `lineno` attributes with Tyhp source values.

**`StackFrameTranslator`** — handles `stack_get` and `stack_depth` responses:
- Extract `<stack>` elements from the `stack_get` response.
- For each stack frame with `filename` and `lineno` attributes:
  - Reverse-map the PHP file/line to Tyhp source using the sourcemap.
  - Replace `filename` and `lineno` attributes.
  - Optionally update the `where` attribute (function name) if the emitter renamed it.
- Preserve frames for files without sourcemaps (e.g., vendor PHP code) — leave them as PHP references.

**`VariableTranslator`** — handles `context_get`, `property_get`, `property_value` responses:
- Inspect `<property>` elements in the response.
- For the initial Phase 3 implementation: pass through all variable names unmodified, using only the `names` array from sourcemaps for any simple name lookups that are directly available.
- Advanced variable translation (struct backing array display names, extension method `$this` parameter renaming, decimal type display) is deferred to Phase 6.
- `// PLACEHOLDER_PHASE_6: Expand variable translation for struct, extension, and decimal display names`

**Path mapping logic** (implemented in `PathMapper.cs`, the helper consumed by `DbgpMessageTranslator`):
- Convert between Tyhp source paths and PHP output paths:
  - Tyhp path: `/project/src/App/Services/UserService.tyhp`
  - PHP path: `/project/build/App/Services/UserService.php`
- The mapping uses the project's `output.path` config (or the sourcemap's `sourceRoot` field).
- Handle the `file://` URI scheme that XDebug uses for paths.
- Handle path normalization (forward slashes, case sensitivity on Linux, etc.).
- Handle Windows vs. Unix path formats.

**Session-local state tracking:**
- `DbgpMessageTranslator` needs per-session state to track:
  - Breakpoint ID to original Tyhp file/line mapping (so breakpoint *responses* can be translated even though they only contain PHP paths).
  - The `file_uri` base path from the `<init>` packet (tells us XDebug's working directory).
  - Feature negotiation results (encoding, max data, etc.).

### Acceptance Criteria

- A `breakpoint_set` command for `file.tyhp:42` is translated to the correct `file.php:N` based on sourcemap data.
- A `stack_get` response with PHP file/line references is translated back to Tyhp source references.
- Breakpoint responses contain Tyhp paths and line numbers, not PHP paths.
- Files without sourcemaps pass through untranslated (no crash, no modification).
- Path mapping correctly handles the `file://` URI scheme.
- Multiple concurrent sessions maintain independent state.
- Code compiles without errors.

### Dependencies

- **Previous phases:** Phase 1 (SourceMap), Phase 2 (DBGp protocol).
- **Provides for future phases:** Phase 4 (networking) uses this translator in the message relay loop.

---

## Phase 4: TCP Networking and Session Management

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement the TCP server infrastructure that accepts connections from both the IDE debug adapter and XDebug, pairs them into debug sessions, and relays translated messages bidirectionally. This phase creates the runtime backbone of the proxy.

### Deliverables

- `Tyhp/XDebugProxy/Network/TcpConnectionHandler.cs` — Low-level async TCP read/write helpers
- `Tyhp/XDebugProxy/Network/ProxyServer.cs` — Dual TCP listener (IDE port + XDebug port), session pairing
- `Tyhp/XDebugProxy/Network/DebugSession.cs` — Bidirectional message relay for one paired session

### Implementation Details

**`TcpConnectionHandler`** — thin wrapper around `TcpClient` + `NetworkStream`:
- `ReadCommandAsync(NetworkStream stream, CancellationToken ct)` — read a null-terminated DBGp command from the IDE. Accumulate bytes until `\0`. Return the parsed `DbgpCommand`.
- `ReadResponseAsync(NetworkStream stream, CancellationToken ct)` — read a DBGp response from XDebug. First read the ASCII length integer until `\0`, then read exactly that many bytes of XML, then consume the trailing `\0`. Return the parsed `DbgpResponse`.
- `WriteCommandAsync(NetworkStream stream, DbgpCommand command, CancellationToken ct)` — serialize and write a command.
- `WriteResponseAsync(NetworkStream stream, DbgpResponse response, CancellationToken ct)` — serialize and write a response.
- Handle partial reads, connection drops, and timeouts gracefully.
- Use configurable read buffer size (default 8192 bytes).

**`ProxyServer`** — the main server orchestrator:
- Constructor takes `XDebugProxyConfig` and `SourceMapStore`.
- `StartAsync(CancellationToken ct)`:
  - Start a `TcpListener` on the IDE port (e.g., 9003). This is where the IDE debug adapter connects.
  - Start a `TcpListener` on the XDebug port (e.g., 9004). This is where XDebug connects when a PHP script starts.
  - Enter an accept loop on both listeners.
- **Session pairing strategy:**
  - Primary: `idekey`-based pairing. Use a `ConcurrentDictionary<string, PendingConnection>` keyed by `idekey`. When XDebug connects and sends an `<init>` packet, extract the `idekey` attribute and store the XDebug connection as pending. When the IDE connects, it identifies its `idekey` (from configuration) and is paired with the matching pending XDebug connection.
  - Fallback: When no `idekey` is present (or `idekey` is empty), use sequential FIFO pairing — the first unpaired IDE connection is matched with the first unpaired XDebug connection.
  - Support multiple concurrent sessions (up to `MaxSessions` from configuration).
  - Reject connections that exceed `MaxSessions` with a log warning.
- `StopAsync()` — stop listeners, close all active sessions, dispose resources.
- Log session lifecycle events (connect, pair, disconnect).

**`DebugSession`** — manages one paired connection:
- Properties: `SessionId` (string), `IdeClient` (TcpClient), `XDebugClient` (TcpClient), `Translator` (DbgpMessageTranslator)
- `RunAsync(CancellationToken ct)`:
  - Forward the `<init>` packet from XDebug to the IDE (after translating any paths in it).
  - Start two concurrent async tasks:
    - **IDE-to-XDebug relay:** Read commands from IDE stream, translate via `DbgpMessageTranslator.TranslateIdeToXDebug()`, write to XDebug stream.
    - **XDebug-to-IDE relay:** Read responses from XDebug stream, translate via `DbgpMessageTranslator.TranslateXDebugToIde()`, write to IDE stream.
  - Both tasks run until either side disconnects or cancellation is requested.
  - When one side disconnects, signal the other side to close.
  - **Proxy-generated responses:** Some commands may be intercepted by the proxy and responded to directly without forwarding to XDebug. For example, the `source` command (Phase 6) returns Tyhp source content from sourcemaps instead of forwarding to XDebug. The relay loop should support a `DbgpMessageTranslator.InterceptCommand(DbgpCommand)` method that returns a `DbgpResponse?` — when non-null, the response is sent directly to the IDE and the command is NOT forwarded to XDebug.
- Error handling:
  - Catch `IOException`, `SocketException`, `ObjectDisposedException` — these indicate connection loss. Log and clean up.
  - Catch `XmlException` or parse errors — log the bad message, skip it, and continue relaying. Do not tear down the session for a single bad message.
- Dispose: close both TCP connections.

**Connection lifecycle:**
1. IDE debug adapter connects to proxy IDE port.
2. PHP script starts with XDebug enabled, XDebug connects to proxy XDebug port.
3. Proxy reads `<init>` from XDebug, creates a `DebugSession`, forwards `<init>` to IDE.
4. IDE sends `feature_set`, `breakpoint_set`, `run` etc. — proxy translates and forwards.
5. XDebug sends responses, `<break>` notifications — proxy translates and forwards.
6. Session ends when IDE sends `stop`/`detach` or XDebug disconnects.

### Acceptance Criteria

- The proxy can accept a TCP connection on the IDE port.
- The proxy can accept a TCP connection on the XDebug port.
- Two connections are paired into a `DebugSession`.
- Messages flow bidirectionally: IDE commands reach XDebug, XDebug responses reach IDE.
- Translation is applied during relay (verified by inspecting translated messages in logs).
- Connection drops on either side are handled gracefully (no crash, clean resource cleanup).
- Multiple sequential sessions work (IDE connects, debug, disconnect, reconnect).
- `CancellationToken` stops all listeners and sessions cleanly.
- Code compiles without errors.

### Dependencies

- **Previous phases:** Phase 2 (DBGp protocol), Phase 3 (translation).
- **Provides for future phases:** Phase 5 (CLI wiring) uses `ProxyServer` as the entry point.

---

## Phase 5: CLI Wiring and Configuration

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Wire the XDebug proxy into the Tyhp CLI as the `xdebug_proxy` action, implement configuration parsing for proxy-specific settings, and complete the end-to-end startup/shutdown lifecycle.

### Deliverables

- `Tyhp/XDebugProxy/Config/XDebugProxyConfig.cs` — Configuration model for proxy settings
- `Tyhp/CLI/XDebugProxyAction.cs` — CLI action that starts the proxy
- Modifications to `Tyhp/CLI/TyhpHostedService.cs` — Wire the action into the switch statement
- Modifications to `Tyhp/Config/Project.cs` — Parse proxy-related configuration options
- Modifications to `Tyhp/Domain/Exceptions/MessageCode.cs` — Add proxy-specific message codes

### Implementation Details

**`XDebugProxyConfig`** — configuration POCO:
- `IdeListenPort` (int, default 9003) — port the IDE connects to
- `XDebugListenPort` (int, default 9004) — port XDebug connects to
- `IdeListenAddress` (string, default "127.0.0.1") — bind address for IDE listener
- `XDebugListenAddress` (string, default "127.0.0.1") — bind address for XDebug listener
- `SourceMapDirectory` (string?) — directory containing `.php.map` files; defaults to `output.path` from project config
- `TyhpSourceRoot` (string?) — root directory of Tyhp source files; defaults to project root
- `PhpOutputRoot` (string?) — root directory of compiled PHP files; defaults to `output.path`
- `IdeKey` (string?) — optional filter to only accept XDebug sessions with a specific IDE key
- `MaxSessions` (int, default 10) — maximum concurrent debug sessions
- `LogLevel` (string, default "info") — verbosity: "debug", "info", "warn", "error"
- `AutoReloadSourceMaps` (bool, default true) — watch for `.map` file changes and reload
- Populate from `IConfiguration` using the `xdebugProxy` section of `tyhp.json` or CLI arguments.

**`XDebugProxyAction`** — extends `ActionRunnerBase`:
- Override `Start(CancellationToken cancellationToken)`:
  1. Load `XDebugProxyConfig` from project configuration.
  2. Validate configuration (ports are valid, directories exist, sourcemap directory contains `.map` files).
  3. Create `SourceMapStore` with the configured sourcemap directory.
  4. Call `SourceMapStore.LoadAll()` — load and index all sourcemaps.
  5. If no sourcemaps found, display a warning: "No sourcemaps found in {directory}. Build the project with sourcemap generation enabled first."
  6. Create `ProxyServer` with the config and store.
  7. Display startup info: "XDebug Proxy listening — IDE port: {port}, XDebug port: {port}"
  8. Call `ProxyServer.StartAsync(cancellationToken)` — this blocks until cancellation.
  9. On shutdown, display "XDebug Proxy stopped."
- Handle `OperationCanceledException` gracefully (normal shutdown path).

**`TyhpHostedService.cs` modifications:**
- In the `case Tyhp.Config.Action.xdebug_proxy:` block:
  - Instantiate `XDebugProxyAction`.
  - Call `Start()` with the cancellation token.
  - The `_isLongRunning = true` flag is already set.

**`Project.cs` modifications:**
- Add configuration parsing for proxy-related settings in `ConfigChanged()`:
  - `xdebugProxy.idePort` -> `int`
  - `xdebugProxy.xdebugPort` -> `int`
  - `xdebugProxy.sourceMapDir` -> `string`
  - `xdebugProxy.ideKey` -> `string`
  - `xdebugProxy.maxSessions` -> `int`
  - `xdebugProxy.logLevel` -> `string`
- Alternatively, read these from CLI arguments: `--ide-port`, `--xdebug-port`, `--sourcemap-dir`, `--ide-key`.
- Expose an `XDebugProxyConfig? XDebugProxy` property on `Project`.

**`MessageCode.cs` additions — use the 7400–7499 range (CLI `xdebug_proxy` action subdivision):**
- `ProxyUnknownError = 7400` — Generic XDebug proxy error
- `ProxySourceMapNotFound = 7401` — No sourcemap file found for a given PHP file
- `ProxySourceMapParseError = 7402` — Sourcemap JSON is invalid or malformed
- `ProxyConnectionFailed = 7403` — TCP connection error
- `ProxySessionPairingTimeout = 7404` — IDE or XDebug side didn't connect within timeout
- `ProxyTranslationError = 7405` — Error translating a DBGp message (non-fatal, logged)
- `ProxyInvalidDbgpMessage = 7406` — Received a malformed DBGp message
- `ProxyPortInUse = 7407` — Configured port is already bound by another process

**CLI argument handling:**
- Add proxy-specific CLI arguments to `ActionConfigSource`/`ActionConfigProvider` if needed:
  - `--ide-port <port>`
  - `--xdebug-port <port>`
  - `--sourcemap-dir <path>`
  - `--ide-key <key>`
  - `--log-level <level>`

### Acceptance Criteria

- Running `tyhp xdebug_proxy` starts the proxy and displays the listening ports.
- The proxy reads configuration from `tyhp.json` `xdebugProxy` section.
- CLI arguments override `tyhp.json` settings (e.g., `tyhp xdebug_proxy --ide-port 9005`).
- If no sourcemaps exist, a clear warning is displayed but the proxy still starts (passthrough mode).
- `Ctrl+C` (SIGINT) gracefully shuts down listeners and active sessions.
- Invalid port numbers or inaccessible directories produce clear error messages.
- The PID file (`tyhp.pid`) is written on startup and cleaned up on shutdown (existing behavior from `TyhpHostedService`).
- Code compiles without errors.

### Dependencies

- **Previous phases:** Phase 1 (SourceMap), Phase 2 (DBGp), Phase 3 (Translation), Phase 4 (Network).
- **Provides for future phases:** Phase 6 (advanced features) and Phase 7 (help text) build on this working CLI action.

---

## Phase 6: Advanced Translation Features and Edge Cases

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Expand the translation logic to handle advanced debugging scenarios: conditional breakpoints, eval expressions, `source` command file content retrieval, multi-file sourcemaps, and robust handling of edge cases encountered during real debugging sessions.

### Deliverables

- Modifications to `Tyhp/XDebugProxy/Translation/BreakpointTranslator.cs` — Conditional breakpoints, exception breakpoints, breakpoint listing
- Modifications to `Tyhp/XDebugProxy/Translation/StackFrameTranslator.cs` — Async stack frames, multi-file traces
- Modifications to `Tyhp/XDebugProxy/Translation/VariableTranslator.cs` — Struct backing array display, extension method `$this` renaming
- New file `Tyhp/XDebugProxy/Translation/SourceCommandTranslator.cs` — Handle the `source` command to return Tyhp source instead of PHP source
- New file `Tyhp/XDebugProxy/Translation/EvalTranslator.cs` — Translate eval expressions
- Modifications to `Tyhp/XDebugProxy/Translation/DbgpMessageTranslator.cs` — Register new translators

### Implementation Details

**Conditional breakpoint translation:**
- `breakpoint_set` with `-t conditional` includes an expression in the data payload.
- The expression is PHP code evaluated by XDebug. If the expression references Tyhp-renamed variables, translate them.
- Translate conditional breakpoint expressions using the emitter's variable renaming metadata from Story 11. When the IDE sets a conditional breakpoint with a Tyhp variable name (e.g., `$myVar > 5`), use the sourcemap's `names` array to map the Tyhp variable name to its compiled PHP equivalent before forwarding the condition to XDebug.

**Exception breakpoints:**
- `breakpoint_set` with `-t exception` uses `-x` for the exception class name.
- If the Tyhp code uses a type alias for an exception class, the IDE might send the alias name. Translate to the real PHP class name using the binder/emitter metadata.
- For now, pass through unmodified — PHP exception class names are typically unchanged by the emitter.

**`source` command translation:**
- The `source` command (`source -i tid -f file_uri`) asks XDebug to return the source code of a file.
- Intercept this command and, if the requested file has a sourcemap, return the Tyhp source content instead:
  - Check if the `SourceMapFile` has `sourcesContent` embedded.
  - If yes, return the Tyhp source from `sourcesContent`.
  - If no, read the Tyhp source file from disk using the path from the `sources` array.
  - Base64-encode the content per DBGp spec and construct a response.
- If no sourcemap exists, forward the command to XDebug unmodified.

**Eval translation:**
- The `eval` command sends a PHP expression to XDebug for evaluation.
- If the IDE sends Tyhp-specific syntax in an eval (unlikely but possible with a Tyhp-aware debug adapter), it would need compilation. This is out of scope.
- Translate variable names in eval expressions if the emitter renamed them.
- For initial implementation, pass through unmodified with `// PLACEHOLDER_STORY_19: Add Tyhp expression compilation for eval`.

**Variable display improvements:**
- Struct variables: In PHP, structs are backed by arrays. When XDebug reports a struct variable, its `<property>` elements show array keys. Translate array key names back to struct property names using sourcemap `names` or emitter metadata.
- Extension method `$this` parameter: The emitter rewrites `$value->extensionMethod()` to `ExtensionClass::extensionMethod($value)`. Inside the extension method, the first parameter might be named differently. Translate it back to show as the original variable name.
- Decimal type: `\Tyhp\decimal` objects in XDebug will show internal representation. Add a custom display that shows the decimal value directly.

**Multi-file debugging:**
- A single debug session may involve breakpoints across many `.tyhp` files.
- Ensure `SourceMapStore` efficiently handles many sourcemaps without noticeable latency.
- Pre-index all sourcemaps by both PHP path and Tyhp source path for O(1) lookups.

**Edge cases to handle:**
- Breakpoints on lines that don't map to any PHP line (e.g., type-only lines erased by the emitter). Find the closest mapped line using the forward-then-backward scan strategy described in `FindGeneratedPosition()`.
- Multiple Tyhp lines mapping to the same PHP line. When reverse-mapping, choose the first Tyhp source line.
- XDebug sending `file://` URIs vs. plain paths. Normalize consistently.
- Windows backslash paths vs. Unix forward slash paths.
- Sourcemap files that reference relative paths with `sourceRoot`. Resolve correctly.
- XDebug `dbgp://` protocol for eval'd code. Pass through unmodified.

### Acceptance Criteria

- The `source` command for a file with sourcemaps returns the Tyhp source content.
- Breakpoints on Tyhp-only lines (no direct PHP mapping) are set on the nearest valid PHP line.
- Path normalization handles both `file://` URIs and plain paths correctly.
- Struct variables display property names instead of array key indices.
- All existing Phase 3 functionality continues to work (no regressions).
- Code compiles without errors.

### Dependencies

- **Previous phases:** Phase 3 (Translation), Phase 5 (CLI wiring — for integration testing).
- **Provides for future phases:** Phase 7 (polish) depends on this phase being stable.

---

## Phase 7: Help Text, Logging, and Polish

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Complete the user-facing experience: help text for the `xdebug_proxy` action, structured logging for debugging proxy issues, graceful error messages, and final cleanup.

### Deliverables

- Modifications to `Tyhp/Config/DisplayHelp.cs` — Replace the placeholder `XDebugProxyHelp()` implementation from Story 13 with the full version containing complete proxy documentation, configuration examples, and usage instructions
- New file `Tyhp/XDebugProxy/ProxyLogger.cs` — Structured logging for proxy events
- Modifications to `Tyhp/XDebugProxy/Network/ProxyServer.cs` — Add logging calls
- Modifications to `Tyhp/XDebugProxy/Network/DebugSession.cs` — Add logging calls
- Modifications to various files — Final code review and cleanup

### Implementation Details

**`DisplayHelp.cs` — `XDebugProxyHelp()` implementation:**
- Display usage: `tyhp xdebug_proxy [options]`
- List all CLI options with descriptions:
  - `--ide-port <port>` — Port for IDE debug adapter connections (default: 9003)
  - `--xdebug-port <port>` — Port for XDebug connections (default: 9004)
  - `--sourcemap-dir <path>` — Directory containing .php.map sourcemap files
  - `--ide-key <key>` — Only accept XDebug sessions with this IDE key
  - `--log-level <level>` — Logging verbosity: debug, info, warn, error (default: info)
- Display a brief description of what the proxy does and how to configure XDebug to connect to it.
- Show example `php.ini` / `xdebug.ini` configuration:
  - `xdebug.mode = debug`
  - `xdebug.client_host = 127.0.0.1`
  - `xdebug.client_port = 9004` (the proxy's XDebug port)
  - `xdebug.idekey = <your-ide-key>`
- Show example `tyhp.json` configuration.
- Show example VS Code `launch.json` configuration (connecting to the proxy's IDE port).

**`ProxyLogger`** — logging utility:
- Wraps `Message.Debug()`, `Message.Info()`, `Message.Warn()`, `Message.Error()`.
- Adds structured context: `[session:{id}]`, `[proxy]`, timestamps.
- Respects the configured `LogLevel` — only emit messages at or above the configured level.
- At `debug` level: log every message flowing through the proxy (command names, translated paths, raw XML for responses).
- At `info` level: log session connect/disconnect, breakpoint set/hit, configuration info.
- At `warn` level: log translation failures, missing sourcemaps, malformed messages.
- At `error` level: log connection failures, fatal errors.

**Startup experience improvements:**
- On startup, display a summary:
  ```
  XDebug Proxy started
    IDE port:      9003
    XDebug port:   9004
    Sourcemaps:    42 files loaded from ./build/
    Source root:   ./src/
    IDE key:       (any)
  ```
- If sourcemaps are empty, display a prominent warning with actionable guidance.
- If a port is already in use, display a clear error: "Port 9003 is already in use. Specify a different port with --ide-port."

**Shutdown experience:**
- On `Ctrl+C`, display: "Shutting down... closing N active session(s)."
- Wait briefly for active sessions to complete current message relay.
- Display: "XDebug Proxy stopped."

**Code review and cleanup:**
- Remove any `TODO` comments that have been addressed.
- Ensure all `PLACEHOLDER_STORY_*` comments are present for unfinished cross-story integrations.
- Verify all `IDisposable` types are disposed correctly (TCP clients, listeners, streams).
- Verify `CancellationToken` is propagated to all async operations.
- Ensure no fire-and-forget tasks (all tasks are awaited or tracked).
- Verify thread safety of shared state in `SourceMapStore` and `ProxyServer`.

### Acceptance Criteria

- `tyhp help --subject=xdebug_proxy` displays complete, accurate help text with configuration examples.
- At `debug` log level, every relayed message is logged with direction and translation details.
- At `info` log level, session lifecycle events are logged cleanly.
- The startup banner displays all relevant configuration.
- Port-in-use errors display a clear, actionable message.
- Graceful shutdown completes within a reasonable time and closes all resources.
- All `PLACEHOLDER_STORY_*` comments are documented and point to the correct future stories.
- No compiler warnings (nullable reference types, unused variables, etc.).
- Code compiles without errors.

### Dependencies

- **Previous phases:** All previous phases (1-6).
- **Provides for future phases:** This is the final phase for Story 18. The proxy is complete and ready for use.

---

## Appendix: MessageCode Ranges for Story 18

All XDebug proxy error codes use the **7400–7499** range (CLI action subdivision for `xdebug_proxy`), as defined in `MessageCode.cs`.

| Code | Name | Description |
|------|------|-------------|
| 7400 | `ProxyUnknownError` | Generic XDebug proxy error |
| 7401 | `ProxySourceMapNotFound` | No sourcemap for a PHP file |
| 7402 | `ProxySourceMapParseError` | Invalid sourcemap JSON |
| 7403 | `ProxyConnectionFailed` | TCP connection error |
| 7404 | `ProxySessionPairingTimeout` | Pairing timeout |
| 7405 | `ProxyTranslationError` | Message translation error (non-fatal) |
| 7406 | `ProxyInvalidDbgpMessage` | Malformed DBGp message |
| 7407 | `ProxyPortInUse` | Port already in use |

---

## Appendix: Configuration Schema (`tyhp.json`)

```json
{
  "xdebugProxy": {
    "idePort": 9003,
    "xdebugPort": 9004,
    "ideListenAddress": "127.0.0.1",
    "xdebugListenAddress": "127.0.0.1",
    "sourceMapDir": null,
    "ideKey": null,
    "maxSessions": 10,
    "logLevel": "info",
    "autoReloadSourceMaps": true
  }
}
```

When `sourceMapDir` is `null`, the proxy uses the project's `output.path` configuration value. When `ideKey` is `null`, the proxy accepts all XDebug sessions regardless of IDE key.

---

## Appendix: DBGp Message Flow Example

```
IDE                        Proxy                       XDebug
 |                           |                            |
 |  [connect to IDE port]    |                            |
 |-------------------------->|                            |
 |                           |  [XDebug connects]         |
 |                           |<---------------------------|
 |                           |  <init fileuri="file.php"> |
 |                           |<---------------------------|
 |  <init fileuri="file.tyhp">                            |
 |<--------------------------|                            |
 |                           |                            |
 |  breakpoint_set           |                            |
 |  -f file.tyhp -n 42      |                            |
 |-------------------------->|                            |
 |                           |  breakpoint_set            |
 |                           |  -f file.php -n 67         |
 |                           |--------------------------->|
 |                           |  <response id="1">         |
 |                           |<---------------------------|
 |  <response id="1">        |                            |
 |<--------------------------|                            |
 |                           |                            |
 |  run -i 2                 |                            |
 |-------------------------->|  run -i 2                  |
 |                           |--------------------------->|
 |                           |  <response status="break"  |
 |                           |   filename="file.php"      |
 |                           |   lineno="67">             |
 |                           |<---------------------------|
 |  <response status="break" |                            |
 |   filename="file.tyhp"    |                            |
 |   lineno="42">            |                            |
 |<--------------------------|                            |
 |                           |                            |
 |  stack_get -i 3           |                            |
 |-------------------------->|  stack_get -i 3            |
 |                           |--------------------------->|
 |                           |  <response>                |
 |                           |   <stack filename="file.php" lineno="67"/>
 |                           |<---------------------------|
 |  <response>               |                            |
 |   <stack filename="file.tyhp" lineno="42"/>            |
 |<--------------------------|                            |
```

---

*Generated: 2026-02-16*

---

## Human Testing and Verification

> **Note:** These steps are for a human developer to manually verify the XDebug Proxy implementation. Steps can be skipped, reordered, or modified as appropriate. You will need: a built `tyhp` binary, PHP with XDebug installed, a compiled Tyhp project with sourcemaps, and VS Code (or another debugger that speaks DBGp).

### Step 1: Verify the Build Compiles

```bash
cd /path/to/tyhp
dotnet build
```

Confirm zero errors. All new files under `Tyhp/XDebugProxy/` should compile cleanly.

### Step 2: Prepare a Test Tyhp Project with Sourcemaps

Create a small Tyhp project and build it with sourcemap generation enabled (Story 17 must be complete):

```bash
mkdir -p /tmp/tyhp-xdebug-test/src
cd /tmp/tyhp-xdebug-test
```

Create `tyhp.json`:

```json
{
  "include": ["src/**/*.tyhp"],
  "output": {
    "path": "build/",
    "phpVersion": "8.4",
    "strictTypes": true,
    "sourceMap": true
  }
}
```

Create `src/Calculator.tyhp`:

```tyhp
<?tyhp

namespace App;

class Calculator {
    public function add(int $a, int $b): int {
        $result = $a + $b;
        return $result;
    }

    public function multiply(int $a, int $b): int {
        $product = $a * $b;
        return $product;
    }
}
```

Create `src/index.tyhp`:

```tyhp
<?tyhp

namespace App;

$calc = new Calculator();
$sum = $calc->add(3, 4);
$product = $calc->multiply(5, 6);
echo "Sum: {$sum}, Product: {$product}\n";
```

Build the project:

```bash
dotnet run --project /path/to/tyhp -- build
```

**Expected:** `build/` directory contains `.php` files and `.php.map` sourcemap files.

### Step 3: Verify the Proxy Starts

```bash
dotnet run --project /path/to/tyhp -- xdebug_proxy --sourcemap-dir=./build/
```

**Expected output (approximate):**

```
XDebug Proxy started
  IDE port:      9003
  XDebug port:   9004
  Sourcemaps:    N files loaded from ./build/
  Source root:   ./src/
  IDE key:       (any)
```

The proxy should remain running and listening. Press Ctrl+C to stop — it should shut down gracefully with a message like "XDebug Proxy stopped."

### Step 4: Verify Custom Port Configuration

```bash
dotnet run --project /path/to/tyhp -- xdebug_proxy --ide-port=9010 --xdebug-port=9011 --sourcemap-dir=./build/
```

**Expected:** The startup banner shows IDE port 9010 and XDebug port 9011.

### Step 5: Verify Port-in-Use Error

Start the proxy in one terminal, then try to start a second instance on the same ports:

```bash
# Terminal 1:
dotnet run --project /path/to/tyhp -- xdebug_proxy --sourcemap-dir=./build/

# Terminal 2:
dotnet run --project /path/to/tyhp -- xdebug_proxy --sourcemap-dir=./build/
```

**Expected:** The second instance should fail with a clear error: "Port 9003 is already in use. Specify a different port with --ide-port."

### Step 6: Verify No-Sourcemap Warning

```bash
dotnet run --project /path/to/tyhp -- xdebug_proxy --sourcemap-dir=/tmp/empty-dir
```

**Expected:** A warning message like "No sourcemaps found in /tmp/empty-dir. Build the project with sourcemap generation enabled first." The proxy should still start (passthrough mode).

### Step 7: Verify Help Text

```bash
dotnet run --project /path/to/tyhp -- help --subject=xdebug_proxy
```

**Expected:** Complete help text including:
- Usage syntax
- All CLI options (`--ide-port`, `--xdebug-port`, `--sourcemap-dir`, `--ide-key`, `--log-level`)
- Example XDebug `php.ini` configuration
- Example VS Code `launch.json` configuration
- Example `tyhp.json` `xdebugProxy` section

### Step 8: End-to-End Debugging Session with VS Code

This is the most comprehensive test. It requires PHP + XDebug + VS Code.

**8a. Configure XDebug in `php.ini`:**

```ini
[xdebug]
xdebug.mode = debug
xdebug.client_host = 127.0.0.1
xdebug.client_port = 9004
xdebug.start_with_request = yes
```

Note: `client_port` points to the proxy's XDebug port (9004), NOT the IDE port.

**8b. Create VS Code `launch.json`:**

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Debug Tyhp via Proxy",
            "type": "php",
            "request": "launch",
            "port": 9003,
            "pathMappings": {
                "/tmp/tyhp-xdebug-test/src": "${workspaceFolder}/src"
            },
            "program": "${workspaceFolder}/build/index.php"
        }
    ]
}
```

Note: `port` points to the proxy's IDE port (9003).

**8c. Start the proxy:**

```bash
cd /tmp/tyhp-xdebug-test
dotnet run --project /path/to/tyhp -- xdebug_proxy --sourcemap-dir=./build/ --log-level=debug
```

**8d. Set a breakpoint in VS Code:**

Open `src/Calculator.tyhp` in VS Code and set a breakpoint on the `$result = $a + $b;` line.

**8e. Start debugging:**

Press F5 in VS Code to start the debug session. This runs `php build/index.php`.

**Expected behavior:**

1. The proxy log should show the IDE and XDebug connections being paired.
2. The breakpoint should be translated from `Calculator.tyhp:7` to the corresponding `Calculator.php` line (visible in proxy debug logs).
3. Execution should pause at the breakpoint.
4. The VS Code call stack should show `Calculator.tyhp:7` (the Tyhp source), NOT the PHP file.
5. Hovering over `$a` and `$b` in VS Code should show their values (3 and 4).
6. Stepping over (`F10`) should advance to the `return $result;` line in the Tyhp source.
7. The Variables panel should show `$result = 7`.
8. Pressing Continue (`F5`) should let the program finish.

### Step 9: Verify Stack Trace Translation

During the debug session in Step 8, when paused at a breakpoint:

- Open the Call Stack panel in VS Code.
- All frames from your Tyhp code should show `.tyhp` file paths and Tyhp source line numbers.
- Frames from vendor PHP code (if any) should retain their original `.php` paths.

### Step 10: Verify Source Command Translation

During a debug session, use VS Code's "Open Loaded Script" feature or evaluate a source request. The proxy should return the Tyhp source content (from `sourcesContent` in the sourcemap or from disk), NOT the compiled PHP.

### Step 11: Verify Debug Logging

Run the proxy at `debug` log level:

```bash
dotnet run --project /path/to/tyhp -- xdebug_proxy --sourcemap-dir=./build/ --log-level=debug
```

Then run a debug session. The proxy output should log every DBGp message flowing through, including:
- Command names and directions (`IDE→XDebug`, `XDebug→IDE`)
- Translated file paths and line numbers (before → after)
- Session connect/disconnect events

### Step 12: Verify tyhp.json Configuration

Test configuration via `tyhp.json` instead of CLI flags. Add to `/tmp/tyhp-xdebug-test/tyhp.json`:

```json
{
  "xdebugProxy": {
    "idePort": 9005,
    "xdebugPort": 9006,
    "logLevel": "info"
  }
}
```

```bash
dotnet run --project /path/to/tyhp -- xdebug_proxy
```

**Expected:** The proxy starts on ports 9005/9006 as configured in `tyhp.json`.

### Step 13: Clean Up

```bash
rm -rf /tmp/tyhp-xdebug-test /tmp/empty-dir
```

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
