# Tyhp documentation

Docs are authored as Markdown with YAML front matter and built into a multi-page static HTML site.

## Build

```bash
cd docs
composer install   # first time / after dependency changes
php generate_docs.php
```

Output is written to `output/` and zipped as `output.zip`.

## Content layout

| Path | Role |
|------|------|
| `content/toc.json` | JSON5 table of contents (section metadata + item list) |
| `content/*.md` | Doc pages referenced by the TOC |
| `content/_template.md` | Dialect reference / examples (listed in TOC) |
| `template/` | HTML templates and theme SCSS |
| `generator-src/` | PHP build pipeline (`Tyhp\Docs\`) |

TOC item entries must be `.md` files that exist under `content/`. Missing or non-Markdown items fail the build.

## Front matter

```yaml
---
title: Page title
status:
  tier: 0
  story: "08"
  state: planned
---
```

- `title` (required) — used for the page heading, `<title>`, and sidebar label
- `status` (optional) — implementation badge (`tier`, `story` or `stories`, `state`)

## Markdown dialect

Standard CommonMark: headings, paragraphs, lists, emphasis, links, inline code, and raw HTML (allowed).

### Admonitions

Opening line is `:::type` (optional title after the type). Closing line is exactly `:::`.

| Form | Renders as |
|------|------------|
| `:::tip` / `:::note` / `:::warning` / `:::danger` | Callout boxes |
| `:::warning Custom title` | Callout with custom title |
| `:::alert{style=primary}` | Bootstrap alert (`style` = primary, secondary, …) |
| `:::member[identifier]` | Member/API card with `identifier` as the heading |

Inner content is Markdown (rendered once; nesting admonitions is not required).

### Fenced code

Normal fences use language tags and themed templates:

````markdown
```tyhp
<?tyhp
string $x = "hello";
```

```php
<?php
$x = "hello";
```

```json
{ "ok": true }
```
````

Unknown languages use the generic code-block template.

### Magic YAML fences

If the info string contains `status`, `classdef`, or `functiondef` (alone or after `yaml`), the fence body is YAML and rendered specially:

````markdown
```status
tier: 1
story: "16"
state: planned
```

```classdef
type: class
identifier: Example
members:
  - type: property
    def: { modifiers: public, type: string, identifier: $name }
```

```functiondef
modifiers: public static
identifier: example
parameters:
  - { type: string, identifier: $name }
returnType: void
```
````

## Deferred features

Not supported in the Markdown generator yet (were only used in the old template):

- `includeContent` / placeholder replacements
- `*CodeFile` / file-based code includes

Include fragments may still exist as JSON under `content/` for later work.

## One-shot converter

`tools/convert_json_to_md.php` converted the historical JSON5 pages to Markdown. It is kept for reference; content is now Markdown-only.
