# Grant of Rights in Tyhp to The PHP Group

**Grantor:** Anthony Rainer, author and copyright owner of Tyhp
**Grantee:** The PHP Group and the PHP project (defined in Section 2)
**Effective date:** the date this document is first published in the
`tyhpproject/tyhp` repository, as recorded by its commit history

## 1. Purpose

Tyhp is a typed superset of the PHP language. I built it because I want PHP
itself to have these features. This document gives the PHP project permission to
take anything it wants from Tyhp — syntax, semantics, type rules, runtime
behavior, source code, tests, documentation — and make it native PHP, under
PHP's own terms, with no attribution, no notice requirements, and no obligation
back to me.

The Apache License 2.0 that covers Tyhp already lets anyone copy the code, but it
attaches conditions (retained notices, a copy of the Apache license, change
markings) that do not fit the PHP License or the php-src codebase. This grant
removes those conditions for the PHP project. It is an additional permission on
top of Apache 2.0, not a replacement for it.

## 2. Who receives this grant

"Grantee" means all of the following, together and individually:

- **The PHP Group**, as the steward of the PHP language.
- **The PHP project**, meaning the official repositories and work products of
  the PHP language, including but not limited to `php-src`, `php-doc` and the
  language documentation, the PHP RFC process and RFC texts, PECL, official
  extensions, official tooling, and their successors under any name.
- **Contributors to the PHP project**, when acting to propose, implement,
  document, test, or maintain PHP or its official components, including work
  done in RFCs, pull requests, forks, and branches intended for the PHP project.
- **Recipients of PHP**, meaning anyone who receives PHP or any official PHP
  component that incorporates material covered by this grant. This is so that
  material taken from Tyhp can flow to PHP's users under PHP's license alone.

This grant is addressed to the PHP project. It does not enlarge or reduce the
rights of anyone else under the Apache License 2.0.

## 3. What is covered

"Covered Works" means everything I have authored and published, in any version,
past or future, under the Tyhp name, including:

- The compiler, CLI, language server, type checker, binder, emitter, test suites,
  and all other code in `github.com/tyhpproject/tyhp`.
- The runtime packages and type definitions (`.tyhpdef` files authored by me) in
  `github.com/tyhpproject/tyhp-runtime-src` and the packages published under
  `github.com/tyhpproject-packages`.
- The language documentation and site generator in
  `github.com/tyhpproject/tyhp-docs-src` and the published documentation site.
- The agent handbook and language guide in
  `github.com/tyhpproject/tyhp-ai-dev-guide`.
- The editor integrations in `github.com/tyhpproject/tyhp-ide-plugin`.
- Any other Tyhp repository, package, or publication I release later, unless it
  carries an express statement that it is outside this grant.

"Covered Design" means the language design of Tyhp regardless of its form of
expression: grammar, keywords, syntax, operators, type system, generics, structs,
nullability rules, type inference and narrowing rules, diagnostics and their
wording, standard library and runtime API shapes, compilation strategies,
PHP-emission strategies, and all other ideas, methods, and behaviors that Tyhp
implements or describes.

## 4. Rights granted

Subject only to the exclusions in Section 6, I grant the Grantee a perpetual,
worldwide, irrevocable, non-exclusive, royalty-free, fully paid-up license and
permission to do all of the following, for any purpose connected with PHP:

1. **Use the Covered Design.** Implement, re-implement, adapt, extend, alter,
   rename, or partially adopt any part of the Covered Design in PHP, in any
   programming language, in any form.
2. **Copy the Covered Works.** Reproduce, translate, port, modify, and create
   derivative works of any part of the Covered Works, including verbatim copying
   of source code, tests, grammar, error messages, and documentation text.
3. **Incorporate and relicense.** Include any of the above in PHP or any
   official PHP component, and distribute it under the PHP License, the PHP
   Documentation License, or any other terms the PHP project chooses, now or in
   the future. The Grantee may sublicense these rights to recipients of PHP.
4. **Relabel.** Present adopted material as part of PHP, without describing it
   as derived from Tyhp.

## 5. Conditions waived

For the Grantee, I waive every condition and obligation that the Apache License
2.0 would otherwise impose on the Covered Works, including without limitation
Section 4 of that license. In particular, the Grantee is **not** required to:

