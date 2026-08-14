using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    public sealed class EmitConfig
    {
        public string OutputPath { get; }
        public string? NamespacePrefix { get; }
        public bool StrictTypes { get; }
        public bool IncludeComments { get; }
        public string TargetPhpVersion { get; }
        public string? EntryPointAutoloader { get; }
        /// <summary>
        /// Raw <c>build.entryPointAutoloader</c> map for per-file <c>declare(autoload=…)</c> lookups.
        /// </summary>
        public IReadOnlyDictionary<string, string>? EntryPointAutoloaderMap { get; }
        public string? SourceRoot { get; }

        public EmitConfig()
        {
            this.OutputPath = "build/";
            this.NamespacePrefix = null;
            this.StrictTypes = true;
            this.IncludeComments = true;
            this.TargetPhpVersion = "8.4";
            this.EntryPointAutoloader = null;
            this.EntryPointAutoloaderMap = null;
            this.SourceRoot = null;
        }

        public EmitConfig(Project project) : this()
        {
            this.OutputPath = project.Output.Path;
            this.NamespacePrefix = project.Output.NamespacePrefix;
            this.StrictTypes = project.Output.StrictTypes;
            this.IncludeComments = project.Output.IncludeComments;
            this.TargetPhpVersion = project.PhpVersion;
            this.SourceRoot = project.GetProjectPath();

            // Default: Composer autoload under the output directory. An empty/"none" configured
            // value disables injection; any other non-empty `composer` (or first) entry overrides.
            this.EntryPointAutoloaderMap = project.Build.EntryPointAutoloader;
            this.EntryPointAutoloader = ResolveEntryPointAutoloader(project.Build.EntryPointAutoloader);
        }

        /// <summary>
        /// Default Composer autoloader path relative to the output directory.
        /// </summary>
        public const string DefaultComposerAutoloaderPath = "vendor/autoload.php";

        private static string? ResolveEntryPointAutoloader(Dictionary<string, string>? configured)
        {
            if (configured is null)
            {
                return DefaultComposerAutoloaderPath;
            }

            if (TryGetConfiguredAutoloader(configured, "composer", out var composerAutoloader))
            {
                return IsDisabledAutoloaderPath(composerAutoloader) ? null : composerAutoloader;
            }

            var first = configured.Values.FirstOrDefault(v => !IsDisabledAutoloaderPath(v));
            return first;
        }

        /// <summary>
        /// Resolves a <c>declare(autoload="…")</c> value to an output-relative path, or
        /// <c>null</c> to disable injection for that file.
        /// </summary>
        /// <remarks>
        /// <c>"composer"</c> is special: uses the configured <c>composer</c> mapping when present,
        /// otherwise <see cref="DefaultComposerAutoloaderPath"/>. Other values look up a config
        /// key first, then fall back to a literal path. Empty / <c>none</c> disables.
        /// </remarks>
        public static string? ResolveAutoloadDirectiveValue(
            string? directiveValue,
            IReadOnlyDictionary<string, string>? configuredMap)
        {
            if (IsDisabledAutoloaderPath(directiveValue))
            {
                return null;
            }

            var trimmed = directiveValue!.Trim();

            if (string.Equals(trimmed, "composer", StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetConfiguredAutoloader(configuredMap, "composer", out var composerPath))
                {
                    return IsDisabledAutoloaderPath(composerPath) ? null : composerPath;
                }

                return DefaultComposerAutoloaderPath;
            }

            if (TryGetConfiguredAutoloader(configuredMap, trimmed, out var mapped))
            {
                return IsDisabledAutoloaderPath(mapped) ? null : mapped;
            }

            return trimmed.Replace('\\', '/').TrimStart('/');
        }

        internal static bool IsDisabledAutoloaderPath(string? path) =>
            string.IsNullOrWhiteSpace(path)
            || string.Equals(path.Trim(), "none", StringComparison.OrdinalIgnoreCase);

        private static bool TryGetConfiguredAutoloader(
            IReadOnlyDictionary<string, string>? configuredMap,
            string key,
            out string? value)
        {
            value = null;
            if (configuredMap is null)
            {
                return false;
            }

            if (configuredMap.TryGetValue(key, out value))
            {
                return true;
            }

            foreach (var pair in configuredMap)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            return false;
        }

        public EmitConfig(
            string outputPath,
            string? namespacePrefix = null,
            bool strictTypes = true,
            bool includeComments = true,
            string targetPhpVersion = "8.4",
            string? entryPointAutoloader = null,
            string? sourceRoot = null,
            IReadOnlyDictionary<string, string>? entryPointAutoloaderMap = null)
        {
            this.OutputPath = outputPath;
            this.NamespacePrefix = namespacePrefix;
            this.StrictTypes = strictTypes;
            this.IncludeComments = includeComments;
            this.TargetPhpVersion = targetPhpVersion;
            this.EntryPointAutoloader = entryPointAutoloader;
            this.EntryPointAutoloaderMap = entryPointAutoloaderMap;
            this.SourceRoot = sourceRoot;
        }
    }

    public sealed class EmitContext
    {
        public GlobalScope GlobalScope { get; }
        public DiagnosticBag Diagnostics { get; }
        public EmitConfig Config { get; }

        /// <summary>
        /// The originating <see cref="Project"/> configuration, or null for test/standalone contexts.
        /// Backs the config-flag helpers (<see cref="IsStructBackedByArray"/>, <see cref="GetDecimalBacking"/>,
        /// <see cref="IsExperimentalReadonlyCloneWith"/>, <see cref="IsRuntimeGenericChecks"/>).
        /// </summary>
        public Project? Project { get; private set; }
        public Dictionary<string, string> TypeAliasMap { get; }
        public Dictionary<string, string> TyhpdefAliasMap { get; }

        /// <summary>
        /// Tyhpdef member <c>as</c> aliases (<c>function php_name as tyhpName</c> on a class).
        /// Separate from <see cref="TyhpdefAliasMap"/> so case-insensitive class aliases cannot
        /// rewrite <c>$this-&gt;promise</c>-style member names.
        /// </summary>
        public Dictionary<string, string> TyhpdefMemberAliasMap { get; }

        public HashSet<string> UsedImports { get; } = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<PHPOutputFile, HashSet<string>> _usedImportsByFile = new();

        /// <summary>
        /// The output file currently being alias-converted or emitted. Used to attribute import usage
        /// (via <see cref="TrackUsedImport"/>) to a single output file so pruning is per-file.
        /// </summary>
        public PHPOutputFile? CurrentOutputFile { get; set; }

        /// <summary>
        /// Records that <paramref name="importFqn"/> is referenced. Tracked both globally (for callers that
        /// inspect <see cref="UsedImports"/>) and against <see cref="CurrentOutputFile"/> so per-file import
        /// pruning does not treat an import used in one file as used in another.
        /// </summary>
        public void TrackUsedImport(string importFqn)
        {
            if (string.IsNullOrWhiteSpace(importFqn))
            {
                return;
            }

            this.UsedImports.Add(importFqn);

            if (this.CurrentOutputFile is { } file)
            {
                if (!this._usedImportsByFile.TryGetValue(file, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    this._usedImportsByFile[file] = set;
                }

                set.Add(importFqn);
            }
        }

        /// <summary>
        /// Returns the import FQNs tracked as used while emitting/converting the given output file.
        /// </summary>
        public IReadOnlyCollection<string> GetUsedImportsForFile(PHPOutputFile file)
            => this._usedImportsByFile.TryGetValue(file, out var set)
                ? set
                : Array.Empty<string>();

        private readonly Dictionary<PHPOutputFile, HashSet<string>> _fqStaticCallImportsByFile = new();

        /// <summary>
        /// Records that <paramref name="importFqn"/> is referenced only via a fully-qualified static
        /// call in the currently-emitting file (e.g. an extension-class rewrite like
        /// <c>\Tyhp\Extensions\StringExtensions::method()</c>). The late import pass drops these from
        /// the file header because the leading backslash makes the <c>use</c> statement redundant.
        /// </summary>
        public void TrackFullyQualifiedStaticCallImport(string importFqn)
        {
            if (string.IsNullOrWhiteSpace(importFqn) || this.CurrentOutputFile is null)
            {
                return;
            }

            var normalized = importFqn.TrimStart('\\');
            if (!this._fqStaticCallImportsByFile.TryGetValue(this.CurrentOutputFile, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                this._fqStaticCallImportsByFile[this.CurrentOutputFile] = set;
            }

            set.Add(normalized);
        }

        /// <summary>
        /// Returns the import FQNs that were referenced only via fully-qualified static calls while
        /// emitting/converting the given output file.
        /// </summary>
        public IReadOnlyCollection<string> GetFullyQualifiedStaticCallImportsForFile(PHPOutputFile file)
            => this._fqStaticCallImportsByFile.TryGetValue(file, out var set)
                ? set
                : Array.Empty<string>();

        /// <summary>
        /// Runtime Composer packages required by emitted code. Populated by emitter transformers (Story 11).
        /// </summary>
        public HashSet<string> RequiredPackages { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Fully-qualified names the inline emitter needs imported into the emitted PHP file.
        /// Populated during the emit walk; consumed by the late import-pruning pass.
        /// </summary>
        public HashSet<string> AdditionalImports { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tracks active disposable PHP variables per scope depth (Story 11 Phase 7 disposable emission).
        /// </summary>
        public DisposableTracker Disposables { get; } = new();

        /// <summary>
        /// Scope-depth tracking for bare := disposable assignment (Story 11 Phase 7).
        /// </summary>
        internal int _scopeDepth { get; private set; }

        /// <summary>
        /// Maps scope depth to the unique PHP variable name for the disposable scope (e.g. $__scope, $__scope_1).
        /// </summary>
        internal readonly Dictionary<int, string> _scopeVarNames = new();

        /// <summary>
        /// Classes/enums flagged by the checker as needing <c>GenericObject</c> runtime tracking.
        /// </summary>
        public IReadOnlySet<ObjectDeclarationSymbol> RequiresRuntimeGenericTracking { get; private set; }

        /// <summary>
        /// Functions and methods flagged by the checker as needing their Mechanism D
        /// <c>__tyhpGeneric</c> binder variant emitted.
        /// </summary>
        public IReadOnlySet<IBaseSymbol> RequiresGenericVariant { get; private set; }

        /// <summary>
        /// Callee resolved by the checker for each call to a generic function or method.
        /// </summary>
        public IReadOnlyDictionary<PhpCallAst, IBaseSymbol> GenericCallTargets { get; private set; }

        /// <summary>
        /// Closures flagged for WeakReference <c>$this</c> capture.
        /// </summary>
        public IReadOnlySet<PhpInlineFunctionAst> RequiresWeakReferenceCapture { get; private set; }

        /// <summary>
        /// Contextual parameter/return types for closures that omitted authored annotations.
        /// </summary>
        public IReadOnlyDictionary<PhpInlineFunctionAst, InferredClosureSignature> InferredClosureSignatures
        {
            get;
            private set;
        }

        /// <summary>
        /// Per-expression types from the checker (Story 16 Phase 2 expression-tree <c>$type</c> strings).
        /// </summary>
        public IReadOnlyDictionary<IBase2Ast, ICheckedType> ExpressionTypes { get; private set; }

        /// <summary>
        /// Disposable scopes flagged for try/finally fallback.
        /// </summary>
        public IReadOnlySet<PhpStatementBlockAst> RequiresDisposableTryFinally { get; private set; }

        /// <summary>
        /// Await-foreach loops classified by the checker for desugaring.
        /// </summary>
        public IReadOnlyDictionary<PhpLoopAst, AsyncForeachKind> AsyncForeachKinds { get; private set; }

        /// <summary>
        /// When set, <c>$this</c> inside the active closure is rewritten to <c>$weakVar-&gt;get()</c>.
        /// </summary>
        public string? WeakSelfCaptureVar { get; set; }

        /// <summary>
        /// When set (typically <c>$this_</c>), author <c>$this</c> inside an extension method or
        /// static operator-form body is rewritten to this name because PHP forbids <c>$this</c> as a
        /// parameter of a <c>static</c> method. See <see cref="GeneratedNames.ExtensionReceiverThisAlias"/>.
        /// </summary>
        public string? ExtensionReceiverThisAlias { get; set; }

        /// <summary>
        /// Scope depths currently emitting <c>:=</c> as plain <c>=</c> under try/finally fallback.
        /// Nested depths are independent so inner <c>:=</c> can still use <c>DisposableScope</c>.
        /// </summary>
        private readonly HashSet<int> _tryFinallyFallbackScopeDepths = new();

        public SrcFileAst? CurrentSourceFile { get; set; }
        /// <summary>
        /// Emitted namespace name (may include <c>output.namespacePrefix</c>).
        /// </summary>
        public string? CurrentNamespace { get; set; }
        /// <summary>
        /// Source namespace as written in the Tyhp file (no output prefix). Used when resolving
        /// relative qualified names during emit.
        /// </summary>
        public string? CurrentSourceNamespace { get; set; }
        public int UniqueVarCounter { get; set; }

        public IBaseSymbol? GetSymbolForAst(IBase2Ast node) => node.BoundSymbol;

        public EmitContext(
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            EmitConfig config,
            Dictionary<string, string>? typeAliasMap = null,
            Dictionary<string, string>? tyhpdefAliasMap = null,
            Dictionary<string, string>? tyhpdefMemberAliasMap = null,
            Project? project = null,
            IReadOnlySet<ObjectDeclarationSymbol>? requiresRuntimeGenericTracking = null,
            IReadOnlySet<PhpInlineFunctionAst>? requiresWeakReferenceCapture = null,
            IReadOnlySet<PhpStatementBlockAst>? requiresDisposableTryFinally = null,
            IReadOnlyDictionary<PhpLoopAst, AsyncForeachKind>? asyncForeachKinds = null,
            IReadOnlySet<IBaseSymbol>? requiresGenericVariant = null,
            IReadOnlyDictionary<PhpCallAst, IBaseSymbol>? genericCallTargets = null,
            IReadOnlyDictionary<PhpInlineFunctionAst, InferredClosureSignature>? inferredClosureSignatures = null,
            IReadOnlyDictionary<IBase2Ast, ICheckedType>? expressionTypes = null)
        {
            this.GlobalScope = globalScope;
            this.Diagnostics = diagnostics;
            this.Config = config;
            this.Project = project;
            this.TypeAliasMap = typeAliasMap ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            this.TyhpdefAliasMap = tyhpdefAliasMap ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            this.TyhpdefMemberAliasMap = tyhpdefMemberAliasMap
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            this.RequiresRuntimeGenericTracking = requiresRuntimeGenericTracking
                ?? (IReadOnlySet<ObjectDeclarationSymbol>)new HashSet<ObjectDeclarationSymbol>();
            this.RequiresWeakReferenceCapture = requiresWeakReferenceCapture
                ?? (IReadOnlySet<PhpInlineFunctionAst>)new HashSet<PhpInlineFunctionAst>();
            this.RequiresDisposableTryFinally = requiresDisposableTryFinally
                ?? (IReadOnlySet<PhpStatementBlockAst>)new HashSet<PhpStatementBlockAst>();
            this.AsyncForeachKinds = asyncForeachKinds
                ?? (IReadOnlyDictionary<PhpLoopAst, AsyncForeachKind>)new Dictionary<PhpLoopAst, AsyncForeachKind>();
            this.RequiresGenericVariant = requiresGenericVariant
                ?? (IReadOnlySet<IBaseSymbol>)new HashSet<IBaseSymbol>();
            this.GenericCallTargets = genericCallTargets
                ?? (IReadOnlyDictionary<PhpCallAst, IBaseSymbol>)new Dictionary<PhpCallAst, IBaseSymbol>();
            this.InferredClosureSignatures = inferredClosureSignatures
                ?? (IReadOnlyDictionary<PhpInlineFunctionAst, InferredClosureSignature>)
                    new Dictionary<PhpInlineFunctionAst, InferredClosureSignature>();
            this.ExpressionTypes = expressionTypes
                ?? (IReadOnlyDictionary<IBase2Ast, ICheckedType>)
                    new Dictionary<IBase2Ast, ICheckedType>();
        }

        public static EmitContext Create(
            GlobalScope? globalScope,
            DiagnosticBag diagnostics,
            Project? project = null,
            IReadOnlySet<ObjectDeclarationSymbol>? requiresRuntimeGenericTracking = null,
            IReadOnlySet<PhpInlineFunctionAst>? requiresWeakReferenceCapture = null,
            IReadOnlySet<PhpStatementBlockAst>? requiresDisposableTryFinally = null,
            IReadOnlyDictionary<PhpLoopAst, AsyncForeachKind>? asyncForeachKinds = null,
            IReadOnlySet<IBaseSymbol>? requiresGenericVariant = null,
            IReadOnlyDictionary<PhpCallAst, IBaseSymbol>? genericCallTargets = null,
            IReadOnlyDictionary<PhpInlineFunctionAst, InferredClosureSignature>? inferredClosureSignatures = null,
            IReadOnlyDictionary<IBase2Ast, ICheckedType>? expressionTypes = null)
        {
            var scope = globalScope ?? new GlobalScope();
            var config = project != null ? new EmitConfig(project) : new EmitConfig();
            var (typeAliasMap, tyhpdefAliasMap, tyhpdefMemberAliasMap) =
                BuildAliasMaps(scope, config.NamespacePrefix);
            return new EmitContext(
                scope,
                diagnostics,
                config,
                typeAliasMap,
                tyhpdefAliasMap,
                tyhpdefMemberAliasMap,
                project,
                requiresRuntimeGenericTracking,
                requiresWeakReferenceCapture,
                requiresDisposableTryFinally,
                asyncForeachKinds,
                requiresGenericVariant,
                genericCallTargets,
                inferredClosureSignatures,
                expressionTypes);
        }

        /// <summary>
        /// True when <paramref name="objectDecl"/> was flagged for <c>GenericObject</c> emission.
        /// </summary>
        public bool RequiresRuntimeGenericTrackingFor(ObjectDeclarationSymbol? objectDecl)
            => objectDecl is not null && this.RequiresRuntimeGenericTracking.Contains(objectDecl);

        /// <summary>
        /// True when <paramref name="callable"/> was flagged for Mechanism D binder emission
        /// (declared name + <c>__tyhpGeneric</c> Closure binder pair).
        /// </summary>
        public bool RequiresGenericVariantFor(IBaseSymbol? callable)
            => callable is not null && this.RequiresGenericVariant.Contains(callable);

        public bool RequiresWeakReferenceCaptureFor(PhpInlineFunctionAst? closure)
            => closure is not null && this.RequiresWeakReferenceCapture.Contains(closure);

        public bool TryGetInferredClosureSignature(
            PhpInlineFunctionAst? closure,
            out InferredClosureSignature? signature)
        {
            signature = null;
            if (closure is null)
            {
                return false;
            }

            if (!this.InferredClosureSignatures.TryGetValue(closure, out var found))
            {
                return false;
            }

            signature = found;
            return true;
        }

        public bool RequiresDisposableTryFinallyFor(PhpStatementBlockAst? block)
            => block is not null && this.RequiresDisposableTryFinally.Contains(block);

        public AsyncForeachKind GetAsyncForeachKind(PhpLoopAst? loop)
            => loop is not null && this.AsyncForeachKinds.TryGetValue(loop, out var kind)
                ? kind
                : AsyncForeachKind.None;

        private SymbolTree? _symbolTree;

        public SymbolTree GetSymbolTree()
            => this._symbolTree ??= new SymbolTree(this.GlobalScope);

        public string GenerateUniqueVarName(string prefix = "__tyhp")
        {
            var name = $"${prefix}_{this.UniqueVarCounter}";
            this.UniqueVarCounter += 1;
            return name;
        }

        /// <summary>
        /// Unique temp for async-foreach desugaring (<c>$__asyncIter_1</c>, <c>$__asyncIter_2</c>, …).
        /// Counter starts at 1 to match Story 11 emission examples.
        /// </summary>
        public string GenerateAsyncIterVarName()
        {
            this._asyncIterCounter += 1;
            return $"$__asyncIter_{this._asyncIterCounter}";
        }

        private int _asyncIterCounter;

        /// <summary>
        /// Registers a Tyhp runtime Composer package required by emitted code.
        /// </summary>
        public void RequirePackage(string packageName)
        {
            if (!String.IsNullOrWhiteSpace(packageName))
            {
                this.RequiredPackages.Add(packageName);
            }
        }

        /// <summary>
        /// The configured struct backing (<c>build.structBacking</c>); defaults to <c>array</c> when
        /// <see cref="Project"/> is null (test/standalone emit) or the setting is unset.
        /// </summary>
        public string GetStructBacking()
            => this.Project?.Build.StructBacking ?? "array";

        /// <summary>
        /// True when structs are backed by PHP arrays (the default <c>build.structBacking = "array"</c>).
        /// When false, <see cref="GetStructBacking"/> names a custom backing class.
        /// </summary>
        public bool IsStructBackedByArray()
            => string.Equals(this.GetStructBacking(), "array", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The decimal backing library (<c>bcmath</c> or <c>gmp</c>); defaults to <c>bcmath</c>.
        /// </summary>
        public string GetDecimalBacking()
            => this.Project?.Build.DecimalBacking ?? "bcmath";

        /// <summary>
        /// Opt-in for the anonymous-class wrapper for <c>clone ... with</c> on readonly properties (PHP &lt; 8.5).
        /// Reads <c>build.experimentalReadonlyCloneWith</c>.
        /// </summary>
        public bool IsExperimentalReadonlyCloneWith()
            => this.Project?.Build.ExperimentalReadonlyCloneWith ?? false;

        /// <summary>
        /// True when <see cref="EmitConfig.TargetPhpVersion"/> is at least <paramref name="major"/>.<paramref name="minor"/>.
        /// </summary>
        public bool IsPhpVersionAtLeast(int major, int minor)
        {
            if (!TryParsePhpVersion(this.Config.TargetPhpVersion, out var parsedMajor, out var parsedMinor))
            {
                return false;
            }

            return parsedMajor > major
                || (parsedMajor == major && parsedMinor >= minor);
        }

        private static bool TryParsePhpVersion(string? version, out int major, out int minor)
        {
            major = 0;
            minor = 0;
            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || !int.TryParse(parts[0], out major))
            {
                return false;
            }

            if (parts.Length >= 2 && !int.TryParse(parts[1], out minor))
            {
                minor = 0;
            }

            return true;
        }

        /// <summary>
        /// Emit runtime type checks at generic boundaries. Reads <c>build.runtimeGenericChecks</c>.
        /// </summary>
        public bool IsRuntimeGenericChecks()
            => this.Project?.Build.RuntimeGenericChecks ?? false;

        private static (
            Dictionary<string, string> TypeAliases,
            Dictionary<string, string> TyhpdefAliases,
            Dictionary<string, string> TyhpdefMemberAliases) BuildAliasMaps(
            GlobalScope globalScope,
            string? namespacePrefix)
        {
            var typeAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var tyhpdefAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var tyhpdefMemberAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CollectAliasMaps(globalScope, typeAliases, tyhpdefAliases, tyhpdefMemberAliases, namespacePrefix);
            return (typeAliases, tyhpdefAliases, tyhpdefMemberAliases);
        }

        /// <summary>
        /// Ensures a disposable scope exists for the current block depth.
        /// Returns the unique PHP variable name for the scope (e.g. $__scope, $__scope_1).
        /// Creates one on first call per depth; subsequent calls return the same name.
        /// </summary>
        public string EnsureDisposableScopeForCurrentBlock()
        {
            if (this._scopeVarNames.TryGetValue(this._scopeDepth, out var existingName))
            {
                return existingName;
            }

            var name = this.GenerateUniqueVarName("__scope");
            this._scopeVarNames[this._scopeDepth] = name;
            return name;
        }

        public void EnterDisposableBlockScope() => this._scopeDepth++;

        public void ExitDisposableBlockScope()
        {
            this._tryFinallyFallbackScopeDepths.Remove(this._scopeDepth);
            this._scopeVarNames.Remove(this._scopeDepth);
            if (this._scopeDepth > 0)
            {
                this._scopeDepth--;
            }
        }

        /// <summary>
        /// Marks the current disposable scope depth as try/finally fallback for <c>:=</c> emission.
        /// </summary>
        public void BeginDisposableTryFinallyFallback() =>
            this._tryFinallyFallbackScopeDepths.Add(this._scopeDepth);

        public void EndDisposableTryFinallyFallback() =>
            this._tryFinallyFallbackScopeDepths.Remove(this._scopeDepth);

        /// <summary>
        /// True when <c>:=</c> in the current scope depth should emit plain <c>=</c>.
        /// </summary>
        public bool IsCurrentScopeDisposableTryFinallyFallback =>
            this._tryFinallyFallbackScopeDepths.Contains(this._scopeDepth);

        private static void CollectAliasMaps(
            IBaseScope scope,
            Dictionary<string, string> typeAliases,
            Dictionary<string, string> tyhpdefAliases,
            Dictionary<string, string> tyhpdefMemberAliases,
            string? namespacePrefix)
        {
            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol == null)
                {
                    continue;
                }

                switch (symbol)
                {
                    case TypeAliasSymbol typeAlias when typeAlias.AliasedType != null:
                        typeAliases[typeAlias.Name] = TypeSpellingHelper.Spell(
                            typeAlias.AliasedType, typeAliases, namespacePrefix: namespacePrefix);
                        break;
                    case ObjectTypeAliasSymbol objectAlias when objectAlias.AliasedType != null:
                    {
                        var spelled = TypeSpellingHelper.Spell(
                            objectAlias.AliasedType, typeAliases, namespacePrefix: namespacePrefix);
                        typeAliases[objectAlias.Name] = spelled;
                        if (scope is ObjectDeclarationScope objectScope
                            && !string.IsNullOrWhiteSpace(objectScope.DeclarationSymbol?.Name))
                        {
                            var className = objectScope.DeclarationSymbol.Name;
                            typeAliases[className + "\\" + objectAlias.Name] = spelled;
                            typeAliases["self\\" + objectAlias.Name] = spelled;
                        }

                        break;
                    }
                    case UseIncludeSymbol useInclude:
                        tyhpdefAliases[useInclude.Name] = useInclude.ImportedName;
                        break;
                    case FunctionDeclarationSymbol { OriginalPhpName: { Length: > 0 } originalFunction }:
                        // Tyhpdef `function php_name as tyhpName` — symbol lives under tyhpName.
                        tyhpdefAliases[symbol.Name] = originalFunction;
                        break;
                    case ObjectMethodSymbol { OriginalPhpName: { Length: > 0 } originalMethod }:
                        tyhpdefMemberAliases[symbol.Name] = originalMethod;
                        break;
                }
            }

            foreach (var childScope in scope.GetAllChildScopes())
            {
                if (childScope == null)
                {
                    continue;
                }

                CollectAliasMaps(
                    childScope, typeAliases, tyhpdefAliases, tyhpdefMemberAliases, namespacePrefix);
            }
        }
    }

    /// <summary>
    /// Tracks disposable PHP variable names per scope depth for Story 11 emission.
    /// </summary>
    public sealed class DisposableTracker
    {
        private int _scopeDepth;
        private readonly Dictionary<int, List<string>> _varsByDepth = new();

        public void EnterScope() => this._scopeDepth++;

        public void ExitScope()
        {
            this._varsByDepth.Remove(this._scopeDepth);
            if (this._scopeDepth > 0)
            {
                this._scopeDepth--;
            }
        }

        public void Track(string phpVarName)
        {
            if (!this._varsByDepth.TryGetValue(this._scopeDepth, out var list))
            {
                list = new List<string>();
                this._varsByDepth[this._scopeDepth] = list;
            }

            list.Add(phpVarName);
        }

        public IReadOnlyList<string> GetCurrentScopeVars()
            => this._varsByDepth.TryGetValue(this._scopeDepth, out var list)
                ? list
                : Array.Empty<string>();
    }
}
