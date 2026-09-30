using System.Collections.Frozen;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Configuration options for the compilation service.
    /// Controls threading, caching, profiling, progress reporting, memory management,
    /// and tyhpdef discovery settings.
    /// </summary>
    public class CompilationOptions
    {
        private int _maxThreads = -1;

        /// <summary>
        /// Gets or sets the maximum number of concurrent parsing threads.
        /// Use -1 for unlimited parallelism (bounded by available CPU cores).
        /// Values less than -1 or equal to 0 are invalid and will be reset to -1.
        /// Default: -1 (unlimited).
        /// </summary>
        public int MaxThreads
        {
            get => this._maxThreads;
            set
            {
                // Validate: must be -1 or positive integer
                if (value < -1 || value == 0)
                {
                    this._maxThreads = -1; // Default to unlimited
                }
                else
                {
                    this._maxThreads = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets whether to use AST caching for parsed files.
        /// When enabled, parsed files are cached and reused if their hash matches.
        /// Default: true.
        /// </summary>
        public bool EnableAstCache { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to report ANTLR ambiguities during parsing.
        /// Enables LL_EXACT_AMBIG_DETECTION mode for detailed grammar analysis.
        /// Default: false.
        /// </summary>
        public bool ReportAmbiguities { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to enable parser profiling.
        /// Collects detailed performance metrics for each grammar decision point.
        /// Default: false.
        /// </summary>
        public bool EnableProfiling { get; set; } = false;

        /// <summary>
        /// Gets or sets the progress callback for reporting compilation progress.
        /// Receives updates about files processed, errors, memory usage, etc.
        /// Default: null (no progress reporting).
        /// </summary>
        public IProgress<CompilationProgress>? Progress { get; set; } = null;

        /// <summary>
        /// Compile-target used when <see cref="PhpVersion"/> is missing/empty (Story 20.5).
        /// </summary>
        public const string DefaultPhpVersionWhenUnset = "8.2";

        private string _phpVersion = DefaultPhpVersionWhenUnset;

        /// <summary>
        /// Target PHP version for emit, checker gates, and package tyhpdef filtering
        /// (e.g. <c>8.2</c>, <c>8.4</c>). Package symbols are filtered by the binder
        /// against this value — not by loading a per-minor <c>tyhpdef/php-8.x</c> package.
        /// Defaults to <see cref="DefaultPhpVersionWhenUnset"/> so options constructed
        /// without going through <see cref="FromProject"/> (tooling, language server,
        /// in-memory parsing) target the same version as an unset project config.
        /// Setting this also updates <see cref="Checker"/>.<see cref="CheckerOptions.PhpVersion"/>
        /// so the binder and checker cannot silently disagree on the target version; assign
        /// <see cref="Checker"/> as a whole afterward if it needs a different value.
        /// </summary>
        public string PhpVersion
        {
            get => _phpVersion;
            set
            {
                _phpVersion = value;
                Checker.PhpVersion = value;
            }
        }

        /// <summary>
        /// True when <see cref="PhpVersion"/> was empty and defaulted to
        /// <see cref="DefaultPhpVersionWhenUnset"/>. Checker emits 4306 once per compilation.
        /// </summary>
        public bool PhpVersionWasDefaulted { get; set; }

        /// <summary>
        /// If <see cref="PhpVersion"/> is missing/whitespace, default to
        /// <see cref="DefaultPhpVersionWhenUnset"/> and mark <see cref="PhpVersionWasDefaulted"/>.
        /// Loaded package tyhpdefs then filter against that target via the same binder
        /// <c>declare(php=…)</c> / <c>#[\Tyhp\Php]</c> evaluator used for user files.
        /// </summary>
        public void ApplyMissingPhpVersionDefault()
        {
            if (!string.IsNullOrWhiteSpace(PhpVersion))
            {
                return;
            }

            PhpVersion = DefaultPhpVersionWhenUnset;
            PhpVersionWasDefaulted = true;
            Checker.PhpVersion = DefaultPhpVersionWhenUnset;
            Checker.PhpVersionWasDefaulted = true;
        }

        /// <summary>
        /// Glob patterns for additional project tyhpdef/tyhp overlay files to load.
        /// </summary>
        public IReadOnlyList<string> TyhpdefIncludePaths { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Glob patterns for project-owned overlay tyhpdefs loaded after includes (last wins).
        /// </summary>
        public IReadOnlyList<string> TyhpdefOverlayPaths { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Glob patterns for tyhpdef/tyhp overlay files to exclude after discovery.
        /// </summary>
        public IReadOnlyList<string> TyhpdefExcludePaths { get; set; } = Array.Empty<string>();

        /// <summary>
        /// When false, package/project <c>overlay</c> globs are discovered but not bound
        /// (Layer 1 include baseline only). Used by <c>tyhp overlay stamp</c>.
        /// </summary>
        public bool ApplyTyhpdefOverlays { get; set; } = true;

        /// <summary>
        /// Elevates overlay stamp-mismatch warnings to errors (<c>--strict</c> / <c>build.strictMode</c>).
        /// </summary>
        public bool StrictMode { get; set; }

        /// <summary>
        /// Warning codes to drop during this compilation (from <c>suppressWarnings</c>).
        /// Errors are never dropped.
        /// </summary>
        public IReadOnlySet<MessageCode> SuppressedWarnings { get; set; } = FrozenSet<MessageCode>.Empty;

        /// <summary>
        /// Project root directory used to resolve tyhpdef include/exclude globs.
        /// </summary>
        public string ProjectPath { get; set; } = Directory.GetCurrentDirectory();

        /// <summary>
        /// When true, <c>.tyhp</c>/<c>.tyhpdef</c> files may omit open tags and <c>?&gt;</c> is an error.
        /// </summary>
        public bool Tagless { get; set; } = false;

        /// <summary>
        /// Type-checker behavior options. Hardcoded defaults until Story 10 config parsing lands.
        /// </summary>
        // PLACEHOLDER_STORY_10: Read checker configuration from Project config
        public CheckerOptions Checker { get; set; } = new();

        /// <summary>
        /// When true, the check phase is skipped after binding (parse and bind still run).
        /// </summary>
        public bool SkipChecking { get; set; } = false;

        /// <summary>
        /// Creates compilation options populated from project configuration.
        /// </summary>
        public static CompilationOptions FromProject(Tyhp.Config.Project? project, Action<CompilationOptions>? configure = null)
        {
            project ??= Tyhp.Config.Project.Singleton;

            var options = new CompilationOptions();
            if (project != null)
            {
                options.PhpVersion = project.PhpVersion;
                options.PhpVersionWasDefaulted = project.PhpVersionWasDefaulted;
                options.TyhpdefIncludePaths = project.TyhpdefIncludePaths;
                options.TyhpdefOverlayPaths = project.TyhpdefOverlayPaths;
                options.TyhpdefExcludePaths = project.TyhpdefExcludePaths;
                options.ProjectPath = project.GetProjectPath();
                options.Tagless = project.Tagless;
                options.EnableAstCache = !project.NoCache;
                options.Checker = CheckerOptions.FromProject(project);
                options.StrictMode = project.Strict;
                options.SuppressedWarnings = project.SuppressedWarnings;
            }

            configure?.Invoke(options);
            return options;
        }

        private int _garbageCollectInterval = 1000;

        /// <summary>
        /// Gets or sets the interval for triggering garbage collection during parsing.
        /// GC.Collect() is called every N files. Use 0 to disable forced GC.
        /// Values less than 0 are invalid and will be reset to 0 (disabled).
        /// Default: 1000.
        /// </summary>
        public int GarbageCollectInterval
        {
            get => this._garbageCollectInterval;
            set
            {
                // Validate: must be non-negative
                if (value < 0)
                {
                    this._garbageCollectInterval = 0; // Default to disabled
                }
                else
                {
                    this._garbageCollectInterval = value;
                }
            }
        }

        private long _preReadThreshold = 1024L * 1024L * 1024L; // 1GB
        private int _preReadMinFiles = 1000;

        /// <summary>
        /// Gets or sets the byte threshold for pre-reading files into memory.
        /// If the total file size is below this threshold and file count meets minimum,
        /// all files are pre-read before parsing for better cache locality.
        /// Values less than 0 are invalid and will be reset to 0 (disabled).
        /// Default: 1073741824 (1GB).
        /// </summary>
        public long PreReadThreshold
        {
            get => this._preReadThreshold;
            set
            {
                // Validate: must be non-negative
                if (value < 0)
                {
                    this._preReadThreshold = 0; // Default to disabled
                }
                else
                {
                    this._preReadThreshold = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the minimum file count required to trigger pre-reading.
        /// Pre-reading only occurs if file count >= this value AND total size <= PreReadThreshold.
        /// Values less than 0 are invalid and will be reset to 0 (disabled).
        /// Default: 1000.
        /// </summary>
        public int PreReadMinFiles
        {
            get => this._preReadMinFiles;
            set
            {
                // Validate: must be non-negative
                if (value < 0)
                {
                    this._preReadMinFiles = 0; // Default to disabled
                }
                else
                {
                    this._preReadMinFiles = value;
                }
            }
        }
    }
}
