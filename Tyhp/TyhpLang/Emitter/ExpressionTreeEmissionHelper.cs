using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Story 16 Phase 2 — builds <c>new \Tyhp\Expression(...)</c> AST for inline expression-tree
    /// arrow functions targeting <c>Expression&lt;T, R&gt;</c> parameters.
    /// </summary>
    internal static class ExpressionTreeEmissionHelper
    {
        /// <summary>
        /// When <paramref name="expectedType"/> is <c>Expression&lt;…&gt;</c> (not PropertyPath) and
        /// <paramref name="expression"/> is an arrow fn, returns the rewritten
        /// <c>new \Tyhp\Expression(...)</c> expression.
        /// </summary>
        public static bool TryRewriteInlineFn(
            IExpression expression,
            ITypeExpression expectedType,
            Func<ITypeExpression?, IBaseSymbol?> resolveTypeSymbol,
            Func<string?, string?, string> formatClassFqn,
            Func<PhpInlineFunctionAst, InferredClosureSignature?>? getInferredSignature,
            IReadOnlyDictionary<IBase2Ast, ICheckedType>? expressionTypes,
            Base2Ast context,
            out IExpression rewritten)
        {
            rewritten = expression;

            if (expression is not PhpInlineFunctionAst closure || !closure.IsArrowFunction)
            {
                return false;
            }

            // PropertyPath stays on PropertyPathEmissionHelper — do not rewrite those here.
            if (PropertyPathEmissionHelper.IsPropertyPathTypeExpression(expectedType, resolveTypeSymbol))
            {
                return false;
            }

            if (!IsExpressionTypeExpression(expectedType, resolveTypeSymbol))
            {
                return false;
            }

            if (!PropertyPathSupport.TryGetArrowBodyExpression(closure, out var body))
            {
                return false;
            }

            var inferred = getInferredSignature?.Invoke(closure);
            var paramNames = GetParameterNames(closure);
            var parameterInfos = BuildParameterInfos(
                closure,
                inferred,
                resolveTypeSymbol,
                formatClassFqn,
                context);

            var treeBody = RewriteNode(
                body,
                paramNames,
                parameterInfos,
                expressionTypes,
                formatClassFqn,
                context);
            if (treeBody is null)
            {
                return false;
            }

            var returnTypeSpelling = SpellReturnType(
                expectedType,
                closure,
                inferred,
                resolveTypeSymbol,
                formatClassFqn);

            var parameterList = parameterInfos
                .OrderBy(p => p.Value.Index)
                .Select(p => BuildParameterExpressionNode(p.Value, context))
                .ToList();

            var args = PhpArgumentListAst.Create(
                [
                    PhpArgumentAst.CreateNamedFromContext(treeBody, "body", context),
                    PhpArgumentAst.CreateNamedFromContext(
                        BuildArray(parameterList, context),
                        "parameters",
                        context),
                    PhpArgumentAst.CreateNamedFromContext(closure, "callable", context),
                    PhpArgumentAst.CreateNamedFromContext(
                        PhpScalarAst.CreateStringFromContext(context, returnTypeSpelling),
                        "returnType",
                        context),
                ],
                context);

            rewritten = PhpNewAst.CreateFromContext(
                PhpNameAst.CreateFromContext(@"\Tyhp\Expression", context),
                args,
                context);
            return true;
        }

        public static bool IsExpressionTypeExpression(
            ITypeExpression? typeExpr,
            Func<ITypeExpression?, IBaseSymbol?> resolveTypeSymbol)
            => IsTyhpExpressionTypeExpression(
                typeExpr,
                resolveTypeSymbol,
                PropertyPathSupport.ExpressionSimpleName,
                PropertyPathSupport.ExpressionFqn);

        private static bool IsTyhpExpressionTypeExpression(
            ITypeExpression? typeExpr,
            Func<ITypeExpression?, IBaseSymbol?> resolveTypeSymbol,
            string simpleName,
            string fqn)
        {
            if (resolveTypeSymbol(typeExpr) is ObjectDeclarationSymbol obj)
            {
                // Bound declarations are authoritative: require Tyhp\Expression (or a bare
                // namespace-less Expression from package tyhpdef). Never match App\Expression.
                var normalized = (obj.FullyQualifiedName ?? obj.Name ?? "").TrimStart('\\');
                if (string.Equals(normalized, fqn, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.IsNullOrEmpty(obj.FullyQualifiedName) || !normalized.Contains('\\'))
                {
                    return string.Equals(obj.Name, simpleName, StringComparison.OrdinalIgnoreCase);
                }

                return false;
            }

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

            // Unbound spelling: exact simple name or Tyhp\Expression only.
            return string.Equals(text, simpleName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, fqn, StringComparison.OrdinalIgnoreCase);
        }

        private readonly record struct ParameterInfo(string Name, int Index, IExpression TypeArg);

        private static Dictionary<string, ParameterInfo> BuildParameterInfos(
            PhpInlineFunctionAst closure,
            InferredClosureSignature? inferred,
            Func<ITypeExpression?, IBaseSymbol?> resolveTypeSymbol,
            Func<string?, string?, string> formatClassFqn,
            Base2Ast context)
        {
            var result = new Dictionary<string, ParameterInfo>(StringComparer.OrdinalIgnoreCase);
            var parameters = closure.Parameters?.GetAllNotNull().ToList() ?? [];
            for (var i = 0; i < parameters.Count; i++)
            {
                var param = parameters[i];
                var name = param.Name?.TrimStart('$') ?? $"arg{i}";
                var typeArg = SpellParameterType(
                    param.Type,
                    inferred,
                    i,
                    resolveTypeSymbol,
                    formatClassFqn,
                    context);
                result[name] = new ParameterInfo(name, i, typeArg);
            }

            return result;
        }

        private static IExpression BuildParameterExpressionNode(ParameterInfo info, Base2Ast context)
            => NewExpressionNode(
                @"\Tyhp\Expression\ParameterExpression",
                [
                    PhpScalarAst.CreateStringFromContext(context, info.Name),
                    // Type arg may already be parented on another ParameterExpression; re-spell
                    // via EmittedPhpExprAst / clone-safe scalar when needed.
                    CloneTypeArg(info.TypeArg, context),
                    EmittedPhpExprAst.Create(info.Index.ToString(), context),
                ],
                context);

        private static IExpression CloneTypeArg(IExpression typeArg, Base2Ast context)
        {
            if (typeArg is EmittedPhpExprAst emitted)
            {
                return EmittedPhpExprAst.Create(emitted.PhpText ?? "mixed", context);
            }

            if (typeArg is PhpScalarAst scalar)
            {
                return PhpScalarAst.CreateStringFromContext(context, scalar.ValueString ?? "mixed");
            }

            return typeArg;
        }

        private static IExpression SpellParameterType(
            ITypeExpression? authoredType,
            InferredClosureSignature? inferred,
            int index,
            Func<ITypeExpression?, IBaseSymbol?> resolveTypeSymbol,
            Func<string?, string?, string> formatClassFqn,
            Base2Ast context)
        {
            if (authoredType is not null
                && resolveTypeSymbol(authoredType) is ObjectDeclarationSymbol fromAuthored)
            {
                var fqn = formatClassFqn(fromAuthored.FullyQualifiedName, fromAuthored.Name);
                return EmittedPhpExprAst.Create(fqn + "::class", context);
            }

            if (inferred?.ParameterTypes is { } inferredParams
                && index < inferredParams.Count
                && inferredParams[index] is { } inferredType)
            {
                return PropertyPathEmissionHelper.SpellCheckedTypeAsCtorArg(
                    inferredType,
                    formatClassFqn,
                    context);
            }

            if (authoredType is not null)
            {
                return PhpScalarAst.CreateStringFromContext(
                    context,
                    PropertyPathEmissionHelper.SpellTypeAsRuntimeString(
                        authoredType,
                        resolveTypeSymbol,
                        formatClassFqn));
            }

            return PhpScalarAst.CreateStringFromContext(context, "mixed");
        }

        private static string SpellReturnType(
            ITypeExpression expectedType,
            PhpInlineFunctionAst closure,
            InferredClosureSignature? inferred,
            Func<ITypeExpression?, IBaseSymbol?> resolveTypeSymbol,
            Func<string?, string?, string> formatClassFqn)
        {
            var typeArgs = PropertyPathEmissionHelper.GetGenericTypeArguments(expectedType);
            if (typeArgs.Count > 0)
            {
                return PropertyPathEmissionHelper.SpellTypeAsRuntimeString(
                    typeArgs[^1],
                    resolveTypeSymbol,
                    formatClassFqn);
            }

            if (closure.ReturnType is not null)
            {
                return PropertyPathEmissionHelper.SpellTypeAsRuntimeString(
                    closure.ReturnType,
                    resolveTypeSymbol,
                    formatClassFqn);
            }

            if (inferred?.ReturnType is { } inferredReturn)
            {
                return PropertyPathEmissionHelper.SpellCheckedTypeAsRuntimeString(
                    inferredReturn,
                    formatClassFqn);
            }

            return "mixed";
        }

        private static IExpression? RewriteNode(
            IExpression? expression,
            HashSet<string> paramNames,
            Dictionary<string, ParameterInfo> parameters,
            IReadOnlyDictionary<IBase2Ast, ICheckedType>? expressionTypes,
            Func<string?, string?, string> formatClassFqn,
            Base2Ast context)
        {
            if (expression is null)
            {
                return null;
            }

            switch (expression)
            {
                case PhpDereferenceableExpressionAst paren when paren.Expression is IExpression inner:
                    return RewriteNode(inner, paramNames, parameters, expressionTypes, formatClassFqn, context);

                case PhpVariableAst variable:
                    return RewriteVariable(
                        variable,
                        paramNames,
                        parameters,
                        expressionTypes,
                        formatClassFqn,
                        context);

                case PhpScalarAst:
                case PhpMagicConstantAst:
                    return NewExpressionNode(
                        @"\Tyhp\Expression\ConstantExpression",
                        [
                            expression,
                            PhpScalarAst.CreateStringFromContext(
                                context,
                                LookupTypeSpelling(expression, expressionTypes, formatClassFqn)),
                        ],
                        context);

                case PhpTernaryOpAst ternary:
                {
                    var condition = RewriteNode(
                        ternary.Condition, paramNames, parameters, expressionTypes, formatClassFqn, context);
                    var ifTrue = ternary.TrueExpr is null
                        ? EmittedPhpExprAst.Create("null", context)
                        : RewriteNode(
                            ternary.TrueExpr, paramNames, parameters, expressionTypes, formatClassFqn, context);
                    var ifFalse = RewriteNode(
                        ternary.FalseExpr, paramNames, parameters, expressionTypes, formatClassFqn, context);
                    if (condition is null || ifTrue is null || ifFalse is null)
                    {
                        return null;
                    }

                    return NewExpressionNode(
                        @"\Tyhp\Expression\TernaryExpression",
                        [
                            condition,
                            ifTrue,
                            ifFalse,
                            PhpScalarAst.CreateStringFromContext(
                                context,
                                LookupTypeSpelling(ternary, expressionTypes, formatClassFqn)),
                        ],
                        context);
                }

                case PhpBinaryOpAst binary:
                    return RewriteBinary(
                        binary, paramNames, parameters, expressionTypes, formatClassFqn, context);

                case PhpUnaryOpAst unary:
                    return RewriteUnary(
                        unary, paramNames, parameters, expressionTypes, formatClassFqn, context);

                case PhpNewAst newExpr:
                    return RewriteNew(
                        newExpr, paramNames, parameters, expressionTypes, formatClassFqn, context);

                case PhpDereferenceableAst deref:
                    return RewriteDereferenceable(
                        deref, paramNames, parameters, expressionTypes, formatClassFqn, context);

                default:
                    return null;
            }
        }

        private static IExpression RewriteVariable(
            PhpVariableAst variable,
            HashSet<string> paramNames,
            Dictionary<string, ParameterInfo> parameters,
            IReadOnlyDictionary<IBase2Ast, ICheckedType>? expressionTypes,
            Func<string?, string?, string> formatClassFqn,
            Base2Ast context)
        {
            var name = CheckerHelpers.GetVariableName(variable);
            if (!string.IsNullOrEmpty(name)
                && parameters.TryGetValue(name, out var parameter))
            {
                return BuildParameterExpressionNode(parameter, context);
            }

            // Captures / other variables become ConstantExpression with the variable AST as value.
            return NewExpressionNode(
                @"\Tyhp\Expression\ConstantExpression",
                [
                    variable,
                    PhpScalarAst.CreateStringFromContext(
                        context,
                        LookupTypeSpelling(variable, expressionTypes, formatClassFqn)),
                ],
                context);
        }

        private static IExpression? RewriteBinary(
            PhpBinaryOpAst binary,
            HashSet<string> paramNames,
            Dictionary<string, ParameterInfo> parameters,
            IReadOnlyDictionary<IBase2Ast, ICheckedType>? expressionTypes,
            Func<string?, string?, string> formatClassFqn,
            Base2Ast context)
        {
            var opText = binary.Operator?.ValueString ?? "";
            var token = (int)(binary.Operator?.ValueInt64 ?? -1);
            if (PhpBinaryOperatorExtensions.FromToken(token) == PhpBinaryOperator.InstanceOf
                || IsInstanceOfText(opText))
            {
                var operand = RewriteNode(
                    binary.Left, paramNames, parameters, expressionTypes, formatClassFqn, context);
                var targetType = SpellInstanceofTarget(binary.Right, formatClassFqn, context);
                if (operand is null || targetType is null)
                {
                    return null;
                }

                return NewExpressionNode(
                    @"\Tyhp\Expression\InstanceofExpression",
                    [
                        operand,
                        targetType,
                        PhpScalarAst.CreateStringFromContext(
                            context,
                            LookupTypeSpelling(binary, expressionTypes, formatClassFqn)),
                    ],
                    context);
            }

            var left = RewriteNode(
                binary.Left, paramNames, parameters, expressionTypes, formatClassFqn, context);
            var right = RewriteNode(
                binary.Right, paramNames, parameters, expressionTypes, formatClassFqn, context);
            if (left is null || right is null)
            {
                return null;
            }

            if (PhpBinaryOperatorExtensions.FromToken(token) == PhpBinaryOperator.Coalesce
                || opText == "??")
            {
                return NewExpressionNode(
                    @"\Tyhp\Expression\CoalesceExpression",
                    [
                        left,
                        right,
                        PhpScalarAst.CreateStringFromContext(
                            context,
                            LookupTypeSpelling(binary, expressionTypes, formatClassFqn)),
                    ],
                    context);
            }

            return NewExpressionNode(
                @"\Tyhp\Expression\BinaryExpression",
                [
                    left,
                    PhpScalarAst.CreateStringFromContext(context, opText),
                    right,
                    PhpScalarAst.CreateStringFromContext(
                        context,
                        LookupTypeSpelling(binary, expressionTypes, formatClassFqn)),
                ],
                context);
        }

        private static IExpression? RewriteUnary(
            PhpUnaryOpAst unary,
            HashSet<string> paramNames,
            Dictionary<string, ParameterInfo> parameters,
            IReadOnlyDictionary<IBase2Ast, ICheckedType>? expressionTypes,
            Func<string?, string?, string> formatClassFqn,
            Base2Ast context)
        {
            var operand = RewriteNode(
                unary.Operand, paramNames, parameters, expressionTypes, formatClassFqn, context);
            if (operand is null)
            {
                return null;
            }

            var token = (int)(unary.Operator?.ValueInt64 ?? -1);
            if (IsCastToken(token))
            {
                return NewExpressionNode(
                    @"\Tyhp\Expression\CastExpression",
                    [
                        PhpScalarAst.CreateStringFromContext(context, CastTokenToTypeName(token)),
                        operand,
                    ],
                    context);
            }

            var opText = unary.Operator?.ValueString ?? "";
            return NewExpressionNode(
                @"\Tyhp\Expression\UnaryExpression",
                [
                    PhpScalarAst.CreateStringFromContext(context, NormalizeUnaryOperator(opText, token)),
                    operand,
                    EmittedPhpExprAst.Create(unary.IsPrefix ? "true" : "false", context),
                    PhpScalarAst.CreateStringFromContext(
                        context,
                        LookupTypeSpelling(unary, expressionTypes, formatClassFqn)),
                ],
                context);
        }

        private static IExpression? RewriteNew(
            PhpNewAst newExpr,
            HashSet<string> paramNames,
            Dictionary<string, ParameterInfo> parameters,
            IReadOnlyDictionary<IBase2Ast, ICheckedType>? expressionTypes,
            Func<string?, string?, string> formatClassFqn,
            Base2Ast context)
        {
            var classArg = SpellClassNameAsCtorArg(newExpr.ClassName, formatClassFqn, context);
            var args = RewriteArgumentNodes(
                newExpr.Arguments, paramNames, parameters, expressionTypes, formatClassFqn, context);
            if (args is null)
            {
                return null;
            }

            return NewExpressionNode(
                @"\Tyhp\Expression\NewExpression",
                [
                    classArg,
                    BuildArray(args, context),
                ],
                context);
        }

        private static IExpression? RewriteDereferenceable(
            PhpDereferenceableAst deref,
            HashSet<string> paramNames,
            Dictionary<string, ParameterInfo> parameters,
            IReadOnlyDictionary<IBase2Ast, ICheckedType>? expressionTypes,
            Func<string?, string?, string> formatClassFqn,
            Base2Ast context)
        {
            if (deref.Base is PhpDereferenceableExpressionAst paren && deref.Suffix is null)
            {
                return RewriteNode(
                    paren.Expression, paramNames, parameters, expressionTypes, formatClassFqn, context);
            }

            if (deref.Suffix is PhpCallAst call)
            {
                if (deref.Base is PhpDereferenceableAst inner
                    && inner.Suffix is PhpInstanceMemberAccessAst member)
                {
                    // Checker rejects `?->method()`; do not emit a plain MethodCallExpression.
                    if (IsNullSafeAccessor(member.Accessor))
                    {
                        return null;
                    }

                    var objectNode = RewriteNode(
                        inner.Base as IExpression,
                        paramNames,
                        parameters,
                        expressionTypes,
                        formatClassFqn,
                        context);
                    var methodName = GetMemberName(member.MemberName);
                    var args = RewriteArgumentNodes(
                        call.Arguments, paramNames, parameters, expressionTypes, formatClassFqn, context);
                    if (objectNode is null || methodName is null || args is null)
                    {
                        return null;
                    }

                    return NewExpressionNode(
                        @"\Tyhp\Expression\MethodCallExpression",
                        [
                            objectNode,
                            PhpScalarAst.CreateStringFromContext(context, methodName),
                            BuildArray(args, context),
                            PhpScalarAst.CreateStringFromContext(
                                context,
                                LookupTypeSpelling(deref, expressionTypes, formatClassFqn)),
                        ],
                        context);
                }

                if (deref.Base is PhpDereferenceableAst staticInner
                    && TryGetStaticMemberName(staticInner.Suffix, out var staticMethodName))
                {
                    // Class::method(...) — usually PhpClassConstantAccessAst at parse time.
                    var classArg = SpellClassNameAsCtorArg(
                        staticInner.Base as IClassNameReference,
                        formatClassFqn,
                        context);
                    var args = RewriteArgumentNodes(
                        call.Arguments, paramNames, parameters, expressionTypes, formatClassFqn, context);
                    if (staticMethodName is null || args is null)
                    {
                        return null;
                    }

                    return NewExpressionNode(
                        @"\Tyhp\Expression\StaticMethodCallExpression",
                        [
                            classArg,
                            PhpScalarAst.CreateStringFromContext(context, staticMethodName),
                            BuildArray(args, context),
                            PhpScalarAst.CreateStringFromContext(
                                context,
                                LookupTypeSpelling(deref, expressionTypes, formatClassFqn)),
                        ],
                        context);
                }

                return null;
            }

            if (deref.Suffix is PhpInstanceMemberAccessAst property)
            {
                var objectNode = RewriteNode(
                    deref.Base as IExpression,
                    paramNames,
                    parameters,
                    expressionTypes,
                    formatClassFqn,
                    context);
                var propertyName = GetMemberName(property.MemberName);
                if (objectNode is null || propertyName is null)
                {
                    return null;
                }

                var nullSafe = property.Accessor?.ValueInt64 == TyhpParser.T_NULLSAFE_OBJECT_OPERATOR
                    || string.Equals(property.Accessor?.ValueString, "?->", StringComparison.Ordinal);
                var className = nullSafe
                    ? @"\Tyhp\Expression\NullSafeAccessExpression"
                    : @"\Tyhp\Expression\PropertyAccessExpression";

                return NewExpressionNode(
                    className,
                    [
                        objectNode,
                        PhpScalarAst.CreateStringFromContext(context, propertyName),
                        PhpScalarAst.CreateStringFromContext(
                            context,
                            LookupTypeSpelling(deref, expressionTypes, formatClassFqn)),
                    ],
                    context);
            }

            if (deref.Suffix is PhpArrayAccessAst arrayAccess)
            {
                var arrayNode = RewriteNode(
                    deref.Base as IExpression,
                    paramNames,
                    parameters,
                    expressionTypes,
                    formatClassFqn,
                    context);
                var indexNode = RewriteNode(
                    arrayAccess.IndexExpression,
                    paramNames,
                    parameters,
                    expressionTypes,
                    formatClassFqn,
                    context);
                if (arrayNode is null || indexNode is null)
                {
                    return null;
                }

                return NewExpressionNode(
                    @"\Tyhp\Expression\ArrayAccessExpression",
                    [
                        arrayNode,
                        indexNode,
                        PhpScalarAst.CreateStringFromContext(
                            context,
                            LookupTypeSpelling(deref, expressionTypes, formatClassFqn)),
                    ],
                    context);
            }

            if (deref.Suffix is PhpStaticMemberAccessAst or PhpClassConstantAccessAst)
            {
                // Class::CONST (PhpClassConstantAccessAst) / Class::$var — ConstantExpression.
                return NewExpressionNode(
                    @"\Tyhp\Expression\ConstantExpression",
                    [
                        deref,
                        PhpScalarAst.CreateStringFromContext(
                            context,
                            LookupTypeSpelling(deref, expressionTypes, formatClassFqn)),
                    ],
                    context);
            }

            if (deref.Suffix is null)
            {
                return RewriteNode(
                    deref.Base as IExpression,
                    paramNames,
                    parameters,
                    expressionTypes,
                    formatClassFqn,
                    context);
            }

            return null;
        }

        private static List<IExpression>? RewriteArgumentNodes(
            PhpArgumentListAst? arguments,
            HashSet<string> paramNames,
            Dictionary<string, ParameterInfo> parameters,
            IReadOnlyDictionary<IBase2Ast, ICheckedType>? expressionTypes,
            Func<string?, string?, string> formatClassFqn,
            Base2Ast context)
        {
            var result = new List<IExpression>();
            if (arguments is null)
            {
                return result;
            }

            foreach (var arg in arguments.GetAllNotNull())
            {
                if (arg.Expression is null)
                {
                    continue;
                }

                var rewritten = RewriteNode(
                    arg.Expression,
                    paramNames,
                    parameters,
                    expressionTypes,
                    formatClassFqn,
                    context);
                if (rewritten is null)
                {
                    return null;
                }

                result.Add(rewritten);
            }

            return result;
        }

        private static IExpression NewExpressionNode(
            string classFqn,
            IEnumerable<IExpression> ctorArgs,
            Base2Ast context)
        {
            var args = PhpArgumentListAst.Create(
                ctorArgs.Select(a => PhpArgumentAst.CreateFromContext(a, context)),
                context);
            return PhpNewAst.CreateFromContext(
                PhpNameAst.CreateFromContext(classFqn, context),
                args,
                context);
        }

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

        private static string LookupTypeSpelling(
            IExpression expression,
            IReadOnlyDictionary<IBase2Ast, ICheckedType>? expressionTypes,
            Func<string?, string?, string> formatClassFqn)
        {
            if (expression is IBase2Ast node
                && expressionTypes is not null
                && expressionTypes.TryGetValue(node, out var type))
            {
                return PropertyPathEmissionHelper.SpellCheckedTypeAsRuntimeString(type, formatClassFqn);
            }

            return "mixed";
        }

        private static HashSet<string> GetParameterNames(PhpInlineFunctionAst closure)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var param in closure.Parameters?.GetAllNotNull() ?? [])
            {
                var name = param.Name?.TrimStart('$');
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }

        private static string? GetMemberName(IExpression? memberName) =>
            memberName switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                PhpScalarAst scalar => scalar.ValueString,
                _ => null,
            };

        private static bool TryGetStaticMemberName(IDereferenceableSuffix? suffix, out string? name)
        {
            name = suffix switch
            {
                PhpClassConstantAccessAst classConst => GetMemberName(classConst.Member),
                PhpStaticMemberAccessAst staticMember => GetMemberName(staticMember.Member),
                _ => null,
            };
            return name is not null;
        }

        private static bool IsNullSafeAccessor(TokenValueAst? accessor) =>
            accessor?.ValueInt64 == TyhpParser.T_NULLSAFE_OBJECT_OPERATOR
            || string.Equals(accessor?.ValueString, "?->", StringComparison.Ordinal);

        private static string SpellClassNameReference(
            IClassNameReference? classRef,
            Func<string?, string?, string> formatClassFqn,
            Base2Ast context)
        {
            if (classRef is PhpNameAst name)
            {
                if (name.BoundSymbol is ObjectDeclarationSymbol obj)
                {
                    return formatClassFqn(obj.FullyQualifiedName, obj.Name).TrimStart('\\');
                }

                return (name.ValueString ?? name.Identifier ?? "mixed").TrimStart('\\');
            }

            if (classRef?.BoundSymbol is ObjectDeclarationSymbol bound)
            {
                return formatClassFqn(bound.FullyQualifiedName, bound.Name).TrimStart('\\');
            }

            return classRef?.Identifier?.TrimStart('\\') ?? "mixed";
        }

        /// <summary>
        /// Prefer <c>ClassName::class</c> for object types (runtime string FQN); fall back to a
        /// string literal for unbound / non-object spellings.
        /// </summary>
        private static IExpression SpellClassNameAsCtorArg(
            IClassNameReference? classRef,
            Func<string?, string?, string> formatClassFqn,
            Base2Ast context)
        {
            if (classRef is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol obj })
            {
                var fqn = formatClassFqn(obj.FullyQualifiedName, obj.Name);
                return EmittedPhpExprAst.Create(fqn + "::class", context);
            }

            if (classRef?.BoundSymbol is ObjectDeclarationSymbol bound)
            {
                var fqn = formatClassFqn(bound.FullyQualifiedName, bound.Name);
                return EmittedPhpExprAst.Create(fqn + "::class", context);
            }

            return PhpScalarAst.CreateStringFromContext(
                context,
                SpellClassNameReference(classRef, formatClassFqn, context));
        }

        private static string? GetTypeExpressionSimpleName(ITypeExpression? typeExpr) =>
            typeExpr switch
            {
                PhpTypeExpressionAst { Types: { } types } =>
                    GetTypeExpressionSimpleName(types.GetAllNotNull().FirstOrDefault()),
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                PhpNamedTypeAst { Name: TyhpGenericIdentifierAst generic } =>
                    generic.ValueString ?? generic.Identifier,
                PhpNamedTypeAst { Name: PhpNameAst name } =>
                    name.ValueString ?? name.Identifier,
                PhpNamedTypeAst named => named.Name?.Identifier,
                _ => typeExpr?.Identifier,
            };

        private static bool IsCastToken(int token) =>
            token is TyhpParser.T_INT_CAST
                or TyhpParser.T_BOOL_CAST
                or TyhpParser.T_STRING_CAST
                or TyhpParser.T_DOUBLE_CAST
                or TyhpParser.T_DECIMAL_CAST
                or TyhpParser.T_ARRAY_CAST
                or TyhpParser.T_OBJECT_CAST;

        private static string CastTokenToTypeName(int token) =>
            token switch
            {
                TyhpParser.T_INT_CAST => "int",
                TyhpParser.T_BOOL_CAST => "bool",
                TyhpParser.T_STRING_CAST => "string",
                TyhpParser.T_DOUBLE_CAST => "float",
                TyhpParser.T_DECIMAL_CAST => "decimal",
                TyhpParser.T_ARRAY_CAST => "array",
                TyhpParser.T_OBJECT_CAST => "object",
                _ => "mixed",
            };

        private static string NormalizeUnaryOperator(string opText, int token) =>
            token switch
            {
                TyhpParser.T_SYM_BANG => "!",
                TyhpParser.T_SYM_MINUS => "-",
                TyhpParser.T_SYM_PLUS => "+",
                TyhpParser.T_SYM_TILDE => "~",
                _ => string.IsNullOrEmpty(opText) ? "!" : opText,
            };

        private static bool IsInstanceOfText(string op) =>
            op is "instanceof" or "is" or "isa" or "isan" or "is_a" or "is_an";

        private static bool IsBuiltinInstanceofTarget(string spelling) =>
            spelling is "string" or "int" or "float" or "bool" or "null" or "void"
                or "mixed" or "never" or "array" or "object" or "callable" or "iterable"
                or "resource" or "true" or "false" or "decimal";

        /// <summary>
        /// Spells the <c>instanceof</c>/<c>is</c> RHS as a ctor argument: <c>Class::class</c>
        /// for object types, a string literal for builtins, or the captured variable AST.
        /// </summary>
        private static IExpression? SpellInstanceofTarget(
            IExpression? right,
            Func<string?, string?, string> formatClassFqn,
            Base2Ast context)
        {
            if (right is null)
            {
                return null;
            }

            if (right is PhpVariableAst)
            {
                return right;
            }

            if (right is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol obj })
            {
                var fqn = formatClassFqn(obj.FullyQualifiedName, obj.Name);
                return EmittedPhpExprAst.Create(fqn + "::class", context);
            }

            if (right is PhpNameAst name)
            {
                var spelling = (name.ValueString ?? name.Identifier ?? "").TrimStart('\\');
                if (string.IsNullOrEmpty(spelling))
                {
                    return null;
                }

                if (name.BoundSymbol is ObjectDeclarationSymbol bound)
                {
                    var fqn = formatClassFqn(bound.FullyQualifiedName, bound.Name);
                    return EmittedPhpExprAst.Create(fqn + "::class", context);
                }

                if (IsBuiltinInstanceofTarget(spelling))
                {
                    return PhpScalarAst.CreateStringFromContext(context, spelling);
                }

                // Unbound class name on an instanceof RHS (binder often leaves these unbound).
                var fqnSpelling = spelling.Contains('\\') ? "\\" + spelling : spelling;
                return EmittedPhpExprAst.Create(fqnSpelling + "::class", context);
            }

            if (right is PhpBuiltinTypeAst builtin)
            {
                return PhpScalarAst.CreateStringFromContext(context, builtin.Identifier ?? "mixed");
            }

            if (right is IClassNameReference classRef)
            {
                return SpellClassNameAsCtorArg(classRef, formatClassFqn, context);
            }

            if (right is PhpDereferenceableAst deref
                && deref.Suffix is PhpClassConstantAccessAst or PhpStaticMemberAccessAst)
            {
                return deref;
            }

            var text = GetTypeExpressionSimpleName(right as ITypeExpression);
            if (!string.IsNullOrEmpty(text))
            {
                return PhpScalarAst.CreateStringFromContext(context, text.TrimStart('\\'));
            }

            return null;
        }
    }
}
