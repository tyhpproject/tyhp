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
    /// Undeclared instance-property writes (TYHP4134) are allowed only when the receiver
    /// type is exactly the global engine class <c>\stdClass</c> (not a subclass, not a
    /// generic parameter constrained to it). Composite receivers (nullable, union,
    /// intersection, generic-parameter constraints) are checked by a dedicated resolver
    /// in this rule.
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
            // type of the whole property-access expression. Unknown/mixed/scalar/unconstrained
            // generic receivers cannot be checked and must not be flagged. Composite receivers
            // (nullable, union, intersection, generic-parameter constraints) are walked here
            // rather than through TryGetObjectDeclaration, which only matches a single concrete
            // object declaration.
            var receiverType = context.ResolveExpressionType(receiver, state);

            // Properties are stored in Members under their declared name including the leading
            // '$' (to keep the property namespace distinct from the method namespace, since PHP
            // allows a property and method with the same bare name). Member access (`$this->foo`)
            // yields the bare name, so normalize to the '$'-prefixed key before lookup.
            var propertyKey = memberName.StartsWith('$') ? memberName : "$" + memberName;

            if (ReceiverAllowsUndeclaredWrite(receiverType, propertyKey, state, context))
            {
                return;
            }

            CheckerHelpers.ReportError(
                context, state, binary, MessageCode.CheckerDynamicPropertyProhibited, memberName);
        }

        /// <summary>
        /// Verdict for an undeclared instance-property write on one receiver type.
        /// </summary>
        private enum UndeclaredWriteVerdict
        {
            /// <summary>
            /// Not a checkable object (mixed, scalar, unconstrained type parameter). Leave
            /// TYHP4134 unreported, matching the historical unknown-receiver skip.
            /// </summary>
            Unknown,

            /// <summary>Exact engine <c>\stdClass</c> named gate, or a declared property.</summary>
            Allowed,

            /// <summary>Checkable object that is not exact <c>\stdClass</c> and lacks the property.</summary>
            Prohibited,
        }

        /// <summary>
        /// Suppress TYHP4134 unless some checkable object arm of the receiver would reject the
        /// write. The exact <c>\stdClass</c> named gate applies only when that type is the
        /// receiver itself (including nullable wrap); a generic parameter constrained to
        /// <c>\stdClass</c> is not the named gate, because the bound includes subclasses.
        /// </summary>
        private static bool ReceiverAllowsUndeclaredWrite(
            ICheckedType receiverType,
            string propertyKey,
            CheckerState state,
            CheckerRuleContext context) =>
            EvaluateUndeclaredWrite(
                receiverType,
                propertyKey,
                state,
                context,
                stdClassGateApplies: true,
                visiting: null) != UndeclaredWriteVerdict.Prohibited;

        private static UndeclaredWriteVerdict EvaluateUndeclaredWrite(
            ICheckedType type,
            string propertyKey,
            CheckerState state,
            CheckerRuleContext context,
            bool stdClassGateApplies,
            HashSet<GenericTypeParameterSymbol>? visiting)
        {
            switch (type)
            {
                case NullableCheckedType nullable:
                    return EvaluateUndeclaredWrite(
                        nullable.InnerType, propertyKey, state, context, stdClassGateApplies, visiting);

                case UnionCheckedType union:
                    return CombineUnionArms(
                        union.Members, propertyKey, state, context, stdClassGateApplies, visiting);

                case IntersectionCheckedType intersection:
                    return CombineIntersectionArms(
                        intersection.Members, propertyKey, state, context, stdClassGateApplies, visiting);

                case GenericCheckedType generic:
                    return EvaluateUndeclaredWrite(
                        generic.BaseType, propertyKey, state, context, stdClassGateApplies, visiting);

                case StaticCheckedType staticType:
                    return EvaluateUndeclaredWrite(
                        staticType.DeclaringType, propertyKey, state, context, stdClassGateApplies, visiting);

                case LiteralCheckedType literal:
                    return EvaluateUndeclaredWrite(
                        literal.UnderlyingType, propertyKey, state, context, stdClassGateApplies, visiting);

                case SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol typeParam }:
                    return EvaluateGenericParameterConstraint(
                        typeParam, propertyKey, state, context, visiting);
            }

            var objectDecl = CheckerHelpers.TryGetObjectDeclaration(type);
            if (objectDecl is not null)
            {
                return EvaluateObjectDeclarationWrite(
                    objectDecl, propertyKey, context, stdClassGateApplies);
            }

            return UndeclaredWriteVerdict.Unknown;
        }

        private static UndeclaredWriteVerdict EvaluateGenericParameterConstraint(
            GenericTypeParameterSymbol typeParam,
            string propertyKey,
            CheckerState state,
            CheckerRuleContext context,
            HashSet<GenericTypeParameterSymbol>? visiting)
        {
            visiting ??= [];
            if (!visiting.Add(typeParam))
            {
                return UndeclaredWriteVerdict.Unknown;
            }

            try
            {
                var constraint = typeParam.ResolvedConstraint
                    ?? GenericConstraintResolver.EnsureResolved(typeParam, state, context);
                if (constraint is null)
                {
                    return UndeclaredWriteVerdict.Unknown;
                }

                // The bound is an upper bound, not the receiver's exact type. `T extends \stdClass`
                // includes subclasses, so the named gate must not fire for the parameter.
                return EvaluateUndeclaredWrite(
                    constraint,
                    propertyKey,
                    state,
                    context,
                    stdClassGateApplies: false,
                    visiting);
            }
            finally
            {
                visiting.Remove(typeParam);
            }
        }

        private static UndeclaredWriteVerdict CombineUnionArms(
            IReadOnlyList<ICheckedType> members,
            string propertyKey,
            CheckerState state,
            CheckerRuleContext context,
            bool stdClassGateApplies,
            HashSet<GenericTypeParameterSymbol>? visiting)
        {
            var anyAllowed = false;
            var anyProhibited = false;
            foreach (var member in members)
            {
                switch (EvaluateUndeclaredWrite(
                    member, propertyKey, state, context, stdClassGateApplies, visiting))
                {
                    case UndeclaredWriteVerdict.Allowed:
                        anyAllowed = true;
                        break;
                    case UndeclaredWriteVerdict.Prohibited:
                        anyProhibited = true;
                        break;
                }
            }

            // A union value may be any arm: one rejecting arm is enough to TYHP4134.
            if (anyProhibited)
            {
                return UndeclaredWriteVerdict.Prohibited;
            }

            return anyAllowed ? UndeclaredWriteVerdict.Allowed : UndeclaredWriteVerdict.Unknown;
        }

        private static UndeclaredWriteVerdict CombineIntersectionArms(
            IReadOnlyList<ICheckedType> members,
            string propertyKey,
            CheckerState state,
            CheckerRuleContext context,
            bool stdClassGateApplies,
            HashSet<GenericTypeParameterSymbol>? visiting)
        {
            var anyAllowed = false;
            var anyProhibited = false;
            foreach (var member in members)
            {
                switch (EvaluateUndeclaredWrite(
                    member, propertyKey, state, context, stdClassGateApplies, visiting))
                {
                    case UndeclaredWriteVerdict.Allowed:
                        anyAllowed = true;
                        break;
                    case UndeclaredWriteVerdict.Prohibited:
                        anyProhibited = true;
                        break;
                }
            }

            // An intersection value is every arm at once, so a declared property on any arm
            // is present on the object. Report only when every checkable arm rejects the write.
            if (anyAllowed)
            {
                return UndeclaredWriteVerdict.Allowed;
            }

            return anyProhibited ? UndeclaredWriteVerdict.Prohibited : UndeclaredWriteVerdict.Unknown;
        }

        private static UndeclaredWriteVerdict EvaluateObjectDeclarationWrite(
            ObjectDeclarationSymbol objectDecl,
            string propertyKey,
            CheckerRuleContext context,
            bool stdClassGateApplies)
        {
            // Named gate: undeclared writes are legal only on the global engine class
            // `\stdClass` itself. Subclasses, harvest bags, leftover
            // `#[\AllowDynamicProperties]` stamps, and generic-parameter bounds are not an opt-in.
            if (stdClassGateApplies && IsExactEngineStdClass(objectDecl))
            {
                return UndeclaredWriteVerdict.Allowed;
            }

            return HasDeclaredInstanceProperty(objectDecl, propertyKey, context)
                ? UndeclaredWriteVerdict.Allowed
                : UndeclaredWriteVerdict.Prohibited;
        }

        private static bool HasDeclaredInstanceProperty(
            ObjectDeclarationSymbol objectDecl,
            string propertyKey,
            CheckerRuleContext context)
        {
            foreach (var declInChain in EnumerateClassHierarchy(objectDecl, context))
            {
                if (declInChain.Members.TryGetValue(propertyKey, out var member)
                    && member is ObjectPropertySymbol)
                {
                    return true;
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
                        return true;
                    }
                }

                if (hasUnresolvedTrait)
                {
                    return true;
                }
            }

            return false;
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

        /// <summary>
        /// PHP's engine <c>\stdClass</c> (global class), not a namespaced lookalike and not
        /// a subclass. Undeclared property writes are legal only on this type.
        /// </summary>
        private static bool IsExactEngineStdClass(ObjectDeclarationSymbol objectDecl)
        {
            if (objectDecl.ObjectKind != PhpTypeDeclType.Class)
            {
                return false;
            }

            var fqn = string.IsNullOrEmpty(objectDecl.FullyQualifiedName)
                ? objectDecl.Name
                : objectDecl.FullyQualifiedName;
            return string.Equals(fqn.TrimStart('\\'), "stdClass", StringComparison.OrdinalIgnoreCase);
        }
    }
}
