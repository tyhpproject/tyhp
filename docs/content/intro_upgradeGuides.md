---
title: 'Upgrade Guides'
---

This page provides guidance on upgrading between Tyhp versions. As Tyhp evolves, new features may be added and syntax may change. This guide helps you navigate those transitions smoothly.

## Version Numbering

Tyhp uses semantic versioning (MAJOR.MINOR.PATCH) aligned with PHP compatibility:

- MAJOR version indicates the PHP major version that Tyhp targets (e.g., Tyhp 8.x targets PHP 8.x)
- MINOR version indicates new Tyhp features or breaking Tyhp syntax changes
- PATCH version indicates bug fixes and non-breaking improvements

## Upgrading to Tyhp 805

Tyhp 805 understands PHP 8.5 syntax (pipe `|>`, `(void)` cast, `clone(…)` / clone-with, attributes on top-level `const`, and related 8.4 parse completions). When `output.phpVersion` is set below the introducing PHP minor, the emitter rewrites those constructs for the lower target — you do not need to rewrite source solely to keep an older emit target.

## General Upgrade Steps

When upgrading to a new version of Tyhp, follow these steps:

1. Read the release notes for the new version to understand what has changed
2. Update the Tyhp compiler to the new version
3. Run tyhp lint on your project to identify any new errors or warnings introduced by the upgrade
4. Address any deprecation warnings — these indicate syntax or features that will be removed in a future major version
5. Run tyhp build and verify the compiled output works correctly
6. Update your tyhp.json configuration if new options are available or if defaults have changed

## Using Lint to Find Issues

The tyhp lint command is your primary tool for identifying upgrade issues. Run it after updating the compiler to get a complete list of errors and warnings:

```
# Check the entire project
tyhp lint

# Get machine-readable output for CI
tyhp lint --format=json

# Check a single file
tyhp lint --file=src/MyClass.tyhp
```

## Handling Deprecations

Tyhp follows a deprecation cycle for breaking changes. When a feature or syntax is deprecated:

1. A deprecation warning is emitted during compilation, explaining what is changing and what to use instead
2. The deprecated feature continues to work for at least one minor version
3. In the next major version, the deprecated feature is removed

Address deprecation warnings promptly to ensure a smooth transition when the next major version is released.

## Handling PHP Syntax Conflicts

When a new PHP version introduces syntax that conflicts with Tyhp syntax, Tyhp follows a defined migration path. See the New Syntax Creation page for the detailed process. In summary: Tyhp will deprecate its conflicting syntax, provide compile-time options to control which syntax is active during the transition period, and remove the old syntax in the next major version.

## Updating tyhp.json Configuration

New Tyhp versions may introduce new configuration options in tyhp.json. The compiler uses sensible defaults for all options, so your existing configuration will continue to work. However, you may want to review new options to take advantage of new features.

:::tip
After upgrading, run tyhp build --verbose to see detailed output including any configuration options that are using default values. This helps you discover new options that may be relevant to your project.
:::

## Regenerating Tyhpdef Files

After a major Tyhp upgrade, review your tyhpdef files for compatibility. Automatic regeneration (`tyhp generate_tyhpdef`) is **not** in this alpha. Update stubs by hand, and bump `tyhp/php` when a new stubs package is published.
