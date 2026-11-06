using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Which classes <c>lazy new</c> may construct, and which of those PHP can actually keep lazy.
    /// An internal class other than <c>\stdClass</c> cannot be a lazy ghost. A class with no stored
    /// instance property makes <c>newLazyGhost</c> return a normal instance and never call the
    /// initializer (PHP 8.5: the constructor would not run at all), so that expression is emitted
    /// as an ordinary <c>new</c>.
    /// </summary>
    internal static class LazyNewSupport
    {
        internal static string DisplayName(ObjectDeclarationSymbol symbol)
        {
            var name = string.IsNullOrEmpty(symbol.FullyQualifiedName)
                ? symbol.Name
                : symbol.FullyQualifiedName;
            return name.StartsWith('\\') ? name : "\\" + name;
        }

        /// <summary>
        /// True when <c>lazy new</c> of <paramref name="symbol"/> is rejected. Structs, interfaces,
        /// enums, traits, and internal PHP classes other than <c>\stdClass</c> (including a user
        /// class that extends one) cannot stay lazy.
        /// </summary>
        internal static bool IsDisallowedLazyTarget(
            ObjectDeclarationSymbol symbol,
            SymbolTree tree,
            GlobalScope scope)
        {
            if (symbol.IsStruct || symbol.IsExtension)
            {
                return true;
            }

            if (symbol.ObjectKind is not PhpTypeDeclType.Class)
            {
                return true;
            }

            var seen = new HashSet<ObjectDeclarationSymbol>();
            for (var current = symbol;
                 current is not null && seen.Add(current);
                 current = TypeComparer.TryGetParentDeclaration(current, tree, scope))
            {
                if (IsEngineDeclaredClass(current) && !IsStdClass(current))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when the class, an ancestor, or a used trait is a tracked generic class (Mechanism C)
        /// and therefore carries the injected <c>$__tyhpGeneric</c> bag. The bag is a real stored
        /// instance property that no author symbol declares, so it is not found by
        /// <see cref="HasLazyCapableInstanceProperty"/>. <paramref name="isGenericTracked"/> answers
        /// whether a class was flagged for runtime generic tracking; the checker only knows that
        /// after the whole walk, the emitter knows it throughout.
        /// </summary>
        internal static bool InheritsGenericBag(
            ObjectDeclarationSymbol symbol,
            SymbolTree tree,
            GlobalScope scope,
            Func<ObjectDeclarationSymbol, bool> isGenericTracked)
        {
            var seen = new HashSet<ObjectDeclarationSymbol>();
            for (var current = symbol;
                 current is not null && seen.Add(current);
                 current = TypeComparer.TryGetParentDeclaration(current, tree, scope))
            {
                if (current.ObjectKind is PhpTypeDeclType.Class
                    && current.GenericParameters.Count > 0
                    && isGenericTracked(current))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when the class or an ancestor declares generic parameters, so it may turn out to be
        /// tracked and carry the generic bag. The upper bound the checker uses before tracking is
        /// final.
        /// </summary>
        internal static bool MightInheritGenericBag(
            ObjectDeclarationSymbol symbol,
            SymbolTree tree,
            GlobalScope scope) =>
            InheritsGenericBag(symbol, tree, scope, static _ => true);

        /// <summary>
        /// True when a ghost of <paramref name="symbol"/> can stay lazy: an author-declared stored
        /// instance property, or the injected generic bag.
        /// </summary>
        internal static bool HasStoredInstanceState(
            ObjectDeclarationSymbol symbol,
            SymbolTree tree,
            GlobalScope scope,
            Func<ObjectDeclarationSymbol, bool> isGenericTracked) =>
            HasLazyCapableInstanceProperty(symbol, tree, scope)
            || InheritsGenericBag(symbol, tree, scope, isGenericTracked);

        /// <summary>
        /// True when the class, an ancestor, or a used trait declares stored instance state.
        /// Static properties and virtual hook-only properties do not count. The injected generic
        /// bag is not on the author symbol; see <see cref="InheritsGenericBag"/>.
        /// </summary>
        internal static bool HasLazyCapableInstanceProperty(
            ObjectDeclarationSymbol symbol,
            SymbolTree tree,
            GlobalScope scope)
        {
            var seen = new HashSet<ObjectDeclarationSymbol>();
            for (var current = symbol;
                 current is not null && seen.Add(current);
                 current = TypeComparer.TryGetParentDeclaration(current, tree, scope))
            {
                if (DeclarationHasLazyCapableProperty(current, tree, scope))
                {
                    return true;
                }

                foreach (var trait in TypeComparer.ResolveUsedTraits(current, tree, scope, out _))
                {
                    if (DeclarationHasLazyCapableProperty(trait, tree, scope))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static bool IsEngineDeclaredClass(ObjectDeclarationSymbol symbol)
        {
            var path = (symbol.SourceFile ?? string.Empty).Replace('\\', '/');
            if (path.Length == 0)
            {
                return false;
            }

            if (RuntimePackagePaths.IsEnginePhpPackageSource(path))
            {
                return true;
            }

            return path.Contains("packages/tyhpdef/php-ext-", StringComparison.OrdinalIgnoreCase)
                || path.Contains("php/_tyhpdef/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStdClass(ObjectDeclarationSymbol symbol)
        {
            var name = (symbol.FullyQualifiedName ?? symbol.Name).TrimStart('\\');
            return string.Equals(name, "stdClass", StringComparison.OrdinalIgnoreCase);
        }

        private static bool DeclarationHasLazyCapableProperty(
            ObjectDeclarationSymbol symbol,
            SymbolTree tree,
            GlobalScope scope)
        {
            foreach (var member in symbol.Members.Values)
            {
                if (member is ObjectPropertySymbol property
                    && IsLazyCapableInstanceProperty(property, symbol, tree, scope))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLazyCapableInstanceProperty(
            ObjectPropertySymbol property,
            ObjectDeclarationSymbol owner,
            SymbolTree tree,
            GlobalScope scope)
        {
            if (property.SymbolType != SymbolType.InstanceObjectProperty)
            {
                return false;
            }

            return !IsVirtualHookOnly(property, owner, tree, scope);
        }

        /// <summary>
        /// Matches PHP's virtual properties: a hooked property with no default, no arrow
        /// <c>set</c>, no <c>$this-&gt;prop</c> reference, and no plain ancestor storage.
        /// </summary>
        private static bool IsVirtualHookOnly(
            ObjectPropertySymbol property,
            ObjectDeclarationSymbol owner,
            SymbolTree tree,
            GlobalScope scope)
        {
            if (!property.HasGetHook && !property.HasSetHook)
            {
                return false;
            }

            if (property.DefaultValue is not null)
            {
                return false;
            }

            var hooks = HooksOf(property);
            if (hooks is not null)
            {
                foreach (var hook in hooks.GetAllNotNull())
                {
                    if (string.Equals(hook.Identifier, "set", StringComparison.OrdinalIgnoreCase)
                        && hook.IsExpressionBody)
                    {
                        return false;
                    }
                }

                if (HookListReferencesProperty(hooks, BareName(property.Name)))
                {
                    return false;
                }
            }

            return !HasPlainAncestorProperty(owner, BareName(property.Name), tree, scope);
        }

        private static bool HasPlainAncestorProperty(
            ObjectDeclarationSymbol owner,
            string bareName,
            SymbolTree tree,
            GlobalScope scope)
        {
            var seen = new HashSet<ObjectDeclarationSymbol>();
            for (var current = TypeComparer.TryGetParentDeclaration(owner, tree, scope);
                 current is not null && seen.Add(current);
                 current = TypeComparer.TryGetParentDeclaration(current, tree, scope))
            {
                if (HasPlainInstanceProperty(current, bareName))
                {
                    return true;
                }

                foreach (var trait in TypeComparer.ResolveUsedTraits(current, tree, scope, out _))
                {
                    if (HasPlainInstanceProperty(trait, bareName))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasPlainInstanceProperty(ObjectDeclarationSymbol symbol, string bareName)
        {
            foreach (var member in symbol.Members.Values)
            {
                if (member is not ObjectPropertySymbol property
                    || property.SymbolType != SymbolType.InstanceObjectProperty
                    || property.HasGetHook
                    || property.HasSetHook)
                {
                    continue;
                }

                if (string.Equals(BareName(property.Name), bareName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static PhpPropertyHookListAst? HooksOf(ObjectPropertySymbol property) =>
            property.DeclaringAstNode switch
            {
                PhpPropertyAst declared => declared.Hooks,
                PhpParameterAst parameter => parameter.PropertyHooks as PhpPropertyHookListAst,
                _ => null,
            };

        private static bool HookListReferencesProperty(PhpPropertyHookListAst hooks, string bareName)
        {
            foreach (var hook in hooks.GetAllNotNull())
            {
                if (hook.Body is IBase2Ast body && ReferencesThisProperty(body, bareName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ReferencesThisProperty(IBase2Ast root, string bareName)
        {
            if (root is PhpDereferenceableAst
                {
                    Base: PhpVariableAst variable,
                    Suffix: PhpInstanceMemberAccessAst { MemberName: PhpNameAst member },
                }
                && IsThis(variable)
                && string.Equals(BareName(member.ValueString ?? ""), bareName, StringComparison.Ordinal))
            {
                return true;
            }

            foreach (var child in root.AstChildren)
            {
                if (child is not null && ReferencesThisProperty(child, bareName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsThis(PhpVariableAst variable)
        {
            var name = variable.VariableToken?.ValueString?.TrimStart('$');
            return string.Equals(name, "this", StringComparison.OrdinalIgnoreCase);
        }

        private static string BareName(string name) => name.TrimStart('$');
    }
}
