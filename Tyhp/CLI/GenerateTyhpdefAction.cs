using System.Diagnostics;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Enums;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;

namespace Tyhp.CLI
{
    /// <summary>
    /// CLI verb <c>generate_tyhpdef</c>: parse flags, validate, and dispatch Reflection / PHP-source harvest
    /// or <c>--vendor</c> (which loops both). Library <c>package.tyhpdef</c> is written from <c>BuildAction</c>, not this verb.
    /// </summary>
    public class GenerateTyhpdefAction : ActionRunnerBase
    {
        private const int DefaultPhpProcessTimeoutMs = 60_000;

        private static readonly HashSet<string> ValidSplitModes = new(StringComparer.OrdinalIgnoreCase)
        {
            "file",
            "namespace",
            "type",
        };

        protected CancellationToken? CancelToken { get; set; }

        private readonly Config.Project? _project;

        /// <summary>Last parsed options (for tests and later-phase dispatch).</summary>
        internal TyhpdefGenerationOptions? LastOptions { get; private set; }

        /// <summary>Last generation result (for tests).</summary>
        internal TyhpdefGenerationResult? LastResult { get; private set; }

        /// <summary>Reflection harvest generator. Tests may inject a stub to avoid downloading PHP.</summary>
        internal PhpDelegationTyhpdefGenerator Generator { get; init; } = new();

        /// <summary>PHP-source harvest generator (Composer package or <c>--source</c>). No PHP process.</summary>
        internal NativeTyhpdefGenerator NativeGenerator { get; init; } = new();

        /// <summary><c>--vendor</c> orchestrator. Tests inject fakes for Composer / Packagist.</summary>
        internal VendorTyhpdefGenerator VendorGenerator { get; init; } = new();

        /// <summary>Parse-check existing tyhpdefs (<c>--validate</c>).</summary>
        internal TyhpdefValidateService Validator { get; init; } = new();

        /// <summary>Overlay-aware compatibility check (<c>--verify</c>).</summary>
        internal TyhpdefVerifyService Verifier { get; init; } = new();

        /// <summary>Layer 2 consensus / faithfulness report (<c>--audit-stubs</c>).</summary>
        internal StubHarvestAuditService Auditor { get; init; } = new();

        public GenerateTyhpdefAction()
            : this(Config.Project.Singleton)
        {
        }

        public GenerateTyhpdefAction(Config.Project? project)
        {
            this._project = project;
        }

        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            this.CancelToken = cancellationToken;
            var result = this.Run(cancellationToken);
            this.LastResult = result;
            this.Report(result);
            Environment.ExitCode = result.Success
                ? (int)ExitCode.Success
                : (int)ExitCode.GenericError;
            return null;
        }

        private TyhpdefGenerationResult Run(CancellationToken cancellationToken)
        {
            var result = new TyhpdefGenerationResult();
            var stopwatch = Stopwatch.StartNew();

            if (this._project is null)
            {
                this.FailUsage(result, "CLI_TyhpdefRequiredArguments");
                result.Duration = stopwatch.Elapsed;
                return result;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var extName = this._project.GetExtName();
            var packagePath = this._project.GetTyhpdefPackagePath();
            var sourcePaths = this._project.GetTyhpdefSourcePaths();
            var validatePath = this._project.GetTyhpdefValidatePath();
            var vendorDirectory = this._project.GetTyhpdefVendorDirectory();

            var hasExt = !String.IsNullOrWhiteSpace(extName);
            var hasPackage = !String.IsNullOrWhiteSpace(packagePath);
            var hasSource = sourcePaths.Count > 0;
            var hasVendor = vendorDirectory is not null;
            var hasValidate = !String.IsNullOrWhiteSpace(validatePath);
            var auditPath = this._project.GetTyhpdefAuditStubsPath();
            var hasAudit = !String.IsNullOrWhiteSpace(auditPath);
            var modeCount = (hasExt ? 1 : 0) + (hasPackage ? 1 : 0) + (hasSource ? 1 : 0) + (hasVendor ? 1 : 0);

            if (hasAudit)
            {
                if (modeCount > 0)
                {
                    this.FailUsage(result, "CLI_TyhpdefAuditStubsExclusive");
                    result.Duration = stopwatch.Elapsed;
                    return result;
                }

                var auditOptions = new TyhpdefGenerationOptions
                {
                    FetchStubCache = true,
                    RequireStubs = true,
                    AuditStubsPath = auditPath,
                    AuditOutPath = this._project.GetTyhpdefAuditOutPath(),
                };
                this.LastOptions = auditOptions;
                this.Auditor.Audit(
                    auditPath!,
                    auditOptions,
                    result,
                    this._project.BeQuiet,
                    cancellationToken);
                if (hasValidate)
                {
                    this.Validator.Validate(
                        validatePath!,
                        result,
                        this._project.BeQuiet,
                        cancellationToken);
                }

                result.Duration = stopwatch.Elapsed;
                return result;
            }

            if (modeCount == 0 && hasValidate)
            {
                this.Validator.Validate(
                    validatePath!,
                    result,
                    this._project.BeQuiet,
                    cancellationToken);
                result.Duration = stopwatch.Elapsed;
                return result;
            }

            if (modeCount == 0)
            {
                this.FailUsage(result, "CLI_TyhpdefRequiredArguments");
                result.Duration = stopwatch.Elapsed;
                return result;
            }

            if (modeCount > 1)
            {
                this.FailUsage(result, "CLI_TyhpdefMultipleModes");
                result.Duration = stopwatch.Elapsed;
                return result;
            }

            TyhpdefGenerationMode mode;
            if (hasVendor)
            {
                mode = TyhpdefGenerationMode.Vendor;
            }
            else if (hasExt)
            {
                mode = TyhpdefGenerationMode.PhpExtension;
            }
            else if (hasPackage)
            {
                mode = TyhpdefGenerationMode.ComposerPackage;
            }
            else
            {
                mode = TyhpdefGenerationMode.PhpSourceFiles;
            }

            var phpPath = this._project.GetPhpExecutablePath();
            var phpTargets = this._project.GetTyhpdefPhpTargets();
            if (!String.IsNullOrWhiteSpace(phpPath) && phpTargets.Count > 0)
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefPhpUserBinaryWithTargets,
                    "generate_tyhpdef",
                    0,
                    0);
            }

