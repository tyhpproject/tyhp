using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
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

            // `new Name[...] with [...]` is a second list on the initializer. The bracket
            // list is already the whole entry list.
            if (IsStructInitializerExpression(withExpr.Left))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    withExpr,
                    MessageCode.CheckerStructInitializerFollowedByWith,
                    WrittenInitializerName(withExpr.Left));
                return;
            }

            var targetType = context.ResolveExpressionType(withExpr.Left, state);
            var form = DetectWithForm(withExpr.Left);

            if (withExpr.Left is PhpNewAst { IsStructInitializer: true } initializer
                && !IsConcreteStructTarget(targetType))
            {
                // A name that resolves to nothing is left to the ordinary unresolved-name
                // diagnostics; only a known non-struct is reported here.
                if (NonStructInitializerKind(initializer, targetType, state, context) is { } kind)
                {
                    var written = WrittenNewName(initializer, targetType);
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        withExpr,
                        MessageCode.CheckerStructInitializerOnNonStruct,
                        written,
                        kind,
                        NewWithListHint(initializer, targetType, written, state, context));
                }

                return;
            }

            if (withExpr.Right is PhpArrayPairListAst pairList)
            {
                ValidateWithProperties(pairList, targetType, form, withExpr, state, context, diagnostics);

                if (form == WithForm.New
                    && withExpr.Left is PhpNewAst newExpr
                    && CheckerHelpers.TryGetObjectDeclaration(targetType) is { IsStruct: true } structDecl)
                {
                    context.MarkStructNewCheckedViaWith(newExpr);
                    var bracket = newExpr.IsStructInitializer;
                    ValidateRequiredStructProperties(
                        pairList, structDecl, withExpr, bracket, state, context, diagnostics);
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
            bool bracketInitializer,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var provided = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in pairList.GetAllNotNull())
            {
                if (pair.IsSkippedSlot)
                {
                    continue;
                }

                if (pair.IsExpansion)
                {
                    AddSpreadGuaranteedNames(pair, structDecl, provided, state, context);
                    continue;
                }

                var propertyName = GetPropertyName(pair.KeyExpr, out _);
                if (propertyName is null)
                {
                    continue;
                }

                provided.Add(BareName(propertyName));
            }

            foreach (var property in EnumerateStructProperties(structDecl, context))
            {
                if (!IsRequiredStructProperty(property, state, context))
                {
                    continue;
                }

                var bareName = BareName(property.Name);
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
                    structDecl.Name,
                    RequiredPropertyConstructionHint(structDecl.Name, bareName, bracketInitializer));
            }
        }

        /// <summary>
        /// Text of the construction form a missing required field should name.
        /// A bare <c>new Struct()</c> names both spellings.
        /// </summary>
        internal static string RequiredPropertyConstructionHint(
            string structName,
            string propertyName,
            bool? bracketInitializer)
        {
            var bracket = $"new {structName}[{propertyName} => value]";
            var with = $"new {structName}() with [{propertyName} => value]";
            return bracketInitializer switch
            {
                true => bracket,
                false => with,
                _ => $"{bracket} or {with}",
            };
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
                    else if (IsConcreteStructTarget(targetType))
                    {
                        ApplyStructSpread(
                            pair, targetType, form, withExpr, state, context, diagnostics);
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
                    else if (IsConcreteStructTarget(targetType)
                        && pair.KeyExpr is null
                        && pair.ValueExpr is not null)
                    {
                        CheckerHelpers.ReportError(
                            diagnostics,
                            state,
                            pair,
                            MessageCode.CheckerStructEntryUnkeyed);
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
        /// Checks one spread in a struct initializer or struct <c>with</c> list.
        /// The source must be a concrete struct. Each of its fields must exist on the
        /// target, use the same PHP array key, and be assignable to the target field.
        /// </summary>
        private static void ApplyStructSpread(
            PhpArrayPairAst pair,
            ICheckedType targetType,
            WithForm form,
            PhpBinaryOpAst withExpr,
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
                    MessageCode.CheckerStructSpreadNotStruct,
                    targetType.DisplayName,
                    sourceType.DisplayName);
                return;
            }

            var sourceDecl = CheckerHelpers.TryGetObjectDeclaration(sourceType);
            var targetDecl = CheckerHelpers.TryGetObjectDeclaration(targetType);
            var targetShape = StructTypeHelper.TryGetPropertyShape(
                targetType,
                state,
                context.SymbolTree,
                context.GlobalScope,
                context.ResolveTypeAnnotation,
                instancePropertiesOnly: true);

            foreach (var (memberName, info) in shape.Properties)
            {
                var bare = BareName(memberName);
                var lookup = MemberKey(memberName);
                var targetProperty = targetDecl is null
                    ? null
                    : FindMemberInHierarchy(targetDecl, lookup, context) as ObjectPropertySymbol;
                StructPropertyInfo? targetInfo = null;
                if (targetShape is not null
                    && (targetShape.Properties.TryGetValue(lookup, out var found)
                        || targetShape.Properties.TryGetValue(memberName, out found)))
                {
                    targetInfo = found;
                }

                if (targetProperty is null && targetInfo is null)
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        pair,
                        MessageCode.CheckerStructSpreadExtraField,
                        bare,
                        sourceType.DisplayName,
                        targetType.DisplayName);
                    continue;
                }

                var sourceKey = StructFieldArrayKey(memberName, info);
                var targetKey = targetProperty is not null
                    ? TargetPropertyArrayKey(targetProperty)
                    : StructFieldArrayKey(lookup, targetInfo!);
                if (!string.Equals(sourceKey, targetKey, StringComparison.Ordinal))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        pair,
                        MessageCode.CheckerStructSpreadKeyMismatch,
                        bare,
                        sourceKey,
                        sourceType.DisplayName,
                        targetKey,
                        targetType.DisplayName);
                }

                if (targetProperty is { DeclaredType: not null } && targetDecl is not null)
                {
                    CheckWithValue(
                        targetProperty,
                        bare,
                        pair,
                        info.Type,
                        targetType,
                        targetDecl,
                        form,
                        withExpr,
                        state,
                        context,
                        diagnostics);
                }
                else if (targetInfo is { } shapeInfo
                    && !context.IsAssignable(info.Type, shapeInfo.Type))
                {
                    context.CheckAssignment(pair, info.Type, shapeInfo.Type);
                }
            }
        }

        /// <summary>
        /// Fields a spread contributes to the required-property set: present on the target
        /// under the same array key, and guaranteed by the source (required there, or defaulted).
        /// A nullable source field with no default may be absent, so it does not count.
        /// A later entry adds names; it does not drop a name an earlier entry already set.
        /// </summary>
        private static void AddSpreadGuaranteedNames(
            PhpArrayPairAst pair,
            ObjectDeclarationSymbol targetDecl,
            HashSet<string> provided,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (pair.ValueExpr is null)
            {
                return;
            }

            var sourceType = context.ResolveExpressionType(pair.ValueExpr, state);
            if (!TryGetConcreteStructShape(sourceType, state, context, out var shape) || shape is null)
            {
                return;
            }

            var sourceDecl = CheckerHelpers.TryGetObjectDeclaration(sourceType);
            foreach (var (memberName, info) in shape.Properties)
            {
                var lookup = MemberKey(memberName);
                if (FindMemberInHierarchy(targetDecl, lookup, context) is not ObjectPropertySymbol targetProperty)
                {
                    continue;
                }

                if (!string.Equals(
                        StructFieldArrayKey(memberName, info),
                        TargetPropertyArrayKey(targetProperty),
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (!SpreadSourceFieldIsGuaranteed(memberName, info, sourceDecl, context))
                {
                    continue;
                }

                provided.Add(BareName(memberName));
            }
        }

        /// <summary>
        /// A source field is present in every value of that struct when it is required
        /// (non-nullable, no default) or when the declaration supplies a default.
        /// </summary>
        private static bool SpreadSourceFieldIsGuaranteed(
            string memberName,
            StructPropertyInfo info,
            ObjectDeclarationSymbol? sourceDecl,
            CheckerRuleContext context)
        {
            if (sourceDecl is not null
                && FindMemberInHierarchy(sourceDecl, MemberKey(memberName), context) is ObjectPropertySymbol property
                && property.DefaultValue is not null)
            {
                return true;
            }

            return !info.Type.IsNullable;
        }

        private static string TargetPropertyArrayKey(ObjectPropertySymbol property)
        {
            if (StructTypeHelper.TryGetIntegerKeyAlias(property) is int integerKey)
            {
                return integerKey.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            var stringAlias = StructTypeHelper.TryGetStringKeyAlias(property);
            if (!string.IsNullOrEmpty(stringAlias))
            {
                return stringAlias;
            }

            return BareName(property.Name);
        }

        private static bool IsConcreteStructTarget(ICheckedType targetType)
        {
            if (targetType.IsNullable
                || targetType.IsMixed
                || targetType is UnionCheckedType or IntersectionCheckedType)
            {
                return false;
            }

            return targetType is StructCheckedType
                || CheckerHelpers.TryGetObjectDeclaration(targetType) is { IsStruct: true };
        }

        private static bool IsStructInitializerExpression(IExpression expr) =>
            expr is PhpBinaryOpAst binary
            && IsWithOperator(binary.Operator)
            && binary.Left is PhpNewAst { IsStructInitializer: true };

        private static string WrittenInitializerName(IExpression expr)
        {
            if (expr is PhpBinaryOpAst binary && binary.Left is PhpNewAst created)
            {
                return WrittenNewName(created, CheckedTypes.Unresolved);
            }

            return "struct";
        }

        private static string WrittenNewName(PhpNewAst newExpr, ICheckedType targetType)
        {
            if (newExpr.ClassName is PhpNameAst name)
            {
                var written = FirstNonEmpty(name.ValueString, name.Identifier);
                if (!string.IsNullOrWhiteSpace(written))
                {
                    return written.TrimStart('\\');
                }
            }

            return string.IsNullOrWhiteSpace(targetType.DisplayName) ? "struct" : targetType.DisplayName;
        }

        /// <summary>
        /// The kind (<c>class</c>, <c>interface</c>, <c>enum</c>, <c>object shape</c>, …) of the
        /// known non-struct named by <c>new Name[...]</c>, or null when the name is a struct
        /// or resolves to nothing.
        /// </summary>
        internal static string? NonStructInitializerKind(
            PhpNewAst newExpr,
            ICheckedType targetType,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (IsConcreteStructTarget(targetType))
            {
                return null;
            }

            return DescribeNonStructKind(newExpr, targetType, state, context);
        }

        private static string? DescribeNonStructKind(
            PhpNewAst newExpr,
            ICheckedType targetType,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (targetType.Kind == CheckedTypeKind.ObjectShape)
            {
                return "object shape";
            }

            if (ObjectShapeSupport.TryResolveTypeAlias(
                    newExpr.ClassName, NameResolutionScope(state, context), context.SymbolTree, out _))
            {
                return "type alias";
            }

            if (CheckerHelpers.TryGetObjectDeclaration(targetType) is { } declaration)
            {
                return declaration.ObjectKind switch
                {
                    PhpTypeDeclType.Interface => "interface",
                    PhpTypeDeclType.Enum => "enum",
                    PhpTypeDeclType.Trait => "trait",
                    PhpTypeDeclType.Class => "class",
                    _ => "type",
                };
            }

            var written = WrittenNewName(newExpr, targetType);
            if (written is "self" or "static" or "parent")
            {
                return "class";
            }

            return targetType.Kind is CheckedTypeKind.Unresolved ? null : "type";
        }

        /// <summary>
        /// Suffix of TYHP4412. Empty unless <c>new Name() with [...]</c> can be written
        /// for this name. A type alias is never constructed under its own name, including
        /// an alias of a class or a struct.
        /// </summary>
        private static string NewWithListHint(
            PhpNewAst newExpr,
            ICheckedType targetType,
            string writtenName,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (!CanConstructWithNewWith(newExpr, targetType, state, context))
            {
                return string.Empty;
            }

            return Message.Localize("CLI_StructInitializerWithListHint", writtenName);
        }

        /// <summary>
        /// True when <c>new Name() with [...]</c> is legal for the name after <c>new</c>.
        /// That form constructs a class. Interfaces, enums, traits, object shapes, abstract
        /// classes, and every <c>type</c> alias are not constructed that way.
        /// </summary>
        private static bool CanConstructWithNewWith(
            PhpNewAst newExpr,
            ICheckedType targetType,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (newExpr.ClassName is not null
                && ObjectShapeSupport.TryResolveTypeAlias(
                    newExpr.ClassName, NameResolutionScope(state, context), context.SymbolTree, out _))
            {
                return false;
            }

            if (targetType.Kind == CheckedTypeKind.ObjectShape)
            {
                return false;
            }

            var declaration = CheckerHelpers.TryGetObjectDeclaration(targetType);
            if (declaration is null)
            {
                var written = WrittenNewName(newExpr, targetType);
                declaration = written switch
                {
                    "self" or "static" => state.EnclosingObject,
                    "parent" when state.EnclosingObject is { } enclosing =>
                        TypeComparer.TryGetParentDeclaration(
                            enclosing, context.SymbolTree, context.GlobalScope),
                    _ => null,
                };
            }

            return declaration is { ObjectKind: PhpTypeDeclType.Class, IsStruct: false }
                && (declaration.Visibility & MemberModifier.Abstract) == 0;
        }

        private static IBaseScope NameResolutionScope(CheckerState state, CheckerRuleContext context) =>
            state.NameResolutionScope
            ?? state.EnclosingCallable?.ContainingScope
            ?? state.EnclosingObject?.ContainingScope
            ?? (IBaseScope)context.GlobalScope;

        private static string BareName(string name) =>
            name.StartsWith('$') ? name[1..] : name;

        private static string MemberKey(string name) =>
            name.StartsWith('$') ? name : "$" + name;

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
