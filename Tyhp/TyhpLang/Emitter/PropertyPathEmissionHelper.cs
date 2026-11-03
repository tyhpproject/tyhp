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
    /// Builds a <c>\Tyhp\PropertyPath::from(...)</c> call for inline property-path
    /// arrow functions targeting <c>PropertyPath&lt;TCallableShape&gt;</c> parameters.
    /// The helper infers the result type from the closure and the property chain, so the
    /// call does not pass a type string.
    /// </summary>
    internal static class PropertyPathEmissionHelper
    {
        /// <summary>
        /// When <paramref name="expectedType"/> is <c>PropertyPath&lt;…&gt;</c> and
        /// <paramref name="expression"/> is a valid property-chain arrow fn, returns the
        /// rewritten <c>\Tyhp\PropertyPath::from(...)</c> call. The result type is not
        /// an argument: <c>PropertyPath::from</c> reads it from the closure, then from
        /// the property chain.
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
            SplitCallableShapeTypeArguments(typeArgs, out var sourceTypeExpr, out _);

            var sourceArg = BuildSourceTypeArgument(
                sourceTypeExpr,
                closure,
                inferred,
                resolveTypeSymbol,
                formatClassFqn,
                context);

            IExpression chain = BuildLambdaCall(
                @"\Tyhp\PropertyPath",
                "on",
                [PhpArgumentAst.CreateFromContext(sourceArg, context)],
                context);
            foreach (var segment in segments)
            {
                if (segment.NullSafe)
                {
                    chain = ExpressionTreeEmissionHelper.CallInstance(chain, "nullSafe", [], context);
                }

                chain = ExpressionTreeEmissionHelper.IsFluentMethodName(segment.Name)
                    ? ExpressionTreeEmissionHelper.CallInstance(
                        chain,
                        "prop",
                        [PhpScalarAst.CreateStringFromContext(context, segment.Name)],
                        context)
                    : ExpressionTreeEmissionHelper.AccessProperty(chain, segment.Name, context);
            }

            rewritten = ExpressionTreeEmissionHelper.CallInstance(chain, "backedBy", [closure], context);
            ExpressionTreeEmissionHelper.RecordRewrittenInlineFn(rewritten, closure);
            return true;
        }

        internal static IExpression BuildLambdaCall(
            string classFqn,
            string methodName,
            IReadOnlyList<PhpArgumentAst> arguments,
            Base2Ast context)
        {
            var staticMember = PhpStaticMemberAccessAst.CreateFromContext(
                PhpNameAst.CreateFromContext(methodName, context),
                context);
            var classBase = PhpDereferenceableAst.CreateFromContext(
                PhpNameAst.CreateFromContext(classFqn, context),
                staticMember,
                context);
            var args = PhpArgumentListAst.Create(arguments, context);
            return PhpDereferenceableAst.CreateFromContext(
                classBase,
                PhpCallAst.CreateFromContext(args, context),
                context);
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
            if (SpellFullCheckedType(type, formatClassFqn) is { } full)
            {
                return full;
            }

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

            if (CheckerHelpers.TryGetObjectDeclaration(declared) is { IsStruct: false } obj)
            {
                var fqn = formatClassFqn(obj.FullyQualifiedName, obj.Name);
                var full = SpellFullCheckedType(declared, formatClassFqn);
                if (full is null || full == fqn.TrimStart('\\'))
                {
                    return EmittedPhpExprAst.Create(fqn + "::class", context);
                }

                return PhpScalarAst.CreateStringFromContext(context, full);
            }

            return PhpScalarAst.CreateStringFromContext(
                context,
                SpellCheckedTypeAsRuntimeString(type, formatClassFqn));
        }

        /// <summary>
        /// <c>\Tyhp\Type::parse()</c> spelling of <paramref name="type"/> with type arguments,
        /// unions and structs kept (<c>array&lt;App\Post&gt;</c>, <c>?App\Table&lt;App\Blog&gt;</c>,
        /// <c>struct App\Address</c>). Null when a part cannot be spelled at runtime
        /// (type parameters, literals, callable shapes, <c>static</c>).
        /// </summary>
        internal static string? SpellFullCheckedType(
            ICheckedType type,
            Func<string?, string?, string> formatClassFqn)
        {
            switch (type)
            {
                case NullableCheckedType nullable:
                    var inner = SpellFullCheckedType(nullable.InnerType, formatClassFqn);
                    if (inner is null || inner is "mixed" or "null" || inner.StartsWith('?'))
                    {
                        return inner;
                    }

                    if (inner.Contains('|'))
                    {
                        return inner + "|null";
                    }

                    return inner.Contains('&') ? "(" + inner + ")|null" : "?" + inner;
                case GenericCheckedType generic:
                    var head = SpellFullCheckedType(generic.BaseType, formatClassFqn);
                    if (head is null || head.Contains('|') || head.Contains('&') || head.StartsWith('?'))
                    {
                        return null;
                    }

                    var args = new List<string>();
                    foreach (var arg in generic.TypeArguments)
                    {
                        if (SpellFullCheckedType(arg, formatClassFqn) is not { } argText)
                        {
                            return null;
                        }

                        args.Add(argText);
                    }

                    return args.Count == 0 || args.All(a => a == "mixed")
                        ? head
                        : head + "<" + string.Join(", ", args) + ">";
                case UnionCheckedType union:
                    return JoinFullCheckedTypes(union.Members, "|", formatClassFqn);
                case IntersectionCheckedType { IsOverloadSet: false } intersection:
                    return JoinFullCheckedTypes(intersection.Members, "&", formatClassFqn);
                case LiteralCheckedType { Value: null }:
                    return "null";
                case SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol }:
                    return null;
                case SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol obj }:
                    var fqn = formatClassFqn(obj.FullyQualifiedName, obj.Name).TrimStart('\\');
                    return obj.IsStruct ? "struct " + fqn : fqn;
                case SimpleCheckedType { ResolvedSymbol: BuiltInTypeSymbol builtin }:
                    var name = builtin.Name.Trim().TrimStart('\\').ToLowerInvariant();
                    return RuntimeParsableBuiltins.Contains(name) ? name : null;
                case SpecialCheckedType special when special.IsMixed:
                    return "mixed";
                case SpecialCheckedType special when special.IsVoid:
                    return "void";
                case SpecialCheckedType special when special.IsNever:
                    return "never";
                default:
                    return null;
            }
        }

        private static readonly HashSet<string> RuntimeParsableBuiltins = new(StringComparer.OrdinalIgnoreCase)
        {
            "string", "int", "float", "bool", "true", "false", "null", "void", "mixed", "never",
            "object", "callable", "iterable", "resource", "array", "decimal",
        };

        private static string? JoinFullCheckedTypes(
            IReadOnlyList<ICheckedType> members,
            string separator,
            Func<string?, string?, string> formatClassFqn)
        {
            var parts = new List<string>();
            var hasNull = false;
            foreach (var member in members)
            {
                if (SpellFullCheckedType(member, formatClassFqn) is not { } text)
                {
                    return null;
                }

                if (separator == "|")
                {
                    foreach (var part in SplitUnionSpelling(text))
                    {
                        if (part == "null")
                        {
                            hasNull = true;
                        }
                        else if (!parts.Contains(part))
                        {
                            parts.Add(member is IntersectionCheckedType ? "(" + part + ")" : part);
                        }
                    }

                    continue;
                }

                parts.Add(text);
            }

            if (parts.Contains("mixed"))
            {
                return separator == "|" ? "mixed" : null;
            }

            if (parts.Count == 0)
            {
                return hasNull ? "null" : null;
            }

            var joined = string.Join(separator, parts);
            if (!hasNull)
            {
                return joined;
            }

            return parts.Count == 1 && !parts[0].StartsWith('(') ? "?" + joined : joined + "|null";
        }

        private static IEnumerable<string> SplitUnionSpelling(string text)
        {
            if (text.StartsWith('?'))
            {
                return [text[1..], "null"];
            }

            var parts = new List<string>();
            var depth = 0;
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                switch (text[i])
                {
                    case '<' or '(':
                        depth++;
                        break;
                    case '>' or ')':
                        depth--;
                        break;
                    case '|' when depth == 0:
                        parts.Add(text[start..i]);
                        start = i + 1;
                        break;
                }
            }

            parts.Add(text[start..]);
            return parts;
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

            // `callable(User): string` and `Expression<callable(User): string>` store type
            // arguments on the `typeName` grammar addon, not on a generic identifier child.
            if (typeExpr is Base2Ast node
                && node.AstGrammarAddons.TryGetValue("typeName", out var addon)
                && addon is PhpTypeExpressionListAst addonArgs)
            {
                return FlattenTypeArgs(addonArgs);
            }

            return [];
        }

        /// <summary>
        /// <c>PropertyPath</c>/<c>Expression</c> now take a single <c>TCallableShape</c>
        /// (<c>callable(TArgs ...): TReturn</c>). Peel that shape into the first parameter
        /// type (source) and the return type. A zero-parameter callable has no source.
        /// </summary>
        internal static void SplitCallableShapeTypeArguments(
            IReadOnlyList<ITypeExpression> typeArgs,
            out ITypeExpression? sourceTypeExpr,
            out ITypeExpression? resultTypeExpr)
        {
            sourceTypeExpr = null;
            resultTypeExpr = null;
            if (typeArgs.Count == 0)
            {
                return;
            }

            var shapeArgs = GetGenericTypeArguments(typeArgs[0]);
            if (shapeArgs.Count == 0)
            {
            // Bare `callable` or a non-generic argument — no peelable arity.
                return;
            }

            resultTypeExpr = shapeArgs[^1];
            if (shapeArgs.Count >= 2)
            {
                sourceTypeExpr = shapeArgs[0];
            }
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
            var authoredParamType = closure.Parameters?.GetAllNotNull().FirstOrDefault()?.Type;
            if ((PropertyTypeStampSupport.TrySpellLossy(sourceTypeExpr)
                    ?? PropertyTypeStampSupport.TrySpellLossy(authoredParamType)) is { } detailed)
            {
                return PhpScalarAst.CreateStringFromContext(context, detailed);
            }

            // Prefer PropertyPath<callable(T): R> type args when still present (pre-erasure).
            if (sourceTypeExpr is not null
                && resolveTypeSymbol(sourceTypeExpr) is ObjectDeclarationSymbol fromGeneric)
            {
                var fqn = formatClassFqn(fromGeneric.FullyQualifiedName, fromGeneric.Name);
                return EmittedPhpExprAst.Create(fqn + "::class", context);
            }

            // Authored fn parameter annotation.
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
