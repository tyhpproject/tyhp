using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    public sealed partial class TypeCompatibilityRule
    {
        private static void CheckBinaryOp(
            PhpBinaryOpAst binary,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var op = binary.Operator?.ValueString ?? string.Empty;

            // PHP 8.5 `|>`: LHS may be any value (including mixed); RHS is a callable check —
            // do not run arithmetic-style mixed-operand restrictions.
            if (op == "|>")
            {
                CheckPipe(binary, state, context, diagnostics);
                return;
            }

            // Progressive left→right narrowing for short-circuit logic. `&&` / `and`: the right
            // is only evaluated when the left is true, so it is checked under the left's positive
            // narrowing (`\is_array($x) && \array_key_exists(0, $x)`). `||` / `or`: the right is
            // only evaluated when the left is false, so it is checked under the left's negative
            // narrowing (`!\class_exists($n) || \is_subclass_of($n, $base)`). Child traversal is
            // suppressed for these ops so this walk is the sole visitor. The narrowing is applied
            // to a disposable probe (not the ambient `state`) — this node is not necessarily an
            // if/while/ternary/switch condition (real branch narrowing for those goes through a
            // dedicated `ApplyConditionNarrowing(..., thenState/loopState, ...)` call elsewhere),
            // so without a probe a bare `\is_string($x) && …;` expression statement would leak
            // `$x`'s narrowed type forward into unrelated code that follows it.
            if (TypeNarrowingRule.IsShortCircuitLogical(op)
                && binary.Left is IExpression leftExpr
                && binary.Right is not null)
            {
                var probe = state.Split(ScopeType.CodeBlock);
                context.CheckNode(leftExpr, probe);
                // Bare `mixed` as a logical operand is type-specific (Tyhp conditions are bool).
                CheckerHelpers.ReportMixedRequiresNarrowing(
                    diagnostics, state, leftExpr, context.ResolveExpressionType(leftExpr, probe));
                TypeNarrowingRule.ApplyConditionNarrowing(
                    leftExpr, probe, context, context.SymbolTree, context.GlobalScope,
                    positive: TypeNarrowingRule.IsLogicalAnd(op));
                context.CheckNode(binary.Right, probe);
                CheckerHelpers.ReportMixedRequiresNarrowing(
                    diagnostics, state, binary.Right,
                    context.ResolveExpressionType(binary.Right, probe));
                return;
            }

            if (!IsAssignmentOperator(op))
            {
                if (CheckerHelpers.IsInstanceofLikeOperator(binary) && binary.Right is not null)
                {
                    CheckerHelpers.ResolveInstanceofTargetType(
                        binary.Right,
                        state,
                        context,
                        context.SymbolTree,
                        context.GlobalScope,
                        diagnostics);
                }

                CheckMixedRestrictedBinaryOperands(binary, op, state, context, diagnostics);
                CheckObjectOperatorOverloadApplicability(binary, op, state, context, diagnostics);
                return;
            }

            if (binary.Left is null || binary.Right is null)
            {
                return;
            }

            if (op == "="
                && ArrayAccessDestructureSupport.TryGetPattern(binary.Left, out var destructurePattern))
            {
                var destructureSource = context.ResolveExpressionType(binary.Right, state);
                ArrayAccessDestructureSupport.Check(
                    destructurePattern,
                    destructureSource,
                    binary,
                    state,
                    context,
                    diagnostics);
                return;
            }

            // Simple `$recv->prop =` / `$recv[$k] =` skips child traversal of the left
            // (NullSafetyRule treats it as a write, not a read). Still validate the receiver
            // for mixed (TYHP4160) and Unresolved (TYHP4197) use-sites.
            //
            // `??=`'s left is an existence probe, not a plain write target: NullSafetyRule
            // re-visits it under `IsExistenceProbeContext` (same as bare `??`) so a receiver
            // the checker could not resolve does not report there. Checking it again here,
            // unconditionally and before that probe flag is set (`TypeCompatibilityRule` runs
            // before `NullSafetyRule` for this same node), would report TYHP4197 / TYHP4160 on
            // `$hole->x ??= 1` / `$m->x ??= 1` even though `$hole->x ?? 1` / `$m->x ?? 1` are
            // correctly silent — so skip the early check for `??=` and let NullSafetyRule's
            // probed re-visit be the sole check, matching bare `??`.
            if (op != "??=" && binary.Left is PhpDereferenceableAst leftDeref)
            {
                context.MarkImportNames(leftDeref, state);
                CheckDereferenceable(leftDeref, state, context, diagnostics);
            }

            if (state.TrackArrayAccessShapeOffsetSetCoverage
                && op == "="
                && binary.Right is PhpVariableAst valueVar
                && !string.IsNullOrEmpty(state.ArrayAccessShapeValueParameterName)
                && string.Equals(
                    CheckerHelpers.GetVariableName(valueVar),
                    state.ArrayAccessShapeValueParameterName,
                    StringComparison.OrdinalIgnoreCase))
            {
                state.HasArrayAccessShapeCoverage = true;
            }

            // Compound arithmetic/bitwise/concat assigns read the left as an operand of a
            // type-specific operator — reject unnarrowed `mixed` there (plain `=` / `??=` do not).
            if (op is not ("=" or "??="))
            {
                CheckerHelpers.ReportMixedRequiresNarrowing(
                    diagnostics, state, binary.Left,
                    context.ResolveExpressionType(binary.Left, state));
                CheckerHelpers.ReportMixedRequiresNarrowing(
                    diagnostics, state, binary.Right,
                    context.ResolveExpressionType(binary.Right, state));
                CheckObjectOperatorOverloadApplicability(binary, op, state, context, diagnostics);
            }

            ICheckedType? assignmentTargetForContext = null;
            if (op is "=" or "??=")
            {
                assignmentTargetForContext = ResolveAssignmentTargetType(binary.Left, state, context);
            }

            var previousExpected = state.ExpectedExpressionType;
            if (ContextualNewInference.IsUsableExpectedType(assignmentTargetForContext))
            {
                state.ExpectedExpressionType = assignmentTargetForContext;
            }

            try
            {
                if (binary.Right is PhpNewAst newExpr)
                {
                    CheckNew(newExpr, state, context, diagnostics);
                }

                // For a compound assignment (`+=`, `.=`, etc.) the value assigned back to the target is
                // the RESULT of the operation, not the bare right operand. Resolving the whole binary
                // node routes through the operator inference (including operator overloads, which yield
                // an unknown/permissive type for object operands), matching how plain `$a + $b` is
                // treated. Using only `binary.Right` here wrongly rejected `$money += 10;`.
                var isCompoundAssignment = op != "=";
                var sourceType = isCompoundAssignment
                    ? context.ResolveExpressionType(binary, state)
                    : context.ResolveExpressionType(binary.Right, state);

                // `$arr[] = $v` / `$arr[$k] = $v` on an unannotated / empty array local: grow a
                // concrete map/list type (`array<int, …>` / `array<K, …>`) so later `\implode` /
                // `\array_reverse` see resolved keys. Skip the element-vs-placeholder check
                // (`int` ↛ `never`) when we just refined.
                var refinedOpenArrayAppend = op == "="
                    && ArrayAppendInference.TryRefineLocal(
                        binary.Left,
                        sourceType,
                        state,
                        context.SymbolTree,
                        context.GlobalScope,
                        expr => context.ResolveExpressionType(expr, state));

                var targetType = assignmentTargetForContext
                    ?? ResolveAssignmentTargetType(binary.Left, state, context);

                if (!refinedOpenArrayAppend && targetType is not UnresolvedCheckedType)
                {
                    // `??=` assigns the right operand when the left is null, so a bag literal on
                    // the right is the value that lands in the target — same as plain `=`.
                    var bagChecked = op is "=" or "??="
                        && StructBagLiteralChecker.TryCheck(
                            binary.Right, targetType, state, context, diagnostics);
                    if (GeneratorBodyInference.TryHandleYieldSendAssignment(binary.Right, targetType, state))
                    {
                        // Unpinned TSend: yield currently types as mixed; the assignment target
                        // constrains TSend instead of reporting mixed↛T.
                    }
                    else if (!bagChecked
                        && !context.IsAssignable(sourceType, targetType, state)
                        && !CheckerHelpers.IsArrayCallableLiteral(binary.Right, targetType, context, state))
                    {
                        if (!CheckerHelpers.TryReportObjectShapeRequiresGuard(
                                diagnostics, state, binary, sourceType, targetType)
                            && !CheckerHelpers.TryReportCallableShapeRequiresGuard(
                                diagnostics, state, binary, sourceType, targetType)
                            && !CheckerHelpers.TryReportNewConstraintFailure(
                                diagnostics, state, binary, sourceType, targetType,
                                context.SymbolTree, context.GlobalScope)
                            && !SymbolNameTypeAssignability.TryReportLiteralExistenceFailure(
                                sourceType, targetType, state, context.SymbolTree, context.GlobalScope,
                                diagnostics, binary))
                        {
                            CheckerHelpers.ReportError(
                                diagnostics, state, binary, MessageCode.CheckerTypeMismatch,
                                sourceType.DisplayName, targetType.DisplayName);
                        }
                    }
                }

                if (IsReadonlyAssignmentTarget(binary.Left, state))
                {
                    var memberName = binary.Left is PhpDereferenceableAst
                    {
                        Suffix: PhpInstanceMemberAccessAst memberAccess
                    }
                        ? GetExpressionText(memberAccess.MemberName)
                        : null;
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        binary,
                        MessageCode.CheckerReadonlyPropertyReassigned,
                        memberName ?? "?");
                }

                if (binary.Left is PhpVariableAst variable)
                {
                    var name = CheckerHelpers.GetVariableName(variable);
                    if (name is not null)
                    {
                        // Drop index/member-access narrowing keyed on this receiver — AssignVariable
                        // overwrites the variable's own NarrowedType, but structural maps are separate.
                        TypeNarrowingRule.ResetNarrowingOnAssignment(name, state);
                        state.AssignVariable(name, sourceType, diagnostics);
                    }
                }
                else if (TypeNarrowingRule.TryGetTrackedPropertyKey(binary.Left, state, out var propertyKey)
                    && IsDefinitePropertyInitializingAssignment(op))
                {
                    // Track both definite init and post-assignment type so later `$this->prop !== null`
                    // / `self::$prop` reads see the RHS (mirrors AssignVariable for locals).
                    state.AssignPropertyType(propertyKey!, sourceType);
                }
            }
            finally
            {
                state.ExpectedExpressionType = previousExpected;
            }
        }

        /// <summary>
        /// Rejects unnarrowed <c>mixed</c> operands of type-specific binary operators.
        /// Assignment, comparison, coalesce, and <c>instanceof</c>/<c>is</c> are allowed
        /// (Story 08: only those are valid for all types / needed to narrow).
        /// </summary>
        private static void CheckMixedRestrictedBinaryOperands(
            PhpBinaryOpAst binary,
            string op,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (binary.Left is null)
            {
                return;
            }

            // Comparison / coalesce / instanceof: allowed on mixed (enable narrowing).
            if (IsMixedAllowedBinaryOperator(op))
            {
                return;
            }

            // Logical `||` / `or` / `xor` and every arithmetic/bitwise/concat op: restricted.
            CheckerHelpers.ReportMixedRequiresNarrowing(
                diagnostics, state, binary.Left,
                context.ResolveExpressionType(binary.Left, state));

            if (binary.Right is not null)
            {
                CheckerHelpers.ReportMixedRequiresNarrowing(
                    diagnostics, state, binary.Right,
                    context.ResolveExpressionType(binary.Right, state));
            }
        }

        private static bool IsMixedAllowedBinaryOperator(string op) =>
            op is "==" or "!=" or "===" or "!==" or "<" or ">" or "<=" or ">=" or "<=>"
                or "??"
                or "instanceof" or "is";

        /// <summary>
        /// True only for the two operators that guarantee <c>$this->prop</c> holds a value
        /// afterward *without* first reading the (possibly uninitialized) current value: plain
        /// <c>=</c>, and <c>??=</c> (which PHP treats as an existence probe on the left — it never
        /// throws on an uninitialized typed property, unlike a read). Compound arithmetic/string
        /// operators (<c>+=</c>, <c>.=</c>, …) read-then-write, so — unlike <see cref="AssignProperty"/>
        /// firing here before <see cref="NullSafetyRule"/> walks the same left operand as a child —
        /// marking the property initialized for those would suppress the legitimate TYHP4157 for the
        /// implicit read (dispatch runs this rule on the parent binary node, marking initialized,
        /// *before* recursing into <c>binary.Left</c> where the read is actually checked).
        /// </summary>
        private static bool IsDefinitePropertyInitializingAssignment(string op) =>
            op is "=" or "??=";

        // An assignment must conform to the variable's DECLARED type, not its currently narrowed type.
        // A narrowed variable (e.g. inside `if ($x instanceof T)`) may still be reassigned any value of
        // its declared type, so checking against the narrowed type would wrongly reject the assignment.
        private static ICheckedType ResolveAssignmentTargetType(
            IBase2Ast left,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (left is PhpVariableAst variable
                && CheckerHelpers.GetVariableName(variable) is { } name
                && state.LookupVariable(name) is { NarrowedType: not null, DeclaredType: { } declared })
            {
                return declared;
            }

            // `$this->prop` / `self::$prop` — assignment is against the declared slot type, not
            // the current refinement. Inside `if (self::$x === null) { self::$x = new self(); }`
            // the then-branch has narrowed the read to null; writing must still see `?self`.
            if (left is IExpression leftExpr
                && TypeNarrowingRule.TryGetTrackedPropertyKey(leftExpr, state, out var trackedKey)
                && state.LookupPropertyInit(trackedKey!) is not null
                && state.EnclosingObject is { } enclosingObject
                && context.SymbolTree.ResolveMember(trackedKey!, enclosingObject, new DiagnosticBag())
                    is ObjectPropertySymbol { DeclaredType: { } declaredAst })
            {
                return context.ResolveTypeAnnotation(declaredAst, state);
            }

            // `$arr[1] = value` — index-access control-flow narrowing (e.g. from a prior
            // `\is_string($arr[1])` guard) describes what an earlier *read* observed, not a
            // constraint on what may be *written*. Without this, writing a new value of a
            // different type to a narrowed slot was wrongly rejected against the stale narrowed
            // type instead of the array's real element type — and the narrowing must also be
            // dropped so a subsequent read in the same branch does not keep seeing the old type.
            if (left is PhpDereferenceableAst { Base: PhpVariableAst arrayVar, Suffix: PhpArrayAccessAst arrayAccess }
                && TypeNarrowingRule.TryGetIndexAccessKey(arrayVar, arrayAccess, out var indexKey)
                && state.RemoveIndexAccessNarrowing(indexKey!))
            {
                return context.ResolveExpressionType(left, state);
            }

            // `$obj->prop = value` — same invalidate-on-write for MemberAccessNarrowing.
            if (left is PhpDereferenceableAst { Base: PhpVariableAst objVar, Suffix: PhpInstanceMemberAccessAst memberAccess }
                && TypeNarrowingRule.TryGetMemberAccessKey(objVar, memberAccess, out var memberKey)
                && state.RemoveMemberAccessNarrowing(memberKey!))
            {
                return context.ResolveExpressionType(left, state);
            }

            return context.ResolveExpressionType(left, state);
        }

        private static bool IsAssignmentOperator(string op) =>
            op is "=" or "+=" or "-=" or "*=" or "/=" or ".=" or "%=" or "**="
                or "&=" or "|=" or "^=" or "<<=" or ">>=" or "??=";

        private static bool IsReadonlyAssignmentTarget(IExpression left, CheckerState state)
        {
            if (left is not PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst memberAccess } dereferenceable)
            {
                return false;
            }

            var memberName = GetExpressionText(memberAccess.MemberName);
            if (memberName is null || state.EnclosingObject is null)
            {
                return false;
            }

            var propertyKey = memberName.StartsWith('$') ? memberName : "$" + memberName;
            if (state.EnclosingObject.Members.TryGetValue(propertyKey, out var member) is not true
                || member is not ObjectPropertySymbol { Visibility: var visibility }
                || (visibility & MemberModifier.Readonly) == 0)
            {
                return false;
            }

            // PHP allows a readonly property to be written once from within the declaring class's own
            // scope (constructors are the overwhelmingly common case, but any instance method may do
            // the deferred initialization) — this is a call-site-shape check, not full "written exactly
            // once" data-flow analysis, so `$this->prop = ...` is unconditionally allowed here and PHP's
            // own runtime `Error: Cannot modify readonly property` remains the backstop against a second
            // write. Only `$other->prop = ...` (a receiver other than `$this`) is flagged: PHP rejects
            // that unconditionally, even from inside the declaring class.
            return dereferenceable.Base is not PhpVariableAst receiver || !CheckerHelpers.IsThisVariable(receiver);
        }

        private static ICheckedType ResolveNewClassType(
            IClassNameReference classRef,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (classRef is ITypeExpression typeExpr)
            {
                return context.ResolveTypeAnnotation(typeExpr, state);
            }

            if (classRef is PhpBuiltinTypeAst builtin)
            {
                return context.ResolveTypeAnnotation(builtin, state);
            }

            return context.ResolveExpressionType((IBase2Ast)classRef, state);
        }

        private static void CheckNew(
            PhpNewAst newExpr,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var classRef = newExpr.ClassName;
            if (classRef is null)
            {
                return;
            }

            if (TryReportObjectShapeUsedAsClass(classRef, state, context, diagnostics)
                || TryReportTypeAliasUsedAsNewTarget(classRef, state, context, diagnostics))
            {
                return;
            }

            var classType = context.ResolveExpressionType(newExpr, state);
            if (TryGetGenericTypeParameter(classRef, classType, state, out var typeParam))
            {
                var paramType = CheckedTypes.FromSymbol(typeParam);
                if (TypeComparer.TryGetNewUtility(paramType, out var newUtility))
                {
                    if (IsObjectGenericSimpleName(typeParam.Name, state))
                    {
                        context.MarkRequiresRuntimeGenericTracking(state.EnclosingObject);
                    }

                    ValidateNewShapeConstructorArguments(
                        newExpr, typeParam.Name, newUtility, state, context, diagnostics);
                    return;
                }

                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    newExpr,
                    MessageCode.CheckerNewTypeParameterRequiresNew,
                    typeParam.Name);
                return;
            }

            if (TryCheckNewOnClassNameBrand(newExpr, classRef, state, context, diagnostics))
            {
                return;
            }

            // `new T()` where T is an object generic type parameter → runtime tracking.
            if (IsObjectGenericTypeParameterName(classRef, state))
            {
                context.MarkRequiresRuntimeGenericTracking(state.EnclosingObject);
            }

            // Passing a class type-parameter as a type argument (e.g. `new Collection<T>()`).
            if (classRef is TyhpGenericIdentifierAst genericId
                && GenericTypeArgsReferenceObjectParam(genericId, state))
            {
                context.MarkRequiresRuntimeGenericTracking(state.EnclosingObject);
            }

            // Prefer the expression-inferred type (same path as InferNew): named structs often
            // resolve as StructCheckedType via type annotation, which TryGetObjectDeclaration
            // cannot unwrap. Expression inference yields SimpleCheckedType + ObjectDeclarationSymbol.
            var objectDecl = CheckerHelpers.TryGetObjectDeclaration(classType)
                ?? classRef.BoundSymbol as ObjectDeclarationSymbol
                ?? CheckerHelpers.TryGetObjectDeclaration(ResolveNewClassType(classRef, state, context));

            if (objectDecl is null)
            {
                var annotationType = ResolveNewClassType(classRef, state, context);
                if (IsNonInstantiableBuiltin(annotationType) || IsNonInstantiableClassName(classRef))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, newExpr, MessageCode.CheckerCannotInstantiateNonClass,
                        annotationType.DisplayName);
                }

                return;
            }

            if (objectDecl.IsExtern)
            {
                return;
            }

            switch (objectDecl.ObjectKind)
            {
                case PhpTypeDeclType.Trait:
                    CheckerHelpers.ReportError(
                        diagnostics, state, newExpr, MessageCode.CheckerCannotInstantiateTrait, objectDecl.Name);
                    break;
                case PhpTypeDeclType.Interface:
                    CheckerHelpers.ReportError(
                        diagnostics, state, newExpr, MessageCode.CheckerCannotInstantiateInterface, objectDecl.Name);
                    break;
                case PhpTypeDeclType.Enum:
                    CheckerHelpers.ReportError(
                        diagnostics, state, newExpr, MessageCode.CheckerCannotInstantiateEnum, objectDecl.Name);
                    break;
                default:
                    if ((objectDecl.Visibility & MemberModifier.Abstract) != 0)
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, state, newExpr, MessageCode.CheckerAbstractClassInstantiated, objectDecl.Name);
                    }

                    if (objectDecl.IsStruct
                        && !context.IsStructNewCheckedViaWith(newExpr))
                    {
                        ReportMissingRequiredStructProperties(newExpr, objectDecl, state, context, diagnostics);
                    }

                    ValidateConstructorCallArguments(
                        newExpr, objectDecl, classType, state, context, diagnostics);

                    break;
            }
        }

        /// <summary>
        /// Validates named-argument form and argument types against <c>__construct</c> (or the
        /// implicit empty parameter list when no constructor is declared).
        /// </summary>
        private static void ValidateConstructorCallArguments(
            PhpNewAst newExpr,
            ObjectDeclarationSymbol objectDecl,
            ICheckedType constructedType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            IReadOnlyList<ParameterInfo> parameters = [];
            ObjectMethodSymbol? ctor = null;
            if (context.SymbolTree.ResolveMember("__construct", objectDecl, new DiagnosticBag())
                is ObjectMethodSymbol resolvedCtor)
            {
                ctor = resolvedCtor;
                parameters = ctor.Parameters;
                CheckMemberVisibility(ctor, state, newExpr, diagnostics);
            }

            ValidateArgumentArity(
                newExpr.Arguments,
                parameters,
                state,
                diagnostics,
                newExpr,
                objectDecl.Name);

            if (newExpr.Arguments is null)
            {
                return;
            }

            ValidateNamedArguments(newExpr.Arguments, state, newExpr, diagnostics);
            // Pass the constructed receiver (`new static<T>` → `Promise<T>`) so constructor
            // parameters like `callable(): TReturn` substitute class generics the same way
            // instance-method calls already do via ResolveMemberDeclaredType.
            ValidateArgumentTypes(
                newExpr.Arguments,
                parameters,
                state,
                context,
                diagnostics,
                selfResolutionReceiver: constructedType,
                calleeMethod: ctor);
        }

        /// <summary>
        /// Bare <c>new Struct()</c> (no <c>with</c>) must not omit required properties.
        /// </summary>
        private static void ReportMissingRequiredStructProperties(
            PhpNewAst newExpr,
            ObjectDeclarationSymbol structDecl,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var visited = new HashSet<ObjectDeclarationSymbol>();
            for (var current = structDecl;
                 current is not null;
                 current = TypeComparer.TryGetParentDeclaration(current, context.SymbolTree, context.GlobalScope))
            {
                if (!visited.Add(current))
                {
                    break;
                }

                foreach (var member in current.Members.Values)
                {
                    if (member is not ObjectPropertySymbol property
                        || property.DefaultValue is not null
                        || property.DeclaredType is null)
                    {
                        continue;
                    }

                    var propType = context.ResolveTypeAnnotation(property.DeclaredType, state);
                    if (propType.IsNullable)
                    {
                        continue;
                    }

                    var bareName = property.Name.StartsWith('$') ? property.Name[1..] : property.Name;
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        newExpr,
                        MessageCode.CheckerStructRequiredPropertyNotSet,
                        bareName,
                        structDecl.Name);
                }
            }
        }

        private static bool IsObjectGenericTypeParameterName(IClassNameReference classRef, CheckerState state)
        {
            var name = GetClassNameText(classRef)?.TrimStart('\\');
            if (string.IsNullOrEmpty(name) || name.Contains('\\'))
            {
                return false;
            }

            return state.ObjectGenerics.Any(gp => string.Equals(gp.Name, name, StringComparison.Ordinal));
        }

        private static bool TryGetGenericTypeParameter(
            IClassNameReference classRef,
            ICheckedType classType,
            CheckerState state,
            out GenericTypeParameterSymbol typeParam)
        {
            if (classType is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol fromType })
            {
                typeParam = fromType;
                return true;
            }

            var name = GetClassNameText(classRef)?.TrimStart('\\');
            if (string.IsNullOrEmpty(name) || name.Contains('\\'))
            {
                typeParam = null!;
                return false;
            }

            typeParam = state.FunctionGenerics.FirstOrDefault(gp =>
                    string.Equals(gp.Name, name, StringComparison.Ordinal))
                ?? state.ObjectGenerics.FirstOrDefault(gp =>
                    string.Equals(gp.Name, name, StringComparison.Ordinal))!;
            return typeParam is not null;
        }

        /// <summary>
        /// <c>new $cls(...)</c> when <c>$cls</c> is a <c>__ClassName&lt;T&gt;</c> brand.
        /// Shape brands without <c>__New</c> are TYHP4356; <c>__ClassName&lt;__New&lt;Shape&gt;&gt;</c>
        /// checks arguments against the shape constructor (TYHP4357). Bare
        /// <c>__ClassName</c> / <c>__ClassName&lt;object&gt;</c> / <c>__ClassName&lt;Nominal&gt;</c>
        /// keep existing dynamic-<c>new</c> (allowed, no shape ctor check).
        /// </summary>
        private static bool TryCheckNewOnClassNameBrand(
            PhpNewAst newExpr,
            IClassNameReference classRef,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var classNameType = ResolveNewClassType(classRef, state, context);
            if (!SymbolNameTypeHelper.TryGetClassNameBrandArgument(classNameType, out var brand))
            {
                return false;
            }

            if (TypeComparer.TryGetNewUtility(brand, out var newUtility))
            {
                var display = GetClassNameBrandNewDisplay(classRef, classNameType);
                ValidateNewShapeConstructorArguments(
                    newExpr, display, newUtility, state, context, diagnostics);
                return true;
            }

            if (IsClassNameBrandShapeWithoutNew(brand))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    newExpr,
                    MessageCode.CheckerNewClassNameRequiresNew,
                    GetClassNameBrandNewDisplay(classRef, classNameType),
                    classNameType.DisplayName);
                return true;
            }

            return true;
        }

        private static bool IsClassNameBrandShapeWithoutNew(ICheckedType brand)
        {
            if (TypeComparer.TryGetNewUtility(brand, out _))
            {
                return false;
            }

            if (brand is SimpleCheckedType
                {
                    ResolvedSymbol: GenericTypeParameterSymbol { ResolvedConstraint: { } constraint },
                })
            {
                return IsClassNameBrandShapeWithoutNew(constraint);
            }

            return TypeComparer.IsStructuralClassNameBrandArgument(brand);
        }

        private static string GetClassNameBrandNewDisplay(
            IClassNameReference classRef,
            ICheckedType classNameType)
        {
            if (classRef is PhpVariableAst variable)
            {
                var name = CheckerHelpers.GetVariableName(variable);
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }

            var written = GetClassNameText(classRef)?.TrimStart('$', '\\');
            return string.IsNullOrEmpty(written) ? classNameType.DisplayName : written;
        }

        /// <summary>
        /// <c>new T(...)</c> / <c>new $cls(...)</c> argument lists are checked against the
        /// <em>shape</em> constructor (TYHP4357), not every prefix the eventual class happens
        /// to accept. Named arguments (<c>new T(b: 1, a: $s)</c>) are matched against the
        /// selected facet's <see cref="CallableCheckedType.ParameterNames"/> the same way a
        /// nominal <c>new Foo(...)</c> matches them against <c>ParameterInfo.Name</c> — the
        /// shape ctor arity is not just the positional-argument count.
        /// </summary>
        private static void ValidateNewShapeConstructorArguments(
            PhpNewAst newExpr,
            string targetDisplay,
            GenericCheckedType newUtility,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var shapeType = newUtility.TypeArguments[0];
            var ctor = TypeComparer.GetShapeConstructorCallable(
                shapeType, context.SymbolTree, context.GlobalScope);

            if (newExpr.Arguments is not null)
            {
                ValidateNamedArguments(newExpr.Arguments, state, newExpr, diagnostics);
            }

            var positionalCount = CallableArityFacetBuilder.CountPositionalArguments(newExpr.Arguments);
            var namedCount = CountNamedArguments(newExpr.Arguments);
            if (!CallableArityFacetBuilder.TrySelectCallableFacet(
                    ctor, positionalCount + namedCount, out var facet)
                || facet is null)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    newExpr,
                    MessageCode.CheckerObjectShapeConstructorArgumentMismatch,
                    targetDisplay,
                    shapeType.DisplayName);
                return;
            }

            if (newExpr.Arguments is null)
            {
                return;
            }

            var index = 0;
            foreach (var arg in newExpr.Arguments.GetAllNotNull())
            {
                if (arg.IsVariadic || arg.Expression is null)
                {
                    continue;
                }

                ICheckedType expected;
                if (arg.Name is not null)
                {
                    var namedIndex = FindFacetParameterIndexByName(facet, arg.Name.ValueString);
                    if (namedIndex < 0)
                    {
                        CheckerHelpers.ReportError(
                            diagnostics,
                            state,
                            arg,
                            MessageCode.CheckerObjectShapeConstructorArgumentMismatch,
                            targetDisplay,
                            shapeType.DisplayName);
                        continue;
                    }

                    expected = facet.ParameterTypes[namedIndex];
                }
                else if (facet.LastParameterIsVariadic && index >= facet.ParameterTypes.Count - 1)
                {
                    expected = facet.ParameterTypes[^1];
                }
                else if (index < facet.ParameterTypes.Count)
                {
                    expected = facet.ParameterTypes[index];
                }
                else
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        newExpr,
                        MessageCode.CheckerObjectShapeConstructorArgumentMismatch,
                        targetDisplay,
                        shapeType.DisplayName);
                    return;
                }

                var argType = context.ResolveExpressionType(arg.Expression, state);
                if (!context.IsAssignable(argType, expected, state)
                    && !CheckerHelpers.IsArrayCallableLiteral(arg.Expression, expected, context, state))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        arg,
                        MessageCode.CheckerObjectShapeConstructorArgumentMismatch,
                        targetDisplay,
                        shapeType.DisplayName);
                }

                if (arg.Name is null)
                {
                    index++;
                }
            }
        }

        private static int CountNamedArguments(PhpArgumentListAst? arguments)
        {
            if (arguments is null)
            {
                return 0;
            }

            var count = 0;
            foreach (var argument in arguments.GetAllNotNull())
            {
                if (!argument.IsVariadic && argument.Name is not null)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Case-insensitive lookup of <paramref name="name"/> (no leading <c>$</c>, matching
        /// PHP named-argument syntax) in the selected facet's parameter names.
        /// </summary>
        private static int FindFacetParameterIndexByName(CallableCheckedType facet, string? name) =>
            CallableArityFacetBuilder.FindParameterIndexByName(facet, name);

        private static bool GenericTypeArgsReferenceObjectParam(TyhpGenericIdentifierAst genericId, CheckerState state)
        {
            if (state.ObjectGenerics.Count == 0)
            {
                return false;
            }

            if (genericId.GenericArguments is not PhpTypeExpressionListAst typeArgs)
            {
                return false;
            }

            foreach (var arg in typeArgs.GetAllNotNull())
            {
                if (TypeExpressionReferencesObjectGeneric(arg, state))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TypeExpressionReferencesObjectGeneric(ITypeExpression? typeExpr, CheckerState state)
        {
            if (typeExpr is null || state.ObjectGenerics.Count == 0)
            {
                return false;
            }

            if (typeExpr is PhpNamedTypeAst named)
            {
                return named.Name switch
                {
                    TyhpGenericIdentifierAst g => GenericTypeArgsReferenceObjectParam(g, state),
                    PhpNameAst n => IsObjectGenericSimpleName(n.ValueString, state),
                    ITypeExpression inner => TypeExpressionReferencesObjectGeneric(inner, state),
                    _ => false,
                };
            }

            if (typeExpr is PhpNameAst name)
            {
                return IsObjectGenericSimpleName(name.ValueString, state);
            }

            if (typeExpr is TyhpGenericIdentifierAst nested)
            {
                return GenericTypeArgsReferenceObjectParam(nested, state);
            }

            if (typeExpr is PhpTypeExpressionAst composite && composite.Types is { } members)
            {
                foreach (var member in members.GetAllNotNull())
                {
                    if (TypeExpressionReferencesObjectGeneric(member, state))
                    {
                        return true;
                    }
                }
            }

            foreach (var child in typeExpr.AstChildren)
            {
                if (child is ITypeExpression childType
                    && TypeExpressionReferencesObjectGeneric(childType, state))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryReportObjectShapeUsedAsClass(
            IBase2Ast node,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var scope = state.NameResolutionScope
                ?? state.EnclosingCallable?.ContainingScope
                ?? state.EnclosingObject?.ContainingScope
                ?? (IBaseScope)context.GlobalScope;

            if (!ObjectShapeSupport.TryResolveObjectShapeAlias(
                    node, scope, context.SymbolTree, out var alias))
            {
                return false;
            }

            CheckerHelpers.ReportError(
                diagnostics,
                state,
                node,
                MessageCode.CheckerObjectShapeUsedAsClass,
                alias.Name);
            return true;
        }

        /// <summary>
        /// <c>new Foo()</c> where <c>Foo</c> is a <c>type</c> alias is never instantiating a
        /// class, even when the alias expands to one. Shape aliases (including a rename or
        /// generic wrapper of a shape) stay TYHP4347; every other alias is TYHP4069.
        /// Direct shape aliases are already reported by
        /// <see cref="TryReportObjectShapeUsedAsClass"/>.
        /// </summary>
        private static bool TryReportTypeAliasUsedAsNewTarget(
            IClassNameReference classRef,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var scope = state.NameResolutionScope
                ?? state.EnclosingCallable?.ContainingScope
                ?? state.EnclosingObject?.ContainingScope
                ?? (IBaseScope)context.GlobalScope;

            if (!ObjectShapeSupport.TryResolveTypeAlias(
                    classRef, scope, context.SymbolTree, out var alias))
            {
                return false;
            }

            var code = ObjectShapeSupport.AliasResolvesToObjectShape(alias)
                ? MessageCode.CheckerObjectShapeUsedAsClass
                : MessageCode.CheckerCannotInstantiateNonClass;
            CheckerHelpers.ReportError(diagnostics, state, classRef, code, alias.Name);
            return true;
        }

        private static bool IsObjectGenericSimpleName(string? name, CheckerState state)
        {
            var simple = name?.TrimStart('\\');
            return !string.IsNullOrEmpty(simple)
                && !simple.Contains('\\')
                && state.ObjectGenerics.Any(gp => string.Equals(gp.Name, simple, StringComparison.Ordinal));
        }

        private static bool IsNonInstantiableClassName(IClassNameReference classRef)
        {
            var name = GetClassNameText(classRef);
            return name is "int" or "float" or "string" or "bool" or "array" or "callable"
                or "iterable" or "mixed" or "void" or "never" or "object" or "null"
                or "true" or "false" or "resource";
        }

        private static string? GetClassNameText(IClassNameReference classRef) =>
            classRef switch
            {
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                _ => classRef.Identifier,
            };

        private static bool IsNonInstantiableBuiltin(ICheckedType type) =>
            type is LiteralCheckedType
            || CheckerHelpers.IsBuiltInName(type, "int")
            || CheckerHelpers.IsBuiltInName(type, "float")
            || CheckerHelpers.IsBuiltInName(type, "string")
            || CheckerHelpers.IsBuiltInName(type, "bool")
            || CheckerHelpers.IsBuiltInName(type, "array")
            || CheckerHelpers.IsBuiltInName(type, "callable")
            || CheckerHelpers.IsBuiltInName(type, "iterable")
            || CheckerHelpers.IsBuiltInName(type, "mixed")
            || type.Kind is CheckedTypeKind.Void or CheckedTypeKind.Never;

        private static void CheckUnaryOp(
            PhpUnaryOpAst unary,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            // Story 14.5: keyword call forms (`exit(...)` / `die(...)` / `clone(...)`) with a
            // PhpArgumentListAst operand are checked against ExtCore tyhpdef signatures — not
            // the unary clone object-type rule. Bare `exit;` / unary `clone $x` fall through.
            if (TryCheckKeywordConstructCall(unary, state, context, diagnostics))
            {
                return;
            }

            // PHP 8.5 `(void) expr` — type-check the operand; result is void (see TypeInferrer).
            // Mixed operands are fine: discard is not a type-specific use of the value.
            // Wrapping a call is the intentional-discard form that suppresses TYHP4165.
            if (CheckerHelpers.IsVoidCastUnary(unary))
            {
                CheckVoidCast(unary, state, context, diagnostics);
                return;
            }

            if (unary.Operand is null)
            {
                return;
            }

            var op = unary.Operator?.ValueString ?? string.Empty;
            var operandType = context.ResolveExpressionType(unary.Operand, state);

            if (string.Equals(op, "clone", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsObjectType(operandType))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, unary, MessageCode.CheckerCloneNonObject, operandType.DisplayName);
                }

                return;
            }

            // Casts are intentional assertions (a form of narrowing) — allowed on mixed.
            // `await` / `@` are not type-specific on mixed and are not in the restricted set below;
            // `!` (like `+`/`-`/`~`/`++`/`--`) *is* restricted — Tyhp conditions require a real
            // `bool` operand, so negating unnarrowed `mixed` needs narrowing first (Story 08).
            if (IsMixedRestrictedUnaryOperator(op))
            {
                CheckerHelpers.ReportMixedRequiresNarrowing(
                    diagnostics, state, unary.Operand, operandType);
            }

            CheckObjectUnaryOperatorOverloadApplicability(unary, op, operandType, state, context, diagnostics);
        }

        private static bool IsMixedRestrictedUnaryOperator(string op) =>
            op is "+" or "-" or "~" or "++" or "--" or "!";

        /// <summary>
        /// Object operands cannot use native PHP arithmetic / bitwise / concat. Require a matching
        /// Story 11 overload form (unary vs binary are distinct — a unary <c>+</c> does not satisfy
        /// binary <c>$a + $b</c>). Comparisons stay on the native PHP path. Concat is native when
        /// every operand is scalar or <c>\Stringable</c> (including <c>__toString</c> auto-implement).
        /// </summary>
        private static void CheckObjectOperatorOverloadApplicability(
            PhpBinaryOpAst binary,
            string opText,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (binary.Left is null || binary.Right is null)
            {
                return;
            }

            var overloadOp = MapBinaryOpTextToOverloadable(opText);
            if (overloadOp == OverloadableOperator.Invalid
                || IsNativeComparableWithoutOverload(overloadOp))
            {
                return;
            }

            var leftType = context.ResolveExpressionType(binary.Left, state);
            var rightType = context.ResolveExpressionType(binary.Right, state);

            // Unresolved is a checker error-recovery marker (undefined symbol, unhandled AST shape,
            // not-yet-inferred generic, …) — a resolution failure already reported elsewhere. Reporting
            // TYHP4029 on top would cascade a second diagnostic from that same failure.
            if (TypeComparer.IsUnresolvedType(leftType) || TypeComparer.IsUnresolvedType(rightType))
            {
                return;
            }

            if (!OperandRequiresObjectOperatorOverload(leftType)
                && !OperandRequiresObjectOperatorOverload(rightType))
            {
                return;
            }

            if (context.HasMatchingBinaryOperatorOverload(overloadOp, leftType, rightType, state))
            {
                return;
            }

            // PHP concatenates Stringable objects by calling __toString; that is not an
            // operator overload. Non-Stringable objects still need a concat form (TYHP4029).
            if (overloadOp == OverloadableOperator.Concat
                && CheckerHelpers.IsStringableType(leftType, context.SymbolTree, context.GlobalScope)
                && CheckerHelpers.IsStringableType(rightType, context.SymbolTree, context.GlobalScope))
            {
                return;
            }

            // Prefer the underlying binary spelling in diagnostics (`+` not `+=`).
            var displayOp = StripCompoundAssignSuffix(opText);
            CheckerHelpers.ReportError(
                diagnostics,
                state,
                binary,
                MessageCode.CheckerInvalidOperatorForType,
                displayOp,
                leftType.DisplayName,
                rightType.DisplayName);
        }

        private static void CheckObjectUnaryOperatorOverloadApplicability(
            PhpUnaryOpAst unary,
            string opText,
            ICheckedType operandType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var overloadOp = MapUnaryOpTextToOverloadable(opText);
            if (overloadOp == OverloadableOperator.Invalid)
            {
                return;
            }

            if (TypeComparer.IsUnresolvedType(operandType))
            {
                return;
            }

            if (!OperandRequiresObjectOperatorOverload(operandType))
            {
                return;
            }

            if (context.HasMatchingUnaryOperatorOverload(overloadOp, operandType, state))
            {
                return;
            }

            CheckerHelpers.ReportError(
                diagnostics,
                state,
                unary,
                MessageCode.CheckerInvalidOperatorForType,
                opText,
                operandType.DisplayName,
                operandType.DisplayName);
        }

        private static bool OperandRequiresObjectOperatorOverload(ICheckedType type)
        {
            var unwrapped = type is NullableCheckedType nullable ? nullable.InnerType : type;
            if (unwrapped is LiteralCheckedType literal)
            {
                unwrapped = literal.UnderlyingType;
            }

            // Structs are not on the class operator-overload path; builtins use native PHP / extensions.
            return CheckerHelpers.TryGetObjectDeclaration(unwrapped) is { IsStruct: false };
        }

        /// <summary>
        /// PHP compares objects natively; overloads are optional enhancements, not required.
        /// </summary>
        private static bool IsNativeComparableWithoutOverload(OverloadableOperator op) =>
            op is OverloadableOperator.CompareEqual
                or OverloadableOperator.CompareNotEqual
                or OverloadableOperator.CompareIdentical
                or OverloadableOperator.CompareNotIdentical
                or OverloadableOperator.CompareLessThan
                or OverloadableOperator.CompareLessThanOrEqualTo
                or OverloadableOperator.CompareGreaterThan
                or OverloadableOperator.CompareGreaterThanOrEqualTo
                or OverloadableOperator.CompareSpaceship;

        private static OverloadableOperator MapBinaryOpTextToOverloadable(string op) =>
            op switch
            {
                "+" or "+=" => OverloadableOperator.Add,
                "-" or "-=" => OverloadableOperator.Subtract,
                "*" or "*=" => OverloadableOperator.Multiply,
                "/" or "/=" => OverloadableOperator.Divide,
                "%" or "%=" => OverloadableOperator.Mod,
                "**" or "**=" => OverloadableOperator.Pow,
                "." or ".=" => OverloadableOperator.Concat,
                "&" or "&=" => OverloadableOperator.BitwiseAnd,
                "|" or "|=" => OverloadableOperator.BitwiseOr,
                "^" or "^=" => OverloadableOperator.BitwiseXor,
                "<<" or "<<=" => OverloadableOperator.BitwiseShiftLeft,
                ">>" or ">>=" => OverloadableOperator.BitwiseShiftRight,
                "==" => OverloadableOperator.CompareEqual,
                "!=" => OverloadableOperator.CompareNotEqual,
                "===" => OverloadableOperator.CompareIdentical,
                "!==" => OverloadableOperator.CompareNotIdentical,
                "<" => OverloadableOperator.CompareLessThan,
                "<=" => OverloadableOperator.CompareLessThanOrEqualTo,
                ">" => OverloadableOperator.CompareGreaterThan,
                ">=" => OverloadableOperator.CompareGreaterThanOrEqualTo,
                "<=>" => OverloadableOperator.CompareSpaceship,
                _ => OverloadableOperator.Invalid,
            };

        private static OverloadableOperator MapUnaryOpTextToOverloadable(string op) =>
            op switch
            {
                "+" => OverloadableOperator.Plus,
                "-" => OverloadableOperator.Minus,
                "~" => OverloadableOperator.BitwiseNot,
                "++" => OverloadableOperator.Increment,
                "--" => OverloadableOperator.Decrement,
                // `!` is natively valid on objects (truthiness); do not require an overload.
                _ => OverloadableOperator.Invalid,
            };

        private static string StripCompoundAssignSuffix(string op) =>
            op.Length >= 2 && op[^1] == '=' && op is not ("==" or "!=" or "===" or "!==" or "<=" or ">=" or "<=>" or "??=")
                ? op[..^1]
                : op;

        private static bool IsObjectType(ICheckedType type) =>
            CheckerHelpers.TryGetObjectDeclaration(type) is not null
            || CheckerHelpers.IsBuiltInName(type, "object");
            // `mixed` is intentionally excluded — Story 08: clone on mixed → TYHP4073; narrow first.

        private static void CheckVariable(
            PhpVariableAst variable,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (variable.VariableExpression is not null)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, variable, MessageCode.CheckerVariableVariableProhibited);
                return;
            }

            if (!CheckerHelpers.IsThisVariable(variable))
            {
                return;
            }

            if (CheckerHelpers.IsInStaticContext(state)
                && !CheckerHelpers.IsExtensionReceiverThis(state))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, variable, MessageCode.CheckerThisInStaticContext);
            }
        }

        private static void CheckArray(
            PhpArrayAst array,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var seenKeys = new Dictionary<string, PhpArrayPairAst>(StringComparer.Ordinal);
            foreach (var pair in array.ArrayPairs?.GetAllExcludingSkippedSlots() ?? [])
            {
                ReportDuplicateArrayKeyIfNeeded(pair, seenKeys, state, diagnostics);

                if (pair.ValueExpr is not null)
                {
                    _ = context.ResolveExpressionType(pair.ValueExpr, state);
                }
            }
        }

        private static void CheckArrayPairList(
            PhpArrayPairListAst pairList,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            // Array literals share this node type with `list()` / `[]` destructure.
            // Spread-in-destructure (4095) and non-array sources (4094) are checked from
            // assignment / foreach via ArrayAccessDestructureSupport — not here, so nested
            // array literals like `[[1, 2], 3]` are not reported as illegal spread.
            // Parsed `[…]` / `array(…)` literals are this node (not `PhpArrayAst`), so
            // duplicate keys are checked here. Skip-slot trailing commas are not keys.
            _ = context;
            var seenKeys = new Dictionary<string, PhpArrayPairAst>(StringComparer.Ordinal);
            foreach (var pair in pairList.GetAllExcludingSkippedSlots())
            {
                ReportDuplicateArrayKeyIfNeeded(pair, seenKeys, state, diagnostics);
            }
        }

        private static void ReportDuplicateArrayKeyIfNeeded(
            PhpArrayPairAst pair,
            Dictionary<string, PhpArrayPairAst> seenKeys,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (pair.IsSkippedSlot || pair.KeyExpr is null)
            {
                return;
            }

            var keyText = GetArrayKeyText(pair.KeyExpr);
            if (string.IsNullOrEmpty(keyText))
            {
                return;
            }

            if (seenKeys.TryGetValue(keyText, out var firstPair))
            {
                var fileName = CheckerHelpers.ResolveDiagnosticFileName(state, pair);
                diagnostics.AddDuplicateFromAst(
                    MessageCode.CheckerDuplicateArrayKey,
                    pair,
                    fileName,
                    firstPair,
                    fileName,
                    keyText);
                return;
            }

            seenKeys[keyText] = pair;
        }

        private static string? GetArrayKeyText(IExpression key) =>
            key switch
            {
                PhpEncapsListAst list => GetEncapsListKeyText(list),
                PhpEncapsStringAst encaps => encaps.ValueString ?? encaps.TokenValue?.ValueString,
                PhpScalarAst scalar => scalar.ValueString ?? scalar.ValueInt64?.ToString(),
                _ => GetExpressionText(key),
            };

        private static string? GetEncapsListKeyText(PhpEncapsListAst list)
        {
            var parts = list.GetAllNotNull().ToList();
            if (parts.Count == 0 || parts.Any(part => part is not PhpEncapsStringAst))
            {
                return null;
            }

            return string.Concat(parts.Select(part => part.ValueString ?? ""));
        }

        private static void CheckTypedVar(
            TyhpTypedVarExprAst typedVar,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (typedVar.AssignedExpression is PhpInlineFunctionAst closure)
            {
                context.CheckNode(closure, state);
            }

            if (typedVar.AssignedExpression is PhpNewAst newExpr)
            {
                CheckNew(newExpr, state, context, diagnostics);
            }
            else if (typedVar.AssignedExpression is not null
                && typedVar.TypeExpression is not null)
            {
                var source = context.ResolveExpressionType(typedVar.AssignedExpression, state);
                var target = context.ResolveTypeAnnotation(typedVar.TypeExpression, state);
                if (!context.IsAssignable(source, target, state))
                {
                    if (!CheckerHelpers.TryReportObjectShapeRequiresGuard(
                            diagnostics, state, typedVar, source, target)
                        && !CheckerHelpers.TryReportCallableShapeRequiresGuard(
                            diagnostics, state, typedVar, source, target)
                        && !CheckerHelpers.TryReportNewConstraintFailure(
                            diagnostics, state, typedVar, source, target,
                            context.SymbolTree, context.GlobalScope)
                        && !SymbolNameTypeAssignability.TryReportLiteralExistenceFailure(
                            source, target, state, context.SymbolTree, context.GlobalScope,
                            diagnostics, typedVar)
                        && !context.TryReportTemplateStringBudgetExceeded(typedVar, state))
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, state, typedVar, MessageCode.CheckerTypeMismatch,
                            source.DisplayName, target.DisplayName);
                    }
                }
            }
        }

        private static string? GetExpressionText(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                PhpScalarAst scalar => scalar.ValueString ?? scalar.ValueInt64?.ToString(),
                PhpVariableAst variable => CheckerHelpers.GetVariableName(variable),
                _ => expression?.Identifier,
            };
    }
}
