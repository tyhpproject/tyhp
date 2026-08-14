using Tyhp.Domain.Enums;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Domain.Diagnostics
{
    /// <summary>
    /// Aggregates all outputs and diagnostics from the compilation pipeline.
    /// Carries results through parse, bind, check, and emit phases.
    /// </summary>
    public class CompilationResult
    {
        /// <summary>
        /// Gets the diagnostic bag collecting all diagnostics across all phases.
        /// </summary>
        public DiagnosticBag Diagnostics { get; }

        /// <summary>
        /// Gets whether the compilation succeeded (no errors).
        /// </summary>
        public bool Success => !this.Diagnostics.HasErrors;

        /// <summary>
        /// Gets or sets the parsed source file AST nodes from the parse phase.
        /// </summary>
        public IReadOnlyList<SrcFileAst>? ParsedFiles { get; set; }

        /// <summary>
        /// Gets or sets the global scope from the bind phase.
        /// </summary>
        public Tyhp.TyhpLang.Binder.Scopes.GlobalScope? GlobalScope { get; set; }

        /// <summary>
        /// Narrowed types keyed by AST nodes where control-flow narrowing applies.
        /// Populated during the check phase for downstream optimizer and LSP consumers.
        /// </summary>
        public IReadOnlyDictionary<IBase2Ast, ICheckedType>? NarrowedTypes { get; set; }

        /// <summary>
        /// Classes/enums flagged by the checker as needing <c>GenericObject</c> runtime tracking
        /// (Story 08 §5.6a side dictionary — not stored on the symbol).
        /// </summary>
        public IReadOnlySet<ObjectDeclarationSymbol>? RequiresRuntimeGenericTracking { get; set; }

        /// <summary>
        /// Functions and methods flagged by the checker as needing their Mechanism D
        /// <c>__tyhpGeneric</c> binder variant emitted (FOUND_BUGS Mechanism D / item 1 lineage).
        /// </summary>
        public IReadOnlySet<Tyhp.TyhpLang.Binder.Symbols.Interfaces.IBaseSymbol>? RequiresGenericVariant { get; set; }

        /// <summary>
        /// Callee resolved for each call to a generic function or method, so the emitter can route a
        /// call site with explicit type arguments to the <c>__tyhpGeneric</c> variant.
        /// </summary>
        public IReadOnlyDictionary<PhpCallAst, Tyhp.TyhpLang.Binder.Symbols.Interfaces.IBaseSymbol>? GenericCallTargets { get; set; }

        /// <summary>
        /// Closures flagged for WeakReference <c>$this</c> capture (Story 08 §6.6 / Story 11 disposables).
        /// </summary>
        public IReadOnlySet<PhpInlineFunctionAst>? RequiresWeakReferenceCapture { get; set; }

        /// <summary>
        /// Contextual parameter/return types for closures that omitted authored annotations, so the
        /// emitter can spell recoverable PHP typehints.
        /// </summary>
        public IReadOnlyDictionary<PhpInlineFunctionAst, InferredClosureSignature>? InferredClosureSignatures { get; set; }

        /// <summary>
        /// Per-expression types memoized by the checker (Story 16 Phase 2 expression-tree emit).
        /// </summary>
        public IReadOnlyDictionary<IBase2Ast, ICheckedType>? ExpressionTypes { get; set; }

        /// <summary>
        /// Disposable scopes flagged for try/finally fallback due to circular references.
        /// </summary>
        public IReadOnlySet<PhpStatementBlockAst>? RequiresDisposableTryFinally { get; set; }

        /// <summary>
        /// Await-foreach loops classified for emitter desugaring (Story 11 Phase 9).
        /// </summary>
        public IReadOnlyDictionary<PhpLoopAst, Tyhp.TyhpLang.Enum.AsyncForeachKind>? AsyncForeachKinds { get; set; }

        /// <summary>
        /// Gets or sets the output files from the emit phase.
        /// </summary>
        public IReadOnlyList<Tyhp.TyhpLang.Emitter.PHPOutputFile>? OutputFiles { get; set; }

        /// <summary>
        /// Gets or sets the duration of the parse phase.
        /// </summary>
        public TimeSpan ParseDuration { get; set; }

        /// <summary>
        /// Gets or sets the duration of the bind phase.
        /// </summary>
        public TimeSpan BindDuration { get; set; }

        /// <summary>
        /// Gets or sets the duration of the check phase.
        /// </summary>
        public TimeSpan CheckDuration { get; set; }

        /// <summary>
        /// Gets or sets the duration of the emit phase.
        /// </summary>
        public TimeSpan EmitDuration { get; set; }

        /// <summary>
        /// Gets or sets the duration of the optimize phase (when the optimizer runs).
        /// </summary>
        public TimeSpan OptimizeDuration { get; set; }

        /// <summary>
        /// Number of files loaded from the AST cache during parsing.
        /// </summary>
        public int AstCacheHits { get; set; }

        /// <summary>
        /// Number of files re-parsed because the AST cache missed.
        /// </summary>
        public int AstCacheMisses { get; set; }

        /// <summary>
        /// When true, the build exited early because no source files or configuration changed.
        /// </summary>
        public bool IncrementalBuildSkipped { get; set; }

        /// <summary>
        /// Number of source files discovered for the build (set before parsing).
        /// </summary>
        public int SourceFileCount { get; set; }

        /// <summary>
        /// When set (e.g. by <c>tyhp lint --file</c>), identifies the single-file lint target
        /// for machine-readable summary output.
        /// </summary>
        public string? LintTargetFile { get; set; }

        /// <summary>
        /// When true, the pipeline was cancelled (e.g. Ctrl+C) and
        /// <see cref="GetExitCode"/> returns <see cref="ExitCode.GenericError"/>.
        /// </summary>
        public bool WasCancelled { get; set; }

        /// <summary>
        /// Errors introduced during the parse phase only (delta, not cumulative).
        /// </summary>
        public int ParseErrorCount { get; set; }

        /// <summary>
        /// Errors introduced during the bind phase only (delta, not cumulative).
        /// </summary>
        public int BindErrorCount { get; set; }

        /// <summary>
        /// Errors introduced during the check phase only (delta, not cumulative).
        /// </summary>
        public int CheckErrorCount { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompilationResult"/> class.
        /// </summary>
        public CompilationResult()
        {
            this.Diagnostics = new DiagnosticBag();
        }

        /// <summary>
        /// Gets the appropriate exit code based on the diagnostic results.
        /// </summary>
        /// <param name="strictMode">
        /// When true, warnings-only results map to <see cref="ExitCode.CompileError"/>
        /// (treat warnings as errors for CI / <c>--strict</c>).
        /// </param>
        /// <returns>
        /// <see cref="ExitCode.GenericError"/> if the pipeline was cancelled,
        /// <see cref="ExitCode.Success"/> if no errors or warnings,
        /// <see cref="ExitCode.CompileError"/> if errors exist (or warnings under strict mode),
        /// <see cref="ExitCode.CompileWarning"/> if only warnings exist and strict mode is off.
        /// </returns>
        public ExitCode GetExitCode(bool strictMode = false)
        {
            if (this.WasCancelled)
            {
                return ExitCode.GenericError;
            }

            if (this.Diagnostics.HasErrors)
            {
                return ExitCode.CompileError;
            }

            if (this.Diagnostics.HasWarnings)
            {
                return strictMode ? ExitCode.CompileError : ExitCode.CompileWarning;
            }

            return ExitCode.Success;
        }
    }
}
