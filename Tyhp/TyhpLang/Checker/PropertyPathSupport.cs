using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Story 16 Phase 1 helpers for <c>\Tyhp\PropertyPath&lt;TSource, TReturn&gt;</c>:
    /// type recognition, arrow-body extraction, and property-chain walking.
    /// </summary>
    internal static class PropertyPathSupport
    {
        public const string PropertyPathSimpleName = "PropertyPath";
        public const string PropertyPathFqn = "Tyhp\\PropertyPath";
        public const string ExpressionSimpleName = "Expression";
        public const string ExpressionFqn = "Tyhp\\Expression";

        public readonly record struct PathSegment(string Name, bool NullSafe);

        /// <summary>
        /// True when <paramref name="symbol"/> is the <c>tyhp/lambda</c> <c>\Tyhp\Expression</c>
        /// class (not a user type also named <c>Expression</c>, and not <c>PropertyPath</c>).
        /// </summary>
        public static bool IsTyhpExpressionDeclaration(IBaseSymbol? symbol)
        {
            if (symbol is not ObjectDeclarationSymbol obj)
            {
                return false;
            }

            var normalized = (obj.FullyQualifiedName ?? obj.Name ?? "").TrimStart('\\');
            if (string.Equals(normalized, ExpressionFqn, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrEmpty(obj.FullyQualifiedName) || !normalized.Contains('\\'))
            {
                return string.Equals(obj.Name, ExpressionSimpleName, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// Last property-chain segment of <c>nameof(fn ($x) => $x->a->b)</c>, following C#'s
        /// last-segment convention. Fails unless the argument is a single-parameter arrow fn
        /// whose body is a simple property / <c>?-&gt;</c> chain.
        /// </summary>
        public static bool TryGetNameofPropertyPathLastSegment(
            PhpInlineFunctionAst closure,
            out string lastSegment)
        {
            lastSegment = "";
            if (!TryGetArrowBodyExpression(closure, out var body))
            {
                return false;
            }

            var paramName = GetSingleArrowParameterName(closure);
            if (paramName is null
                || !TryExtractPropertyChain(body, paramName, out var segments)
                || segments.Count == 0)
            {
                return false;
            }

            lastSegment = segments[^1].Name;
            return true;
        }

        /// <summary>
        /// True when <paramref name="type"/> is (or unwraps to) <c>PropertyPath&lt;…&gt;</c>
        /// from the <c>tyhp/lambda</c> package.
        /// </summary>
        public static bool IsPropertyPathType(ICheckedType? type)
            => TryGetPropertyPathTypeArgs(type, out _, out _);

        /// <summary>
        /// True when <paramref name="type"/> is <c>PropertyPath</c> or <c>Expression</c>
        /// (either carries a compiled <c>$callable</c> for <c>\Closure</c> extraction).
        /// </summary>
        public static bool IsPropertyPathOrExpressionType(ICheckedType? type)
        {
            if (type is null)
            {
                return false;
            }

            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            return IsNamedTyhpLambdaType(type, PropertyPathSimpleName, PropertyPathFqn)
                || IsNamedTyhpLambdaType(type, ExpressionSimpleName, ExpressionFqn);
        }

        public static bool TryGetPropertyPathTypeArgs(
            ICheckedType? type,
            out ICheckedType sourceType,
            out ICheckedType resultType)
        {
            sourceType = CheckedTypes.Mixed;
            resultType = CheckedTypes.Mixed;

            if (type is null)
            {
                return false;
            }

            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (!IsNamedTyhpLambdaType(type, PropertyPathSimpleName, PropertyPathFqn))
            {
                return false;
            }

            if (type is GenericCheckedType { TypeArguments.Count: >= 2 } generic)
            {
                sourceType = generic.TypeArguments[0];
                resultType = generic.TypeArguments[^1];
                return true;
            }

            // Bare PropertyPath without type args — still recognize the type for 4320 reporting.
            return true;
        }

        /// <summary>
        /// Maps <c>PropertyPath&lt;TSource, TReturn&gt;</c> to <c>callable&lt;TSource, TReturn&gt;</c>
        /// for contextual closure typing at call sites.
        /// </summary>
        public static bool TryMapToCallable(ICheckedType type, out CallableCheckedType callable)
        {
            callable = null!;
            if (!TryGetPropertyPathTypeArgs(type, out var source, out var result))
            {
                return false;
            }

            callable = new CallableCheckedType([source], result);
            return true;
        }

        /// <summary>
        /// Extracts the single returned expression from an arrow <c>fn</c>
        /// (<c>return &lt;expr&gt;;</c> wrapper produced by the parser).
        /// </summary>
        public static bool TryGetArrowBodyExpression(PhpInlineFunctionAst closure, out IExpression body)
        {
            body = null!;
            if (!closure.IsArrowFunction || closure.Body is not PhpStatementBlockAst block)
            {
                return false;
            }

            var stmts = block.GetAllNotNull().ToList();
            if (stmts.Count != 1 || stmts[0] is not PhpUnaryOpAst unary)
            {
                return false;
            }

            if (unary.Operator?.ValueInt64 != TyhpParser.T_RETURN
                && !string.Equals(unary.Operator?.ValueString, "return", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (unary.Operand is not IExpression operand)
            {
                return false;
            }

            body = operand;
            return true;
        }

        /// <summary>
        /// Walks <c>$param-&gt;a-&gt;b</c> / <c>$param?-&gt;a?-&gt;b</c> and returns segments in order.
        /// Fails for method calls, dynamic members, non-parameter roots, or empty chains.
        /// </summary>
        public static bool TryExtractPropertyChain(
            IExpression expression,
            string expectedParameterName,
            out IReadOnlyList<PathSegment> segments)
        {
            var collected = new List<PathSegment>();
            IExpression? current = expression;

            while (current is PhpDereferenceableAst deref)
            {
                if (deref.Suffix is PhpCallAst)
                {
                    segments = [];
                    return false;
                }

                if (deref.Suffix is not PhpInstanceMemberAccessAst member)
                {
                    segments = [];
                    return false;
                }

                var name = GetMemberName(member.MemberName);
                if (string.IsNullOrEmpty(name))
                {
                    segments = [];
                    return false;
                }

                var nullSafe = member.Accessor?.ValueInt64 == TyhpParser.T_NULLSAFE_OBJECT_OPERATOR
                    || string.Equals(member.Accessor?.ValueString, "?->", StringComparison.Ordinal);
                collected.Insert(0, new PathSegment(name, nullSafe));
                current = deref.Base as IExpression;
            }

            if (collected.Count == 0
                || current is not PhpVariableAst variable
                || !string.Equals(
                    CheckerHelpers.GetVariableName(variable),
                    expectedParameterName.TrimStart('$'),
                    StringComparison.OrdinalIgnoreCase))
            {
                segments = [];
                return false;
            }

            segments = collected;
            return true;
        }

        public static string? GetSingleArrowParameterName(PhpInlineFunctionAst closure)
        {
            var parameters = closure.Parameters?.GetAllNotNull().ToList() ?? [];
            if (parameters.Count != 1)
            {
                return null;
            }

            return parameters[0].Name?.TrimStart('$');
        }

        public static string DisplayTypeArg(ICheckedType type)
        {
            var name = type.DisplayName;
            var angle = name.IndexOf('<');
            return angle >= 0 ? name[..angle] : name;
        }

        /// <summary>
        /// A bound declaration is authoritative: a user class also named <c>PropertyPath</c> or
        /// <c>Expression</c> must not be mistaken for the <c>tyhp/lambda</c> type. Only unbound
        /// spellings fall back to comparing the display name.
        /// </summary>
        private static bool IsNamedTyhpLambdaType(ICheckedType type, string simpleName, string fqn)
        {
            if (CheckerHelpers.TryGetObjectDeclaration(type) is { } obj)
            {
                if (!string.IsNullOrEmpty(obj.FullyQualifiedName))
                {
                    return IsTyhpLambdaFqn(obj.FullyQualifiedName, simpleName, fqn);
                }

                // Package tyhpdef may bind without a namespace prefix in some load paths.
                return string.Equals(obj.Name, simpleName, StringComparison.OrdinalIgnoreCase);
            }

            return IsTyhpLambdaFqn(type.DisplayName, simpleName, fqn);
        }

        private static bool IsTyhpLambdaFqn(string spelling, string simpleName, string fqn)
        {
            var baseName = spelling;
            var angle = baseName.IndexOf('<');
            if (angle >= 0)
            {
                baseName = baseName[..angle];
            }

            baseName = baseName.TrimStart('\\');
            return string.Equals(baseName, fqn, StringComparison.OrdinalIgnoreCase)
                || string.Equals(baseName, simpleName, StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetMemberName(IExpression? memberName) =>
            memberName switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                PhpScalarAst scalar => scalar.ValueString,
                _ => null,
            };
    }
}
