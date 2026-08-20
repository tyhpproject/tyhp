namespace Tyhp.Config
{
    using System.Runtime.InteropServices;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.FileSystemGlobbing;
    using Tyhp.CLI;
    using Tyhp.Domain.Exceptions;
    using Tyhp.Extensions;
    using Tyhp.XDebugProxy.Config;

    public sealed class Project
    {
        public static Project? Singleton = null;

        #region configuration items
        
        /// <summary>
        /// The directory to store the cache files. If not specified, the default is the system's local application data directory.
        /// </summary>
        public string? CacheDir {get; private set;}

        /// <summary>
        /// When true, the AST cache is neither read nor written for this run (<c>--no-cache</c>).
        /// An escape hatch for forcing a full re-parse when a stale cache is suspected.
        /// </summary>
        public bool NoCache {get; private set;}

        public string Locale {get; private set;}
        public bool BeQuiet {get; private set;}

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

        public Project(IConfiguration configuration)
        {
            this._configuration = configuration;
            this.Locale = "en-US";
            this.IncludePaths = new List<string>();
            this.ExcludePaths = new List<string>();
            this.TyhpdefIncludePaths = new List<string>();
            this.TyhpdefExcludePaths = new List<string>();
            this.ExplicitPaths = new List<string>();
            this.ConfigChanged();
            Project.Singleton = this;
        }

        internal void ConfigChanged()
        {
            // Reload replaces prior pending warnings from the previous parse.
            this._pendingConfigWarnings.Clear();

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
            this.TyhpdefOptions.Exclude.Clear();

            this.ReadIndexedStringList("tyhpdefInclude", this.TyhpdefOptions.Include);
            this.ReadIndexedStringList("tyhpdefExclude", this.TyhpdefOptions.Exclude);

            this.TyhpdefIncludePaths.Clear();
            this.TyhpdefIncludePaths.AddRange(this.TyhpdefOptions.Include);

            // Entries in the project `include` array that target tyhpdef definition files or
            // package.tyhp.json manifests are loaded as type definitions (bound, never emitted)
            // rather than compiled as source. This lets a project pull in e.g. PHP extension
            // definitions or local runtime packages via a single `include` list, resolved
            // relative to tyhp.json.
            foreach (var includePattern in this.IncludePaths)
            {
                if (includePattern.EndsWith(".tyhpdef", System.StringComparison.OrdinalIgnoreCase)
                    || includePattern.EndsWith("package.tyhp.json", System.StringComparison.OrdinalIgnoreCase)
                    || includePattern.Contains("package.tyhp.json", System.StringComparison.OrdinalIgnoreCase))
                {
                    this.TyhpdefIncludePaths.Add(includePattern);
                }
            }

            this.TyhpdefExcludePaths.Clear();
            this.TyhpdefExcludePaths.AddRange(this.TyhpdefOptions.Exclude);
        }

        private void WarnConfig(MessageCode code, object[] args)
        {
            // Defer emission: lint/build fold these into DiagnosticBag (JSON/SARIF stay clean on
            // stdout); version --json / tokenize / dump-ast flush to stderr; text actions flush
            // to the console. Always record so machine-readable formatters include them even
            // under --quiet.
            this._pendingConfigWarnings.Add((code, args ?? Array.Empty<object>()));
        }

        /// <summary>
        /// Moves pending configuration warnings into <paramref name="diagnostics"/> and clears
        /// the pending list. Used by lint/build so formatters (text/JSON/SARIF) include them.
        /// </summary>
        public void TransferPendingConfigWarningsTo(Domain.Diagnostics.DiagnosticBag diagnostics)
        {
            ArgumentNullException.ThrowIfNull(diagnostics);

            foreach (var (code, args) in this._pendingConfigWarnings)
            {
                diagnostics.AddWarning(
                    code,
                    this.GetConfigFilePathForDiagnostics(),
                    0,
                    0,
                    args);
            }

            this._pendingConfigWarnings.Clear();
        }

        /// <summary>
        /// Writes pending configuration warnings to stderr (machine-readable stdout stays clean)
        /// and clears the pending list.
        /// </summary>
        public void EmitPendingConfigWarningsToStderr()
        {
            if (!this.BeQuiet)
            {
                var path = this.GetConfigFilePathForDiagnostics();
                foreach (var (code, args) in this._pendingConfigWarnings)
                {
                    Message.TyhpWarnToStderr(path, 0, 0, (int)code, args);
                }
            }

            this._pendingConfigWarnings.Clear();
        }

        /// <summary>
        /// Writes pending configuration warnings to the normal console diagnostic stream and
        /// clears the pending list. Used by human-readable actions that have no diagnostic bag.
        /// </summary>
        public void EmitPendingConfigWarningsToConsole()
        {
            if (!this.BeQuiet)
            {
                var path = this.GetConfigFilePathForDiagnostics();
                foreach (var (code, args) in this._pendingConfigWarnings)
                {
                    Message.TyhpWarn(path, 0, 0, (int)code, args);
                }
            }

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
            // tyhpdef definition files matched by `include` are handled by the tyhpdef loader,
            // not compiled/emitted, so they are excluded from the compiled source set here.
            var sourcePatterns = this.IncludePaths
                .Where(static pattern => !pattern.EndsWith(".tyhpdef", System.StringComparison.OrdinalIgnoreCase));

            Matcher fileMatcher = new();
            fileMatcher.AddIncludePatterns(sourcePatterns);
            fileMatcher.AddExcludePatterns(this.ExcludePaths);
            return fileMatcher.GetResultsInFullPath(this.GetProjectPath())
                .Where(static path => !path.EndsWith(".tyhpdef", System.StringComparison.OrdinalIgnoreCase));
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
        /// Whether a project configuration file (<c>tyhp.json</c>) was found on disk.
        /// </summary>
        public bool HasConfigFile()
        {
            var projectFile = this._configuration["*project_file_path"];
            return !String.IsNullOrWhiteSpace(projectFile) && File.Exists(projectFile);
        }
    }
}
