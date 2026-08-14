using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Helpers for resolving struct shapes from checked types (Story 08.5 Phase 5).
    /// </summary>
    internal static class StructTypeHelper
    {
        public static StructCheckedType? TryGetStructShape(
            ICheckedType type,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (type is StructCheckedType structType)
            {
                return structType;
            }

            if (CheckerHelpers.TryGetObjectDeclaration(type) is not { IsStruct: true } obj)
            {
                return null;
            }

            var shape = BuildFromObjectDeclaration(obj, state, symbolTree, globalScope, resolveType);
            return MaybeSubstitute(type, shape, state, symbolTree, globalScope, resolveType);
        }

        /// <summary>
        /// Materializes a property map for struct, class, or interface types.
        /// </summary>
        public static StructCheckedType? TryGetPropertyShape(
            ICheckedType type,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (type is StructCheckedType structType)
            {
                return structType;
            }

            if (CheckerHelpers.TryGetObjectDeclaration(type) is not { } obj)
            {
                return null;
            }

            var shape = BuildFromObjectDeclaration(obj, state, symbolTree, globalScope, resolveType);
            return MaybeSubstitute(type, shape, state, symbolTree, globalScope, resolveType);
        }

        public static ICheckedType BuildStructKeyUnion(StructCheckedType structType)
        {
            var keys = structType.Properties.Keys
                .Select(key => (ICheckedType)new LiteralCheckedType(
                    key, new SimpleCheckedType(new BuiltInTypeSymbol("string"))))
                .ToList();

            return keys.Count switch
            {
                0 => CheckedTypes.String,
                1 => keys[0],
                _ => CheckedTypes.UnionTypes(keys),
            };
        }

        private static StructCheckedType MaybeSubstitute(
            ICheckedType type,
            StructCheckedType shape,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (type is not GenericCheckedType)
            {
                return shape;
            }

            if (!GenericInheritanceBindings.TryBuild(
                    type, state, symbolTree, globalScope, resolveType, out var bindings))
            {
                return shape;
            }

            return GenericInheritanceBindings.SubstituteShape(shape, bindings, symbolTree, globalScope);
        }

        private static StructCheckedType BuildFromObjectDeclaration(
            ObjectDeclarationSymbol obj,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            var properties = new Dictionary<string, StructPropertyInfo>(StringComparer.Ordinal);
            CollectProperties(obj, state, symbolTree, globalScope, resolveType, properties, new HashSet<ObjectDeclarationSymbol>());
            return new StructCheckedType(properties);
        }

        /// <summary>
        /// Walks the <c>extends</c> chain so a utility type (<c>Readonly&lt;T&gt;</c>, <c>Pick&lt;T,
        /// ...&gt;</c>, <c>keyof T</c>, etc.) applied to a struct/class that inherits from another
        /// struct/class sees the inherited properties too, not just the ones declared directly on
        /// <paramref name="obj"/>. Parent properties are collected first so a same-named override on
        /// the child (rare, but legal) wins.
        ///
        /// <c>extends</c> is parsed as a raw <c>IClassName</c>, not an <see cref="ITypeExpression"/>,
        /// so <see cref="ObjectDeclarationSymbol.ExtendsType"/> is usually null; the parent must be
        /// resolved via <see cref="TypeComparer.TryGetParentDeclaration"/> (same AST-fallback path
        /// <c>TypeComparer.Subtyping</c> uses for nominal subtyping), not the <paramref name="resolveType"/>
        /// callback.
        /// </summary>
        private static void CollectProperties(
            ObjectDeclarationSymbol obj,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            Dictionary<string, StructPropertyInfo> properties,
            HashSet<ObjectDeclarationSymbol> visited)
        {
            if (!visited.Add(obj))
            {
                return;
            }

            if (TypeComparer.TryGetParentDeclaration(obj, symbolTree, globalScope) is { } parent)
            {
                CollectProperties(parent, state, symbolTree, globalScope, resolveType, properties, visited);
            }

            foreach (var member in obj.Members.Values)
            {
                if (member is not ObjectPropertySymbol { DeclaredType: { } declaredType, Visibility: var visibility } property)
                {
                    continue;
                }

                var isReadonly = (visibility & MemberModifier.Readonly) != 0;
                // Resolve each property annotation in the declaring type's file/namespace so
                // short names and `use` imports from that file apply (not the access site's), and
                // in the declaring type's generic scope so its own parameters (`T`) still resolve
                // when the shape is materialized from outside.
                var declaringScope = obj.ContainingScope;
                var needsDeclaringScope = declaringScope is not null
                    && !ReferenceEquals(state.NameResolutionScope, declaringScope);
                var needsOwnGenerics = obj.GenericParameters.Count > 0
                    && !ReferenceEquals(state.EnclosingObject, obj);

                var propState = state;
                if (needsDeclaringScope || needsOwnGenerics)
                {
                    propState = state.Fork();
                    propState.NameResolutionScope = declaringScope ?? state.NameResolutionScope;
                    propState.EnclosingObject = obj;
                    propState.EnclosingFunction = null;
                    if (obj.GenericParameters.Count > 0)
                    {
                        propState.ObjectGenerics = obj.GenericParameters;
                    }
                }

                properties[member.Name] = new StructPropertyInfo(
                    resolveType(declaredType, propState, false, true),
                    isReadonly,
                    TryGetIntegerKeyAlias(property));
            }
        }

        /// <summary>
        /// Integer PHP array key for <c>T 0 as $_1</c>-style struct properties; otherwise null.
        /// </summary>
        internal static int? TryGetIntegerKeyAlias(ObjectPropertySymbol property)
        {
            if (property.DeclaringAstNode is not TyhpStructPropertyAst structProp
                || !structProp.IsNumericAlias
                || structProp.ValueInt64 is not long key
                || key is < int.MinValue or > int.MaxValue)
            {
                return null;
            }

            return (int)key;
        }
    }
}
