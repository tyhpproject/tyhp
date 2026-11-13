using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Rejects <c>unset</c> of a local variable, a typed instance property, or a static property
    /// (TYHP4158). Array-offset <c>unset</c> stays allowed. Rejection does not clear
    /// definite-assignment or property-initialization state.
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
                CheckUnsetTarget(target, state, context);
            }
        }

        private static void CheckUnsetTarget(
            IExpression target,
            CheckerState state,
            CheckerRuleContext context)
        {
            // Whether or not the unset is accepted, a narrowed member path under the target no
            // longer holds. Definite-assignment and property-initialization state are never
            // touched here.
            if (TypeNarrowingRule.TryGetMemberPathKey(target, out var unsetPath) && unsetPath is not null)
            {
                state.RemoveMemberAccessNarrowingUnder(unsetPath);
            }

            if (TryGetBannedStorageSlot(target, state, context, out var slot))
            {
                CheckerHelpers.ReportError(
                    context,
                    state,
                    target,
                    MessageCode.CheckerUnsetStorageSlot,
                    slot);
            }
        }

        /// <summary>
        /// True when <paramref name="target"/> unsets a variable or property storage slot.
        /// Array-offset unset (<c>unset($arr[$i])</c>, <c>unset($obj->arr[$key])</c>) is not a slot.
        /// A dynamic instance property name is not reported here: it does not name one typed slot.
        /// </summary>
        private static bool TryGetBannedStorageSlot(
            IExpression target,
            CheckerState state,
            CheckerRuleContext context,
            out string slot)
        {
            slot = "";
            if (target is PhpDereferenceableAst { Suffix: PhpArrayAccessAst })
            {
                return false;
            }

            if (target is PhpVariableAst variable && TryDescribeVariable(variable, out slot))
            {
                return true;
            }

            if (TryDescribeStaticProperty(target, out slot))
            {
                return true;
            }

            return TryDescribeTypedInstanceProperty(target, state, context, out slot);
        }

        private static bool TryDescribeVariable(PhpVariableAst variable, out string slot)
        {
            slot = "";
            if (variable.VariableExpression is PhpVariableAst inner)
            {
                var innerName = CheckerHelpers.GetVariableName(inner);
                slot = innerName is null ? "$$" : "$$" + innerName.TrimStart('$');
                return true;
            }

            var name = CheckerHelpers.GetVariableName(variable);
            if (name is null)
            {
                return false;
            }

            slot = "$" + name.TrimStart('$');
            return true;
        }

        private static bool TryDescribeStaticProperty(IExpression target, out string slot)
        {
            slot = "";
            if (target is not PhpDereferenceableAst { Suffix: PhpStaticMemberAccessAst staticAccess } deref)
            {
                return false;
            }

            var classPart = deref.Base is PhpNameAst name
                ? (string.IsNullOrEmpty(name.ValueString) ? name.Identifier : name.ValueString)
                : null;
            var propPart = DescribeStaticMember(staticAccess);
            slot = string.IsNullOrEmpty(classPart) ? propPart : classPart + "::" + propPart;
            return true;
        }

        private static string DescribeStaticMember(PhpStaticMemberAccessAst staticAccess)
        {
            if (staticAccess.Member is PhpVariableAst { VariableExpression: PhpVariableAst inner })
            {
                var innerName = CheckerHelpers.GetVariableName(inner);
                return innerName is null ? "$$" : "$$" + innerName.TrimStart('$');
            }

            var simple = staticAccess.Member switch
            {
                PhpVariableAst member => CheckerHelpers.GetVariableName(member),
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                _ => staticAccess.Member?.Identifier,
            };

            if (string.IsNullOrEmpty(simple) || simple.StartsWith('{'))
            {
                return "$";
            }

            return "$" + simple.TrimStart('$');
        }

        private static bool TryDescribeTypedInstanceProperty(
            IExpression target,
            CheckerState state,
            CheckerRuleContext context,
            out string slot)
        {
            slot = "";
            if (target is not PhpDereferenceableAst
                {
                    Suffix: PhpInstanceMemberAccessAst memberAccess,
                } deref
                || !TryGetInstancePropertyName(memberAccess, out var propName))
            {
                return false;
            }

            if (deref.Base is null
                || !ReceiverHasTypedProperty(deref.Base, propName, state, context))
            {
                return false;
            }

            slot = DescribeInstanceSlot(deref.Base, propName);
            return true;
        }

        private static bool TryGetInstancePropertyName(PhpInstanceMemberAccessAst memberAccess, out string propName)
        {
            propName = "";
            if (CheckerHelpers.IsNullSafeAccessor(memberAccess.Accessor))
            {
                return false;
            }

            // Same rule as the type inferrer: only a bare identifier names a property. A computed
            // name (`->{$expr}`, `->$var`, `->{'literal'}`) is not a resolved slot.
            var memberName = memberAccess.MemberName switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                _ => null,
            };

            if (string.IsNullOrEmpty(memberName) || memberName.StartsWith('{') || memberName.StartsWith('$'))
            {
                return false;
            }

            propName = memberName;
            return true;
        }

        private static string DescribeInstanceSlot(IDereferenceableBase? receiver, string propName)
        {
            if (receiver is PhpVariableAst variable && CheckerHelpers.GetVariableName(variable) is { } name)
            {
                return "$" + name.TrimStart('$') + "->" + propName;
            }

            return "$" + propName;
        }

        private static bool ReceiverHasTypedProperty(
            IDereferenceableBase receiver,
            string propName,
            CheckerState state,
            CheckerRuleContext context)
        {
            var propertyKey = "$" + propName;
            var receiverType = context.ResolveExpressionType(receiver, state);
            if (TypeHasTypedProperty(receiverType, propertyKey, context))
            {
                return true;
            }

            if (receiver is PhpVariableAst variable
                && CheckerHelpers.IsThisVariable(variable)
                && state.EnclosingObject is { } enclosing
                && context.SymbolTree.ResolveMember(propertyKey, enclosing, new DiagnosticBag())
                    is ObjectPropertySymbol prop)
            {
                return IsTypedStorageProperty(prop);
            }

            return false;
        }

        private static bool TypeHasTypedProperty(
            ICheckedType type,
            string propertyKey,
            CheckerRuleContext context)
        {
            switch (CheckerHelpers.UnwrapMemberAccessReceiver(type))
            {
                case UnionCheckedType union:
                    return union.Members.Any(arm => TypeHasTypedProperty(arm, propertyKey, context));
                case IntersectionCheckedType intersection:
                    return intersection.Members.Any(arm => TypeHasTypedProperty(arm, propertyKey, context));
                default:
                    var receiver = CheckerHelpers.UnwrapMemberAccessReceiver(type);

                    // A struct is array-backed: `unset($struct->optional)` removes an optional
                    // key and is not a typed property slot.
                    if (CheckerHelpers.IsClosedMemberReceiver(receiver))
                    {
                        return false;
                    }

                    var declaration = CheckerHelpers.TryGetObjectDeclaration(receiver);
                    if (declaration is null)
                    {
                        return false;
                    }

                    return context.SymbolTree.ResolveMember(propertyKey, declaration, new DiagnosticBag())
                        is ObjectPropertySymbol prop
                        && IsTypedStorageProperty(prop);
            }
        }

        private static bool IsTypedStorageProperty(ObjectPropertySymbol prop) =>
            prop.DeclaredType is not null
            && prop.SymbolType is SymbolType.InstanceObjectProperty or SymbolType.StaticObjectProperty;
    }
}