            if (mode == TyhpdefGenerationMode.PhpExtension && this._project.GetTyhpdefNoPhp())
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefPhpNotFound,
                    "generate_tyhpdef",
                    0,
                    0);
            }

            var split = this._project.GetTyhpdefSplit();
            if (!ValidSplitModes.Contains(split))
            {
                this.FailUsage(result, "CLI_TyhpdefInvalidSplit", split);
            }

            var options = this.BuildOptions(mode, extName, packagePath, sourcePaths);
            this.LastOptions = options;

            if (result.Diagnostics.HasErrors)
            {
                result.Duration = stopwatch.Elapsed;
                return result;
            }

            switch (mode)
            {
                case TyhpdefGenerationMode.Vendor:
                    this.VendorGenerator.Generate(
                        this._project,
                        vendorDirectory!,
                        options,
                        result,
                        cancellationToken);
                    break;

                case TyhpdefGenerationMode.PhpExtension:
                    if (phpTargets.Count > 0)
                    {
                        this.Generator.GenerateFromPhpTargets(options, result, cancellationToken);
                        break;
                    }

                    this.Generator.GenerateFromExtension(
                        options,
                        result,
                        this._project.PhpVersion,
                        cancellationToken);
                    break;

                case TyhpdefGenerationMode.ComposerPackage:
                case TyhpdefGenerationMode.PhpSourceFiles:
                    if (phpTargets.Count > 0)
                    {
                        this.FailUsage(result, "CLI_TyhpdefPhpTargetsRequiresExtName");
                        break;
                    }

                    this.NativeGenerator.Generate(
                        options,
                        result,
                        this._project.GetProjectPath(),
                        cancellationToken);
                    break;
            }

            if (options.Verify && !result.Diagnostics.HasErrors)
            {
                var golden = this.Generator.LastLayer1 ?? this.NativeGenerator.LastLayer1;
                this.Verifier.Verify(options, result, golden, cancellationToken);
            }

            if (hasValidate)
            {
                this.Validator.Validate(
                    validatePath!,
                    result,
                    this._project.BeQuiet,
                    cancellationToken);
            }

            result.Duration = stopwatch.Elapsed;
            return result;
        }

        private TyhpdefGenerationOptions BuildOptions(
            TyhpdefGenerationMode mode,
            string? extName,
            string? packagePath,
            List<string> sourcePaths)
        {
            var project = this._project!;
            var outputDirectory = mode == TyhpdefGenerationMode.Vendor
                ? VendorTyhpdefLayout.OutputDirectory(project.GetProjectPath())
                : project.GetTyhpdefOutputDir();
            var outputFile = mode == TyhpdefGenerationMode.Vendor
                ? null
                : project.GetTyhpdefOutputFile();
            if (!String.IsNullOrWhiteSpace(outputFile) && !Path.IsPathRooted(outputFile))
            {
                outputFile = Path.Combine(outputDirectory, outputFile);
            }

            return new TyhpdefGenerationOptions
            {
                Mode = mode,
                ExtensionName = String.IsNullOrWhiteSpace(extName) ? null : extName.Trim(),
                PackagePath = String.IsNullOrWhiteSpace(packagePath) ? null : packagePath.Trim(),
                SourcePaths = new List<string>(sourcePaths),
                OutputDirectory = outputDirectory,
                OutputFileName = String.IsNullOrWhiteSpace(outputFile) ? null : outputFile,
                PreferPhpRuntime = mode == TyhpdefGenerationMode.PhpExtension,
                PhpExecutablePath = project.GetPhpExecutablePath(),
                PhpRuntimeDir = project.GetTyhpdefPhpRuntimeDir(),
                NoPhpRuntimeUpdate = project.GetTyhpdefNoPhpRuntimeUpdate(),
                RefreshSnapshots = project.GetTyhpdefRefreshSnapshots(),
                PhpTargets = new List<string>(project.GetTyhpdefPhpTargets()),
                SnapshotDirectory = Path.Combine(project.GetProjectPath(), "tyhpdef_gen", "snapshots"),
                PhpVersion = project.GetTyhpdefPhpVersion(),
                PhpProcessTimeoutMs = DefaultPhpProcessTimeoutMs,
                Locale = project.GetTyhpdefLocale(),
                IncludeDocComments = !project.GetTyhpdefNoDocs(),
                IncludeDeprecated = project.GetTyhpdefIncludeDeprecated(),
                IncludeInternal = project.GetTyhpdefIncludeInternal(),
                IncludeDev = project.GetTyhpdefIncludeDev(),
                Split = project.GetTyhpdefSplit(),
                RequireStubs = project.GetTyhpdefRequireStubs(),
                FetchStubCache = true,
                Overwrite = project.GetTyhpdefOverwrite(),
                Verify = project.GetTyhpdefVerify(),
                ValidatePath = project.GetTyhpdefValidatePath(),
            };
        }

        private void FailUsage(TyhpdefGenerationResult result, string messageKey, params object[] args)
        {
            Message.Error(messageKey, args);
            result.Diagnostics.AddError(
                MessageCode.TyhpdefGenerationError,
                "generate_tyhpdef",
                0,
                0,
                Message.Localize(messageKey, args));
        }

        private void FailPlaceholder(TyhpdefGenerationResult result, string messageKey)
        {
            Message.Error(messageKey);
            result.Diagnostics.AddError(
                MessageCode.TyhpdefGenerationError,
                "generate_tyhpdef",
                0,
                0,
                Message.Localize(messageKey));
        }

        private void Report(TyhpdefGenerationResult result)
        {
            var quiet = this._project?.BeQuiet ?? false;

            if (!quiet)
            {
                foreach (var file in result.GeneratedFiles)
                {
                    Message.Display("CLI_TyhpdefGeneratedFile", file);
                }

                if (result.GeneratedFiles.Count > 0 || result.TotalDeclarations > 0)
                {
                    Message.Display(
                        "CLI_TyhpdefDeclarationCounts",
                        result.TotalDeclarations,
                        result.ClassCount,
                        result.FunctionCount,
                        result.ConstantCount);
                    Message.Display("CLI_TyhpdefDuration", result.Duration.TotalSeconds);
                }
                else if (result.ValidatedFileCount > 0 || !string.IsNullOrWhiteSpace(this._project?.GetTyhpdefValidatePath()))
                {
                    Message.Display("CLI_TyhpdefDuration", result.Duration.TotalSeconds);
                }
                else if (!string.IsNullOrWhiteSpace(this._project?.GetTyhpdefAuditStubsPath()))
                {
                    Message.Display("CLI_TyhpdefDuration", result.Duration.TotalSeconds);
                }
            }

            this.DisplayDiagnostics(result.Diagnostics, quiet);
        }

        /// <summary>
        /// Usage and Phase 1 placeholders (<see cref="MessageCode.TyhpdefGenerationError"/>) are
        /// already printed directly via <see cref="Message.Error"/> in <see cref="FailUsage"/> /
        /// <see cref="FailPlaceholder"/>, so the formatter must skip them here — otherwise a run
        /// that raises one of those *and* a real diagnostic (e.g. <c>--split=bogus</c> combined
        /// with <c>--no-php --ext-name</c>, which reports TYHP7501 too) would print the usage
        /// message twice. Display TYHP7501/7510 and any later-phase diagnostics through the
        /// formatter, followed by the summary line whenever at least one of those was shown.
        /// </summary>
        private void DisplayDiagnostics(DiagnosticBag bag, bool quiet)
        {
            var formatter = new ConsoleDiagnosticFormatter(quiet);
            var displayedAny = false;

            foreach (var diagnostic in bag.All)
            {
                if (diagnostic.Code == MessageCode.TyhpdefGenerationError)
                {
                    continue;
                }

                formatter.Format(diagnostic);
                displayedAny = true;
            }

            if (displayedAny)
            {
                formatter.FormatSummary(bag);
            }
        }
    }
}
