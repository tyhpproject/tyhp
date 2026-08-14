# Attributes — Developer Technical Guide

## What this folder actually is

Despite the name, **`Tyhp/TyhpLang/Attributes` does not define C# attributes**, PHP attributes, or binder “attribute” symbols. It contains a **static AST node type ↔ binary ID registry** used by AST serialization.

| File | Role |
|------|------|
| `AstNodeTypeRegistry.cs` | Thread-safe registry: assign/lookup byte IDs for concrete `Base2Ast` subclasses; custom-type hash path for out-of-assembly types |
| `AstNodeTypeRegistryInitializer.cs` | Thin startup wrapper that calls `AstNodeTypeRegistry.Initialize()` |

**Do not confuse with:**

- `Base2Ast.Attributes` — a list of child AST nodes (PHP `#\[…]` attribute nodes and similar), serialized when bit `0x04` is set
- `PhpAttributeAst` / other attribute AST classes under `Tyhp/TyhpLang/Ast/`
- C# `[Attribute]` types anywhere in the compiler

Namespace: `Tyhp.TyhpLang.Attributes`.

---

## Pipeline fit

```
Parse (ANTLR → visitor → Base2Ast tree)
        │
        ▼
AstCacheService.AddOrUpdate / Get
        │  Serialize() / Deserialize()
        │  (every node writes/reads NodeType byte + CustomNodeType int64)
        ▼
AstNodeTypeRegistry  ◄── NodeType / CustomNodeType properties on Base2Ast
        │
        ▼
Bind → Check → Emit   (registry not consulted after deserialize; BoundSymbol is not serialized)
```

The registry is **only** required for the **binary AST cache** path:

1. `CompilationService` parses files (often in parallel) and, when caching is enabled, calls `AstCacheService.AddOrUpdate(ast)`.
2. `AstCacheService` calls `SrcFileAst.Serialize()` → recursive `Base2Ast.SerializeInto`.
3. On a cache hit, `AstCacheService.Get` calls `Base2Ast.Deserialize` / `TryDeserialize`, which uses `GetNodeTypeClass` → registry lookups to `Activator.CreateInstance` the correct CLR type.
4. Tyhpdef loading (`Binder/BuiltIn/Tyhpdef.cs`) also reads/writes the same cache using the same serialize/deserialize path.

After a successful deserialize, the binder rebuilds symbols; `BoundSymbol` and `OwningFile` are explicitly **not** part of the binary format.

---

## `AstNodeTypeRegistry` — how it works

### Backing stores

All maps are `ConcurrentDictionary`:

| Map | Key → Value | Purpose |
|-----|-------------|---------|
| `_typeToNodeId` | `Type` → `byte` | Serialize: CLR type → wire ID |
| `_nodeIdToType` | `byte` → `Type` | Deserialize: wire ID → CLR type |
| `_typeToCustomHash` | `Type` → `long` | Custom types: CLR type → hash |
| `_customHashToType` | `long` → `Type` | Custom types: hash → CLR type |

Initialization is guarded by `_initLock` plus a `volatile bool _isInitialized` double-checked pattern so parallel cache deserializes during `Parallel.ForEach` parse loops are safe.

### Built-in registration (`Initialize`)

```csharp
var assembly = typeof(Ast.Base2Ast).Assembly;
var nodeTypes = assembly.GetTypes()
    .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(Ast.Base2Ast)))
    .OrderBy(t => t.FullName)
    .ToList();
```

Rules:

- Only **concrete** subclasses of `Base2Ast` in the **compiler assembly** (`typeof(Base2Ast).Assembly`).
- Abstract helpers (e.g. `NodeListAst<,>`, `Base2Ast` itself) are excluded.
- Order is **stable lexicographic `FullName`**, then IDs assigned from `0` upward.
- ID `0xFF` (`CustomNodeTypeByte`) is **reserved** and never assigned. If `nextId` reaches `0xFF`, initialization throws (`Too many AST node types…`).

