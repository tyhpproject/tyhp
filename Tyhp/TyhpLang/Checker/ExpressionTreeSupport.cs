using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Story 16 Phase 2 helpers for <c>\Tyhp\Expression&lt;TArgs…, TReturn&gt;</c>:
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
        /// Extracts parameter types (all but last type argument) and return type (last).
        /// Bare <c>Expression</c> without type args is still recognized (empty params, mixed return).
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

            if (type is GenericCheckedType { TypeArguments.Count: > 0 } generic)
            {
                if (generic.TypeArguments.Count == 1)
                {
                    parameterTypes = [];
                    returnType = generic.TypeArguments[0];
                    return true;
                }

                parameterTypes = generic.TypeArguments.Take(generic.TypeArguments.Count - 1).ToList();
                returnType = generic.TypeArguments[^1];
                return true;
            }

            return true;
        }

        /// <summary>
        /// Maps <c>Expression&lt;TArgs…, TReturn&gt;</c> to <c>callable&lt;TArgs…, TReturn&gt;</c>
        /// for contextual closure typing at call sites (same arity convention as callable).
        /// </summary>
        public static bool TryMapToCallable(ICheckedType type, out CallableCheckedType callable)
        {
            callable = null!;
            if (!TryGetExpressionTypeArgs(type, out var parameters, out var result))
            {
                return false;
            }

            callable = new CallableCheckedType(parameters, result);
            return true;
        }

        /// <summary>
        /// Recursively walks <paramref name="body"/> and returns false with a kind string when an
        /// unsupported node is found (for TYHP4322).
        /// </summary>
        public static bool TryValidateSupportedBody(
            IExpression body,
            PhpInlineFunctionAst closure,
            out string? unsupportedKind)
        {
            unsupportedKind = null;
            return TryValidateExpression(body, out unsupportedKind);
        }

        /// <summary>
        /// Collects outer-scope variable names referenced in <paramref name="body"/> that are not
        /// parameters of <paramref name="closure"/>.
        /// </summary>
        public static IReadOnlyList<string> CollectCapturedVariables(
            IExpression body,
            PhpInlineFunctionAst closure)
        {
            var paramNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var param in closure.Parameters?.GetAllNotNull() ?? [])
            {
                var name = param.Name?.TrimStart('$');
                if (!string.IsNullOrEmpty(name))
                {
                    paramNames.Add(name);
                }
            }

            var captures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectCaptures(body, paramNames, captures);
            return captures.ToList();
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

        private static bool TryValidateExpression(IExpression? expression, out string? unsupportedKind)
        {
            unsupportedKind = null;
            if (expression is null)
            {
                return true;
            }

            switch (expression)
            {
                case PhpScalarAst:
                case PhpMagicConstantAst:
                    return true;

                case PhpVariableAst:
                    return true;

                case PhpInlineFunctionAst:
                    unsupportedKind = "nested fn";
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
                    return TryValidateExpression(ternary.Condition, out unsupportedKind)
                        && TryValidateExpression(ternary.TrueExpr, out unsupportedKind)
                        && TryValidateExpression(ternary.FalseExpr, out unsupportedKind);

                case PhpBinaryOpAst binary:
                    return TryValidateBinary(binary, out unsupportedKind);

                case PhpUnaryOpAst unary:
                    return TryValidateUnary(unary, out unsupportedKind);

                case PhpNewAst newExpr:
                    if (newExpr.AnonymousClass is not null)
                    {
                        unsupportedKind = "anonymous class";
                        return false;
                    }

                    return TryValidateArgumentList(newExpr.Arguments, out unsupportedKind);

                case PhpArrayAst:
                    unsupportedKind = "array literal";
                    return false;

                case PhpDereferenceableAst deref:
                    return TryValidateDereferenceable(deref, out unsupportedKind);

                case PhpDereferenceableExpressionAst paren
                    when paren.Expression is IExpression inner:
                    return TryValidateExpression(inner, out unsupportedKind);

                default:
                    unsupportedKind = DescribeUnsupported(expression);
                    return false;
            }
        }

        private static bool TryValidateBinary(PhpBinaryOpAst binary, out string? unsupportedKind)
        {
            unsupportedKind = null;
            var token = binary.Operator?.ValueInt64 ?? -1;
            var opText = binary.Operator?.ValueString ?? "";

            if (PhpAssignmentOperatorExtensions.FromToken((int)token) is not null
                || IsAssignmentOperatorText(opText))
            {
                unsupportedKind = "assignment";
                return false;
            }

            var binaryOp = PhpBinaryOperatorExtensions.FromToken((int)token);
            if (binaryOp == PhpBinaryOperator.InstanceOf
                || IsInstanceOfText(opText))
            {
                // Phase 3: `$x instanceof T` / `$x is int` — the RHS is a type reference, not a
                // value expression. Dynamic `$x instanceof $classNameVar` is allowed (captured).
                if (!TryValidateExpression(binary.Left, out unsupportedKind))
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

            return TryValidateExpression(binary.Left, out unsupportedKind)
                && TryValidateExpression(binary.Right, out unsupportedKind);
        }

        private static bool TryValidateUnary(PhpUnaryOpAst unary, out string? unsupportedKind)
        {
            unsupportedKind = null;
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
                return TryValidateExpression(unary.Operand, out unsupportedKind);
            }

            if (token is TyhpParser.T_SYM_BANG
                or TyhpParser.T_SYM_MINUS
                or TyhpParser.T_SYM_PLUS
                or TyhpParser.T_SYM_TILDE
                || opText is "!" or "-" or "+" or "~")
            {
                return TryValidateExpression(unary.Operand, out unsupportedKind);
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
            out string? unsupportedKind)
        {
            unsupportedKind = null;

            // Parenthesized: (expr)->… or (expr) alone wrapped as dereferenceable base.
            if (deref.Base is PhpDereferenceableExpressionAst paren)
            {
                if (deref.Suffix is null)
                {
                    return TryValidateExpression(paren.Expression, out unsupportedKind);
                }

                if (!TryValidateExpression(paren.Expression, out unsupportedKind))
                {
                    return false;
                }
            }

            if (deref.Suffix is PhpCallAst call)
            {
                if (deref.Base is PhpDereferenceableAst inner
                    && inner.Suffix is PhpInstanceMemberAccessAst instanceMember)
                {
                    // `$obj?->method(...)` has no expression-node representation yet.
                    if (IsNullSafeAccessor(instanceMember.Accessor))
                    {
                        unsupportedKind = "nullsafe method call";
                        return false;
                    }

                    // $obj->method(...)
                    return TryValidateExpression(inner.Base as IExpression, out unsupportedKind)
                        && TryValidateArgumentList(call.Arguments, out unsupportedKind);
                }

                if (deref.Base is PhpDereferenceableAst staticInner
                    && (staticInner.Suffix is PhpStaticMemberAccessAst
                        || staticInner.Suffix is PhpClassConstantAccessAst))
                {
                    // Class::method(...) — parsed as class-constant-access (or ::$var static member).
                    return TryValidateArgumentList(call.Arguments, out unsupportedKind);
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

                return TryValidateExpression(deref.Base as IExpression, out unsupportedKind);
            }

            if (deref.Suffix is PhpStaticMemberAccessAst staticMember)
            {
                // Class::$var / rare static-member spelling without call.
                if (GetMemberName(staticMember.Member) is null)
                {
                    unsupportedKind = "dynamic member";
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

                return true;
            }

            if (deref.Suffix is PhpArrayAccessAst arrayAccess)
            {
                return TryValidateExpression(deref.Base as IExpression, out unsupportedKind)
                    && TryValidateExpression(arrayAccess.IndexExpression, out unsupportedKind);
            }

            if (deref.Suffix is null)
            {
                return TryValidateExpression(deref.Base as IExpression, out unsupportedKind);
            }

            unsupportedKind = "dereference";
            return false;
        }

        private static bool TryValidateArgumentList(
            PhpArgumentListAst? arguments,
            out string? unsupportedKind)
        {
            unsupportedKind = null;
            if (arguments is null)
            {
                return true;
            }

            foreach (var arg in arguments.GetAllNotNull())
            {
                if (arg.Expression is not null
                    && !TryValidateExpression(arg.Expression, out unsupportedKind))
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

            // Nested fn bodies are unsupported; still avoid descending into them for captures.
            if (node is PhpInlineFunctionAst)
            {
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

        private static bool IsCastToken(int token) =>
            token is TyhpParser.T_INT_CAST
                or TyhpParser.T_BOOL_CAST
                or TyhpParser.T_STRING_CAST
                or TyhpParser.T_DOUBLE_CAST
                or TyhpParser.T_DECIMAL_CAST
                or TyhpParser.T_ARRAY_CAST
                or TyhpParser.T_OBJECT_CAST;

        private static bool IsAssignmentOperatorText(string op) =>
            op is "=" or "+=" or "-=" or "*=" or "/=" or ".=" or "%=" or "**="
                or "&=" or "|=" or "^=" or "<<=" or ">>=" or "??=" or ":=";

        private static bool IsInstanceOfText(string op) =>
            op is "instanceof" or "is" or "isa" or "isan" or "is_a" or "is_an";

        /// <summary>
        /// RHS of <c>instanceof</c>/<c>is</c>: a type name, builtin, or a variable holding a
        /// class-name string. Nested expressions (calls, operators) are not a type target.
        /// </summary>
        private static bool IsSupportedInstanceofTarget(IExpression? right) =>
            right is PhpNameAst
                or PhpBuiltinTypeAst
                or PhpNamedTypeAst
                or PhpVariableAst
                or ITypeExpression
                or PhpDereferenceableAst { Suffix: PhpClassConstantAccessAst or PhpStaticMemberAccessAst };

        private static bool IsIncludeText(string op) =>
            op is "include" or "include_once" or "require" or "require_once";

        private static bool IsNullSafeAccessor(TokenValueAst? accessor) =>
            accessor?.ValueInt64 == TyhpParser.T_NULLSAFE_OBJECT_OPERATOR
            || string.Equals(accessor?.ValueString, "?->", StringComparison.Ordinal);

        private static string? GetMemberName(IExpression? memberName) =>
            memberName switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                PhpScalarAst scalar => scalar.ValueString,
                _ => null,
            };

        private static string DescribeUnsupported(IExpression expression)
        {
            var typeName = expression.GetType().Name;
            if (typeName.StartsWith("Php", StringComparison.Ordinal)
                && typeName.EndsWith("Ast", StringComparison.Ordinal))
            {
                typeName = typeName[3..^3];
            }

            return string.IsNullOrEmpty(typeName) ? "unsupported" : typeName;
        }
    }
}
