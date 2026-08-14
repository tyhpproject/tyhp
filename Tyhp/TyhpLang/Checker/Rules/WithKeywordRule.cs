using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>Validates Tyhp <c>with</c> keyword property assignments.</summary>
    public sealed class WithKeywordRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes => [typeof(PhpBinaryOpAst)];

        public bool Handles(IBase2Ast node) =>
            node is PhpBinaryOpAst binary && IsWithOperator(binary.Operator);

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            if (node is not PhpBinaryOpAst withExpr || withExpr.Left is null || withExpr.Right is null)
            {
                return;
            }

            var targetType = context.ResolveExpressionType(withExpr.Left, state);
            var form = DetectWithForm(withExpr.Left);

            if (withExpr.Right is PhpArrayPairListAst pairList)
            {
                ValidateWithProperties(pairList, targetType, form, withExpr, state, context, diagnostics);

                if (form == WithForm.New
                    && withExpr.Left is PhpNewAst newExpr
                    && CheckerHelpers.TryGetObjectDeclaration(targetType) is { IsStruct: true } structDecl)
                {
                    context.MarkStructNewCheckedViaWith(newExpr);
                    ValidateRequiredStructProperties(
                        pairList, structDecl, withExpr, state, context, diagnostics);
                }
            }
        }

        /// <summary>
        /// Non-nullable struct properties without defaults must appear in the <c>with</c> list
        /// when constructing via <c>new Struct() with [...]</c>.
        /// </summary>
        private static void ValidateRequiredStructProperties(
            PhpArrayPairListAst pairList,
            ObjectDeclarationSymbol structDecl,
            PhpBinaryOpAst withExpr,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var provided = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in pairList.GetAllNotNull())
            {
                var propertyName = GetPropertyName(pair.KeyExpr, out _);
                if (propertyName is null)
                {
                    continue;
                }

                provided.Add(propertyName.StartsWith('$') ? propertyName[1..] : propertyName);
            }

            foreach (var property in EnumerateStructProperties(structDecl, context))
            {
                if (!IsRequiredStructProperty(property, state, context))
                {
                    continue;
                }

                var bareName = property.Name.StartsWith('$') ? property.Name[1..] : property.Name;
                if (provided.Contains(bareName))
                {
                    continue;
                }

                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    withExpr,
                    MessageCode.CheckerStructRequiredPropertyNotSet,
                    bareName,
                    structDecl.Name);
            }
        }

        private static IEnumerable<ObjectPropertySymbol> EnumerateStructProperties(
            ObjectDeclarationSymbol structDecl,
            CheckerRuleContext context)
        {
            var visited = new HashSet<ObjectDeclarationSymbol>();
            for (var current = structDecl; current is not null; current = ResolveParent(current, context))
            {
                if (!visited.Add(current))
                {
                    yield break;
                }

                foreach (var member in current.Members.Values)
                {
                    if (member is ObjectPropertySymbol property)
                    {
                        yield return property;
                    }
                }
            }
        }

        private static bool IsRequiredStructProperty(
            ObjectPropertySymbol property,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (property.DefaultValue is not null || property.DeclaredType is null)
            {
                return false;
            }

            var propType = context.ResolveTypeAnnotation(property.DeclaredType, state);
            return !propType.IsNullable;
        }

        private static void ValidateWithProperties(
            PhpArrayPairListAst pairList,
            ICheckedType targetType,
            WithForm form,
            PhpBinaryOpAst withExpr,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var objectDecl = CheckerHelpers.TryGetObjectDeclaration(targetType);

            foreach (var pair in pairList.GetAllNotNull())
            {
                var propertyName = GetPropertyName(pair.KeyExpr, out var writtenAsQuoted);
                if (propertyName is null)
                {
                    continue;
                }

                // Properties are keyed in Members with their leading '$'; the with-keyword names
                // are bare, so normalize before lookup (but keep the bare name for diagnostics).
                var propertyKey = propertyName.StartsWith('$') ? propertyName : "$" + propertyName;
                var member = objectDecl is null
                    ? null
                    : FindMemberInHierarchy(objectDecl, propertyKey, context);

                if (objectDecl is not null && member is null)
                {
                    // A quoted key occupies more source than the bare name it decodes to, so an edit
                    // span measured from the name would cut into the quotes. Those keys get the
                    // plain error; only bare keys carry a "did you mean" fix.
                    var candidates = writtenAsQuoted
                        ? Array.Empty<string>()
                        : InScopeNameCandidates.CollectPropertyNames(
                            objectDecl,
                            current => ResolveParent(current, context));

                    CheckerHelpers.ReportErrorWithDidYouMean(
                        diagnostics,
                        state,
                        pair,
                        MessageCode.CheckerWithKeywordInvalidProperty,
                        propertyName,
                        candidates,
                        propertyName,
                        targetType.DisplayName);
                    continue;
                }

                if (pair.ValueExpr is not null
                    && member is ObjectPropertySymbol property
                    && property.DeclaredType is not null)
                {
                    var propertyType = context.ResolveMemberDeclaredType(
                        property.DeclaredType, targetType, state);
                    var valueType = context.ResolveExpressionType(pair.ValueExpr, state);
                    if (!context.IsAssignable(valueType, propertyType))
                    {
                        context.CheckAssignment(pair.ValueExpr, valueType, propertyType);
                    }

                    if ((property.Visibility & MemberModifier.Readonly) != 0)
                    {
                        ValidateReadonlyWith(form, propertyName, objectDecl!, withExpr, state, context, diagnostics);
                    }
                }
            }
        }

        private static void ValidateReadonlyWith(
            WithForm form,
            string propertyName,
            ObjectDeclarationSymbol objectDecl,
            PhpBinaryOpAst withExpr,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            // In-place with can never reinitialize readonly properties.
            if (form == WithForm.InPlace)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    withExpr,
                    MessageCode.CheckerWithReadonlyInPlace,
                    propertyName);
                return;
            }

            // PHP 8.5+ native clone($obj, [...]) handles readonly for clone and new forms.
            if (IsPhpVersionAtLeast(context.Options.PhpVersion, 8, 5))
            {
                return;
            }

            // PHP < 8.5: final classes cannot be subclassed for the anonymous-class wrapper.
            if ((objectDecl.Visibility & MemberModifier.Final) != 0)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    withExpr,
                    MessageCode.CheckerWithReadonlyFinalClass,
                    objectDecl.Name);
                return;
            }

            // clone ... with on readonly requires the experimental opt-in for PHP < 8.5.
            // new ... with always has a non-reflection anonymous-class strategy (no opt-in).
            if (form == WithForm.Clone
                && !context.Options.ExperimentalReadonlyCloneWith)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    withExpr,
                    MessageCode.CheckerCloneWithReadonlyRequiresConfig,
                    propertyName);
            }
        }

        private static bool IsPhpVersionAtLeast(string? version, int major, int minor)
        {
            if (!TryParsePhpVersion(version ?? "8.4", out var parsedMajor, out var parsedMinor))
            {
                return false;
            }

            return parsedMajor > major
                || (parsedMajor == major && parsedMinor >= minor);
        }

        private static bool TryParsePhpVersion(string version, out int major, out int minor)
        {
            major = 0;
            minor = 0;
            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || !int.TryParse(parts[0], out major))
            {
                return false;
            }

            if (parts.Length >= 2)
            {
                if (!int.TryParse(parts[1], out minor))
                {
                    minor = 0;
                }
            }

            return true;
        }

        private static WithForm DetectWithForm(IExpression left)
        {
            if (left is PhpUnaryOpAst unary
                && (string.Equals(unary.Operator?.ValueString, "clone", StringComparison.OrdinalIgnoreCase)
                    || GetTokenType(unary.Operator) == TyhpParser.T_CLONE))
            {
                return WithForm.Clone;
            }

            if (left is PhpNewAst)
            {
                return WithForm.New;
            }

            return WithForm.InPlace;
        }

        /// <summary>
        /// Finds a member by key on <paramref name="objectDecl"/> or any of its ancestors.
        /// Inherited properties are not flattened into <see cref="ObjectDeclarationSymbol.Members"/>.
        /// </summary>
        private static IBaseSymbol? FindMemberInHierarchy(
            ObjectDeclarationSymbol objectDecl,
            string propertyKey,
            CheckerRuleContext context)
        {
            var visited = new HashSet<ObjectDeclarationSymbol>();
            for (var current = objectDecl; current is not null; current = ResolveParent(current, context))
            {
                if (!visited.Add(current))
                {
                    break;
                }

                if (current.Members.TryGetValue(propertyKey, out var member))
                {
                    return member;
                }
            }

            return null;
        }

        private static ObjectDeclarationSymbol? ResolveParent(
            ObjectDeclarationSymbol child,
            CheckerRuleContext context)
            => TypeComparer.TryGetParentDeclaration(child, context.SymbolTree, context.GlobalScope);

        /// <summary>
        /// Resolves a <c>with</c> key to a bare property name. Keys may be written bare
        /// (<c>name => …</c>) or quoted when the name collides with a Tyhp keyword or builtin type
        /// (<c>'type' => …</c>, <c>'class' => …</c>). Returns null when no static name is available,
        /// so a dynamic key is skipped rather than reported as a missing property.
        /// </summary>
        /// <param name="writtenAsQuoted">
        /// True when the key came from a string literal, meaning the name is shorter than the
        /// source it was written as.
        /// </param>
        private static string? GetPropertyName(IExpression? keyExpr, out bool writtenAsQuoted)
        {
            writtenAsQuoted = keyExpr is PhpScalarAst or PhpEncapsStringAst or PhpEncapsListAst;

            return keyExpr switch
            {
                PhpNameAst name => FirstNonEmpty(name.ValueString, name.Identifier),
                TokenValueAst token => FirstNonEmpty(token.ValueString, token.Identifier),
                PhpScalarAst scalar => Unquote(FirstNonEmpty(scalar.ValueString, scalar.Identifier)),
                PhpEncapsStringAst encaps => Unquote(
                    FirstNonEmpty(encaps.ValueString, encaps.TokenValue?.ValueString)),
                PhpEncapsListAst encapsList =>
                    PhpStringLiteralHelper.TryGetStaticLiteral(encapsList, out var literal)
                        ? FirstNonEmpty(literal)
                        : null,
                PhpBuiltinTypeAst builtin => FirstNonEmpty(builtin.Identifier, builtin.ValueString),
                PhpNamedTypeAst { Name: PhpNameAst typeName } => FirstNonEmpty(typeName.ValueString),
                IExpression expr => FirstNonEmpty(expr.Identifier),
                _ => null,
            };
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static string? Unquote(string? text)
        {
            if (text is null)
            {
                return null;
            }

            if (PhpStringLiteralHelper.TryDecodeQuotedTokenText(text, out var decoded))
            {
                return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
            }

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static bool IsWithOperator(TokenValueAst? op)
        {
            if (op is null)
            {
                return false;
            }

            if (GetTokenType(op) == TyhpParser.T_TYHP_WITH)
            {
                return true;
            }

            return string.Equals(op.ValueString, "with", StringComparison.OrdinalIgnoreCase)
                || string.Equals(op.Identifier, "with", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetTokenType(TokenValueAst? token) =>
            token?.ValueInt64 is long value ? (int)value : -1;

        private enum WithForm
        {
            Clone,
            New,
            InPlace,
        }
    }
}
