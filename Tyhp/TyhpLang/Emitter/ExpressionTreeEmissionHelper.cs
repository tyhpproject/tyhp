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
    /// Rewrites inline expression-tree arrow functions targeting <c>Expression&lt;TCallableShape&gt;</c>
    /// parameters into a fluent <c>\Tyhp\Expr::param(...)->…->backedBy(fn)</c> chain. The runtime
    /// reads the return type from the backing closure and the tree body, so the chain does not
    /// pass one. The supported body shapes are the ones <see cref="ExpressionTreeSupport"/> accepts.
    /// </summary>
    internal static partial class ExpressionTreeEmissionHelper
    {
        /// <summary>
        /// When <paramref name="expectedType"/> is <c>Expression&lt;…&gt;</c> (not PropertyPath) and
        /// <paramref name="expression"/> is an arrow fn, returns the rewritten
        /// <c>\Tyhp\Expr::…->backedBy(fn)</c> chain. When the body has a construct the chain cannot
        /// express, <paramref name="reportUnsupported"/> receives that node and its construct name
        /// and the method returns false; the caller must not keep the <c>fn</c> as a closure.
        /// </summary>
        public static bool TryRewriteInlineFn(
            IExpression expression,
            ITypeExpression expectedType,
            Func<ITypeExpression?, IBaseSymbol?> resolveTypeSymbol,
            Func<string?, string?, string> formatClassFqn,
            Func<PhpInlineFunctionAst, InferredClosureSignature?>? getInferredSignature,
            IReadOnlyDictionary<IBase2Ast, ICheckedType>? expressionTypes,
            Base2Ast context,
            Action<IBase2Ast, string>? reportUnsupported,
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
            var parameterInfos = BuildParameterInfos(
                closure,
                inferred,
                resolveTypeSymbol,
                formatClassFqn,
                context);

            var failure = new RewriteFailure();
            var chain = RewriteFluent(
                body,
                parameterInfos,
                expressionTypes,
                formatClassFqn,
                resolveTypeSymbol,
                getInferredSignature,
                context,
                failure);
            if (chain is null)
            {
                var node = failure.Node ?? body as IBase2Ast ?? closure;
                reportUnsupported?.Invoke(node, failure.Kind ?? ExpressionTreeSupport.DescribeUnsupported(node));
                return false;
            }

            rewritten = CallInstance(
                RenderChain(chain, context),
                "backedBy",
                [closure],
                context);
            RecordRewrittenInlineFn(rewritten, closure);
            return true;
        }

        /// <summary>
        /// Emitted <c>…->backedBy(fn)</c> chains (Expression and PropertyPath) mapped to the source
        /// <c>fn</c>. Call arguments are rewritten bottom-up, so an enclosing tree meets an inner
        /// argument that is already a chain and rebuilds it from the source <c>fn</c> as a nested lambda.
        /// </summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IBase2Ast, PhpInlineFunctionAst>
            RewrittenInlineFns = new();

        internal static void RecordRewrittenInlineFn(IExpression rewritten, PhpInlineFunctionAst closure)
        {
            if (rewritten is IBase2Ast node)
            {
                RewrittenInlineFns.AddOrUpdate(node, closure);
            }
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
            if (PropertyTypeStampSupport.TrySpellLossy(authoredType) is { } detailed)
            {
                return PhpScalarAst.CreateStringFromContext(context, detailed);
            }

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

        private static bool IsCastToken(int token, string? text)
        {
            if (TrySpellCast(text, out _))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(text))
            {
                return false;
            }

            return token is TyhpParser.T_INT_CAST
                or TyhpParser.T_BOOL_CAST
                or TyhpParser.T_STRING_CAST
                or TyhpParser.T_DOUBLE_CAST
                or TyhpParser.T_DECIMAL_CAST
                or TyhpParser.T_ARRAY_CAST
                or TyhpParser.T_OBJECT_CAST;
        }

        private static string CastTokenToTypeName(int token, string? text)
        {
            if (TrySpellCast(text, out var spelled))
            {
                return spelled;
            }

            return token switch
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
        }

        /// <summary>
        /// PHP cast tokens spell as <c>(int)</c> (parens included). The spelling is the
        /// operator; numeric ids differ between <c>TyhpParser</c> and <c>TyhpdefParser</c>.
        /// </summary>
        private static bool TrySpellCast(string? text, out string typeName)
        {
            typeName = "";
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var spelled = text.Trim().Trim('(', ')').Trim().ToLowerInvariant();
            typeName = spelled switch
            {
                "int" or "integer" => "int",
                "bool" or "boolean" => "bool",
                "string" or "binary" => "string",
                "float" or "double" or "real" => "float",
                "decimal" => "decimal",
                "array" => "array",
                "object" => "object",
                _ => "",
            };
            return typeName.Length > 0;
        }

        private static string NormalizeUnaryOperator(string opText, int token)
        {
            if (!string.IsNullOrEmpty(opText))
            {
                return opText;
            }

            return token switch
            {
                TyhpParser.T_SYM_BANG => "!",
                TyhpParser.T_SYM_MINUS => "-",
                TyhpParser.T_SYM_PLUS => "+",
                TyhpParser.T_SYM_TILDE => "~",
                _ => "!",
            };
        }

        private static bool IsInstanceOfText(string op) =>
            op is "instanceof" or "is";

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

            // `$x is ?T`: the parser wraps `T` in a `?` marker. The target keeps the nullable
            // spelling (`'?string'`, `'?' . Foo::class`) so a translator sees the null case.
            if (CheckerHelpers.IsNullableInstanceofMarker(right, out var nullableOperand)
                && nullableOperand is IExpression nullableInner)
            {
                return SpellInstanceofTarget(nullableInner, formatClassFqn, context) switch
                {
                    PhpScalarAst { ValueString: { } targetText } => PhpScalarAst.CreateStringFromContext(context, "?" + targetText),
                    EmittedPhpExprAst emitted => EmittedPhpExprAst.Create("'?' . " + emitted.PhpText, context),
                    var other => other,
                };
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
