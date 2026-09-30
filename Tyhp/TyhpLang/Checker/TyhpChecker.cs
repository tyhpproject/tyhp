using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Emitter.Splice;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Type-checking orchestrator. Walks bound AST trees and dispatches validation rules.
    /// </summary>
    public sealed class TyhpChecker
    {
        private readonly DiagnosticBag _diagnostics;
        private readonly SymbolTree _symbolTree;
        private readonly GlobalScope _globalScope;
        private readonly CheckerRuleRegistry _rules;
        private readonly TypeInferrer _typeInferrer;
        private readonly CheckerRuleContext _ruleContext;
        private readonly Dictionary<IBase2Ast, ICheckedType> _expressionTypes = new();
        private readonly Dictionary<IBase2Ast, ICheckedType> _narrowedTypes = new();
        private readonly HashSet<ObjectDeclarationSymbol> _requiresRuntimeGenericTracking = new();
        private readonly HashSet<IBaseSymbol> _requiresGenericVariant = new();
        private readonly Dictionary<PhpCallAst, IBaseSymbol> _genericCallTargets = new();

        /// <summary>
        /// Every method declaration seen, with the type that declares it, so the generic-variant flag
        /// can be propagated down inheritance chains once all bodies have been visited.
        /// </summary>
        private readonly List<(ObjectMethodSymbol Method, ObjectDeclarationSymbol Owner)> _declaredMethods = new();
        private readonly HashSet<PhpInlineFunctionAst> _requiresWeakReferenceCapture = new();
        private readonly HashSet<PhpStatementBlockAst> _requiresDisposableTryFinally = new();
        private readonly Dictionary<PhpLoopAst, AsyncForeachKind> _asyncForeachKinds = new();
        private readonly Dictionary<PhpInlineFunctionAst, InferredClosureSignature> _inferredClosureSignatures = new();
        private readonly Dictionary<object, ICheckedType> _inferredGeneratorReturns = new();
        private readonly HashSet<object> _finishedGeneratorBodyInferences = new();

        private readonly Dictionary<string, int> _errorsPerFile = new(StringComparer.Ordinal);
        private readonly HashSet<string> _thresholdReported = new(StringComparer.Ordinal);
        private readonly CheckerOptions _options;

        private Dictionary<string, IBaseSymbol> _nativeTypeTests =
            new(StringComparer.OrdinalIgnoreCase);

        public TyhpChecker(
            DiagnosticBag diagnostics,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            CheckerOptions? options = null,
            IEnumerable<ICheckerRule>? rules = null)
        {
            _diagnostics = diagnostics;
            _symbolTree = symbolTree;
            _globalScope = globalScope;
            _options = options ?? new CheckerOptions();
            TypeComparer.ConfigureTemplateStringMaxStates(_options.TemplateStringMaxStates);
            _rules = new CheckerRuleRegistry(rules ?? CreateDefaultRules());
            _typeInferrer = new TypeInferrer(symbolTree, globalScope, diagnostics, this);
            _ruleContext = new CheckerRuleContext(this, symbolTree, globalScope, diagnostics, _options);
        }

        private static IEnumerable<ICheckerRule> CreateDefaultRules() =>
        [
            new DeclarationRule(),
            new TypeAnnotationRule(),
            new ControlFlowRule(),
            new TypeCompatibilityRule(),
            new TypeDeclarationValidationRule(),
            new ReferenceTrackingRule(),
            new ClosureRule(),
            new AsyncBlockRule(),
            new NullSafetyRule(),
            new UnsetTrackingRule(),
            new StructRule(),
            new OperatorOverloadRule(),
            new ExtensionRule(),
            new AsyncRule(),
            new DisposableRule(),
            new CompileTimeRule(),
            new DeprecationRule(),
            new ExternTypeUseRule(),
            new RestrictedFeatureRule(),
            new OverloadRule(),
            new AttributeRule(),
            new ImportRule(),
            new CodeQualityRule(),
            new WithKeywordRule(),
            new PhpVersionRule(),
            new InlineSpliceRule(),
        ];

        internal CheckerRuleContext RuleContext => _ruleContext;

        public IReadOnlyDictionary<IBase2Ast, ICheckedType> ExpressionTypes => _expressionTypes;

        /// <summary>
        /// <c>T →</c> free function or concrete static method marked <c>#[\Tyhp\NativeTypeTest]</c>
        /// for <c>$x is T</c> emit.
        /// </summary>
        public IReadOnlyDictionary<string, IBaseSymbol> NativeTypeTests => _nativeTypeTests;

        public IReadOnlyDictionary<IBase2Ast, ICheckedType> NarrowedTypes => _narrowedTypes;

        /// <summary>
        /// Classes/enums that need <c>GenericObject</c> runtime tracking (Story 08 §5.6a side dictionary).
        /// </summary>
        public IReadOnlySet<ObjectDeclarationSymbol> RequiresRuntimeGenericTracking => _requiresRuntimeGenericTracking;

        /// <summary>
        /// Functions and methods that use their own generic parameters in a construct needing the
        /// bound type at runtime, so the emitter must produce the Mechanism D pair: the declared
        /// name delegating to a <c>__tyhpGeneric</c> Closure binder that takes the type arguments
        /// and returns a value Closure (FOUND_BUGS Mechanism D). Members are
        /// <see cref="FunctionDeclarationSymbol"/> or <see cref="ObjectMethodSymbol"/>.
        /// </summary>
        public IReadOnlySet<IBaseSymbol> RequiresGenericVariant => _requiresGenericVariant;

        /// <summary>
        /// The callee resolved for each call to a generic function or method, keyed by the call node.
        /// The emitter joins this with <see cref="RequiresGenericVariant"/> to route a call site that
        /// wrote explicit type arguments to the <c>__tyhpGeneric</c> variant. Recorded here because
        /// resolving an instance receiver needs type inference, which the emitter does not have.
        /// </summary>
        public IReadOnlyDictionary<PhpCallAst, IBaseSymbol> GenericCallTargets => _genericCallTargets;

        /// <summary>
        /// Closures that capture <c>$this</c> and are stored as properties — emitter should use WeakReference capture.
        /// </summary>
        public IReadOnlySet<PhpInlineFunctionAst> RequiresWeakReferenceCapture => _requiresWeakReferenceCapture;

        /// <summary>
        /// Contextual parameter/return types for closures that omitted authored annotations, so the
        /// emitter can still spell recoverable PHP typehints.
        /// </summary>
        public IReadOnlyDictionary<PhpInlineFunctionAst, InferredClosureSignature> InferredClosureSignatures =>
            _inferredClosureSignatures;

        /// <summary>
        /// Disposable scopes with unresolvable circular references — emitter should use try/finally fallback.
        /// </summary>
        public IReadOnlySet<PhpStatementBlockAst> RequiresDisposableTryFinally => _requiresDisposableTryFinally;

        /// <summary>
        /// Await-foreach loops classified for emitter desugaring (Story 11 Phase 9).
        /// </summary>
        public IReadOnlyDictionary<PhpLoopAst, AsyncForeachKind> AsyncForeachKinds => _asyncForeachKinds;

        public CheckerOptions Options => _options;

        public ICheckedType ResolveExpressionType(IBase2Ast expr, CheckerState state) =>
            _typeInferrer.InferExpressionType(expr, state);

        /// <summary>
        /// Value type of an index / destructure read on <paramref name="receiverType"/>
        /// (same as <c>$obj[$k]</c>).
        /// </summary>
        internal ICheckedType InferIndexValueType(
            ICheckedType receiverType,
            ICheckedType? indexType,
            CheckerState state) =>
            _typeInferrer.InferIndexValue(receiverType, indexType, state);

        /// <summary>
        /// Whether a declared operator overload form matches the operand types (Story 11 selection).
        /// </summary>
        internal bool HasMatchingBinaryOperatorOverload(
            OverloadableOperator op,
            ICheckedType left,
            ICheckedType right,
            CheckerState state)
            => _typeInferrer.HasMatchingBinaryOperatorOverload(op, left, right, state);

        /// <summary>
        /// Whether a declared unary operator overload form matches the operand type.
        /// </summary>
        internal bool HasMatchingUnaryOperatorOverload(
            OverloadableOperator op,
            ICheckedType operand,
            CheckerState state)
            => _typeInferrer.HasMatchingUnaryOperatorOverload(op, operand, state);

        internal void RecordGenericCallTargetsIn(IBase2Ast root, CheckerState state) =>
            _typeInferrer.RecordGenericCallTargetsIn(root, state);

        public ICheckedType ResolveTypeAnnotation(
            ITypeExpression typeAst,
            CheckerState state,
            bool isReturnTypePosition = false,
            bool isUserTypeDeclaration = true) =>
            _typeInferrer.ResolveTypeExpression(typeAst, state, isReturnTypePosition, isUserTypeDeclaration);

        /// <summary>
        /// Resolves a callee parameter/property annotation on a generic receiver, substituting the
        /// receiver's type arguments into class type parameters (see
        /// <c>TypeInferrer.ResolveMemberDeclaredType</c>).
        /// </summary>
        public ICheckedType ResolveMemberDeclaredType(
            ITypeExpression declaredType,
            ICheckedType receiverType,
            CheckerState state,
            ObjectMethodSymbol? method = null,
            IDereferenceableBase? callBase = null) =>
            _typeInferrer.ResolveMemberDeclaredType(declaredType, receiverType, state, method, callBase);

        public ICheckedType ResolveFunctionDeclaredType(
            ITypeExpression declaredType,
            FunctionDeclarationSymbol function,
            CheckerState state,
            IDereferenceableBase? callBase = null) =>
            _typeInferrer.ResolveFunctionDeclaredType(declaredType, function, state, callBase);

        internal bool TryInferGenericBindings(
            IReadOnlyList<GenericTypeParameterSymbol> genericParameters,
            IReadOnlyList<ParameterInfo> parameters,
            PhpCallAst call,
            CheckerState state,
            out Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings,
            ICheckedType? receiverType = null,
            ObjectMethodSymbol? method = null) =>
            _typeInferrer.TryInferGenericBindings(
                genericParameters, parameters, call, state, out bindings, receiverType, method);

        public void Check(IEnumerable<SrcFileAst> astTrees)
        {
            if (_options.PhpVersionWasDefaulted)
            {
                _diagnostics.AddWarning(
                    MessageCode.CheckerPhpVersionDefaulted,
                    "",
                    0,
                    0);
            }

            foreach (var srcFile in astTrees)
            {
                // Unsatisfied (but valid) file-level declare(php=…) skips bind; skip check
                // too so bodies without symbols do not raise follow-on errors. Invalid
                // constraints still walk so 4300 can fire.
                if (IsSilentlySkippedPhpVersionFile(srcFile))
                {
                    continue;
                }

                var state = CreateInitialState(srcFile);
                foreach (var child in srcFile.AstChildren)
                {
                    if (child is not null)
                    {
                        CheckNode(child, state);
                    }
                }
            }

            CheckBoundTyhpdefThinMappings(astTrees);

            _rules.RegisteredRules.OfType<ImportRule>().FirstOrDefault()
                ?.FlushRemainingImports(_diagnostics);

            PropagateGenericVariantAcrossHierarchies();

            _nativeTypeTests = NativeTypeTestAttributeSupport.IndexAndValidate(
                _globalScope, _ruleContext, _diagnostics);
        }

        /// <summary>
        /// Spreads the generic-variant requirement across every declaration of the same generic method
        /// in one hierarchy, so a whole method family emits the variant or none of it does.
        ///
        /// A call site binds against the *statically known* method, so both directions matter, and each
        /// failure is a silent wrong answer rather than a crash:
        /// <list type="bullet">
        /// <item>Downward — a call through the base emits <c>$x-&gt;m__tyhpGeneric(…)</c>. If an override
        /// emitted only the plain name, that call reaches the base variant on a subclass instance and
        /// the override is skipped.</item>
        /// <item>Upward — an <c>interface</c> or <c>abstract</c> declaration has no body to infer from,
        /// so nothing flags it even when every implementation needs the bound type. A call through the
        /// contract then emits the plain name, and the implementation's wrapper supplies nulls, leaving
        /// <c>typeof(T)</c> as <c>mixed</c>.</item>
        /// </list>
        ///
        /// Runs after the walk: the flag is inferred from a body, so a declaration in a file checked
        /// later would not be flagged yet.
        /// </summary>
        private void PropagateGenericVariantAcrossHierarchies()
        {
            var families = new MethodFamilies();

            foreach (var (method, owner) in _declaredMethods)
            {
                if (method.GenericParameters.Count == 0)
                {
                    continue;
                }

                foreach (var ancestor in EnumerateAncestors(owner))
                {
                    if (ancestor.Members.TryGetValue(method.Name, out var related)
                        && related is ObjectMethodSymbol relatedMethod)
                    {
                        families.Union(method, relatedMethod);
                    }
                }
            }

            foreach (var family in families.Components())
            {
                if (family.Any(_requiresGenericVariant.Contains))
                {
                    foreach (var method in family)
                    {
                        _requiresGenericVariant.Add(method);
                    }
                }
            }
        }

        /// <summary>
        /// Every base class and implemented interface reachable from <paramref name="type"/>,
        /// transitively. Interfaces are included because a call through the contract resolves to the
        /// interface's declaration, not the implementation's.
        /// </summary>
        private IEnumerable<ObjectDeclarationSymbol> EnumerateAncestors(ObjectDeclarationSymbol type)
        {
            var visited = new HashSet<ObjectDeclarationSymbol> { type };
            var pending = new Queue<ObjectDeclarationSymbol>();
            pending.Enqueue(type);

            while (pending.Count > 0)
            {
                var current = pending.Dequeue();

                foreach (var ancestor in TypeComparer.EnumerateDirectAncestors(current, _symbolTree, _globalScope))
                {
                    if (visited.Add(ancestor))
                    {
                        pending.Enqueue(ancestor);
                        yield return ancestor;
                    }
                }
            }
        }

        internal void RecordDeclaredMethod(ObjectMethodSymbol method, ObjectDeclarationSymbol? owner)
        {
            if (owner is not null)
            {
                _declaredMethods.Add((method, owner));
            }
        }

        /// <summary>
        /// Disjoint sets of method symbols that override or implement one another, so a requirement
        /// found on any one of them can be applied to all.
        /// </summary>
        private sealed class MethodFamilies
        {
            private readonly Dictionary<ObjectMethodSymbol, ObjectMethodSymbol> _parent = new();

            public void Union(ObjectMethodSymbol left, ObjectMethodSymbol right)
            {
                var leftRoot = Find(left);
                var rightRoot = Find(right);
                if (leftRoot != rightRoot)
                {
                    _parent[leftRoot] = rightRoot;
                }
            }

            public IEnumerable<IReadOnlyCollection<ObjectMethodSymbol>> Components() =>
                _parent.Keys
                    .GroupBy(Find)
                    .Select(group => (IReadOnlyCollection<ObjectMethodSymbol>)group.ToList());

            private ObjectMethodSymbol Find(ObjectMethodSymbol method)
            {
                if (!_parent.TryGetValue(method, out var parent))
                {
                    _parent[method] = method;
                    return method;
                }

                if (parent == method)
                {
                    return method;
                }

                var root = Find(parent);
                _parent[method] = root;
                return root;
            }
        }

        private CheckerState CreateInitialState(SrcFileAst srcFile)
        {
            var state = new CheckerState
            {
                ScopeType = ScopeType.File,
                CurrentFileName = srcFile.FileName,
                PhpVersionConstraintStack = [.. GetFilePhpVersionConstraints(srcFile)],
            };
            return state;
        }

        /// <summary>
        /// Included tyhpdefs are bound into <see cref="GlobalScope"/> but are not in
        /// <c>ParsedFiles</c>, so the main walk never sees them. Class-body thin
        /// mappings get splice-member checks (4174–4176, 4179). A standalone
        /// <see cref="TyhpdefStandaloneExtensionDeclAst"/> gets
        /// <see cref="ExtensionRule.Check"/>, the same block-target declaration
        /// checks a project-source standalone extension gets. That call also runs
        /// the splice-member checks for those members.
        /// </summary>
        private void CheckBoundTyhpdefThinMappings(IEnumerable<SrcFileAst> walkedTrees)
        {
            var walkedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tree in walkedTrees)
            {
                if (!string.IsNullOrEmpty(tree.FileName))
                {
                    walkedFiles.Add(tree.FileName);
                }
            }

            foreach (var obj in EnumerateObjectDeclarations(_globalScope))
            {
                if (obj.IsCompilerGenerated)
                {
                    continue;
                }

                var source = obj.SourceFile ?? "";
                if (!string.IsNullOrEmpty(source) && walkedFiles.Contains(source))
                {
                    continue;
                }

                if (obj.SyntheticInlineExtension is { } synth)
                {
                    CheckThinMappingSymbolMembers(obj, synth);
                }

                if (obj.IsExtension && IsTyhpdefSource(obj))
                {
                    if (obj.DeclaringAstNode is TyhpdefStandaloneExtensionDeclAst standalone
                        && TryCheckIncludedStandaloneTyhpdefExtension(standalone))
                    {
                        continue;
                    }

                    CheckThinMappingSymbolMembers(obj, obj);
                }
            }
        }

        /// <summary>
        /// Runs <see cref="ExtensionRule.Check"/> on an included standalone tyhpdef
        /// extension. Returns false when <see cref="ExtensionRule"/> is not registered,
        /// so the caller can still run splice-member checks.
        /// </summary>
        private bool TryCheckIncludedStandaloneTyhpdefExtension(TyhpdefStandaloneExtensionDeclAst extension)
        {
            var rule = _rules.RegisteredRules.OfType<ExtensionRule>().FirstOrDefault();
            if (rule is null)
            {
                return false;
            }

            var state = extension.OwningFile is { } file
                ? CreateInitialState(file)
                : new CheckerState
                {
                    ScopeType = ScopeType.File,
                    CurrentFileName = (extension.BoundSymbol as ObjectDeclarationSymbol)?.SourceFile ?? "",
                };
            rule.Check(extension, state, _ruleContext, _diagnostics);
            return true;
        }

        private void CheckThinMappingSymbolMembers(
            ObjectDeclarationSymbol receiverOrExtension,
            ObjectDeclarationSymbol memberOwner)
        {
            var state = new CheckerState
            {
                ScopeType = ScopeType.ObjectTypeDeclaration,
                CurrentFileName = receiverOrExtension.SourceFile ?? memberOwner.SourceFile ?? "",
                EnclosingObject = receiverOrExtension,
                EnclosingObjectType = CheckedTypes.FromSymbol(receiverOrExtension),
            };

            foreach (var member in memberOwner.Members.Values.OfType<ObjectMethodSymbol>())
            {
                if (member is ObjectOperatorOverloadMethodSymbol)
                {
                    continue;
                }

                var report = SpliceAst.FindInlineExtensionWrapper(
                        receiverOrExtension.DeclaringAstNode, member.Name)
                    ?? SpliceAst.FindInlineExtensionWrapper(memberOwner.DeclaringAstNode, member.Name)
                    ?? member.DeclaringAstNode;
                if (report is not null)
                {
                    InlineSpliceRule.CheckMemberDeclaration(report, state, _ruleContext, _diagnostics);
                }
            }

            IEnumerable<ObjectOperatorOverloadMethodSymbol> operators =
                receiverOrExtension.ExtensionContributedOperators.Count > 0
                    ? receiverOrExtension.ExtensionContributedOperators
                    : memberOwner.Members.Values.OfType<ObjectOperatorOverloadMethodSymbol>();
            foreach (var op in operators)
            {
                if (op.DeclaringAstNode is { } opNode)
                {
                    InlineSpliceRule.CheckMemberDeclaration(opNode, state, _ruleContext, _diagnostics);
                }
            }
        }

        private static bool IsTyhpdefSource(ObjectDeclarationSymbol obj) =>
            (obj.SourceFile ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase)
            || string.Equals(obj.DeclaringAstNode?.LanguageMode, "tyhpdef", StringComparison.OrdinalIgnoreCase)
            || obj.DeclaringAstNode?.OwningFile is TyhpdefSrcFileAst;

        private static IEnumerable<ObjectDeclarationSymbol> EnumerateObjectDeclarations(IBaseScope scope)
        {
            foreach (var childScope in scope.GetAllChildScopes())
            {
                if (childScope is ObjectDeclarationScope { DeclarationSymbol: ObjectDeclarationSymbol decl })
                {
                    yield return decl;
                }

                foreach (var nested in EnumerateObjectDeclarations(childScope))
                {
                    yield return nested;
                }
            }
        }

        private bool IsSilentlySkippedPhpVersionFile(SrcFileAst srcFile)
        {
            var fileSymbol = FindFileSymbol(srcFile);
            return fileSymbol is { IsPhpVersionGateInactive: true, IsPhpVersionConstraintValid: true };
        }

        private IReadOnlyList<string> GetFilePhpVersionConstraints(SrcFileAst srcFile)
            => FindFileSymbol(srcFile)?.PhpVersionConstraints ?? [];

        private FileSymbol? FindFileSymbol(SrcFileAst srcFile)
        {
            var fileName = srcFile.FileName;
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            foreach (var child in ((IBaseScope)_globalScope).GetAllChildScopes())
            {
                if (child is FileScope fileScope
                    && string.Equals(fileScope.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                    && fileScope.DeclarationSymbol is FileSymbol fileSymbol)
                {
                    return fileSymbol;
                }
            }

            return null;
        }

        /// <summary>
        /// Class members bypass <see cref="CheckNode"/>; <see cref="DeclarationRule"/> /
        /// <see cref="ExtensionRule"/> call this so <see cref="PhpVersionRule"/> still sees them.
        /// </summary>
        internal void ValidatePhpVersionMember(IBase2Ast node, CheckerState state)
        {
            _rules.RegisteredRules.OfType<PhpVersionRule>().FirstOrDefault()
                ?.Check(node, state, _ruleContext, _diagnostics);
        }

        internal void CheckNode(IBase2Ast node, CheckerState state)
        {
            if (node is ErrorAst)
            {
                return;
            }

            // Generic type-parameter constraints (e.g. `T extends void|mixed`) intentionally admit
            // `mixed`/`never` in unions. Flag that position so TypeDeclarationValidationRule skips
            // CheckerMixedInComposite there; defaults and other children stay normal.
            if (node is TyhpGenericsTypeArgumentAst genericParam)
            {
                var suppressGenericChildren = _rules.Dispatch(node, state, _ruleContext, _diagnostics);
                if (!suppressGenericChildren)
                {
                    if (genericParam.Name is not null)
                    {
                        CheckNode(genericParam.Name, state);
                    }

                    if (genericParam.TypeConstraint is IBase2Ast constraintNode)
                    {
                        var previous = state.IsGenericConstraintPosition;
                        state.IsGenericConstraintPosition = true;
                        CheckNode(constraintNode, state);
                        state.IsGenericConstraintPosition = previous;
                    }

                    if (genericParam.DefaultType is IBase2Ast defaultNode)
                    {
                        CheckNode(defaultNode, state);
                    }
                }

                CheckAttributes(node, state);
                return;
            }

            var suppressChildren = _rules.Dispatch(node, state, _ruleContext, _diagnostics);
            if (suppressChildren)
            {
                CheckAttributes(node, state);
                return;
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    CheckNode(child, state);
                }
            }

            CheckAttributes(node, state);
        }

        /// <summary>
        /// Walks <paramref name="node"/>'s <c>AstAttributes</c> through <see cref="CheckNode"/>.
        /// Exposed for class-member entry points that bypass full declaration dispatch.
        /// </summary>
        internal void CheckAttributes(IBase2Ast node, CheckerState state)
        {
            foreach (var attribute in node.AstAttributes)
            {
                CheckNode(attribute, state);
            }
        }

        internal void SetExpressionType(IBase2Ast expression, ICheckedType type) =>
            _expressionTypes[expression] = type;

        internal void ClearCachedTypesUnder(IBase2Ast? root)
        {
            if (root is null)
            {
                return;
            }

            var nodes = new HashSet<IBase2Ast>();
            CollectAstNodes(root, nodes);
            foreach (var node in nodes)
            {
                _expressionTypes.Remove(node);
                _narrowedTypes.Remove(node);
            }
        }

        private static void CollectAstNodes(IBase2Ast node, HashSet<IBase2Ast> nodes)
        {
            if (!nodes.Add(node))
            {
                return;
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    CollectAstNodes(child, nodes);
                }
            }
        }

        internal void RecordNarrowedType(IBase2Ast node, ICheckedType narrowedType) =>
            _narrowedTypes[node] = narrowedType;

        /// <summary>
        /// Marks a class/enum as requiring <c>\Tyhp\Concerns\GenericObject</c> emission.
        /// Only applies to declarations that declare generic parameters (interfaces/traits skipped).
        /// </summary>
        internal void MarkRequiresRuntimeGenericTracking(ObjectDeclarationSymbol? objectDecl)
        {
            if (objectDecl is null)
            {
                return;
            }

            if (objectDecl.ObjectKind is not (PhpTypeDeclType.Class or PhpTypeDeclType.Enum))
            {
                return;
            }

            if (objectDecl.GenericParameters.Count == 0)
            {
                return;
            }

            _requiresRuntimeGenericTracking.Add(objectDecl);
        }

        /// <summary>
        /// Marks a function or method as needing its Mechanism D generic binder emitted. Callables
        /// that declare no generic parameters of their own are ignored: a construct reading a
        /// <em>class</em> generic is served by <c>GenericObject</c> tracking (Mechanism C) instead.
        /// </summary>
        internal void MarkRequiresGenericVariant(IBaseSymbol? callable)
        {
            var generics = callable switch
            {
                ObjectMethodSymbol method => method.GenericParameters,
                FunctionDeclarationSymbol function => function.GenericParameters,
                _ => null,
            };

            if (generics is { Count: > 0 })
            {
                _requiresGenericVariant.Add(callable!);
            }
        }

        /// <summary>
        /// Records the callee of a call to a generic function or method. Whether that callee needs a
        /// Mechanism D binder is not yet known — the declaration may be visited later — so every
        /// generic callee is recorded and the emitter filters.
        /// </summary>
        internal void RecordGenericCallTarget(PhpCallAst? call, IBaseSymbol? callee)
        {
            if (call is null)
            {
                return;
            }

            var generics = callee switch
            {
                ObjectMethodSymbol method => method.GenericParameters,
                FunctionDeclarationSymbol function => function.GenericParameters,
                _ => null,
            };

            if (generics is { Count: > 0 })
            {
                _genericCallTargets[call] = callee!;
            }
        }

        internal void MarkRequiresWeakReferenceCapture(PhpInlineFunctionAst? closure)
        {
            if (closure is not null)
            {
                _requiresWeakReferenceCapture.Add(closure);
            }
        }

        internal void RecordInferredClosureSignature(
            PhpInlineFunctionAst? closure,
            InferredClosureSignature signature)
        {
            if (closure is not null)
            {
                _inferredClosureSignatures[closure] = signature;
            }
        }

        internal void RecordInferredGeneratorReturn(
            IBaseSymbol? callableSymbol,
            PhpInlineFunctionAst? closure,
            ICheckedType inferred)
        {
            if (callableSymbol is not null)
            {
                _inferredGeneratorReturns[callableSymbol] = inferred;
            }

            if (closure is not null)
            {
                _inferredGeneratorReturns[closure] = inferred;
            }
        }

        internal bool TryGetInferredGeneratorReturn(object? key, out ICheckedType inferred)
        {
            inferred = CheckedTypes.Unresolved;
            return key is not null && _inferredGeneratorReturns.TryGetValue(key, out inferred!);
        }

        /// <returns><see langword="true"/> when this callable should run generator-body finish
        /// (first visit). Later <c>CheckNode</c>s of the same closure skip so 4087 is not duplicated.</returns>
        internal bool TryBeginGeneratorBodyInference(object key) =>
            _finishedGeneratorBodyInferences.Add(key);

        /// <returns><see langword="true"/> when <paramref name="block"/> was newly flagged.</returns>
        internal bool MarkRequiresDisposableTryFinally(PhpStatementBlockAst? block)
        {
            if (block is null)
            {
                return false;
            }

            return _requiresDisposableTryFinally.Add(block);
        }

        internal void MarkAsyncForeachKind(PhpLoopAst? loop, AsyncForeachKind kind)
        {
            if (loop is null || kind == AsyncForeachKind.None)
            {
                return;
            }

            _asyncForeachKinds[loop] = kind;
        }

        internal bool TryAddError(CheckerState state, IBase2Ast node, MessageCode code, object[] args)
        {
            var fileName = CheckerHelpers.ResolveDiagnosticFileName(state, node);
            if (_options.MaxErrorsPerFile > 0)
            {
                var count = _errorsPerFile.GetValueOrDefault(fileName);
                if (count >= _options.MaxErrorsPerFile)
                {
                    if (_thresholdReported.Add(fileName))
                    {
                        _diagnostics.AddInfo(
                            MessageCode.CheckerErrorThresholdReached,
                            fileName,
                            0,
                            0);
                    }

                    return false;
                }

                _errorsPerFile[fileName] = count + 1;
            }

            DiagnosticExtensions.GetOptionalEnd(node, out var endLine, out var endColumn);
            _diagnostics.Add(
                Diagnostic.Error(
                    code,
                    fileName,
                    node.Line,
                    node.Column,
                    args,
                    endLine,
                    endColumn));
            return true;
        }

        internal bool TryGetExpressionType(IBase2Ast expression, out ICheckedType? type) =>
            _expressionTypes.TryGetValue(expression, out type);

        public bool IsAssignable(ICheckedType source, ICheckedType target) =>
            TypeComparer.IsAssignableTo(source, target, _symbolTree, _globalScope);

        /// <summary>
        /// Marks imports used by every <see cref="PhpNameAst"/> under <paramref name="node"/>,
        /// including grammar-addon type arguments (Story 12 / CHECKER_GAPS P0 #7).
        /// </summary>
        public void MarkImportNames(IBase2Ast? node, CheckerState state)
        {
            if (node is null)
            {
                return;
            }

            var fileName = state.CurrentFileName ?? node.OwningFile?.FileName ?? string.Empty;
            _rules.RegisteredRules.OfType<ImportRule>().FirstOrDefault()
                ?.MarkNamesIn(node, fileName);
        }

        public void CheckAssignment(
            IBase2Ast node,
            ICheckedType source,
            ICheckedType target,
            string context)
        {
            // No named-bag checking here: this overload only carries a file name, so resolving a
            // bag value's type would run without variable scope and silently accept anything.
            // Bag targets are checked where a real CheckerState exists (arguments, typed
            // variables, assignments, parameter defaults, returns).
            if (IsAssignable(source, target))
            {
                return;
            }

            if (TryReportTemplateStringBudgetExceeded(node, new CheckerState { CurrentFileName = node.OwningFile?.FileName }))
            {
                return;
            }

            var reportState = new CheckerState { CurrentFileName = node.OwningFile?.FileName };
            if (CheckerHelpers.TryReportObjectShapeRequiresGuard(
                    _diagnostics, reportState, node, source, target)
                || CheckerHelpers.TryReportCallableShapeRequiresGuard(
                    _diagnostics, reportState, node, source, target)
                || CheckerHelpers.TryReportNewConstraintFailure(
                    _diagnostics, reportState, node, source, target, _symbolTree, _globalScope))
            {
                return;
            }

            TryAddError(
                reportState,
                node,
                MessageCode.CheckerTypeMismatch,
                [source.DisplayName, target.DisplayName]);
        }

        public void CheckReturnType(IBase2Ast node, ICheckedType actual, ICheckedType expected, CheckerState state)
        {
            if (StructBagLiteralChecker.TryCheck(node, expected, state, _ruleContext, _diagnostics))
            {
                return;
            }

            // Call/return/`new` sites allow `operator convert` the same way AliasConverter rewrites
            // them — plain assignability alone would reject `return $money;` when `: int` and a
            // convert-to exists.
            if (IsAssignable(actual, expected)
                || TypeComparer.IsAssignableViaOperatorConvert(actual, expected, _symbolTree, _globalScope))
            {
                return;
            }

            if (TryReportTemplateStringBudgetExceeded(node, state))
            {
                return;
            }

            if (CheckerHelpers.TryReportObjectShapeRequiresGuard(
                    _diagnostics, state, node, actual, expected)
                || CheckerHelpers.TryReportCallableShapeRequiresGuard(
                    _diagnostics, state, node, actual, expected)
                || CheckerHelpers.TryReportNewConstraintFailure(
                    _diagnostics, state, node, actual, expected, _symbolTree, _globalScope))
            {
                return;
            }

            TryAddError(
                state,
                node,
                MessageCode.CheckerIncompatibleReturnType,
                [actual.DisplayName, expected.DisplayName]);
        }

        internal bool TryReportTemplateStringBudgetExceeded(IBase2Ast node, CheckerState state)
        {
            if (!TypeComparer.TryConsumeTemplateStringBudgetExceeded())
            {
                return false;
            }

            TryAddError(
                state,
                node,
                MessageCode.CheckerTemplateStringMaxStatesExceeded,
                [_options.TemplateStringMaxStates]);
            return true;
        }
    }
}
