using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Builds a nameless <see cref="StructCheckedType"/> from a type-position
    /// <c>struct { … }</c> / <c>struct extends Parent { … }</c> shape. Named
    /// <c>type Name = struct { … }</c> aliases bind as
    /// <see cref="ObjectDeclarationSymbol"/> and use <see cref="StructTypeHelper"/> instead.
    /// </summary>
    internal static class StructShapeTypeBuilder
    {
        public static StructCheckedType Build(
            TyhpStructShapeAst shape,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, ICheckedType> resolveType)
        {
            var properties = new Dictionary<string, StructPropertyInfo>(StringComparer.Ordinal);

            if (TryGetParentShape(shape.Extends, state, symbolTree, globalScope) is { } parent)
            {
                foreach (var (name, info) in parent.Properties)
                {
                    properties[name] = info;
                }
            }

            foreach (var property in shape.PropertyList?.GetAllNotNull() ?? [])
            {
                var propName = property.Property?.Identifier ?? property.Identifier;
                if (string.IsNullOrEmpty(propName))
                {
                    continue;
                }

                var key = propName.StartsWith('$') ? propName : "$" + propName;
                var declared = property.TypeExpression is null
                    ? CheckedTypes.Unresolved
                    : resolveType(property.TypeExpression);
                properties[key] = new StructPropertyInfo(
                    declared,
                    IntegerKeyAlias: IntegerKeyAlias(property));
            }

            return new StructCheckedType(properties);
        }

        public static bool TryGetProperty(
            ICheckedType type,
            string memberName,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            out ICheckedType propertyType)
        {
            propertyType = CheckedTypes.Unresolved;
            var shape = TypeComparer.TryGetStructShapeForAssignability(type, symbolTree, globalScope);
            if (shape is null)
            {
                return false;
            }

            var key = memberName.StartsWith('$') ? memberName : "$" + memberName;
            if (shape.Properties.TryGetValue(key, out var info)
                || shape.Properties.TryGetValue(memberName, out info))
            {
                propertyType = info.Type;
                return true;
            }

            return false;
        }

        private static int? IntegerKeyAlias(TyhpStructPropertyAst property)
        {
            if (!property.IsNumericAlias
                || property.ValueInt64 is not long key
                || key is < int.MinValue or > int.MaxValue)
            {
                return null;
            }

            return (int)key;
        }

        private static StructCheckedType? TryGetParentShape(
            PhpNameAst? extends,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (extends is null)
            {
                return null;
            }

            var parent = extends.BoundSymbol as ObjectDeclarationSymbol
                ?? ResolveParentByName(extends, state, symbolTree, globalScope);
            if (parent is not { IsStruct: true })
            {
                return null;
            }

            return TypeComparer.TryGetStructShapeForAssignability(
                CheckedTypes.FromSymbol(parent), symbolTree, globalScope);
        }

        private static ObjectDeclarationSymbol? ResolveParentByName(
            PhpNameAst extends,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var name = extends.ValueString ?? extends.Identifier;
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var scope = state.NameResolutionScope
                ?? state.EnclosingObject?.ContainingScope
                ?? (IBaseScope)globalScope;
            return symbolTree.ResolveSymbol(name, scope, new DiagnosticBag()) as ObjectDeclarationSymbol;
        }
    }
}
