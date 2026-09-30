# Tyhp branding

This file is the source of truth for how Tyhp is named, described, and shown. Titles, taglines, descriptions, and public claims in the README, website, docs, CLI, packages, and editor clients must match this document. If a published page disagrees with this file, this file wins and the page should be updated.

The Apache License 2.0 (`LICENSE.txt`) covers the **code**. It does **not** grant rights to the Tyhp name, logos, or other marks (Apache 2.0, section 6). Those are described here.

Permission requests: [email@tyhplang.com](mailto:email@tyhplang.com).

---

## Identity

**Tyhp** is a typed superset of the PHP language. The compiler type-checks Tyhp source and emits readable PHP.

Tyhp is **not** PHP. It is not a PHP extension, not a PHP runtime, and not affiliated with, endorsed by, or sponsored by the PHP Group.

The usual comparison is TypeScript’s relationship to JavaScript: a superset language that compiles to the original runtime’s language. TypeScript is not JavaScript. Tyhp is not PHP.

---

## Official names

| What | Use this | Do not use this |
| --- | --- | --- |
| Language / project | **Tyhp** | TYHP, TyHP, `TyHP`, “typed PHP”, “a PHP” |
| CLI binary | `tyhp` | `tyhplang`, `php-tyhp` |
| Source files | `.tyhp`, open tag `<?tyhp` | treating `.tyhp` as PHP |
| Declaration files | `.tyhpdef`, open tag `<?tyhpdef` | `.d.ts` as the file type name |
| Website | [tyhplang.com](https://tyhplang.com) | using **tyhplang** as a product name |
| GitHub org | `tyhpproject` | |
| Composer vendor | `tyhp/` (`tyhp/compiler`, `tyhpdef/php`, `tyhp/core`, …) | |
| Editor clients | **Tyhp Language** (`tyhp-lang`) | `tyhp` as the editor product name (that is the CLI) |

**Tyhp** is a name, not an acronym. The letters nod to PHP; that is etymology, not a claim that Tyhp is PHP.

Spell it **Tyhp** in prose (capital T, lowercase yhp). Use `tyhp` only for the CLI, package ids, file extensions, and similar machine names.

---

## Canonical copy

Use these strings as written. Do not paraphrase them into something that claims Tyhp is PHP, or into marketing noise.

### Tagline

> A typed superset of the PHP language.

Use this under the logo (README, homepage, social cards).

### Title / heading

> Tyhp is a typed superset of the PHP language.

Use this as the homepage `<h1>` and anywhere a full-sentence title is needed.

### Short description (meta, one-liner)

> Tyhp is a typed superset of the PHP language. The compiler type-checks Tyhp source and emits readable PHP.

Use this for `<meta name="description">`, Packagist/GitHub summaries, and similar.

### Secondary lines (optional)

These are accurate and allowed. They describe the **output**, not the identity of the language:

- **Compile with Tyhp. Deploy with PHP.**
- **Write this. Ship PHP.** (only as a heading above a compile-to-PHP example)

### One-paragraph explanation

Tyhp is a typed superset of the PHP language. You write `.tyhp` (and can keep `.php` in the same project). The compiler type-checks the Tyhp source and emits readable PHP. Required types, null safety, generics, structs, `async`/`await`, extension methods, and expression trees are part of Tyhp; the output is ordinary PHP that runs on a PHP runtime.

### PHP Group disclaimer (required on public footers and the README)

> PHP is a trademark of the PHP Group. Tyhp is not affiliated with, endorsed, or sponsored by the PHP Group.

---

## Claims

### Say this

- Tyhp is a typed superset of the PHP language.
- The compiler emits readable PHP (currently 8.2–8.5).
- `.php` files in a Tyhp project are passed through unchanged.
- `<?tyhp` source requires types (or inference) and restricts some PHP features.
- Types are checked at compile time and Tyhp-only type syntax is erased in the output.
- You can mix `.tyhp` and `.php` in one project.
- Existing PHP libraries are described to the checker with `.tyhpdef` files.
- Some Tyhp features depend on small Composer packages (`tyhp/core`, `tyhp/async`, …).

### Do not say this

Do not claim, directly or by implication, that Tyhp **is** PHP.

| Avoid | Why |
| --- | --- |
| “Tyhp is a strongly typed PHP” | Bad English, and it calls Tyhp PHP. |
| “A strongly typed superset of the PHP language.” | Public wording is “A typed superset of the PHP language.” |
| “It is still PHP” / “still PHP at its core” | Same problem. |
| “typed PHP” as a product identity | The name is Tyhp. “Untyped PHP” is fine when you mean PHP code that lacks types. |
| “making PHP strongly typed” | Tyhp is a separate language. |
| “neither a separate language” | Tyhp **is** a language. |
| “full compatibility” / “full backward compatibility” | `<?tyhp` restricts some PHP features. |
| “eliminates entire categories of bugs” | Overstates what a type checker does. |
| “real type system” (as a slight at PHP) | Unnecessarily combative. |
| “the features PHP has been reaching for” | Bragging. List the features instead. |
| Performance or “zero overhead” claims you have not measured | Types are erased; some features still need runtime packages. |
| Affiliation with the PHP Group, php.net, or PHP.net projects | False. |

If a comparison is useful, use TypeScript/JavaScript, then stop. Do not pile on superlatives.

### Tone

Write like a compiler project, not a launch trailer. Prefer short factual sentences. Do not hype, do not dunk on PHP, and do not promise a complete toolchain the alpha has not shipped.

---

## Using the word “PHP” in Tyhp’s own copy

PHP is a trademark of the PHP Group. Tyhp follows the same kind of care we ask others to use with **Tyhp**.

**Allowed in our copy**

- “superset of the PHP language”
- “emits PHP”, “readable PHP”, “runs on a PHP runtime”
- “PHP 8.2–8.5” as a version range for emitted code
- “Composer packages”, “PHP extensions”, “`.php` files”
- The TypeScript/JavaScript analogy, which treats Tyhp as **not** PHP

**Not allowed in our copy**

- Calling Tyhp “PHP”, “a PHP”, or “typed PHP”
- Product names of the form “PHP Tyhp”, “php-tyhp”, or “Tyhp PHP”
- Anything that reads as official PHP, a PHP Group project, or a drop-in replacement **for** the PHP language itself

The PHP Group’s own guidance: do not put “PHP” in a product name; “Foo for PHP” is the pattern they suggest, not “PHP Foo” or “phpfoo”. See [php.net/license](https://www.php.net/license/index.php).

---

## Use of the Tyhp name

The name **Tyhp** is how people find this project. Using it in *your* product’s name ties your work to this one and sends people to the wrong issue tracker. Pick a name that stands on its own. If the work is useful, it will not need “Tyhp” in the title to be found.

This follows the same idea as the PHP Group’s rules for the name “PHP” (PHP License 3.01, clauses 3–4, and the [license FAQ](https://www.php.net/license/index.php)).

### You may

- Say that software **compiles** Tyhp, **emits** PHP from Tyhp, or **works with** Tyhp.
- Use **“Foo for Tyhp”** (or “Foo for the Tyhp compiler”) to mean a tool or library that targets Tyhp.
- Use `tyhp` in **technical identifiers** that are clearly yours: Git remotes, local folder names, CLI flags that *invoke* Tyhp, Composer `require` of official `tyhp/*` and `tyhpdef/*` packages.
- Quote the canonical tagline or description when **referring to** this project, unmodified.
- Report bugs, write tutorials, and review Tyhp under its real name.

### You may not (without prior written permission)

- Call a product **Tyhp**, or put **Tyhp** / **tyhp** in the product name (`Tyhp ORM`, `tyhpfoo`, `php-tyhp`, `Official Tyhp …`).
- Use the name **Tyhp** to endorse or promote a product, event, or service as if it came from this project.
- Use names that a reasonable person would read as official: `tyhpproject` (except linking to the real org), `tyhplang` as a product, confusingly similar spellings (`TyPHP`, `TPHP`, `TypedPHP`).
- Imply affiliation, partnership, or that Tyhp is PHP.

For written permission, contact [email@tyhplang.com](mailto:email@tyhplang.com).

Unmodified forks of this repository may keep the name **Tyhp** for the forked compiler. A **different** product built from a fork needs its own name.

---

## Use of the Tyhp logo

Official artwork (outlined triangle, full **Tyhp** wordmark, brand blue `#34A4F4`):

- [tyhp-logo.svg](https://tyhplang.com/assets/images/tyhp-logo.svg) — vector; transparent background
- [tyhp-logo-alt.svg](https://tyhplang.com/assets/images/tyhp-logo-alt.svg) — the same vector, published under the alternate filename

Source files for that lockup are `template/tyhp-logo-new.svg` and `template/tyhp-logo-new.png` in [tyhp-docs-src](https://github.com/tyhpproject/tyhp-docs-src). The docs build copies the SVG to both published filenames.

`template/tyhp-logo.svg` and `template/tyhp-logo-alt.svg` in that repo are the previous filled-triangle “Ty” lockup. Do not use them.

Copy the file to your own site or package. Do not hotlink the image from tyhplang.com.

### You may

- Use an **unmodified** official SVG or PNG to refer to Tyhp (documentation, a “compiled with Tyhp” note, a talk slide about Tyhp).
- Scale it proportionally. Keep clear space; do not crop the triangle or the wordmark.

### You may not (without prior written permission)

- Change colors, proportions, letterforms, or add outlines, slogans, or other marks on top of the logo.
- Use the logo as **your** product’s icon, app mark, or company mark.
- Combine it with another logo in a way that implies partnership or endorsement.
- Use the logo to suggest that Tyhp is PHP, or to decorate a product that is not Tyhp.
- Register the logo, a lookalike, or the word **Tyhp** as a trademark in any jurisdiction.

A logo next to your own brand is not permission to look like an official Tyhp product. When in doubt, use the word **Tyhp** in text and skip the mark.

---

## Where this copy is used

| Surface | Must match |
| --- | --- |
| README tagline | Tagline |
| README opening paragraphs | Identity + one-paragraph explanation |
| Homepage `<h1>` and intro | Title / heading + short description |
| `<meta name="description">` | Short description |
| Site and README footers | PHP Group disclaimer |
| “What is Tyhp?” docs / FAQ | Identity and claims sections |

Do not invent a second tagline for social posts, conference CFPs, or package listings.
