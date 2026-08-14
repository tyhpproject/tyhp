using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Per-check session context passed to checker rules for traversal, type resolution, and diagnostics.
    /// </summary>
    public sealed class CheckerRuleContext : INarrowingResolution
    {
        private readonly TyhpChecker _checker;

        internal CheckerRuleContext(
            TyhpChecker checker,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            CheckerOptions options)
        {
            _checker = checker;
            SymbolTree = symbolTree;
            GlobalScope = globalScope;
            Diagnostics = diagnostics;
            Options = options;
        }

        public SymbolTree SymbolTree { get; }

        public GlobalScope GlobalScope { get; }

        public DiagnosticBag Diagnostics { get; }

        public CheckerOptions Options { get; }

        public void CheckNode(IBase2Ast node, CheckerState state) =>
            _checker.CheckNode(node, state);

        /// <summary>
        /// Records import usage for every name spelling under <paramref name="node"/> (including
        /// grammar-addon type arguments) without dispatching other checker rules.
        /// </summary>
        public void MarkImportNames(IBase2Ast? node, CheckerState state) =>
            _checker.MarkImportNames(node, state);

        /// <summary>
        /// Walks <paramref name="node"/>'s <c>AstAttributes</c> through the normal
        /// <see cref="CheckNode"/> path so name-based rules (notably <see cref="ImportRule"/>)
        /// see attribute class names. Used by class-member entry points that bypass full
        /// <c>CheckNode</c> on the declaration itself.
        /// </summary>
        public void CheckAttributes(IBase2Ast node, CheckerState state) =>
            _checker.CheckAttributes(node, state);

        public void CheckNodes(IEnumerable<IBase2Ast?> nodes, CheckerState state)
        {
            foreach (var node in nodes)
            {
                if (node is not null)
                {
                    CheckNode(node, state);
                }
            }
        }

        public void CheckStatementBlock(PhpStatementBlockAst? block, CheckerState state)
        {
            if (block is null)
            {
                return;
            }

            var blockState = state.Split(ScopeType.CodeBlock);
            foreach (var statement in block.GetAllNotNull())
            {
                CheckNode(statement, blockState);
                // Expression statements that discard a #[\NoDiscard] return → TYHP4165;
                // `(void) expr` suppresses. Function/method bodies use this path (not
                // ControlFlowRule's block dispatch).
                CheckerHelpers.ReportNoDiscardIfDiscarded(
                    statement, blockState, this, Diagnostics);
            }

            // Prop-init #7: constructor / method body property assignments live on blockState;
            // absorb so post-body analysis (and nested callers) see the final init map.
            state.AbsorbJoinedVariables(blockState);
            state.HasReturnedOnAllPaths = blockState.HasReturnedOnAllPaths;
        }

        public ICheckedType ResolveExpressionType(IBase2Ast expression, CheckerState state) =>
            _checker.ResolveExpressionType(expression, state);

        public ICheckedType ResolveTypeAnnotation(
            ITypeExpression typeAst,
            CheckerState state,
            bool isReturnTypePosition = false,
            bool isUserTypeDeclaration = true) =>
            _checker.ResolveTypeAnnotation(typeAst, state, isReturnTypePosition, isUserTypeDeclaration);

        public ICheckedType ResolveMemberDeclaredType(
            ITypeExpression declaredType,
            ICheckedType receiverType,
            CheckerState state,
            ObjectMethodSymbol? method = null,
            IDereferenceableBase? callBase = null) =>
            _checker.ResolveMemberDeclaredType(declaredType, receiverType, state, method, callBase);

        public ICheckedType ResolveFunctionDeclaredType(
            ITypeExpression declaredType,
            FunctionDeclarationSymbol function,
            CheckerState state,
            IDereferenceableBase? callBase = null) =>
            _checker.ResolveFunctionDeclaredType(declaredType, function, state, callBase);

        public bool TryInferGenericBindings(
            IReadOnlyList<GenericTypeParameterSymbol> genericParameters,
            IReadOnlyList<ParameterInfo> parameters,
            PhpCallAst call,
            CheckerState state,
            out Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings,
            ICheckedType? receiverType = null,
            ObjectMethodSymbol? method = null) =>
            _checker.TryInferGenericBindings(
                genericParameters, parameters, call, state, out bindings, receiverType, method);

        public bool IsAssignable(ICheckedType source, ICheckedType target, CheckerState? state = null) =>
            SymbolNameTypeAssignability.IsAssignableTo(source, target, SymbolTree, GlobalScope, state);

        /// <summary>
        /// Assignability plus Story 11 <c>operator convert</c> rewrite eligibility (call / return /
        /// <c>new</c> only — not plain assignments).
        /// </summary>
        public bool IsAssignableAllowingOperatorConvert(
            ICheckedType source,
            ICheckedType target,
            CheckerState? state = null) =>
            IsAssignable(source, target, state)
            || TypeComparer.IsAssignableViaOperatorConvert(source, target, SymbolTree, GlobalScope);

        public void CheckAssignment(IBase2Ast node, ICheckedType source, ICheckedType target) =>
            _checker.CheckAssignment(node, source, target, string.Empty);

        public void CheckReturnType(IBase2Ast node, ICheckedType actual, ICheckedType expected, CheckerState state) =>
            _checker.CheckReturnType(node, actual, expected, state);

        public bool TryReportTemplateStringBudgetExceeded(IBase2Ast node, CheckerState state) =>
            _checker.TryReportTemplateStringBudgetExceeded(node, state);

        public void ReportError(CheckerState state, IBase2Ast node, MessageCode code, params object[] args) =>
            _checker.TryAddError(state, node, code, args);

        public void RecordNarrowedType(IBase2Ast node, ICheckedType narrowedType) =>
            _checker.RecordNarrowedType(node, narrowedType);

        public void MarkRequiresRuntimeGenericTracking(ObjectDeclarationSymbol? objectDecl) =>
            _checker.MarkRequiresRuntimeGenericTracking(objectDecl);

        public void MarkRequiresGenericVariant(IBaseSymbol? callable) =>
            _checker.MarkRequiresGenericVariant(callable);

        /// <summary>
        /// Registers a method declaration so the generic-variant flag can be propagated to overrides
        /// after every body has been visited.
        /// </summary>
        public void RecordDeclaredMethod(ObjectMethodSymbol method, ObjectDeclarationSymbol? owner) =>
            _checker.RecordDeclaredMethod(method, owner);

        /// <summary>
        /// Records every call written with explicit generic type arguments under <paramref name="root"/>
        /// so the emitter can route each to its callee's Mechanism D binder.
        /// </summary>
        public void RecordGenericCallTargetsIn(IBase2Ast root, CheckerState state) =>
            _checker.RecordGenericCallTargetsIn(root, state);

        public void MarkRequiresWeakReferenceCapture(PhpInlineFunctionAst? closure) =>
            _checker.MarkRequiresWeakReferenceCapture(closure);

        /// <summary>
        /// Records contextual closure parameter/return types for emitter typehint recovery when the
        /// Tyhp source omitted those annotations.
        /// </summary>
        public void RecordInferredClosureSignature(
            PhpInlineFunctionAst? closure,
            InferredClosureSignature signature) =>
            _checker.RecordInferredClosureSignature(closure, signature);

        /// <returns><see langword="true"/> when <paramref name="block"/> was newly flagged.</returns>
        public bool MarkRequiresDisposableTryFinally(PhpStatementBlockAst? block) =>
            _checker.MarkRequiresDisposableTryFinally(block);

        public void MarkAsyncForeachKind(PhpLoopAst? loop, AsyncForeachKind kind) =>
            _checker.MarkAsyncForeachKind(loop, kind);

        /// <summary>
        /// <c>new Struct()</c> nodes already covered by a parent <c>new Struct() with [...]</c>
        /// check, so bare-<c>new</c> required-property validation can skip them.
        /// </summary>
        private readonly HashSet<PhpNewAst> _structNewsCheckedViaWith = [];

        public void MarkStructNewCheckedViaWith(PhpNewAst newExpr) =>
            _structNewsCheckedViaWith.Add(newExpr);

        public bool IsStructNewCheckedViaWith(PhpNewAst newExpr) =>
            _structNewsCheckedViaWith.Contains(newExpr);
    }
}
