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
    /// Detects PHP dynamic features and constructs prohibited or restricted in Tyhp.
    /// </summary>
    public sealed class RestrictedFeatureRule : ICheckerRule
    {
        private static readonly HashSet<int> IncludeOperators =
        [
            TyhpParser.T_INCLUDE,
            TyhpParser.T_INCLUDE_ONCE,
            TyhpParser.T_REQUIRE,
            TyhpParser.T_REQUIRE_ONCE,
        ];

        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(PhpEvalStatementAst),
            typeof(PhpUnaryOpAst),
            typeof(PhpVariableAst),
            typeof(PhpGlobalStatementAst),
            typeof(PhpDereferenceableAst),
            typeof(PhpBinaryOpAst),
        ];

        public bool Handles(IBase2Ast node) =>
            node is not PhpUnaryOpAst unary || IsIncludeOperator(unary.Operator);

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            switch (node)
            {
                case PhpEvalStatementAst eval when !context.Options.AllowEval:
                    CheckerHelpers.ReportInfo(
                        diagnostics, state, eval, MessageCode.CheckerEvalUsage);
                    break;

                case PhpUnaryOpAst unary when IsIncludeOperator(unary.Operator):
                    CheckerHelpers.ReportError(
                        context, state, unary, MessageCode.CheckerIncludeNotAllowed);
                    break;

                case PhpVariableAst variable when IsVariableVariable(variable):
                    CheckerHelpers.ReportError(
                        context, state, variable, MessageCode.CheckerVariableVariableProhibited);
                    break;

                case PhpGlobalStatementAst global:
                    CheckerHelpers.ReportWarning(
                        diagnostics, state, global, MessageCode.CheckerGlobalVariableWarning);
                    break;

                case PhpDereferenceableAst deref when deref.Suffix is PhpCallAst call:
                    CheckRestrictedCall(deref, call, state, context);
                    break;

                case PhpBinaryOpAst binary when IsAssignmentOperator(binary.Operator?.ValueString):
                    CheckDynamicPropertyAssignment(binary, state, context, diagnostics);
                    break;
            }
        }

        private static void CheckRestrictedCall(
            PhpDereferenceableAst deref,
            PhpCallAst call,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (deref.Base is not PhpNameAst nameAst)
            {
                return;
            }

            var name = nameAst.ValueString ?? nameAst.Identifier ?? nameAst.BoundSymbol?.Name;
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            if (string.Equals(name, "compact", StringComparison.OrdinalIgnoreCase))
            {
                CheckerHelpers.ReportError(
                    context, state, call, MessageCode.CheckerCompactProhibited);
            }
            else if (string.Equals(name, "extract", StringComparison.OrdinalIgnoreCase))
            {
                CheckerHelpers.ReportError(
                    context, state, call, MessageCode.CheckerExtractProhibited);
            }
        }

        private static void CheckDynamicPropertyAssignment(
            PhpBinaryOpAst binary,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (binary.Left is not PhpDereferenceableAst { Base: { } receiver, Suffix: PhpInstanceMemberAccessAst memberAccess })
            {
                return;
            }

            // Only statically-known property names can be checked. A dynamic member name
            // (`$this->{$expr}` / `$this->$var`) computes the property at runtime, so the
            // property being targeted is unknown at compile time and must not be flagged.
            if (memberAccess.MemberName is not (PhpNameAst or TokenValueAst))
            {
                return;
            }

            var memberName = GetMemberName(memberAccess.MemberName);
            if (memberName is null)
            {
                return;
            }

            // Resolve the type of the receiver (the object being assigned into), not the
            // type of the whole property-access expression. If the receiver type cannot be
            // resolved to a concrete object declaration (unknown/mixed/scalar), we cannot
            // know whether the property exists, so we must not report a violation.
            var receiverType = context.ResolveExpressionType(receiver, state);
            var objectDecl = CheckerHelpers.TryGetObjectDeclaration(receiverType);
            if (objectDecl is null)
            {
                return;
            }

            // Properties are stored in Members under their declared name including the leading
            // '$' (to keep the property namespace distinct from the method namespace, since PHP
            // allows a property and method with the same bare name). Member access (`$this->foo`)
            // yields the bare name, so normalize to the '$'-prefixed key before lookup.
            var propertyKey = memberName.StartsWith('$') ? memberName : "$" + memberName;

            foreach (var declInChain in EnumerateClassHierarchy(objectDecl, context))
            {
                if (declInChain.Members.TryGetValue(propertyKey, out var member)
                    && member is ObjectPropertySymbol)
                {
                    return;
                }

                if (AllowsDynamicProperties(declInChain))
                {
                    return;
                }

                // Trait members are not flattened onto the class symbol — resolve used traits
                // (transitively) and look for the property there. Only suppress when a trait
                // name cannot be resolved (property may exist but is out of reach).
                var traits = TypeComparer.ResolveUsedTraits(
                    declInChain, context.SymbolTree, context.GlobalScope, out var hasUnresolvedTrait);
                foreach (var trait in traits)
                {
                    if (trait.Members.TryGetValue(propertyKey, out var traitMember)
                        && traitMember is ObjectPropertySymbol)
                    {
                        return;
                    }
                }

                if (hasUnresolvedTrait)
                {
                    return;
                }
            }

            CheckerHelpers.ReportError(
                context, state, binary, MessageCode.CheckerDynamicPropertyProhibited, memberName);
        }

        private static IEnumerable<ObjectDeclarationSymbol> EnumerateClassHierarchy(
            ObjectDeclarationSymbol objectDecl,
            CheckerRuleContext context)
        {
            var visited = new HashSet<ObjectDeclarationSymbol>();
            for (var current = objectDecl; current is not null; current = ResolveParent(current, context))
            {
                if (!visited.Add(current))
                {
                    yield break;
                }

                yield return current;
            }
        }

        private static ObjectDeclarationSymbol? ResolveParent(
            ObjectDeclarationSymbol child,
            CheckerRuleContext context)
            => TypeComparer.TryGetParentDeclaration(child, context.SymbolTree, context.GlobalScope);

        private static bool IsVariableVariable(PhpVariableAst variable) =>
            variable.VariableExpression is not null;

        private static bool IsIncludeOperator(TokenValueAst? op)
        {
            if (op?.ValueInt64 is long tokenValue && IncludeOperators.Contains((int)tokenValue))
            {
                return true;
            }

            var text = op?.ValueString?.ToLowerInvariant();
            return text is "include" or "include_once" or "require" or "require_once";
        }

        private static bool IsAssignmentOperator(string? op) =>
            op is "=" or "+=" or "-=" or "*=" or "/=" or ".=" or "%=" or "**=" or "&=" or "|=" or "^=" or "<<=" or ">>=";

        private static string? GetMemberName(IExpression? memberName) =>
            memberName switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                IExpression expr => expr.Identifier,
                _ => null,
            };

        private static bool AllowsDynamicProperties(ObjectDeclarationSymbol objectDecl)
        {
            if (objectDecl.DeclaringAstNode is not IBase2Ast declaringNode)
            {
                return false;
            }

            foreach (var attribute in declaringNode.AstAttributes)
            {
                if (attribute is PhpAttributeAst attr && IsAllowDynamicPropertiesAttribute(attr))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAllowDynamicPropertiesAttribute(PhpAttributeAst attribute)
        {
            var name = GetAttributeName(attribute.Name);
            return name is not null
                && (string.Equals(name, "AllowDynamicProperties", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("\\AllowDynamicProperties", StringComparison.OrdinalIgnoreCase));
        }

        private static string? GetAttributeName(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                IExpression expr => expr.Identifier,
                _ => null,
            };
    }
}
