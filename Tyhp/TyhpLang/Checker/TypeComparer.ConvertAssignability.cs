using System;
using System.Collections.Generic;
using System.Linq;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Emitter.NameGeneration;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    public static partial class TypeComparer
    {
        /// <summary>
        /// True when <paramref name="source"/> can reach <paramref name="target"/> via a matching
        /// <c>operator convert</c> that emit rewrites at call / return / <c>new</c> sites
        /// (convert-to instance <c>__to{T}()</c> or convert-from static <c>__from</c>).
        /// Does <em>not</em> implement Story 31 Idea 2 <c>*Convertible</c> acceptance — only the
        /// declared convert operator forms AliasConverter already rewrites.
        /// </summary>
        public static bool IsAssignableViaOperatorConvert(
            ICheckedType source,
            ICheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (IsUnresolvedType(source) || IsUnresolvedType(target))
            {
                return false;
            }

            // Emit only rewrites when the expected type is a single concrete target (nullable
            // unwrapped; multi-member unions are skipped).
            if (!TryGetSingleConvertTargetKey(target, out var targetKey, out var expectedObject))
            {
                return false;
            }

            var nonNullSource = source is NullableCheckedType nullableSource
                ? nullableSource.InnerType
                : source;

            // convert-to: object source where a scalar/named target is expected. `$this` inside a
            // trait method types as the trait itself (checker has no per-composing-class walk of
            // trait bodies — mirrors the same gap AliasConverter closes for emit via composing-class
            // search), so a trait source also accepts a composing class's convert-to.
            if (TryGetObjectDeclaration(nonNullSource) is { IsStruct: false } sourceObject
                && (expectedObject is null || !ReferenceEquals(sourceObject, expectedObject))
                && (ClassHasConvertToOverload(sourceObject, targetKey, globalScope)
                    || (sourceObject.ObjectKind == PhpTypeDeclType.Trait
                        && TraitComposingClassHasConvertToOverload(sourceObject, targetKey, symbolTree, globalScope))))
            {
                return true;
            }

            // convert-from: scalar/other source where an object type with matching __from is expected.
            if (expectedObject is not null
                && !expectedObject.IsStruct
                && (TryGetObjectDeclaration(nonNullSource) is not { } sourceAsObject
                    || !ReferenceEquals(sourceAsObject, expectedObject))
                && ClassHasConvertFromOverload(expectedObject, nonNullSource, globalScope))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Ordinary assignability, or a convert rewrite that emit would insert at call/return/new.
        /// Prefer this over <see cref="IsAssignableTo"/> only at those sites — plain assignments
        /// are not rewritten today.
        /// </summary>
        public static bool IsAssignableToOrViaOperatorConvert(
            ICheckedType source,
            ICheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope) =>
            IsAssignableTo(source, target, symbolTree, globalScope)
            || IsAssignableViaOperatorConvert(source, target, symbolTree, globalScope);

        private static bool TryGetSingleConvertTargetKey(
            ICheckedType target,
            out string targetKey,
            out ObjectDeclarationSymbol? expectedObject)
        {
            targetKey = "";
            expectedObject = null;

            var unwrapped = target is NullableCheckedType nullable
                ? nullable.InnerType
                : target;

            if (unwrapped is UnionCheckedType union)
            {
                var members = union.Members
                    .Where(m => !IsNullLiteral(m) && !IsBuiltInName(m, "null")
                        && !IsVoidType(m) && !IsNeverType(m))
                    .ToList();
                if (members.Count != 1)
                {
                    return false;
                }

                unwrapped = members[0];
            }

            if (TryGetObjectDeclaration(unwrapped) is { IsStruct: false } objectDecl)
            {
                expectedObject = objectDecl;
                targetKey = FormatCheckedTypeKey(objectDecl.Name);
                return !string.IsNullOrEmpty(targetKey)
                    && !string.Equals(targetKey, "Mixed", StringComparison.OrdinalIgnoreCase);
            }

            targetKey = SpellCheckedTypeKey(unwrapped);
            return !string.IsNullOrEmpty(targetKey)
                && !string.Equals(targetKey, "Mixed", StringComparison.OrdinalIgnoreCase);
        }

        private static string SpellCheckedTypeKey(ICheckedType type)
        {
            if (TryGetBuiltInName(type, out var builtinName))
            {
                return FormatCheckedTypeKey(builtinName);
            }

            if (type is LiteralCheckedType literal)
            {
                if (literal.Value is bool)
                {
                    return FormatCheckedTypeKey("bool");
                }

                if (literal.Value is long or int)
                {
                    return FormatCheckedTypeKey("int");
                }

                if (literal.Value is double or float)
                {
                    return FormatCheckedTypeKey("float");
                }

                if (literal.Value is string)
                {
                    return FormatCheckedTypeKey("string");
                }

                if (TryGetBuiltInName(literal.UnderlyingType, out var underlying))
                {
                    return FormatCheckedTypeKey(underlying);
                }
            }

            if (TryGetObjectDeclaration(type) is { } obj)
            {
                return FormatCheckedTypeKey(obj.Name);
            }

            var display = type.DisplayName?.TrimStart('\\') ?? "";
            return FormatCheckedTypeKey(display);
        }

        private static string FormatCheckedTypeKey(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "Mixed";
            }

            var formatted = TypeNameFormatter.FormatTypeNameSegment(raw);
            return string.IsNullOrEmpty(formatted) ? "Mixed" : formatted;
        }

        private static bool ClassHasConvertToOverload(
            ObjectDeclarationSymbol typeSymbol,
            string targetKey,
            GlobalScope globalScope)
        {
            foreach (var overload in EnumerateClassOperatorOverloads(typeSymbol, globalScope)
                .Concat(typeSymbol.ExtensionContributedOperators))
            {
                if (overload.IsNativePassthrough
                    || overload.Operator != OverloadableOperator.Convert
                    || overload.Parameters.Count != 1
                    || !OperatorOverloadResolver.IsConvertToForm(overload, typeSymbol))
                {
                    continue;
                }

                var returnKey = OperatorOverloadResolver.SpellTypeKey(
                    overload.ReturnType, typeSymbol.Name);
                if (string.Equals(returnKey, targetKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when a class/enum that <c>use</c>s <paramref name="trait"/> declares a matching
        /// convert-to overload. Used only for a trait-typed source (i.e. <c>$this</c> inside a
        /// trait method) — mirrors <c>AliasConverter.TraitComposingClassHasConvertToOverload</c>.
        /// </summary>
        private static bool TraitComposingClassHasConvertToOverload(
            ObjectDeclarationSymbol trait,
            string targetKey,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            foreach (var composing in EnumerateObjectsUsingTrait(trait, symbolTree, globalScope))
            {
                if (ClassHasConvertToOverload(composing, targetKey, globalScope))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Classes/enums in <paramref name="globalScope"/> that <c>use</c> <paramref name="trait"/>
        /// (transitively via nested trait uses). Shared by convert-to assignability and
        /// operator-overload return inference for trait-<c>$this</c>.
        /// </summary>
        internal static IEnumerable<ObjectDeclarationSymbol> EnumerateObjectsUsingTrait(
            ObjectDeclarationSymbol trait,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            foreach (var candidate in EnumerateObjectDeclarations(globalScope))
            {
                if (candidate.ObjectKind is not (PhpTypeDeclType.Class or PhpTypeDeclType.Enum))
                {
                    continue;
                }

                var used = ResolveUsedTraits(candidate, symbolTree, globalScope, out _);
                if (used.Contains(trait))
                {
                    yield return candidate;
                }
            }
        }

        private static IEnumerable<ObjectDeclarationSymbol> EnumerateObjectDeclarations(IBaseScope scope)
        {
            foreach (var childScope in scope.GetAllChildScopes())
            {
                if (childScope is ObjectDeclarationScope { DeclarationSymbol: ObjectDeclarationSymbol decl })
                {
                    yield return decl;
                }

                foreach (var nested in EnumerateObjectDeclarations(childScope))
                {
                    yield return nested;
                }
            }
        }

        private static bool ClassHasConvertFromOverload(
            ObjectDeclarationSymbol typeSymbol,
            ICheckedType sourceType,
            GlobalScope globalScope)
        {
            var sourceKey = SpellCheckedTypeKey(sourceType);
            if (string.IsNullOrEmpty(sourceKey)
                || string.Equals(sourceKey, "Mixed", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (var candidate in EnumerateClassOperatorOverloads(typeSymbol, globalScope)
                .Concat(typeSymbol.ExtensionContributedOperators))
            {
                if (candidate.IsNativePassthrough
                    || candidate.Operator != OverloadableOperator.Convert
                    || candidate.Parameters.Count != 1
                    || OperatorOverloadResolver.IsConvertToForm(candidate, typeSymbol))
                {
                    continue;
                }

                var paramKey = OperatorOverloadResolver.SpellTypeKey(
                    candidate.Parameters[0].DeclaredType, typeSymbol.Name);
                if (string.Equals(paramKey, sourceKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Class-body operator overload members for <paramref name="typeSymbol"/> (excludes
        /// extension-contributed forms). Prefer the declaration scope when available.
        /// </summary>
        internal static IEnumerable<ObjectOperatorOverloadMethodSymbol> EnumerateClassOperatorOverloads(
            ObjectDeclarationSymbol typeSymbol,
            GlobalScope globalScope)
        {
            var objectScope = FindObjectDeclarationScope(globalScope, typeSymbol);
            if (objectScope != null)
            {
                foreach (var symbol in ((IBaseScope)objectScope).GetAllChildSymbols())
                {
                    if (symbol is ObjectOperatorOverloadMethodSymbol classOverload
                        && !classOverload.IsExtensionOperator)
                    {
                        yield return classOverload;
                    }
                }

                yield break;
            }

            // Fallback when the declaration scope is unavailable (e.g. synthetic symbols).
            foreach (var member in typeSymbol.Members.Values)
            {
                if (member is ObjectOperatorOverloadMethodSymbol classOverload
                    && !classOverload.IsExtensionOperator)
                {
                    yield return classOverload;
                }
            }
        }

        private static ObjectDeclarationScope? FindObjectDeclarationScope(
            IBaseScope scope,
            ObjectDeclarationSymbol typeSymbol)
        {
            if (scope is ObjectDeclarationScope objectScope
                && ReferenceEquals(objectScope.DeclarationSymbol, typeSymbol))
            {
                return objectScope;
            }

            foreach (var childScope in scope.GetAllChildScopes())
            {
                var found = FindObjectDeclarationScope(childScope, typeSymbol);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
