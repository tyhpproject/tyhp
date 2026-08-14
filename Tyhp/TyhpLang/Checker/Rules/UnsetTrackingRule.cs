using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Prop-init #8: track <c>unset($x)</c> / <c>unset($this->prop)</c> against definite-assignment
    /// and property-init state. Typed properties require <c>#[\Tyhp\AllowUnset]</c>.
    /// </summary>
    public sealed class UnsetTrackingRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes => [typeof(PhpUnsetStatementAst)];

        public bool SuppressChildTraversal(IBase2Ast node) => true;

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            if (node is not PhpUnsetStatementAst unset)
            {
                return;
            }

            foreach (var target in unset.Variables?.GetAllNotNull() ?? [])
            {
                CheckUnsetTarget(target, state, context, diagnostics);
            }
        }

        private static void CheckUnsetTarget(
            IExpression target,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (target is PhpVariableAst variable)
            {
                var name = CheckerHelpers.GetVariableName(variable);
                if (name is null || CheckerHelpers.IsThisVariable(variable))
                {
                    return;
                }

                state.UnsetVariable(name);
                return;
            }

            if (target is not PhpDereferenceableAst
                {
                    Base: PhpVariableAst receiver,
                    Suffix: PhpInstanceMemberAccessAst memberAccess,
                } deref
                || !CheckerHelpers.IsThisVariable(receiver))
            {
                // Array offsets, static props, non-$this receivers: leave alone (out of #7/#8 scope).
                return;
            }

            var memberName = GetMemberName(memberAccess.MemberName);
            if (memberName is null || memberName.StartsWith('{'))
            {
                return;
            }

            var propertyKey = memberName.StartsWith('$') ? memberName : "$" + memberName;
            if (state.EnclosingObject?.Members.TryGetValue(propertyKey, out var member) is not true
                || member is not ObjectPropertySymbol prop
                || prop.SymbolType != SymbolType.InstanceObjectProperty
                || prop.DeclaredType is null
                || prop.HasAccessor)
            {
                // Not a tracked typed storage property.
                return;
            }

            if (!prop.AllowsUnset)
            {
                var displayName = propertyKey.StartsWith('$') ? propertyKey[1..] : propertyKey;
                CheckerHelpers.ReportError(
                    context,
                    state,
                    deref,
                    MessageCode.CheckerUnsetTypedPropertyWithoutAllowUnset,
                    displayName);
                return;
            }

            state.UnsetProperty(propertyKey);
        }

        private static string? GetMemberName(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                PhpScalarAst scalar => scalar.ValueString ?? scalar.ValueInt64?.ToString(),
                PhpVariableAst variable => CheckerHelpers.GetVariableName(variable),
                _ => expression?.Identifier,
            };
    }
}
