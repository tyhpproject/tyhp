---
title: 'CLI: Source Map Generation'
status:
  tier: 2
  story: '17'
  state: planned
---

:::warning Not in this alpha
This feature is **not included** in Tyhp 805.0.0-alpha.1 (roadmap Tier 2/3). The rest of this page describes the planned design. Do not expect these commands or syntax to work yet.
:::


Source maps provide a bidirectional mapping between positions in the compiled PHP output and their originating positions in the original Tyhp source files. They enable debugging with your .tyhp files while PHP executes the compiled output, and allow tools to map PHP runtime errors back to Tyhp source locations.

## Source Map Format

Tyhp generates source maps using the Source Map v3 specification (the same format used by JavaScript/TypeScript tooling). Each .php output file gets a corresponding .php.map file containing a JSON document with the mapping data.

```json
{
    "version": 3,
    "file": "User.php",
    "sourceRoot": "../src/",
    "sources": ["Models/User.tyhp"],
    "sourcesContent": [null],
    "names": [],
    "mappings": "AAAA;AACA,SAAS,..."
}
```

The mappings field contains VLQ Base64-encoded position data that maps each segment of the generated PHP code back to a position in the original Tyhp source.

## Enabling Source Map Generation

Source maps are generated as part of the tyhp build process. Enable them in your tyhp.json configuration:

```json
{
    "build": {
        "generateSourcemap": true
    }
}
```

When enabled, the build action generates a .php.map file alongside each compiled .php file:

```
build/
  App/
    Models/
      User.php
      User.php.map
    Services/
      Auth.php
      Auth.php.map
```

## Configuration Options

- build.generateSourcemap — Master switch to enable or disable source map generation (default: false).
- build.sourcemapIncludeContent — When true, embeds the original .tyhp source content in the sourcesContent array of the source map. This makes source maps self-contained but larger (default: false).

## Source Mapping URL

When source maps are generated, the compiler appends a sourceMappingURL comment to the end of each compiled PHP file, linking it to its source map:

```php
<?php
declare(strict_types=1);

namespace App\Models;

class User {
    // ... compiled code ...
}

//# sourceMappingURL=User.php.map
```

## Inline Source Maps

As an alternative to external .php.map files, source maps can be embedded directly in the PHP output as a base64 data URL comment. This avoids the need for separate .map files but increases the size of the PHP output. This mode is available for special use cases but external source maps are recommended for most workflows.

## How Source Maps Are Used

Source maps are consumed by several Tyhp tools:

- XDebug Proxy — The tyhp xdebug_proxy command loads source maps to translate breakpoints and stack traces between .tyhp and .php locations during debugging.
- Language Server — The Tyhp LSP server can use source maps to correlate compiled output with source files for navigation and diagnostic features.
- Error Mapping — When a PHP runtime error references a line in a compiled .php file, source maps allow tooling to map it back to the original .tyhp line for developer-facing output.

## Validation

The Tyhp compiler includes a source map validator that can verify the correctness of generated source maps. It checks VLQ encoding integrity, mapping coverage, source index bounds, and line count consistency. Validation can be enabled during builds for quality assurance.

:::note
Source map generation adds a small amount of overhead to the build process. If you do not need debugging support or source-mapped error reporting, leave build.generateSourcemap set to false for the fastest builds.
:::
