using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Story 16 Phase 2 helpers for <c>\Tyhp\Expression&lt;TCallableShape&gt;</c>:
    /// type recognition, body validation, and capture collection.
    /// </summary>
    internal static class ExpressionTreeSupport
    {
        /// <summary>
        /// True when <paramref name="type"/> is (or unwraps to) <c>Expression&lt;…&gt;</c>
        /// from the <c>tyhp/lambda</c> package (not <c>PropertyPath</c>).
        /// </summary>
        public static bool IsExpressionType(ICheckedType? type)
            => TryGetExpressionTypeArgs(type, out _, out _);

        /// <summary>
        /// Extracts parameter types and return type from <c>TCallableShape</c> (the single
        /// type argument). Bare <c>Expression</c> without type args is still recognized
        /// (empty params, mixed return).
        /// </summary>
        public static bool TryGetExpressionTypeArgs(
            ICheckedType? type,
            out IReadOnlyList<ICheckedType> parameterTypes,
            out ICheckedType returnType)
        {
            parameterTypes = [];
            returnType = CheckedTypes.Mixed;

            if (type is null)
            {
                return false;
            }

            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (!IsNamedExpressionType(type))
            {
                return false;
            }

            if (PropertyPathSupport.TryGetCallableShapeArgument(type, out var shape)
                && PropertyPathSupport.TryMapCallableShape(shape, out var mapped))
            {
                parameterTypes = mapped.ParameterTypes;
                returnType = mapped.ReturnType;
            }

            return true;
        }

        /// <summary>
        /// Maps <c>Expression&lt;TCallableShape&gt;</c> to the callable facet of
        /// <c>TCallableShape</c> for contextual closure typing at call sites.
        /// </summary>
        public static bool TryMapToCallable(ICheckedType type, out CallableCheckedType callable)
        {
            callable = null!;
            if (!IsExpressionType(type))
            {
                return false;
            }

            if (PropertyPathSupport.TryGetCallableShapeArgument(type, out var shape)
                && PropertyPathSupport.TryMapCallableShape(shape, out callable))
            {
                return true;
            }

            callable = new CallableCheckedType([], CheckedTypes.Mixed);
            return true;
        }

        /// <summary>
        /// After an inline <c>fn</c> has been checked against an <c>Expression&lt;…&gt;</c>
        /// annotation, reports arrow / body / capture diagnostics. Returns true when
        /// <paramref name="targetType"/> is Expression so the caller must not compare the
        /// inferred <c>\Closure</c> to the Expression class.
        /// </summary>
        public static bool TryValidateInlineFnCapture(
            PhpInlineFunctionAst closure,
            ICheckedType targetType,
            CheckerState outerState,
            DiagnosticBag diagnostics,
            IBase2Ast reportNode)
        {
            if (!IsExpressionType(targetType))
            {
                return false;
            }

            if (!closure.IsArrowFunction)
            {
                TryGetExpressionTypeArgs(targetType, out var paramTypes, out var returnType);
                CheckerHelpers.ReportError(
                    diagnostics,
                    outerState,
                    reportNode,
                    MessageCode.CheckerExpressionRequiresInlineFn,
                    DisplayFirstParamArg(paramTypes),
                    DisplayReturnArg(returnType));
                return true;
            }

            if (!PropertyPathSupport.TryGetArrowBodyExpression(closure, out var body))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    outerState,
                    closure,
                    MessageCode.CheckerExpressionUnsupportedNode,
                    "statement body");
                return true;
            }

            if (!TryValidateSupportedBody(body, closure, out var unsupportedKind, out var reportAt)
                && unsupportedKind is not null)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    outerState,
                    reportAt ?? (body is IBase2Ast bodyNode ? bodyNode : closure),
                    MessageCode.CheckerExpressionUnsupportedNode,
                    unsupportedKind);
                return true;
            }

            var captures = CollectCapturedVariables(body, closure);
            if (!TryValidateCapturesAssigned(captures, outerState, out var undefinedName)
                && undefinedName is not null)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    outerState,
                    body is IBase2Ast captureSite ? captureSite : closure,
                    MessageCode.CheckerExpressionCapturedVarUndefined,
                    undefinedName);
            }

            return true;
        }

        /// <summary>
        /// Recursively walks <paramref name="body"/> and returns false with a kind string when an
        /// unsupported node is found (for TYHP4322). A nested arrow <c>fn</c> is part of the tree
        /// and its body is held to the same rules. When the unsupported node sits inside a nested
        /// <c>fn</c>, <paramref name="reportAt"/> is that nested body (the innermost one), so the
        /// diagnostic lands where the nested fn's own validation would report it; otherwise null.
        /// </summary>
        public static bool TryValidateSupportedBody(
            IExpression body,
            PhpInlineFunctionAst closure,
            out string? unsupportedKind,
            out IBase2Ast? reportAt)
            => TryValidateExpression(body, out unsupportedKind, out reportAt);

        /// <summary>
        /// Collects outer-scope variable names referenced in <paramref name="body"/> that are not
        /// parameters of <paramref name="closure"/>. Parameters of a nested <c>fn</c> are not
        /// captures inside that nested body.
        /// </summary>
        public static IReadOnlyList<string> CollectCapturedVariables(
            IExpression body,
            PhpInlineFunctionAst closure)
        {
            var captures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectCaptures(body, ParameterNames(closure), captures);
            return captures.ToList();
        }

        private static HashSet<string> ParameterNames(PhpInlineFunctionAst closure)
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

        /// <summary>
        /// True when every captured name is present and definitely assigned in
        /// <paramref name="outerState"/>. Returns the first failing name when not.
        /// </summary>
        public static bool TryValidateCapturesAssigned(
            IReadOnlyList<string> captures,
            CheckerState outerState,
            out string? undefinedName)
        {
            undefinedName = null;
            foreach (var name in captures)
            {
                var variable = outerState.LookupVariable(name);
                if (variable is null || !variable.IsDefinitelyAssigned)
                {
                    undefinedName = name;
                    return false;
                }
            }

            return true;
        }

        public static string DisplayFirstParamArg(IReadOnlyList<ICheckedType> parameterTypes)
            => parameterTypes.Count > 0
                ? PropertyPathSupport.DisplayTypeArg(parameterTypes[0])
                : "mixed";

        public static string DisplayReturnArg(ICheckedType returnType)
            => PropertyPathSupport.DisplayTypeArg(returnType);

        private static bool IsNamedExpressionType(ICheckedType type)
        {
            if (CheckerHelpers.TryGetObjectDeclaration(type) is { } obj)
            {
                if (!string.IsNullOrEmpty(obj.FullyQualifiedName))
                {
                    return IsExpressionFqn(obj.FullyQualifiedName);
                }

                return string.Equals(
                    obj.Name,
                    PropertyPathSupport.ExpressionSimpleName,
                    StringComparison.OrdinalIgnoreCase);
            }

            return IsExpressionFqn(type.DisplayName);
        }

        private static bool IsExpressionFqn(string spelling)
        {
            var baseName = spelling;
            var angle = baseName.IndexOf('<');
            if (angle >= 0)
            {
                baseName = baseName[..angle];
            }

            baseName = baseName.TrimStart('\\');
            return string.Equals(baseName, PropertyPathSupport.ExpressionFqn, StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    baseName,
                    PropertyPathSupport.ExpressionSimpleName,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryValidateExpression(
            IExpression? expression,
            out string? unsupportedKind,
            out IBase2Ast? reportAt)
        {
            unsupportedKind = null;
            reportAt = null;
            if (expression is null)
            {
                return true;
            }

            switch (expression)
            {
                case PhpScalarAst:
                case PhpMagicConstantAst:
                    return true;

                // String literals parse as an encaps list; a literal made only of string pieces is a
                // constant. Interpolation (`"x{$u->name}"`) has no node form.
                case PhpEncapsListAst { StringType: PhpStringType.BackQuoted or PhpStringType.BinaryBackQuoted }:
                    unsupportedKind = "shell command";
                    return false;

                case PhpEncapsListAst encapsList:
                    if (!IsConstantStringLiteral(encapsList))
                    {
                        unsupportedKind = "string interpolation";
                        return false;
                    }

                    return true;

                case PhpEncapsStringAst:
                    return true;

                // `true` / `false` / `null` parse as bare names.
                case PhpNameAst name when IsLiteralConstantName(name):
                    return true;

                case PhpVariableAst:
                    return true;

                case PhpInlineFunctionAst nested:
                    return TryValidateNestedFn(nested, out unsupportedKind, out reportAt);

                case TyhpAsyncBlockAst:
                    unsupportedKind = "async block";
                    return false;

                case PhpYieldAst:
                    unsupportedKind = "yield";
                    return false;

                case PhpConditionalAst { IsMatchSyntax: true }:
                    unsupportedKind = "match";
                    return false;

                case PhpConditionalAst:
                    unsupportedKind = "switch";
                    return false;

                case PhpTernaryOpAst ternary:
                    return TryValidateExpression(ternary.Condition, out unsupportedKind, out reportAt)
                        && TryValidateExpression(ternary.TrueExpr, out unsupportedKind, out reportAt)
                        && TryValidateExpression(ternary.FalseExpr, out unsupportedKind, out reportAt);

                case PhpBinaryOpAst binary:
                    return TryValidateBinary(binary, out unsupportedKind, out reportAt);

                case PhpUnaryOpAst unary:
                    return TryValidateUnary(unary, out unsupportedKind, out reportAt);

                case PhpNewAst newExpr:
                    if (newExpr.AnonymousClass is not null)
                    {
                        unsupportedKind = "anonymous class";
                        return false;
                    }

                    if (IsDynamicClassReference(newExpr.ClassName))
                    {
                        unsupportedKind = "dynamic class";
                        return false;
                    }

                    return TryValidateArgumentList(newExpr.Arguments, out unsupportedKind, out reportAt);

                case PhpArrayAst array:
                    return TryValidateArrayLiteral(array.ArrayPairs, out unsupportedKind, out reportAt);

                // Short-syntax `[...]` literal.
                case PhpArrayPairListAst pairList:
                    return TryValidateArrayLiteral(pairList, out unsupportedKind, out reportAt);

                case PhpDereferenceableAst deref:
                    return TryValidateDereferenceable(deref, out unsupportedKind, out reportAt);

                case PhpDereferenceableExpressionAst paren
                    when paren.Expression is IExpression inner:
                    return TryValidateExpression(inner, out unsupportedKind, out reportAt);

                default:
                    unsupportedKind = DescribeUnsupported(expression);
                    return false;
            }
        }

        private static bool TryValidateBinary(
            PhpBinaryOpAst binary,
            out string? unsupportedKind,
            out IBase2Ast? reportAt)
        {
            unsupportedKind = null;
            reportAt = null;
            var token = binary.Operator?.ValueInt64 ?? -1;
            var opText = binary.Operator?.ValueString ?? "";

            if (PhpAssignmentOperatorExtensions.FromToken((int)token, opText) is not null)
            {
                unsupportedKind = "assignment";
                return false;
            }

            var binaryOp = PhpBinaryOperatorExtensions.FromToken((int)token, opText);
            if (binaryOp == PhpBinaryOperator.InstanceOf
                || IsInstanceOfText(opText))
            {
                // Phase 3: `$x instanceof T` / `$x is int` — the RHS is a type reference, not a
                // value expression. Dynamic `$x instanceof $classNameVar` is allowed (captured).
                if (!TryValidateExpression(binary.Left, out unsupportedKind, out reportAt))
                {
                    return false;
                }

                if (!IsSupportedInstanceofTarget(binary.Right))
                {
                    unsupportedKind = "instanceof";
                    return false;
                }

                return true;
            }

            if (binaryOp == PhpBinaryOperator.Pipe
                || string.Equals(opText, "|>", StringComparison.Ordinal))
            {
                unsupportedKind = "pipe";
                return false;
            }

            return TryValidateExpression(binary.Left, out unsupportedKind, out reportAt)
                && TryValidateExpression(binary.Right, out unsupportedKind, out reportAt);
        }

        private static bool TryValidateUnary(
            PhpUnaryOpAst unary,
            out string? unsupportedKind,
            out IBase2Ast? reportAt)
        {
            unsupportedKind = null;
            reportAt = null;
            var token = unary.Operator?.ValueInt64 ?? -1;
            var opText = unary.Operator?.ValueString ?? "";

            if (string.Equals(opText, "await", StringComparison.OrdinalIgnoreCase)
                || token == TyhpParser.T_TYHP_AWAIT)
            {
                unsupportedKind = "await";
                return false;
            }

            if (string.Equals(opText, "throw", StringComparison.OrdinalIgnoreCase)
                || token == TyhpParser.T_THROW)
            {
                unsupportedKind = "throw";
                return false;
            }

            if (token is TyhpParser.T_INCLUDE
                or TyhpParser.T_INCLUDE_ONCE
                or TyhpParser.T_REQUIRE
                or TyhpParser.T_REQUIRE_ONCE
                || IsIncludeText(opText))
            {
                unsupportedKind = "include";
                return false;
            }

            if (token == TyhpParser.T_VOID_CAST
                || string.Equals(opText, "(void)", StringComparison.OrdinalIgnoreCase))
            {
                unsupportedKind = "void cast";
                return false;
            }

            if (IsCastToken((int)token))
            {
                return TryValidateExpression(unary.Operand, out unsupportedKind, out reportAt);
            }

            if (token is TyhpParser.T_SYM_BANG
                or TyhpParser.T_SYM_MINUS
                or TyhpParser.T_SYM_PLUS
                or TyhpParser.T_SYM_TILDE
                || opText is "!" or "-" or "+" or "~")
            {
                return TryValidateExpression(unary.Operand, out unsupportedKind, out reportAt);
            }

            if (token is TyhpParser.T_INC or TyhpParser.T_DEC
                || opText is "++" or "--")
            {
                unsupportedKind = "increment";
                return false;
            }

            if (token == TyhpParser.T_CLONE
                || string.Equals(opText, "clone", StringComparison.OrdinalIgnoreCase))
            {
                unsupportedKind = "clone";
                return false;
            }

            if (token == TyhpParser.T_SYM_AT || opText == "@")
            {
                // Error-suppression is not part of the expression-tree surface.
                unsupportedKind = "error suppression";
                return false;
            }

            // Unknown unary — reject rather than silently emit a wrong tree.
            unsupportedKind = string.IsNullOrEmpty(opText) ? "unary" : opText;
            return false;
        }

        private static bool TryValidateDereferenceable(
            PhpDereferenceableAst deref,
            out string? unsupportedKind,
            out IBase2Ast? reportAt)
        {
            unsupportedKind = null;
            reportAt = null;

            // Parenthesized: (expr)->… or (expr) alone wrapped as dereferenceable base.
            if (deref.Base is PhpDereferenceableExpressionAst paren)
            {
                if (deref.Suffix is null)
                {
                    return TryValidateExpression(paren.Expression, out unsupportedKind, out reportAt);
                }

                if (!TryValidateExpression(paren.Expression, out unsupportedKind, out reportAt))
                {
                    return false;
                }
            }

            if (deref.Suffix is PhpCallAst call)
            {
                // `$u->name(...)` / `name(...)` creates a Closure; a call node would describe an
                // invocation instead.
                if (Rules.CheckerHelpers.IsFirstClassCallableArgumentList(call.Arguments))
                {
                    unsupportedKind = "first-class callable";
                    return false;
                }

                if (deref.Base is PhpDereferenceableAst inner
                    && inner.Suffix is PhpInstanceMemberAccessAst method)
                {
                    // $obj->method(...) / $obj?->method(...)
                    if (GetMemberName(method.MemberName) is null)
                    {
                        unsupportedKind = "dynamic member";
                        return false;
                    }

                    return TryValidateExpression(inner.Base as IExpression, out unsupportedKind, out reportAt)
                        && TryValidateArgumentList(call.Arguments, out unsupportedKind, out reportAt);
                }

                if (deref.Base is PhpDereferenceableAst staticInner
                    && (staticInner.Suffix is PhpStaticMemberAccessAst
                        || staticInner.Suffix is PhpClassConstantAccessAst))
                {
                    // Class::method(...) — parsed as class-constant-access (or ::$var static member).
                    if (IsDynamicClassReference(staticInner.Base))
                    {
                        unsupportedKind = "dynamic class";
                        return false;
                    }

                    return TryValidateArgumentList(call.Arguments, out unsupportedKind, out reportAt);
                }

                // Free function call: name(...), \strtolower(...), $var(...), etc.
                unsupportedKind = "function call";
                return false;
            }

            if (deref.Suffix is PhpInstanceMemberAccessAst member)
            {
                if (GetMemberName(member.MemberName) is null)
                {
                    unsupportedKind = "dynamic member";
                    return false;
                }

                return TryValidateExpression(deref.Base as IExpression, out unsupportedKind, out reportAt);
            }

            if (deref.Suffix is PhpStaticMemberAccessAst staticMember)
            {
                // Class::$var / rare static-member spelling without call.
                if (GetMemberName(staticMember.Member) is null)
                {
                    unsupportedKind = "dynamic member";
                    return false;
                }

                if (IsDynamicClassReference(deref.Base))
                {
                    unsupportedKind = "dynamic class";
                    return false;
                }

                return true;
            }

            if (deref.Suffix is PhpClassConstantAccessAst classConst)
            {
                // Class::CONST (and the parse shape shared with Class::method before the call suffix).
                if (GetMemberName(classConst.Member) is null)
                {
                    unsupportedKind = "dynamic member";
                    return false;
                }

                if (IsDynamicClassReference(deref.Base))
                {
                    unsupportedKind = "dynamic class";
                    return false;
                }

                return true;
            }

            if (deref.Suffix is PhpArrayAccessAst arrayAccess)
            {
                if (arrayAccess.IndexExpression is null)
                {
                    unsupportedKind = "array append";
                    return false;
                }

                return TryValidateExpression(deref.Base as IExpression, out unsupportedKind, out reportAt)
                    && TryValidateExpression(arrayAccess.IndexExpression, out unsupportedKind, out reportAt);
            }

            if (deref.Suffix is null)
            {
                return TryValidateExpression(deref.Base as IExpression, out unsupportedKind, out reportAt);
            }

            unsupportedKind = "dereference";
            return false;
        }

        /// <summary>
        /// A nested arrow <c>fn</c> becomes a <c>LambdaExpression</c>. Its parameters must be plain
        /// (no by-reference, variadic, or default value) and its body follows the tree rules.
        /// </summary>
        private static bool TryValidateNestedFn(
            PhpInlineFunctionAst nested,
            out string? unsupportedKind,
            out IBase2Ast? reportAt)
        {
            unsupportedKind = null;
            reportAt = null;

            if (!nested.IsArrowFunction)
            {
                unsupportedKind = "closure";
                return false;
            }

            if (nested.ReturnsRef)
            {
                unsupportedKind = "by-reference fn";
                return false;
            }

            foreach (var param in nested.Parameters?.GetAllNotNull() ?? [])
            {
                unsupportedKind = param switch
                {
                    { IsRef: true } => "by-reference parameter",
                    { IsVariadic: true } => "variadic parameter",
                    { DefaultValue: not null } => "parameter default value",
                    _ => null,
                };
                if (unsupportedKind is not null)
                {
                    return false;
                }
            }

            if (!PropertyPathSupport.TryGetArrowBodyExpression(nested, out var body))
            {
                unsupportedKind = "statement body";
                return false;
            }

            if (TryValidateExpression(body, out unsupportedKind, out reportAt))
            {
                return true;
            }

            reportAt ??= body as IBase2Ast;
            return false;
        }

        /// <summary>
        /// List and keyed array literals become an <c>ArrayExpression</c>. Keys and values follow
        /// the tree rules; spread (<c>...$xs</c>) is rejected.
        /// </summary>
        private static bool TryValidateArrayLiteral(
            PhpArrayPairListAst? pairs,
            out string? unsupportedKind,
            out IBase2Ast? reportAt)
        {
            unsupportedKind = null;
            reportAt = null;

            foreach (var pair in pairs?.GetAllTrimmingTrailingSkippedSlots() ?? [])
            {
                if (pair.IsExpansion)
                {
                    unsupportedKind = "array spread";
                    return false;
                }

                if (pair.IsSkippedSlot)
                {
                    unsupportedKind = "array literal";
                    return false;
                }

                if (pair.KeyExpr is { } key
                    && !TryValidateExpression(key, out unsupportedKind, out reportAt))
                {
                    return false;
                }

                if (!TryValidateExpression(pair.ValueExpr, out unsupportedKind, out reportAt))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryValidateArgumentList(
            PhpArgumentListAst? arguments,
            out string? unsupportedKind,
            out IBase2Ast? reportAt)
        {
            unsupportedKind = null;
            reportAt = null;
            if (arguments is null)
            {
                return true;
            }

            foreach (var arg in arguments.GetAllNotNull())
            {
                if (arg.Expression is null)
                {
                    continue;
                }

                // Tree call nodes hold a positional argument list.
                if (arg.IsVariadic)
                {
                    unsupportedKind = "argument unpacking";
                    return false;
                }

                if (arg.Name is not null)
                {
                    unsupportedKind = "named argument";
                    return false;
                }

                if (!TryValidateExpression(arg.Expression, out unsupportedKind, out reportAt))
                {
                    return false;
                }
            }

            return true;
        }

        private static void CollectCaptures(
            IBase2Ast? node,
            HashSet<string> paramNames,
            HashSet<string> captures)
        {
            if (node is null)
            {
                return;
            }

            if (node is PhpVariableAst variable)
            {
                var name = CheckerHelpers.GetVariableName(variable);
                if (!string.IsNullOrEmpty(name)
                    && !paramNames.Contains(name)
                    && !string.Equals(name, "this", StringComparison.OrdinalIgnoreCase))
                {
                    captures.Add(name);
                }
            }

            if (node is TyhpAsyncBlockAst)
            {
                return;
            }

            if (node is PhpInlineFunctionAst nested)
            {
                if (nested.IsArrowFunction
                    && PropertyPathSupport.TryGetArrowBodyExpression(nested, out var nestedBody))
                {
                    var scoped = new HashSet<string>(paramNames, StringComparer.OrdinalIgnoreCase);
                    scoped.UnionWith(ParameterNames(nested));
                    CollectCaptures(nestedBody as IBase2Ast, scoped, captures);
                }

                return;
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    CollectCaptures(child, paramNames, captures);
                }
            }
        }

        /// <summary>
        /// True for a string literal with no interpolated expression parts: it evaluates to the same
        /// value on every run, so the tree holds it as a <c>ConstantExpression</c>.
        /// </summary>
        public static bool IsConstantStringLiteral(PhpEncapsListAst encapsList)
            => encapsList.GetAllNotNull().All(static part => part is PhpEncapsStringAst);

        /// <summary>
        /// True for the bare literal names <c>true</c>, <c>false</c>, and <c>null</c> (any case,
        /// optionally root-qualified).
        /// </summary>
        public static bool IsLiteralConstantName(PhpNameAst name)
        {
            var text = name.ValueString?.TrimStart('\\');
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "null", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCastToken(int token) =>
            token is TyhpParser.T_INT_CAST
                or TyhpParser.T_BOOL_CAST
                or TyhpParser.T_STRING_CAST
                or TyhpParser.T_DOUBLE_CAST
                or TyhpParser.T_DECIMAL_CAST
                or TyhpParser.T_ARRAY_CAST
                or TyhpParser.T_OBJECT_CAST;

        private static bool IsInstanceOfText(string op) =>
            op is "instanceof" or "is";

        /// <summary>
        /// RHS of <c>instanceof</c>/<c>is</c>: a type name, builtin, or a variable holding a
        /// class-name string. Nested expressions (calls, operators) are not a type target.
        /// `$x is ?T` wraps the real target in a synthetic prefix `?` unary
        /// (<see cref="Rules.CheckerHelpers.IsNullableInstanceofMarker"/>, FOUND_BUGS #21); unwrap
        /// it before checking the underlying shape.
        /// </summary>
        private static bool IsSupportedInstanceofTarget(IExpression? right)
        {
            if (Rules.CheckerHelpers.IsNullableInstanceofMarker(right, out var nullableOperand))
            {
                right = nullableOperand as IExpression;
            }

            return right is PhpNameAst
                or PhpBuiltinTypeAst
                or PhpNamedTypeAst
                or PhpVariableAst
                or ITypeExpression
                or PhpDereferenceableAst { Suffix: PhpClassConstantAccessAst or PhpStaticMemberAccessAst };
        }

        /// <summary>
        /// True when the class part of <c>new X</c> / <c>X::…</c> is computed (a variable or another
        /// expression) rather than a written class name. A tree records the class by name, and the
        /// chain that builds the tree runs before any tree parameter has a value.
        /// </summary>
        internal static bool IsDynamicClassReference(IBase2Ast? classPart)
        {
            while (classPart is PhpDereferenceableAst { Suffix: null } bare)
            {
                classPart = bare.Base;
            }

            return classPart is PhpVariableAst or PhpDereferenceableAst or PhpDereferenceableExpressionAst;
        }

        private static bool IsIncludeText(string op) =>
            op is "include" or "include_once" or "require" or "require_once";

        private static string? GetMemberName(IExpression? memberName) =>
            memberName switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                PhpScalarAst scalar => scalar.ValueString,
                _ => null,
            };

        /// <summary>
        /// Construct name used in tree diagnostics (<c>isset</c>, <c>class constant access</c>, …).
        /// </summary>
        internal static string DescribeUnsupported(IBase2Ast expression)
        {
            switch (expression)
            {
                case PhpIssetStatementAst:
                    return "isset";
                case PhpEmptyStatementAst:
                    return "empty";
                case TyhpNameofAst:
                    return "nameof";
                case PhpArrayPairListAst:
                case PhpArrayAst:
                    return "array literal";
            }

            // Readable fallback: strip the `Php`/`Tyhp` prefix and `Ast` suffix, then spell the
            // PascalCase remainder as lower-case words (`ClassConstantAccess` → `class constant access`).
            var typeName = expression.GetType().Name;
            foreach (var prefix in new[] { "Php", "Tyhp" })
            {
                if (typeName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    typeName = typeName[prefix.Length..];
                    break;
                }
            }

            if (typeName.EndsWith("Ast", StringComparison.Ordinal))
            {
                typeName = typeName[..^3];
            }

            if (string.IsNullOrEmpty(typeName))
            {
                return "unsupported";
            }

            var words = new System.Text.StringBuilder(typeName.Length + 4);
            for (var i = 0; i < typeName.Length; i++)
            {
                if (i > 0 && char.IsUpper(typeName[i]))
                {
                    words.Append(' ');
                }

                words.Append(char.ToLowerInvariant(typeName[i]));
            }

            return words.ToString();
        }
    }
}
