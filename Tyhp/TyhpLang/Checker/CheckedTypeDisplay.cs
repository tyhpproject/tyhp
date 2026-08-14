using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Canonical diagnostic spellings for nullable / union <see cref="ICheckedType.DisplayName"/> values.
    /// Display-only: does not change assignability or union construction.
    /// </summary>
    /// <remarks>
    /// Rules:
    /// <list type="bullet">
    /// <item><description><c>?</c> may only wrap a single non-union type (<c>?T</c>); an intersection is
    /// parenthesized (<c>?(A&amp;B)</c>) so it is not misread as <c>(?A)&amp;B</c>.</description></item>
    /// <item><description>A multi-member union that includes null uses explicit <c>|null</c> (never <c>?(A|B)</c>).</description></item>
    /// <item><description>Nested nullability is flattened (<c>?T</c> ≡ <c>T|null</c>); duplicate members are dropped.</description></item>
    /// </list>
    /// </remarks>
    internal static class CheckedTypeDisplay
    {
        public static string FormatUnion(IReadOnlyList<ICheckedType> members)
        {
            var nonNull = new List<ICheckedType>();
            var hasNull = false;
            foreach (var member in members)
            {
                Collect(member, nonNull, ref hasNull);
            }

            return FormatCollected(nonNull, hasNull);
        }

        public static string FormatNullable(ICheckedType innerType)
        {
            var nonNull = new List<ICheckedType>();
            var hasNull = true;
            Collect(innerType, nonNull, ref hasNull);
            return FormatCollected(nonNull, hasNull: true);
        }

        private static void Collect(
            ICheckedType type,
            List<ICheckedType> nonNull,
            ref bool hasNull)
        {
            switch (type)
            {
                case UnionCheckedType union:
                    foreach (var member in union.Members)
                    {
                        Collect(member, nonNull, ref hasNull);
                    }

                    break;

                case NullableCheckedType nullable:
                    hasNull = true;
                    Collect(nullable.InnerType, nonNull, ref hasNull);
                    break;

                case LiteralCheckedType { Value: null }:
                    hasNull = true;
                    break;

                case SimpleCheckedType simple
                    when simple.ResolvedSymbol is BuiltInTypeSymbol builtIn
                         && builtIn.Name.Equals("null", StringComparison.OrdinalIgnoreCase):
                    hasNull = true;
                    break;

                default:
                    if (!nonNull.Any(existing => CheckedTypes.AreTypesEqual(existing, type)))
                    {
                        nonNull.Add(type);
                    }

                    break;
            }
        }

        private static string FormatCollected(List<ICheckedType> nonNull, bool hasNull)
        {
            if (nonNull.Count == 0)
            {
                return hasNull ? "null" : "never";
            }

            if (!hasNull)
            {
                return string.Join("|", nonNull.Select(MemberDisplay));
            }

            // Prefer ?T when the only non-null member is a single non-union type.
            // An intersection needs parens under `?` so `?(A&B)` is not misread as `(?A)&B`.
            if (nonNull.Count == 1 && nonNull[0] is not UnionCheckedType)
            {
                return nonNull[0] is IntersectionCheckedType
                    ? "?(" + MemberDisplay(nonNull[0]) + ")"
                    : "?" + MemberDisplay(nonNull[0]);
            }

            return string.Join("|", nonNull.Select(MemberDisplay)) + "|null";
        }

        /// <summary>
        /// Formats a collected non-null member. Avoids re-entering union/nullable normalization
        /// for the same top-level shape by using leaf DisplayName; nested generics/callables still
        /// normalize through their own DisplayName implementations.
        /// </summary>
        private static string MemberDisplay(ICheckedType type) => type.DisplayName;
    }
}
