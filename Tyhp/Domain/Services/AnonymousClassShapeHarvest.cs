using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Harvests <c>return new class { … }</c> into overlay-friendly object-shape aliases.
    /// Does not emit a PHP class for <c>anonClass@</c> identifiers.
    /// </summary>
    internal static class AnonymousClassShapeHarvest
    {
        public const string AnonClassPrefix = "anonClass@";

        public static bool IsAnonymousClass(PhpObjectTypeDeclAst? decl)
            => decl is not null
            && (decl.IsAnonymousClass
                || IsAnonymousClassName(decl.Identifier));

        public static bool IsAnonymousClassName(string? name)
            => !string.IsNullOrEmpty(name)
            && name.StartsWith(AnonClassPrefix, StringComparison.Ordinal);

        public static bool IsWeakReturn(string? type)
        {
            var trimmed = (type ?? "").Trim();
            return trimmed.Length == 0
                || trimmed.Equals("mixed", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("object", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsPublicInstanceMethod(TyhpdefMethod method)
        {
            if (method.Modifiers.Any(m => m.Equals("static", StringComparison.OrdinalIgnoreCase)
                || m.Equals("protected", StringComparison.OrdinalIgnoreCase)
                || m.Equals("private", StringComparison.OrdinalIgnoreCase)
                || m.Equals("abstract", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return true;
        }

        public static bool IsPublicInstanceProperty(TyhpdefProperty property)
        {
            if (property.Modifiers.Any(m => m.Equals("static", StringComparison.OrdinalIgnoreCase)
                || m.Equals("protected", StringComparison.OrdinalIgnoreCase)
                || m.Equals("private", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return true;
        }

        public static bool IsPublicConstant(TyhpdefConstant constant)
        {
            if (constant.Modifiers.Any(m => m.Equals("protected", StringComparison.OrdinalIgnoreCase)
                || m.Equals("private", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return true;
        }

        public static TyhpdefObjectShape FilterShapeMembers(
            IEnumerable<TyhpdefMethod>? methods,
            IEnumerable<TyhpdefProperty>? properties,
            IEnumerable<TyhpdefConstant>? constants)
        {
            var shapeMethods = new List<TyhpdefMethod>();
            foreach (var method in methods ?? [])
            {
                if (!IsPublicInstanceMethod(method) || string.IsNullOrWhiteSpace(method.Name))
                {
                    continue;
                }

                shapeMethods.Add(method with
                {
                    Modifiers = ShapeMethodModifiers(method),
                    DocComment = null,
                    Attributes = [],
                    Overloads = [],
                });
            }

            var shapeProperties = new List<TyhpdefProperty>();
            foreach (var property in properties ?? [])
            {
                if (!IsPublicInstanceProperty(property) || string.IsNullOrWhiteSpace(property.Name))
                {
                    continue;
                }

                shapeProperties.Add(property with
                {
                    Modifiers = ShapePropertyModifiers(property),
                    DocComment = null,
                    Attributes = [],
                });
            }

            var shapeConstants = new List<TyhpdefConstant>();
            foreach (var constant in constants ?? [])
            {
                if (!IsPublicConstant(constant) || string.IsNullOrWhiteSpace(constant.Name))
                {
                    continue;
                }

                shapeConstants.Add(constant with
                {
                    Modifiers = ["public"],
                    DocComment = null,
                    Attributes = [],
                });
            }

            return new TyhpdefObjectShape
            {
                Methods = shapeMethods,
                Properties = shapeProperties,
                Constants = shapeConstants,
            };
        }

        public static List<string> UniqueNonEmpty(IEnumerable<string> types)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var type in types)
            {
                var trimmed = (type ?? "").Trim();
                if (trimmed.Length == 0 || IsWeakReturn(trimmed) || !seen.Add(trimmed))
                {
                    continue;
                }

                result.Add(trimmed);
            }

            return result;
        }

        public static string UnionTypes(IEnumerable<string> types)
        {
            var unique = UniqueNonEmpty(types);
            return unique.Count == 0 ? "" : string.Join("|", unique);
        }

        public static string AllocateAliasName(
            string? ownerTypeName,
            string callableName,
            ISet<string> usedNames)
        {
            var owner = SanitizeIdentifier(ShortName(ownerTypeName));
            var callable = SanitizeIdentifier(callableName);
            if (callable.Length == 0)
            {
                callable = "anon";
            }

            var stem = owner.Length == 0
                ? callable + "_Return"
                : owner + "_" + callable + "_Return";
            if (TyhpdefOutputWriter.IsTyhpdefKeywordTypeName(stem))
            {
                stem += "_Shape";
            }

            var candidate = stem;
            var suffix = 2;
            while (!usedNames.Add(candidate))
            {
                candidate = stem + "_" + suffix;
                suffix++;
            }

            return candidate;
        }

        public static string ShortName(string? name)
        {
            var trimmed = (name ?? "").Trim().TrimStart('\\');
            if (trimmed.Length == 0)
            {
                return "";
            }

            var slash = trimmed.LastIndexOf('\\');
            return slash < 0 ? trimmed : trimmed[(slash + 1)..];
        }

        public static ReturnNews CollectReturnNews(IBase2Ast? body)
        {
            var anonymous = new List<PhpObjectTypeDeclAst>();
            var named = new List<IClassNameReference>();
            var hasOther = false;
            var hasUnknownOther = false;
            CollectReturns(body, anonymous, named, ref hasOther, ref hasUnknownOther);
            return new ReturnNews(anonymous, named, hasOther, hasUnknownOther);
        }

        public static string? ApplyToReturnType(
            string existingReturn,
            IBase2Ast? body,
            string? ownerTypeName,
            string callableName,
            ISet<string> usedNames,
            Action<TyhpdefTypeAlias> addAlias,
            Func<PhpObjectTypeDeclAst, HarvestedAnonymousClass> mapAnonymous,
            Func<IClassNameReference, string> spellNamed)
        {
            var news = CollectReturnNews(body);
            if (news.Anonymous.Count == 0)
            {
                return null;
            }

            var onlyAnonymousReturns = !news.HasOther && news.Named.Count == 0;
            if (onlyAnonymousReturns && !IsWeakReturn(existingReturn))
            {
                return null;
            }

            if (news.HasUnknownOther)
            {
                // A return we cannot spell (not `new`, not null-coalesce/ternary/match
                // transparent, not a bare `null`) shares this callable with an anonymous
                // class return. Replacing the existing (weak) return with just the
                // harvested shape union would silently drop that arm and unsoundly
                // narrow the signature. Leave the return type untouched.
                return null;
            }

            var shapeNames = new List<string>();
            foreach (var anonymous in news.Anonymous)
            {
                var harvested = mapAnonymous(anonymous);
                if (!harvested.Shape.HasMembers)
                {
                    foreach (var parent in harvested.Parents)
                    {
                        shapeNames.Add(parent);
                    }

                    continue;
                }

                var aliasName = AllocateAliasName(ownerTypeName, callableName, usedNames);
                addAlias(new TyhpdefTypeAlias
                {
                    Name = aliasName,
                    IntersectionTypes = UniqueNonEmpty(harvested.Parents),
                    ObjectShape = harvested.Shape,
                });
                shapeNames.Add(aliasName);
            }

            if (shapeNames.Count == 0)
            {
                return null;
            }

            var arms = new List<string>();
            if (!IsWeakReturn(existingReturn))
            {
                arms.Add(existingReturn);
            }
            else
            {
                foreach (var named in news.Named)
                {
                    var spelled = spellNamed(named);
                    if (!string.IsNullOrWhiteSpace(spelled))
                    {
                        arms.Add(spelled);
                    }
                }
            }

            arms.AddRange(shapeNames);
            var union = UnionTypes(arms);
            return union.Length == 0 ? null : union;
        }

        private static void CollectReturns(
            IBase2Ast? node,
            List<PhpObjectTypeDeclAst> anonymous,
            List<IClassNameReference> named,
            ref bool hasOther,
            ref bool hasUnknownOther)
        {
            if (node is null
                || node is PhpFunctionDeclAst
                || node is PhpMethodDeclAst
                || node is PhpInlineFunctionAst
                || node is PhpObjectTypeDeclAst)
            {
                return;
            }

            if (node is PhpJumpStatementAst { JumpType: PhpJumpType.Return } jump)
            {
                CollectFromReturnedExpression(jump.Expression, anonymous, named, ref hasOther, ref hasUnknownOther);
                return;
            }

            if (node is PhpReturnStatementAst ret)
            {
                CollectFromReturnedExpression(ret.Expression, anonymous, named, ref hasOther, ref hasUnknownOther);
                return;
            }

            if (IsReturnUnary(node))
            {
                CollectFromReturnedExpression(((PhpUnaryOpAst)node).Operand, anonymous, named, ref hasOther, ref hasUnknownOther);
                return;
            }

            foreach (var child in node.AstChildren)
            {
                CollectReturns(child, anonymous, named, ref hasOther, ref hasUnknownOther);
            }
        }

        private static bool IsReturnUnary(IBase2Ast node)
        {
            if (node is not PhpUnaryOpAst unary)
            {
                return false;
            }

            var op = unary.Operator?.ValueString ?? unary.Operator?.Identifier ?? "";
            return op.Equals("return", StringComparison.OrdinalIgnoreCase);
        }

        private static void CollectFromReturnedExpression(
            IExpression? expression,
            List<PhpObjectTypeDeclAst> anonymous,
            List<IClassNameReference> named,
            ref bool hasOther,
            ref bool hasUnknownOther)
        {
            switch (expression)
            {
                case PhpNewAst { AnonymousClass: { } anon }:
                    anonymous.Add(anon);
                    return;
                case PhpNewAst { ClassName: { } className }:
                    named.Add(className);
                    hasOther = true;
                    return;
                case PhpTernaryOpAst ternary:
                    CollectFromReturnedExpression(ternary.TrueExpr ?? ternary.Condition, anonymous, named, ref hasOther, ref hasUnknownOther);
                    CollectFromReturnedExpression(ternary.FalseExpr, anonymous, named, ref hasOther, ref hasUnknownOther);
                    return;
                case PhpBinaryOpAst binary when IsNullCoalesce(binary):
                    CollectFromReturnedExpression(binary.Left, anonymous, named, ref hasOther, ref hasUnknownOther);
                    CollectFromReturnedExpression(binary.Right, anonymous, named, ref hasOther, ref hasUnknownOther);
                    return;
                case PhpConditionalAst { IsMatchSyntax: true } match:
                    foreach (var arm in match.Arms?.GetAllNotNull() ?? [])
                    {
                        CollectReturns(arm.Body, anonymous, named, ref hasOther, ref hasUnknownOther);
                    }

                    return;
                case null:
                    return;
                default:
                    // Anything else (`null`, a variable, a call, a scalar, …) cannot be
                    // spelled as a tyhpdef type here. Record it separately from `named`
                    // so the caller can decline to narrow the return instead of quietly
                    // dropping this arm.
                    hasOther = true;
                    hasUnknownOther = true;
                    return;
            }
        }

        private static bool IsNullCoalesce(PhpBinaryOpAst binary)
        {
            var op = binary.Operator?.ValueString ?? binary.Operator?.Identifier ?? "";
            return op == "??";
        }

        private static List<string> ShapeMethodModifiers(TyhpdefMethod method)
        {
            var result = new List<string> { "public" };
            if (method.Modifiers.Any(m => m.Equals("readonly", StringComparison.OrdinalIgnoreCase)))
            {
                result.Add("readonly");
            }

            return result;
        }

        private static List<string> ShapePropertyModifiers(TyhpdefProperty property)
        {
            var result = new List<string> { "public" };
            if (property.Modifiers.Any(m => m.Equals("readonly", StringComparison.OrdinalIgnoreCase)))
            {
                result.Add("readonly");
            }

            return result;
        }

        private static string SanitizeIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name) || IsAnonymousClassName(name))
            {
                return "";
            }

            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (!char.IsAsciiLetterOrDigit(chars[i]) && chars[i] != '_')
                {
                    chars[i] = '_';
                }
            }

            var sanitized = new string(chars).Trim('_');
            if (sanitized.Length == 0)
            {
                return "";
            }

            if (char.IsAsciiDigit(sanitized[0]))
            {
                sanitized = "_" + sanitized;
            }

            return sanitized;
        }

        internal readonly record struct ReturnNews(
            IReadOnlyList<PhpObjectTypeDeclAst> Anonymous,
            IReadOnlyList<IClassNameReference> Named,
            bool HasOther,
            bool HasUnknownOther);

        internal readonly record struct HarvestedAnonymousClass(
            TyhpdefObjectShape Shape,
            IReadOnlyList<string> Parents);
    }
}