- give recipients a copy of the Apache License 2.0;
- mark modified files as changed;
- retain any copyright, patent, trademark, or attribution notice from Tyhp;
- include or reproduce any NOTICE file, `THIRD_PARTY.md` content authored by me,
  or other attribution text;
- credit me, Tyhp, or the Tyhp project anywhere, in any form.

Attribution is welcome but is never required.

To the fullest extent permitted by law, I also waive, and agree not to assert
against the Grantee, any moral rights or similar rights of attribution or
integrity in the Covered Works.

## 6. Exclusions

This grant covers only what I own. It does not cover:

1. **Third-party material.** Works owned by others that Tyhp downloads, reads,
   or references, including PHP itself, the PHP manuals, the Psalm, PHPStan,
   Phan, and PhpStorm stub corpora, StaticPHP and Homebrew artifacts, and any
   NuGet, Composer, or npm dependency. `THIRD_PARTY.md` in the `tyhp`
   repository lists these. Their own licenses continue to apply.
2. **Contributions accepted outside the Contributor Agreement.** Code,
   documentation, or other material contributed to a Covered Work by a person
   other than me is covered by this grant when it was Submitted under the Tyhp
   Contributor Agreement (`cla.md` in the `tyhp` repository), which licenses
   contributions to me for redistribution under this grant. Material from
   another person that was not accepted under that agreement is excluded to the
   extent that person holds copyright in it. As of the Effective Date I am the
   sole author of the Covered Works.
3. **The Tyhp name and marks.** The name "Tyhp", any Tyhp logo, and any other
   Tyhp trademarks are not licensed by this document, except that the Grantee
   may use them factually to describe where a feature or piece of code came
   from, in RFCs, commit messages, changelogs, documentation, or discussion.

## 7. Patents

I do not currently hold any patents on Tyhp and I do not intend to seek any.
If I, or any entity I control, now hold or later obtain any patent claim that
would be infringed by implementing or using the Covered Design or the Covered
Works, I grant the Grantee a perpetual, worldwide, irrevocable, royalty-free
license under that claim to make, have made, use, sell, offer to sell, import,
and otherwise distribute PHP and any official PHP component, and I covenant not
to assert that claim against the Grantee or against any recipient of PHP for
that use. This covenant does not terminate for any reason, including litigation
between the parties on unrelated matters.

## 8. Nature of this grant

- **No acceptance needed.** This grant is effective on publication. The Grantee
  does not need to sign, acknowledge, notify me, or take any other action to
  rely on it.
- **No obligations.** Nothing in this document obliges the PHP project to adopt
  anything from Tyhp, to consult me, to keep any adopted feature compatible with
  Tyhp, or to do anything at all.
- **Irrevocable.** I cannot withdraw this grant for any Covered Work or Covered
  Design that has been published before a withdrawal notice. If I ever publish a
  new Tyhp release that I want to exclude from this grant, I must say so
  expressly in that release; the exclusion applies only to the newly excluded
  material and only going forward.
- **Runs with the work.** This grant binds me and my heirs, successors, and
  assigns, including anyone who later acquires copyright in the Covered Works.
- **Additional permission.** This document does not reduce any right the
  Grantee has under the Apache License 2.0 or under law. Where this document and
  the Apache License 2.0 differ, the Grantee may rely on whichever is more
  permissive to it.
- **Severability.** If any part of this grant is unenforceable, the rest remains
  in force, and the unenforceable part is to be read as narrowly as needed to
  make it enforceable.

## 9. No warranty

The Covered Works and Covered Design are provided "as is", without warranty of
any kind, express or implied, including warranties of title, non-infringement,
merchantability, or fitness for a particular purpose. I am not liable to the
Grantee or to anyone else for any damages arising from this grant or from use of
the Covered Works or Covered Design, under any legal theory, to the fullest
extent permitted by law.

## 10. Publication and authorship

I make this grant by publishing this document in the `tyhpproject/tyhp`
repository from my own account. No handwritten or electronic signature is
required for it to take effect. The commit that adds this file, and any later
commits that amend it, are the record of who made this grant and when. Any
amendment is effective only from the date of its commit and cannot narrow the
grant for material already published (see Section 8).

Anthony Rainer
Author and copyright owner, Tyhp
`github.com/tyhpproject`
