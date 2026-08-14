using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    public partial class TyhpEmitter
    {
        private readonly EmitContext _context;

        // Short name of the object declaration currently being emitted (e.g. "Decimal"), used to
        // resolve `self`-typed operator operands to the concrete type key that call-site rewriting
        // produces. Null outside an object body.
        private string? _currentObjectShortName;

        // Object currently being emitted (for GenericObject injection).
        private PhpObjectTypeDeclAst? _currentObjectDecl;
        private ObjectDeclarationSymbol? _currentObjectSymbol;
        private bool _currentObjectNeedsGenericTracking;
        private IReadOnlyList<GenericTypeParameterSymbol> _currentObjectGenericParams =
            Array.Empty<GenericTypeParameterSymbol>();
        private readonly HashSet<string> _currentObjectGenericParamNames =
            new(StringComparer.Ordinal);
        private bool _currentObjectEmittedConstructor;
        private Dictionary<string, string>? _ctorGenericLocalVars;

        /// <summary>
        /// Simple names of generic parameters declared by the function/method currently being
        /// emitted (empty outside a callable body). Used to erase free type parameters in
        /// signatures and runtime <c>\Tyhp\Type</c> args when the binder left them unbound and
        /// Mechanism D is not active for this callable (FOUND #1b related <c>T::class</c> spill).
        /// </summary>
        private readonly HashSet<string> _currentCallableGenericParamNames =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Mechanism C object-scope state. <see cref="_currentObjectInGenericChain"/> is true when this
        /// class needs an <c>__initGenerics__tyhpGeneric</c> override — either because it declares
        /// tracked generic parameters of its own, or because an ancestor does and the chain has to pass
        /// through. <see cref="_currentObjectParentInGenericChain"/> decides whether the override ends
        /// in a <c>parent::</c> chain call or in <c>$this-&gt;__tyhpGeneric-&gt;markBound()</c>.
        /// </summary>
        private bool _currentObjectInGenericChain;
        private bool _currentObjectParentInGenericChain;
        private string? _currentObjectFqn;

        /// <summary>
        /// Property-hook polyfill chain state (PHP &lt; 8.4). True when this class lowers hooks or an
        /// ancestor does — so <c>__initPropertyHooks__tyhpPropertyHook</c> must run independent of
        /// <c>parent::__construct</c>. Parent-in-chain decides <c>parent::</c> vs <c>markBound()</c>.
        /// </summary>
        private bool _currentObjectInPropertyHookChain;
        private bool _currentObjectParentInPropertyHookChain;

        /// <summary>
        /// True when this class records bindings for generic parameters it declares itself. Broader than
        /// <see cref="_currentObjectNeedsGenericTracking"/>, which only covers a class that reads its own
        /// parameters through <c>typeof</c>/<c>default</c>: a class that merely <em>forwards</em> a
        /// parameter to a generic base (<c>class Sub&lt;T&gt; extends Base&lt;T, int&gt;</c>) has to
        /// record it too, or the chain call has nothing to read.
        /// </summary>
        private bool _currentObjectRecordsOwnGenerics;

        /// <summary>
        /// When set, value-returning <c>return</c> statements emit a temp +
        /// <c>\Tyhp\Type::check</c> against this runtime type expression before returning.
        /// </summary>
        private string? _currentMethodGenericReturnCheck;

        /// <summary>
        /// True while emitting the body of a <c>static</c> member, where <c>$this</c> does not exist.
        /// Guards the runtime generic lookup, which reads the binding off the instance.
        /// </summary>
        private bool _currentMemberIsStatic;

        /// <summary>
        /// Generic parameters of the Mechanism D binder currently being emitted, or empty while
        /// emitting an ordinary declaration or the delegating wrapper. While non-empty the signature
        /// builders append the <c>__tyhpGeneric</c> suffix and emit type-arg-only binder parameters
        /// returning <c>\Closure</c>, and <c>typeof</c>/<c>default</c> read those captured
        /// parameters instead of the instance's generic registry.
        /// </summary>
        private IReadOnlyList<GenericTypeParameterSymbol> _currentVariantGenericParams =
            Array.Empty<GenericTypeParameterSymbol>();

        // Operator overloads collected during member emission for the current object. In the Story 11
        // §8 redesign, all forms of an operator collapse into a single static method (plus convert's
        // instance to-forms), so overloads are gathered first and emitted together after all other
        // members. Method names are deterministic (no `_N` collision suffix); reserved-name conflicts
        // are reported by the checker, not resolved here.
        private readonly List<TyhpOperatorOverloadAst> _pendingOperatorOverloads = new();

        public TyhpEmitter(EmitContext context)
        {
            this._context = context;
        }

        public IReadOnlyList<PHPOutputFile> Emit(IEnumerable<SrcFileAst> parsedFiles)
        {
            var files = parsedFiles as IList<SrcFileAst> ?? parsedFiles.ToList();

            var outputFiles = new List<PHPOutputFile>();

            foreach (var srcFile in files)
            {
                this._context.CurrentSourceFile = srcFile;
                var splitFiles = SplitSourceFile(srcFile);
                outputFiles.AddRange(splitFiles);
            }

            this.ConvertAliasesForAll(outputFiles);
            this.MergeOutputFiles(outputFiles);
            this.BuildEmitTrees(outputFiles);
            this.PruneImportsForAll(outputFiles);
            this.GenerateAll(outputFiles);

            return outputFiles;
        }

        private IEnumerable<PHPOutputFile> SplitSourceFile(SrcFileAst srcFile)
            => PHPOutputFile.FromAstTree(srcFile, this._context);

        private void ConvertAliasesForAll(IEnumerable<PHPOutputFile> outputFiles)
        {
            foreach (var outputFile in outputFiles)
            {
                this._context.CurrentOutputFile = outputFile;
                outputFile.ConvertAliases(this._context);
            }

            this._context.CurrentOutputFile = null;
        }

        private void BuildEmitTrees(IEnumerable<PHPOutputFile> outputFiles)
        {
            foreach (var outputFile in outputFiles)
            {
                this._context.CurrentOutputFile = outputFile;
                this.BuildEmitTree(outputFile);
            }

            this._context.CurrentOutputFile = null;
        }

        private void BuildEmitTree(PHPOutputFile outputFile)
        {
            var provider = outputFile.SourceFileAst
                ?? throw new InvalidOperationException("PHPOutputFile.SourceFileAst must be set before emission.");

            var root = EmitItem.Empty(provider, EmitType.FileHeader, parent: null);
            outputFile.RootEmitItem = root;

            foreach (var declare in outputFile.FileDeclares)
            {
                this.EmitNode(declare, root);
            }

            EmitItem? contentParent = root;

            if (outputFile.FileNameSpace is PhpBlockNamespaceDeclAst blockNamespace)
            {
                contentParent = this.EmitBlockNamespaceDeclaration(blockNamespace, root);
            }
            else if (outputFile.FileNameSpace is PhpNamespaceDeclAst statementNamespace)
            {
                this.EmitNamespaceDeclaration(statementNamespace, root);
            }

            foreach (var importList in outputFile.FileImports)
            {
                this.EmitNode(importList, contentParent);
            }

            if (this.ShouldWrapEntryPointInPromiseRun(outputFile))
            {
                this._context.RequirePackage("tyhp/async");

                // Keep declarations outside Promise::run; only wrap executable top-level statements.
                var declarations = new List<IBase2Ast>();
                var executable = new List<IBase2Ast>();
                foreach (var stmt in outputFile.Statements)
                {
                    if (IsTopLevelDeclaration(stmt, outputFile))
                    {
                        declarations.Add(stmt);
                    }
                    else
                    {
                        executable.Add(stmt);
                    }
                }

                foreach (var decl in declarations)
                {
                    this.EmitNode(decl, contentParent);
                }

                if (executable.Count > 0 && executable.Any(ContainsAwaitExpression))
                {
                    var runBlock = EmitItem.Block(
                        provider,
                        EmitType.RootStatement,
                        "\\Tyhp\\Promise::run(function () {",
                        "});",
                        contentParent);
                    foreach (var stmt in executable)
                    {
                        this.EmitNode(stmt, runBlock);
                    }
                }
                else
                {
                    foreach (var stmt in executable)
                    {
                        this.EmitNode(stmt, contentParent);
                    }
                }
            }
            else
            {
                foreach (var stmt in outputFile.Statements)
                {
                    this.EmitNode(stmt, contentParent);
                }
            }
        }

        private static bool IsTopLevelDeclaration(IBase2Ast stmt, PHPOutputFile outputFile)
        {
            if (stmt is PhpFunctionDeclAst
                or PhpObjectTypeDeclAst
                or PhpConstDeclAst
                or PhpConstDeclListAst
                or TyhpExtensionDeclAst
                or TyhpStructDeclAst
                or TyhpTypeAliasAst)
            {
                return true;
            }

            if (stmt is not ITopStatement top)
            {
                return false;
            }

            var ns = outputFile.FileNameSpace switch
            {
                PhpNamespaceDeclAst n => n.Identifier,
                PhpBlockNamespaceDeclAst n => n.Identifier,
                _ => null,
            };

            return DeclarationExistenceGateHelper.TryGetValidExistenceGate(top, ns, out _, out _);
        }

        private void PruneImportsForAll(IEnumerable<PHPOutputFile> outputFiles)
        {
            foreach (var outputFile in outputFiles)
            {
                outputFile.PruneFileImports(this._context);
            }
        }

        private void MergeOutputFiles(List<PHPOutputFile> outputFiles)
        {
            var mergedByPath = new Dictionary<string, PHPOutputFile>(StringComparer.OrdinalIgnoreCase);

            foreach (var outputFile in outputFiles.ToList())
            {
                if (outputFile.IsPSR4ObjectDeclaration)
                {
                    continue;
                }

                if (!mergedByPath.TryGetValue(outputFile.OutputFilePath, out var existing))
                {
                    mergedByPath[outputFile.OutputFilePath] = outputFile;
                    continue;
                }

                existing.Merge(outputFile, this._context);
                outputFiles.Remove(outputFile);
            }
        }

        private void GenerateAll(IEnumerable<PHPOutputFile> outputFiles)
        {
            foreach (var outputFile in outputFiles)
            {
                outputFile.Generate(this._context);
            }
        }

        private EmitItem EmitNode(IBase2Ast node, EmitItem parent)
        {
            if (node is ErrorAst)
            {
                return EmitItem.Empty(node, EmitType.RootStatement, parent);
            }

            EmitItem? emitted = node switch
            {
                PhpNamespaceDeclAst namespaceDecl => this.EmitNamespaceDeclaration(namespaceDecl, parent),
                PhpBlockNamespaceDeclAst blockNamespace => this.EmitBlockNamespaceDeclaration(blockNamespace, parent),
                PhpImportDeclListAst importList => this.EmitImportList(importList, parent),
                PhpImportDeclAst importDecl => this.EmitImportDeclaration(importDecl, parent),
                PhpObjectTypeDeclAst objectDecl => this.EmitObjectDeclaration(objectDecl, parent),
                TyhpExtensionDeclAst extensionDecl => this.EmitExtensionDeclaration(extensionDecl, parent),
                PhpFunctionDeclAst functionDecl => this.EmitFunctionDeclaration(functionDecl, parent),
                // File-scope `const` — attributes emit natively for PHP ≥ 8.5; lower targets strip
                // (see EmitConstDeclaration) with TYHP5017 when attributes were present.
                PhpConstDeclAst constDecl => this.EmitConstDeclaration(constDecl, parent, EmitType.RootStatement),
                PhpConstDeclListAst constList => this.EmitConstDeclarationList(constList, parent, EmitType.RootStatement),
                PhpDeclareAst declareAst => this.EmitDeclareStatement(declareAst, parent),
                PhpMethodDeclAst method => this.EmitMethodDeclaration(method, parent),
                PhpPropertyDeclAst property => this.EmitPropertyDeclaration(property, parent),
                PhpTraitUseAst traitUse => this.EmitTraitUse(traitUse, parent),
                PhpEnumCaseAst enumCase => this.EmitEnumCase(enumCase, parent),
                TyhpStructDeclAst => EmitItem.Empty(node, EmitType.RootStatement, parent),
                TyhpTypeAliasAst => EmitItem.Empty(node, EmitType.RootStatement, parent),
                _ => null,
            };

            if (emitted != null)
            {
                return emitted;
            }

            emitted = node switch
            {
                IStatement statement => this.EmitStatement(statement, parent),
                _ => null,
            };

            if (emitted != null)
            {
                return emitted;
            }

            this._context.Diagnostics.AddErrorFromAst(
                MessageCode.EmitterUnsupportedAstNode,
                node,
                this._context.CurrentSourceFile?.Identifier ?? "",
                node.GetType().Name);

            return EmitItem.Line(
                node,
                EmitType.RootStatement,
                "/* TYHP: unsupported construct */",
                parent);
        }
    }
}
