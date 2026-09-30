namespace Tyhp.Domain.Services
{
    /// <summary>
    /// How tyhpdef generation chooses its input (CLI harvest or compile-time library tyhpdef).
    /// </summary>
    public enum TyhpdefGenerationMode
    {
        /// <summary>Generate from a PHP extension using Reflection (<c>--ext-name</c>).</summary>
        PhpExtension,

        /// <summary>Generate from a Composer package's autoloaded PHP files (<c>--package-path</c>).</summary>
        ComposerPackage,

        /// <summary>Generate from PHP source files (<c>--source</c>).</summary>
        PhpSourceFiles,

        /// <summary>Generate from compiled Tyhp public API (<c>tyhp build</c> only).</summary>
        TyhpCode,

        /// <summary>Scan Composer <c>vendor/</c> plus root <c>ext-*</c> (Story 21.5, <c>--vendor</c>).</summary>
        Vendor,
    }

    /// <summary>
    /// Parsed options for <c>tyhp generate_tyhpdef</c> (Reflection / PHP source) and library tyhpdef during build.
    /// </summary>
    public sealed record TyhpdefGenerationOptions
    {
        /// <summary>Which generation mode to run.</summary>
        public TyhpdefGenerationMode Mode { get; init; }

        /// <summary>PHP extension name (for <see cref="TyhpdefGenerationMode.PhpExtension"/>).</summary>
        public string? ExtensionName { get; init; }

        /// <summary>Path to a Composer package directory (CLI: <c>--package-path</c>).</summary>
        public string? PackagePath { get; init; }

        /// <summary>
        /// Glob patterns for PHP source files (CLI: <c>--source</c>).
        /// Resolved later with the same matcher as <c>Project.GetProjectSourceFiles()</c>.
        /// </summary>
        public List<string> SourcePaths { get; init; } = [];

        /// <summary>
        /// Directory for generated <c>.tyhpdef</c> files.
        /// Default: <c>{projectRoot}/tyhpdef/</c> when a project is loaded, otherwise <c>{cwd}/tyhpdef/</c>.
        /// </summary>
        public string OutputDirectory { get; init; } = "";

        /// <summary>
        /// Explicit output file path (<c>--output-file</c>). Relative paths resolve against
        /// <see cref="OutputDirectory"/> unless absolute.
        /// </summary>
        public string? OutputFileName { get; init; }

        /// <summary>
        /// Unused for Composer/source (always PHP-source harvest). For extensions this is always true
        /// (Reflection harvest requires PHP).
        /// </summary>
        public bool PreferPhpRuntime { get; init; }

        /// <summary>User PHP binary (<c>--php</c>). Null means Tyhp-managed PHP.</summary>
        public string? PhpExecutablePath { get; init; }

        /// <summary>Managed PHP cache override (<c>--php-runtime-dir</c> / <c>TYHP_PHP_RUNTIME_DIR</c>).</summary>
        public string? PhpRuntimeDir { get; init; }

        /// <summary>Skip managed PHP patch auto-update.</summary>
        public bool NoPhpRuntimeUpdate { get; init; }

        /// <summary>Re-reflect even if snapshots already exist.</summary>
        public bool RefreshSnapshots { get; init; }

        /// <summary>Managed PHP minor versions (<c>--php-targets</c>). Illegal with <c>--php</c>.</summary>
        public List<string> PhpTargets { get; init; } = [];

        /// <summary>Target PHP version string for output metadata (<c>--php-version</c>).</summary>
        public string? PhpVersion { get; init; }

        /// <summary>
        /// Directory for Reflection JSON snapshots (<c>tyhpdef_gen/snapshots/{minor}/</c>).
        /// Default: <c>{projectRoot|cwd}/tyhpdef_gen/snapshots</c>.
        /// </summary>
        public string? SnapshotDirectory { get; init; }

        /// <summary>Timeout for the Reflection PHP process (default 60000 ms, one process per extension).</summary>
        public int PhpProcessTimeoutMs { get; init; } = 60_000;

        /// <summary>php.net manual language for Reflection harvest. Default <c>en</c> (or project locale).</summary>
        public string Locale { get; init; } = "en";

        /// <summary>
        /// Emit <c>/** */</c> (default true). <c>--no-docs</c> skips emit and the php.net manual download.
        /// </summary>
        public bool IncludeDocComments { get; init; } = true;

        /// <summary>Include and mark deprecated items (default true; <c>--no-deprecated</c> skips).</summary>
        public bool IncludeDeprecated { get; init; } = true;

        /// <summary>Include <c>@internal</c> items (default false).</summary>
        public bool IncludeInternal { get; init; }

        /// <summary>Also collect Composer <c>autoload-dev</c> (<c>--include-dev</c>).</summary>
        public bool IncludeDev { get; init; }

        /// <summary>CLI <c>tyhpdef/</c> layout: <c>file</c> (default), <c>namespace</c>, or <c>type</c>.</summary>
        public string Split { get; init; } = "file";

        /// <summary>Fail if Layer 2 stub cache is missing (default false).</summary>
        public bool RequireStubs { get; init; }

        /// <summary>
        /// Override for the Layer 2 stub corpus directory (tests / <c>TYHP_STUB_CACHE</c>).
        /// When null, Tyhp uses <c>tools/stub-cache/</c> under the repo (or the env override).
        /// </summary>
        public string? StubCacheDirectory { get; init; }

        /// <summary>
        /// Download stub corpora into the cache when it is missing.
        /// CLI Reflection harvest sets this true; unit tests leave it false so they never hit the network.
        /// </summary>
        public bool FetchStubCache { get; init; }

        /// <summary>Overwrite existing tyhpdef files (default false).</summary>
        public bool Overwrite { get; init; }

        /// <summary>Apply overlays and compatibility-verify final forms (<c>--verify</c>).</summary>
        public bool Verify { get; init; }

        /// <summary>Directory or file to parse-check (<c>--validate</c>).</summary>
        public string? ValidatePath { get; init; }

        /// <summary>
        /// Existing tyhpdef tree to audit against stub corpora (<c>--audit-stubs</c>).
        /// </summary>
        public string? AuditStubsPath { get; init; }

        /// <summary>Optional markdown output path for <c>--audit-stubs</c> (<c>--out</c>).</summary>
        public string? AuditOutPath { get; init; }

        /// <summary>
        /// Directories of existing <c>tyhpdef/*</c> wrappers to index for PHP-source harvest
        /// <c>extern</c> classification. <see langword="null"/> discovers the runtime
        /// packages root (<c>TYHP_RUNTIME_SRC</c>, then a sibling
        /// <c>tyhp-runtime-src/packages</c>). An empty list disables the catalog
        /// (every foreign name is unknown origin).
        /// </summary>
        public IReadOnlyList<string>? CatalogRoots { get; init; }
    }
}
