using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>Control-flow statement validation including branches, loops, exceptions, and jumps.</summary>
    public sealed partial class ControlFlowRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(PhpIfAst),
            typeof(PhpLoopAst),
            typeof(PhpTryCatchAst),
            typeof(PhpStatementBlockAst),
            typeof(PhpReturnStatementAst),
            typeof(PhpJumpStatementAst),
            typeof(PhpConditionalAst),
            typeof(PhpYieldAst),
            typeof(PhpGotoStatementAst),
            typeof(PhpLabelStatementAst),
            typeof(PhpEchoStatementAst),
            typeof(PhpUnaryOpAst),
            typeof(PhpTernaryOpAst),
        ];

        public bool Handles(IBase2Ast node) =>
            node is not PhpUnaryOpAst unary
            || string.Equals(unary.Operator?.ValueString, "throw", StringComparison.OrdinalIgnoreCase)
            || string.Equals(unary.Operator?.ValueString, "return", StringComparison.OrdinalIgnoreCase);

        public bool SuppressChildTraversal(IBase2Ast node) =>
            node is not PhpUnaryOpAst;

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            switch (node)
            {
                case PhpIfAst ifAst:
                    CheckIf(ifAst, state, context, diagnostics);
                    break;
                case PhpLoopAst loop:
                    CheckLoop(loop, state, context, diagnostics);
                    break;
                case PhpTryCatchAst tryCatch:
                    CheckTryCatch(tryCatch, state, context, diagnostics);
                    break;
                case PhpStatementBlockAst block:
                    CheckStatementBlock(block, state, context, diagnostics);
                    break;
                case PhpReturnStatementAst returnStmt:
                    CheckReturn(returnStmt, returnStmt.Expression, state, context, diagnostics);
                    break;
                case PhpJumpStatementAst jump:
                    CheckJump(jump, state, context, diagnostics);
                    break;
                case PhpConditionalAst conditional:
                    CheckConditional(conditional, state, context, diagnostics);
                    break;
                case PhpYieldAst yield:
                    CheckYield(yield, state, context, diagnostics);
                    break;
                case PhpGotoStatementAst:
                case PhpLabelStatementAst:
                    CheckerHelpers.ReportError(context, state, node, MessageCode.CheckerGotoProhibited);
                    break;
                case PhpEchoStatementAst echo:
                    CheckEcho(echo, state, context, diagnostics);
                    break;
                case PhpTernaryOpAst ternary:
                    CheckTernary(ternary, state, context, diagnostics);
                    break;
                case PhpUnaryOpAst unary when string.Equals(unary.Operator?.ValueString, "throw", StringComparison.OrdinalIgnoreCase):
                    CheckThrow(unary, state, context, diagnostics);
                    break;
                // Expression-bodied (arrow) functions, methods, and operator overloads synthesize
                // their implicit return as a unary 'return' operator rather than a dedicated return
                // statement. Treat it identically so control-flow and return-type analysis apply.
                case PhpUnaryOpAst unary when string.Equals(unary.Operator?.ValueString, "return", StringComparison.OrdinalIgnoreCase):
                    CheckReturn(unary, unary.Operand, state, context, diagnostics);
                    break;
            }
        }

        private static void CheckIf(
            PhpIfAst ifAst,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            CheckExistenceGateArgument(ifAst, state, context, diagnostics);
            // Type-check the condition on a disposable probe so progressive `&&` narrowing
            // applied while validating operands cannot leak into the post-if continuation.
            CheckConditionExpression(ifAst.Condition, state, context, diagnostics);

            var beforeBranch = state.SnapShot();
            var thenState = beforeBranch.Split(ScopeType.CodeBlock);
            ApplyConditionNarrowing(ifAst.Condition, thenState, context, positive: true);
            CheckStatement(ifAst.ThenStatement, thenState, context, diagnostics);

            if (ifAst.ElseStatement is not null)
            {
                var elseState = beforeBranch.Split(ScopeType.CodeBlock);
                ApplyConditionNarrowing(ifAst.Condition, elseState, context, positive: false);
                CheckStatement(ifAst.ElseStatement, elseState, context, diagnostics);
                // Join then⋈else first, then absorb — merging each into the pre-if state would
                // count the unassigned entry state as a third path and never clear 4014 when both
                // arms assign.
                thenState.Merge(elseState);
                state.AbsorbJoinedVariables(thenState);
                state.HasReturnedOnAllPaths = thenState.HasReturnedOnAllPaths && elseState.HasReturnedOnAllPaths;
            }
            else
            {
                // No explicit else: build the implicit negative-narrowed branch the same way the
                // if/else case builds its else arm, so continuation code sees both (a) the
                // then-branch's effects when it falls through and (b) the condition's negative
                // narrowing when it does not enter the then-branch — e.g. `if ($x === null) { ... }`
                // followed by code that relies on `$x` being non-null. Previously this merged
                // `thenState` straight into the un-narrowed `state`, which (1) never applied
                // negative narrowing to the not-entered path and (2) still folded the then-branch's
                // (possibly narrowed-to-null) variable state into the continuation even when that
                // branch always returns/throws/continues/breaks — a dead path that must not leak
                // into what follows. `HasReturnedOnAllPaths` is the abrupt-completion signal for
                // all of those (set by CheckReturn / CheckThrow / CheckJump).
                var negativeState = beforeBranch.Split(ScopeType.CodeBlock);
                ApplyConditionNarrowing(ifAst.Condition, negativeState, context, positive: false);

                if (thenState.HasReturnedOnAllPaths)
                {
                    state.AbsorbJoinedVariables(negativeState);
                }
                else
                {
                    thenState.Merge(negativeState);
                    state.AbsorbJoinedVariables(thenState);
                }

                state.HasReturnedOnAllPaths = false;
            }
        }

        /// <summary>
        /// Checks the condition, then defers arm-walking and the definite-assignment merge to
        /// <see cref="TypeInferrer"/>'s ternary type inference via the memoized
        /// <see cref="CheckerRuleContext.ResolveExpressionType"/> cache.
        ///
        /// A ternary used as a plain assignment's RHS (or a call argument) is *also* reached
        /// through <c>TypeCompatibilityRule.CheckBinaryOp</c>'s assignability check, which resolves
        /// <c>binary.Right</c>'s type — i.e. runs ternary inference — before <c>NullSafetyRule</c>
        /// re-walks that RHS and dispatches here. Splitting fresh branch states from <paramref
        /// name="state"/> independently in both places meant whichever ran second derived its
        /// "before the ternary" snapshot from a state the other call had already merged both arms
        /// into, masking a read that happens before its own arm's assignment (e.g.
        /// <c>$x = $cond ? ($z = $z + 1) : ($z = 2);</c>). Resolving by node identity guarantees
        /// exactly one caller performs the real split/walk/merge, from whichever state is live the
        /// first time this node is reached.
        ///
        /// Elvis (<c>expr ?: fallback</c>, <see cref="PhpTernaryOpAst.TrueExpr"/> null) is an
        /// empty-check on any value — do not require the left operand to be <c>bool</c>.
        /// </summary>
        private static void CheckTernary(
            PhpTernaryOpAst ternary,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (ternary.Condition is not null)
            {
                // Elvis (`expr ?: fallback`) is an empty-check on any type, not a boolean
                // condition — only the real ternary form (`cond ? a : b`) requires bool.
                if (ternary.TrueExpr is not null)
                {
                    CheckConditionExpression(ternary.Condition, state, context, diagnostics);
                }
                else
                {
                    // Still validate compile-time constructs on the left operand.
                    // Elvis uses a probe so progressive `&&` narrowing cannot leak.
                    var probe = state.Split(ScopeType.CodeBlock);
                    CheckerHelpers.CheckCompileTimeConstructsInTree(
                        ternary.Condition, probe, context, diagnostics);
                }
            }

            context.ResolveExpressionType(ternary, state);
        }

        /// <summary>
        /// When <c>if (!*_exists(...))</c> wraps a single matching declaration, the gate argument
        /// must name that declaration (FQN string or <c>__NAMESPACE__.'\\Name'</c>).
        /// <c>nameof(...)</c> is deferred and not validated here yet.
        /// </summary>
        private static void CheckExistenceGateArgument(
            PhpIfAst ifAst,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (!DeclarationExistenceGateHelper.TryGetExistenceGateCandidate(
                    ifAst,
                    out _,
                    out var argument,
                    out _,
                    out var declName)
                || argument is null)
            {
                return;
            }

            // Deferred: nameof of a not-yet-declared symbol is ambiguous; skip for now.
            if (argument is TyhpNameofAst)
            {
                return;
            }

            if (DeclarationExistenceGateHelper.IsValidGateArgument(
                    argument,
                    state.CurrentNamespaceName,
                    declName))
            {
                return;
            }

            var expected = DeclarationExistenceGateHelper.BuildExpectedFqn(
                state.CurrentNamespaceName,
                declName);
            CheckerHelpers.ReportError(
                context,
                state,
                argument,
                MessageCode.CheckerExistenceGateInvalidName,
                expected);
        }

        private static void CheckLoop(
            PhpLoopAst loop,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var loopState = state.Split(ScopeType.CodeBlock);
            loopState.IsInLoopContext = true;
            loopState.LoopDepth = state.LoopDepth + 1;

            switch (loop.LoopType)
            {
                case PhpLoopType.While:
                    // Positive narrowing from the condition applies to the body: entering the
                    // body means the condition held (e.g. `while (\is_string($x)) { … }`).
                    // Condition type-check uses a probe; body narrowing is applied separately.
                    CheckConditionExpression(loop.Condition, loopState, context, diagnostics);
                    ApplyConditionNarrowing(loop.Condition, loopState, context, positive: true);
                    if (loop.Body is not null)
                    {
                        context.CheckNode(loop.Body, loopState);
                    }
                    break;
                case PhpLoopType.DoWhile:
                    // Do-while runs the body before the condition is proven; do not narrow the
                    // body from the condition (that would accept a first-iteration false positive).
                    CheckConditionExpression(loop.Condition, loopState, context, diagnostics);
                    if (loop.Body is not null)
                    {
                        context.CheckNode(loop.Body, loopState);
                    }
                    break;
                case PhpLoopType.For:
                    // Order matches PHP: init → test (narrow) → body → update. Checking updates
                    // before the body used to mark update-only assignments as definite before
                    // the first iteration and would also wipe condition narrowing via
                    // ResetNarrowingOnAssignment before the body ran.
                    //
                    // for-cond list (php-src for_cond_exprs): only the *last* item is the boolean
                    // condition; preceding items (including `(void) expr` and discarded calls) are
                    // evaluated for side effects only — same as init/update lists.
                    {
                        var inits = loop.InitExpressions?.GetAllNotNull().ToList() ?? [];
                        context.CheckNodes(inits.Cast<IBase2Ast>(), loopState);
                        foreach (var init in inits)
                        {
                            CheckerHelpers.ReportNoDiscardIfDiscarded(
                                init, loopState, context, diagnostics);
                        }

                        var tests = loop.TestExpressions?.GetAllNotNull().ToList() ?? [];
                        for (var i = 0; i < tests.Count; i++)
                        {
                            var test = tests[i];
                            var isLast = i == tests.Count - 1;
                            if (isLast)
                            {
                                CheckConditionExpression(test, loopState, context, diagnostics);
                                ApplyConditionNarrowing(test, loopState, context, positive: true);
                            }
                            else
                            {
                                context.CheckNode(test, loopState);
                                CheckerHelpers.ReportNoDiscardIfDiscarded(
                                    test, loopState, context, diagnostics);
                            }
                        }

                        if (loop.Body is not null)
                        {
                            context.CheckNode(loop.Body, loopState);
                        }

                        var updates = loop.UpdateExpressions?.GetAllNotNull().ToList() ?? [];
                        context.CheckNodes(updates.Cast<IBase2Ast>(), loopState);
                        foreach (var update in updates)
                        {
                            CheckerHelpers.ReportNoDiscardIfDiscarded(
                                update, loopState, context, diagnostics);
                        }
                    }
                    break;
                case PhpLoopType.Foreach:
                    CheckForeach(loop, loopState, state, context, diagnostics);
                    if (loop.Body is not null)
                    {
                        context.CheckNode(loop.Body, loopState);
                    }
                    break;
            }

            state.Merge(loopState);
            state.HasReturnedOnAllPaths = false;
        }

        private static void CheckStatementBlock(
            PhpStatementBlockAst block,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            foreach (var statement in block.GetAllNotNull())
            {
                context.CheckNode(statement, state);
                // Expression statements that discard a #[\NoDiscard] return → TYHP4165;
                // `(void) expr` is intentional discard and suppresses that warning.
                CheckerHelpers.ReportNoDiscardIfDiscarded(statement, state, context, diagnostics);
                if (state.HasReturnedOnAllPaths && statement is not PhpStatementBlockAst)
                {
                    // Unreachable code after return is detected per-statement in jump handling.
                }
            }
        }

        private static void CheckStatement(
            IStatement? statement,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (statement is PhpStatementBlockAst block)
            {
                CheckStatementBlock(block, state, context, diagnostics);
            }
            else if (statement is not null)
            {
                context.CheckNode(statement, state);
            }
        }

        private static void CheckReturn(
            IBase2Ast node,
            IExpression? expression,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (state.IsInsideFinally)
            {
                CheckerHelpers.ReportWarning(diagnostics, state, node, MessageCode.CheckerReturnInFinally);
            }

            // ControlFlowRule suppresses child traversal on return/jump, so compile-time
            // constructs inside the returned expression would otherwise never be validated.
            // Walk only those nodes (not a full CheckNode) to avoid re-entering statement
            // rules / closure bodies from expression context.
            CheckerHelpers.CheckCompileTimeConstructsInTree(expression, state, context, diagnostics);

            var actual = expression is not null
                ? context.ResolveExpressionType(expression, state)
                : CheckedTypes.Void;

            // PHP fatal: `Method X::__construct() cannot return a value` (same for `__destruct`).
            // Bare `return;` is legal; only a value-carrying return is rejected. Prefer a dedicated
            // diagnostic over the ordinary void-mismatch (4009) so the message matches the runtime error.
            // `!state.IsInsideClosure` matters because `EnclosingCallable` deliberately keeps pointing at
            // the ctor/dtor even inside a closure declared in its body (see `IsInsideClosure` doc) — a
            // `return <value>;` inside that closure belongs to the closure, not the constructor.
            if (expression is not null
                && !state.IsInsideClosure
                && state.EnclosingCallable is ObjectConstructorMethodSymbol or ObjectDestructorMethodSymbol)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    node,
                    MessageCode.CheckerConstructorDestructorCannotReturnValue,
                    state.EnclosingCallable.Name);
            }
            else if (state.IsTypeGuardFunction)
            {
                if (!CheckerHelpers.IsBoolType(actual))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, node, MessageCode.CheckerTypeGuardInvalidReturn);
                }
            }
            else if (state.ExpectedReturnType is not null)
            {
                context.CheckReturnType(node, actual, state.ExpectedReturnType, state);
            }

            state.HasReturnedOnAllPaths = true;
        }

        private static void CheckJump(
            PhpJumpStatementAst jump,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            switch (jump.JumpType)
            {
                case PhpJumpType.Return:
                    CheckReturn(jump, jump.Expression, state, context, diagnostics);
                    break;
                case PhpJumpType.Break:
                    if (!state.IsInLoopContext && !state.IsInSwitchContext)
                    {
                        CheckerHelpers.ReportError(context, state, jump, MessageCode.CheckerBreakOutsideLoop);
                    }

                    if (state.IsInsideFinally)
                    {
                        CheckerHelpers.ReportWarning(diagnostics, state, jump, MessageCode.CheckerBreakInFinally);
                    }

                    ValidateJumpLevel(jump, state, context, diagnostics);
                    // Abrupt completion: control leaves this block (loop iteration / switch arm).
                    // Same signal as return/throw so CheckIf can absorb only the negative-narrowed
                    // arm after `if (!guard) { break; }` / `if ($x === null) { break; }`.
                    state.HasReturnedOnAllPaths = true;
                    break;
                case PhpJumpType.Continue:
                    if (!state.IsInLoopContext)
                    {
                        CheckerHelpers.ReportError(context, state, jump, MessageCode.CheckerContinueOutsideLoop);
                    }

                    ValidateJumpLevel(jump, state, context, diagnostics);
                    // Abrupt completion: skips the rest of this iteration. Enables early-exit
                    // narrowing for `if (!guard) { continue; }` (Top-type #2).
                    state.HasReturnedOnAllPaths = true;
                    break;
                case PhpJumpType.Goto:
                    CheckerHelpers.ReportError(context, state, jump, MessageCode.CheckerGotoProhibited);
                    break;
            }
        }

        private static void CheckThrow(
            PhpUnaryOpAst throwExpr,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (throwExpr.Operand is not null)
            {
                var thrown = context.ResolveExpressionType(throwExpr.Operand, state);
                if (!CheckerHelpers.IsThrowableType(thrown, context.SymbolTree, context.GlobalScope))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, throwExpr, MessageCode.CheckerThrowNotThrowable, thrown.DisplayName);
                }
            }

            state.HasReturnedOnAllPaths = true;
        }

        private static void ValidateJumpLevel(
            PhpJumpStatementAst jump,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (jump.Expression is null)
            {
                return;
            }

            _ = context.ResolveExpressionType(jump.Expression, state);
            if (jump.Expression is PhpScalarAst scalar && scalar.ValueInt64 is long level && level > state.LoopDepth)
            {
                CheckerHelpers.ReportError(context, state, jump, MessageCode.CheckerBreakOutsideLoop, level);
            }
        }

        /// <summary>
        /// Checks <c>switch</c> / <c>match</c>.
        ///
        /// <para>
        /// For <c>match</c>, the subject is checked here and arm narrowing / CheckNode /
        /// definite-assignment merge are deferred to <see cref="TypeInferrer"/>'s match inference
        /// via the memoized <see cref="CheckerRuleContext.ResolveExpressionType"/> cache — same
        /// rationale as <see cref="CheckTernary"/>. <c>match</c> arm bodies are synthesized as
        /// <c>return &lt;expr&gt;</c> unary ops (<c>PhpParserAstVisitor.VisitMatchArm</c>); those
        /// produce the match value (not the enclosing function's return). InferMatch clears
        /// <see cref="CheckerState.ExpectedReturnType"/> on each arm state and types the returned
        /// operand so the match result is validated at assignment/return use sites.
        /// </para>
        /// <para>
        /// For <c>switch</c>, each non-falling-through case group starts from a fresh
        /// <see cref="CheckerState.Split"/> of the pre-switch state (same isolation as match arms /
        /// if-else). Single-condition arms get positive
        /// <see cref="TypeNarrowingRule.ApplyConditionNarrowing"/> so the idiomatic
        /// <c>switch (true) { case \is_string($x): …; break; }</c> form narrows the case body.
        /// Multi-condition OR arms are left un-narrowed (positive OR is unsound for one variable).
        /// A fall-through <em>target</em> joins two entry paths before the body is checked: (1) the
        /// continued prior-arm state after <see cref="RevertStaleGuardNarrowing"/> (prior guard is
        /// not provably true when falling in from above; a real assignment the falling-through body
        /// made still keeps its value) and (2) a fresh direct-entry <see cref="CheckerState.Split"/>
        /// of the pre-switch state with this arm's own single-condition guard applied. The body is
        /// checked once against that merge so neither path is ignored (FOUND #10). Abrupt completion
        /// still resets per arm via <see cref="CheckerState.HasReturnedOnAllPaths"/> (Top-type #2).
        /// </para>
        /// </summary>
        private static void CheckConditional(
            PhpConditionalAst conditional,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (conditional.Expression is not null)
            {
                context.CheckNode(conditional.Expression, state);
            }

            if (conditional.IsMatchSyntax)
            {
                context.ResolveExpressionType(conditional, state);
                return;
            }

            var arms = conditional.Arms?.GetAllNotNull().ToList() ?? [];
            CheckerState? joined = null;
            CheckerState? fallThroughState = null;
            SwitchArmGuardBaseline? fallThroughGuardBaseline = null;

            foreach (var arm in arms)
            {
                CheckerState armState;
                var isFallThroughTarget = fallThroughState is not null;
                if (isFallThroughTarget)
                {
                    // Path (1): continue the previous arm's state (assignments remain) but drop only
                    // the *unused* condition narrowing from whichever earlier arm's guard produced
                    // it — that guard is not provably true when falling in from above.
                    armState = fallThroughState!;
                    armState.HasReturnedOnAllPaths = false;
                    RevertStaleGuardNarrowing(armState, fallThroughGuardBaseline);
                    fallThroughGuardBaseline = null;

                    // Path (2): direct entry via this arm's own case label — fresh split from the
                    // pre-switch state with this arm's single-condition guard applied. Merge before
                    // checking the body so uses that are only safe on one of the two paths are
                    // rejected (FOUND #10). Do not treat the joined entry as abrupt.
                    var directEntry = state.Split(ScopeType.CodeBlock);
                    directEntry.IsInSwitchContext = true;
                    directEntry.HasReturnedOnAllPaths = false;
                    ApplySwitchArmConditionNarrowing(arm, directEntry, context);
                    armState.Merge(directEntry);
                    armState.HasReturnedOnAllPaths = false;
                }
                else
                {
                    armState = state.Split(ScopeType.CodeBlock);
                    armState.IsInSwitchContext = true;
                    armState.HasReturnedOnAllPaths = false;
                    fallThroughGuardBaseline = ApplySwitchArmConditionNarrowing(arm, armState, context);
                }

                // Conditions are checked against the pre-switch state (un-narrowed), matching
                // InferMatch — the case label expression is not itself under the arm's narrowing.
                // Use a probe so progressive `&&` narrowing cannot mutate the pre-switch state.
                if (arm.Conditions is not null)
                {
                    foreach (var condition in arm.Conditions.GetAllNotNull())
                    {
                        var probe = state.Split(ScopeType.CodeBlock);
                        context.CheckNode(condition, probe);
                    }
                }

                if (arm.Body is not null)
                {
                    context.CheckNode(arm.Body, armState);
                }

                if (!armState.HasReturnedOnAllPaths)
                {
                    fallThroughState = armState;
                }
                else
                {
                    fallThroughState = null;
                    fallThroughGuardBaseline = null;
                    if (joined is null)
                    {
                        joined = armState;
                    }
                    else
                    {
                        joined.Merge(armState);
                    }
                }
            }

            if (fallThroughState is not null)
            {
                if (joined is null)
                {
                    joined = fallThroughState;
                }
                else
                {
                    joined.Merge(fallThroughState);
                }
            }

            if (joined is not null)
            {
                state.Merge(joined);
            }

            state.HasReturnedOnAllPaths = false;
        }

        /// <summary>
        /// Snapshot of the variable/property narrowing a single arm's own case-condition guard
        /// changed, captured immediately before (<c>Pre*</c>) and after (<c>Post*</c>) narrowing is
        /// applied. Lets a later fall-through transition tell "still exactly what the guard set —
        /// safe to revert" apart from "the arm's body went on to reassign this for real — keep it"
        /// without needing to distinguish the two kinds of write at the point they happen (both go
        /// through the same <see cref="VariableState.NarrowedType"/> / property-narrowing fields).
        /// </summary>
        private sealed record SwitchArmGuardBaseline(
            Dictionary<string, (ICheckedType? Pre, bool PreIsPossiblyNull, ICheckedType? Post, bool PostIsPossiblyNull)> Variables,
            Dictionary<string, (ICheckedType? Pre, ICheckedType? Post)> Properties);

        /// <summary>
        /// Applies positive condition narrowing for a single-condition switch arm. Default arms and
        /// multi-condition (OR) arms are left un-narrowed — same rule as match arms. Returns which
        /// variables/properties the narrowing touched (and their pre-narrowing values), or <c>null</c>
        /// if nothing was narrowed, so a fall-through transition can undo exactly this guard's effect
        /// later — see <see cref="RevertStaleGuardNarrowing"/>.
        /// </summary>
        private static SwitchArmGuardBaseline? ApplySwitchArmConditionNarrowing(
            PhpConditionalArmAst arm,
            CheckerState armState,
            CheckerRuleContext context)
        {
            if (arm.IsDefault || arm.Conditions is null)
            {
                return null;
            }

            var conditions = arm.Conditions.GetAllNotNull().ToList();
            // Multiple arm conditions would be OR'd. Positive OR narrowing is unsound for a single
            // variable (documented on TypeNarrowingRule), so only narrow when there is exactly one.
            if (conditions.Count != 1)
            {
                return null;
            }

            var preVariables = armState.Variables.ToDictionary(
                kvp => kvp.Key,
                kvp => (kvp.Value.NarrowedType, kvp.Value.IsPossiblyNull),
                StringComparer.Ordinal);
            var prePropertyInit = armState.PropertyInit.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.NarrowedType,
                StringComparer.Ordinal);

            ApplyConditionNarrowing(conditions[0], armState, context, positive: true);

            Dictionary<string, (ICheckedType? Pre, bool PreIsPossiblyNull, ICheckedType? Post, bool PostIsPossiblyNull)>? changedVariables = null;
            foreach (var (name, pre) in preVariables)
            {
                if (armState.Variables.TryGetValue(name, out var post)
                    && (!ReferenceEquals(post.NarrowedType, pre.NarrowedType) || post.IsPossiblyNull != pre.IsPossiblyNull))
                {
                    changedVariables ??= new(StringComparer.Ordinal);
                    changedVariables[name] = (pre.NarrowedType, pre.IsPossiblyNull, post.NarrowedType, post.IsPossiblyNull);
                }
            }

            Dictionary<string, (ICheckedType? Pre, ICheckedType? Post)>? changedProperties = null;
            foreach (var (key, preType) in prePropertyInit)
            {
                if (armState.PropertyInit.TryGetValue(key, out var post) && !ReferenceEquals(post.NarrowedType, preType))
                {
                    changedProperties ??= new(StringComparer.Ordinal);
                    changedProperties[key] = (preType, post.NarrowedType);
                }
            }

            if (changedVariables is null && changedProperties is null)
            {
                return null;
            }

            return new SwitchArmGuardBaseline(
                changedVariables ?? new(StringComparer.Ordinal),
                changedProperties ?? new(StringComparer.Ordinal));
        }

        /// <summary>
        /// Undoes exactly the narrowing an earlier arm's own case-condition guard applied — but only
        /// for variables/properties still holding precisely that guard's value. A falling-through
        /// body reassigning one for real (<c>$x = "…";</c>, <c>$this->prop = …;</c>) leaves a
        /// different value in place at this point, so that real assignment is left untouched;
        /// only the still-unconsumed guard assumption is dropped. Used when a switch arm falls
        /// through into the next label, since entry there may also be via that label alone (the
        /// guard is not provably true on that path).
        /// </summary>
        private static void RevertStaleGuardNarrowing(CheckerState armState, SwitchArmGuardBaseline? baseline)
        {
            if (baseline is null)
            {
                return;
            }

            foreach (var (name, entry) in baseline.Variables)
            {
                if (!armState.Variables.TryGetValue(name, out var current)
                    || !ReferenceEquals(current.NarrowedType, entry.Post)
                    || current.IsPossiblyNull != entry.PostIsPossiblyNull)
                {
                    continue;
                }

                if (entry.Pre is null)
                {
                    armState.ResetNarrowing(name);
                }
                else
                {
                    armState.NarrowVariable(name, entry.Pre);
                }

                if (armState.LookupVariable(name) is { } varState)
                {
                    varState.IsPossiblyNull = entry.PreIsPossiblyNull;
                }
            }

            foreach (var (key, entry) in baseline.Properties)
            {
                if (!armState.PropertyInit.TryGetValue(key, out var current) || !ReferenceEquals(current.NarrowedType, entry.Post))
                {
                    continue;
                }

                if (entry.Pre is null)
                {
                    armState.ResetPropertyNarrowing(key);
                }
                else
                {
                    armState.NarrowProperty(key, entry.Pre);
                }
            }
        }
    }
}
