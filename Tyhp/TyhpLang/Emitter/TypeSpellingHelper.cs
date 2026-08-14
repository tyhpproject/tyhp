using System;
using System.Collections.Generic;
using System.Linq;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Produces PHP type spellings from bound type-expression AST nodes.
    /// Shared by alias-map collection and alias conversion so keys and lookups stay aligned.
    /// </summary>
    internal static class TypeSpellingHelper
    {
        public static string Spell(
            ITypeExpression? typeExpression,
            IReadOnlyDictionary<string, string>? typeAliasMap = null,
            IBaseScope? scope = null,
            string? namespacePrefix = null)
            => Spell(typeExpression, typeAliasMap, erasingParams: null, resolvingAliases: null, scope, namespacePrefix);

        /// <summary>
        /// PHP typehint spelling from a checker <see cref="ICheckedType"/>, using the same erasure
        /// rules as <see cref="Spell"/> (generics → constraint/`mixed`, structs → <c>array</c>,
        /// symbol-name brands → <c>string</c>, unions collapse on <c>mixed</c>, …). Used when the
        /// emitter recovers a typehint that was never written in Tyhp source.
        /// </summary>
        public static string SpellCheckedType(
            ICheckedType? type,
            IReadOnlyDictionary<string, string>? typeAliasMap = null,
            IBaseScope? scope = null,
            string? namespacePrefix = null)
            => SpellCheckedType(type, typeAliasMap, erasingParams: null, resolvingAliases: null, scope, namespacePrefix);

        /// <summary>
        /// PHPDoc / static-analysis spelling: keep bare generic parameter names (e.g. <c>TValue</c>)
        /// and type-argument lists (e.g. <c>\Foo\Box&lt;TValue&gt;</c>) so SA tools can see the
        /// template. Runtime PHP typehints still use <see cref="Spell"/> erasure.
        /// </summary>
        public static string SpellForPhpDoc(
            ITypeExpression? typeExpression,
            IReadOnlyDictionary<string, string>? typeAliasMap = null,
            IBaseScope? scope = null,
            string? namespacePrefix = null)
            => Spell(
                typeExpression,
                typeAliasMap,
                erasingParams: null,
                resolvingAliases: null,
                scope,
                namespacePrefix,
                preserveGenericParameters: true);

        private static string Spell(
            ITypeExpression? typeExpression,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope = null,
            string? namespacePrefix = null,
            bool preserveGenericParameters = false)
        {
            if (typeExpression == null)
            {
                return "";
            }

            return typeExpression switch
            {
                PhpTypeExpressionAst typeExpr => SpellComposite(
                    typeExpr, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix, preserveGenericParameters),
                PhpNamedTypeAst namedType => SpellNamedType(
                    namedType, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix, preserveGenericParameters),
                PhpBuiltinTypeAst builtinType => SpellBuiltinType(builtinType, typeAliasMap),
                TyhpReturnTypeGuardAst => "bool",
                TyhpTemplateStringTypeAst template => template.ValueString ?? "string",
                _ => "",
            };
        }

        private static string SpellComposite(
            PhpTypeExpressionAst typeExpr,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix,
            bool preserveGenericParameters = false)
        {
            var types = typeExpr.Types?.GetAllNotNull().ToList() ?? [];
            if (types.Count == 0)
            {
                return "";
            }

            // PSR-12 §6.2: one space before and after `|` / `&` in union and intersection types.
            var separator = typeExpr.TypeKind switch
            {
                PhpTypeKind.Union => " | ",
                PhpTypeKind.Intersection => " & ",
                _ => "",
            };

            if (typeExpr.IsStatic)
            {
                return "static";
            }

            var parts = types
                .Select(t => Spell(
                    t, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix, preserveGenericParameters))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                // Literal unions such as `'a' | 'b'` widen to `string | string`; collapse duplicates
                // so the PHP hint is a single scalar (or a clean mixed-scalar union).
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (parts.Count == 0)
            {
                return "";
            }

            // `mixed` cannot be combined in a union/intersection or marked nullable in PHP
            // (e.g. `?mixed` and `mixed|string` are fatal errors). Whenever a composite contains
            // `mixed` — most commonly from an erased generic parameter — it collapses to a bare
            // `mixed`, which already subsumes every other member including null.
            if (parts.Any(p => p == "mixed"))
            {
                return "mixed";
            }

            // PHP intersection types may only contain class/interface names. Drop illegal members
            // (`object`, `array` from structs, `callable`, scalars, …). Structural bounds such as
            // `object&TProperties` (→ `object&array`) become `object`; `Foo&SomeStruct` becomes
            // `Foo`; pure class intersections like `Foo&Bar` are kept.
            if (typeExpr.TypeKind == PhpTypeKind.Intersection)
            {
                parts = NormalizePhpIntersectionParts(parts);
            }

            // Prefer PHP nullable shorthand: `T | null` / `null | T` → `?T` (single simple type only).
            if (typeExpr.TypeKind == PhpTypeKind.Union && parts.Count == 2)
            {
                var nullIndex = parts.FindIndex(p => string.Equals(p, "null", StringComparison.OrdinalIgnoreCase));
                if (nullIndex >= 0)
                {
                    var other = parts[1 - nullIndex];
                    if (IsSimplePhpTypeName(other))
                    {
                        return "?" + other;
                    }
                }
            }

            // After intersection normalization a single survivor is not joined with `&`.
            var result = separator.Length > 0 && parts.Count > 1
                ? string.Join(separator, parts)
                : parts.First();

            if (typeExpr.IsNullable)
            {
                result = "?" + result;
            }

            return result;
        }

        private static string SpellNamedType(
            PhpNamedTypeAst namedType,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix,
            bool preserveGenericParameters = false)
        {
            // Resolve bound type aliases to their underlying PHP spelling before any FQN path.
            if (TrySpellBoundAlias(
                    namedType.BoundSymbol,
                    typeAliasMap,
                    erasingParams,
                    ref resolvingAliases,
                    scope,
                    namespacePrefix,
                    preserveGenericParameters,
                    out var fromBound))
            {
                return MaybeAppendPhpDocTypeArguments(
                    fromBound, namedType, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix, preserveGenericParameters);
            }

            // PHP has no generics, so a generic type parameter (e.g. `TValue`) is erased in any
            // *runtime* type-hint position. The closest valid PHP equivalent is the parameter's
            // declared constraint (`<T extends Foo>` -> `Foo`); an unconstrained parameter falls
            // back to `mixed`. Runtime `Tyhp\Type` checks still enforce the full original
            // constraint. BoundSymbol may live on the named type or on its name child (resolution
            // records both shapes depending on the path); either must erase for PHP hints —
            // bare `T` is not a PHP type. PHPDoc / SA spellings keep the parameter name instead.
            if (TryGetGenericTypeParameter(namedType) is { } genericParam)
            {
                if (preserveGenericParameters)
                {
                    return genericParam.Name;
                }

                return EraseGenericParameter(genericParam, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix);
            }

            if (StructEmissionHelper.ResolveStructFromNamedType(namedType, scope) is not null)
            {
                // Prefer the rewritten name when AliasConverter has already swapped a struct type
                // for a custom backing class (BoundSymbol cleared, Name is the backing FQN).
                var rewrittenName = namedType.Name switch
                {
                    PhpNameAst n => n.ValueString ?? "",
                    _ => "",
                };
                if (!string.IsNullOrWhiteSpace(rewrittenName)
                    && rewrittenName.Contains('\\')
                    && namedType.BoundSymbol is null)
                {
                    var rewritten = rewrittenName.StartsWith('\\') ? rewrittenName : "\\" + rewrittenName;
                    return MaybeAppendPhpDocTypeArguments(
                        rewritten, namedType, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix, preserveGenericParameters);
                }

                return "array";
            }

            if (namedType.BoundSymbol is BuiltInTypeSymbol builtIn)
            {
                return MaybeAppendPhpDocTypeArguments(
                    SpellBuiltinName(builtIn.Name, typeAliasMap),
                    namedType,
                    typeAliasMap,
                    erasingParams,
                    resolvingAliases,
                    scope,
                    namespacePrefix,
                    preserveGenericParameters);
            }

            // Symbol-name types (`__ClassName`, `__FunctionName`, …) and Phase 7 type-name
            // algebra brands (`__TypeName`, `__AsType`, …) are checker-only — PHP has no such
            // classes. Erase them so signatures stay callable at runtime (Parametric #2 / #3).
            if (TryEraseSymbolNameType(
                    namedType.BoundSymbol,
                    namedType,
                    typeAliasMap,
                    erasingParams,
                    resolvingAliases,
                    scope,
                    namespacePrefix,
                    preserveGenericParameters,
                    out var erasedFromNamed))
            {
                return erasedFromNamed;
            }

            if (TryEraseTypeNameAlgebraType(namedType.BoundSymbol, namedType, out var algebraFromNamed))
            {
                return algebraFromNamed;
            }

            if (TryEraseUtilityType(
                    namedType.BoundSymbol,
                    namedType,
                    typeAliasMap,
                    erasingParams,
                    resolvingAliases,
                    scope,
                    namespacePrefix,
                    preserveGenericParameters,
                    out var utilityFromNamed))
            {
                return utilityFromNamed;
            }

            var nameExpr = namedType.Name;
            var nameText = nameExpr switch
            {
                TyhpGenericIdentifierAst g => g.ValueString ?? "",
                PhpNameAst n => n.ValueString ?? "",
                _ => "",
            };

            // Keep relative class keywords as written (`self` / `static` / `parent`), even when
            // BoundSymbol points at the concrete class (otherwise `self` would emit as FQN).
            if (IsRelativeClassKeyword(nameText))
            {
                return MaybeAppendPhpDocTypeArguments(
                    nameText, namedType, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix, preserveGenericParameters);
            }

            if (namedType.BoundSymbol is IBaseSymbol namedBound
                && !string.IsNullOrWhiteSpace(namedBound.FullyQualifiedName)
                && namedBound is not TypeAliasSymbol
                && namedBound is not ObjectTypeAliasSymbol)
            {
                return MaybeAppendPhpDocTypeArguments(
                    EmittedFqnHelper.Format(namedBound.FullyQualifiedName, namespacePrefix, namedBound),
                    namedType,
                    typeAliasMap,
                    erasingParams,
                    resolvingAliases,
                    scope,
                    namespacePrefix,
                    preserveGenericParameters);
            }

            if (nameExpr is TyhpGenericIdentifierAst)
            {
                return MaybeAppendPhpDocTypeArguments(
                    nameText, namedType, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix, preserveGenericParameters);
            }

            if (nameExpr is PhpNameAst name)
            {
                if (name.BoundSymbol is GenericTypeParameterSymbol nameGenericParam)
                {
                    if (preserveGenericParameters)
                    {
                        return nameGenericParam.Name;
                    }

                    return EraseGenericParameter(nameGenericParam, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix);
                }

                if (TrySpellBoundAlias(
                        name.BoundSymbol,
                        typeAliasMap,
                        erasingParams,
                        ref resolvingAliases,
                        scope,
                        namespacePrefix,
                        preserveGenericParameters,
                        out var fromNameAlias))
                {
                    return MaybeAppendPhpDocTypeArguments(
                        fromNameAlias, namedType, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix, preserveGenericParameters);
                }

                if (TryEraseSymbolNameType(
                        name.BoundSymbol,
                        namedType,
                        typeAliasMap,
                        erasingParams,
                        resolvingAliases,
                        scope,
                        namespacePrefix,
                        preserveGenericParameters,
                        out var erasedFromName))
                {
                    return erasedFromName;
                }

                if (TryEraseTypeNameAlgebraType(name.BoundSymbol, namedType, out var algebraFromName))
                {
                    return algebraFromName;
                }

                if (TryEraseUtilityType(
                        name.BoundSymbol,
                        namedType,
                        typeAliasMap,
                        erasingParams,
                        resolvingAliases,
                        scope,
                        namespacePrefix,
                        preserveGenericParameters,
                        out var utilityFromName))
                {
                    return utilityFromName;
                }

                if (name.BoundSymbol is IBaseSymbol bound
                    && !string.IsNullOrWhiteSpace(bound.FullyQualifiedName)
                    && bound is not TypeAliasSymbol
                    && bound is not ObjectTypeAliasSymbol)
                {
                    return MaybeAppendPhpDocTypeArguments(
                        EmittedFqnHelper.Format(bound.FullyQualifiedName, namespacePrefix, bound),
                        namedType,
                        typeAliasMap,
                        erasingParams,
                        resolvingAliases,
                        scope,
                        namespacePrefix,
                        preserveGenericParameters);
                }

                if (typeAliasMap != null
                    && typeAliasMap.TryGetValue(nameText, out var aliased)
                    && !string.IsNullOrWhiteSpace(aliased))
                {
                    return MaybeAppendPhpDocTypeArguments(
                        aliased, namedType, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix, preserveGenericParameters);
                }

                return MaybeAppendPhpDocTypeArguments(
                    nameText, namedType, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix, preserveGenericParameters);
            }

            return "";
        }

        /// <summary>
        /// Erases Story 08.5 symbol-name utility types to their PHP emit spelling.
        /// Reuses <see cref="SymbolNameTypeHelper.IsSymbolNameBehavior"/> so checker and emitter
        /// stay aligned on the brand family.
        /// </summary>
        private static bool TryEraseSymbolNameType(
            IBaseSymbol? symbol,
            IBase2Ast? addonHost,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix,
            bool preserveGenericParameters,
            out string spelling)
        {
            if (symbol is not BuiltInUtilityTypeSymbol utility
                || !SymbolNameTypeHelper.IsSymbolNameBehavior(utility.Behavior))
            {
                spelling = "";
                return false;
            }

            // `__TyhpInternal<T>` is not a name-string brand like its siblings — the checker
            // resolves it directly to `T` (`UtilityTypeResolver.Resolve`), so the emitted
            // signature must spell `T` too. Forcing `string` here would let a checker-accepted
            // call (e.g. an `int` argument for `__TyhpInternal<int>`) emit a `string` type hint
            // and throw a `TypeError` at runtime — the exact failure mode Parametric #2 fixes for
            // every other brand in this family.
            if (utility.Behavior == UtilityBehavior.TyhpInternal)
            {
                var typeArgExpr = GetFirstGenericTypeArgument(addonHost);
                var argSpelling = typeArgExpr is null
                    ? ""
                    : Spell(
                        typeArgExpr,
                        typeAliasMap,
                        erasingParams,
                        resolvingAliases,
                        scope,
                        namespacePrefix,
                        preserveGenericParameters);
                spelling = string.IsNullOrWhiteSpace(argSpelling) ? "mixed" : argSpelling;
                return true;
            }

            // Full erasure for every remaining symbol-name brand is plain <c>string</c>.
            spelling = "string";
            return true;
        }

        /// <summary>
        /// Erases Story 08.5 Phase 5 struct/type utilities, Story 16.5 callable-signature
        /// utilities, and <c>\Tyhp\…</c> utilities to their PHP surface. These types are
        /// checker-only carriers — emitting <c>\__StructKey</c> / <c>\Tyhp\ReturnType</c> /
        /// <c>\__CallableReturnType</c> produces a fatal undefined-class type hint.
        /// Documented erasure targets live in <c>Examples/NewBuiltinTypes.tyhp</c>.
        /// </summary>
        private static bool TryEraseUtilityType(
            IBaseSymbol? symbol,
            IBase2Ast? addonHost,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix,
            bool preserveGenericParameters,
            out string spelling)
        {
            spelling = "";
            if (symbol is not BuiltInUtilityTypeSymbol utility)
            {
                return false;
            }

            // Symbol-name and type-name-algebra brands have dedicated erase helpers.
            if (SymbolNameTypeHelper.IsSymbolNameBehavior(utility.Behavior)
                || TypeNameAlgebraResolver.IsTypeNameAlgebraBehavior(utility.Behavior))
            {
                return false;
            }

            switch (utility.Behavior)
            {
                case UtilityBehavior.StructKey:
                case UtilityBehavior.Properties:
                    spelling = "string";
                    return true;

                case UtilityBehavior.StructRecord:
                    // Type-level carrier only (`= void` in Examples/NewBuiltinTypes.tyhp). PHP has
                    // no void parameters; use mixed so a mistaken value-position use stays callable.
                    spelling = "mixed";
                    return true;

                case UtilityBehavior.StructDef:
                case UtilityBehavior.StructPartial:
                case UtilityBehavior.Record:
                case UtilityBehavior.Pick:
                case UtilityBehavior.Omit:
                case UtilityBehavior.Partial:
                case UtilityBehavior.Required:
                case UtilityBehavior.Parameters:
                case UtilityBehavior.CallableParametersStruct:
                case UtilityBehavior.CallableParametersTuple:
                    spelling = "array";
                    return true;

                case UtilityBehavior.CallableParametersRest:
                    // Variadic *element* type (`Rest<T> ...$args`). Spelling `array` would make
                    // PHP demand each unpacked argument be an array.
                    spelling = "mixed";
                    return true;

                case UtilityBehavior.Readonly:
                    // Preserve the underlying object/struct spelling (struct → array via Spell).
                    spelling = SpellUtilityTypeArgument(
                        GetFirstGenericTypeArgument(addonHost),
                        typeAliasMap,
                        erasingParams,
                        resolvingAliases,
                        scope,
                        namespacePrefix,
                        preserveGenericParameters);
                    return true;

                case UtilityBehavior.ReturnType:
                case UtilityBehavior.CallableReturnType:
                    spelling = SpellCallableReturnTypeArgument(
                        GetFirstGenericTypeArgument(addonHost),
                        typeAliasMap,
                        erasingParams,
                        resolvingAliases,
                        scope,
                        namespacePrefix,
                        preserveGenericParameters);
                    return true;

                case UtilityBehavior.NonNullable:
                case UtilityBehavior.AsNotNullable:
                case UtilityBehavior.Awaited:
                case UtilityBehavior.AsReadOnly:
                case UtilityBehavior.FunctionReturnType:
                    spelling = SpellUtilityTypeArgument(
                        GetFirstGenericTypeArgument(addonHost),
                        typeAliasMap,
                        erasingParams,
                        resolvingAliases,
                        scope,
                        namespacePrefix,
                        preserveGenericParameters);
                    return true;

                case UtilityBehavior.Nullable:
                case UtilityBehavior.AsNullable:
                {
                    var inner = SpellUtilityTypeArgument(
                        GetFirstGenericTypeArgument(addonHost),
                        typeAliasMap,
                        erasingParams,
                        resolvingAliases,
                        scope,
                        namespacePrefix,
                        preserveGenericParameters);
                    spelling = inner.StartsWith('?') || string.Equals(inner, "mixed", StringComparison.Ordinal)
                        ? inner
                        : "?" + inner;
                    return true;
                }

                case UtilityBehavior.Exclude:
                case UtilityBehavior.Extract:
                case UtilityBehavior.TypeDiff:
                    spelling = SpellUtilityTypeArgument(
                        GetFirstGenericTypeArgument(addonHost),
                        typeAliasMap,
                        erasingParams,
                        resolvingAliases,
                        scope,
                        namespacePrefix,
                        preserveGenericParameters);
                    return true;

                case UtilityBehavior.MethodReturnType:
                    // Owner + method-name pair; without checker resolution fall back to mixed.
                    spelling = "mixed";
                    return true;

                default:
                    return false;
            }
        }

        private static string SpellUtilityTypeArgument(
            ITypeExpression? typeArgExpr,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix,
            bool preserveGenericParameters)
        {
            if (typeArgExpr is null)
            {
                return "mixed";
            }

            var spelled = Spell(
                typeArgExpr,
                typeAliasMap,
                erasingParams,
                resolvingAliases,
                scope,
                namespacePrefix,
                preserveGenericParameters);
            return string.IsNullOrWhiteSpace(spelled) ? "mixed" : spelled;
        }

        /// <summary>
        /// <c>\Tyhp\ReturnType&lt;callable&lt;…, TReturn&gt;&gt;</c> / closure forms → spell
        /// <c>TReturn</c>. Bare <c>callable</c> without type args → <c>mixed</c>.
        /// </summary>
        private static string SpellCallableReturnTypeArgument(
            ITypeExpression? callableArg,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix,
            bool preserveGenericParameters)
        {
            if (callableArg is null)
            {
                return "mixed";
            }

            var leaf = UnwrapSingleTypeLeaf(callableArg);
            var callableArgs = GetAllGenericTypeArguments(leaf as IBase2Ast ?? callableArg);
            if (callableArgs.Count > 0)
            {
                return SpellUtilityTypeArgument(
                    callableArgs[^1],
                    typeAliasMap,
                    erasingParams,
                    resolvingAliases,
                    scope,
                    namespacePrefix,
                    preserveGenericParameters);
            }

            // Bare callable/Closure — return type unknown at emit.
            return "mixed";
        }

        /// <summary>
        /// Erases Story 08.5 Phase 7 type-name algebra utility types to their PHP emit spelling.
        /// Reuses <see cref="TypeNameAlgebraResolver.IsTypeNameAlgebraBehavior"/> so checker and
        /// emitter stay aligned on the brand family.
        /// Most algebra brands are string brands (type-name / template-string types) and erase to
        /// <c>string</c>. <c>__AsType&lt;…&gt;</c> is the inverse converter — it resolves a
        /// type-name string back to a type — so it must spell the resolved type, not
        /// <c>string</c> and not <c>\__AsType</c> (see FOUND_BUGS Parametric #3).
        /// </summary>
        private static bool TryEraseTypeNameAlgebraType(
            IBaseSymbol? symbol,
            IBase2Ast? addonHost,
            out string spelling)
        {
            if (symbol is not BuiltInUtilityTypeSymbol utility
                || !TypeNameAlgebraResolver.IsTypeNameAlgebraBehavior(utility.Behavior))
            {
                spelling = "";
                return false;
            }

            if (utility.Behavior == UtilityBehavior.AsType)
            {
                spelling = SpellAsTypeResolvedArgument(GetFirstGenericTypeArgument(addonHost));
                return true;
            }

            // Every remaining algebra brand is a type-name / template-string string brand.
            spelling = "string";
            return true;
        }

        /// <summary>
        /// Mirrors <see cref="TypeNameAlgebraResolver"/>'s <c>ResolveAsType</c> for emit:
        /// a string-literal type argument <c>'int'</c> becomes <c>int</c>; unknown / non-literal
        /// arguments fall back to <c>mixed</c> (the checker-side wide union collapses the same way).
        /// </summary>
        private static string SpellAsTypeResolvedArgument(ITypeExpression? typeArgExpr)
        {
            var leaf = UnwrapSingleTypeLeaf(typeArgExpr);
            if (leaf is PhpBuiltinTypeAst builtin
                && StaticValueTypeHelper.TryParse(builtin.Identifier, out var literalValue, out _)
                && literalValue is string typeNameLiteral)
            {
                return MapTypeNameLiteralToPhpSpelling(typeNameLiteral);
            }

            // Bare (unquoted) builtin type argument, e.g. `__AsType<int>` rather than
            // `__AsType<'int'>`: the written type is a named type bound to the builtin symbol
            // rather than a literal spelling — read its name directly so both spellings agree.
            if (leaf?.BoundSymbol is BuiltInTypeSymbol boundBuiltin)
            {
                return MapTypeNameLiteralToPhpSpelling(boundBuiltin.Name);
            }

            return "mixed";
        }

        /// <summary>
        /// Generic type arguments are visited as <see cref="PhpTypeExpressionAst"/> wrappers
        /// (<c>VisitTyhpGenericTypeArgument</c> → <c>VisitTypeExpr</c>). Unwrap a single-member
        /// composite so callers can inspect the leaf builtin / named type.
        /// </summary>
        private static ITypeExpression? UnwrapSingleTypeLeaf(ITypeExpression? typeExpr)
        {
            while (typeExpr is PhpTypeExpressionAst composite)
            {
                var members = composite.Types?.GetAllNotNull().ToList() ?? [];
                if (members.Count != 1)
                {
                    return typeExpr;
                }

                typeExpr = members[0];
            }

            return typeExpr;
        }

        /// <summary>
        /// Maps a decoded type-name string (contents of <c>'int'</c>, <c>'struct'</c>, …) to the
        /// PHP type hint <see cref="TypeNameAlgebraResolver"/> would resolve <c>__AsType</c> to.
        /// </summary>
        private static string MapTypeNameLiteralToPhpSpelling(string typeNameLiteral)
        {
            switch (typeNameLiteral.ToLowerInvariant())
            {
                case "void":
                    // PHP allows <c>void</c> only as a return type; still spell it so return
                    // positions stay precise. Parameter/property misuse is a separate checker concern.
                    return "void";
                case "null":
                    return "null";
                case "struct":
                    return "array";
                case "int":
                case "float":
                case "bool":
                case "string":
                case "mixed":
                case "array":
                case "object":
                case "callable":
                case "iterable":
                    return typeNameLiteral.ToLowerInvariant();
                default:
                    // Non-builtin literals (class FQNs, unions, …) resolve to the wide
                    // mixed|struct|void union on the checker side — emit <c>mixed</c>.
                    return "mixed";
            }
        }

        /// <summary>
        /// Reads the written <c>&lt;T&gt;</c> type arguments: prefer the "typeName" grammar addon
        /// on the named-type / name node (see
        /// <c>PhpParserAstVisitor.PhpTypes.VisitTypeWithoutStatic</c>), then fall back to
        /// <see cref="TyhpGenericIdentifierAst.GenericArguments"/> when the identifier itself
        /// carries the args.
        /// </summary>
        private static ITypeExpression? GetFirstGenericTypeArgument(IBase2Ast? node)
        {
            if (node?.AstGrammarAddons.TryGetValue("typeName", out var addon) == true
                && addon is PhpTypeExpressionListAst list)
            {
                return list.GetAllNotNull().FirstOrDefault();
            }

            if (node is PhpNamedTypeAst { Name: TyhpGenericIdentifierAst genericOnNamed }
                && genericOnNamed.GenericArguments is PhpTypeExpressionListAst namedArgs)
            {
                return namedArgs.GetAllNotNull().FirstOrDefault();
            }

            if (node is TyhpGenericIdentifierAst generic
                && generic.GenericArguments is PhpTypeExpressionListAst identifierArgs)
            {
                return identifierArgs.GetAllNotNull().FirstOrDefault();
            }

            return null;
        }

        /// <summary>
        /// All written <c>&lt;T,…&gt;</c> type arguments on a named type (addon list or identifier
        /// children), for PHPDoc spellings that keep the argument list.
        /// </summary>
        private static IReadOnlyList<ITypeExpression> GetAllGenericTypeArguments(IBase2Ast? node)
        {
            if (node?.AstGrammarAddons.TryGetValue("typeName", out var addon) == true
                && addon is PhpTypeExpressionListAst list)
            {
                return list.GetAllNotNull().ToList();
            }

            if (node is PhpNamedTypeAst { Name: TyhpGenericIdentifierAst genericOnNamed }
                && genericOnNamed.GenericArguments is PhpTypeExpressionListAst namedArgs)
            {
                return namedArgs.GetAllNotNull().ToList();
            }

            if (node is TyhpGenericIdentifierAst generic
                && generic.GenericArguments is PhpTypeExpressionListAst identifierArgs)
            {
                return identifierArgs.GetAllNotNull().ToList();
            }

            return Array.Empty<ITypeExpression>();
        }

        /// <summary>
        /// When spelling for PHPDoc, append <c>&lt;Arg,…&gt;</c> so SA sees
        /// <c>\Probe\Box&lt;TValue&gt;</c> instead of a bare erased class name.
        /// </summary>
        private static string MaybeAppendPhpDocTypeArguments(
            string baseSpelling,
            PhpNamedTypeAst namedType,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix,
            bool preserveGenericParameters)
        {
            if (!preserveGenericParameters || string.IsNullOrWhiteSpace(baseSpelling))
            {
                return baseSpelling;
            }

            var args = GetAllGenericTypeArguments(namedType);
            if (args.Count == 0)
            {
                return baseSpelling;
            }

            var spelledArgs = args
                .Select(a => Spell(
                    a, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix, preserveGenericParameters: true))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            if (spelledArgs.Count == 0)
            {
                return baseSpelling;
            }

            return baseSpelling + "<" + string.Join(", ", spelledArgs) + ">";
        }

        private static bool TrySpellBoundAlias(
            IBaseSymbol? symbol,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            ref HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix,
            bool preserveGenericParameters,
            out string spelling)
        {
            ITypeExpression? aliasedType = symbol switch
            {
                TypeAliasSymbol fileAlias => fileAlias.AliasedType,
                ObjectTypeAliasSymbol objectAlias => objectAlias.AliasedType,
                _ => null,
            };

            if (symbol == null || aliasedType == null)
            {
                spelling = "";
                return false;
            }

            resolvingAliases ??= new HashSet<IBaseSymbol>(ReferenceEqualityComparer.Instance);
            if (!resolvingAliases.Add(symbol))
            {
                // Circular alias — defensive fallback (checker should already report).
                spelling = "mixed";
                return true;
            }

            try
            {
                spelling = Spell(
                    aliasedType,
                    typeAliasMap,
                    erasingParams,
                    resolvingAliases,
                    scope,
                    namespacePrefix,
                    preserveGenericParameters);
                return true;
            }
            finally
            {
                resolvingAliases.Remove(symbol);
            }
        }

        private static bool IsRelativeClassKeyword(string name)
            => string.Equals(name, "self", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "static", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "parent", StringComparison.OrdinalIgnoreCase);

        private static GenericTypeParameterSymbol? TryGetGenericTypeParameter(PhpNamedTypeAst namedType)
        {
            if (namedType.BoundSymbol is GenericTypeParameterSymbol fromNamed)
            {
                return fromNamed;
            }

            return namedType.Name switch
            {
                // TyhpGenericIdentifierAst subclasses PhpNameAst — one arm covers both.
                PhpNameAst { BoundSymbol: GenericTypeParameterSymbol fromName } => fromName,
                _ => null,
            };
        }

        private static bool IsSimplePhpTypeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith('?'))
            {
                return false;
            }

            return !name.Contains('|') && !name.Contains('&') && !name.Contains('<');
        }

        private static string EraseGenericParameter(
            GenericTypeParameterSymbol genericParam,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix)
        {
            if (genericParam.Constraint == null)
            {
                return "mixed";
            }

            // Guard against cyclic constraints (e.g. `<T extends U, U extends T>`): once we are
            // already erasing this parameter, stop and fall back to `mixed`.
            erasingParams ??= [];
            if (!erasingParams.Add(genericParam))
            {
                return "mixed";
            }

            try
            {
                var constraintSpelling = Spell(genericParam.Constraint, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix);
                if (string.IsNullOrWhiteSpace(constraintSpelling))
                {
                    return "mixed";
                }

                // Intersection constraints are normalized in SpellComposite to PHP-legal forms
                // (e.g. `object&TProperties` → `object`, `Foo&Bar` kept). Do not collapse every
                // `&` to `mixed` — that erased valid class intersections and structural object
                // bounds used by ObjectHelper::with.
                return constraintSpelling;
            }
            finally
            {
                erasingParams.Remove(genericParam);
            }
        }

        /// <summary>
        /// PHP intersection types allow only class/interface names. Prefer those when present;
        /// otherwise fall back to a single useful builtin (<c>object</c>, then <c>array</c>,
        /// <c>callable</c>, <c>iterable</c>) so structural bounds like <c>object&amp;Struct</c>
        /// erase to <c>object</c> instead of <c>mixed</c>.
        /// </summary>
        private static List<string> NormalizePhpIntersectionParts(List<string> parts)
        {
            var classLike = parts.Where(IsPhpIntersectionClassLikeMember).ToList();
            if (classLike.Count > 0)
            {
                return classLike;
            }

            static bool Has(List<string> list, string name)
                => list.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));

            if (Has(parts, "object"))
            {
                return ["object"];
            }

            if (Has(parts, "array"))
            {
                return ["array"];
            }

            if (Has(parts, "callable"))
            {
                return ["callable"];
            }

            if (Has(parts, "iterable"))
            {
                return ["iterable"];
            }

            return ["mixed"];
        }

        /// <summary>
        /// True when <paramref name="part"/> is a legal PHP intersection member (class/interface
        /// name). Builtins such as <c>object</c>/<c>array</c>/<c>callable</c> are not legal.
        /// </summary>
        private static bool IsPhpIntersectionClassLikeMember(string part)
        {
            if (string.IsNullOrWhiteSpace(part)
                || part.StartsWith('?')
                || part.Contains('|')
                || part.Contains('&')
                || part.Contains('<'))
            {
                return false;
            }

            if (IsPhpIntersectionIllegalBuiltin(part))
            {
                return false;
            }

            // Relative class keywords are also illegal in PHP intersections.
            if (IsRelativeClassKeyword(part))
            {
                return false;
            }

            return true;
        }

        private static bool IsPhpIntersectionIllegalBuiltin(string part)
        {
            return part.ToLowerInvariant() switch
            {
                "object" or "array" or "callable" or "iterable"
                    or "mixed" or "void" or "never" or "null"
                    or "true" or "false"
                    or "int" or "float" or "string" or "bool"
                    or "decimal" => true,
                _ => false,
            };
        }

        private static string SpellBuiltinType(
            PhpBuiltinTypeAst builtinType,
            IReadOnlyDictionary<string, string>? typeAliasMap)
        {
            var name = builtinType.Identifier ?? "";

            return SpellBuiltinName(name, typeAliasMap);
        }

        private static string SpellBuiltinName(
            string name,
            IReadOnlyDictionary<string, string>? typeAliasMap)
        {
            if (string.Equals(name, "decimal", StringComparison.OrdinalIgnoreCase))
            {
                return @"\Tyhp\Decimal";
            }

            if (string.Equals(name, "struct", StringComparison.OrdinalIgnoreCase))
            {
                return "array";
            }

            // Static-value literals are compile-time only; PHP has no `'red'` / `42` type hints,
            // so widen to the underlying scalar (`true`/`false`/`null` stay as registered builtins).
            if (StaticValueTypeHelper.TryGetUnderlyingBuiltinName(name, out var underlyingName))
            {
                return underlyingName;
            }

            if (typeAliasMap != null
                && typeAliasMap.TryGetValue(name, out var aliased)
                && !string.IsNullOrWhiteSpace(aliased))
            {
                return aliased;
            }

            return name;
        }

        private static string SpellCheckedType(
            ICheckedType? type,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix)
        {
            if (type is null || type.Kind == CheckedTypeKind.Unresolved)
            {
                return "";
            }

            switch (type)
            {
                case SpecialCheckedType special when special.IsMixed:
                    return "mixed";
                case SpecialCheckedType special when special.IsVoid:
                    return "void";
                case SpecialCheckedType special when special.IsNever:
                    return "never";
                case NullableCheckedType nullable:
                    return SpellCheckedNullable(
                        nullable.InnerType, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix);
                case UnionCheckedType union:
                    return SpellCheckedUnion(
                        union.Members, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix);
                case IntersectionCheckedType intersection:
                    return SpellCheckedIntersection(
                        intersection.Members, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix);
                case CallableCheckedType:
                    return "callable";
                case StructCheckedType:
                    return "array";
                case LiteralCheckedType literal:
                    return SpellCheckedLiteral(
                        literal, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix);
                case TemplateStringCheckedType:
                    return "string";
                case GenericCheckedType generic:
                    return SpellCheckedGeneric(
                        generic, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix);
                case SimpleCheckedType simple:
                    return SpellCheckedSimple(
                        simple, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix);
                default:
                    return "";
            }
        }

        private static string SpellCheckedNullable(
            ICheckedType inner,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix)
        {
            var spelled = SpellCheckedType(
                inner, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix);
            if (string.IsNullOrWhiteSpace(spelled) || spelled == "mixed")
            {
                // `?mixed` is illegal in PHP — bare mixed already includes null.
                return spelled;
            }

            if (IsSimplePhpTypeName(spelled))
            {
                return "?" + spelled;
            }

            return spelled + " | null";
        }

        private static string SpellCheckedUnion(
            IReadOnlyList<ICheckedType> members,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix)
        {
            var parts = members
                .Select(m => SpellCheckedType(
                    m, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (parts.Count == 0)
            {
                return "";
            }

            if (parts.Any(p => p == "mixed"))
            {
                return "mixed";
            }

            if (parts.Count == 2)
            {
                var nullIndex = parts.FindIndex(p => string.Equals(p, "null", StringComparison.OrdinalIgnoreCase));
                if (nullIndex >= 0)
                {
                    var other = parts[1 - nullIndex];
                    if (IsSimplePhpTypeName(other))
                    {
                        return "?" + other;
                    }
                }
            }

            return parts.Count == 1 ? parts[0] : string.Join(" | ", parts);
        }

        private static string SpellCheckedIntersection(
            IReadOnlyList<ICheckedType> members,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix)
        {
            var parts = members
                .Select(m => SpellCheckedType(
                    m, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (parts.Count == 0)
            {
                return "";
            }

            if (parts.Any(p => p == "mixed"))
            {
                return "mixed";
            }

            parts = NormalizePhpIntersectionParts(parts);
            return parts.Count == 1 ? parts[0] : string.Join(" & ", parts);
        }

        private static string SpellCheckedLiteral(
            LiteralCheckedType literal,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix)
        {
            if (literal.Value is null)
            {
                return "null";
            }

            if (literal.Value is bool)
            {
                return "bool";
            }

            if (literal.Value is long or int)
            {
                return "int";
            }

            if (literal.Value is double or float)
            {
                return "float";
            }

            if (literal.Value is string)
            {
                return "string";
            }

            return SpellCheckedType(
                literal.UnderlyingType, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix);
        }

        private static string SpellCheckedGeneric(
            GenericCheckedType generic,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix)
        {
            if (TryEraseCheckedUtilityBrand(generic, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix, out var erased))
            {
                return erased;
            }

            // Erase type arguments: `array<…>` / `Promise<…>` → `array` / `\Tyhp\Promise`.
            return SpellCheckedType(
                generic.BaseType, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix);
        }

        private static string SpellCheckedSimple(
            SimpleCheckedType simple,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix)
        {
            if (simple.ResolvedSymbol is GenericTypeParameterSymbol genericParam)
            {
                return EraseCheckedGenericParameter(
                    genericParam, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix);
            }

            if (simple.ResolvedSymbol is TypeAliasSymbol alias)
            {
                if (resolvingAliases is not null && !resolvingAliases.Add(alias))
                {
                    return "mixed";
                }

                resolvingAliases ??= [];
                resolvingAliases.Add(alias);
                try
                {
                    if (alias.AliasedType is not null)
                    {
                        return Spell(
                            alias.AliasedType,
                            typeAliasMap,
                            erasingParams,
                            resolvingAliases,
                            scope,
                            namespacePrefix);
                    }
                }
                finally
                {
                    resolvingAliases.Remove(alias);
                }

                return "mixed";
            }

            if (simple.ResolvedSymbol is ObjectDeclarationSymbol { IsStruct: true })
            {
                return "array";
            }

            if (TryEraseCheckedUtilityBrand(simple, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix, out var erased))
            {
                return erased;
            }

            if (simple.ResolvedSymbol is BuiltInTypeSymbol builtIn)
            {
                return SpellBuiltinName(builtIn.Name, typeAliasMap);
            }

            if (simple.ResolvedSymbol is IBaseSymbol bound
                && !string.IsNullOrWhiteSpace(bound.FullyQualifiedName))
            {
                return EmittedFqnHelper.Format(bound.FullyQualifiedName, namespacePrefix, bound);
            }

            return SpellBuiltinName(simple.DisplayName.TrimStart('\\'), typeAliasMap);
        }

        private static string EraseCheckedGenericParameter(
            GenericTypeParameterSymbol genericParam,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix)
        {
            erasingParams ??= [];
            if (!erasingParams.Add(genericParam))
            {
                return "mixed";
            }

            try
            {
                if (genericParam.ResolvedConstraint is not null)
                {
                    var fromResolved = SpellCheckedType(
                        genericParam.ResolvedConstraint,
                        typeAliasMap,
                        erasingParams,
                        resolvingAliases,
                        scope,
                        namespacePrefix);
                    if (!string.IsNullOrWhiteSpace(fromResolved))
                    {
                        return fromResolved;
                    }
                }

                if (genericParam.Constraint is not null)
                {
                    var fromAst = Spell(
                        genericParam.Constraint,
                        typeAliasMap,
                        erasingParams,
                        resolvingAliases,
                        scope,
                        namespacePrefix);
                    if (!string.IsNullOrWhiteSpace(fromAst))
                    {
                        return fromAst;
                    }
                }

                return "mixed";
            }
            finally
            {
                erasingParams.Remove(genericParam);
            }
        }

        private static bool TryEraseCheckedUtilityBrand(
            ICheckedType type,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix,
            out string spelling)
        {
            spelling = "";
            if (!SymbolNameTypeHelper.TryGetUtilitySymbol(type, out var utility))
            {
                // Algebra brands share the BuiltInUtilityTypeSymbol path via TryGetUtilitySymbol
                // when present; otherwise probe the simple/generic base symbol directly.
                var symbol = type switch
                {
                    SimpleCheckedType s => s.ResolvedSymbol,
                    GenericCheckedType { BaseType: SimpleCheckedType baseSimple } => baseSimple.ResolvedSymbol,
                    _ => null,
                };
                if (symbol is not BuiltInUtilityTypeSymbol algebraUtility
                    || !TypeNameAlgebraResolver.IsTypeNameAlgebraBehavior(algebraUtility.Behavior))
                {
                    return false;
                }

                if (algebraUtility.Behavior == UtilityBehavior.AsType
                    && type is GenericCheckedType { TypeArguments.Count: > 0 } asTypeGeneric)
                {
                    spelling = SpellCheckedType(
                        asTypeGeneric.TypeArguments[0],
                        typeAliasMap,
                        erasingParams,
                        resolvingAliases,
                        scope,
                        namespacePrefix);
                    if (string.IsNullOrWhiteSpace(spelling))
                    {
                        spelling = "mixed";
                    }

                    return true;
                }

                spelling = "string";
                return true;
            }

            if (utility.Behavior == UtilityBehavior.TyhpInternal
                && type is GenericCheckedType { TypeArguments.Count: > 0 } internalGeneric)
            {
                spelling = SpellCheckedType(
                    internalGeneric.TypeArguments[0],
                    typeAliasMap,
                    erasingParams,
                    resolvingAliases,
                    scope,
                    namespacePrefix);
                if (string.IsNullOrWhiteSpace(spelling))
                {
                    spelling = "mixed";
                }

                return true;
            }

            if (SymbolNameTypeHelper.IsSymbolNameBehavior(utility.Behavior))
            {
                spelling = "string";
                return true;
            }

            if (TypeNameAlgebraResolver.IsTypeNameAlgebraBehavior(utility.Behavior))
            {
                if (utility.Behavior == UtilityBehavior.AsType
                    && type is GenericCheckedType { TypeArguments.Count: > 0 } asType)
                {
                    spelling = SpellCheckedType(
                        asType.TypeArguments[0],
                        typeAliasMap,
                        erasingParams,
                        resolvingAliases,
                        scope,
                        namespacePrefix);
                    if (string.IsNullOrWhiteSpace(spelling))
                    {
                        spelling = "mixed";
                    }

                    return true;
                }

                spelling = "string";
                return true;
            }

            // Phase 5 / \Tyhp\* utilities — spell the resolved PHP surface from CheckedType args.
            if (TryEraseCheckedPhase5Utility(utility.Behavior, type, typeAliasMap, erasingParams, resolvingAliases, scope, namespacePrefix, out spelling))
            {
                return true;
            }

            return false;
        }

        private static bool TryEraseCheckedPhase5Utility(
            UtilityBehavior behavior,
            ICheckedType type,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            HashSet<GenericTypeParameterSymbol>? erasingParams,
            HashSet<IBaseSymbol>? resolvingAliases,
            IBaseScope? scope,
            string? namespacePrefix,
            out string spelling)
        {
            spelling = "";
            var typeArgs = type is GenericCheckedType generic ? generic.TypeArguments : [];

            switch (behavior)
            {
                case UtilityBehavior.StructKey:
                case UtilityBehavior.Properties:
                    spelling = "string";
                    return true;

                case UtilityBehavior.StructRecord:
                    spelling = "mixed";
                    return true;

                case UtilityBehavior.StructDef:
                case UtilityBehavior.StructPartial:
                case UtilityBehavior.Record:
                case UtilityBehavior.Pick:
                case UtilityBehavior.Omit:
                case UtilityBehavior.Partial:
                case UtilityBehavior.Required:
                case UtilityBehavior.Parameters:
                case UtilityBehavior.CallableParametersStruct:
                case UtilityBehavior.CallableParametersTuple:
                    spelling = "array";
                    return true;

                case UtilityBehavior.CallableParametersRest:
                    spelling = "mixed";
                    return true;

                case UtilityBehavior.Readonly:
                case UtilityBehavior.NonNullable:
                case UtilityBehavior.AsNotNullable:
                case UtilityBehavior.Awaited:
                case UtilityBehavior.AsReadOnly:
                case UtilityBehavior.FunctionReturnType:
                case UtilityBehavior.Exclude:
                case UtilityBehavior.Extract:
                case UtilityBehavior.TypeDiff:
                    if (typeArgs.Count == 0)
                    {
                        spelling = "mixed";
                        return true;
                    }

                    spelling = SpellCheckedType(
                        typeArgs[0],
                        typeAliasMap,
                        erasingParams,
                        resolvingAliases,
                        scope,
                        namespacePrefix);
                    if (string.IsNullOrWhiteSpace(spelling))
                    {
                        spelling = "mixed";
                    }

                    return true;

                case UtilityBehavior.Nullable:
                case UtilityBehavior.AsNullable:
                {
                    if (typeArgs.Count == 0)
                    {
                        spelling = "mixed";
                        return true;
                    }

                    var inner = SpellCheckedType(
                        typeArgs[0],
                        typeAliasMap,
                        erasingParams,
                        resolvingAliases,
                        scope,
                        namespacePrefix);
                    if (string.IsNullOrWhiteSpace(inner))
                    {
                        inner = "mixed";
                    }

                    spelling = inner.StartsWith('?') || string.Equals(inner, "mixed", StringComparison.Ordinal)
                        ? inner
                        : "?" + inner;
                    return true;
                }

                case UtilityBehavior.ReturnType:
                case UtilityBehavior.CallableReturnType:
                    if (typeArgs.Count == 0)
                    {
                        spelling = "mixed";
                        return true;
                    }

                    // Mirror UtilityTypeResolver.ResolveReturnType: facet / Closure / callable
                    // generics / CallableCheckedType all expose a return slot via reflection.
                    if (CallableSignatureReflection.TryGetReturnType(typeArgs[0], out var returnType))
                    {
                        spelling = SpellCheckedType(
                            returnType,
                            typeAliasMap,
                            erasingParams,
                            resolvingAliases,
                            scope,
                            namespacePrefix);
                        if (string.IsNullOrWhiteSpace(spelling))
                        {
                            spelling = "mixed";
                        }

                        return true;
                    }

                    spelling = "mixed";
                    return true;

                case UtilityBehavior.MethodReturnType:
                    spelling = "mixed";
                    return true;

                default:
                    return false;
            }
        }
    }
}
