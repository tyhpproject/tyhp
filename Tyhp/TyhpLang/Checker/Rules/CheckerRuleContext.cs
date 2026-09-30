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

        /// <summary>
        /// PHP version-gate validation for class members that bypass <see cref="CheckNode"/>.
        /// </summary>
        public void ValidatePhpVersionMember(IBase2Ast node, CheckerState state) =>
            _checker.ValidatePhpVersionMember(node, state);

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
            CheckStatementSequence(block.GetAllNotNull(), blockState);

            // Prop-init #7: constructor / method body property assignments live on blockState;
            // absorb so post-body analysis (and nested callers) see the final init map.
            state.AbsorbJoinedVariables(blockState);
            state.HasReturnedOnAllPaths = blockState.HasReturnedOnAllPaths;
            state.HasArrayAccessShapeCoverage = blockState.HasArrayAccessShapeCoverage;
        }

        /// <summary>
        /// Walks a statement list on <paramref name="state"/> (no extra split). Function/method
        /// bodies go through <see cref="CheckStatementBlock"/>; nested blocks / if-arm lists use
        /// this directly so abrupt-completion flags stay on the branch state.
        ///
        /// After a definite <c>return</c> / <c>throw</c> / <c>break</c> / <c>continue</c> /
        /// <c>exit</c> / <c>die</c> (or a never-typed expression statement), the first following
        /// executable statement is TYHP4012. Later statements in the same list are still type-checked
        /// but do not each get another 4012. If this list was already unreachable when entered
        /// (parent reported the whole block), inner statements are not re-warned.
        /// </summary>
        public void CheckStatementSequence(IEnumerable<IBase2Ast> statements, CheckerState state)
        {
            var reportedUnreachable = state.HasReturnedOnAllPaths;
            var list = statements as IList<IBase2Ast> ?? statements.ToList();
            for (var index = 0; index < list.Count; index++)
            {
                var statement = list[index];
                if (index + 1 < list.Count
                    && PhpVersionRule.AreComplementaryExtBlocks(statement, list[index + 1]))
                {
                    CheckComplementaryExtBlocks(
                        (PhpDeclareAst)statement, (PhpDeclareAst)list[index + 1], state);
                    index++;
                    continue;
                }

                if (state.HasReturnedOnAllPaths
                    && !reportedUnreachable
                    && IsReportableUnreachableStatement(statement))
                {
                    CheckerHelpers.ReportWarning(
                        Diagnostics,
                        state,
                        statement,
                        MessageCode.CheckerUnreachableCode);
                    reportedUnreachable = true;
                }

                CheckNode(statement, state);
                // Expression statements that discard a #[\NoDiscard] return → TYHP4165;
                // `(void) expr` suppresses.
                CheckerHelpers.ReportNoDiscardIfDiscarded(
                    statement, state, this, Diagnostics);
                MarkTerminatedIfNeverReturning(statement, state);
            }
        }

        private readonly HashSet<PhpDeclareAst> _pairedGateBlocks = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// True while <paramref name="declareAst"/> is one arm of a <c>declare(ext="x")</c> /
        /// <c>declare(ext="!x")</c> pair being checked as <c>if … else …</c>.
        /// </summary>
        internal bool IsPairedGateBlock(PhpDeclareAst declareAst) => _pairedGateBlocks.Contains(declareAst);

        /// <summary>
        /// <c>declare(ext="x") { … }</c> directly followed by <c>declare(ext="!x") { … }</c> emits as
        /// <c>if (\extension_loaded('x')) { … }</c> and <c>if (!\extension_loaded('x')) { … }</c>, so
        /// exactly one arm runs. Joins them the way an <c>if</c>/<c>else</c> is joined, so a variable
        /// both arms assign is definitely assigned afterwards.
        /// </summary>
        private void CheckComplementaryExtBlocks(PhpDeclareAst first, PhpDeclareAst second, CheckerState state)
        {
            var before = state.SnapShot();
            _pairedGateBlocks.Add(first);
            _pairedGateBlocks.Add(second);
            try
            {
                var firstState = before.Split(ScopeType.CodeBlock);
                CheckNode(first, firstState);
                var secondState = before.Split(ScopeType.CodeBlock);
                CheckNode(second, secondState);

                var bothReturn = firstState.HasReturnedOnAllPaths && secondState.HasReturnedOnAllPaths;
                firstState.Merge(secondState);
                state.AbsorbJoinedVariables(firstState);
                state.HasReturnedOnAllPaths = bothReturn;
            }
            finally
            {
                _pairedGateBlocks.Remove(first);
                _pairedGateBlocks.Remove(second);
            }
        }

        /// <summary>
        /// Empty <c>;</c> and parse-error placeholders are not worth a 4012 of their own — skip
        /// them so the warning lands on the first real dead statement (or is omitted).
        /// </summary>
        private static bool IsReportableUnreachableStatement(IBase2Ast statement) =>
            statement is not PhpNopStatementAst and not ErrorAst;

        /// <summary>
        /// <c>exit</c> / <c>die</c> and other never-typed expression statements leave no
        /// continuation. First-class <c>exit(...)</c> produces a callable and does not terminate.
        /// Control-flow containers are excluded: their own walks already set
        /// <see cref="CheckerState.HasReturnedOnAllPaths"/>.
        ///
        /// Called from <see cref="CheckStatementSequence"/> for list members, and directly by
        /// <c>ControlFlowRule.CheckStatement</c> for a braceless if-arm (a single statement, not a
        /// list) — <c>return</c>/<c>throw</c>/<c>break</c>/<c>continue</c> already set the flag via
        /// their own dispatch either way, but a braceless <c>if ($c) exit;</c> would otherwise never
        /// reach this exit/die/never check at all.
        /// </summary>
        public void MarkTerminatedIfNeverReturning(IBase2Ast statement, CheckerState state)
        {
            if (state.HasReturnedOnAllPaths
                || statement is PhpStatementBlockAst
                    or PhpIfAst
                    or PhpLoopAst
                    or PhpTryCatchAst
                    or PhpConditionalAst
                    or PhpFunctionDeclAst
                    or PhpMethodDeclAst
                    or PhpNopStatementAst
                    or ErrorAst)
            {
                return;
            }

            if (statement is PhpUnaryOpAst unary
                && IsTerminatingExitOrDie(unary))
            {
                state.HasReturnedOnAllPaths = true;
                return;
            }

            if (statement is IExpression expression)
            {
                var type = ResolveExpressionType(expression, state);
                if (TypeComparer.IsNeverType(type))
                {
                    state.HasReturnedOnAllPaths = true;
                }
            }
        }

        private static bool IsTerminatingExitOrDie(PhpUnaryOpAst unary)
        {
            var name = unary.Operator?.ValueString;
            if (!string.Equals(name, "exit", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, "die", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return unary.Operand is not PhpArgumentListAst args
                || !CheckerHelpers.IsFirstClassCallableArgumentList(args);
        }

        public ICheckedType ResolveExpressionType(IBase2Ast expression, CheckerState state) =>
            _checker.ResolveExpressionType(expression, state);

        /// <summary>
        /// Value type of an index / destructure read (same as <c>$obj[$k]</c>).
        /// </summary>
        public ICheckedType InferIndexValueType(
            ICheckedType receiverType,
            ICheckedType? indexType,
            CheckerState state) =>
            _checker.InferIndexValueType(receiverType, indexType, state);

        /// <summary>
        /// Whether a Story 11 operator-overload form matches the binary operand types.
        /// </summary>
        public bool HasMatchingBinaryOperatorOverload(
            OverloadableOperator op,
            ICheckedType left,
            ICheckedType right,
            CheckerState state)
            => _checker.HasMatchingBinaryOperatorOverload(op, left, right, state);

        /// <summary>
        /// Whether a Story 11 unary operator-overload form matches the operand type.
        /// </summary>
        public bool HasMatchingUnaryOperatorOverload(
            OverloadableOperator op,
            ICheckedType operand,
            CheckerState state)
            => _checker.HasMatchingUnaryOperatorOverload(op, operand, state);

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

        public void RecordInferredGeneratorReturn(
            IBaseSymbol? callableSymbol,
            PhpInlineFunctionAst? closure,
            ICheckedType inferred) =>
            _checker.RecordInferredGeneratorReturn(callableSymbol, closure, inferred);

        public bool TryGetInferredGeneratorReturn(object? key, out ICheckedType inferred) =>
            _checker.TryGetInferredGeneratorReturn(key, out inferred);

        public bool TryBeginGeneratorBodyInference(object key) =>
            _checker.TryBeginGeneratorBodyInference(key);

        public void SetExpressionType(IBase2Ast expression, ICheckedType type) =>
            _checker.SetExpressionType(expression, type);

        /// <summary>
        /// Drops memoized expression and narrowed types under <paramref name="root"/> so a
        /// per-key <c>ArrayAccessShape</c> body walk does not reuse the previous key's types.
        /// </summary>
        public void ClearCachedTypesUnder(IBase2Ast? root) =>
            _checker.ClearCachedTypesUnder(root);

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
