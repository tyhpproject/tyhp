using System.Globalization;

namespace Tyhp.Domain.Services.PhpDoc
{
    /// <summary>
    /// Parses PHPDoc type expressions into strings that are valid tyhpdef type syntax.
    /// Does not construct IR — callers pass the resulting string onward.
    /// </summary>
    public static class PhpDocTypeParser
    {
        /// <summary>
        /// Normalizes a complete PHPDoc type expression to tyhpdef type syntax.
        /// Unparseable or unrepresentable expressions become <c>mixed</c>.
        /// </summary>
        public static string Normalize(string? typeExpression)
        {
            if (string.IsNullOrWhiteSpace(typeExpression))
            {
                return "mixed";
            }

            var reader = new Reader(typeExpression);
            if (!TryParseUnion(reader, out var node))
            {
                return "mixed";
            }

            reader.SkipWs();
            return reader.AtEnd ? node.Emit() : "mixed";
        }

        /// <summary>
        /// Consumes one PHPDoc type from <paramref name="text"/> starting at
        /// <paramref name="start"/>. On success, <paramref name="end"/> is the index
        /// after the last character of the type (trailing whitespace is not consumed).
        /// </summary>
        public static bool TryConsume(string text, int start, out int end, out string normalized)
        {
            normalized = "mixed";
            end = start;
            if (string.IsNullOrEmpty(text) || start < 0 || start >= text.Length)
            {
                return false;
            }

            var reader = new Reader(text, start);
            reader.SkipWs();
            var typeStart = reader.Position;
            if (!TryParseUnion(reader, out var node))
            {
                return false;
            }

            end = reader.Position;
            if (end <= typeStart)
            {
                return false;
            }

            normalized = node.Emit();
            return true;
        }

        /// <summary>
        /// Consumes one PHPDoc type from the start of <paramref name="text"/>.
        /// <paramref name="remainder"/> is the unconsumed suffix (may include leading whitespace).
        /// </summary>
        public static bool TryConsume(string text, out string normalized, out string remainder)
        {
            if (TryConsume(text, 0, out var end, out normalized))
            {
                remainder = end < text.Length ? text[end..] : "";
                return true;
            }

            remainder = text;
            normalized = "mixed";
            return false;
        }

        /// <summary>
        /// One field of a PHPStan/Psalm array or list shape
        /// (<c>array{index: string, type?: int}</c>, <c>array{string, int}</c>).
        /// </summary>
        public sealed class ArrayShapeField
        {
            public ArrayShapeField(
                string key,
                string type,
                bool optional,
                bool keyIsQuotedString,
                bool keyIsInteger,
                IReadOnlyList<ArrayShapeField>? nestedShape)
            {
                this.Key = key;
                this.Type = type;
                this.Optional = optional;
                this.KeyIsQuotedString = keyIsQuotedString;
                this.KeyIsInteger = keyIsInteger;
                this.NestedShape = nestedShape;
            }

            public string Key { get; }

            public string Type { get; }

            public bool Optional { get; }

            public bool KeyIsQuotedString { get; }

            public bool KeyIsInteger { get; }

            public IReadOnlyList<ArrayShapeField>? NestedShape { get; }
        }

        /// <summary>
        /// Parses <c>array{...}</c>, <c>non-empty-array{...}</c>, <c>list{...}</c>,
        /// or a bare <c>{...}</c> shape. Returns false when the expression is not a shape.
        /// </summary>
        public static bool TryParseArrayShape(string? typeExpression, out IReadOnlyList<ArrayShapeField> fields)
        {
            fields = [];
            if (string.IsNullOrWhiteSpace(typeExpression))
            {
                return false;
            }

            var reader = new Reader(typeExpression);
            if (!TryParseArrayShape(reader, out var parsed))
            {
                return false;
            }

            reader.SkipWs();
            if (!reader.AtEnd)
            {
                return false;
            }

            fields = parsed;
            return true;
        }

        private static bool TryParseUnion(Reader reader, out Node node)
        {
            if (!TryParseIntersection(reader, out node))
            {
                return false;
            }

            List<Node>? members = null;
            while (reader.TryReadPipe())
            {
                if (!TryParseIntersection(reader, out var next))
                {
                    node = MixedNode.Instance;
                    return false;
                }

                members ??= [node];
                members.Add(next);
            }

            if (members is not null)
            {
                node = new UnionNode(members);
            }

            return true;
        }

