using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
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

            // Progressive left→right narrowing for `&&` / `and`: after the left operand is
            // proven (short-circuit), the right is type-checked under the left's positive
            // narrowing. Without this, `\is_array($x) && \array_key_exists(0, $x)` still sees
            // `$x` as `mixed` in the second call. Child traversal is suppressed for these ops
            // so this walk is the sole visitor. The narrowing is applied to a disposable probe
            // (not the ambient `state`) — this node is not necessarily an if/while/ternary/switch
            // condition (real branch narrowing for those goes through a dedicated
            // `ApplyConditionNarrowing(..., thenState/loopState, ...)` call elsewhere), so without
            // a probe a bare `\is_string($x) && …;` expression statement would leak `$x`'s
            // narrowed type forward into unrelated code that follows it.
            if (TypeNarrowingRule.IsLogicalAnd(op)
                && binary.Left is IExpression leftExpr
                && binary.Right is not null)
            {
                var probe = state.Split(ScopeType.CodeBlock);
                context.CheckNode(leftExpr, probe);
                // Bare `mixed` as a logical operand is type-specific (Tyhp conditions are bool).
                CheckerHelpers.ReportMixedRequiresNarrowing(
                    diagnostics, state, leftExpr, context.ResolveExpressionType(leftExpr, probe));
                TypeNarrowingRule.ApplyConditionNarrowing(
                    leftExpr, probe, context, context.SymbolTree, context.GlobalScope, positive: true);
                context.CheckNode(binary.Right, probe);
                CheckerHelpers.ReportMixedRequiresNarrowing(
                    diagnostics, state, binary.Right,
                    context.ResolveExpressionType(binary.Right, probe));
                return;
            }

            if (!IsAssignmentOperator(op))
            {
                CheckMixedRestrictedBinaryOperands(binary, op, state, context, diagnostics);
                return;
            }

            if (binary.Left is null || binary.Right is null)
            {
                return;
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
            }

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
            var targetType = ResolveAssignmentTargetType(binary.Left, state, context);

            if (targetType is not UnresolvedCheckedType)
            {
                // `??=` assigns the right operand when the left is null, so a bag literal on
                // the right is the value that lands in the target — same as plain `=`.
                var bagChecked = op is "=" or "??="
                    && StructBagLiteralChecker.TryCheck(
                        binary.Right, targetType, state, context, diagnostics);
                if (!bagChecked
                    && !context.IsAssignable(sourceType, targetType, state)
                    && !CheckerHelpers.IsArrayCallableLiteral(binary.Right, targetType, context, state))
                {
                    if (!SymbolNameTypeAssignability.TryReportLiteralExistenceFailure(
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
            else if (TryGetThisPropertyAssignmentTarget(binary.Left, out var propertyKey)
                && IsDefinitePropertyInitializingAssignment(op))
            {
                // Track both definite init and post-assignment type so later `$this->prop !== null`
                // / reads see the RHS (mirrors AssignVariable for locals).
                state.AssignPropertyType(propertyKey!, sourceType);
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
                or "instanceof" or "is" or "isa" or "isan" or "is_a" or "is_an";

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

        /// <summary>
        /// True when <paramref name="left"/> is <c>$this->prop</c> (plain property write target).
        /// </summary>
        private static bool TryGetThisPropertyAssignmentTarget(IExpression left, out string? propertyKey)
        {
            propertyKey = null;
            if (left is not PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst memberAccess } dereferenceable)
            {
                return false;
            }

            if (dereferenceable.Base is not PhpVariableAst receiver
                || !CheckerHelpers.IsThisVariable(receiver))
            {
                return false;
            }

            var memberName = GetExpressionText(memberAccess.MemberName);
            if (memberName is null || memberName.StartsWith('{'))
            {
                return false;
            }

            propertyKey = memberName.StartsWith('$') ? memberName : "$" + memberName;
            return true;
        }

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
            var classType = context.ResolveExpressionType(newExpr, state);
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
            // parameters like `callable<TReturn>` substitute class generics the same way
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
        }

        private static bool IsMixedRestrictedUnaryOperator(string op) =>
            op is "+" or "-" or "~" or "++" or "--" or "!";

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
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in array.ArrayPairs?.GetAllNotNull() ?? [])
            {
                if (pair.KeyExpr is null)
                {
                    continue;
                }

                var keyText = GetExpressionText(pair.KeyExpr) ?? pair.KeyExpr.ToString() ?? string.Empty;
                if (!seenKeys.Add(keyText))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, pair, MessageCode.CheckerDuplicateArrayKey, keyText);
                }

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
            foreach (var pair in pairList.GetAllNotNull())
            {
                if (pair.ValueExpr is PhpArrayPairListAst)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, pair, MessageCode.CheckerDestructuringSpread);
                }

                if (pair.KeyExpr is PhpArrayPairListAst destructuring)
                {
                    var sourceType = pair.ValueExpr is not null
                        ? context.ResolveExpressionType(pair.ValueExpr, state)
                        : CheckedTypes.Unresolved;
                    if (!CheckerHelpers.IsArrayOrStringType(sourceType) && sourceType is not UnresolvedCheckedType)
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, state, pair, MessageCode.CheckerDestructuringNonArray, sourceType.DisplayName);
                    }

                    CheckArrayPairList(destructuring, state, context, diagnostics);
                }
            }
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
                    if (!SymbolNameTypeAssignability.TryReportLiteralExistenceFailure(
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
