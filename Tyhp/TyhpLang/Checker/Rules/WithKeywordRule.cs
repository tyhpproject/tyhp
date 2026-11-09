using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
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
            // Struct `with` lists are a separate feature. Object lists (classes and other
            // non-struct declarations) check unkeyed entries and spreads here.
            var objectTarget = objectDecl is { IsStruct: false } ? objectDecl : null;
            Dictionary<string, ObjectWithAssignment>? pending = objectTarget is null
                ? null
                : new Dictionary<string, ObjectWithAssignment>(StringComparer.Ordinal);
            // An unkeyed entry names no property on any non-struct target, including a nullable
            // class, a union of classes, `object`, and a type parameter.
            var reportUnkeyed = objectTarget is not null || IsNonStructWithTarget(targetType);

            foreach (var pair in pairList.GetAllNotNull())
            {
                if (pair.IsSkippedSlot)
                {
                    continue;
                }

                if (pair.IsExpansion)
                {
                    if (objectTarget is not null && pending is not null)
                    {
                        ApplyObjectSpread(
                            pair, targetType, objectTarget, pending, state, context, diagnostics);
                    }

                    continue;
                }

                var propertyName = GetPropertyName(pair.KeyExpr, out var writtenAsQuoted);
                if (propertyName is null)
                {
                    if (reportUnkeyed && pair.KeyExpr is null && pair.ValueExpr is not null)
                    {
                        CheckerHelpers.ReportError(
                            diagnostics,
                            state,
                            pair,
                            MessageCode.CheckerWithEntryUnkeyed);
                    }

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
                    var bareName = propertyName.StartsWith('$') ? propertyName[1..] : propertyName;
                    var valueType = context.ResolveExpressionType(pair.ValueExpr, state);
                    if (pending is not null)
                    {
                        // A later entry or spread replaces an earlier write to the same property.
                        pending[bareName] = new ObjectWithAssignment(
                            property, bareName, valueType, pair.ValueExpr);
                    }
                    else
                    {
                        CheckWithValue(
                            property,
                            propertyName,
                            pair.ValueExpr,
                            valueType,
                            targetType,
                            objectDecl!,
                            form,
                            withExpr,
                            state,
                            context,
                            diagnostics);
                    }
                }
            }

            if (pending is null || objectTarget is null)
            {
                return;
            }

            foreach (var assignment in pending.Values)
            {
                CheckWithValue(
                    assignment.Property,
                    assignment.PropertyName,
                    assignment.ValueNode,
                    assignment.ValueType,
                    targetType,
                    objectTarget,
                    form,
                    withExpr,
                    state,
                    context,
                    diagnostics);
            }
        }

        /// <summary>
        /// True when no part of <paramref name="targetType"/> can be a struct, so the list is an
        /// object list whatever the target's exact class. Unresolved targets and structural
        /// shapes are not decided here.
        /// </summary>
        private static bool IsNonStructWithTarget(ICheckedType targetType) =>
            targetType switch
            {
                { Kind: CheckedTypeKind.Unresolved or CheckedTypeKind.Inferred or CheckedTypeKind.ObjectShape } => false,
                StructCheckedType => false,
                NullableCheckedType nullable => IsNonStructWithTarget(nullable.InnerType),
                UnionCheckedType union => union.Members.All(IsNonStructWithTarget),
                IntersectionCheckedType intersection => intersection.Members.All(IsNonStructWithTarget),
                _ => CheckerHelpers.TryGetObjectDeclaration(targetType) is not { IsStruct: true },
            };

        /// <summary>
        /// Checks one spread in an object <c>with</c> list. The source must be a concrete struct
        /// (named, generic instance, or anonymous). Each field is matched to a target property by
        /// its PHP array key — the alias when the field has one — and the last write to that
        /// property is what <see cref="CheckWithValue"/> sees.
        /// </summary>
        private static void ApplyObjectSpread(
            PhpArrayPairAst pair,
            ICheckedType targetType,
            ObjectDeclarationSymbol objectTarget,
            Dictionary<string, ObjectWithAssignment> pending,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (pair.ValueExpr is null)
            {
                return;
            }

            var sourceType = context.ResolveExpressionType(pair.ValueExpr, state);
            if (sourceType.Kind is CheckedTypeKind.Unresolved or CheckedTypeKind.Inferred)
            {
                return;
            }

            if (!TryGetConcreteStructShape(sourceType, state, context, out var shape) || shape is null)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    pair,
                    MessageCode.CheckerWithSpreadNotStruct,
                    targetType.DisplayName,
                    sourceType.DisplayName);
                return;
            }

            foreach (var (memberName, info) in shape.Properties)
            {
                var arrayKey = StructFieldArrayKey(memberName, info);
                var propertyKey = arrayKey.StartsWith('$') ? arrayKey : "$" + arrayKey;
                if (FindMemberInHierarchy(objectTarget, propertyKey, context) is not ObjectPropertySymbol property
                    || property.DeclaredType is null)
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        pair,
                        MessageCode.CheckerWithSpreadExtraField,
                        arrayKey,
                        sourceType.DisplayName,
                        targetType.DisplayName);
                    continue;
                }

                var bareName = arrayKey.StartsWith('$') ? arrayKey[1..] : arrayKey;
                pending[bareName] = new ObjectWithAssignment(property, bareName, info.Type, pair);
            }
        }

        /// <summary>
        /// A spread source whose fields can be checked: a named struct (including a generic
        /// instance) or an anonymous struct shape. Nullable structs, unions, intersections,
        /// <c>mixed</c>, arrays, and the bare <c>struct</c> bound are rejected — their keys
        /// are not a fixed set of property names.
        /// </summary>
        private static bool TryGetConcreteStructShape(
            ICheckedType sourceType,
            CheckerState state,
            CheckerRuleContext context,
            out StructCheckedType? shape)
        {
            shape = null;
            if (sourceType.IsNullable
                || sourceType.IsMixed
                || sourceType is UnionCheckedType or IntersectionCheckedType)
            {
                return false;
            }

            var isConcreteStruct = sourceType is StructCheckedType
                || CheckerHelpers.TryGetObjectDeclaration(sourceType) is { IsStruct: true };
            if (!isConcreteStruct)
            {
                return false;
            }

            shape = StructTypeHelper.TryGetPropertyShape(
                sourceType,
                state,
                context.SymbolTree,
                context.GlobalScope,
                context.ResolveTypeAnnotation,
                instancePropertiesOnly: true);
            return shape is not null;
        }

        /// <summary>
        /// PHP array key for a struct field. An alias (<c>'status_code' as $statusCode</c>,
        /// <c>0 as $arg1</c>) is the key the runtime assigns; otherwise the bare field name.
        /// </summary>
        private static string StructFieldArrayKey(string memberName, StructPropertyInfo info)
        {
            if (info.IntegerKeyAlias is int integerKey)
            {
                return integerKey.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrEmpty(info.StringKeyAlias))
            {
                return info.StringKeyAlias;
            }

            return memberName.StartsWith('$') ? memberName[1..] : memberName;
        }

        private static void CheckWithValue(
            ObjectPropertySymbol property,
            string propertyName,
            IBase2Ast valueNode,
            ICheckedType valueType,
            ICheckedType targetType,
            ObjectDeclarationSymbol objectDecl,
            WithForm form,
            PhpBinaryOpAst withExpr,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (property.DeclaredType is null)
            {
                return;
            }

            var propertyType = context.ResolveMemberDeclaredType(
                property.DeclaredType, targetType, state);
            if (!context.IsAssignable(valueType, propertyType))
            {
                context.CheckAssignment(valueNode, valueType, propertyType);
            }

            if ((property.Visibility & MemberModifier.Readonly) != 0)
            {
                ValidateReadonlyWith(form, propertyName, objectDecl, withExpr, state, context, diagnostics);
            }
        }

        private readonly record struct ObjectWithAssignment(
            ObjectPropertySymbol Property,
            string PropertyName,
            ICheckedType ValueType,
            IBase2Ast ValueNode);

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

            var targetVersion = TargetPhpVersionText(context);

            // Subclass __clone() may reassign a readonly property from PHP 8.3
            // (IS_PROP_REINITABLE). `new readonly class` is the same version, so a
            // readonly class uses that wrapper for `new … with` on 8.3 and 8.4.
            if (form == WithForm.New && !IsPhpVersionAtLeast(targetVersion, 8, 3))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    withExpr,
                    MessageCode.CheckerReadonlyWithRequiresPhp83,
                    propertyName,
                    targetVersion);
                return;
            }

            // A readonly class can only be extended by a readonly class, which cannot
            // hold the override list (no static property, no mutable property). Native
            // clone() on PHP 8.5 does not need that subclass.
            if (form == WithForm.Clone
                && (objectDecl.Visibility & MemberModifier.Readonly) != 0)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    withExpr,
                    MessageCode.CheckerReadonlyClassCloneWithRequiresPhp85,
                    objectDecl.Name,
                    targetVersion);
                return;
            }

            // The same __clone() assignment is illegal on PHP 8.2 even when the
            // experimental flag is on. Enabling the flag would still fatal.
            if (form == WithForm.Clone && !IsPhpVersionAtLeast(targetVersion, 8, 3))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    withExpr,
                    MessageCode.CheckerReadonlyWithRequiresPhp83,
                    propertyName,
                    targetVersion);
                return;
            }

            // clone ... with on readonly requires the experimental opt-in for PHP 8.3–8.4.
            // new ... with uses the anonymous-class __clone wrapper with no opt-in.
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

        private static string TargetPhpVersionText(CheckerRuleContext context)
            => string.IsNullOrWhiteSpace(context.Options.PhpVersion)
                ? CompilationOptions.DefaultPhpVersionWhenUnset
                : context.Options.PhpVersion.Trim();

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
        /// (<c>'type' => …</c>, <c>'class' => …</c>). Returns null when no static name is available.
        /// A dynamic key is skipped rather than reported as a missing property. An unkeyed entry
        /// on a class is reported by <see cref="ValidateWithProperties"/>.
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
