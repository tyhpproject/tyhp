using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Binder.Symbols
{
    /// <summary>
    /// Compile-time <c>#[\Tyhp\EraseGeneric]</c>: identify the attribute on a class, property,
    /// or promoted parameter. Presence opts that slot (or every generic-typed instance property
    /// on the class) out of Mechanism C property tracking.
    /// </summary>
    public static class EraseGenericAttributeSupport
    {
        private const string TyhpEraseGenericAttributeFqn = "\\Tyhp\\EraseGeneric";

        public static bool IsEraseGenericAttribute(PhpAttributeAst attribute)
        {
            if (attribute.Name is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol bound }
                && IsEraseGenericFullyQualifiedName(bound.FullyQualifiedName))
            {
                return true;
            }

            return IsEraseGenericFullyQualifiedName(GetAttributeNameText(attribute.Name));
        }

        public static bool IsEraseGenericFullyQualifiedName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var trimmed = name.Trim();
            var withoutRoot = trimmed.TrimStart('\\');
            return trimmed.Equals(TyhpEraseGenericAttributeFqn, StringComparison.OrdinalIgnoreCase)
                || withoutRoot.Equals("Tyhp\\EraseGeneric", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith("\\Tyhp\\EraseGeneric", StringComparison.OrdinalIgnoreCase)
                || withoutRoot.Equals("EraseGeneric", StringComparison.OrdinalIgnoreCase);
        }

        public static bool HasEraseGeneric(IBase2Ast? host)
        {
            if (host is null)
            {
                return false;
            }

            foreach (var attributeNode in host.AstAttributes)
            {
                if (attributeNode is PhpAttributeAst attribute && IsEraseGenericAttribute(attribute))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsLegalTarget(IBase2Ast target) =>
            target is PhpObjectTypeDeclAst
                or PhpPropertyDeclAst
                or PhpParameterAst;

        public static PhpAttributeAst? FindEraseGenericAttribute(IBase2Ast? host)
        {
            if (host is null)
            {
                return null;
            }

            foreach (var attributeNode in host.AstAttributes)
            {
                if (attributeNode is PhpAttributeAst attribute && IsEraseGenericAttribute(attribute))
                {
                    return attribute;
                }
            }

            return null;
        }

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
