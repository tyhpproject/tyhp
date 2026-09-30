using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder
{
    /// <summary>
    /// Object-shape aliases are <see cref="TypeAliasSymbol"/> / <see cref="ObjectTypeAliasSymbol"/>
    /// whose RHS contains a <see cref="TyhpObjectShapeAst"/>. They are types, not classes:
    /// <c>new</c>, <c>::</c>, <c>extends</c>, <c>implements</c>, and <c>::class</c> must not
    /// treat the alias as an <see cref="ObjectDeclarationSymbol"/>. Callable-shape aliases
    /// use the same alias symbols (<see cref="AliasResolvesToCallableShape"/>); struct-shape
    /// aliases bind as named structs instead.
    /// </summary>
    internal static class ObjectShapeSupport
    {
        public static ITypeExpression? GetAliasedType(IBaseSymbol? symbol) =>
            symbol switch
            {
                TypeAliasSymbol fileAlias => fileAlias.AliasedType,
                ObjectTypeAliasSymbol objectAlias => objectAlias.AliasedType,
                _ => null,
            };

        public static TyhpObjectShapeAst? TryGetObjectShape(IBaseSymbol? symbol) =>
            TyhpObjectShapeAst.Find(GetAliasedType(symbol));

        public static bool IsObjectShapeAlias([NotNullWhen(true)] IBaseSymbol? symbol) =>
            TryGetObjectShape(symbol) is not null;

        public static bool TryResolveObjectShapeAlias(
            IBase2Ast? nameNode,
            IBaseScope fromScope,
            SymbolTree symbolTree,
            out IBaseSymbol alias)
        {
            alias = null!;
            if (nameNode is null)
            {
                return false;
            }

            if (IsObjectShapeAlias(nameNode.BoundSymbol))
            {
                alias = nameNode.BoundSymbol!;
                return true;
            }

            var symbol = ResolveTypePositionSymbol(nameNode, fromScope, symbolTree);
            if (!IsObjectShapeAlias(symbol))
            {
                return false;
            }

            alias = symbol!;
            return true;
        }

        /// <summary>
        /// Resolves a written type name to a file-level or class-level <c>type</c> alias.
        /// Used by <c>new</c> so an alias is never treated as a class, even when its RHS
        /// expands to one.
        /// </summary>
        public static bool TryResolveTypeAlias(
            IBase2Ast? nameNode,
            IBaseScope fromScope,
            SymbolTree symbolTree,
            out IBaseSymbol alias)
        {
            alias = null!;
            if (nameNode is null)
            {
                return false;
            }

            if (nameNode.BoundSymbol is TypeAliasSymbol or ObjectTypeAliasSymbol)
            {
                alias = nameNode.BoundSymbol!;
                return true;
            }

            var symbol = ResolveTypePositionSymbol(nameNode, fromScope, symbolTree);
            if (symbol is not TypeAliasSymbol and not ObjectTypeAliasSymbol)
            {
                return false;
            }

            alias = symbol;
            return true;
        }

        /// <summary>
        /// Whether <paramref name="alias"/>'s RHS is an object shape, including a bare rename
        /// (<c>type Foo = Box;</c>) or generic instantiation (<c>type Foo = Box&lt;int&gt;</c>)
        /// of another shape alias.
        /// </summary>
        public static bool AliasResolvesToObjectShape(IBaseSymbol? alias) =>
            AliasResolvesToObjectShape(alias, new HashSet<IBaseSymbol>());

        /// <summary>
        /// Whether <paramref name="alias"/>'s RHS is a <c>callable(…): R</c> shape, including a
        /// bare rename or generic instantiation of another callable-shape alias. Unions that
        /// merely mention a shape are not themselves callable shapes.
        /// </summary>
        public static bool AliasResolvesToCallableShape(IBaseSymbol? alias) =>
            AliasResolvesToCallableShape(alias, new HashSet<IBaseSymbol>());

        private static bool AliasResolvesToObjectShape(IBaseSymbol? alias, HashSet<IBaseSymbol> seen)
        {
            if (alias is null || !seen.Add(alias))
            {
                return false;
            }

            return TypeExpressionResolvesToObjectShape(GetAliasedType(alias), seen);
        }

        private static bool AliasResolvesToCallableShape(IBaseSymbol? alias, HashSet<IBaseSymbol> seen)
        {
            if (alias is null || !seen.Add(alias))
            {
                return false;
            }

            return TypeExpressionResolvesToCallableShape(GetAliasedType(alias), seen);
        }

        private static bool TypeExpressionResolvesToObjectShape(ITypeExpression? type, HashSet<IBaseSymbol> seen)
        {
            if (type is null)
            {
                return false;
            }

            if (TyhpObjectShapeAst.Find(type) is not null)
            {
                return true;
            }

            if (type is PhpTypeExpressionAst { Types: { } items })
            {
                foreach (var child in items.GetAllNotNull())
                {
                    if (TypeExpressionResolvesToObjectShape(child, seen))
                    {
                        return true;
                    }
                }

                return false;
            }

            var bound = type.BoundSymbol
                ?? (type as PhpNamedTypeAst)?.Name?.BoundSymbol;
            if (bound is TypeAliasSymbol or ObjectTypeAliasSymbol)
            {
                return AliasResolvesToObjectShape(bound, seen);
            }

            return false;
        }

        private static bool TypeExpressionResolvesToCallableShape(ITypeExpression? type, HashSet<IBaseSymbol> seen)
        {
            if (type is null)
            {
                return false;
            }

            if (TyhpCallableShapeAst.Find(type) is not null)
            {
                return true;
            }

            if (type is PhpTypeExpressionAst { TypeKind: PhpTypeKind.Simple, Types: { } items })
            {
                foreach (var child in items.GetAllNotNull())
                {
                    if (TypeExpressionResolvesToCallableShape(child, seen))
                    {
                        return true;
                    }
                }

                return false;
            }

            var bound = type.BoundSymbol
                ?? (type as PhpNamedTypeAst)?.Name?.BoundSymbol;
            if (bound is TypeAliasSymbol or ObjectTypeAliasSymbol)
            {
                return AliasResolvesToCallableShape(bound, seen);
            }

            return false;
        }

        private static IBaseSymbol? ResolveTypePositionSymbol(
            IBase2Ast nameNode,
            IBaseScope fromScope,
            SymbolTree symbolTree)
        {
            var written = GetWrittenTypeName(nameNode);
            if (string.IsNullOrEmpty(written))
            {
                return null;
            }

            var raw = written.TrimStart('\\');
            var isFullyQualified = written.StartsWith('\\');
            var segments = raw.Split('\\');
            var resolver = new Resolution.NameResolver(symbolTree, new Domain.Diagnostics.DiagnosticBag());

            if (isFullyQualified)
            {
                return resolver.ResolveQualifiedName(segments);
            }

            if (segments.Length > 1)
            {
                return resolver.ResolveRelativeName(segments, fromScope)
                    ?? resolver.ResolveQualifiedName(segments);
            }

            return resolver.ResolveSymbol(raw, fromScope, Resolution.NameResolver.IsTypePositionSymbol)
                ?? resolver.ResolveRelativeName(segments, fromScope);
        }

        public static string? GetWrittenTypeName(IBase2Ast nameNode) =>
            nameNode switch
            {
                PhpNameAst name => name.ValueString ?? name.Identifier,
                PhpNamedTypeAst named => named.Name switch
                {
                    PhpNameAst inner => inner.ValueString ?? inner.Identifier,
                    TokenValueAst token => token.ValueString,
                    _ => named.Identifier,
                },
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                TokenValueAst token => token.ValueString,
                _ => string.IsNullOrEmpty(nameNode.Identifier) ? nameNode.ValueString : nameNode.Identifier,
            };
    }
}
