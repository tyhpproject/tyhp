---
title: 'FAQ: Other'
---

## How can I contribute to Tyhp?

Tyhp is an open-source project and welcomes contributions. See [CONTRIBUTING.md](https://github.com/tyhpproject/tyhp/blob/main/CONTRIBUTING.md) in the [tyhpproject/tyhp](https://github.com/tyhpproject/tyhp) repository.

## Where do I report bugs?

Report bugs on the [GitHub issue tracker](https://github.com/tyhpproject/tyhp/issues). Include the Tyhp source code that triggers the issue, the expected behavior, the actual behavior (error message or incorrect output), your Tyhp compiler version (run `tyhp version`), and the target PHP version from your `tyhp.json`. Providing a minimal reproduction case helps resolve issues faster.

## Is there a Tyhp community?

Yes. The Tyhp community is centered around the project's repository and issue tracker. This is where discussions about language design, new features, and implementation details take place. Feature requests and design proposals are welcome as issues.

## What is on the roadmap?

The Tyhp roadmap includes ongoing work across several areas:

- Language features — continued expansion of the type system, pattern matching, and language ergonomics.
- Tooling — language server, sourcemaps, and richer IDE integrations (not in this alpha).
- Performance — incremental compilation improvements, dependency-aware rebuilds, and build caching.
- Ecosystem — tyhpdef packages for popular PHP libraries and frameworks, and deeper Composer integration.
- Documentation — expanded guides, tutorials, and reference documentation.

## How do I request a feature?

Open an issue in the project's issue tracker with a clear description of the feature, the problem it solves, and ideally example Tyhp code showing how the feature would be used. Design proposals that consider both the Tyhp syntax and the compiled PHP output are especially helpful.

## How does Tyhp compare to TypeScript?

Tyhp does for PHP what TypeScript does for JavaScript. Both add a strong type system on top of a dynamically-typed language and compile down to the original language. Key similarities include type erasure, gradual adoption, and maintaining ecosystem compatibility. Key differences are the target runtime (PHP vs JavaScript) and language-specific features — Tyhp adds structs, operator overloads, extension methods, and decimal types which are tailored to PHP development patterns.

## Can I gradually adopt Tyhp in an existing PHP project?

Yes, gradual adoption is a core design goal. You can add Tyhp files alongside existing PHP files in the same project. Start by writing tyhpdef files to describe your existing PHP code, then write new features in Tyhp. The compiled output integrates seamlessly with your existing PHP codebase since it produces standard, readable PHP. There is no need to convert your entire project at once.

## What does 'Tyhp' stand for?

Tyhp is a play on "typed PHP". It is also an anagram of the letters in PHP with the addition of a couple of other letters, reflecting that Tyhp builds on top of PHP.
