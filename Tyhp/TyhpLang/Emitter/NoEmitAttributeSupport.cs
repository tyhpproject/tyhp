using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Compile-time <c>#[\Tyhp\NoEmit]</c>: omit tagged type declarations from PHP output, and
    /// strip usages of those types as attributes. Identification of the marker itself is by
    /// bound FQCN or written name; whether some other attribute usage is omitted is decided by
    /// resolving that attribute's class and checking its own declared attributes for the marker.
    /// PHP attributes are not inherited, so only the class's immediate attribute list is read.
    /// </summary>
    internal static class NoEmitAttributeSupport
    {
        private const string TyhpNoEmitAttributeFqn = "\\Tyhp\\NoEmit";

        /// <summary>
        /// True when <paramref name="node"/> itself carries <c>#[\Tyhp\NoEmit]</c> (the whole
        /// class / interface / trait / enum / extension must not appear in emitted PHP).
        /// </summary>
        public static bool ShouldOmitDeclaration(IBase2Ast node)
        {
            foreach (var attributeNode in node.AstAttributes)
            {
                if (attributeNode is PhpAttributeAst attribute && IsNoEmitMarker(attribute))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when this attribute usage must not appear in emitted PHP: either it <em>is</em>
        /// <c>#[\Tyhp\NoEmit]</c>, or its resolved class is tagged with that marker.
        /// </summary>
        public static bool ShouldOmitAttributeUsage(PhpAttributeAst attribute)
        {
            if (IsNoEmitMarker(attribute))
            {
                return true;
            }

            return attribute.Name is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol objectSymbol }
                && IsClassTaggedWithNoEmit(objectSymbol);
        }

        public static bool IsClassTaggedWithNoEmit(ObjectDeclarationSymbol symbol)
            => symbol.DeclaringAstNode is { } declaringNode && ShouldOmitDeclaration(declaringNode);

        /// <summary>
        /// The <c>#[\Tyhp\NoEmit]</c> marker itself — bound FQCN or written name, matching
        /// binder-style recognition. Not recursive: does not ask whether <c>NoEmit</c>'s own
        /// <c>NoEmit</c> usage is tagged.
        /// </summary>
        public static bool IsNoEmitMarker(PhpAttributeAst attribute)
        {
            if (attribute.Name is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol objectSymbol }
                && IsNoEmitFullyQualifiedName(objectSymbol.FullyQualifiedName))
            {
                return true;
            }

            return IsNoEmitFullyQualifiedName(GetAttributeNameText(attribute.Name));
        }

        public static bool IsNoEmitFullyQualifiedName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var trimmed = name.Trim();
            return trimmed.Equals(TyhpNoEmitAttributeFqn, StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Tyhp\\NoEmit", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith("\\Tyhp\\NoEmit", StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetAttributeNameText(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                IExpression expr => expr.Identifier,
                _ => null,
            };
    }
}
