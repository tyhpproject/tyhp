using System.Text.Json.Nodes;
using Tyhp.CLI.Support;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;

namespace Tyhp.CLI
{
    /// <summary>
    /// Implements the <c>symbol_tree</c> action: parse and bind the target project, then dump the
    /// fully resolved symbol environment as JSON. Includes builtins, package tyhpdefs, overlays,
    /// and project sources — the same tree bind, check, and emit use.
    /// </summary>
    public class SymbolTreeAction : ActionRunnerBase
    {
        private readonly Config.Project _project;
        private readonly string? _outputPath;
        private readonly string? _nameFilter;

        /// <summary>
        /// Initializes a new instance of the <see cref="SymbolTreeAction"/> class.
        /// </summary>
        /// <param name="project">Project configuration (sources, tyhpdefs, PHP version).</param>
        /// <param name="outputPath">Optional <c>--out</c> file path; when null, JSON is written to stdout.</param>
        /// <param name="nameFilter">Optional <c>--filter</c> substring on Tyhp name, FQN, or PHP name.</param>
        public SymbolTreeAction(Config.Project project, string? outputPath, string? nameFilter)
        {
            this._project = project ?? throw new ArgumentNullException(nameof(project));
            this._outputPath = outputPath;
            this._nameFilter = nameFilter;
        }

        /// <inheritdoc/>
        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            this.Status("CLI_StartingSymbolTree");

            var discoveryDiagnostics = new DiagnosticBag(this._project.SuppressedWarnings);
            var sourceFiles = DebugCommandSupport.ResolveInputFiles(this._project, discoveryDiagnostics);

            var compilationOptions = CompilationOptions.FromProject(this._project, options =>
            {
                options.SkipChecking = true;
                options.EnableAstCache = !this._project.NoCache;
            });

            CompilationResult result;
            using (var compilationService = new CompilationService())
            {
                if (sourceFiles.Count > 0)
                {
                    result = compilationService.ParseFiles(sourceFiles, compilationOptions, cancellationToken);
                }
                else
                {
                    result = this.BindEnvironmentOnly(compilationService, compilationOptions, cancellationToken);
                }
            }

            result.Diagnostics.AddRange(discoveryDiagnostics);
            result.SourceFileCount = sourceFiles.Count;
            this._project.TransferPendingConfigWarningsTo(result.Diagnostics);

            var root = SymbolTreeDumper.Dump(
                result.GlobalScope,
                result.Diagnostics,
                this._nameFilter,
                compilationOptions.PhpVersion,
                sourceFiles.Count);

            if (result.GlobalScope is null)
            {
                this.Status("CLI_SymbolTreeBindFailed");
            }

            DebugCommandSupport.WriteJson(root, this._outputPath);
            this.Status("CLI_SymbolTreeCompleted", CountDumpedSymbols(root));
            return result;
        }

        /// <summary>
        /// Bind still loads builtins and package tyhpdefs when the project has no source files.
        /// <see cref="CompilationService.ParseFiles"/> skips bind on an empty list, so this path
        /// parses a declaration-free dummy and binds that.
        /// </summary>
        private CompilationResult BindEnvironmentOnly(
            CompilationService compilationService,
            CompilationOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = new CompilationResult(this._project.SuppressedWarnings);
            this.Status("CLI_SymbolTreeNoSourceFiles");

            var dummyPath = Path.Combine(this._project.GetProjectPath(), "<symbol_tree>.tyhp");
            var dummyContent = options.Tagless ? string.Empty : "<?tyhp\n";
            var dummy = compilationService.ParseFromContent(
                dummyContent,
                dummyPath,
                result.Diagnostics,
                options);

            if (dummy is null || result.Diagnostics.HasErrors)
            {
                result.ParsedFiles = dummy is null ? [] : [dummy];
                return result;
            }

            result.ParsedFiles = [dummy];
            try
            {
                var binder = new TyhpBinder(result.Diagnostics, options);
                result.GlobalScope = binder.Bind([dummy]);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                result.Diagnostics.AddError(
                    MessageCode.BinderUnknownError,
                    dummyPath,
                    0,
                    0,
                    $"Binder invocation failed: {ex.GetType().Name} - {ex.Message}");
            }

            return result;
        }

        private void Status(string messageKey, params object[] args)
        {
            if (this._project.BeQuiet)
            {
                return;
            }

            DebugCommandSupport.Status(messageKey, args);
        }

        private static int CountDumpedSymbols(JsonObject root)
        {
            if (!root.TryGetPropertyValue("counts", out var countsNode)
                || countsNode is not JsonObject counts
                || !counts.TryGetPropertyValue("symbols", out var symbolsNode)
                || symbolsNode is not JsonValue symbolsValue
                || !symbolsValue.TryGetValue(out int count))
            {
                return 0;
            }

            return count;
        }
    }
}
