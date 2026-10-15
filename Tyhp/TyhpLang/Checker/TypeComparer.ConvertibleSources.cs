using System;
using System.Collections.Generic;
using System.Linq;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>How a value reaches a parameter through <c>operator convert</c>.</summary>
    internal enum ConvertibleSourceKind
    {
        /// <summary>The target class declares <c>operator convert(X $v): self</c> (static <c>__from</c>).</summary>
        ConvertFrom,

        /// <summary>A source class declares <c>operator convert(self $v): T</c> (<c>__to{T}</c>).</summary>
        ConvertTo,
    }

    /// <summary>
    /// One type that converts to a parameter's declared type at a call site: a value of
    /// <see cref="SourceType"/> is rewritten through <see cref="Overload"/>.
    /// </summary>
    internal sealed class ConvertibleSource
    {
        public required ConvertibleSourceKind Kind { get; init; }

        /// <summary>The type a caller may pass for this source.</summary>
        public required ICheckedType SourceType { get; init; }

        public required ObjectOperatorOverloadMethodSymbol Overload { get; init; }

        /// <summary>
        /// <see cref="ConvertibleSourceKind.ConvertFrom"/>: the target class the convert is
        /// declared on (or extends). <see cref="ConvertibleSourceKind.ConvertTo"/>: the source
        /// class that declares the convert (or the class an extension extends).
        /// </summary>
        public required ObjectDeclarationSymbol Owner { get; init; }

        /// <summary>
        /// The key of the concrete type this source converts to (<see cref="TypeComparer.TryGetConvertTargetKey"/>
        /// of the enumerated target). Sources enumerated from different overload signatures differ here.
        /// </summary>
        public required string TargetKey { get; init; }
    }

    /// <summary>A class that converts to a given target, with the overload that does it.</summary>
    internal sealed record ConvertToCandidate(
        ObjectDeclarationSymbol SourceClass,
        ObjectOperatorOverloadMethodSymbol Overload);

    public static partial class TypeComparer
    {
        /// <summary>
        /// The types that implicitly convert to <paramref name="target"/> at a call site: the
        /// target-driven view of <see cref="IsAssignableViaOperatorConvert"/>. Empty when the
        /// target is not one concrete type (a multi-member union, <c>mixed</c>, unresolved).
        /// Project-source extension converts are limited to the current call-site file
        /// (<see cref="UseConvertSite"/>), the same rule as the source-driven check.
        /// </summary>
        /// <param name="target">The declared parameter type (<c>?T</c> counts as <c>T</c>).</param>
        /// <param name="globalScope">The compilation's global scope.</param>
        /// <param name="resolveSourceType">Resolves a convert-from operand's declared type.</param>
        internal static IReadOnlyList<ConvertibleSource> EnumerateConvertibleSources(
            ICheckedType target,
            GlobalScope globalScope,
            Func<ITypeExpression, ICheckedType> resolveSourceType)
        {
            if (IsUnresolvedType(target)
                || !TryGetSingleConvertTargetKey(target, out var targetKey, out var expectedObject))
            {
                return [];
            }

            var sources = new List<ConvertibleSource>();

            // convert-from: `operator convert(X $v): self` on the target class or an extension of it.
            if (expectedObject is { IsStruct: false })
            {
                foreach (var candidate in EnumerateClassOperatorOverloads(expectedObject, globalScope)
                    .Concat(VisibleExtensionConverts(expectedObject, globalScope)))
                {
                    if (candidate.IsNativePassthrough
                        || candidate.Operator != OverloadableOperator.Convert
                        || candidate.Parameters.Count != 1
                        || OperatorOverloadResolver.IsConvertToForm(candidate, expectedObject)
                        || candidate.Parameters[0].DeclaredType is not { } declared
                        || IsCompositeTypeExpression(declared))
                    {
                        continue;
                    }

                    var sourceType = resolveSourceType(declared);
                    if (sourceType is NullableCheckedType nullable)
                    {
                        sourceType = nullable.InnerType;
                    }

                    var sourceKey = SpellCheckedTypeKey(sourceType);
                    if (IsUnresolvedType(sourceType)
                        || string.Equals(sourceKey, "Mixed", StringComparison.OrdinalIgnoreCase)
                        || (TryGetObjectDeclaration(sourceType) is { } sourceObject
                            && ReferenceEquals(sourceObject, expectedObject)))
                    {
                        continue;
                    }

                    sources.Add(new ConvertibleSource
                    {
                        Kind = ConvertibleSourceKind.ConvertFrom,
                        SourceType = sourceType,
                        Overload = candidate,
                        Owner = expectedObject,
                        TargetKey = targetKey,
                    });
                }
            }

            // convert-to: a class with `operator convert(self $v): T` whose T is the target.
            foreach (var candidate in GetConvertToCandidates(targetKey, globalScope))
            {
                if (expectedObject is not null && ReferenceEquals(candidate.SourceClass, expectedObject))
                {
                    continue;
                }

                sources.Add(new ConvertibleSource
                {
                    Kind = ConvertibleSourceKind.ConvertTo,
                    SourceType = CheckedTypes.FromSymbol(candidate.SourceClass),
                    Overload = candidate.Overload,
                    Owner = candidate.SourceClass,
                    TargetKey = targetKey,
                });
            }

            return sources;
        }

        private static bool IsCompositeTypeExpression(ITypeExpression type) =>
            type is Ast.PhpTypeExpressionAst { TypeKind: PhpTypeKind.Union or PhpTypeKind.Intersection };

        private static IReadOnlyList<ConvertToCandidate> GetConvertToCandidates(
            string targetKey,
            GlobalScope globalScope)
        {
            var site = _convertSite;
            if (site is null)
            {
                BuildConvertToIndex(globalScope, null).TryGetValue(targetKey, out var uncached);
                return uncached ?? [];
            }

            site.ConvertToIndex ??= BuildConvertToIndex(globalScope, site.File);
            return site.ConvertToIndex.TryGetValue(targetKey, out var cached) ? cached : [];
        }

        /// <summary>
        /// Every convert-to overload visible from <paramref name="file"/>, keyed by the spelled
        /// target key. One walk of the scope tree; each class reads its own declaration scope.
        /// </summary>
        private static Dictionary<string, List<ConvertToCandidate>> BuildConvertToIndex(
            GlobalScope globalScope,
            Ast.SrcFileAst? file)
        {
            var index = new Dictionary<string, List<ConvertToCandidate>>(StringComparer.OrdinalIgnoreCase);

            foreach (var objectScope in EnumerateObjectScopes(globalScope))
            {
                if (objectScope.DeclarationSymbol is not ObjectDeclarationSymbol
                    {
                        IsStruct: false,
                        ObjectKind: not PhpTypeDeclType.Trait,
                    } declaration)
                {
                    continue;
                }

                var overloads = ((IBaseScope)objectScope).GetAllChildSymbols()
                    .OfType<ObjectOperatorOverloadMethodSymbol>()
                    .Where(o => !o.IsExtensionOperator)
                    .Concat(declaration.ExtensionContributedOperators.Where(
                        o => ExtensionActivation.IsConvertVisible(o, file, globalScope)));

                foreach (var overload in overloads)
                {
                    if (overload.IsNativePassthrough
                        || overload.Operator != OverloadableOperator.Convert
                        || overload.Parameters.Count != 1
                        || !OperatorOverloadResolver.IsConvertToForm(overload, declaration))
                    {
                        continue;
                    }

                    var returnKey = OperatorOverloadResolver.SpellTypeKey(overload.ReturnType, declaration.Name);
                    if (!index.TryGetValue(returnKey, out var list))
                    {
                        list = [];
                        index[returnKey] = list;
                    }

                    list.Add(new ConvertToCandidate(declaration, overload));
                }
            }

            return index;
        }

        private static IEnumerable<ObjectDeclarationScope> EnumerateObjectScopes(IBaseScope scope)
        {
            foreach (var childScope in scope.GetAllChildScopes())
            {
                if (childScope is ObjectDeclarationScope objectScope)
                {
                    yield return objectScope;
                }

                foreach (var nested in EnumerateObjectScopes(childScope))
                {
                    yield return nested;
                }
            }
        }
    }
}
