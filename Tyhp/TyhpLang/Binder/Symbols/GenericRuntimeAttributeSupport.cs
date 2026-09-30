using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Binder.Symbols
{
    /// <summary>
    /// <c>#[\Tyhp\GenericRuntime]</c>: identify the attribute, read named arguments
    /// (including <c>erased</c>, <c>layouts</c>, and <c>layout:</c> sugar), and persist them
    /// onto symbols for consumer emit and <c>\Tyhp\Generic::bind</c>.
    /// </summary>
    public static class GenericRuntimeAttributeSupport
    {
        public const int CurrentLayout = 1;

        private const string TyhpGenericRuntimeAttributeFqn = "\\Tyhp\\GenericRuntime";
        private const string ErasedArgumentName = "erased";
        private const string BinderArgumentName = "binder";
        private const string FactoryArgumentName = "factory";
        private const string AliasFactoryArgumentName = "aliasFactory";
        private const string LayoutsArgumentName = "layouts";
        private const string LayoutArgumentName = "layout";
        private const string CompilerArgumentName = "compiler";

        public static bool IsGenericRuntimeAttribute(PhpAttributeAst attribute)
        {
            if (attribute.Name is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol bound }
                && IsGenericRuntimeFullyQualifiedName(bound.FullyQualifiedName))
            {
                return true;
            }

            return IsGenericRuntimeFullyQualifiedName(GetAttributeNameText(attribute.Name));
        }

        public static bool IsGenericRuntimeFullyQualifiedName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var trimmed = name.Trim();
            var withoutRoot = trimmed.TrimStart('\\');
            return trimmed.Equals(TyhpGenericRuntimeAttributeFqn, StringComparison.OrdinalIgnoreCase)
                || withoutRoot.Equals("Tyhp\\GenericRuntime", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith("\\Tyhp\\GenericRuntime", StringComparison.OrdinalIgnoreCase)
                || withoutRoot.Equals("GenericRuntime", StringComparison.OrdinalIgnoreCase);
        }

        public static GenericRuntimeInfo? TryRead(IBase2Ast? host)
        {
            if (host is null)
            {
                return null;
            }

            foreach (var attributeNode in host.AstAttributes)
            {
                if (attributeNode is PhpAttributeAst attribute
                    && IsGenericRuntimeAttribute(attribute)
                    && TryReadArguments(attribute, out var info))
                {
                    return info;
                }
            }

            return null;
        }

        public static GenericRuntimeInfo? TryRead(IBaseSymbol? symbol)
        {
            if (symbol is BaseSymbol baseSymbol)
            {
                return baseSymbol.GenericRuntime ?? TryRead(baseSymbol.DeclaringAstNode);
            }

            return null;
        }

        public static bool TryReadArguments(PhpAttributeAst attribute, out GenericRuntimeInfo info)
        {
            bool? erased = null;
            string? binder = null;
            string? factory = null;
            string? aliasFactory = null;
            List<int>? layouts = null;
            int? layoutSugar = null;
            string? compiler = null;

            foreach (var argument in attribute.Arguments?.GetAllNotNull() ?? [])
            {
                var argName = argument.Name?.ValueString;
                if (string.IsNullOrEmpty(argName))
                {
                    argName = argument.Name?.Identifier;
                }

                if (string.IsNullOrEmpty(argName))
                {
                    continue;
                }

                if (string.Equals(argName, ErasedArgumentName, StringComparison.OrdinalIgnoreCase))
                {
                    if (TryReadBooleanLiteral(argument.Expression, out var erasedValue))
                    {
                        erased = erasedValue;
                    }
                }
                else if (string.Equals(argName, BinderArgumentName, StringComparison.OrdinalIgnoreCase))
                {
                    TryReadStringLiteral(argument.Expression, out binder);
                }
                else if (string.Equals(argName, FactoryArgumentName, StringComparison.OrdinalIgnoreCase))
                {
                    TryReadStringLiteral(argument.Expression, out factory);
                }
                else if (string.Equals(argName, AliasFactoryArgumentName, StringComparison.OrdinalIgnoreCase))
                {
                    TryReadStringLiteral(argument.Expression, out aliasFactory);
                }
                else if (string.Equals(argName, LayoutsArgumentName, StringComparison.OrdinalIgnoreCase))
                {
                    if (TryReadIntList(argument.Expression, out var parsedLayouts))
                    {
                        layouts = parsedLayouts;
                    }
                }
                else if (string.Equals(argName, LayoutArgumentName, StringComparison.OrdinalIgnoreCase))
                {
                    if (TryReadIntLiteral(argument.Expression, out var layout))
                    {
                        layoutSugar = layout;
                    }
                }
                else if (string.Equals(argName, CompilerArgumentName, StringComparison.OrdinalIgnoreCase))
                {
                    TryReadStringLiteral(argument.Expression, out compiler);
                }
            }

            IReadOnlyList<int> resolvedLayouts;
            if (layouts is not null)
            {
                resolvedLayouts = layouts;
            }
            else if (layoutSugar is { } sugar)
            {
                resolvedLayouts = [sugar];
            }
            else
            {
                resolvedLayouts = [CurrentLayout];
            }

            info = new GenericRuntimeInfo
            {
                Erased = erased ?? false,
                Binder = binder,
                Factory = factory,
                AliasFactory = aliasFactory,
                Layouts = resolvedLayouts,
                Compiler = compiler,
            };
            // Presence of the attribute is the stamp. Helper names, erased, and layouts are
            // optional payload; an empty `#[GenericRuntime]` still means foreign bind.
            return true;
        }

        public static bool IsTyhpdefSymbol(IBaseSymbol? symbol)
        {
            if (symbol is null)
            {
                return false;
            }

            var file = symbol.SourceFile ?? "";
            if (file.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase)
                || file.Contains("<tyhpdef:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var node = symbol.DeclaringAstNode;
            if (node is null)
            {
                return false;
            }

            return string.Equals(node.LanguageMode, "tyhpdef", StringComparison.OrdinalIgnoreCase)
                || node.OwningFile is TyhpdefSrcFileAst;
        }

        public static IReadOnlyList<string> FormatArgumentList(GenericRuntimeInfo info)
        {
            var arguments = new List<string>
            {
                "erased: " + (info.Erased ? "true" : "false"),
            };
            if (info.HasBinder)
            {
                arguments.Add("binder: " + QuoteString(info.Binder!));
            }

            if (info.HasFactory)
            {
                arguments.Add("factory: " + QuoteString(info.Factory!));
            }

            if (info.HasAliasFactory)
            {
                arguments.Add("aliasFactory: " + QuoteString(info.AliasFactory!));
            }

            var layouts = info.Layouts.Count > 0 ? info.Layouts : [CurrentLayout];
            arguments.Add("layouts: [" + string.Join(", ", layouts) + "]");
            if (!string.IsNullOrWhiteSpace(info.Compiler))
            {
                arguments.Add("compiler: " + QuoteString(info.Compiler!));
            }

            return arguments;
        }

        public static string FormatAttributeLine(GenericRuntimeInfo info)
            => "#[\\Tyhp\\GenericRuntime(" + string.Join(", ", FormatArgumentList(info)) + ")]";

        public static string QuoteString(string value)
            => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static bool TryReadStringLiteral(IExpression? expression, out string? value)
        {
            value = null;
            if (expression is null)
            {
                return false;
            }

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
                {
                    var parts = new List<string>();
                    foreach (var part in list.GetAllNotNull())
                    {
                        var piece = UnquotePhpString(part.ValueString);
                        if (!string.IsNullOrEmpty(piece))
                        {
                            parts.Add(piece);
                        }
                    }

                    if (parts.Count == 0)
                    {
                        return false;
                    }

                    value = string.Concat(parts);
                    return !string.IsNullOrEmpty(value);
                }

                default:
                    return false;
            }
        }

        private static bool TryReadIntLiteral(IExpression? expression, out int value)
        {
            value = CurrentLayout;
            if (expression is PhpScalarAst { ValueInt64: { } number })
            {
                value = (int)number;
                return true;
            }

            var text = GetLiteralText(expression);
            if (int.TryParse(text, out var parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        }

        private static bool TryReadBooleanLiteral(IExpression? expression, out bool value)
        {
            value = false;
            if (expression is PhpScalarAst { ValueBoolean: { } flag })
            {
                value = flag;
                return true;
            }

            var text = GetLiteralText(expression);
            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }

            if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }

            return false;
        }

        private static bool TryReadIntList(IExpression? expression, out List<int> values)
        {
            values = [];
            IEnumerable<PhpArrayPairAst>? pairs = expression switch
            {
                PhpArrayAst array => array.ArrayPairs?.GetAllNotNull(),
                PhpArrayPairListAst list => list.GetAllNotNull(),
                _ => null,
            };
            if (pairs is null)
            {
                return false;
            }

            foreach (var pair in pairs)
            {
                var item = pair.ValueExpr ?? pair.KeyExpr;
                if (!TryReadIntLiteral(item, out var number))
                {
                    values = [];
                    return false;
                }

                values.Add(number);
            }

            return true;
        }

        private static string? GetLiteralText(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => !string.IsNullOrEmpty(name.ValueString) ? name.ValueString : name.Identifier,
                PhpScalarAst scalar => !string.IsNullOrEmpty(scalar.ValueString) ? scalar.ValueString : scalar.Identifier,
                TokenValueAst token => !string.IsNullOrEmpty(token.ValueString) ? token.ValueString : token.Identifier,
                IExpression expr => expr.Identifier,
                _ => null,
            };

        private static string UnquotePhpString(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            var trimmed = text.Trim();
            if (trimmed.Length >= 2
                && ((trimmed[0] == '"' && trimmed[^1] == '"')
                    || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
            {
                return trimmed[1..^1];
            }

            return trimmed;
        }

        private static string? GetAttributeNameText(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => !string.IsNullOrEmpty(name.ValueString) ? name.ValueString : name.Identifier,
                TokenValueAst token => !string.IsNullOrEmpty(token.ValueString) ? token.ValueString : token.Identifier,
                IExpression expr => expr.Identifier,
                _ => null,
            };
    }
}
