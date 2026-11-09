using Tyhp.TyhpLang;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Binder.Symbols
{
    /// <summary>
    /// Compile-time <c>#[\Tyhp\InferGeneric]</c> on a function or method. The declared
    /// name keeps the value signature and a <c>name__tyhpGeneric</c> companion carries
    /// the type arguments. Every type parameter must be reachable from an argument.
    /// </summary>
    public static class InferGenericAttributeSupport
    {
        private const string TyhpInferGenericAttributeFqn = "\\Tyhp\\InferGeneric";

        public static bool IsInferGenericAttribute(PhpAttributeAst attribute)
        {
            if (attribute.Name is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol bound }
                && IsInferGenericFullyQualifiedName(bound.FullyQualifiedName))
            {
                return true;
            }

            return IsInferGenericFullyQualifiedName(GetAttributeNameText(attribute.Name));
        }

        public static bool IsInferGenericFullyQualifiedName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var trimmed = name.Trim();
            var withoutRoot = trimmed.TrimStart('\\');
            return trimmed.Equals(TyhpInferGenericAttributeFqn, StringComparison.OrdinalIgnoreCase)
                || withoutRoot.Equals("Tyhp\\InferGeneric", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith("\\Tyhp\\InferGeneric", StringComparison.OrdinalIgnoreCase)
                || withoutRoot.Equals("InferGeneric", StringComparison.OrdinalIgnoreCase);
        }

        public static bool HasInferGeneric(IBase2Ast? host)
        {
            if (host is null)
            {
                return false;
            }

            foreach (var attributeNode in host.AstAttributes)
            {
                if (attributeNode is PhpAttributeAst attribute && IsInferGenericAttribute(attribute))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasInferGeneric(IBaseSymbol? symbol) =>
            HasInferGeneric(symbol?.DeclaringAstNode);

        /// <summary>
        /// Binder name stored on the plain method. <c>decode</c> becomes
        /// <c>decode__tyhpGeneric</c>; <c>Backer::decode</c> becomes
        /// <c>Backer::decode__tyhpGeneric</c>.
        /// </summary>
        public static string CompanionBinderName(string binderName) =>
            binderName + GeneratedNames.GenericVariantSuffix;

        private static string? GetAttributeNameText(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => !string.IsNullOrEmpty(name.ValueString) ? name.ValueString : name.Identifier,
                TokenValueAst token => !string.IsNullOrEmpty(token.ValueString) ? token.ValueString : token.Identifier,
                IExpression expr => expr.Identifier,
                _ => null,
            };
    }
}