Lazy callers (`GetNodeTypeId` / `GetTypeForNodeId`) call `Initialize()` if needed; you do not have to call it at process startup for correctness.

### Lookups

**`GetNodeTypeId(Type)`**

1. Ensure initialized.
2. Known registered type → return its byte.
3. Else if `type.IsSubclassOf(Base2Ast)` → return `CustomNodeTypeByte` (`0xFF`) without registering into the built-in maps.
4. Else → `ArgumentException` (“not an AST node type”).

**`GetTypeForNodeId(byte)`**

1. Known ID → return type.
2. `0xFF` → `ArgumentException` (“Cannot get type for custom node ID without custom type info”).
3. Unknown ID → `ArgumentException`.

**`Base2Ast` surfaces these as:**

- `NodeType` → `GetNodeTypeId(this.GetType())`
- `CustomNodeType` → if `NodeType == 0xFF`, `GetCustomTypeHash(this.GetType())`, else `0L`

### Custom types (hash path)

Used when a `Base2Ast` subclass is **not** in the compiler assembly’s registered set (plugin / external assembly scenarios).

| API | Behavior |
|-----|----------|
| `GetCustomTypeHash(Type)` | Cache hit, or SHA-256-based `long` from Unicode bytes of `FullName` (four `Int64` XOR folds), then `RegisterCustomType` |
| `RegisterCustomType(Type, long)` | Writes both custom maps |
| `GetCustomType(long)` | Map lookup; on miss, **scans every type in every loaded assembly** (`AsParallel`) for a matching hash, registers, or throws |

Wire format always stores both fields: 1-byte `NodeType` + 8-byte `CustomNodeType`. For built-ins, custom is `0`; for customs, node type is `0xFF` and the hash is meaningful.

Deserialize path (`Base2Ast.GetNodeTypeClass`):

- `nodeType != 0xFF` → `GetTypeForNodeId` (exceptions swallowed → fall through / null)
- `nodeType == 0xFF` and `customNodeType != 0` → `GetCustomType`

---

## Binary layout coupling (`Base2Ast`)

Relevant fragment of the per-node block (documented on `Base2Ast`):

```text
4 bytes  block size
1 byte   NodeType          ← AstNodeTypeRegistry ID
1 byte   presence bitfield
8 bytes  CustomNodeType    ← hash when NodeType == 0xFF, else 0
…
```

Serialize guard:

```csharp
if (this.NodeType == 0) {
    throw new Exception("Node of type " + this.GetType().Name + " cannot be serialized.");
}
```

Because registration **starts IDs at 0**, the **lexicographically first** concrete `Base2Ast` subclass always gets ID `0` and therefore **cannot be serialized**.

As of the current type set, that first name is `Tyhp.TyhpLang.Ast.EmittedPhpExprAst` — an emitter-only node (`WithKeywordHelper` / expression emit), never produced by the parser and never written to the AST cache. It also has a **private** constructor, so `Activator.CreateInstance` in deserialize would not work for it even with a non-zero ID.

**Practical implication:** renaming/adding types that sort before `EmittedPhpExprAst` would move ID `0` onto a different type and make *that* type uncacheable until the guard or assignment scheme changes. Adding/removing/renaming *any* registered AST type also **shifts subsequent IDs**, so on-disk caches from another compiler build must not be reused. `AstCacheService` already namespaces cache dirs by `ModuleVersionId` (`CompilerBuildId`) plus `CacheFormatVersion`, which is what makes ID reshuffles safe across rebuilds.

---

## Who uses this

| Call site | Usage |
|-----------|--------|
| `Ast/Base2Ast.cs` | **Only production consumer.** `NodeType` / `CustomNodeType` properties; `GetNodeTypeClass` during deserialize |
| `Domain/Services/AstCacheService.cs` | Indirect: `Serialize` / `Deserialize` on cached `SrcFileAst` trees (parallel parse + tyhpdef cache) |
| `Domain/Services/CompilationService.cs` | Indirect: enables AST cache hits/misses during parallel file parse |
| `Binder/BuiltIn/Tyhpdef.cs` | Indirect: caches parsed tyhpdef ASTs |
| `AstNodeTypeRegistryInitializer` | **No call sites** in the repo (see below) |
| Implementation plans / docs | Mentions that new AST nodes must remain registry-compatible for ser/de |

