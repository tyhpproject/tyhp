namespace Tyhp.Config
{
    using System.Collections.Frozen;
    using System.Runtime.InteropServices;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.FileSystemGlobbing;
    using Tyhp.CLI;
    using Tyhp.Domain.Diagnostics;
    using Tyhp.Domain.Exceptions;
    using Tyhp.Domain.Services;
    using Tyhp.Extensions;
    using Tyhp.XDebugProxy.Config;

    public sealed class Project
    {
        /// <summary>
        /// Process-wide active project for CLI/LSP cache pathing and fallback
        /// <see cref="Domain.Services.CompilationOptions.FromProject"/>. The constructor does not
        /// set this; <c>TyhpHostedService</c> and the language server assign it. Tests that need
        /// the fallback must set and restore it themselves.
        /// </summary>
        public static Project? Singleton = null;

        #region configuration items
        
        /// <summary>
        /// The directory to store the cache files. If not specified, the default is the system's local application data directory.
        /// </summary>
        public string? CacheDir {get; internal set;}

        /// <summary>
        /// When true, the AST cache is neither read nor written for this run (<c>--no-cache</c>).
        /// An escape hatch for forcing a full re-parse when a stale cache is suspected.
        /// </summary>
        public bool NoCache {get; private set;}

        public string Locale {get; private set;}
        public bool BeQuiet {get; private set;}

        /// <summary>
        /// Warning codes listed in <c>tyhp.json</c> <c>suppressWarnings</c> (or CLI
        /// <c>--suppress-warnings</c>). Matching warning-severity diagnostics are dropped
        /// during build, lint, overlay, and language-server analysis. Errors are never dropped.
        /// </summary>
        public IReadOnlySet<MessageCode> SuppressedWarnings { get; private set; } = FrozenSet<MessageCode>.Empty;

        /// <summary>
        /// Optional process-id file path (<c>--pid-file</c>). Unset by default so Tyhp never
        /// writes into the user's project. When set, the host writes the current process id at
        /// start and deletes the file on shutdown.
        /// </summary>
        public string? PidFile { get; private set; }

        /// <summary>
        /// When true, actions that support it emit machine-readable JSON (<c>--json</c>).
        /// </summary>
        public bool JsonOutput { get; private set; }

        /// <summary>
        /// <c>--verbose</c> flag. Pass-through to <see cref="BuildConfig.Verbose"/>
        /// (parsed by Story 10's <see cref="BuildConfig.ApplyFrom"/>).
        /// </summary>
        public bool Verbose => this.Build.Verbose;

        /// <summary>
        /// <c>--dry-run</c> flag. Pass-through to <see cref="BuildConfig.DryRun"/>.
        /// </summary>
        public bool DryRun => this.Build.DryRun;

        /// <summary>
        /// <c>--strict</c> flag (treat warnings as errors). Pass-through to
        /// <see cref="BuildConfig.StrictMode"/>.
        /// </summary>
        public bool Strict => this.Build.StrictMode;

        /// <summary>
        /// <c>--clean</c> flag. Pass-through to <see cref="BuildConfig.CleanBeforeBuild"/>.
        /// </summary>
        public bool Clean => this.Build.CleanBeforeBuild;

        // used by the `help` action
        public string? Subject {get; private set;}

        public List<string> IncludePaths {get; private set;}

        public List<string> ExcludePaths {get; private set;}

        /// <summary>
        /// Glob patterns for additional tyhpdef/tyhp overlay files to load (from <c>tyhp.json</c> or CLI).
        /// Mirrors <see cref="TyhpdefOptions"/>.<see cref="TyhpdefConfig.Include"/>.
        /// </summary>
        public List<string> TyhpdefIncludePaths { get; private set; }

        /// <summary>
        /// Glob patterns for project-owned overlay tyhpdefs loaded after includes (last wins).
        /// Mirrors <see cref="TyhpdefOptions"/>.<see cref="TyhpdefConfig.Overlay"/> and top-level
        /// <c>overlay</c> in <c>tyhp.json</c>.
        /// </summary>
        public List<string> TyhpdefOverlayPaths { get; private set; }

        /// <summary>
        /// Glob patterns for tyhpdef/tyhp overlay files to exclude after discovery.
        /// Mirrors <see cref="TyhpdefOptions"/>.<see cref="TyhpdefConfig.Exclude"/>.
        /// </summary>
        public List<string> TyhpdefExcludePaths { get; private set; }

        /// <summary>
        /// Target PHP version for tyhpdef selection (e.g. <c>8.2</c>, <c>8.4</c>).
        /// Mirrors <see cref="Output"/>.<see cref="OutputConfig.PhpVersion"/>.
        /// </summary>
        public string PhpVersion => this.Output.PhpVersion;

        /// <summary>
        /// True when <c>output.phpVersion</c> was unset in configuration and defaulted (Story 20.5).
        /// Mirrors <see cref="Output"/>.<see cref="OutputConfig.PhpVersionWasDefaulted"/>.
        /// </summary>
        public bool PhpVersionWasDefaulted => this.Output.PhpVersionWasDefaulted;

        /// <summary>
        /// Explicit file or directory paths passed on the command line (e.g. <c>tyhp lint path/to/dir</c>).
        /// </summary>
        public List<string> ExplicitPaths {get; private set;}

        /// <summary>
        /// When true, <c>&lt;?tyhp</c>/<c>&lt;?tyhpdef</c> open tags are optional and <c>?&gt;</c> is an error.
        /// </summary>
        public bool Tagless { get; private set; }

        /// <summary>Project type: application (default) or library.</summary>
        public ProjectType Type { get; private set; } = ProjectType.Application;

        /// <summary>Output path and emitter targeting options.</summary>
        public OutputConfig Output { get; private set; } = new();

        /// <summary>Build pipeline and optimization options.</summary>
        public BuildConfig Build { get; private set; } = new();

        /// <summary>Type-checker behavior options.</summary>
        public CheckerConfig Checker { get; private set; } = new();

        /// <summary>Tyhpdef discovery glob patterns.</summary>
        public TyhpdefConfig TyhpdefOptions { get; private set; } = new();

        /// <summary>
        /// Lint diagnostic output format: <c>text</c> (default), <c>json</c>, or <c>sarif</c>.
        /// From <c>--format</c> or <c>lint.format</c> in <c>tyhp.json</c>.
        /// </summary>
        public string LintFormat { get; private set; } = "text";

        /// <summary>
        /// Optional single-file path for lint (<c>--file</c>). When null, lint uses the whole project
        /// (or <see cref="ExplicitPaths"/> when present).
        /// </summary>
        public string? LintFile { get; private set; }

        /// <summary>
        /// Whether to apply auto-fixes (<c>--fix</c> / <c>lint.fix</c>). Default false.
        /// </summary>
        public bool LintFix { get; private set; }

        /// <summary>
        /// XDebug proxy settings from <c>tyhp.json</c> <c>xdebugProxy.*</c> and CLI flags.
        /// </summary>
        public XDebugProxyConfig XDebugProxy { get; private set; } = new();

        #endregion configuration items

        private readonly IConfiguration _configuration;

        /// <summary>
        /// Configuration warnings collected during <see cref="ConfigChanged"/> that have not yet
        /// been transferred to a run's <see cref="Domain.Diagnostics.DiagnosticBag"/> or flushed
        /// to the console / stderr.
        /// </summary>
        private readonly List<(MessageCode Code, object[] Args)> _pendingConfigWarnings = new();

        /// <summary>
        /// Configuration errors collected during <see cref="ConfigChanged"/> (interpolation
        /// failures). Transferred as errors before output directories are created.
        /// </summary>
        private readonly List<(MessageCode Code, object[] Args)> _pendingConfigErrors = new();

        public Project(IConfiguration configuration)
        {
            this._configuration = configuration;
            this.Locale = "en-US";
            this.IncludePaths = new List<string>();
            this.ExcludePaths = new List<string>();
            this.TyhpdefIncludePaths = new List<string>();
            this.TyhpdefOverlayPaths = new List<string>();
            this.TyhpdefExcludePaths = new List<string>();
            this.ExplicitPaths = new List<string>();
            this.ConfigChanged();
            // Do not assign Singleton here. Tests construct many Project instances; a process-wide
            // write races under xUnit class parallelization. The CLI host and language server
            // publish the active instance after construction.
        }

        internal void ConfigChanged()
        {
            // Reload replaces prior pending diagnostics from the previous parse.
            this._pendingConfigWarnings.Clear();
            this._pendingConfigErrors.Clear();

            // needs to be first
            this.BeQuiet = this._configuration["quiet"].ParseBool();
            this.JsonOutput = this._configuration["json"].ParseBool();

            var pidFile = this._configuration["pid-file"];
            this.PidFile = string.IsNullOrWhiteSpace(pidFile) ? null : pidFile.Trim();

            this.CacheDir = this._configuration["cache-dir"] ?? null;
            this.NoCache = this._configuration["no-cache"].ParseBool();

            // Needs to be second
            this.Locale = this._configuration["locale"] ?? "en-US";
            System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(this.Locale);
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(this.Locale);

            this.ExplicitPaths = new List<string>();
            if (this._configuration.GetSection("path:0").Exists()) {
                for (int i = 0; i < 255; i++) {
                    if (!this._configuration.GetSection("path:" + i.ToString()).Exists()) {
                        break;
                    }
                    string? path = this._configuration.GetSection("path:" + i.ToString()).Value;
                    if (!String.IsNullOrWhiteSpace(path)) {
                        this.ExplicitPaths.Add(path);
                    }
                }
            }

            // help config options
            if (this._configuration["*action"] == Tyhp.Config.Action.help.ToString()) {
                this.Subject = this._configuration["subject"];
            }

            this.IncludePaths.Clear();
            this.IncludePaths.AddRange(this.ReadGlobList("include"));

            this.ExcludePaths.Clear();
            this.ExcludePaths.AddRange(this.ReadGlobList("exclude"));

            this.Tagless = this._configuration["source:tagless"].ParseBool();

            this.ApplySuppressedWarnings();

            this.Type = this.ParseProjectType();
            this.Output = new OutputConfig();
            this.Build = new BuildConfig();
            this.Checker = new CheckerConfig();
            this.TyhpdefOptions = new TyhpdefConfig();
            this.XDebugProxy = new XDebugProxyConfig();

            var warn = new Action<MessageCode, object[]>(this.WarnConfig);
            this.Output.ApplyFrom(this._configuration, warn);
            this.Build.ApplyFrom(this._configuration, warn);
            this.Checker.ApplyFrom(this._configuration);
            this.XDebugProxy.ApplyFrom(this._configuration);
            this.ApplyTyhpdefOptions();

            this.Build.GenerateTyhpdef ??= (this.Type == ProjectType.Library);

            ConfigInterpolator.Apply(this, this.ErrorConfig);

            // Lint config options (--format / --file / --fix, or lint.* in tyhp.json)
            var format = this._configuration["format"] ?? this._configuration["lint:format"];
            this.LintFormat = String.IsNullOrWhiteSpace(format) ? "text" : format.Trim();

            // CLI --file wins when present; otherwise lint.file from tyhp.json
            var file = this._configuration["file"] ?? this._configuration["lint:file"];
            this.LintFile = String.IsNullOrWhiteSpace(file) ? null : file.Trim();

            // CLI --fix wins when present (including --fix=false); otherwise lint.fix
            if (this._configuration.GetSection("fix").Exists())
            {
                this.LintFix = this._configuration["fix"].ParseBool();
            }
            else
            {
                this.LintFix = this._configuration["lint:fix"].ParseBool();
            }
        }

        /// <summary>
        /// Reads a glob list that <c>tyhp.json</c> supplies as an array (<c>include:0</c>,
        /// <c>include:1</c>, …) or that the command line supplies as a single comma-separated value.
        /// </summary>
        /// <remarks>
        /// .NET's command-line provider binds <c>--include=…</c> to the flat <c>include</c> key and
        /// never to the indexed keys a JSON array produces, so the two spellings have to be read
        /// separately. A command-line value replaces the whole array rather than appending to it,
        /// matching the "CLI flags override matching config keys" rule the help text documents.
        /// </remarks>
        private List<string> ReadGlobList(string key)
        {
            var globs = new List<string>();

            string? flatValue = this._configuration[key];
            if (!String.IsNullOrWhiteSpace(flatValue)) {
                globs.AddRange(flatValue.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

                return globs;
            }

            for (int i = 0; i < 255; i++) {
                var section = this._configuration.GetSection(key + ":" + i.ToString());
                if (!section.Exists()) {
                    break;
                }

                if (!String.IsNullOrWhiteSpace(section.Value)) {
                    globs.Add(section.Value);
                }
            }

            return globs;
        }

        /// <summary>
        /// Reads <c>suppressWarnings</c> from <c>tyhp.json</c>, or <c>--suppress-warnings</c> when
        /// that CLI flag is present (the flag replaces the JSON array, same as <c>--include</c>).
        /// Unknown tokens become <see cref="MessageCode.ConfigInvalidValue"/> and are skipped.
        /// </summary>
        private void ApplySuppressedWarnings()
        {
            this.SuppressedWarnings = FrozenSet<MessageCode>.Empty;
            DiagnosticBag.DefaultSuppressedWarnings = this.SuppressedWarnings;

            var raw = this._configuration.GetSection("suppress-warnings").Exists()
                ? this.ReadGlobList("suppress-warnings")
                : this.ReadGlobList("suppressWarnings");

            if (raw.Count == 0)
            {
                return;
            }

            var codes = new HashSet<MessageCode>();
            foreach (var entry in raw)
            {
                if (DiagnosticCodeParser.TryParse(entry, out var code))
                {
                    codes.Add(code);
                }
                else
                {
                    this.WarnConfig(MessageCode.ConfigInvalidValue, ["suppressWarnings", entry]);
                }
            }

            if (codes.Count > 0)
            {
                this.SuppressedWarnings = codes.ToFrozenSet();
            }

            DiagnosticBag.DefaultSuppressedWarnings = this.SuppressedWarnings;
        }

        /// <summary>
        /// Validates lint-specific configuration (format value, optional <c>--file</c> target).
        /// Call at the start of the lint action before the compilation pipeline.
        /// </summary>
        /// <param name="diagnostics">Bag that receives validation diagnostics.</param>
        /// <returns><see langword="true"/> when configuration is valid; otherwise <see langword="false"/>.</returns>
        public bool ValidateLintConfig(Domain.Diagnostics.DiagnosticBag diagnostics)
        {
            var isValid = true;

            if (!IsSupportedLintFormat(this.LintFormat))
            {
                diagnostics.AddError(
                    MessageCode.LintUnsupportedFormat,
                    this.GetConfigFilePathForDiagnostics(),
                    0,
                    0,
                    this.LintFormat);
                isValid = false;
            }

            if (String.IsNullOrWhiteSpace(this.LintFile))
            {
                return isValid;
            }

            string fullPath;
            try
            {
                // Resolve intermediate directory symlinks so --file paths that cross a link
                // (e.g. macOS /tmp → /private/tmp) compare equal to GetProjectPath().
                fullPath = PathCanonicalizer.GetCanonicalFullPath(this.LintFile);
            }
            catch (Exception ex)
            {
                diagnostics.AddError(
                    MessageCode.LintInvalidPath,
                    this.LintFile,
                    0,
                    0,
                    this.LintFile,
                    ex.Message);
                return false;
            }

            if (!File.Exists(fullPath))
            {
                diagnostics.AddError(
                    MessageCode.LintFileNotFound,
                    fullPath,
                    0,
                    0,
                    fullPath);
                return false;
            }

            var extension = Path.GetExtension(fullPath);
            if (!LintableExtensions.Any(ext => extension.Equals(ext, StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.AddError(
                    MessageCode.LintFileNotInProject,
                    fullPath,
                    0,
                    0,
                    fullPath);
                return false;
            }

            // Normalize to the resolved absolute path for downstream discovery.
            this.LintFile = fullPath;

            if (!this.IsLintFileInProject(fullPath))
            {
                diagnostics.AddError(
                    MessageCode.LintFileNotInProject,
                    fullPath,
                    0,
                    0,
                    fullPath);
                isValid = false;
            }

            return isValid;
        }

        private static bool IsSupportedLintFormat(string format)
            => String.Equals(format, "text", StringComparison.OrdinalIgnoreCase)
                || String.Equals(format, "json", StringComparison.OrdinalIgnoreCase)
                || String.Equals(format, "sarif", StringComparison.OrdinalIgnoreCase);

        private static readonly string[] LintableExtensions = [".tyhp", ".php", ".tyhpdef"];

        private bool IsLintFileInProject(string absolutePath)
        {
            var canonicalFile = PathCanonicalizer.GetCanonicalFullPath(absolutePath);

            var projectSources = this.GetProjectSourceFiles()
                .Select(static path => PathCanonicalizer.GetCanonicalFullPath(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (projectSources.Contains(canonicalFile))
            {
                return true;
            }

            // Minimum acceptance: a lintable source/definition extension under the project root.
            var extension = Path.GetExtension(canonicalFile);
            if (!LintableExtensions.Any(ext => extension.Equals(ext, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // Guard against prefix false-positives (e.g. /proj vs /project-other) and symlink
            // spelling mismatches between --file and GetProjectPath().
            return PathCanonicalizer.IsUnderRoot(canonicalFile, this.GetProjectPath());
        }

        private ProjectType ParseProjectType()
        {
            var typeValue = this._configuration["type"];
            if (String.IsNullOrWhiteSpace(typeValue))
            {
                return ProjectType.Application;
            }

            if (String.Equals(typeValue, "application", StringComparison.OrdinalIgnoreCase))
            {
                return ProjectType.Application;
            }

            if (String.Equals(typeValue, "library", StringComparison.OrdinalIgnoreCase))
            {
                return ProjectType.Library;
            }

            this.WarnConfig(MessageCode.ConfigInvalidProjectType, [typeValue]);
            return ProjectType.Application;
        }

        private void ApplyTyhpdefOptions()
        {
            this.TyhpdefOptions.Include.Clear();
            this.TyhpdefOptions.Overlay.Clear();
            this.TyhpdefOptions.Exclude.Clear();

            this.ReadIndexedStringList("tyhpdefInclude", this.TyhpdefOptions.Include);
            this.ReadIndexedStringList("tyhpdefOverlay", this.TyhpdefOptions.Overlay);
            this.ReadIndexedStringList("tyhpdef:overlay", this.TyhpdefOptions.Overlay);
            this.ReadIndexedStringList("overlay", this.TyhpdefOptions.Overlay);
            this.ReadIndexedStringList("tyhpdefExclude", this.TyhpdefOptions.Exclude);

            this.TyhpdefIncludePaths.Clear();
            this.TyhpdefIncludePaths.AddRange(this.TyhpdefOptions.Include);

            // Entries in the project `include` array that target tyhpdef definition files or
            // composer.json package manifests are loaded as type definitions (bound, never emitted)
            // rather than compiled as source. This lets a project pull in e.g. PHP extension
            // definitions or local runtime packages via a single `include` list, resolved
            // relative to tyhp.json.
            foreach (var includePattern in this.IncludePaths)
            {
                if (includePattern.EndsWith(".tyhpdef", System.StringComparison.OrdinalIgnoreCase)
                    || ComposerExtraTyhpPackageManifest.PatternMayMatchComposerJson(includePattern))
                {
                    this.TyhpdefIncludePaths.Add(includePattern);
                }
            }

            this.TyhpdefOverlayPaths.Clear();
            this.TyhpdefOverlayPaths.AddRange(this.TyhpdefOptions.Overlay);

            this.TyhpdefExcludePaths.Clear();
            this.TyhpdefExcludePaths.AddRange(this.TyhpdefOptions.Exclude);
        }

        private void WarnConfig(MessageCode code, object[] args)
        {
            if (this.SuppressedWarnings.Contains(code))
            {
                return;
            }

            // Defer emission: lint/build fold these into DiagnosticBag (JSON/SARIF stay clean on
            // stdout); version --json / tokenize / dump-ast flush to stderr; text actions flush
            // to the console. Always record so machine-readable formatters include them even
            // under --quiet.
            this._pendingConfigWarnings.Add((code, args ?? Array.Empty<object>()));
        }

        private void ErrorConfig(MessageCode code, object[] args)
        {
            this._pendingConfigErrors.Add((code, args ?? Array.Empty<object>()));
        }

        /// <summary>
        /// True when <see cref="ConfigChanged"/> recorded interpolation (or other config) errors
        /// that have not yet been transferred.
        /// </summary>
        public bool HasPendingConfigErrors => this._pendingConfigErrors.Count > 0;

        /// <summary>
        /// Moves pending configuration errors and warnings into <paramref name="diagnostics"/> and
        /// clears the pending lists. Used by lint/build so formatters (text/JSON/SARIF) include them.
        /// </summary>
        public void TransferPendingConfigWarningsTo(Domain.Diagnostics.DiagnosticBag diagnostics)
        {
            ArgumentNullException.ThrowIfNull(diagnostics);

            var path = this.GetConfigFilePathForDiagnostics();
            foreach (var (code, args) in this._pendingConfigErrors)
            {
                diagnostics.AddError(code, path, 0, 0, args);
            }

            foreach (var (code, args) in this._pendingConfigWarnings)
            {
                diagnostics.AddWarning(code, path, 0, 0, args);
            }

            this._pendingConfigErrors.Clear();
            this._pendingConfigWarnings.Clear();
        }

        /// <summary>
        /// Writes pending configuration diagnostics to stderr (machine-readable stdout stays clean)
        /// and clears the pending lists.
        /// </summary>
        public void EmitPendingConfigWarningsToStderr()
        {
            var path = this.GetConfigFilePathForDiagnostics();
            foreach (var (code, args) in this._pendingConfigErrors)
            {
                Message.TyhpErrorToStderr(path, 0, 0, (int)code, args);
            }

            if (!this.BeQuiet)
            {
                foreach (var (code, args) in this._pendingConfigWarnings)
                {
                    if (this.SuppressedWarnings.Contains(code))
                    {
                        continue;
                    }

                    Message.TyhpWarnToStderr(path, 0, 0, (int)code, args);
                }
            }

            this._pendingConfigErrors.Clear();
            this._pendingConfigWarnings.Clear();
        }

        /// <summary>
        /// Writes pending configuration diagnostics to the normal console diagnostic stream and
        /// clears the pending lists. Used by human-readable actions that have no diagnostic bag.
        /// </summary>
        public void EmitPendingConfigWarningsToConsole()
        {
            var path = this.GetConfigFilePathForDiagnostics();
            foreach (var (code, args) in this._pendingConfigErrors)
            {
                Message.TyhpError(path, 0, 0, (int)code, args);
            }

            if (!this.BeQuiet)
            {
                foreach (var (code, args) in this._pendingConfigWarnings)
                {
                    if (this.SuppressedWarnings.Contains(code))
                    {
                        continue;
                    }

                    Message.TyhpWarn(path, 0, 0, (int)code, args);
                }
            }

            this._pendingConfigErrors.Clear();
            this._pendingConfigWarnings.Clear();
        }

        private string GetConfigFilePathForDiagnostics()
        {
            var path = this._configuration["*project_file_path"];
            return !String.IsNullOrWhiteSpace(path) ? path : "tyhp.json";
        }

        private void ReadIndexedStringList(string sectionPrefix, List<string> target)
        {
            if (!this._configuration.GetSection($"{sectionPrefix}:0").Exists())
            {
                return;
            }

            for (int i = 0; i < 255; i++)
            {
                if (!this._configuration.GetSection($"{sectionPrefix}:{i}").Exists())
                {
                    break;
                }

                string? path = this._configuration.GetSection($"{sectionPrefix}:{i}").Value;
                if (!String.IsNullOrWhiteSpace(path))
                {
                    target.Add(path);
                }
            }
        }

        /// <summary>
        /// Reads an arbitrary configuration key (CLI flag or <c>tyhp.json</c> value).
        /// </summary>
        /// <param name="key">Configuration key (e.g. <c>yes</c>, <c>template</c>, <c>php-version</c>).</param>
        /// <returns>The raw string value, or <see langword="null"/> if unset.</returns>
        public string? GetConfigValue(string key)
        {
            return this._configuration[key];
        }

        public IEnumerable<string> GetProjectSourceFiles()
        {
            // tyhpdef definition files and composer.json package manifests matched by
            // `include` are handled by the tyhpdef loader, not compiled/emitted, so they are
            // excluded from the compiled source set here (same promotion condition as
            // ApplyTyhpdefOptions, so a pattern is never both a tyhpdef include and a source).
            var sourcePatterns = this.IncludePaths
                .Where(static pattern => !pattern.EndsWith(".tyhpdef", System.StringComparison.OrdinalIgnoreCase)
                    && !ComposerExtraTyhpPackageManifest.PatternMayMatchComposerJson(pattern));

            Matcher fileMatcher = new();
            fileMatcher.AddIncludePatterns(sourcePatterns);
            fileMatcher.AddExcludePatterns(this.ExcludePaths);
            var files = fileMatcher.GetResultsInFullPath(this.GetProjectPath())
                .Where(static path => !path.EndsWith(".tyhpdef", System.StringComparison.OrdinalIgnoreCase)
                    && !ComposerExtraTyhpPackageManifest.IsComposerJsonFileName(path));

            // require-dev Composer packages contribute tyhpdefs, not compile/lint sources.
            var requireDevRoots = ComposerInstalledInventory.GetRequireDevOnlyInstallPaths(
                this.GetProjectPath());
            if (requireDevRoots.Count == 0)
            {
                return files;
            }

            return files.Where(path =>
            {
                if (!path.EndsWith(".tyhp", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                foreach (var root in requireDevRoots)
                {
                    if (PathCanonicalizer.IsUnderRoot(path, root))
                    {
                        return false;
                    }
                }

                return true;
            });
        }

        public string GetProjectPath()
        {
            string? projectPath = null;
            var projectFile = this._configuration["*project_file_path"] ?? "";
            if (!String.IsNullOrWhiteSpace(projectFile) && File.Exists(projectFile)) {
                var info = new FileInfo(projectFile);
                projectPath = info.DirectoryName;
            }

            return projectPath ?? Directory.GetCurrentDirectory();
        }

        public string? GetExtName()
        {
            return this._configuration["ext-name"];
        }

        /// <summary>
        /// Output directory for generated tyhpdefs (<c>--output</c>).
        /// Default: <c>{projectRoot}/tyhpdef/</c> when a project file is loaded, else <c>{cwd}/tyhpdef/</c>.
        /// </summary>
        public string GetTyhpdefOutputDir()
        {
            var output = this._configuration["output"];
            if (!String.IsNullOrWhiteSpace(output))
            {
                return Path.GetFullPath(output.Trim());
            }

            return Path.Combine(this.GetProjectPath(), "tyhpdef");
        }

        /// <summary>Explicit output file path (<c>--output-file</c>).</summary>
        public string? GetTyhpdefOutputFile()
        {
            var value = this._configuration["output-file"];
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>User PHP binary (<c>--php</c>). Null means managed PHP.</summary>
        public string? GetPhpExecutablePath()
        {
            var value = this._configuration["php"];
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>Target PHP version string for tyhpdef metadata (<c>--php-version</c>).</summary>
        public string? GetTyhpdefPhpVersion()
        {
            var value = this._configuration["php-version"];
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>Managed PHP minors (<c>--php-targets</c>, comma-separated).</summary>
        public List<string> GetTyhpdefPhpTargets()
        {
            var value = this._configuration["php-targets"];
            if (String.IsNullOrWhiteSpace(value))
            {
                return [];
            }

            return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        /// <summary>
        /// Managed PHP cache directory (<c>--php-runtime-dir</c>, else <c>TYHP_PHP_RUNTIME_DIR</c>).
        /// </summary>
        public string? GetTyhpdefPhpRuntimeDir()
        {
            var value = this._configuration["php-runtime-dir"];
            if (!String.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            var env = Environment.GetEnvironmentVariable("TYHP_PHP_RUNTIME_DIR");
            return String.IsNullOrWhiteSpace(env) ? null : env.Trim();
        }

        /// <summary>Skip managed PHP patch auto-update (<c>--no-php-runtime-update</c>).</summary>
        public bool GetTyhpdefNoPhpRuntimeUpdate()
        {
            return this._configuration["no-php-runtime-update"].ParseBool();
        }

        /// <summary>Re-reflect even if snapshots exist (<c>--refresh-snapshots</c>).</summary>
        public bool GetTyhpdefRefreshSnapshots()
        {
            return this._configuration["refresh-snapshots"].ParseBool();
        }

        /// <summary>
        /// php.net manual language (<c>--locale</c>). Defaults to <see cref="Locale"/>.
        /// </summary>
        public string GetTyhpdefLocale()
        {
            var value = this._configuration["locale"];
            return String.IsNullOrWhiteSpace(value) ? this.Locale : value.Trim();
        }

        /// <summary>PHP source globs (<c>--source</c>, comma-separated or repeated).</summary>
        public List<string> GetTyhpdefSourcePaths()
        {
            return this.ReadGlobList("source");
        }

        /// <summary>Composer package directory (<c>--package-path</c>).</summary>
        public string? GetTyhpdefPackagePath()
        {
            var value = this._configuration["package-path"];
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Vendor directory for <c>--vendor</c>. Null when the flag is unset.
        /// Bare <c>--vendor</c> (or <c>=true</c>) means <c>{projectRoot}/vendor</c>.
        /// </summary>
        public string? GetTyhpdefVendorDirectory()
        {
            if (!this._configuration.GetSection("vendor").Exists())
            {
                return null;
            }

            var value = this._configuration["vendor"];
            if (String.IsNullOrWhiteSpace(value)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(Path.Combine(this.GetProjectPath(), "vendor"));
            }

            if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var trimmed = value.Trim();
            return Path.IsPathRooted(trimmed)
                ? Path.GetFullPath(trimmed)
                : Path.GetFullPath(Path.Combine(this.GetProjectPath(), trimmed));
        }

        /// <summary>Skip emitting doc comments (<c>--no-docs</c>). Default false (docs on).</summary>
        public bool GetTyhpdefNoDocs()
        {
            return this._configuration["no-docs"].ParseBool();
        }

        /// <summary>Include <c>@internal</c> items (<c>--include-internal</c>). Default false.</summary>
        public bool GetTyhpdefIncludeInternal()
        {
            return this._configuration["include-internal"].ParseBool();
        }

        /// <summary>
        /// Include deprecated items. Default true. <c>--no-deprecated</c> skips;
        /// <c>--include-deprecated</c> can set the value explicitly.
        /// </summary>
        public bool GetTyhpdefIncludeDeprecated()
        {
            if (this._configuration["no-deprecated"].ParseBool())
            {
                return false;
            }

            if (this._configuration.GetSection("include-deprecated").Exists())
            {
                return this._configuration["include-deprecated"].ParseBool();
            }

            return true;
        }

        /// <summary>Overwrite existing tyhpdef files (<c>--overwrite</c>).</summary>
        public bool GetTyhpdefOverwrite()
        {
            return this._configuration["overwrite"].ParseBool();
        }

        /// <summary>Decline PHP for Reflection harvest (<c>--no-php</c>). Errors with <c>--ext-name</c>.</summary>
        public bool GetTyhpdefNoPhp()
        {
            return this._configuration["no-php"].ParseBool();
        }

        /// <summary>Parse-check path (<c>--validate</c>).</summary>
        public string? GetTyhpdefValidatePath()
        {
            var value = this._configuration["validate"];
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>Layer 2 stub audit tree (<c>--audit-stubs</c>).</summary>
        public string? GetTyhpdefAuditStubsPath()
        {
            var value = this._configuration["audit-stubs"];
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>Optional markdown path for <c>--audit-stubs</c> (<c>--out</c>).</summary>
        public string? GetTyhpdefAuditOutPath()
        {
            var value = this._configuration["out"];
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>Overlay-aware verify (<c>--verify</c>).</summary>
        public bool GetTyhpdefVerify()
        {
            return this._configuration["verify"].ParseBool();
        }

        /// <summary>CLI <c>tyhpdef/</c> layout (<c>--split</c>). Default <c>file</c>.</summary>
        public string GetTyhpdefSplit()
        {
            var value = this._configuration["split"];
            return String.IsNullOrWhiteSpace(value) ? "file" : value.Trim();
        }

        /// <summary>Also collect Composer <c>autoload-dev</c> (<c>--include-dev</c>).</summary>
        public bool GetTyhpdefIncludeDev()
        {
            return this._configuration["include-dev"].ParseBool();
        }

        /// <summary>Fail if Layer 2 stub cache is missing (<c>--require-stubs</c>). Default false.</summary>
        public bool GetTyhpdefRequireStubs()
        {
            return this._configuration["require-stubs"].ParseBool();
        }

        /// <summary>
        /// Whether a project configuration file (<c>tyhp.json</c>) was found on disk.
        /// </summary>
        public bool HasConfigFile()
        {
            var projectFile = this._configuration["*project_file_path"];
            return !String.IsNullOrWhiteSpace(projectFile) && File.Exists(projectFile);
        }
    }
}
