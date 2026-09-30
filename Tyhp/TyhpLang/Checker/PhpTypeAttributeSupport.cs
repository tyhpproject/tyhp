using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Compile-time <c>#[\Tyhp\PhpType('…')]</c>: allowed declaration hosts, constructor-string
    /// PHP type-hint spelling, and emit lookup. Checker types are unchanged; the string replaces
    /// the emitted PHP type of the host (and of implementors that inherit the hint).
    /// </summary>
    internal static class PhpTypeAttributeSupport
    {
        private const string TyhpPhpTypeAttributeFqn = "\\Tyhp\\PhpType";
        private const string TypeArgumentName = "type";

        private static readonly HashSet<string> IntersectionForbidden = new(StringComparer.OrdinalIgnoreCase)
        {
            "int", "float", "string", "bool", "array", "object", "callable", "iterable",
            "mixed", "void", "never", "null", "false", "true", "static",
        };

        public static bool IsPhpTypeAttribute(PhpAttributeAst attribute)
        {
            if (attribute.Name is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol bound }
                && IsPhpTypeFullyQualifiedName(bound.FullyQualifiedName))
            {
                return true;
            }

            return IsPhpTypeFullyQualifiedName(GetAttributeNameText(attribute.Name));
        }

        public static bool IsPhpTypeFullyQualifiedName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var trimmed = name.Trim();
            return trimmed.Equals(TyhpPhpTypeAttributeFqn, StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Tyhp\\PhpType", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith("\\Tyhp\\PhpType", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAllowedTarget(IBase2Ast target) =>
            target is PhpParameterAst
                or PhpFunctionDeclAst
                or PhpMethodDeclAst
                or PhpInlineFunctionAst
                or PhpPropertyDeclAst
                or PhpConstDeclListAst
                or PhpConstDeclAst
                or TyhpdefImportFunctionDeclAst;

        /// <summary>
        /// Reads a local <c>#[\Tyhp\PhpType]</c> constructor string. Does not validate spelling
        /// (the checker already rejected invalid ones; emit trusts the written hint).
        /// </summary>
        public static bool TryGetHint(IBase2Ast? host, out string hint)
        {
            hint = "";
            if (host is null)
            {
                return false;
            }

            foreach (var attributeNode in host.AstAttributes)
            {
                if (attributeNode is PhpAttributeAst attribute
                    && IsPhpTypeAttribute(attribute)
                    && TryReadTypeArgument(attribute, out hint)
                    && !string.IsNullOrWhiteSpace(hint))
                {
                    hint = hint.Trim();
                    return true;
                }
            }

            return false;
        }

        public static bool TryReadTypeArgument(PhpAttributeAst attribute, out string type)
        {
            type = "";
            var arguments = attribute.Arguments?.GetAllNotNull();
            if (arguments is null)
            {
                return false;
            }

            PhpArgumentAst? chosen = null;
            PhpArgumentAst? firstPositional = null;
            foreach (var argument in arguments)
            {
                var argName = argument.Name?.ValueString;
                if (string.IsNullOrEmpty(argName))
                {
                    firstPositional ??= argument;
                    continue;
                }

                if (string.Equals(argName, TypeArgumentName, StringComparison.OrdinalIgnoreCase))
                {
                    chosen = argument;
                    break;
                }
            }

            chosen ??= firstPositional;
            return chosen?.Expression is not null
                && TryReadStringLiteral(chosen.Expression, out type);
        }

        public static bool IsValidPhpTypeHint(string? spelling)
        {
            if (string.IsNullOrWhiteSpace(spelling))
            {
                return false;
            }

            var scanner = new TypeHintScanner(spelling.Trim());
            if (!TryParseType(scanner, out var parsed) || !scanner.AtEnd)
            {
                return false;
            }

            return IsLegalParsedType(parsed);
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
                    return true;

                case PhpEncapsStringAst encaps:
                    value = UnquotePhpString(encaps.ValueString ?? encaps.TokenValue?.ValueString);
                    return !string.IsNullOrEmpty(value);

                case PhpEncapsListAst list:
                    value = string.Concat(
                        list.GetAllNotNull().Select(part => UnquotePhpString(part.ValueString)));
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

        private static string? GetAttributeNameText(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                IExpression expr => expr.Identifier,
                _ => null,
            };

        private enum TypeKind
        {
            Simple,
            Nullable,
            Union,
            Intersection,
            Dnf,
        }

        private sealed record ParsedType(TypeKind Kind, IReadOnlyList<string> Names);

        private static bool TryParseType(TypeHintScanner scanner, out ParsedType parsed)
        {
            parsed = new ParsedType(TypeKind.Simple, []);
            scanner.SkipWs();
            if (scanner.TryConsume('?'))
            {
                scanner.SkipWs();
                if (!TryParseSimpleName(scanner, out var nullableName))
                {
                    return false;
                }

                scanner.SkipWs();
                parsed = new ParsedType(TypeKind.Nullable, [nullableName]);
                return true;
            }

            return TryParseUnionOrIntersection(scanner, out parsed);
        }

        private static bool TryParseUnionOrIntersection(TypeHintScanner scanner, out ParsedType parsed)
        {
            parsed = new ParsedType(TypeKind.Simple, []);
            var members = new List<(bool ParenthesizedIntersection, List<string> Names)>();
            if (!TryParseUnionMember(scanner, out var first))
            {
                return false;
            }

            members.Add(first);
            scanner.SkipWs();
            while (scanner.TryConsume('|'))
            {
                scanner.SkipWs();
                if (!TryParseUnionMember(scanner, out var next))
                {
                    return false;
                }

                members.Add(next);
                scanner.SkipWs();
            }

            if (members.Count > 1
                && members.Any(m => m.Names.Count > 1 && !m.ParenthesizedIntersection))
            {
                return false;
            }

            if (members.Count == 1)
            {
                var only = members[0];
                if (only.Names.Count == 1 && !only.ParenthesizedIntersection)
                {
                    parsed = new ParsedType(TypeKind.Simple, only.Names);
                    return true;
                }

                parsed = new ParsedType(TypeKind.Intersection, only.Names);
                return true;
            }

            var kind = members.Any(m => m.Names.Count > 1) ? TypeKind.Dnf : TypeKind.Union;
            parsed = new ParsedType(kind, members.SelectMany(m => m.Names).ToList());
            return true;
        }

        private static bool TryParseUnionMember(
            TypeHintScanner scanner,
            out (bool ParenthesizedIntersection, List<string> Names) member)
        {
            member = (false, []);
            scanner.SkipWs();
            if (scanner.TryConsume('('))
            {
                scanner.SkipWs();
                if (!TryParseAmpList(scanner, out var grouped) || grouped.Count < 2)
                {
                    return false;
                }

                scanner.SkipWs();
                if (!scanner.TryConsume(')'))
                {
                    return false;
                }

                member = (true, grouped);
                return true;
            }

            if (!TryParseAmpList(scanner, out var names) || names.Count == 0)
            {
                return false;
            }

            member = (false, names);
            return true;
        }

        private static bool TryParseAmpList(TypeHintScanner scanner, out List<string> names)
        {
            names = [];
            if (!TryParseSimpleName(scanner, out var first))
            {
                return false;
            }

            names.Add(first);
            scanner.SkipWs();
            while (scanner.TryConsume('&'))
            {
                scanner.SkipWs();
                if (!TryParseSimpleName(scanner, out var next))
                {
                    return false;
                }

                names.Add(next);
                scanner.SkipWs();
            }

            return true;
        }

        private static bool TryParseSimpleName(TypeHintScanner scanner, out string name)
        {
            name = "";
            scanner.SkipWs();
            var start = scanner.Position;
            if (scanner.TryConsume('\\'))
            {
                if (!scanner.TryConsumeIdentifier())
                {
                    return false;
                }
            }
            else if (!scanner.TryConsumeIdentifier())
            {
                return false;
            }

            while (scanner.TryConsume('\\'))
            {
                if (!scanner.TryConsumeIdentifier())
                {
                    return false;
                }
            }

            name = scanner.Slice(start);
            return name.Length > 0;
        }

        private static bool IsLegalParsedType(ParsedType parsed)
        {
            if (parsed.Names.Count == 0)
            {
                return false;
            }

            if (parsed.Kind == TypeKind.Nullable)
            {
                var only = parsed.Names[0];
                return !IsStandaloneOnlyBuiltin(only) && !IsMixed(only);
            }

            if (parsed.Kind is TypeKind.Union or TypeKind.Dnf)
            {
                if (parsed.Names.Any(IsStandaloneOnlyBuiltin) || parsed.Names.Any(IsMixed))
                {
                    return false;
                }
            }

            if (parsed.Kind is TypeKind.Intersection or TypeKind.Dnf)
            {
                var intersectionNames = parsed.Kind == TypeKind.Intersection
                    ? parsed.Names
                    : parsed.Names;
                if (parsed.Kind == TypeKind.Intersection)
                {
                    foreach (var n in intersectionNames)
                    {
                        if (IntersectionForbidden.Contains(LastSegment(n)))
                        {
                            return false;
                        }
                    }
                }
            }

            if (parsed.Kind == TypeKind.Simple)
            {
                return parsed.Names.Count == 1;
            }

            return true;
        }

        private static bool IsStandaloneOnlyBuiltin(string name)
        {
            var last = LastSegment(name);
            return last.Equals("void", StringComparison.OrdinalIgnoreCase)
                || last.Equals("never", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMixed(string name) =>
            LastSegment(name).Equals("mixed", StringComparison.OrdinalIgnoreCase);

        private static string LastSegment(string name)
        {
            var trimmed = name.TrimStart('\\');
            var slash = trimmed.LastIndexOf('\\');
            return slash < 0 ? trimmed : trimmed[(slash + 1)..];
        }

        private sealed class TypeHintScanner
        {
            private readonly string _text;

            public TypeHintScanner(string text) => _text = text;

            public int Position { get; private set; }

            public bool AtEnd
            {
                get
                {
                    SkipWs();
                    return Position >= _text.Length;
                }
            }

            public void SkipWs()
            {
                while (Position < _text.Length && char.IsWhiteSpace(_text[Position]))
                {
                    Position++;
                }
            }

            public bool TryConsume(char c)
            {
                SkipWs();
                if (Position < _text.Length && _text[Position] == c)
                {
                    Position++;
                    return true;
                }

                return false;
            }

            public bool TryConsumeIdentifier()
            {
                if (Position >= _text.Length || !IsIdentifierStart(_text[Position]))
                {
                    return false;
                }

                Position++;
                while (Position < _text.Length && IsIdentifierPart(_text[Position]))
                {
                    Position++;
                }

                return true;
            }

            public string Slice(int start) => _text[start..Position];

            private static bool IsIdentifierStart(char c) =>
                c is '_' or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') || c >= '\u0080';

            private static bool IsIdentifierPart(char c) =>
                IsIdentifierStart(c) || c is >= '0' and <= '9';
        }
    }
}
