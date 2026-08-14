using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Story 16 Phase 1 — builds <c>new \Tyhp\PropertyPath(...)</c> AST for inline property-path
    /// arrow functions targeting <c>PropertyPath&lt;T, R&gt;</c> parameters.
    /// </summary>
    internal static class PropertyPathEmissionHelper
    {
        /// <summary>
        /// When <paramref name="expectedType"/> is <c>PropertyPath&lt;…&gt;</c> and
        /// <paramref name="expression"/> is a valid property-chain arrow fn, returns the
        /// rewritten <c>new \Tyhp\PropertyPath(...)</c> expression.
        /// </summary>
        public static bool TryRewriteInlineFn(
            IExpression expression,
            ITypeExpression expectedType,
            Func<ITypeExpression?, IBaseSymbol?> resolveTypeSymbol,
            Func<string?, string?, string> formatClassFqn,
            Func<PhpInlineFunctionAst, InferredClosureSignature?>? getInferredSignature,
            Base2Ast context,
            out IExpression rewritten)
        {
            rewritten = expression;

            if (expression is not PhpInlineFunctionAst closure || !closure.IsArrowFunction)
            {
                return false;
            }

            if (!IsPropertyPathTypeExpression(expectedType, resolveTypeSymbol))
            {
                return false;
            }

            if (!PropertyPathSupport.TryGetArrowBodyExpression(closure, out var body))
            {
                return false;
            }

            var paramName = PropertyPathSupport.GetSingleArrowParameterName(closure);
            if (paramName is null
                || !PropertyPathSupport.TryExtractPropertyChain(body, paramName, out var segments)
                || segments.Count == 0)
            {
                return false;
            }

            var typeArgs = GetGenericTypeArguments(expectedType);
            var inferred = getInferredSignature?.Invoke(closure);
            var sourceTypeExpr = typeArgs.Count > 0 ? typeArgs[0] : null;
            var resultTypeExpr = typeArgs.Count > 1 ? typeArgs[^1] : null;

            var sourceArg = BuildSourceTypeArgument(
                sourceTypeExpr,
                closure,
                inferred,
                resolveTypeSymbol,
                formatClassFqn,
                context);
            var resultArg = PhpScalarAst.CreateStringFromContext(
                context,
                SpellResultType(resultTypeExpr, closure, inferred, resolveTypeSymbol, formatClassFqn));
            var pathArg = BuildPathArray(segments, context);

            var argList = new List<PhpArgumentAst>
            {
                PhpArgumentAst.CreateFromContext(sourceArg, context),
                PhpArgumentAst.CreateFromContext(resultArg, context),
                PhpArgumentAst.CreateFromContext(pathArg, context),
                PhpArgumentAst.CreateFromContext(closure, context),
            };

            // Only `?->` chains need the flags, so plain paths keep the shorter emitted call.
            if (segments.Any(s => s.NullSafe))
            {
                argList.Add(PhpArgumentAst.CreateNamedFromContext(
                    BuildNullSafeFlagsArray(segments, context),
                    "nullSafeFlags",
                    context));
            }

            var args = PhpArgumentListAst.Create(argList, context);

            rewritten = PhpNewAst.CreateFromContext(
                PhpNameAst.CreateFromContext(@"\Tyhp\PropertyPath", context),
                args,
                context);
            return true;
        }

        public static bool TryBuildCallableExtraction(
            IExpression expression,
            Base2Ast context,
            out IExpression rewritten)
        {
            rewritten = expression;
            if (expression is not IDereferenceableBase derefBase)
            {
                return false;
            }

            var accessor = TokenValueAst.CreateFromContext("->", TyhpParser.T_OBJECT_OPERATOR, context);
            var member = PhpInstanceMemberAccessAst.CreateFromContext(
                accessor,
                PhpNameAst.CreateFromContext("callable", context),
                context);
            rewritten = PhpDereferenceableAst.CreateFromContext(derefBase, member, context);
            return true;
        }

        public static bool IsPropertyPathTypeExpression(
            ITypeExpression? typeExpr,
            Func<ITypeExpression?, IBaseSymbol?> resolveTypeSymbol)
            => IsTyhpLambdaTypeExpression(
                typeExpr,
                resolveTypeSymbol,
                PropertyPathSupport.PropertyPathSimpleName,
                PropertyPathSupport.PropertyPathFqn);

        public static bool IsPropertyPathOrExpressionTypeExpression(
            ITypeExpression? typeExpr,
            Func<ITypeExpression?, IBaseSymbol?> resolveTypeSymbol)
            => IsTyhpLambdaTypeExpression(
                typeExpr,
                resolveTypeSymbol,
                PropertyPathSupport.PropertyPathSimpleName,
                PropertyPathSupport.PropertyPathFqn)
            || IsTyhpLambdaTypeExpression(
                typeExpr,
                resolveTypeSymbol,
                PropertyPathSupport.ExpressionSimpleName,
                PropertyPathSupport.ExpressionFqn);

        /// <summary>
        /// The bound declaration decides when there is one, so a user class also named
        /// <c>PropertyPath</c> or <c>Expression</c> is never rewritten; unbound spellings
        /// (tyhpdef not loaded) fall back to the written name.
        /// </summary>
        private static bool IsTyhpLambdaTypeExpression(
            ITypeExpression? typeExpr,
            Func<ITypeExpression?, IBaseSymbol?> resolveTypeSymbol,
            string simpleName,
            string fqn)
        {
            if (resolveTypeSymbol(typeExpr) is ObjectDeclarationSymbol obj)
            {
                return IsTyhpLambdaObject(obj, simpleName, fqn);
            }

            return TypeExpressionNames(typeExpr, simpleName, fqn);
        }

        public static bool IsPropertyPathOrExpressionNew(PhpNewAst newExpr)
        {
            if (newExpr.ClassName is not PhpNameAst name)
            {
                return false;
            }

            var text = (name.ValueString ?? name.Identifier ?? "").TrimStart('\\');
            return text.Equals(PropertyPathSupport.PropertyPathFqn, StringComparison.OrdinalIgnoreCase)
                || text.Equals(PropertyPathSupport.ExpressionFqn, StringComparison.OrdinalIgnoreCase)
                || text.Equals(PropertyPathSupport.PropertyPathSimpleName, StringComparison.OrdinalIgnoreCase)
                || text.Equals(PropertyPathSupport.ExpressionSimpleName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary><c>\Closure</c> is always the global PHP class, so the spelling is sufficient.</summary>
        public static bool IsClosureTypeExpression(ITypeExpression expectedType)
        {
            var text = GetTypeExpressionSimpleName(expectedType)?.TrimStart('\\');
            return string.Equals(text, "Closure", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TypeExpressionNames(ITypeExpression? typeExpr, string simpleName, string fqn)
        {
            var text = GetTypeExpressionSimpleName(typeExpr);
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            text = text.TrimStart('\\');
            var angle = text.IndexOf('<');
            if (angle >= 0)
            {
                text = text[..angle];
            }

            // Unbound spelling: exact simple name or Tyhp\{Name} only — never App\PropertyPath.
            return string.Equals(text, simpleName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, fqn, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTyhpLambdaObject(ObjectDeclarationSymbol obj, string simpleName, string fqn)
        {
            var normalized = (obj.FullyQualifiedName ?? obj.Name ?? "").TrimStart('\\');
            if (string.Equals(normalized, fqn, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Package tyhpdef may bind without a namespace prefix; never match App\Expression.
            if (string.IsNullOrEmpty(obj.FullyQualifiedName) || !normalized.Contains('\\'))
            {
                return string.Equals(obj.Name, simpleName, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// Spells an <see cref="ICheckedType"/> for runtime ctor string arguments (Story 16).
        /// </summary>
        internal static string SpellCheckedTypeAsRuntimeString(
            ICheckedType type,
            Func<string?, string?, string> formatClassFqn)
        {
            var isNullable = false;
            while (type is NullableCheckedType nullable)
            {
                isNullable = true;
                type = nullable.InnerType;
            }

            var spelling = SpellNonNullCheckedType(type, formatClassFqn);
            return isNullable && spelling is not ("mixed" or "null")
                ? "?" + spelling
                : spelling;
        }

        /// <summary>
        /// Spells an object <see cref="ICheckedType"/> as <c>FQN::class</c> or a scalar string literal.
        /// </summary>
        internal static IExpression SpellCheckedTypeAsCtorArg(
            ICheckedType type,
            Func<string?, string?, string> formatClassFqn,
            Base2Ast context)
        {
            var declared = type;
            while (declared is NullableCheckedType nullable)
            {
                declared = nullable.InnerType;
            }

            if (CheckerHelpers.TryGetObjectDeclaration(declared) is { } obj)
            {
                var fqn = formatClassFqn(obj.FullyQualifiedName, obj.Name);
                return EmittedPhpExprAst.Create(fqn + "::class", context);
            }

            return PhpScalarAst.CreateStringFromContext(
                context,
                SpellCheckedTypeAsRuntimeString(type, formatClassFqn));
        }

        internal static string SpellTypeAsRuntimeString(
            ITypeExpression? typeExpr,
            Func<ITypeExpression?, IBaseSymbol?> resolveTypeSymbol,
            Func<string?, string?, string> formatClassFqn)
        {
            if (typeExpr is null || IsGenericParameterTypeExpression(typeExpr))
            {
                return "mixed";
            }

            // `?string` must not be reported to the runtime as `string`.
            if (typeExpr is PhpTypeExpressionAst { IsNullable: true, Types: { } nullableTypes }
                && SingleMember(nullableTypes) is { } nullableInner)
            {
                var innerSpelling = SpellTypeAsRuntimeString(nullableInner, resolveTypeSymbol, formatClassFqn);
                return innerSpelling is "mixed" or "null" ? innerSpelling : "?" + innerSpelling;
            }

            if (resolveTypeSymbol(typeExpr) is ObjectDeclarationSymbol obj)
            {
                return formatClassFqn(obj.FullyQualifiedName, obj.Name).TrimStart('\\');
            }

            if (typeExpr is PhpBuiltinTypeAst builtin)
            {
                return builtin.Identifier ?? "mixed";
            }

            var text = GetTypeExpressionSimpleName(typeExpr);
            if (string.IsNullOrWhiteSpace(text))
            {
                return "mixed";
            }

            var angle = text.IndexOf('<');
            if (angle >= 0)
            {
                text = text[..angle];
            }

            return text.TrimStart('\\');
        }

        internal static IReadOnlyList<ITypeExpression> GetGenericTypeArguments(ITypeExpression typeExpr)
        {
            if (typeExpr is PhpTypeExpressionAst { Types: { } composite }
                && SingleMember(composite) is { } inner)
            {
                return GetGenericTypeArguments(inner);
            }

            if (typeExpr is PhpNamedTypeAst { Name: TyhpGenericIdentifierAst generic }
                && generic.GenericArguments is PhpTypeExpressionListAst args)
            {
                return FlattenTypeArgs(args);
            }

            if (typeExpr is TyhpGenericIdentifierAst bare
                && bare.GenericArguments is PhpTypeExpressionListAst bareArgs)
            {
                return FlattenTypeArgs(bareArgs);
            }

            return [];
        }

        private static string SpellNonNullCheckedType(
            ICheckedType type,
            Func<string?, string?, string> formatClassFqn)
        {
            if (type is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol })
            {
                return "mixed";
            }

            if (CheckerHelpers.TryGetObjectDeclaration(type) is { } obj)
            {
                return formatClassFqn(obj.FullyQualifiedName, obj.Name).TrimStart('\\');
            }

            if (CheckerHelpers.IsBuiltInName(type, "string")) return "string";
            if (CheckerHelpers.IsBuiltInName(type, "int")) return "int";
            if (CheckerHelpers.IsBuiltInName(type, "float")) return "float";
            if (CheckerHelpers.IsBuiltInName(type, "bool")) return "bool";
            if (CheckerHelpers.IsBuiltInName(type, "array")) return "array";
            if (CheckerHelpers.IsBuiltInName(type, "mixed")) return "mixed";

            var display = type.DisplayName;
            var angle = display.IndexOf('<');
            if (angle >= 0)
            {
                display = display[..angle];
            }

            return display.TrimStart('\\');
        }

        private static IExpression BuildSourceTypeArgument(
            ITypeExpression? sourceTypeExpr,
            PhpInlineFunctionAst closure,
            InferredClosureSignature? inferred,
            Func<ITypeExpression?, IBaseSymbol?> resolveTypeSymbol,
            Func<string?, string?, string> formatClassFqn,
            Base2Ast context)
        {
            // Prefer PropertyPath<T, R> type args when still present (pre-erasure).
            if (sourceTypeExpr is not null
                && resolveTypeSymbol(sourceTypeExpr) is ObjectDeclarationSymbol fromGeneric)
            {
                var fqn = formatClassFqn(fromGeneric.FullyQualifiedName, fromGeneric.Name);
                return EmittedPhpExprAst.Create(fqn + "::class", context);
            }

            // Authored fn parameter annotation.
            var authoredParamType = closure.Parameters?.GetAllNotNull().FirstOrDefault()?.Type;
            if (authoredParamType is not null
                && resolveTypeSymbol(authoredParamType) is ObjectDeclarationSymbol fromParam)
            {
                var fqn = formatClassFqn(fromParam.FullyQualifiedName, fromParam.Name);
                return EmittedPhpExprAst.Create(fqn + "::class", context);
            }

            // Contextual typing recovered by the checker.
            if (inferred?.ParameterTypes is { Count: > 0 }
                && inferred.ParameterTypes[0] is { } inferredSource
                && CheckerHelpers.TryGetObjectDeclaration(inferredSource) is { } fromInferred)
            {
                var fqn = formatClassFqn(fromInferred.FullyQualifiedName, fromInferred.Name);
                return EmittedPhpExprAst.Create(fqn + "::class", context);
            }

            if (sourceTypeExpr is not null && !IsGenericParameterTypeExpression(sourceTypeExpr))
            {
                return PhpScalarAst.CreateStringFromContext(
                    context,
                    SpellTypeAsRuntimeString(sourceTypeExpr, resolveTypeSymbol, formatClassFqn));
            }

            if (inferred?.ParameterTypes is { Count: > 0 } && inferred.ParameterTypes[0] is { } checkedSource)
            {
                return SpellCheckedTypeAsCtorArg(checkedSource, formatClassFqn, context);
            }

            return PhpScalarAst.CreateStringFromContext(context, "mixed");
        }

        private static string SpellResultType(
            ITypeExpression? resultTypeExpr,
            PhpInlineFunctionAst closure,
            InferredClosureSignature? inferred,
            Func<ITypeExpression?, IBaseSymbol?> resolveTypeSymbol,
            Func<string?, string?, string> formatClassFqn)
        {
            if (resultTypeExpr is not null)
            {
                return SpellTypeAsRuntimeString(resultTypeExpr, resolveTypeSymbol, formatClassFqn);
            }

            if (closure.ReturnType is not null)
            {
                return SpellTypeAsRuntimeString(closure.ReturnType, resolveTypeSymbol, formatClassFqn);
            }

            if (inferred?.ReturnType is { } inferredReturn)
            {
                return SpellCheckedTypeAsRuntimeString(inferredReturn, formatClassFqn);
            }

            return "mixed";
        }

        private static PhpArrayAst BuildPathArray(
            IReadOnlyList<PropertyPathSupport.PathSegment> segments,
            Base2Ast context)
            => BuildArray(
                segments.Select(s => (IExpression)PhpScalarAst.CreateStringFromContext(context, s.Name)),
                context);

        private static PhpArrayAst BuildNullSafeFlagsArray(
            IReadOnlyList<PropertyPathSupport.PathSegment> segments,
            Base2Ast context)
            => BuildArray(
                segments.Select(s => (IExpression)EmittedPhpExprAst.Create(s.NullSafe ? "true" : "false", context)),
                context);

        private static PhpArrayAst BuildArray(IEnumerable<IExpression> values, Base2Ast context)
        {
            var pairs = values
                .Select(value => PhpArrayPairAst.CreateFromContext(
                    keyExpr: null,
                    valueExpr: value,
                    isExpansion: false,
                    context))
                .ToList();

            return PhpArrayAst.CreateFromContext(
                PhpArrayPairListAst.Create(pairs, context),
                isShortSyntax: true,
                context);
        }

        /// <summary>
        /// A free type parameter must erase rather than spell a class that does not exist
        /// (same rule as runtime type expressions: never emit <c>T::class</c>).
        /// </summary>
        private static bool IsGenericParameterTypeExpression(ITypeExpression typeExpr) =>
            typeExpr switch
            {
                PhpTypeExpressionAst { Types: { } types } when SingleMember(types) is { } inner =>
                    IsGenericParameterTypeExpression(inner),
                PhpNamedTypeAst { Name.BoundSymbol: GenericTypeParameterSymbol } => true,
                _ => typeExpr.BoundSymbol is GenericTypeParameterSymbol,
            };

        private static string? GetTypeExpressionSimpleName(ITypeExpression? typeExpr) =>
            typeExpr switch
            {
                // A single-member composite (`\Closure`, `?\Closure`) wraps the real type node.
                PhpTypeExpressionAst { Types: { } types } composite =>
                    GetTypeExpressionSimpleName(SingleMember(types)) ?? composite.Identifier,
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                PhpNamedTypeAst { Name: TyhpGenericIdentifierAst generic } =>
                    generic.ValueString ?? generic.Identifier,
                PhpNamedTypeAst { Name: PhpNameAst name } =>
                    name.ValueString ?? name.Identifier,
                PhpNamedTypeAst named => named.Name?.Identifier,
                _ => typeExpr?.Identifier,
            };

        private static ITypeExpression? SingleMember(PhpTypeExpressionListAst types)
        {
            var members = types.GetAllNotNull().ToList();
            return members.Count == 1 ? members[0] : null;
        }

        private static List<ITypeExpression> FlattenTypeArgs(PhpTypeExpressionListAst list)
        {
            var raw = list.GetAllNotNull().ToList();
            if (raw.Count == 1
                && raw[0] is PhpTypeExpressionAst { Types: PhpTypeExpressionListAst inner })
            {
                var innerArgs = inner.GetAllNotNull().ToList();
                if (innerArgs.Count > 0)
                {
                    return innerArgs;
                }
            }

            return raw;
        }
    }
}
