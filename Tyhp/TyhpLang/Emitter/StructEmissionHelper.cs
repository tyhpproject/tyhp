using System.Globalization;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
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
    /// Rewrites compile-time struct construction/access into PHP associative array operations
    /// (or a configured custom backing class).
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
        /// <c>clone $s</c> on an array-backed struct is a no-op (PHP arrays copy on assignment).
        /// </summary>
        public static bool TryRewriteStructClone(
            PhpUnaryOpAst unary,
            Func<IExpression?, ObjectDeclarationSymbol?> resolveStructType,
            bool isArrayBacked,
            out IBase2Ast rewritten)
        {
            rewritten = unary;

            if (!isArrayBacked
                || !unary.IsPrefix
                || !IsCloneOperator(unary.Operator)
                || unary.Operand is null
                || resolveStructType(unary.Operand) is not { IsStruct: true })
            {
                return false;
            }

            rewritten = unary.Operand;
            return true;
        }

        /// <summary>
        /// Rewrites array-backed struct <c>with</c> forms.
        /// <c>new Struct() with [...]</c> folds defaults and overrides into a single array
        /// literal at emit time; <c>$s with</c> / <c>clone $s with</c> keep a runtime
        /// <c>\array_replace($base, [...])</c> because the base value is not known statically.
        /// </summary>
        public static bool TryRewriteStructWith(
            PhpBinaryOpAst binary,
            Func<PhpNewAst, ObjectDeclarationSymbol?> resolveStructFromNew,
            Func<IExpression?, ObjectDeclarationSymbol?> resolveStructType,
            bool isArrayBacked,
            out IBase2Ast rewritten)
        {
            rewritten = binary;

            if (!isArrayBacked
                || !IsWithOperator(binary.Operator)
                || binary.Right is not PhpArrayPairListAst pairList)
            {
                return false;
            }

            if (binary.Left is PhpNewAst newExpr
                && resolveStructFromNew(newExpr) is { IsStruct: true } newStruct)
            {
                var defaults = CreateDefaultsArray(newStruct, binary);
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

            if (baseExpr is null || resolveStructType(baseExpr) is not { IsStruct: true } baseStruct)
            {
                return false;
            }

            var runtimeOverrides = CreateArrayFromWithList(pairList, baseStruct, binary);
            rewritten = CreateArrayReplaceCall(baseExpr, runtimeOverrides, binary);
            return true;
        }

        public static bool TryRewriteStructNew(
            PhpNewAst newExpr,
            Func<PhpNewAst, ObjectDeclarationSymbol?> resolveStruct,
            EmitContext context,
            Func<string, ObjectDeclarationSymbol?> resolveBackingClass,
            ref bool reportedBackingError,
            out IBase2Ast rewritten)
        {
            rewritten = newExpr;

            if (resolveStruct(newExpr) is not { IsStruct: true } structDecl)
            {
                return false;
            }

            var defaults = CreateDefaultsArray(structDecl, newExpr);

            if (context.IsStructBackedByArray())
            {
                rewritten = defaults;
                return true;
            }

            rewritten = CreateCustomBackingConstruction(
                defaults,
                newExpr,
                context,
                resolveBackingClass,
                ref reportedBackingError);
            return true;
        }

        /// <summary>
        /// Builds <c>new \BackingClass([...defaults...])</c> for a non-array <c>build.structBacking</c>.
        /// Reports <see cref="MessageCode.EmitterStructBackingError"/> once when the class cannot be resolved.
        /// </summary>
        public static PhpNewAst CreateCustomBackingConstruction(
            PhpArrayAst defaults,
            Base2Ast context,
            EmitContext emitContext,
            Func<string, ObjectDeclarationSymbol?> resolveBackingClass,
            ref bool reportedBackingError)
        {
            var backingName = NormalizeBackingClassName(emitContext.GetStructBacking());
            var resolved = resolveBackingClass(backingName);
            if (resolved is null && !reportedBackingError)
            {
                reportedBackingError = true;
                var fileName = context.OwningFile?.Identifier
                    ?? emitContext.CurrentSourceFile?.Identifier
                    ?? "";
                emitContext.Diagnostics.AddErrorFromAst(
                    MessageCode.EmitterStructBackingError,
                    context,
                    fileName,
                    backingName);
            }

            var fqn = resolved?.FullyQualifiedName ?? backingName;
            if (!fqn.StartsWith('\\'))
            {
                fqn = "\\" + fqn;
            }

            var args = PhpArgumentListAst.Create(
                [PhpArgumentAst.CreateFromContext(defaults, context)],
                context);
            return PhpNewAst.CreateFromContext(
                PhpNameAst.CreateFromContext(fqn, context),
                args,
                context);
        }

        public static PhpArrayAst CreateDefaultsArray(ObjectDeclarationSymbol structDecl, Base2Ast context)
        {
            var pairs = EnumerateStructHierarchy(structDecl)
                .SelectMany(decl => decl.Members.Values.OfType<ObjectPropertySymbol>())
                .Select(property => CreateDefaultPair(property, context))
                .Where(pair => pair is not null)
                .Cast<PhpArrayPairAst>()
                .ToList();

            return CreateArrayFromPairs(pairs, context);
        }

        public static IBase2Ast CreateArrayReplaceCall(
            IExpression baseExpr,
            IExpression overridesExpr,
            Base2Ast context)
        {
            var args = PhpArgumentListAst.Create(
                [
                    PhpArgumentAst.CreateFromContext(baseExpr, context),
                    PhpArgumentAst.CreateFromContext(overridesExpr, context),
                ],
                context);

            return PhpDereferenceableAst.CreateFromContext(
                PhpNameAst.CreateFromContext(@"\array_replace", context),
                PhpCallAst.CreateFromContext(args, context),
                context);
        }

        /// <summary>
        /// Compile-time merge of struct defaults with a <c>with</c> override list into one
        /// short-array literal. Default key order is preserved; overridden keys keep their
        /// position; keys present only in overrides are appended.
        /// </summary>
        public static PhpArrayAst MergeArrayPairs(
            PhpArrayAst defaults,
            PhpArrayAst overrides,
            Base2Ast context)
        {
            var merged = new List<PhpArrayPairAst>();
            var indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var pair in defaults.ArrayPairs?.GetAllNotNull() ?? [])
            {
                var key = pair.KeyExpr is null ? null : GetArrayKeyText(pair.KeyExpr);
                if (key is not null)
                {
                    indexByKey[key] = merged.Count;
                }

                merged.Add(pair);
            }

            foreach (var pair in overrides.ArrayPairs?.GetAllNotNull() ?? [])
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
        {
            var pairs = pairList.GetAllNotNull()
                .Select(pair => NormalizeArrayPair(pair, structDecl, context))
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
            ObjectDeclarationSymbol structDecl,
            Base2Ast context)
        {
            if (pair.KeyExpr is null)
            {
                return pair;
            }

            var keyText = GetArrayKeyText(pair.KeyExpr);

            // A `with` key written as the property/alias name must resolve to the backing array
            // key (aliased properties store under their alias, not the member name).
            var resolvedKey = ResolveStructPropertyKey(structDecl, keyText)
                ?? new StructArrayKey(keyText, IsInteger: false);
            return PhpArrayPairAst.CreateFromContext(
                resolvedKey.ToScalarAst(context),
                pair.ValueExpr!,
                pair.IsExpansion,
                context);
        }

        private static PhpArrayPairAst? CreateDefaultPair(ObjectPropertySymbol property, Base2Ast context)
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
                defaultValue,
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
            if (string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            var lookupKey = memberName.StartsWith('$') ? memberName : "$" + memberName;
            foreach (var decl in EnumerateStructHierarchy(structDecl))
            {
                if (decl.Members.TryGetValue(lookupKey, out var member)
                    && member is ObjectPropertySymbol property)
                {
                    return GetStructArrayKey(property);
                }
            }

            // Fallback: treat the member name itself as the key (already-normalized bare name).
            return new StructArrayKey(NormalizePropertyKey(memberName), IsInteger: false);
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
            structDecl.DeclaringAstNode switch
            {
                TyhpStructDeclAst { Extends: { } className } => className,
                PhpObjectTypeDeclAst { Extends: { } className } => className,
                _ => null,
            };

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

        public static string NormalizeBackingClassName(string backing)
        {
            var trimmed = backing.Trim();
            if (trimmed.Length == 0)
            {
                return trimmed;
            }

            return trimmed.StartsWith('\\') ? trimmed : "\\" + trimmed.TrimStart('\\');
        }

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

        public static ObjectDeclarationSymbol? FindStructSymbol(IBaseScope scope, string name)
        {
            if (scope.FindChildSymbolByName(name) is ObjectDeclarationSymbol direct && direct.IsStruct)
            {
                return direct;
            }

            foreach (var childScope in scope.GetAllChildScopes())
            {
                if (FindStructSymbol(childScope, name) is { } found)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