        private static bool TryParseIntersection(Reader reader, out Node node)
        {
            if (!TryParseAtomic(reader, out node))
            {
                return false;
            }

            List<Node>? members = null;
            while (reader.TryReadAmpersand())
            {
                if (!TryParseAtomic(reader, out var next))
                {
                    node = MixedNode.Instance;
                    return false;
                }

                members ??= [node];
                members.Add(next);
            }

            if (members is not null)
            {
                node = new IntersectionNode(members);
            }

            return true;
        }

        private static bool TryParseAtomic(Reader reader, out Node node)
        {
            reader.SkipWs();
            var nullable = reader.TryRead('?');
            if (!TryParsePrimary(reader, out node))
            {
                return false;
            }

            while (true)
            {
                reader.SkipWs();
                if (reader.TryReadArrayPostfix())
                {
                    node = new ArrayPostfixNode(node);
                    continue;
                }

                break;
            }

            if (nullable)
            {
                node = new NullableNode(node);
            }

            return true;
        }

        private static bool TryParsePrimary(Reader reader, out Node node)
        {
            reader.SkipWs();
            if (reader.TryRead('('))
            {
                if (!TryParseUnion(reader, out var inner) || !reader.TryRead(')'))
                {
                    node = MixedNode.Instance;
                    return false;
                }

                // Tyhp `typeExpr` has no parenthesized-union form. Parens in PHPDoc only
                // group for `&`/`|` precedence; UnionNode / IntersectionNode add parens
                // where the grammar requires them (`(A&B)|C`).
                node = inner;
                return true;
            }

            if (reader.TryReadStringLiteral(out _, out var interpolates))
            {
                // The tyhpdef type grammar has no quoted-string-literal type syntax
                // (`typeWithoutStatic` only accepts `array` / `callable` / `name`), so a
                // PHPDoc string literal ('foo') can only be represented by its scalar type.
                node = interpolates ? MixedNode.Instance : new NamedNode("string", args: null);
                return true;
            }

            if (reader.TryReadNumberLiteral(out _, out var isFloat))
            {
                // Same reasoning as string literals: numeric literal types (0, -1, 0xFF, 3.14)
                // have no tyhpdef surface syntax, so fall back to the underlying scalar type.
                node = new NamedNode(isFloat ? "float" : "int", args: null);
                return true;
            }

            if (reader.Peek() == '{')
            {
                if (!reader.TrySkipBalanced('{', '}'))
                {
                    node = MixedNode.Instance;
                    return false;
                }

                node = new NamedNode("array", args: null);
                return true;
            }

            if (!reader.TryReadName(out var name))
            {
                node = MixedNode.Instance;
                return false;
            }

            // PhpStorm stubs (and php.net HTML copied into @return/@param) write
            // `array <p>description</p>`. That must not parse as a generic `array<p>`.
            var angleWasImmediate = reader.Peek() == '<';
            reader.SkipWs();
            if (IsCallableName(name) && reader.Peek() == '(')
            {
                if (!reader.TrySkipBalanced('(', ')'))
                {
                    node = MixedNode.Instance;
                    return false;
                }

                reader.SkipWs();
                if (reader.TryRead(':') && !TryParseUnion(reader, out _))
                {
                    node = MixedNode.Instance;
                    return false;
                }

                node = CallableNode.Instance;
                return true;
            }

            if (reader.Peek() == '{')
            {
                if (!reader.TrySkipBalanced('{', '}'))
                {
                    node = MixedNode.Instance;
                    return false;
                }

                if (IsArrayName(name))
                {
                    node = new NamedNode("array", args: null);
                    return true;
                }

                if (name.Equals("object", StringComparison.OrdinalIgnoreCase))
                {
                    node = new NamedNode("object", args: null);
                    return true;
                }

                node = MixedNode.Instance;
                return true;
            }

            List<Node>? args = null;
            if (reader.Peek() == '<')
            {
                if (reader.LooksLikeHtmlTag(allowUnclosedHtmlElement: !angleWasImmediate))
                {
                    node = new NamedNode(name, args: null);
                    return true;
                }

                if (!reader.TryParseGenericArgs(TryParseUnion, out args))
                {
                    node = MixedNode.Instance;
                    return false;
                }
            }

            node = new NamedNode(name, args);
            return true;
        }

