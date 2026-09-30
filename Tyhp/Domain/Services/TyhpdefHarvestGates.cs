using System.Diagnostics.CodeAnalysis;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Top-level PHP conditions harvest turns into <c>fallback</c>, <c>declare(php)</c>,
    /// or <c>declare(ext)</c>. Anything else inside an <c>if</c> stays skipped.
    /// </summary>
    internal static class TyhpdefHarvestGates
    {
        internal sealed record State(
            bool Drop = false,
            bool Fallback = false,
            string? PhpConstraint = null,
            string? ExtConstraint = null)
        {
            public static State None { get; } = new();

            public bool HasDeclare => PhpConstraint != null || ExtConstraint != null;

            public State Apply(State next) => new(
                Drop || next.Drop,
                Fallback || next.Fallback,
                next.PhpConstraint ?? PhpConstraint,
                next.ExtConstraint ?? ExtConstraint);
        }

        internal readonly record struct EarlyExit(State Subsequent, string? RequiredPath, State? RequiredState);

        internal static bool TryGetEarlyExit(ITopStatement statement, string? sourceFile, out EarlyExit exit)
        {
            exit = default;
            if (statement is not PhpIfAst ifAst || ifAst.ElseStatement != null || ifAst.Condition is not IExpression condition)
            {
                return false;
            }

            var body = Single(ifAst.ThenStatement);
            if (!TryGetReturnExpression(body, out var returned))
            {
                return false;
            }

            if (!TryClassifyTest(condition, out var positive, out var negative))
            {
                return false;
            }

            if (returned == null)
            {
                exit = new EarlyExit(negative, null, null);
                return true;
            }

            if (!TryGetRequirePath(returned, sourceFile, out var path))
            {
                return false;
            }

            exit = new EarlyExit(negative, path, positive);
            return true;
        }

        internal static bool TryGetDefinedConst(
            ITopStatement statement,
            out string name,
            out IExpression? value)
        {
            name = "";
            value = null;
            if (statement is not PhpIfAst ifAst
                || ifAst.ElseStatement != null
                || ifAst.Condition is not IExpression condition
                || !TryGetNegatedCall(condition, "defined", out var argument)
                || !DeclarationExistenceGateHelper.TryReadStringLiteral(argument, out var definedName))
            {
                return false;
            }

            if (!TryGetDefineCall(Single(ifAst.ThenStatement), out var defineName, out value))
            {
                return false;
            }

            if (!string.Equals(definedName, defineName, StringComparison.Ordinal))
            {
                return false;
            }

            name = defineName;
            return name.Length > 0;
        }

        private static bool TryClassifyTest(IExpression condition, out State positive, out State negative)
        {
            positive = State.None;
            negative = State.None;
            var negated = TryGetNegatedCall(condition, out var callName, out var argument, out var inner);
            var test = negated ? inner : condition;
            if (!negated && !TryGetCall(test, out callName, out argument))
            {
                return TryVersionTest(condition, out positive, out negative);
            }

            if (callName.Equals("extension_loaded", StringComparison.OrdinalIgnoreCase)
                && argument != null
                && DeclarationExistenceGateHelper.TryReadStringLiteral(argument, out var extension)
                && IsExtensionName(extension))
            {
                // if (extension_loaded) → true branch is the extension, which harvest drops.
                // False branch (code after return) exists only when the extension is absent.
                // if (!extension_loaded) → true branch is the polyfill (!ext); false branch is dropped.
                if (negated)
                {
                    positive = new State(ExtConstraint: "!" + extension);
                    negative = new State(Drop: true);
                }
                else
                {
                    positive = new State(Drop: true);
                    negative = new State(ExtConstraint: "!" + extension);
                }

                return true;
            }

            if (!negated
                && (callName.Equals("function_exists", StringComparison.OrdinalIgnoreCase)
                    || callName.Equals("defined", StringComparison.OrdinalIgnoreCase)))
            {
                positive = new State(Fallback: true);
                negative = new State(Fallback: true);
                return true;
            }

            return TryVersionTest(condition, out positive, out negative);
        }

        private static bool TryVersionTest(IExpression condition, out State positive, out State negative)
        {
            positive = State.None;
            negative = State.None;
            if (condition is not PhpBinaryOpAst binary
                || binary.Left is not IExpression left
                || binary.Right is not IExpression right
                || !IsPhpVersionId(left)
                || !TryGetInt(right, out var versionId)
                || !TryVersionString(versionId, out var version)
                || !TryOperator(binary, out var op))
            {
                return false;
            }

            var constraint = op + version;
            var negated = NegateOperator(op);
            if (negated == null)
            {
                return false;
            }

            positive = new State(PhpConstraint: constraint);
            negative = new State(PhpConstraint: negated + version);
            return true;
        }

        private static bool TryGetNegatedCall(
            IExpression condition,
            string name,
            [NotNullWhen(true)] out IExpression? argument)
        {
            argument = null;
            return TryGetNegatedCall(condition, out var callName, out argument, out _)
                && callName.Equals(name, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetNegatedCall(
            IExpression condition,
            out string callName,
            out IExpression? argument,
            out IExpression inner)
        {
            callName = "";
            argument = null;
            inner = condition;
            if (condition is not PhpUnaryOpAst unary || !IsBang(unary) || unary.Operand is not IExpression operand)
            {
                return false;
            }

            inner = operand;
            return TryGetCall(operand, out callName, out argument);
        }

        private static bool TryGetCall(IExpression expression, out string name, out IExpression? argument)
        {
            name = "";
            argument = null;
            if (expression is not PhpDereferenceableAst { Suffix: PhpCallAst call } node)
            {
                return false;
            }

            name = SimpleName(CallableName(node.Base));
            var args = call.Arguments?.GetAllNotNull().Select(a => a.Expression).Where(e => e != null).Cast<IExpression>().ToList()
                ?? [];
            if (name.Length == 0 || args.Count == 0)
            {
                return false;
            }

            argument = args[0];
            return true;
        }

        private static bool TryGetDefineCall(IStatement? statement, out string name, out IExpression? value)
        {
            name = "";
            value = null;
            if (statement is not IExpression expression || !TryGetCall(expression, out var callName, out var first))
            {
                return false;
            }

            if (!callName.Equals("define", StringComparison.OrdinalIgnoreCase)
                || statement is not PhpDereferenceableAst { Suffix: PhpCallAst call }
                || !DeclarationExistenceGateHelper.TryReadStringLiteral(first!, out name))
            {
                return false;
            }

            var args = call.Arguments?.GetAllNotNull().Select(a => a.Expression).Where(e => e != null).Cast<IExpression>().ToList()
                ?? [];
            if (args.Count < 2)
            {
                return false;
            }

            value = args[1];
            return true;
        }

        private static bool TryGetRequirePath(IExpression expression, string? sourceFile, out string path)
        {
            path = "";
            if (expression is not PhpUnaryOpAst unary || unary.Operand is not IExpression operand)
            {
                return false;
            }

            var op = unary.Operator?.ValueString ?? "";
            var tokenType = unary.Operator?.ValueInt64;
            var isRequire = op.Contains("require", StringComparison.OrdinalIgnoreCase)
                || tokenType == Tyhp.TyhpLang.Parser.TyhpParser.T_REQUIRE
                || tokenType == Tyhp.TyhpLang.Parser.TyhpParser.T_REQUIRE_ONCE;
            if (!isRequire)
            {
                return false;
            }

            if (!TryRequireLiteral(operand, out var relative))
            {
                return false;
            }

            path = Resolve(sourceFile, relative);
            return path.Length > 0;
        }

        private static bool TryRequireLiteral(IExpression operand, out string relative)
        {
            if (DeclarationExistenceGateHelper.TryReadStringLiteral(operand, out relative))
            {
                return true;
            }

            if (operand is PhpBinaryOpAst binary
                && binary.Right is IExpression right
                && DeclarationExistenceGateHelper.TryReadStringLiteral(right, out var suffix))
            {
                relative = suffix.TrimStart('/', '\\');
                return relative.Length > 0;
            }

            relative = "";
            return false;
        }

        private static string Resolve(string? sourceFile, string relative)
        {
            relative = relative.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(sourceFile))
            {
                return relative;
            }

            var dir = Path.GetDirectoryName(sourceFile);
            if (string.IsNullOrWhiteSpace(dir))
            {
                return relative;
            }

            try
            {
                return Path.GetFullPath(Path.Combine(dir, relative));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                return relative;
            }
        }

        private static bool TryGetReturnExpression(IStatement? statement, out IExpression? returned)
        {
            returned = null;
            switch (statement)
            {
                case PhpReturnStatementAst ret:
                    returned = ret.Expression;
                    return true;
                case PhpJumpStatementAst jump when jump.JumpType == PhpJumpType.Return:
                    returned = jump.Expression;
                    return true;
                case PhpUnaryOpAst unary when IsReturn(unary):
                    returned = unary.Operand;
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsReturn(PhpUnaryOpAst unary)
        {
            var op = unary.Operator;
            if (op?.ValueInt64 is long token && token == Tyhp.TyhpLang.Parser.TyhpParser.T_RETURN)
            {
                return true;
            }

            return string.Equals(op?.ValueString, "return", StringComparison.OrdinalIgnoreCase);
        }

        private static IStatement? Single(IStatement? statement)
        {
            if (statement is PhpStatementBlockAst block)
            {
                var items = block.GetAllNotNull().ToList();
                return items.Count == 1 ? items[0] : null;
            }

            return statement;
        }

        private static bool IsBang(PhpUnaryOpAst unary)
        {
            var op = unary.Operator;
            if (op?.ValueInt64 is long token && token == Tyhp.TyhpLang.Parser.TyhpParser.T_SYM_BANG)
            {
                return true;
            }

            return string.Equals(op?.ValueString, "!", StringComparison.Ordinal);
        }

        private static bool IsPhpVersionId(IExpression expression)
        {
            var name = expression switch
            {
                PhpNameAst nameAst => nameAst.ValueString ?? nameAst.Identifier,
                TokenValueAst token => token.ValueString,
                _ => null,
            };
            return string.Equals(name, "PHP_VERSION_ID", StringComparison.Ordinal);
        }

        private static bool TryGetInt(IExpression expression, out long value)
        {
            value = 0;
            return expression is PhpScalarAst scalar && scalar.ValueInt64 is long number && (value = number) >= 0;
        }

        private static bool TryVersionString(long versionId, out string version)
        {
            version = "";
            if (versionId < 10000)
            {
                return false;
            }

            var major = versionId / 10000;
            var minor = (versionId / 100) % 100;
            var patch = versionId % 100;
            if (major < 5 || major > 20)
            {
                return false;
            }

            version = patch == 0 ? $"{major}.{minor}" : $"{major}.{minor}.{patch}";
            return true;
        }

        private static bool TryOperator(PhpBinaryOpAst binary, out string op)
        {
            op = binary.Operator?.ValueString ?? "";
            op = op switch
            {
                ">=" or ">" or "<" or "<=" => op,
                _ => "",
            };
            if (op.Length > 0)
            {
                return true;
            }

            if (binary.Operator?.ValueInt64 is not long token)
            {
                return false;
            }

            op = token switch
            {
                Tyhp.TyhpLang.Parser.TyhpParser.T_IS_GREATER_OR_EQUAL => ">=",
                Tyhp.TyhpLang.Parser.TyhpParser.T_IS_SMALLER_OR_EQUAL => "<=",
                _ => "",
            };
            return op.Length > 0;
        }

        private static string? NegateOperator(string op) => op switch
        {
            ">=" => "<",
            ">" => "<=",
            "<" => ">=",
            "<=" => ">",
            _ => null,
        };

        private static bool IsExtensionName(string name)
            => name.Length > 0 && name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');

        private static string CallableName(IDereferenceableBase? callableBase) => callableBase switch
        {
            PhpNameAst name => name.ValueString ?? name.Identifier ?? "",
            TokenValueAst token => token.ValueString ?? "",
            PhpDereferenceableAst { Base: var nested, Suffix: null } => CallableName(nested),
            _ => "",
        };

        private static string SimpleName(string name)
        {
            var slash = name.LastIndexOf('\\');
            return slash >= 0 ? name[(slash + 1)..] : name;
        }
    }
}
