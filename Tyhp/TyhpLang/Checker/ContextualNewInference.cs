using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Contextual generic type-argument inference for bare <c>new Generic()</c>
    /// (no explicit <c>&lt;…&gt;</c> list). The expected type of <em>this</em>
    /// expression — not the enclosing function's return type applied to every
    /// <c>new</c> in the body — supplies the arguments when it is a unique
    /// instantiation of the same generic declaration.
    /// </summary>
    internal static class ContextualNewInference
    {
        public static bool IsUsableExpectedType(ICheckedType? type) =>
            type is not null
            && !TypeComparer.IsUnresolvedType(type)
            && !TypeComparer.IsMixedType(type)
            && !TypeComparer.IsVoidType(type)
            && !TypeComparer.IsNeverType(type);

        /// <summary>
        /// Bare <c>new Foo()</c> / <c>new Foo() with […]</c> (no explicit type-argument
        /// list). Call-argument checking skips these until the parameter type is known
        /// so <see cref="CheckerState.ExpectedExpressionType"/> can specialize them.
        /// </summary>
        public static bool IsBareGenericConstruction(IBase2Ast? expr)
        {
            while (expr is PhpDereferenceableExpressionAst wrapped)
            {
                expr = wrapped.Expression;
            }

            if (expr is PhpNewAst newExpr)
            {
                return !HasExplicitTypeArguments(newExpr);
            }

            if (expr is PhpBinaryOpAst binary && IsWithOperator(binary.Operator))
            {
                return IsBareGenericConstruction(binary.Left);
            }

            return false;
        }

        public static bool HasExplicitTypeArguments(PhpNewAst newExpr)
        {
            if (newExpr.ClassName is not PhpNameAst className)
            {
                return false;
            }

            if (className.AstGrammarAddons.TryGetValue("identifier", out var addon)
                && addon is PhpTypeExpressionListAst addonList
                && addonList.GetAllNotNull().Any())
            {
                return true;
            }

            return className is TyhpGenericIdentifierAst { GenericArguments: PhpTypeExpressionListAst genericArgs }
                && genericArgs.GetAllNotNull().Any();
        }

        public static bool TryGetTypeArguments(
            ICheckedType? expected,
            IBaseSymbol constructed,
            out IReadOnlyList<ICheckedType> typeArguments)
        {
            typeArguments = [];
            if (!IsUsableExpectedType(expected)
                || GetGenericParameters(constructed).Count == 0)
            {
                return false;
            }

            return TryGetTypeArgumentsCore(expected!, constructed, out typeArguments);
        }

        /// <summary>
        /// Re-infer a cached <c>new</c> when a later expected type uniquely instantiates
        /// it and the cache still holds the unbound / defaulted form.
        /// </summary>
        public static bool ShouldRespecialize(
            ICheckedType cached,
            ICheckedType? expected,
            IBaseSymbol? constructed)
        {
            if (constructed is null
                || !TryGetTypeArguments(expected, constructed, out var args)
                || args.Count == 0)
            {
                return false;
            }

            if (cached is GenericCheckedType generic
                && TypeComparer.SymbolsMatch(
                    TypeComparer.TryGetNominalSymbol(generic.BaseType), constructed)
                && generic.TypeArguments.Count == args.Count
                && TypeArgumentsEqual(generic.TypeArguments, args))
            {
                return false;
            }

            return true;
        }

        public static IReadOnlyList<GenericTypeParameterSymbol> GetGenericParameters(IBaseSymbol symbol) =>
            symbol switch
            {
                ObjectDeclarationSymbol obj => obj.GenericParameters,
                TypeAliasSymbol alias => alias.GenericParameters,
                ObjectTypeAliasSymbol objectAlias => objectAlias.GenericParameters,
                _ => [],
            };

        public static IBaseSymbol? TryGetConstructedSymbol(PhpNewAst newExpr)
        {
            if (newExpr.ClassName?.BoundSymbol is { } bound
                && GetGenericParameters(bound).Count > 0)
            {
                return bound;
            }

            return newExpr.ClassName?.BoundSymbol as ObjectDeclarationSymbol;
        }

        private static bool TryGetTypeArgumentsCore(
            ICheckedType expected,
            IBaseSymbol constructed,
            out IReadOnlyList<ICheckedType> typeArguments)
        {
            typeArguments = [];
            expected = StripNullability(expected);

            if (expected is UnionCheckedType union)
            {
                IReadOnlyList<ICheckedType>? agreed = null;
                var sawMember = false;
                foreach (var member in union.Members)
                {
                    if (IsNullLike(member))
                    {
                        continue;
                    }

                    if (!TryGetTypeArgumentsCore(member, constructed, out var memberArgs))
                    {
                        return false;
                    }

                    if (!sawMember)
                    {
                        agreed = memberArgs;
                        sawMember = true;
                    }
                    else if (agreed is null || !TypeArgumentsEqual(agreed, memberArgs))
                    {
                        return false;
                    }
                }

                if (!sawMember || agreed is null)
                {
                    return false;
                }

                typeArguments = agreed;
                return true;
            }

            if (expected is not GenericCheckedType generic || generic.TypeArguments.Count == 0)
            {
                return false;
            }

            var origin = TypeComparer.TryGetNominalSymbol(generic.BaseType);
            if (!TypeComparer.SymbolsMatch(origin, constructed))
            {
                return false;
            }

            var parameters = GetGenericParameters(constructed);
            if (parameters.Count == 0 || generic.TypeArguments.Count != parameters.Count)
            {
                return false;
            }

            typeArguments = generic.TypeArguments;
            return true;
        }

        private static ICheckedType StripNullability(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            return type;
        }

        private static bool IsNullLike(ICheckedType type) =>
            TypeComparer.IsNullLiteral(type) || TypeComparer.IsBuiltInName(type, "null");

        private static bool TypeArgumentsEqual(
            IReadOnlyList<ICheckedType> left,
            IReadOnlyList<ICheckedType> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (var i = 0; i < left.Count; i++)
            {
                if (!TypeComparer.AreTypesEqual(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsWithOperator(TokenValueAst? op)
        {
            if (op is null)
            {
                return false;
            }

            if (op.ValueInt64 is long value && (int)value == TyhpParser.T_TYHP_WITH)
            {
                return true;
            }

            return string.Equals(op.ValueString, "with", StringComparison.OrdinalIgnoreCase)
                || string.Equals(op.Identifier, "with", StringComparison.OrdinalIgnoreCase);
        }
    }
}