        private static bool IsCallableName(string name)
        {
            if (name.Equals("callable", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var simple = name.TrimStart('\\');
            var slash = simple.LastIndexOf('\\');
            if (slash >= 0)
            {
                simple = simple[(slash + 1)..];
            }

            return simple.Equals("Closure", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsArrayName(string name)
            => name.Equals("array", StringComparison.OrdinalIgnoreCase);

        private static bool TryParseArrayShape(Reader reader, out List<ArrayShapeField> fields)
        {
            fields = [];
            reader.SkipWs();
            var start = reader.Position;
            if (reader.TryReadKeyword("non-empty-array")
                || reader.TryReadKeyword("non-empty-list")
                || reader.TryReadKeyword("array")
                || reader.TryReadKeyword("list"))
            {
                reader.SkipWs();
            }

            if (reader.Peek() != '{')
            {
                reader.Restore(start);
                return false;
            }

            reader.TryRead('{');
            var index = 0;
            while (true)
            {
                reader.SkipWs();
                if (reader.TryRead('}'))
                {
                    return true;
                }

                if (reader.TryReadDots())
                {
                    reader.SkipWs();
                    while (reader.Peek() != '}' && !reader.AtEnd)
                    {
                        if (reader.Peek() is '{' or '(' or '<')
                        {
                            var open = reader.Peek();
                            var close = open == '{' ? '}' : open == '(' ? ')' : '>';
                            if (!reader.TrySkipBalanced(open, close))
                            {
                                reader.Restore(start);
                                return false;
                            }

                            continue;
                        }

                        reader.Advance();
                    }

                    if (!reader.TryRead('}'))
                    {
                        reader.Restore(start);
                        return false;
                    }

                    return true;
                }

                if (!TryParseShapeField(reader, index, out var field))
                {
                    reader.Restore(start);
                    return false;
                }

                fields.Add(field);
                index++;
                reader.SkipWs();
                if (reader.TryRead(','))
                {
                    continue;
                }

                if (reader.TryRead('}'))
                {
                    return true;
                }

                reader.Restore(start);
                return false;
            }
        }

        private static bool TryParseShapeField(Reader reader, int positionalIndex, out ArrayShapeField field)
        {
            field = new ArrayShapeField("0", "mixed", optional: false, keyIsQuotedString: false, keyIsInteger: true, nestedShape: null);
            reader.SkipWs();
            var fieldStart = reader.Position;
            string key;
            var keyIsQuoted = false;
            var keyIsInteger = false;
            var optional = false;

            if (reader.TryReadStringLiteral(out var quoted, out var interpolates) && !interpolates)
            {
                key = Unquote(quoted);
                keyIsQuoted = true;
            }
            else if (reader.TryReadNumberLiteral(out var number, out var isFloat) && !isFloat)
            {
                key = number.Replace("_", "", StringComparison.Ordinal);
                keyIsInteger = true;
            }
            else if (reader.TryReadName(out var name)
                     && !name.Contains('\\', StringComparison.Ordinal)
                     && !name.Contains("::", StringComparison.Ordinal)
                     && name != "$this"
                     && name != "*")
            {
                key = name;
            }
            else
            {
                reader.Restore(fieldStart);
                return TryParsePositionalShapeField(reader, positionalIndex, out field);
            }

            reader.SkipWs();
            if (reader.TryRead('?'))
            {
                optional = true;
                reader.SkipWs();
            }

            if (!reader.TryRead(':'))
            {
                reader.Restore(fieldStart);
                return TryParsePositionalShapeField(reader, positionalIndex, out field);
            }

            if (!TryParseShapeValue(reader, out var type, out var nested))
            {
                return false;
            }

            field = new ArrayShapeField(key, type, optional, keyIsQuoted, keyIsInteger, nested);
            return true;
        }

        private static bool TryParsePositionalShapeField(Reader reader, int positionalIndex, out ArrayShapeField field)
        {
            field = new ArrayShapeField(
                positionalIndex.ToString(CultureInfo.InvariantCulture),
                "mixed",
                optional: false,
                keyIsQuotedString: false,
                keyIsInteger: true,
                nestedShape: null);
            if (!TryParseShapeValue(reader, out var type, out var nested))
            {
                return false;
            }

            field = new ArrayShapeField(
                positionalIndex.ToString(CultureInfo.InvariantCulture),
                type,
                optional: false,
                keyIsQuotedString: false,
                keyIsInteger: true,
                nested);
            return true;
        }

        private static bool TryParseShapeValue(Reader reader, out string type, out IReadOnlyList<ArrayShapeField>? nested)
        {
            type = "mixed";
            nested = null;
            reader.SkipWs();
            var saved = reader.Position;
            if (TryParseArrayShape(reader, out var nestedFields))
            {
                type = "";
                nested = nestedFields;
                return true;
            }

            reader.Restore(saved);
            if (!TryParseUnion(reader, out var node))
            {
                return false;
            }

            type = node.Emit();
            return true;
        }

        private static string Unquote(string literal)
        {
            if (literal.Length >= 2 && literal[0] is '\'' or '"' && literal[^1] == literal[0])
            {
                return literal[1..^1].Replace("\\'", "'", StringComparison.Ordinal).Replace("\\\"", "\"", StringComparison.Ordinal);
            }

            return literal;
        }

        private abstract class Node
        {
            public abstract string Emit();
        }

        private sealed class MixedNode : Node
        {
            public static readonly MixedNode Instance = new();

            public override string Emit() => "mixed";
        }

        private sealed class CallableNode : Node
        {
            public static readonly CallableNode Instance = new();

            public override string Emit() => "callable";
        }

        private sealed class NullableNode : Node
        {
            public NullableNode(Node inner) => this.Inner = inner;

            public Node Inner { get; }

            public override string Emit()
            {
                var inner = this.Inner.Emit();
                if (inner == "mixed")
                {
                    return "mixed";
                }

                if (this.Inner is UnionNode or IntersectionNode)
                {
                    return "?(" + inner + ")";
                }

                return "?" + inner;
            }
        }

        private sealed class ArrayPostfixNode : Node
        {
            public ArrayPostfixNode(Node element) => this.Element = element;

            public Node Element { get; }

            public override string Emit()
            {
                var element = this.Element.Emit();
                return "array<int, " + element + ">";
            }
        }

        private sealed class UnionNode : Node
        {
            public UnionNode(List<Node> members) => this.Members = members;

            public List<Node> Members { get; }

            public override string Emit()
            {
                var parts = new List<string>(this.Members.Count);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var member in this.Members)
                {
                    var emitted = member.Emit();
                    if (emitted == "mixed")
                    {
                        return "mixed";
                    }

                    var formatted = member is IntersectionNode ? "(" + emitted + ")" : emitted;

                    // Distinct PHPDoc literals (e.g. 'foo'|'bar', 0|1) collapse to the same
                    // scalar fallback type; do not repeat it in the emitted union.
                    if (seen.Add(formatted))
                    {
                        parts.Add(formatted);
                    }
                }

                return string.Join("|", parts);
            }
        }

        private sealed class IntersectionNode : Node
        {
            public IntersectionNode(List<Node> members) => this.Members = members;

            public List<Node> Members { get; }

            public override string Emit()
            {
                var parts = new List<string>(this.Members.Count);
                foreach (var member in this.Members)
                {
                    var emitted = member.Emit();
                    if (emitted == "mixed")
                    {
                        return "mixed";
                    }

                    if (member is UnionNode)
                    {
                        parts.Add("(" + emitted + ")");
                    }
                    else
                    {
                        parts.Add(emitted);
                    }
                }

                return string.Join("&", parts);
            }
        }

        private sealed class NamedNode : Node
        {
            private static readonly HashSet<string> ScalarBuiltins = new(StringComparer.OrdinalIgnoreCase)
            {
                "int", "integer", "string", "bool", "boolean", "float", "double", "long",
                "void", "mixed", "null", "true", "false", "never", "resource",
            };

            private static readonly HashSet<string> GenericBuiltins = new(StringComparer.OrdinalIgnoreCase)
            {
                "array", "list", "iterable",
            };

            public NamedNode(string name, List<Node>? args)
            {
                this.Name = name;
                this.Args = args;
            }

            public string Name { get; }

            public List<Node>? Args { get; }

            public override string Emit()
            {
                if (this.Name.Equals("class-string", StringComparison.OrdinalIgnoreCase))
                {
                    return "string";
                }

                if (this.Name == "$this")
                {
                    return "static";
                }

                if (this.Name == "*"
                    || this.Name.Contains('-', StringComparison.Ordinal)
                    || this.Name.Contains("::", StringComparison.Ordinal))
                {
                    return "mixed";
                }

                if (this.Args is { Count: > 0 })
                {
                    if (ScalarBuiltins.Contains(this.Name) && !GenericBuiltins.Contains(this.Name))
                    {
                        return "mixed";
                    }

                    var args = new List<string>(this.Args.Count);
                    foreach (var arg in this.Args)
                    {
                        args.Add(arg.Emit());
                    }

                    return this.Name + "<" + string.Join(", ", args) + ">";
                }

                return CanonicalScalarAlias(this.Name) ?? this.Name;
            }

            /// <summary>
            /// PHPDoc scalar aliases are not Tyhp type names. <c>long</c> is the
            /// historical php.net spelling of <c>int</c>.
            /// </summary>
            private static string? CanonicalScalarAlias(string name)
            {
                return name.ToLowerInvariant() switch
                {
                    "integer" => "int",
                    "boolean" => "bool",
                    "double" => "float",
                    "long" => "int",
                    _ => null,
                };
            }
        }

        private delegate bool ParseType(Reader reader, out Node node);

        private sealed class Reader
        {
            /// <summary>
            /// HTML elements used as prose in php.net / PhpStorm stubs. Excludes names that are
            /// also PHP types (<c>object</c>) so <c>array&lt;object&gt;</c> stays a generic.
            /// </summary>
            private static readonly HashSet<string> HtmlProseElements = new(StringComparer.OrdinalIgnoreCase)
            {
                "a", "abbr", "article", "aside", "b", "big", "blockquote", "caption", "center",
                "cite", "code", "dd", "del", "details", "dfn", "div", "dl", "dt", "em",
                "figcaption", "figure", "font", "footer", "h1", "h2", "h3", "h4", "h5", "h6",
                "header", "i", "ins", "kbd", "li", "main", "mark", "nav", "nobr", "ol", "p",
                "pre", "q", "s", "samp", "section", "small", "span", "strike", "strong",
                "sub", "summary", "sup", "table", "tbody", "td", "tfoot", "th", "thead", "tr",
                "tt", "u", "ul", "var",
            };

            private static readonly HashSet<string> HtmlVoidElements = new(StringComparer.OrdinalIgnoreCase)
            {
                "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta",
                "param", "source", "track", "wbr",
            };

            private readonly string _text;
            private int _i;

            public Reader(string text, int start = 0)
            {
                this._text = text;
                this._i = start;
            }

            public int Position => this._i;

            public bool AtEnd => this._i >= this._text.Length;

            public void Restore(int position)
                => this._i = position < 0 ? 0 : Math.Min(position, this._text.Length);

            public void Advance()
            {
                if (this._i < this._text.Length)
                {
                    this._i++;
                }
            }

            public bool TryReadKeyword(string keyword)
            {
                this.SkipWs();
                if (this._i + keyword.Length > this._text.Length)
                {
                    return false;
                }

                if (string.Compare(
                        this._text,
                        this._i,
                        keyword,
                        0,
                        keyword.Length,
                        StringComparison.OrdinalIgnoreCase) != 0)
                {
                    return false;
                }

                var after = this._i + keyword.Length;
                if (after < this._text.Length && IsIdentContinue(this._text[after]))
                {
                    return false;
                }

                this._i = after;
                return true;
            }

            public bool TryReadDots()
            {
                this.SkipWs();
                if (this.Peek() != '.' || this.Peek(1) != '.' || this.Peek(2) != '.')
                {
                    return false;
                }

                this._i += 3;
                return true;
            }

            public char Peek(int offset = 0)
            {
                var index = this._i + offset;
                return index >= 0 && index < this._text.Length ? this._text[index] : '\0';
            }

            public void SkipWs()
            {
                while (this._i < this._text.Length && char.IsWhiteSpace(this._text[this._i]))
                {
                    this._i++;
                }
            }

            public bool TryRead(char expected)
            {
                this.SkipWs();
                if (this.Peek() != expected)
                {
                    return false;
                }

                this._i++;
                return true;
            }

            public bool TryReadPipe()
            {
                this.SkipWs();
                var c = this.Peek();
                if (c == '|' && this.Peek(1) != '|')
                {
                    this._i++;
                    return true;
                }

                // PHPDoc sometimes writes ident/ident (php.net / stubs) instead of ident|ident.
                if (c == '/' && this.Peek(1) != '/' && this.Peek(1) != '*')
                {
                    this._i++;
                    return true;
                }

                return false;
            }

            public bool TryReadAmpersand()
            {
                this.SkipWs();
                if (this.Peek() != '&' || this.Peek(1) == '&' || this.Peek(1) == '$')
                {
                    return false;
                }

                this._i++;
                return true;
            }

            public bool TryReadArrayPostfix()
            {
                if (this.Peek() == '[' && this.Peek(1) == ']')
                {
                    this._i += 2;
                    return true;
                }

                return false;
            }

            public bool TryReadName(out string name)
            {
                this.SkipWs();
                var start = this._i;
                if (this.StartsWith("$this") && !IsIdentContinue(this.Peek(5)))
                {
                    this._i += 5;
                    name = "$this";
                    return true;
                }

                if (this.Peek() == '*')
                {
                    this._i++;
                    name = "*";
                    return true;
                }

                if (this.Peek() != '\\' && !IsIdentStart(this.Peek()))
                {
                    name = "";
                    return false;
                }

                while (this.Peek() == '\\' || IsIdentContinue(this.Peek()) || this.Peek() == '-')
                {
                    this._i++;
                }

                // Class-constant used as a type: Foo::BAR
                if (this.Peek() == ':' && this.Peek(1) == ':')
                {
                    this._i += 2;
                    while (IsIdentContinue(this.Peek()))
                    {
                        this._i++;
                    }
                }

                name = this._text[start..this._i];
                return name.Length > 0;
            }

            public bool TryReadStringLiteral(out string text, out bool interpolates)
            {
                interpolates = false;
                text = "";
                this.SkipWs();
                var quote = this.Peek();
                if (quote is not '\'' and not '"')
                {
                    return false;
                }

                var start = this._i;
                this._i++;
                while (this._i < this._text.Length)
                {
                    var c = this._text[this._i];
                    if (c == '\\' && this._i + 1 < this._text.Length)
                    {
                        this._i += 2;
                        continue;
                    }

                    if (quote == '"' && (c == '$' || c == '{'))
                    {
                        interpolates = true;
                    }

                    if (c == quote)
                    {
                        this._i++;
                        text = this._text[start..this._i];
                        return true;
                    }

                    this._i++;
                }

                return false;
            }

            public bool TryReadNumberLiteral(out string text, out bool isFloat)
            {
                text = "";
                isFloat = false;
                this.SkipWs();
                var start = this._i;
                if (this.Peek() is '+' or '-')
                {
                    if (!IsDigit(this.Peek(1)) && this.Peek(1) != '.')
                    {
                        return false;
                    }

                    this._i++;
                }

                if (this.Peek() == '0' && this.Peek(1) is 'x' or 'X' or 'b' or 'B' or 'o' or 'O')
                {
                    this._i += 2;
                    var digits = 0;
                    while (IsHexDigit(this.Peek()) || this.Peek() == '_')
                    {
                        if (this.Peek() != '_')
                        {
                            digits++;
                        }

                        this._i++;
                    }

                    if (digits == 0)
                    {
                        this._i = start;
                        return false;
                    }

                    text = this._text[start..this._i];
                    return true;
                }

                var sawDigit = false;
                while (IsDigit(this.Peek()) || this.Peek() == '_')
                {
                    if (this.Peek() != '_')
                    {
                        sawDigit = true;
                    }

                    this._i++;
                }

                if (this.Peek() == '.' && (IsDigit(this.Peek(1)) || sawDigit))
                {
                    isFloat = true;
                    this._i++;
                    while (IsDigit(this.Peek()) || this.Peek() == '_')
                    {
                        if (this.Peek() != '_')
                        {
                            sawDigit = true;
                        }

                        this._i++;
                    }
                }

                if (!sawDigit)
                {
                    this._i = start;
                    return false;
                }

                if (this.Peek() is 'e' or 'E')
                {
                    var exp = this._i;
                    this._i++;
                    if (this.Peek() is '+' or '-')
                    {
                        this._i++;
                    }

                    if (!IsDigit(this.Peek()))
                    {
                        this._i = exp;
                    }
                    else
                    {
                        isFloat = true;
                        while (IsDigit(this.Peek()) || this.Peek() == '_')
                        {
                            this._i++;
                        }
                    }
                }

                text = this._text[start..this._i];
                return true;
            }

            public bool TrySkipBalanced(char open, char close)
            {
                if (this.Peek() != open)
                {
                    return false;
                }

                var depth = 0;
                while (this._i < this._text.Length)
                {
                    var c = this._text[this._i];
                    if (c is '\'' or '"')
                    {
                        if (!this.TryReadStringLiteral(out _, out _))
                        {
                            return false;
                        }

                        continue;
                    }

                    if (c == open)
                    {
                        depth++;
                    }
                    else if (c == close)
                    {
                        depth--;
                        this._i++;
                        if (depth == 0)
                        {
                            return true;
                        }

                        continue;
                    }

                    this._i++;
                }

                return false;
            }

            /// <summary>
            /// PhpStorm / php.net PHPDoc often continues <c>@return array</c> with an HTML
            /// <c>&lt;p&gt;</c> (or <c>&lt;b&gt;</c>, <c>&lt;/p&gt;</c>, …). Those tags are not
            /// generic arguments. Matching close-tags follow PHPStan's phpdoc-parser; unclosed
            /// prose tags after whitespace cover <c>array &lt;p&gt;</c> without a close.
            /// Does not consume input.
            /// </summary>
            public bool LooksLikeHtmlTag(bool allowUnclosedHtmlElement)
            {
                if (this.Peek() != '<')
                {
                    return false;
                }

                var i = this._i + 1;
                var closing = false;
                if (i < this._text.Length && this._text[i] == '/')
                {
                    closing = true;
                    i++;
                }

                var nameStart = i;
                if (i >= this._text.Length || !char.IsAsciiLetter(this._text[i]))
                {
                    return false;
                }

                i++;
                while (i < this._text.Length && char.IsAsciiLetterOrDigit(this._text[i]))
                {
                    i++;
                }

                if (i == nameStart)
                {
                    return false;
                }

                var tagName = this._text[nameStart..i];
                while (i < this._text.Length && char.IsWhiteSpace(this._text[i]))
                {
                    i++;
                }

                if (i >= this._text.Length)
                {
                    return false;
                }

                if (this._text[i] == '/' && i + 1 < this._text.Length && this._text[i + 1] == '>')
                {
                    return true;
                }

                if (this._text[i] != '>')
                {
                    while (i < this._text.Length && this._text[i] != '>')
                    {
                        if (this._text[i] == '=')
                        {
                            return true;
                        }

                        if (this._text[i] == ',')
                        {
                            return false;
                        }

                        i++;
                    }

                    return false;
                }

                if (closing)
                {
                    return true;
                }

                var close = "</" + tagName + ">";
                if (this._text.IndexOf(close, i + 1, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                if (HtmlVoidElements.Contains(tagName))
                {
                    return true;
                }

                return allowUnclosedHtmlElement && HtmlProseElements.Contains(tagName);
            }

            public bool TryParseGenericArgs(ParseType parseType, out List<Node> args)
            {
                args = [];
                if (!this.TryRead('<'))
                {
                    return false;
                }

                while (true)
                {
                    this.SkipWs();
                    if (this.TryRead('>'))
                    {
                        return args.Count > 0;
                    }

                    if (!parseType(this, out var arg))
                    {
                        return false;
                    }

                    args.Add(arg);
                    this.SkipWs();
                    if (this.TryRead(','))
                    {
                        continue;
                    }

                    return this.TryRead('>');
                }
            }

            private bool StartsWith(string value)
            {
                if (this._i + value.Length > this._text.Length)
                {
                    return false;
                }

                return string.Compare(this._text, this._i, value, 0, value.Length, StringComparison.Ordinal) == 0;
            }

            private static bool IsIdentStart(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_';

            private static bool IsIdentContinue(char c)
                => IsIdentStart(c) || IsDigit(c) || c is '\\';

            private static bool IsDigit(char c) => c is >= '0' and <= '9';

            private static bool IsHexDigit(char c)
                => IsDigit(c) || c is (>= 'A' and <= 'F') or (>= 'a' and <= 'f');
        }
    }
}
