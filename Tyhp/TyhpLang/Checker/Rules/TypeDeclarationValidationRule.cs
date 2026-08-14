using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>Validates PHP type declaration rules on composite and builtin type annotations.</summary>
    public sealed class TypeDeclarationValidationRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(PhpTypeExpressionAst),
            typeof(PhpBuiltinTypeAst),
        ];

        public bool SuppressChildTraversal(IBase2Ast node) => false;

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            switch (node)
            {
                case PhpTypeExpressionAst typeExpr:
                    ValidateTypeExpression(typeExpr, state, context, diagnostics);
                    break;
                case PhpBuiltinTypeAst builtin:
                    ValidateBuiltinType(builtin, state, diagnostics);
                    break;
            }
        }

        private static void ValidateTypeExpression(
            PhpTypeExpressionAst typeExpr,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var members = typeExpr.Types?.GetAllNotNull().ToList() ?? [];
            if (members.Count == 0)
            {
                return;
            }

            var resolved = members
                .Select(m => context.ResolveTypeAnnotation(m, state))
                .ToList();

            ValidateResolvedCompositeType(typeExpr, resolved, state, diagnostics);
        }

        /// <summary>
        /// Validates union/intersection and restricted-position rules on already-resolved members.
        /// Used by parameter registration to avoid double type resolution.
        /// </summary>
        internal static void ValidateResolvedCompositeType(
            PhpTypeExpressionAst typeExpr,
            IReadOnlyList<ICheckedType> resolved,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (resolved.Count == 0)
            {
                return;
            }

            if (typeExpr.TypeKind == PhpTypeKind.Union)
            {
                ValidateUnion(resolved, typeExpr, state, diagnostics);
            }
            else if (typeExpr.TypeKind == PhpTypeKind.Intersection)
            {
                ValidateIntersection(resolved, typeExpr, state, diagnostics);
            }

            if (state.IsPropertyTypePosition || state.IsParameterTypePosition)
            {
                foreach (var member in resolved)
                {
                    ValidateRestrictedPosition(member, typeExpr, state, diagnostics, state.IsPropertyTypePosition);
                }
            }
        }

        /// <summary>
        /// Validates composite parameter/property annotations using an already-resolved type.
        /// </summary>
        internal static void ValidateResolvedParameterType(
            ITypeExpression typeAst,
            ICheckedType resolved,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (typeAst is not PhpTypeExpressionAst typeExpr)
            {
                return;
            }

            if (typeExpr.TypeKind is PhpTypeKind.Union or PhpTypeKind.Intersection)
            {
                var astMembers = typeExpr.Types?.GetAllNotNull().ToList() ?? [];
                if (astMembers.Count == 0)
                {
                    return;
                }

                var resolvedMembers = astMembers
                    .Select(member => context.ResolveTypeAnnotation(member, state))
                    .ToList();
                ValidateResolvedCompositeType(typeExpr, resolvedMembers, state, diagnostics);
                return;
            }

            ValidateResolvedCompositeType(typeExpr, [resolved], state, diagnostics);
        }

        private static void ValidateUnion(
            IReadOnlyList<ICheckedType> members,
            PhpTypeExpressionAst node,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            // Use TypeComparer helpers — named `mixed` often resolves to SimpleCheckedType with a
            // BuiltInTypeSymbol, where `.IsMixed` is false (only SpecialCheckedType sets it).
            // Generic constraints (e.g. `T extends void|mixed`) intentionally allow mixed/never.
            if (!state.IsGenericConstraintPosition
                && members.Any(m => TypeComparer.IsMixedType(m) || TypeComparer.IsNeverType(m)))
            {
                CheckerHelpers.ReportError(diagnostics, state, node, MessageCode.CheckerMixedInComposite);
            }

            if (HasTrueAndFalse(members))
            {
                CheckerHelpers.ReportError(diagnostics, state, node, MessageCode.CheckerUseBoolInsteadOfTrueFalse);
            }

            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    if (CheckedTypes.AreTypesEqual(members[i], members[j]))
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, state, node, MessageCode.CheckerDuplicateTypeInComposite, members[i].DisplayName);
                    }
                }
            }

            if (members.Any(m => CheckerHelpers.IsBuiltInName(m, "bool"))
                && members.Any(m => IsLiteralBool(m) || CheckerHelpers.IsBuiltInName(m, "false")))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, node, MessageCode.CheckerRedundantTypeInUnion, "bool");
            }

            if (members.Any(m => CheckerHelpers.IsBuiltInName(m, "object"))
                && members.Any(m => CheckerHelpers.TryGetObjectDeclaration(m) is not null))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, node, MessageCode.CheckerRedundantTypeInUnion, "object");
            }

            if (members.Any(m => CheckerHelpers.IsBuiltInName(m, "iterable"))
                && members.Any(m => CheckerHelpers.IsBuiltInName(m, "array")))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, node, MessageCode.CheckerRedundantTypeInUnion, "array");
            }
        }

        private static void ValidateIntersection(
            IReadOnlyList<ICheckedType> members,
            PhpTypeExpressionAst node,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (!state.IsGenericConstraintPosition
                && members.Any(m => TypeComparer.IsMixedType(m) || TypeComparer.IsNeverType(m)))
            {
                CheckerHelpers.ReportError(diagnostics, state, node, MessageCode.CheckerMixedInComposite);
            }

            foreach (var member in members)
            {
                if (!IsClassLike(member))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, node, MessageCode.CheckerNonClassInIntersection, member.DisplayName);
                }
            }

            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    if (CheckedTypes.AreTypesEqual(members[i], members[j]))
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, state, node, MessageCode.CheckerDuplicateTypeInComposite, members[i].DisplayName);
                    }
                }
            }
        }

        private static void ValidateBuiltinType(
            PhpBuiltinTypeAst builtin,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            var name = builtin.Identifier.ToLowerInvariant();
            if (name is "resource")
            {
                CheckerHelpers.ReportError(diagnostics, state, builtin, MessageCode.CheckerResourceNotAllowed);
            }

            if (state.IsPropertyTypePosition || state.IsParameterTypePosition)
            {
                if (name is "void")
                {
                    CheckerHelpers.ReportError(diagnostics, state, builtin, MessageCode.CheckerVoidNotAllowedHere);
                }

                if (name is "never")
                {
                    CheckerHelpers.ReportError(diagnostics, state, builtin, MessageCode.CheckerNeverNotAllowedHere);
                }

                if (name is "callable" && state.IsPropertyTypePosition)
                {
                    CheckerHelpers.ReportError(diagnostics, state, builtin, MessageCode.CheckerCallableNotAllowedOnProperty);
                }
            }
        }

        private static void ValidateRestrictedPosition(
            ICheckedType type,
            IBase2Ast node,
            CheckerState state,
            DiagnosticBag diagnostics,
            bool isProperty)
        {
            if (type.Kind == CheckedTypeKind.Void || CheckerHelpers.IsBuiltInName(type, "void"))
            {
                CheckerHelpers.ReportError(diagnostics, state, node, MessageCode.CheckerVoidNotAllowedHere);
            }

            if (type.Kind == CheckedTypeKind.Never || CheckerHelpers.IsBuiltInName(type, "never"))
            {
                CheckerHelpers.ReportError(diagnostics, state, node, MessageCode.CheckerNeverNotAllowedHere);
            }

            if (isProperty && CheckerHelpers.IsBuiltInName(type, "callable"))
            {
                CheckerHelpers.ReportError(diagnostics, state, node, MessageCode.CheckerCallableNotAllowedOnProperty);
            }
        }

        private static bool HasTrueAndFalse(IReadOnlyList<ICheckedType> members) =>
            members.Any(m =>
                m is LiteralCheckedType { Value: true } || CheckerHelpers.IsBuiltInName(m, "true"))
            && members.Any(m =>
                m is LiteralCheckedType { Value: false } || CheckerHelpers.IsBuiltInName(m, "false"));

        private static bool IsLiteralBool(ICheckedType type) =>
            type is LiteralCheckedType { Value: true or false }
            || CheckerHelpers.IsBuiltInName(type, "true")
            || CheckerHelpers.IsBuiltInName(type, "false");

        private static bool IsClassLike(ICheckedType type) =>
            CheckerHelpers.TryGetObjectDeclaration(type) is not null
            || type is IntersectionCheckedType
            || type is CallableCheckedType
            || IsCallableOrClosureType(type)
            || string.Equals(type.DisplayName, "self", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type.DisplayName, "parent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type.DisplayName, "static", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Typed <c>callable</c> / <c>\Closure</c> (including generic forms) may appear in
        /// intersections so optional-arity facets can be written and synthesized as siblings.
        /// </summary>
        private static bool IsCallableOrClosureType(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            return CallableArityFacetBuilder.IsCallableFacetType(type)
                || CallableArityFacetBuilder.IsClosureTypeName(type);
        }
    }
}
