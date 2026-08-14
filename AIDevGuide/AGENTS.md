# Writing Tyhp in this project — start here

This project uses **Tyhp**: PHP 8.x plus static typing and a few additions. You already know PHP, so
you only need the deltas. These docs are structured so you load **only the section you need**:

1. **`QUICK_GUIDE.md`** — the index. One line per feature with an analogy to a language you know
   (C#/TS/…) and a `→` pointer to the file that explains it in full.
2. **`guide/`** — the language reference, one file per topic (`01-mental-model.md` …
   `29-php-mapping.md`). See `guide/00-index.md` for the list.
3. **`handbook/`** — project setup, autoloading, CLI/build, PHP interop, testing, worked examples,
   and runtime API signatures (`01`…`07`). See `handbook/00-index.md`.

**How to work:** skim `QUICK_GUIDE.md`, and **before you write any feature marked `→`, open that
section file** — do not reconstruct Tyhp syntax from the analogy alone. Browse the `00-index.md`
files (or list the folders) to see everything available. `⚠️` means "not supported yet — use the
noted alternative."

**The five things that trip up a PHP dev (get these right even before opening a file):**
- Values are **non-nullable by default** — write `?string` / `|null` to allow `null`.
- **Conditions must be `bool`** — `if ($x !== null)`, never `if ($x)`.
- **Every parameter, property, and (by default) return type needs a type**; annotate array literals too.
- **Constructors declare a return type**: `public function __construct(...): void {}`.
- Prefer `instanceof` or the `is`/`isa`/`isan` aliases (all equivalent).

Anything not covered behaves like plain PHP 8.x.
