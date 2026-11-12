using System.Globalization;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// PHP array key used when erasing a struct property: either a string key or a
    /// decimal integer key (<c>mixed 0 as $arg1</c>).
    /// </summary>
    internal readonly record struct StructArrayKey(string Text, bool IsInteger)
    {
        public PhpScalarAst ToScalarAst(Base2Ast context)
        {
            if (IsInteger
                && long.TryParse(
                    Text.Replace("_", string.Empty),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var numericKey))
            {
                return PhpScalarAst.CreateIntegerFromContext(context, numericKey);
            }

            return PhpScalarAst.CreateStringFromContext(context, Text);
        }
    }

    /// <summary>
    /// Rewrites compile-time struct construction/access into PHP associative array operations.
    /// </summary>
    internal static class StructEmissionHelper
    {
        public static bool IsWithOperator(TokenValueAst? op)
        {
            if (op is null)
            {
                return false;
            }

            if (op.ValueInt64 is long tokenType && tokenType == TyhpParser.T_TYHP_WITH)
            {
                return true;
            }

            return string.Equals(op.ValueString, "with", StringComparison.OrdinalIgnoreCase)
                || string.Equals(op.Identifier, "with", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsCloneOperator(TokenValueAst? op)
        {
            if (op is null)
            {
                return false;
            }

            if (op.ValueInt64 is long tokenType && tokenType == TyhpParser.T_CLONE)
            {
                return true;
            }

            return string.Equals(op.ValueString, "clone", StringComparison.OrdinalIgnoreCase)
                || string.Equals(op.Identifier, "clone", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// <c>clone $s</c> on a struct is a no-op (PHP arrays copy on assignment).
        /// </summary>
        public static bool TryRewriteStructClone(
            PhpUnaryOpAst unary,
            Func<IExpression?, ObjectDeclarationSymbol?> resolveStructType,
            Func<IExpression?, StructCheckedType?>? resolveAnonymousShape,
            out IBase2Ast rewritten)
        {
            rewritten = unary;

            if (!unary.IsPrefix
                || !IsCloneOperator(unary.Operator)
                || unary.Operand is null
                || (resolveStructType(unary.Operand) is not { IsStruct: true }
                    && resolveAnonymousShape?.Invoke(unary.Operand) is null))
            {
                return false;
            }

            rewritten = unary.Operand;
            return true;
        }

        /// <summary>
        /// PHP array key for <paramref name="memberName"/> on an anonymous struct type
        /// (<c>struct { … }</c> in type position): the integer or string alias when declared,
        /// otherwise the member name. Null when the shape has no such property.
        /// </summary>
        public static StructArrayKey? ResolveAnonymousStructKey(StructCheckedType shape, string? memberName)
        {
            if (string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            var lookupKey = memberName.StartsWith('$') ? memberName : "$" + memberName;
            if (!shape.Properties.TryGetValue(lookupKey, out var info)
                && !shape.Properties.TryGetValue(memberName, out info))
            {
                return null;
            }

            if (info.IntegerKeyAlias is int integerKey)
            {
                return new StructArrayKey(integerKey.ToString(CultureInfo.InvariantCulture), IsInteger: true);
            }

            return new StructArrayKey(info.StringKeyAlias ?? NormalizePropertyKey(lookupKey), IsInteger: false);
        }

        /// <summary>
        /// Rewrites struct <c>with</c> forms.
        /// <c>new Struct() with [...]</c> and <c>new Struct[...]</c> with no spread fold defaults
        /// and overrides into a single array literal. A spread is one <c>\array_replace</c>
        /// argument per run (the defaults array, then each spread source, with each run of
        /// consecutive keyed entries merged into one array literal) so integer keys are not
        /// renumbered. <c>$s with</c> / <c>clone $s with</c> use the same runs with the base
        /// value in place of the defaults array, because that value is not known statically.
        /// </summary>
        public static bool TryRewriteStructWith(
            PhpBinaryOpAst binary,
            Func<PhpNewAst, ObjectDeclarationSymbol?> resolveStructFromNew,
            Func<IExpression?, ObjectDeclarationSymbol?> resolveStructType,
            Func<IExpression?, StructCheckedType?>? resolveAnonymousShape,
            Func<IExpression, IExpression>? transformDefault,
            Func<IExpression, IReadOnlySet<string>>? guaranteedSpreadKeys,
            out IBase2Ast rewritten)
        {
            rewritten = binary;

            if (!IsWithOperator(binary.Operator)
                || binary.Right is not PhpArrayPairListAst pairList)
            {
                return false;
            }

            if (binary.Left is PhpNewAst newExpr
                && resolveStructFromNew(newExpr) is { IsStruct: true } newStruct)
            {
                if (HasExpansion(pairList))
                {
                    rewritten = RewriteSpreadWith(
                        leading: null,
                        CreateDefaultsArray(newStruct, binary, transformDefault),
                        pairList,
                        memberName => ResolveStructPropertyKey(newStruct, memberName),
                        guaranteedSpreadKeys,
                        binary);
                    return true;
                }

                var defaults = CreateDefaultsArray(newStruct, binary, transformDefault);
                var overrides = CreateArrayFromWithList(pairList, newStruct, binary);
                rewritten = MergeArrayPairs(defaults, overrides, binary);
                return true;
            }

            IExpression? baseExpr = binary.Left;
            if (binary.Left is PhpUnaryOpAst unary
                && unary.IsPrefix
                && IsCloneOperator(unary.Operator))
            {
                baseExpr = unary.Operand;
            }

            if (baseExpr is null)
            {
                return false;
            }

            Func<string, StructArrayKey?>? resolveKey = null;
            if (resolveStructType(baseExpr) is { IsStruct: true } baseStruct)
            {
                resolveKey = memberName => ResolveStructPropertyKey(baseStruct, memberName);
            }
            else if (resolveAnonymousShape?.Invoke(baseExpr) is { } baseShape)
            {
                resolveKey = memberName => ResolveAnonymousStructKey(baseShape, memberName);
            }

            if (resolveKey is null)
            {
                return false;
            }

            if (HasExpansion(pairList))
            {
                rewritten = RewriteSpreadWith(
                    baseExpr,
                    defaults: null,
                    pairList,
                    resolveKey,
                    guaranteedSpreadKeys,
                    binary);
                return true;
            }

            var runtimeOverrides = CreateArrayFromWithList(pairList, resolveKey, binary);
            rewritten = CreateArrayReplaceCall(baseExpr, runtimeOverrides, binary);
            return true;
        }

        public static bool TryRewriteStructNew(
            PhpNewAst newExpr,
            Func<PhpNewAst, ObjectDeclarationSymbol?> resolveStruct,
            Func<IExpression, IExpression>? transformDefault,
            out IBase2Ast rewritten)
        {
            rewritten = newExpr;

            if (resolveStruct(newExpr) is not { IsStruct: true } structDecl)
            {
                return false;
            }

            rewritten = CreateDefaultsArray(structDecl, newExpr, transformDefault);
            return true;
        }

        /// <summary>
        /// The struct's folded default array. <paramref name="transformDefault"/> rewrites a default
        /// value expression the way the surrounding emit walk would have (declaration defaults are
        /// never walked in place), so <c>Inner $inner = new Inner();</c> becomes Inner's own
        /// defaults array instead of a bare <c>new Inner()</c>.
        /// </summary>
        public static PhpArrayAst CreateDefaultsArray(
            ObjectDeclarationSymbol structDecl,
            Base2Ast context,
            Func<IExpression, IExpression>? transformDefault = null)
        {
            var pairs = EnumerateStructHierarchy(structDecl)
                .SelectMany(decl => decl.Members.Values.OfType<ObjectPropertySymbol>())
                .Select(property => CreateDefaultPair(property, context, transformDefault))
                .Where(pair => pair is not null)
                .Cast<PhpArrayPairAst>()
                .ToList();

            return CreateArrayFromPairs(pairs, context);
        }

        public static IBase2Ast CreateArrayReplaceCall(
            IExpression baseExpr,
            IExpression overridesExpr,
            Base2Ast context)
            => CreateArrayReplaceCall([baseExpr, overridesExpr], context);

        public static IBase2Ast CreateArrayReplaceCall(
            IReadOnlyList<IExpression> arguments,
            Base2Ast context)
        {
            var args = PhpArgumentListAst.Create(
                arguments.Select(argument => PhpArgumentAst.CreateFromContext(argument, context)),
                context);

            return PhpDereferenceableAst.CreateFromContext(
                PhpNameAst.CreateFromContext(@"\array_replace", context),
                PhpCallAst.CreateFromContext(args, context),
                context);
        }

        /// <summary>
        /// Array keys a spread source is known to contain. A required field and a field with a
        /// default count. A nullable field with no default does not: the key may be missing.
        /// Unknown sources and unions contribute no keys, so the target's default stays.
        /// </summary>
        public static IReadOnlySet<string> CollectGuaranteedSpreadKeys(
            ICheckedType? checkedType,
            ObjectDeclarationSymbol? declaration)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var type = checkedType;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is null || type.IsMixed || type is UnionCheckedType or IntersectionCheckedType)
            {
                return keys;
            }

            if (type is StructCheckedType shape)
            {
                AddGuaranteedShapeKeys(shape, keys);
                return keys;
            }

            var structDecl = type is not null
                && TypeComparer.TryGetObjectDeclaration(type) is { IsStruct: true } fromType
                ? fromType
                : declaration is { IsStruct: true } named
                    ? named
                    : null;
            if (structDecl is null)
            {
                return keys;
            }

            var arguments = type is GenericCheckedType generic ? generic.TypeArguments : null;
            AddGuaranteedDeclarationKeys(structDecl, arguments, keys);
            return keys;
        }

        /// <summary>
        /// <c>\array_replace</c> arguments for a struct initializer or struct <c>with</c> list that
        /// contains a spread. Each spread source is one argument (evaluated once). Consecutive
        /// keyed entries collapse into one array literal. A <c>new</c> passes its defaults array
        /// as <paramref name="defaults"/> and no leading value; <c>clone</c> / in-place pass the
        /// base value as <paramref name="leading"/> and no defaults array. A default key is
        /// omitted when a spread guarantees it or a keyed entry sets it. An empty defaults array
        /// is omitted entirely.
        /// </summary>
        private static IBase2Ast RewriteSpreadWith(
            IExpression? leading,
            PhpArrayAst? defaults,
            PhpArrayPairListAst pairList,
            Func<string, StructArrayKey?> resolveKey,
            Func<IExpression, IReadOnlySet<string>>? guaranteedSpreadKeys,
            Base2Ast context)
        {
            var covered = new HashSet<string>(StringComparer.Ordinal);
            var runs = new List<IExpression>();
            var keyed = new List<PhpArrayPairAst>();

            void FlushKeyed()
            {
                if (keyed.Count == 0)
                {
                    return;
                }

                runs.Add(CreateArrayFromPairs(keyed, context));
                keyed.Clear();
            }

            foreach (var pair in pairList.GetAllExcludingSkippedSlots())
            {
                if (pair.IsExpansion)
                {
                    FlushKeyed();
                    if (pair.ValueExpr is not { } source)
                    {
                        continue;
                    }

                    runs.Add(source);
                    if (guaranteedSpreadKeys is not null)
                    {
                        foreach (var key in guaranteedSpreadKeys(source))
                        {
                            covered.Add(key);
                        }
                    }

                    continue;
                }

                var normalized = NormalizeArrayPair(pair, resolveKey, context);
                keyed.Add(normalized);
                if (normalized.KeyExpr is not null)
                {
                    covered.Add(GetArrayKeyText(normalized.KeyExpr));
                }
            }

            FlushKeyed();

            var arguments = new List<IExpression>();
            if (leading is not null)
            {
                arguments.Add(leading);
            }

            if (defaults is not null)
            {
                var kept = new List<PhpArrayPairAst>();
                foreach (var pair in defaults.ArrayPairs?.GetAllExcludingSkippedSlots() ?? [])
                {
                    var key = pair.KeyExpr is null ? null : GetArrayKeyText(pair.KeyExpr);
                    if (key is not null && covered.Contains(key))
                    {
                        continue;
                    }

                    kept.Add(pair);
                }

                if (kept.Count > 0)
                {
                    arguments.Add(CreateArrayFromPairs(kept, context));
                }
            }

            arguments.AddRange(runs);
            if (runs.Count == 0)
            {
                return arguments.Count switch
                {
                    0 => CreateArrayFromPairs([], context),
                    1 => arguments[0],
                    _ => CreateArrayReplaceCall(arguments, context),
                };
            }

            return CreateArrayReplaceCall(arguments, context);
        }

        private static bool HasExpansion(PhpArrayPairListAst pairList) =>
            pairList.GetAllExcludingSkippedSlots().Any(pair => pair.IsExpansion);

        private static void AddGuaranteedShapeKeys(StructCheckedType shape, HashSet<string> keys)
        {
            foreach (var (memberName, info) in shape.Properties)
            {
                if (info.IsOptional || CheckedTypeMayBeAbsent(info.Type))
                {
                    continue;
                }

                keys.Add(ShapeArrayKeyText(memberName, info));
            }
        }

        private static void AddGuaranteedDeclarationKeys(
            ObjectDeclarationSymbol structDecl,
            IReadOnlyList<ICheckedType>? arguments,
            HashSet<string> keys)
        {
            foreach (var level in EnumerateStructHierarchy(structDecl))
            {
                foreach (var property in level.Members.Values.OfType<ObjectPropertySymbol>())
                {
                    var key = CanonicalArrayKeyText(GetStructArrayKey(property));
                    var guaranteed = property.DefaultValue is not null
                        || !TypeMayBeAbsent(property.DeclaredType, level, structDecl, arguments);
                    if (guaranteed)
                    {
                        keys.Add(key);
                    }
                    else
                    {
                        keys.Remove(key);
                    }
                }
            }
        }

        private static string ShapeArrayKeyText(string memberName, StructPropertyInfo info)
        {
            if (info.IntegerKeyAlias is int integerKey)
            {
                return integerKey.ToString(CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrEmpty(info.StringKeyAlias))
            {
                return info.StringKeyAlias;
            }

            return memberName.StartsWith('$') ? memberName[1..] : memberName;
        }

        private static string CanonicalArrayKeyText(StructArrayKey key)
        {
            if (!key.IsInteger)
            {
                return key.Text;
            }

            if (long.TryParse(
                    key.Text.Replace("_", string.Empty),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var numericKey))
            {
                return numericKey.ToString(CultureInfo.InvariantCulture);
            }

            return key.Text;
        }

        /// <summary>
        /// True when a source field of this type may be missing from the array. A default on the
        /// property is handled by the caller and counts as present even when the type is nullable.
        /// </summary>
        private static bool TypeMayBeAbsent(
            ITypeExpression? type,
            ObjectDeclarationSymbol level,
            ObjectDeclarationSymbol instantiated,
            IReadOnlyList<ICheckedType>? arguments)
        {
            if (type is null)
            {
                return true;
            }

            if (type is PhpTypeExpressionAst composite)
            {
                if (composite.IsNullable)
                {
                    return true;
                }

                var parts = TypeParts(composite);
                if (composite.TypeKind == PhpTypeKind.Union && parts.Any(IsNullTypeExpression))
                {
                    return true;
                }

                if (composite.TypeKind == PhpTypeKind.Simple && parts.Count == 1)
                {
                    return TypeMayBeAbsent(parts[0], level, instantiated, arguments);
                }

                return false;
            }

            if (IsNullTypeExpression(type))
            {
                return true;
            }

            if (!TryReadTypeParameter(type, level, out var parameter))
            {
                return false;
            }

            if (!ReferenceEquals(level, instantiated) || arguments is null)
            {
                return true;
            }

            var index = instantiated.GenericParameters.FindIndex(candidate => ReferenceEquals(candidate, parameter));
            if (index < 0 || index >= arguments.Count)
            {
                return true;
            }

            return CheckedTypeMayBeAbsent(arguments[index]);
        }

        private static bool CheckedTypeMayBeAbsent(ICheckedType type)
        {
            if (type is NullableCheckedType || CheckerHelpers.IsNullType(type))
            {
                return true;
            }

            if (type is UnionCheckedType union)
            {
                foreach (var member in union.Members)
                {
                    if (CheckedTypeMayBeAbsent(member))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static List<ITypeExpression> TypeParts(PhpTypeExpressionAst composite)
        {
            var parts = new List<ITypeExpression>();
            if (composite.Types is null)
            {
                return parts;
            }

            foreach (var part in composite.Types.GetAllNotNull())
            {
                if (part is ITypeExpression typePart)
                {
                    parts.Add(typePart);
                }
            }

            return parts;
        }

        private static bool IsNullTypeExpression(ITypeExpression type)
        {
            var written = type switch
            {
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                PhpNameAst name => name.ValueString ?? name.Identifier,
                PhpNamedTypeAst { Name: PhpBuiltinTypeAst builtin } => builtin.Identifier,
                PhpNamedTypeAst { Name: PhpNameAst name } => name.ValueString ?? name.Identifier,
                _ => null,
            };

            return string.Equals(written?.TrimStart('\\'), "null", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryReadTypeParameter(
            ITypeExpression type,
            ObjectDeclarationSymbol level,
            out GenericTypeParameterSymbol? parameter)
        {
            parameter = null;
            IBaseSymbol? bound = null;
            string? written = null;
            switch (type)
            {
                case PhpNameAst nameAst:
                    bound = nameAst.BoundSymbol;
                    written = nameAst.ValueString ?? nameAst.Identifier;
                    break;
                case PhpNamedTypeAst named:
                    bound = named.BoundSymbol;
                    if (named.Name is PhpNameAst innerName)
                    {
                        bound ??= innerName.BoundSymbol;
                        written = innerName.ValueString ?? innerName.Identifier;
                    }

                    break;
                default:
                    return false;
            }

            if (bound is GenericTypeParameterSymbol boundParameter)
            {
                parameter = boundParameter;
                return true;
            }

            if (bound is not null || string.IsNullOrEmpty(written))
            {
                return false;
            }

            var bare = written.TrimStart('\\');
            foreach (var candidate in level.GenericParameters)
            {
                if (string.Equals(candidate.Name, bare, StringComparison.Ordinal))
                {
                    parameter = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Compile-time merge of struct defaults with a <c>with</c> override list into one
        /// short-array literal. Default key order is preserved; overridden keys keep their
        /// position; keys present only in overrides are appended. Skip-slot pairs (trailing-comma
        /// artifacts) are dropped: keyed struct fields never use destructuring skips, and appending
        /// an override after a trailing skip would otherwise turn it into an interior empty PHP
        /// element that <c>BuildArrayExpression</c> cannot distinguish from <c>[, $b]</c>.
        /// </summary>
        public static PhpArrayAst MergeArrayPairs(
            PhpArrayAst defaults,
            PhpArrayAst overrides,
            Base2Ast context)
        {
            var merged = new List<PhpArrayPairAst>();
            var indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var pair in defaults.ArrayPairs?.GetAllExcludingSkippedSlots() ?? [])
            {
                var key = pair.KeyExpr is null ? null : GetArrayKeyText(pair.KeyExpr);
                if (key is not null)
                {
                    indexByKey[key] = merged.Count;
                }

                merged.Add(pair);
            }

            foreach (var pair in overrides.ArrayPairs?.GetAllExcludingSkippedSlots() ?? [])
            {
                var key = pair.KeyExpr is null ? null : GetArrayKeyText(pair.KeyExpr);
                if (key is not null && indexByKey.TryGetValue(key, out var existingIndex))
                {
                    merged[existingIndex] = pair;
                    continue;
                }

                if (key is not null)
                {
                    indexByKey[key] = merged.Count;
                }

                merged.Add(pair);
            }

            return CreateArrayFromPairs(merged, context);
        }

        private static PhpArrayAst CreateArrayFromWithList(
            PhpArrayPairListAst pairList,
            ObjectDeclarationSymbol structDecl,
            Base2Ast context)
            => CreateArrayFromWithList(
                pairList,
                memberName => ResolveStructPropertyKey(structDecl, memberName),
                context);

        private static PhpArrayAst CreateArrayFromWithList(
            PhpArrayPairListAst pairList,
            Func<string, StructArrayKey?> resolveKey,
            Base2Ast context)
        {
            var pairs = pairList.GetAllExcludingSkippedSlots()
                .Select(pair => NormalizeArrayPair(pair, resolveKey, context))
                .ToList();

            return CreateArrayFromPairs(pairs, context);
        }

        private static PhpArrayAst CreateArrayFromPairs(IReadOnlyList<PhpArrayPairAst> pairs, Base2Ast context)
        {
            var pairList = PhpArrayPairListAst.Create(pairs, context);
            return PhpArrayAst.CreateFromContext(pairList, isShortSyntax: true, context);
        }

        private static PhpArrayPairAst NormalizeArrayPair(
            PhpArrayPairAst pair,
            Func<string, StructArrayKey?> resolveKey,
            Base2Ast context)
        {
            if (pair.KeyExpr is null)
            {
                return pair;
            }

            var keyText = GetArrayKeyText(pair.KeyExpr);

            // A `with` key written as the property/alias name must resolve to the backing array
            // key (aliased properties store under their alias, not the member name).
            var resolvedKey = resolveKey(keyText)
                ?? new StructArrayKey(keyText, IsInteger: false);
            return PhpArrayPairAst.CreateFromContext(
                resolvedKey.ToScalarAst(context),
                pair.ValueExpr!,
                pair.IsExpansion,
                context);
        }

        private static PhpArrayPairAst? CreateDefaultPair(
            ObjectPropertySymbol property,
            Base2Ast context,
            Func<IExpression, IExpression>? transformDefault)
        {
            // Optional nullable properties without a default are omitted from default construction
            // (they "may or may not be present" in the array).
            if (property.DefaultValue is not { } defaultValue)
            {
                return null;
            }

            var key = GetStructArrayKey(property);
            return PhpArrayPairAst.CreateFromContext(
                key.ToScalarAst(context),
                transformDefault?.Invoke(defaultValue) ?? defaultValue,
                isExpansion: false,
                context);
        }

        /// <summary>
        /// PHP array key for a struct property: the unquoted alias when present
        /// (<c>string 'String Value' as $strVal</c> or <c>mixed 0 as $arg1</c>),
        /// otherwise the bare property name.
        /// </summary>
        public static StructArrayKey GetStructArrayKey(ObjectPropertySymbol property)
        {
            if (property.DeclaringAstNode is TyhpStructPropertyAst structProp
                && !string.IsNullOrEmpty(structProp.AliasOf))
            {
                if (structProp.IsNumericAlias)
                {
                    return new StructArrayKey(structProp.AliasOf, IsInteger: true);
                }

                return new StructArrayKey(UnquotePhpStringLiteral(structProp.AliasOf), IsInteger: false);
            }

            return new StructArrayKey(NormalizePropertyKey(property.Name), IsInteger: false);
        }

        /// <summary>
        /// Resolves <c>$s->strVal</c> to the PHP array key, honoring string and integer aliases.
        /// </summary>
        public static StructArrayKey? ResolveStructPropertyKey(
            ObjectDeclarationSymbol structDecl,
            string? memberName)
        {
            return TryResolveStructProperty(structDecl, memberName, out var property)
                ? GetStructArrayKey(property!)
                : null;
        }

        /// <summary>
        /// Finds the declared property (walking the inheritance chain) backing <c>$s-&gt;member</c>.
        /// Only declared properties erase to array keys; methods / extensions / unknown names must
        /// stay as member access so call rewrite and the checker can see them. Used by the emitter
        /// both to compute the array key and to resolve a nested struct property's own type (so a
        /// chained <c>$s-&gt;inner-&gt;x</c> keeps rewriting after the first hop erases to <c>[]</c>).
        /// </summary>
        public static bool TryResolveStructProperty(
            ObjectDeclarationSymbol structDecl,
            string? memberName,
            out ObjectPropertySymbol? property)
        {
            property = null;
            if (string.IsNullOrWhiteSpace(memberName))
            {
                return false;
            }

            var lookupKey = memberName.StartsWith('$') ? memberName : "$" + memberName;
            foreach (var decl in EnumerateStructHierarchy(structDecl))
            {
                if (decl.Members.TryGetValue(lookupKey, out var member)
                    && member is ObjectPropertySymbol found)
                {
                    property = found;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The struct's inheritance chain, root base first. Inherited properties are not flattened
        /// into <see cref="ObjectDeclarationSymbol.Members"/>, so backing-array defaults and alias
        /// lookups must walk the chain. Base-first ordering lets a derived declaration's default
        /// override an inherited one.
        /// </summary>
        public static IReadOnlyList<ObjectDeclarationSymbol> EnumerateStructHierarchy(
            ObjectDeclarationSymbol structDecl)
        {
            var visited = new HashSet<ObjectDeclarationSymbol>();
            var chain = new List<ObjectDeclarationSymbol>();
            for (var current = structDecl; current is not null; current = ResolveBaseStruct(current))
            {
                if (!visited.Add(current))
                {
                    break;
                }

                chain.Add(current);
            }

            chain.Reverse();
            return chain;
        }

        private static ObjectDeclarationSymbol? ResolveBaseStruct(ObjectDeclarationSymbol structDecl)
        {
            if (structDecl.ExtendsType is PhpNamedTypeAst named
                && ResolveStructFromNamedType(named, structDecl.ContainingScope) is { } fromNamedType)
            {
                return fromNamedType;
            }

            // `extends` is parsed as a raw IClassName rather than an ITypeExpression, so
            // ExtendsType is usually null on the symbol; fall back to the declaring AST.
            return GetExtendsName(structDecl) switch
            {
                PhpNameAst { BoundSymbol: ObjectDeclarationSymbol { IsStruct: true } bound } => bound,
                { } className => ResolveStructByName(className, structDecl.ContainingScope),
                _ => null,
            };
        }

        private static IClassName? GetExtendsName(ObjectDeclarationSymbol structDecl) =>
            StructExtends.FromDeclaringNode(structDecl.DeclaringAstNode);

        private static ObjectDeclarationSymbol? ResolveStructByName(IClassName className, IBaseScope? scope)
        {
            if (scope is null)
            {
                return null;
            }

            var text = className switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                _ => className.Identifier,
            };

            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return FindStructSymbol(scope, text.TrimStart('\\').Split('\\')[^1]);
        }

        private static string GetArrayKeyText(IExpression keyExpr) =>
            keyExpr switch
            {
                PhpNameAst name => name.ValueString ?? "",
                TokenValueAst token => token.ValueString ?? "",
                PhpScalarAst { ScalarType: PhpScalarType.Integer } scalar =>
                    scalar.ValueInt64?.ToString(CultureInfo.InvariantCulture)
                    ?? scalar.ValueString?.Replace("_", string.Empty)
                    ?? "",
                PhpScalarAst scalar => UnquotePhpStringLiteral(scalar.ValueString ?? ""),
                PhpEncapsStringAst encaps => UnquotePhpStringLiteral(
                    encaps.ValueString ?? encaps.TokenValue?.ValueString ?? ""),
                PhpEncapsListAst encapsList => GetEncapsListKeyText(encapsList),
                PhpBuiltinTypeAst builtin => builtin.Identifier ?? "",
                PhpNamedTypeAst named when named.Name is PhpNameAst typeName => typeName.ValueString ?? "",
                _ => keyExpr.Identifier ?? "",
            };

        private static string GetEncapsListKeyText(PhpEncapsListAst encapsList)
        {
            var parts = encapsList.GetAllNotNull().ToList();
            if (parts.Count == 1 && parts[0] is PhpEncapsStringAst encaps)
            {
                return UnquotePhpStringLiteral(encaps.ValueString ?? encaps.TokenValue?.ValueString ?? "");
            }

            return "";
        }

        private static string UnquotePhpStringLiteral(string literal)
        {
            if (literal.Length >= 2
                && ((literal[0] == '\'' && literal[^1] == '\'') || (literal[0] == '"' && literal[^1] == '"')))
            {
                return literal[1..^1].Replace("\\'", "'").Replace("\\\\", "\\");
            }

            return literal;
        }

        private static string NormalizePropertyKey(string propertyName) =>
            propertyName.StartsWith('$') ? propertyName[1..] : propertyName;

        public static ObjectDeclarationSymbol? ResolveStructFromNamedType(
            PhpNamedTypeAst namedType,
            IBaseScope? scope)
        {
            if (namedType.BoundSymbol is ObjectDeclarationSymbol { IsStruct: true } objectDecl)
            {
                return objectDecl;
            }

            if (namedType.Name is PhpNameAst name
                && name.BoundSymbol is ObjectDeclarationSymbol { IsStruct: true } nameObjectDecl)
            {
                return nameObjectDecl;
            }

            // A name the binder resolved to a class, built-in, or generic parameter is not a
            // struct, even when a struct with the same short name exists in another namespace.
            if (IsBoundToNonStructType(namedType.BoundSymbol)
                || (namedType.Name is PhpNameAst boundName && IsBoundToNonStructType(boundName.BoundSymbol)))
            {
                return null;
            }

            if (scope is null)
            {
                return null;
            }

            var typeName = GetNamedTypeText(namedType);
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            if (typeName.Contains('\\'))
            {
                var segments = typeName.TrimStart('\\').Split('\\');
                var simpleName = segments[^1];
                return FindStructSymbol(scope, simpleName);
            }

            return FindStructSymbol(scope, typeName);
        }

        /// <summary>
        /// Resolves an expression to a struct declaration symbol, including typed variables
        /// whose <see cref="VariableSymbol.DeclaredType"/> names a struct.
        /// </summary>
        public static ObjectDeclarationSymbol? ResolveStructTypeFromExpression(
            IExpression? expression,
            IBaseScope? scope)
        {
            if (expression is null)
            {
                return null;
            }

            if (expression.BoundSymbol is ObjectDeclarationSymbol { IsStruct: true } direct)
            {
                return direct;
            }

            if (expression.BoundSymbol is VariableSymbol { DeclaredType: PhpNamedTypeAst namedFromVar })
            {
                return ResolveStructFromNamedType(namedFromVar, scope);
            }

            if (expression.BoundSymbol is VariableSymbol { DeclaredType: ITypeExpression declaredType })
            {
                var spelled = declaredType.Identifier
                    ?? (declaredType as PhpNamedTypeAst)?.Name?.Identifier;
                if (!string.IsNullOrWhiteSpace(spelled) && scope is not null)
                {
                    var simple = spelled.TrimStart('\\').Split('\\')[^1];
                    return FindStructSymbol(scope, simple);
                }
            }

            switch (expression)
            {
                case PhpNewAst newExpr when newExpr.ClassName is PhpNameAst className
                    && className.BoundSymbol is ObjectDeclarationSymbol { IsStruct: true } fromNew:
                    return fromNew;
                case PhpNamedTypeAst named:
                    return ResolveStructFromNamedType(named, scope);
                case PhpNameAst name when name.BoundSymbol is ObjectDeclarationSymbol { IsStruct: true } fromName:
                    return fromName;
                case PhpUnaryOpAst unary when IsCloneOperator(unary.Operator):
                    return ResolveStructTypeFromExpression(unary.Operand, scope);
                case PhpDereferenceableAst deref:
                    return ResolveStructTypeFromExpression(deref.Base as IExpression, scope);
                case PhpVariableAst variable when variable.BoundSymbol is VariableSymbol vs
                    && vs.DeclaredType is PhpNamedTypeAst namedVarType:
                    return ResolveStructFromNamedType(namedVarType, scope);
            }

            return null;
        }

        private static string? GetNamedTypeText(PhpNamedTypeAst namedType) =>
            namedType.Name switch
            {
                PhpNameAst name => name.ValueString,
                _ => namedType.Name?.Identifier,
            };

        private static bool IsBoundToNonStructType(IBaseSymbol? bound) =>
            bound is ObjectDeclarationSymbol { IsStruct: false }
                or BuiltInTypeSymbol
                or GenericTypeParameterSymbol;

        public static ObjectDeclarationSymbol? FindStructSymbol(IBaseScope scope, string name)
            => EmitLookupCache.GetOrSearch(EmitLookupKind.Struct, scope, name, SearchStructSymbol);

        private static ObjectDeclarationSymbol? SearchStructSymbol(IBaseScope scope, string name)
        {
            // Nested class-member structs are only `Owner\Name`, not an unqualified `Name`.
            if (scope.FindChildSymbolByName(name) is ObjectDeclarationSymbol direct
                && direct.IsStruct
                && scope is not ObjectDeclarationScope { DeclarationSymbol.IsStruct: false })
            {
                return direct;
            }

            foreach (var childScope in scope.GetAllChildScopes())
            {
                if (SearchStructSymbol(childScope, name) is { } found)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
