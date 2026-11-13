using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Enforces non-nullable-by-default semantics and definite-assignment at variable / property use sites.
    /// </summary>
    public sealed class NullSafetyRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(PhpVariableAst),
            typeof(PhpBinaryOpAst),
            typeof(PhpDereferenceableAst),
            typeof(PhpIssetStatementAst),
            typeof(PhpEmptyStatementAst),
            typeof(TyhpVariableExistsAst),
        ];

        public bool SuppressChildTraversal(IBase2Ast node) =>
            node is PhpIssetStatementAst
                or PhpEmptyStatementAst
                or TyhpVariableExistsAst
                || IsSimpleAssignWrite(node)
                || IsCoalesce(node)
                || IsCoalesceAssign(node);

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            // An assignment to a hooked property runs its `set` hook, which can read other
            // properties before the constructor has assigned them.
            if (node is PhpBinaryOpAst assignment
                && assignment.Left is PhpDereferenceableAst assignedAccess
                && PhpAssignmentOperatorExtensions.FromToken(
                    GetTokenType(assignment.Operator),
                    assignment.Operator?.ValueString) is not null)
            {
                CheckHookRunsDuringConstruction(assignedAccess, runsSetHook: true, state, context);
            }

            switch (node)
            {
                case PhpBinaryOpAst binary when IsSimpleAssignWrite(binary):
                    // Simple `$x = …` / `$this->prop = …` — the left is a write target, not a read.
                    // Walk every other child so nested expressions still get checked.
                    foreach (var child in binary.AstChildren)
                    {
                        if (child is not null && !ReferenceEquals(child, binary.Left))
                        {
                            context.CheckNode(child, state);
                        }
                    }
                    return;

                case PhpBinaryOpAst binary when IsCoalesce(binary) || IsCoalesceAssign(binary):
                    // `??` / `??=` left operands are existence probes (PHP does not throw on
                    // uninitialized typed properties or undefined variables). Check the left under
                    // the probe flag, then the right normally.
                    if (binary.Left is not null)
                    {
                        var previous = state.IsExistenceProbeContext;
                        state.IsExistenceProbeContext = true;
                        context.CheckNode(binary.Left, state);
                        state.IsExistenceProbeContext = previous;
                    }

                    if (binary.Right is not null)
                    {
                        context.CheckNode(binary.Right, state);
                    }

                    return;

                case PhpIssetStatementAst:
                case PhpEmptyStatementAst:
                case TyhpVariableExistsAst:
                    // Existence probes are not uses — do not report 4014/4015/4157 on their operands.
                    // Their operands are still typed (silently) so the emitter sees the receiver's
                    // checked type, e.g. to erase `isset($anon->key)` on an anonymous struct to
                    // `isset($anon['key'])`.
                    RecordProbeOperandTypes(node, state, context);
                    return;

                case PhpVariableAst variable:
                    CheckVariableUse(variable, state, context);
                    return;

                case PhpDereferenceableAst deref:
                    CheckPropertyUse(deref, state, context);
                    return;
            }
        }

        private static void RecordProbeOperandTypes(
            IBase2Ast probe,
            CheckerState state,
            CheckerRuleContext context)
        {
            var operands = probe switch
            {
                PhpIssetStatementAst isset => isset.Variables?.GetAllNotNull().Cast<IBase2Ast>().ToList() ?? [],
                PhpEmptyStatementAst { Expression: IBase2Ast operand } => [operand],
                _ => new List<IBase2Ast>(),
            };

            foreach (var operand in operands)
            {
                // Type the whole access chain (`$a->b->c`, `$a['k']->c`) so every receiver hop has
                // a recorded type, not just the outermost operand.
                IBase2Ast? hop = operand;
                while (hop is not null)
                {
                    context.ResolveExpressionType(hop, state);
                    hop = (hop as PhpDereferenceableAst)?.Base as IBase2Ast;
                }
            }
        }

        private static void CheckVariableUse(
            PhpVariableAst variable,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (state.IsExistenceProbeContext || CheckerHelpers.IsThisVariable(variable))
            {
                return;
            }

            var name = CheckerHelpers.GetVariableName(variable);
            if (name is null)
            {
                return;
            }

            var varState = state.LookupVariable(name);
            if (varState is null)
            {
                return;
            }

            if (varState.IsPossiblyUndefined)
            {
                CheckerHelpers.ReportError(
                    context, state, variable, MessageCode.CheckerVariablePossiblyUndefined, name);
            }

            if (varState.IsPossiblyNull && !varState.EffectiveType.IsNullable)
            {
                CheckerHelpers.ReportError(
                    context, state, variable, MessageCode.CheckerVariablePossiblyNull, name);
            }
        }

        /// <summary>
        /// In a constructor, <c>$this->hooked</c> runs the property's <c>get</c> hook on a read
        /// and its <c>set</c> hook on a write. The hook body is checked once, with the facts that
        /// hold after construction, so a property it reads that this path has not initialized yet
        /// is reported here, at the access that runs the hook (TYHP4157).
        /// </summary>
        private static void CheckHookRunsDuringConstruction(
            PhpDereferenceableAst access,
            bool runsSetHook,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (state.EnclosingCallable is not ObjectConstructorMethodSymbol
                || state.IsInsideClosure
                || state.EnclosingObject is not { } owner
                || PropertyInitializationAnalysis.ThisMemberKey(access) is not { } key
                || PropertyInitializationAnalysis.FindHookedInstanceProperty(
                    owner, key, context.SymbolTree, context.GlobalScope) is not { } hooked)
            {
                return;
            }

            foreach (var read in PropertyInitializationAnalysis.HookReadsOfProperties(
                hooked, runsSetHook, owner, context.SymbolTree, context.GlobalScope))
            {
                if (state.LookupPropertyInit(read) is not { IsDefinitelyInitialized: false })
                {
                    continue;
                }

                CheckerHelpers.ReportError(
                    context,
                    state,
                    access,
                    MessageCode.CheckerPropertyPossiblyUninitialized,
                    read.StartsWith('$') ? read[1..] : read);
            }
        }

        private static void CheckPropertyUse(
            PhpDereferenceableAst deref,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (state.IsExistenceProbeContext)
            {
                return;
            }

            if (deref.Suffix is not PhpInstanceMemberAccessAst memberAccess)
            {
                return;
            }

            // Only `$this->prop` (Prop-init #7 intraprocedural scope). Method calls use
            // <see cref="PhpCallAst"/> as the suffix, so they never reach here.
            if (deref.Base is not PhpVariableAst receiver || !CheckerHelpers.IsThisVariable(receiver))
            {
                return;
            }

            var memberName = GetMemberName(memberAccess.MemberName);
            if (memberName is null || memberName.StartsWith('{'))
            {
                return;
            }

            CheckHookRunsDuringConstruction(deref, runsSetHook: false, state, context);

            var propertyKey = memberName.StartsWith('$') ? memberName : "$" + memberName;
            var propState = state.LookupPropertyInit(propertyKey);
            if (propState is null || propState.IsDefinitelyInitialized)
            {
                return;
            }

            var displayName = propertyKey.StartsWith('$') ? propertyKey[1..] : propertyKey;
            CheckerHelpers.ReportError(
                context,
                state,
                deref,
                MessageCode.CheckerPropertyPossiblyUninitialized,
                displayName);
        }

        /// <summary>
        /// True for plain <c>$x = …</c> / <c>$this->prop = …</c> (and <c>:=</c>) where the left
        /// is write-only. Compound assignments (<c>+=</c>, <c>??=</c>, …) still read the left-hand side.
        /// </summary>
        internal static bool IsSimpleAssignWrite(IBase2Ast node)
        {
            if (node is not PhpBinaryOpAst binary)
            {
                return false;
            }

            if (binary.Left is not PhpVariableAst
                && binary.Left is not PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst })
            {
                return false;
            }

            // `$this->foo()` is a call, not a property write target.
            if (binary.Left is PhpDereferenceableAst { Suffix: PhpCallAst })
            {
                return false;
            }

            var op = PhpAssignmentOperatorExtensions.FromToken(
                GetTokenType(binary.Operator),
                binary.Operator?.ValueString);
            return op is PhpAssignmentOperator.Assign or PhpAssignmentOperator.UsingEqual;
        }

        internal static bool IsCoalesce(IBase2Ast node) =>
            node is PhpBinaryOpAst binary
            && PhpBinaryOperatorExtensions.FromToken(
                GetTokenType(binary.Operator),
                binary.Operator?.ValueString) == PhpBinaryOperator.Coalesce;

        internal static bool IsCoalesceAssign(IBase2Ast node)
        {
            if (node is not PhpBinaryOpAst binary)
            {
                return false;
            }

            var op = PhpAssignmentOperatorExtensions.FromToken(
                GetTokenType(binary.Operator),
                binary.Operator?.ValueString);
            return op == PhpAssignmentOperator.CoalesceAssign;
        }

        internal static string? GetMemberName(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                PhpScalarAst scalar => scalar.ValueString ?? scalar.ValueInt64?.ToString(),
                PhpVariableAst variable => CheckerHelpers.GetVariableName(variable),
                _ => expression?.Identifier,
            };

        private static int GetTokenType(TokenValueAst? token) =>
            token?.ValueInt64 is long value ? (int)value : TyhpParser.Eof;
    }
}
