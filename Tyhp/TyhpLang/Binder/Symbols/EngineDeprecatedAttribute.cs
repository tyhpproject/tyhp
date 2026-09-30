using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Binder.Symbols
{
    /// <summary>
    /// PHP engine <c>#[\Deprecated]</c> / <c>#[Deprecated]</c>: name match (the class may be
    /// gated out of the symbol table when <c>output.phpVersion</c> is below 8.4). Sets
    /// <see cref="BaseSymbol.IsDeprecated"/> and, when <c>$message</c> is a string literal,
    /// <see cref="BaseSymbol.DeprecatedMessage"/>.
    /// </summary>
    internal static class EngineDeprecatedAttribute
    {
        private const string MessageArgumentName = "message";

        public static void Apply(BaseSymbol symbol, params IBase2Ast?[] extraHosts)
        {
            foreach (var host in EnumerateHosts(symbol.DeclaringAstNode, extraHosts))
            {
                if (!HasDeprecatedAttribute(host))
                {
                    continue;
                }

                symbol.IsDeprecated = true;
                if (string.IsNullOrEmpty(symbol.DeprecatedMessage)
                    && TryGetLiteralMessage(host, out var message))
                {
                    symbol.DeprecatedMessage = message;
                }
            }
        }

        public static bool HasDeprecatedAttribute(IBase2Ast? node)
        {
            if (node is null)
            {
                return false;
            }

            foreach (var attribute in node.AstAttributes.OfType<PhpAttributeAst>())
            {
                if (IsDeprecatedAttributeName(GetAttributeName(attribute.Name)))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetLiteralMessage(IBase2Ast node, out string message)
        {
            message = "";
            foreach (var attribute in node.AstAttributes.OfType<PhpAttributeAst>())
            {
                if (!IsDeprecatedAttributeName(GetAttributeName(attribute.Name)))
                {
                    continue;
                }

                if (TryReadMessageArgument(attribute, out message))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsDeprecatedAttributeName(string? name)
            => name is not null
                && (string.Equals(name, "Deprecated", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("\\Deprecated", StringComparison.OrdinalIgnoreCase));

        private static IEnumerable<IBase2Ast> EnumerateHosts(
            IBase2Ast? declaringNode,
            IBase2Ast?[] extraHosts)
        {
            if (declaringNode is not null)
            {
                yield return declaringNode;
            }

            foreach (var host in extraHosts)
            {
                if (host is not null && !ReferenceEquals(host, declaringNode))
                {
                    yield return host;
                }
            }
        }

        private static bool TryReadMessageArgument(PhpAttributeAst attribute, out string message)
        {
            message = "";
            var arguments = attribute.Arguments?.GetAllNotNull();
            if (arguments is null)
            {
                return false;
            }

            PhpArgumentAst? namedMessage = null;
            PhpArgumentAst? firstPositional = null;
            foreach (var argument in arguments)
            {
                var argName = NormalizeArgumentName(argument.Name?.ValueString);
                if (string.IsNullOrEmpty(argName))
                {
                    firstPositional ??= argument;
                    continue;
                }

                if (string.Equals(argName, MessageArgumentName, StringComparison.OrdinalIgnoreCase))
                {
                    namedMessage = argument;
                    break;
                }
            }

            var chosen = namedMessage ?? firstPositional;
            if (chosen?.Expression is null
                || !TryReadStringLiteral(chosen.Expression, out message)
                || string.IsNullOrWhiteSpace(message))
            {
                message = "";
                return false;
            }

            return true;
        }

        private static string? NormalizeArgumentName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            return name[0] == '$' ? name[1..] : name;
        }

        private static bool TryReadStringLiteral(IExpression expression, out string value)
        {
            value = "";
            switch (expression)
            {
                case PhpScalarAst scalar:
                    if (scalar.ValueInt64.HasValue || scalar.ValueDecimal.HasValue || scalar.ValueBoolean.HasValue)
                    {
                        return false;
                    }

                    if (string.IsNullOrEmpty(scalar.ValueString))
                    {
                        return false;
                    }

                    value = UnquotePhpString(scalar.ValueString);
                    return !string.IsNullOrEmpty(value);

                case PhpEncapsStringAst encaps:
                    value = UnquotePhpString(encaps.ValueString ?? encaps.TokenValue?.ValueString);
                    return !string.IsNullOrEmpty(value);

                case PhpEncapsListAst list:
                    // Only a genuine literal when every part is a plain string segment — a part
                    // that is itself a variable / member / expression (real string interpolation)
                    // is not a compile-time literal. Mirrors CheckerHelpers.IsConstantExpression's
                    // PhpEncapsListAst case; without this check, an interpolated `"use $x instead"`
                    // silently drops the variable and warns with a mangled truncated message.
                    var parts = list.GetAllNotNull().ToList();
                    if (!parts.All(part => part is PhpEncapsStringAst))
                    {
                        return false;
                    }

                    value = string.Concat(parts.Select(part => UnquotePhpString(part.ValueString)));
                    return !string.IsNullOrEmpty(value);

                default:
                    return false;
            }
        }

        private static string UnquotePhpString(string? literal)
        {
            if (string.IsNullOrEmpty(literal))
            {
                return "";
            }

            if (literal.Length >= 2
                && ((literal[0] == '\'' && literal[^1] == '\'')
                    || (literal[0] == '"' && literal[^1] == '"')))
            {
                return literal[1..^1];
            }

            return literal;
        }

        private static string? GetAttributeName(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => name.ValueString ?? name.Identifier,
                TokenValueAst token => token.ValueString,
                IExpression expr => expr.Identifier,
                _ => null,
            };
    }
}
