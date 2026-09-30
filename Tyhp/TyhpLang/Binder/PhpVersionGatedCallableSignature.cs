using System.Text;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder
{
    /// <summary>
    /// Parameter-list digest for TYHP4303. Same-name functions/methods under overlapping
    /// PHP version gates are overloads when this digest differs; identical digests still
    /// conflict. Non-callables return <see langword="null"/> (name-only overlap).
    /// A static method and an instance method of the same name always conflict in PHP,
    /// even when their parameter lists differ — see
    /// <see cref="AreCoexistingOverloads"/>.
    /// </summary>
    internal static class PhpVersionGatedCallableSignature
    {
        public static string? FromDeclaration(IBase2Ast node) => node switch
        {
            PhpFunctionDeclAst function => Spell(function.Parameters),
            TyhpdefImportFunctionDeclAst tyhpdefFunction => Spell(tyhpdefFunction.Parameters),
            PhpMethodDeclAst method => Spell(method.Parameters),
            TyhpdefInlineExtensionFunctionAst inline => inline.Method is { } method
                ? Spell(method.Parameters)
                : null,
            _ => null,
        };

        /// <summary>
        /// Parameter list plus return type, for comparing <c>fallback function</c> signatures.
        /// </summary>
        public static string SpellFunction(IBase2Ast? node)
        {
            if (node is null)
            {
                return "()";
            }

            var parameters = FromDeclaration(node) ?? "()";
            var ret = node switch
            {
                PhpFunctionDeclAst function => SpellType(function.ReturnType),
                TyhpdefImportFunctionDeclAst tyhpdefFunction => SpellType(tyhpdefFunction.ReturnType),
                _ => "",
            };
            return parameters + ":" + ret;
        }

        public static bool AreDistinctOverloads(string? left, string? right)
            => left is not null
                && right is not null
                && !string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when two overlapping gated callables may coexist: distinct parameter-list
        /// shapes and the same static/instance-ness. Functions (no staticness) still
        /// coexist whenever <see cref="AreDistinctOverloads"/> is true.
        /// </summary>
        public static bool AreCoexistingOverloads(
            IBase2Ast leftNode,
            string? leftSignature,
            IBase2Ast rightNode,
            string? rightSignature)
        {
            if (!AreDistinctOverloads(leftSignature, rightSignature))
            {
                return false;
            }

            var leftStatic = MethodStaticness(leftNode);
            var rightStatic = MethodStaticness(rightNode);
            return leftStatic == rightStatic;
        }

        private static bool? MethodStaticness(IBase2Ast node) => node switch
        {
            PhpMethodDeclAst method => method.Modifiers?.Modifiers.Contains(PhpModifier.Static) == true,
            TyhpdefInlineExtensionFunctionAst inline => inline.Method is { } inner
                ? MethodStaticness(inner)
                : true,
            _ => null,
        };

        private static string Spell(PhpParameterListAst? parameters)
        {
            if (parameters is null)
            {
                return "()";
            }

            var parts = parameters.GetAllNotNull().Select(SpellParameter);
            return "(" + string.Join(",", parts) + ")";
        }

        private static string SpellParameter(PhpParameterAst parameter)
        {
            var builder = new StringBuilder();
            if (parameter.IsRef)
            {
                builder.Append('&');
            }

            if (parameter.IsVariadic)
            {
                builder.Append("...");
            }

            var spelledType = SpellType(parameter.Type);
            builder.Append(spelledType.Length > 0 ? spelledType : "_");
            if (parameter.DefaultValue != null)
            {
                builder.Append("=?");
            }

            return builder.ToString();
        }

        private static string SpellType(ITypeExpression? type)
        {
            if (type is null)
            {
                return "";
            }

            if (type is PhpTypeExpressionAst composite)
            {
                var inner = composite.Types?.GetAllNotNull()
                    .Select(SpellType)
                    .Where(static part => part.Length > 0)
                    .ToList() ?? [];
                var joined = inner.Count switch
                {
                    0 => SpellName(composite),
                    1 => inner[0],
                    _ => string.Join(
                        composite.TypeKind == PhpTypeKind.Intersection ? "&" : "|",
                        inner),
                };
                return composite.IsNullable && joined.Length > 0 && joined[0] != '?'
                    ? "?" + joined
                    : joined;
            }

            if (type is PhpNamedTypeAst named)
            {
                var baseName = SpellName(named.Name) is { Length: > 0 } fromName
                    ? fromName
                    : SpellName(named);
                return AppendGenericArguments(baseName, named.Name);
            }

            return AppendGenericArguments(SpellName(type), type);
        }

        private static string AppendGenericArguments(string baseName, IBase2Ast? node)
        {
            if (node is not TyhpGenericIdentifierAst generic
                || generic.GenericArguments is not TyhpGenericsTypeArgumentListAst args)
            {
                return baseName;
            }

            var parts = args.GetAllNotNull()
                .Select(SpellGenericArgument)
                .Where(static part => part.Length > 0)
                .ToList();
            return parts.Count == 0 ? baseName : baseName + "<" + string.Join(",", parts) + ">";
        }

        private static string SpellGenericArgument(TyhpGenericsTypeArgumentAst argument)
        {
            if (argument.TypeConstraint is not null)
            {
                return SpellType(argument.TypeConstraint);
            }

            if (argument.Name is ITypeExpression namedType)
            {
                return SpellType(namedType);
            }

            return SpellName(argument.Name);
        }

        private static string SpellName(IBase2Ast? node)
        {
            if (node is null)
            {
                return "";
            }

            if (!string.IsNullOrEmpty(node.ValueString))
            {
                return node.ValueString.TrimStart('\\');
            }

            if (!string.IsNullOrEmpty(node.Identifier))
            {
                return node.Identifier.TrimStart('\\');
            }

            foreach (var child in node.AstChildren)
            {
                var spelled = SpellName(child);
                if (spelled.Length > 0)
                {
                    return spelled;
                }
            }

            return "";
        }
    }
}
