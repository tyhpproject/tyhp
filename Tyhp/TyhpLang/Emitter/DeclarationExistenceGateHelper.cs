using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Recognizes top-level declaration gates of the form
    /// <c>if (!function_exists('\\Ns\\Name')) { function Name() {…} }</c>
    /// (and the class/enum/interface/trait counterparts) so the splitter can move the
    /// entire <c>if</c> with the declaration to its destination output file, and so the
    /// checker can require the gate argument to name that declaration.
    /// </summary>
    internal static class DeclarationExistenceGateHelper
    {
        private static readonly HashSet<string> ExistenceGateFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "function_exists",
            "class_exists",
            "enum_exists",
            "interface_exists",
            "trait_exists",
        };

        /// <summary>
        /// True when <paramref name="statement"/> is an <c>if (!*_exists(...))</c> whose then-body
        /// contains a single matching declaration whose gate argument names that declaration in
        /// <paramref name="currentNamespace"/>.
        /// </summary>
        public static bool TryGetValidExistenceGate(
            ITopStatement statement,
            string? currentNamespace,
            out PhpIfAst ifAst,
            out ITopStatement gatedDeclaration)
        {
            ifAst = null!;
            gatedDeclaration = null!;

            if (!TryGetExistenceGateCandidate(
                    statement,
                    out var candidate,
                    out var argument,
                    out var declaration,
                    out var declName)
                || argument is null)
            {
                return false;
            }

            // nameof(...) is deferred — do not treat as a movable gate yet.
            if (argument is TyhpNameofAst)
            {
                return false;
            }

            if (!IsValidGateArgument(argument, currentNamespace, declName))
            {
                return false;
            }

            ifAst = candidate;
            gatedDeclaration = declaration;
            return true;
        }

        /// <summary>
        /// Builds the PHP condition for a valid declaration gate, always rewriting the argument to
        /// <c>__NAMESPACE__ . '\\Name'</c> so output <c>namespacePrefix</c> does not break the check.
        /// </summary>
        public static bool TryBuildEmittedGateCondition(
            ITopStatement statement,
            string? currentNamespace,
            out string conditionPhp)
        {
            conditionPhp = "";

            if (!TryGetValidExistenceGate(statement, currentNamespace, out var ifAst, out var declaration))
            {
                return false;
            }

            if (!TryGetNegatedExistenceCall(ifAst.Condition, out var gateName, out _))
            {
                return false;
            }

            var simpleName = declaration switch
            {
                PhpFunctionDeclAst function => function.Identifier ?? "",
                PhpObjectTypeDeclAst objectDecl => objectDecl.Identifier ?? "",
                _ => "",
            };
            simpleName = GetSimpleName(simpleName);
            if (string.IsNullOrWhiteSpace(simpleName))
            {
                return false;
            }

            // Always absolute call + __NAMESPACE__ concat so prefixed output namespaces stay correct.
            // Emit a single backslash in the PHP source (`'\Name'`), same form users write in Tyhp.
            conditionPhp = $"!\\{gateName}(__NAMESPACE__ . '\\{simpleName}')";
            return true;
        }

        /// <summary>
        /// Shape check only: negated <c>*_exists</c> wrapping a single declaration of the matching
        /// kind. Does not validate the gate argument (used by the checker to report mismatches).
        /// </summary>
        public static bool TryGetExistenceGateCandidate(
            ITopStatement statement,
            out PhpIfAst ifAst,
            out IExpression? gateArgument,
            out ITopStatement gatedDeclaration,
            out string declName)
        {
            ifAst = null!;
            gateArgument = null;
            gatedDeclaration = null!;
            declName = "";

            if (statement is not PhpIfAst candidate)
            {
                return false;
            }

            // Gates are a single-branch guard; else clauses are not part of the idiom.
            if (candidate.ElseStatement != null)
            {
                return false;
            }

            if (!TryGetNegatedExistenceCall(candidate.Condition, out var gateName, out var argument))
            {
                return false;
            }

            if (!TryGetSingleGatedDeclaration(candidate.ThenStatement, out var declaration, out var declKind, out var name))
            {
                return false;
            }

            if (!GateMatchesDeclaration(gateName, declKind))
            {
                return false;
            }

            ifAst = candidate;
            gateArgument = argument;
            gatedDeclaration = declaration;
            declName = name;
            return true;
        }

        public static bool IsGatedFunctionStatement(ITopStatement statement, string? currentNamespace)
            => TryGetValidExistenceGate(statement, currentNamespace, out _, out var decl)
                && decl is PhpFunctionDeclAst;

        /// <summary>
        /// Stable partition: ungated statements first, then valid function-existence gates.
        /// Used so gated functions evaluate after plain declarations in <c>_functions.php</c>.
        /// </summary>
        public static void MoveGatedFunctionsToEnd(List<ITopStatement> statements, string? currentNamespace)
        {
            if (statements.Count < 2)
            {
                return;
            }

            var ungated = new List<ITopStatement>(statements.Count);
            var gated = new List<ITopStatement>();
            foreach (var statement in statements)
            {
                if (IsGatedFunctionStatement(statement, currentNamespace))
                {
                    gated.Add(statement);
                }
                else
                {
                    ungated.Add(statement);
                }
            }

            if (gated.Count == 0)
            {
                return;
            }

            statements.Clear();
            statements.AddRange(ungated);
            statements.AddRange(gated);
        }

        /// <summary>
        /// Fully-qualified name the gate must check for, e.g. <c>\TestEmitter\demo</c>.
        /// </summary>
        public static string BuildExpectedFqn(string? currentNamespace, string declName)
        {
            var simple = GetSimpleName(declName);
            var ns = (currentNamespace ?? "").Trim().TrimStart('\\');
            return string.IsNullOrEmpty(ns) ? "\\" + simple : "\\" + ns + "\\" + simple;
        }

        /// <summary>
        /// True when <paramref name="argument"/> names <paramref name="declName"/> in
        /// <paramref name="currentNamespace"/> via an FQN string literal or
        /// <c>__NAMESPACE__.'\\Name'</c>. <c>nameof(...)</c> is not accepted here (deferred).
        /// </summary>
        public static bool IsValidGateArgument(
            IExpression argument,
            string? currentNamespace,
            string declName)
        {
            if (TryGetStringLiteral(argument, out var literal))
            {
                return LiteralMatchesExpected(literal, currentNamespace, declName);
            }

            if (TryMatchNamespaceConcat(argument, declName))
            {
                return true;
            }

            return false;
        }

        private static bool LiteralMatchesExpected(
            string literal,
            string? currentNamespace,
            string declName)
        {
            if (string.IsNullOrWhiteSpace(literal))
            {
                return false;
            }

            var expected = NormalizeFqn(BuildExpectedFqn(currentNamespace, declName));
            var actual = NormalizeFqn(literal);

            // Unqualified names are only valid in the global namespace.
            if (string.IsNullOrEmpty((currentNamespace ?? "").Trim().TrimStart('\\')))
            {
                return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(actual, GetSimpleName(declName), StringComparison.OrdinalIgnoreCase);
            }

            // Namespaced declarations require the namespace segment — bare `demo` / `\demo` fail.
            if (!actual.Contains('\\', StringComparison.Ordinal))
            {
                return false;
            }

            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryMatchNamespaceConcat(IExpression argument, string declName)
        {
            if (argument is not PhpBinaryOpAst binary
                || !IsConcatOperator(binary)
                || binary.Left is not IExpression left
                || binary.Right is not IExpression right)
            {
                return false;
            }

            if (!IsNamespaceMagicConstant(left) || !TryGetStringLiteral(right, out var suffix))
            {
                return false;
            }

            // `__NAMESPACE__.'\demo'` → suffix must be `\SimpleName`.
            var expectedSuffix = "\\" + GetSimpleName(declName);
            return string.Equals(suffix, expectedSuffix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsConcatOperator(PhpBinaryOpAst binary)
        {
            var op = binary.Operator;
            if (op == null)
            {
                return false;
            }

            if (op.ValueInt64 is long tokenType
                && PhpBinaryOperatorExtensions.FromToken((int)tokenType) == PhpBinaryOperator.Concat)
            {
                return true;
            }

            return string.Equals(op.ValueString, ".", StringComparison.Ordinal);
        }

        private static bool IsNamespaceMagicConstant(IExpression expression)
        {
            switch (expression)
            {
                case PhpMagicConstantAst magic:
                    return string.Equals(magic.ValueString, "__NAMESPACE__", StringComparison.Ordinal);

                case PhpNameAst name:
                    return string.Equals(name.ValueString, "__NAMESPACE__", StringComparison.Ordinal);

                case TokenValueAst token:
                    return string.Equals(token.ValueString, "__NAMESPACE__", StringComparison.Ordinal);

                case PhpDereferenceableAst { Base: var nested, Suffix: null }
                    when nested is IExpression nestedExpr:
                    return IsNamespaceMagicConstant(nestedExpr);

                default:
                    return false;
            }
        }

        private static bool TryGetNegatedExistenceCall(
            IExpression? condition,
            out string gateName,
            out IExpression? argument)
        {
            gateName = "";
            argument = null;

            if (condition is not PhpUnaryOpAst unary
                || !IsLogicalNot(unary)
                || unary.Operand is not IExpression operand)
            {
                return false;
            }

            return TryGetExistenceCall(operand, out gateName, out argument);
        }

        private static bool IsLogicalNot(PhpUnaryOpAst unary)
        {
            var op = unary.Operator;
            if (op == null)
            {
                return false;
            }

            if (op.ValueInt64 is long tokenType && tokenType == TyhpParser.T_SYM_BANG)
            {
                return true;
            }

            return string.Equals(op.ValueString, "!", StringComparison.Ordinal);
        }

        private static bool TryGetExistenceCall(
            IExpression expression,
            out string gateName,
            out IExpression? argument)
        {
            gateName = "";
            argument = null;

            if (expression is not PhpDereferenceableAst { Suffix: PhpCallAst call } callNode)
            {
                return false;
            }

            var callee = GetCallableName(callNode.Base);
            if (string.IsNullOrWhiteSpace(callee))
            {
                return false;
            }

            var simple = GetSimpleName(callee);
            if (!ExistenceGateFunctions.Contains(simple))
            {
                return false;
            }

            var args = call.Arguments?.GetAllNotNull().ToList() ?? [];
            if (args.Count == 0 || args[0].Expression is null)
            {
                return false;
            }

            gateName = simple;
            argument = args[0].Expression;
            return true;
        }

        private static string? GetCallableName(IDereferenceableBase? callableBase)
            => callableBase switch
            {
                PhpNameAst name => name.ValueString ?? name.Identifier,
                TokenValueAst token => token.ValueString,
                PhpDereferenceableAst { Base: var nested, Suffix: null } => GetCallableName(nested),
                _ => null,
            };

        private static bool TryGetSingleGatedDeclaration(
            IStatement? thenStatement,
            out ITopStatement declaration,
            out string declKind,
            out string declName)
        {
            declaration = null!;
            declKind = "";
            declName = "";

            var body = thenStatement;
            if (body is PhpStatementBlockAst block)
            {
                var stmts = block.GetAllNotNull().ToList();
                if (stmts.Count != 1)
                {
                    return false;
                }

                body = stmts[0];
            }

            switch (body)
            {
                case PhpFunctionDeclAst function:
                    declaration = function;
                    declKind = "function";
                    declName = function.Identifier ?? "";
                    return !string.IsNullOrWhiteSpace(declName);

                case PhpObjectTypeDeclAst objectDecl:
                    declaration = objectDecl;
                    declKind = objectDecl.DeclType?.ValueString?.ToLowerInvariant() ?? "class";
                    declName = objectDecl.Identifier ?? "";
                    return !string.IsNullOrWhiteSpace(declName)
                        && !objectDecl.IsAnonymousClass;

                default:
                    return false;
            }
        }

        private static bool GateMatchesDeclaration(string gateName, string declKind)
            => gateName.ToLowerInvariant() switch
            {
                "function_exists" => declKind == "function",
                "class_exists" => declKind == "class",
                "enum_exists" => declKind == "enum",
                "interface_exists" => declKind == "interface",
                "trait_exists" => declKind == "trait",
                _ => false,
            };

        private static string NormalizeFqn(string name)
            => name.Trim().TrimStart('\\');

        private static string GetSimpleName(string name)
        {
            var trimmed = name.Trim().TrimStart('\\');
            var sep = trimmed.LastIndexOf('\\');
            return sep >= 0 ? trimmed[(sep + 1)..] : trimmed;
        }

        private static bool TryGetStringLiteral(IExpression expression, out string value)
        {
            switch (expression)
            {
                case PhpScalarAst { ScalarType: PhpScalarType.String, ValueString: { } scalarText }:
                    value = Unquote(scalarText);
                    return true;

                case PhpEncapsListAst encaps
                    when encaps.GetAllNotNull().Count() == 1
                    && encaps.GetAllNotNull().First() is PhpEncapsStringAst encapsString
                    && encapsString.ValueString is { } encapsText:
                    value = Unquote(encapsText);
                    return true;

                default:
                    value = "";
                    return false;
            }
        }

        private static string Unquote(string text)
        {
            if (text.Length < 2)
            {
                return text;
            }

            var quote = text[0];
            if ((quote != '"' && quote != '\'') || text[^1] != quote)
            {
                return text;
            }

            var inner = text[1..^1];
            if (quote == '\'')
            {
                // PHP single-quoted: only \\ and \' are escapes.
                return inner.Replace(@"\\", "\\", StringComparison.Ordinal)
                    .Replace(@"\'", "'", StringComparison.Ordinal);
            }

            // Double-quoted: unescape common escapes used in FQN gates.
            return inner
                .Replace(@"\\", "\\", StringComparison.Ordinal)
                .Replace(@"\'", "'", StringComparison.Ordinal)
                .Replace("\\\"", "\"", StringComparison.Ordinal);
        }
    }
}
