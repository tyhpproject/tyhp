using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Emitter
{
    public static class EmitHelpers
    {
        public static string GenerateUniqueVarName(EmitContext context, string prefix)
        {
            return context.GenerateUniqueVarName(prefix);
        }

        /// <summary>
        /// Converts a Tyhp type expression to its PHP type-hint string, stripping generics,
        /// expanding type aliases, and mapping Tyhp-only types to their PHP spellings.
        /// Delegates to <see cref="TypeSpellingHelper.Spell"/> so type-hint emission stays
        /// aligned with alias-map collection and alias conversion.
        /// </summary>
        public static string EmitPhpTypeHint(ITypeExpression? tyhpType, EmitContext? context = null)
        {
            var aliasMap = context?.TypeAliasMap;
            return TypeSpellingHelper.Spell(tyhpType, aliasMap, namespacePrefix: context?.Config.NamespacePrefix);
        }

        public static bool IsStructType(IBase2Ast node, EmitContext context)
        {
            if (node.BoundSymbol is ObjectDeclarationSymbol { IsStruct: true })
            {
                return true;
            }

            if (node is IExpression expression)
            {
                return StructEmissionHelper.ResolveStructTypeFromExpression(
                    expression,
                    context.GlobalScope) is not null;
            }

            return false;
        }

        /// <summary>
        /// True when <paramref name="node"/>'s <see cref="IBase2Ast.BoundSymbol"/> resolves to an
        /// extension method call: an <see cref="ObjectMethodSymbol"/> declared on an extension
        /// class, or an <see cref="ObjectOperatorOverloadMethodSymbol"/> with
        /// <see cref="ObjectOperatorOverloadMethodSymbol.IsExtensionOperator"/> set.
        /// </summary>
        public static bool IsExtensionMethodCall(IBase2Ast node, EmitContext context)
        {
            _ = context;
            var symbol = node.BoundSymbol;

            if (symbol is ObjectMethodSymbol method)
            {
                return GetOwningObjectDeclaration(method.ContainingScope)?.IsExtension == true;
            }

            if (symbol is ObjectOperatorOverloadMethodSymbol op)
            {
                return op.IsExtensionOperator;
            }

            return false;
        }

        /// <summary>
        /// Walks up the scope chain from <paramref name="scope"/> to find the nearest
        /// enclosing <see cref="ObjectDeclarationSymbol"/> (the class/trait/extension that owns a member).
        /// </summary>
        private static ObjectDeclarationSymbol? GetOwningObjectDeclaration(IBaseScope? scope)
        {
            for (; scope != null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol objectDecl)
                {
                    return objectDecl;
                }
            }

            return null;
        }
    }
}