There is **no** binder, checker, or emitter logic that branches on registry IDs; those stages see CLR types and symbols.

---

## `AstNodeTypeRegistryInitializer`

```csharp
public static void Initialize()
{
    if (_isInitialized) return;
    AstNodeTypeRegistry.Initialize();
    _isInitialized = true;
}
```

Observations from the codebase:

- **Never referenced** outside its own file (no startup call in CLI, `CompilationService`, or tests).
- Its `_isInitialized` flag is **not** synchronized (unlike the registry’s lock).
- Functionally redundant: `GetNodeTypeId` / `GetTypeForNodeId` already lazy-initialize.

Treat it as an unused convenience entry point unless something external (not in this repo) calls it.

---

## Conventions for maintainers

1. **New AST node types** under `Tyhp.TyhpLang.Ast` that are concrete `Base2Ast` subclasses are **auto-registered** on next `Initialize`. No manual table entry.
2. Prefer **public parameterless constructors** on any node that might appear in a cached tree — deserialize uses `Activator.CreateInstance(reflectedType)`.
3. Do **not** put cacheable AST types in other assemblies unless you intentionally use the custom (`0xFF` + hash) path and ensure both sides can resolve the type.
4. Changing type **names or namespaces** changes `FullName` order → **new ID assignments** for that type and all later ones. Rely on build-id cache invalidation; bump `AstCacheService.CacheFormatVersion` only when the **byte layout** itself changes.
5. Stay under **254** concrete in-assembly node types (`0x00`–`0xFE` assigned; `0xFF` reserved). Current set is on the order of ~90–100 concrete classes.
6. Folder name is historical/misleading — keep registry code here unless you do a deliberate rename across `using Tyhp.TyhpLang.Attributes`.

---

## Weirdness / WHY (grounded)

| Behavior | Why (as implemented) |
|----------|----------------------|
| Folder named “Attributes” | Contains no attributes; name is leftover / misnomer relative to `Base2Ast.Attributes` |
| Concurrent dictionaries + init lock | Comment and `CompilationService` parallel parse/cache deserialize require concurrent populate/query |
| IDs from sorted `FullName` | Stable within a fixed type set without hand-maintained enums |
| `0xFF` reserved | Distinguishes built-in compact IDs from “look up by hash” custom types in one byte |
| `NodeType == 0` refuses serialize | Hard guard in `SerializeInto`; coincides with first assigned ID — currently `EmittedPhpExprAst` (emitter-only) |
| Custom hash = SHA-256 XOR fold of Unicode `FullName` | Stable across processes for the same type name; not cryptographic identity of the type body |
| `GetCustomType` full AppDomain scan | Last-resort discovery when hash was serialized but process hasn’t registered the type yet; calls `GetCustomTypeHash` on **every** type (which also **registers** hashes into the custom maps as a side effect) |
| Initializer class unused | Lazy init on first property access is enough for current hosts |
| Exceptions swallowed in `GetNodeTypeClass` | Deserialize prefers returning null / throwing “Invalid node type” at a higher level over leaking registry exceptions |

---

## Open questions

1. **Was ID `0` intentionally a “non-serializable sentinel,” or should registration have started at `1`?** Today ID `0` is both “first registered type” and “forbidden to serialize.” That only works cleanly while the first sorted name is a non-cached type.
2. **Is the custom-type AppDomain scan still a supported product path?** There are no in-repo `Base2Ast` subclasses outside the compiler assembly; the scan may be dead capability with expensive side effects if ever hit.
3. **Should `AstNodeTypeRegistryInitializer` be deleted or wired at CLI startup?** It currently does nothing for this repository’s hosts.
4. **Hash collisions** on `RegisterCustomType` / `GetCustomTypeHash`: last writer wins in the concurrent dictionaries; collision handling is unspecified.
