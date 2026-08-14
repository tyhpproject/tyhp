# Runtime self-host conformance

Recompile `runtime/packages/*/tyhp_src/` with the current compiler and diff against committed `src/`.

Activated in Story 07 Wave B after Story 10. The .NET test `SelfHostRuntimeConformanceTests` in
`tests/Tyhp.Tests/Conformance/ConformanceSuiteTests.cs` runs this check for `core`, `decimal`, `async`,
and `lambda`.

`SelfHostRunner.ExpectedToCompileAllowlist` tracks packages that must compile and match committed PHP.
The allowlist starts empty while no runtime package self-compiles yet. Any package that *does* compile is
always diff-checked against `src/`, so a building-but-wrong package cannot be masked by others still
failing to build. Add package names to the allowlist as they begin to self-compile.

See `ROADMAP.md` and `CONVENTIONS.md` §5.
