using Tyhp.Domain.Services.PhpDoc;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Pulls functions, types, and members plus preceding PHPDoc out of community stub files.
    /// Intentionally not a full PHP parser — stubs are declaration-heavy.
    /// </summary>
    internal static class StubPhpSymbolExtractor
    {
        public static IReadOnlyList<StubSymbol> ExtractFile(string path, string source)
        {
            var scanner = new Scanner(source ?? "");
            var symbols = new List<StubSymbol>();
            string currentNs = "";
            while (!scanner.AtEnd)
            {
                string? doc = null;
                scanner.SkipTrivia(ref doc);

                if (scanner.TryReadKeyword("namespace"))
                {
                    currentNs = scanner.ReadQualifiedName().Trim().TrimStart('\\');
                    scanner.SkipTrivia();
                    if (scanner.TryRead(';'))
                    {
                        continue;
                    }

                    if (scanner.TryRead('{'))
                    {
                        ParseBlock(scanner, currentNs, parent: null, symbols, closingBrace: true);
                        currentNs = "";
                    }

                    continue;
                }

                if (!TryParseDeclaration(scanner, currentNs, parent: null, doc, symbols))
                {
                    scanner.SkipOne();
                }
            }

            return symbols;
        }

        private static void ParseBlock(
            Scanner scanner,
            string ns,
            StubSymbol? parent,
            List<StubSymbol> symbols,
            bool closingBrace)
        {
            while (!scanner.AtEnd)
            {
                string? doc = null;
                scanner.SkipTrivia(ref doc);
                if (closingBrace && scanner.TryRead('}'))
                {
                    return;
                }

                if (!TryParseDeclaration(scanner, ns, parent, doc, symbols))
                {
                    if (scanner.TryRead('}'))
                    {
                        if (closingBrace)
                        {
                            return;
                        }

                        continue;
                    }

                    scanner.SkipOne();
                }
            }
        }

        private static bool TryParseDeclaration(
            Scanner scanner,
            string ns,
            StubSymbol? parent,
            string? doc,
            List<StubSymbol> symbols)
        {
            var saved = scanner.Position;
            var modifiers = scanner.ReadModifierSequence();
            scanner.SkipTrivia(ref doc);

            if (scanner.TryReadKeyword("function"))
            {
                var function = ReadFunction(scanner, ns, parent, doc, modifiers);
                if (function is not null)
                {
                    symbols.Add(function);
                    parent?.Members.Add(function);
                    return true;
                }

                scanner.Position = saved;
                return false;
            }

            if (scanner.TryReadKeyword("class")
                || scanner.TryReadKeyword("interface")
                || scanner.TryReadKeyword("trait")
                || scanner.TryReadKeyword("enum"))
            {
                scanner.Position = saved;
                modifiers = scanner.ReadModifierSequence();
                scanner.SkipTrivia(ref doc);
                var kind = scanner.ReadIdentifier();
                var type = ReadClass(scanner, ns, kind, doc, modifiers);
                if (type is not null)
                {
                    symbols.Add(type);
                    ParseBlock(scanner, ns, type, type.Members, closingBrace: true);
                    return true;
                }

                scanner.Position = saved;
                return false;
            }

            if (scanner.TryReadKeyword("define"))
            {
                var constant = ReadDefine(scanner, ns, doc);
                if (constant is not null)
                {
                    symbols.Add(constant);
                    return true;
                }

                scanner.Position = saved;
                return false;
            }

            if (scanner.TryReadKeyword("const"))
            {
                var constant = ReadConst(scanner, ns, parent, doc, modifiers);
                if (constant is not null)
                {
                    symbols.Add(constant);
                    parent?.Members.Add(constant);
                    return true;
                }

                scanner.Position = saved;
                return false;
            }

            if (parent is not null && TryReadProperty(scanner, ns, parent, doc, modifiers, out var property))
            {
                symbols.Add(property);
                parent.Members.Add(property);
                return true;
            }

            scanner.Position = saved;
            return false;
        }

        private static StubSymbol? ReadFunction(
            Scanner scanner,
            string ns,
            StubSymbol? parent,
            string? doc,
            List<string> modifiers)
        {
            scanner.SkipTrivia();
            scanner.TryRead('&');
            scanner.SkipTrivia();
            var name = scanner.ReadIdentifier();
            if (name.Length == 0)
            {
                return null;
            }

            scanner.SkipTrivia();
            if (!scanner.TryRead('('))
            {
                return null;
            }

            var paramText = scanner.ReadBalanced('(', ')');
            scanner.SkipTrivia();
            var returnType = "";
            if (scanner.TryRead(':'))
            {
                scanner.SkipTrivia();
                returnType = scanner.ReadTypeExpression();
            }

            scanner.SkipTrivia();
            if (scanner.Peek() == '{')
            {
                scanner.SkipBalanced('{', '}');
            }
            else
            {
                scanner.TryRead(';');
            }

            var block = PhpDocParser.Parse(doc);
            return new StubSymbol
            {
                Kind = parent is null ? StubSymbolKind.Function : StubSymbolKind.Method,
                Name = name,
                Fqn = parent is null ? Qualify(ns, name) : parent.Fqn + "::" + name,
                Namespace = ns,
                DocComment = doc,
                NativeReturnType = returnType,
                Parameters = ParseParameters(paramText),
                Modifiers = modifiers,
                PhpDoc = block,
            };
        }

        private static StubSymbol? ReadClass(
            Scanner scanner,
            string ns,
            string kind,
            string? doc,
            List<string> modifiers)
        {
            scanner.SkipTrivia();
            var name = scanner.ReadIdentifier();
            if (name.Length == 0)
            {
                return null;
            }

            scanner.SkipUntil('{');
            if (!scanner.TryRead('{'))
            {
                return null;
            }

            return new StubSymbol
            {
                Kind = StubSymbolKind.Type,
                Name = name,
                Fqn = Qualify(ns, name),
                Namespace = ns,
                TypeKind = kind.ToLowerInvariant(),
                DocComment = doc,
                Modifiers = modifiers,
                PhpDoc = PhpDocParser.Parse(doc),
                Members = [],
            };
        }

        private static StubSymbol? ReadDefine(Scanner scanner, string ns, string? doc)
        {
            scanner.SkipTrivia();
            if (!scanner.TryRead('('))
            {
                return null;
            }

            scanner.SkipTrivia();
            var name = scanner.ReadStringLiteral();
            scanner.SkipUntil(';');
            scanner.TryRead(';');
            if (name.Length == 0)
            {
                return null;
            }

            return new StubSymbol
            {
                Kind = StubSymbolKind.Constant,
                Name = name,
                Fqn = Qualify(ns, name),
                Namespace = ns,
                DocComment = doc,
                PhpDoc = PhpDocParser.Parse(doc),
            };
        }

        private static StubSymbol? ReadConst(
            Scanner scanner,
            string ns,
            StubSymbol? parent,
            string? doc,
            List<string> modifiers)
        {
            scanner.SkipTrivia();
            var type = "";
            var first = scanner.ReadIdentifier();
            scanner.SkipTrivia();
            var second = scanner.PeekIsIdentifierStart() ? scanner.ReadIdentifier() : "";
            string name;
            if (second.Length > 0)
            {
                type = first;
                name = second;
            }
            else
            {
                name = first;
            }

            scanner.SkipUntil(';');
            scanner.TryRead(';');
            if (name.Length == 0)
            {
                return null;
            }

            return MakeConst(ns, parent, name, type, doc, modifiers);
        }

        private static StubSymbol MakeConst(
            string ns,
            StubSymbol? parent,
            string name,
            string type,
            string? doc,
            List<string> modifiers)
            => new()
            {
                Kind = parent is null ? StubSymbolKind.Constant : StubSymbolKind.ClassConstant,
                Name = name,
                Fqn = parent is null ? Qualify(ns, name) : parent.Fqn + "::" + name,
                Namespace = ns,
                NativeReturnType = type,
                DocComment = doc,
                Modifiers = modifiers,
                PhpDoc = PhpDocParser.Parse(doc),
            };

        private static bool TryReadProperty(
            Scanner scanner,
            string ns,
            StubSymbol parent,
            string? doc,
            List<string> modifiers,
            out StubSymbol property)
        {
            property = null!;
            var saved = scanner.Position;
            var type = "";
            scanner.SkipTrivia();
            if (!scanner.PeekIs('$'))
            {
                type = scanner.ReadTypeExpression();
                scanner.SkipTrivia();
            }

            if (!scanner.TryRead('$'))
            {
                scanner.Position = saved;
                return false;
            }

            var name = scanner.ReadIdentifier();
            if (name.Length == 0)
            {
                scanner.Position = saved;
                return false;
            }

            scanner.SkipUntil(';');
            scanner.TryRead(';');
            var block = PhpDocParser.Parse(doc);
            var phpDocType = block.VarTag?.TypeExpression;
            property = new StubSymbol
            {
                Kind = StubSymbolKind.Property,
                Name = name,
                Fqn = parent.Fqn + "::$" + name,
                Namespace = ns,
                NativeReturnType = type,
                DocComment = doc,
                Modifiers = modifiers,
                PhpDoc = block,
                PhpDocType = phpDocType,
            };
            return true;
        }

        private static List<StubParameter> ParseParameters(string text)
        {
            var result = new List<StubParameter>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            foreach (var piece in SplitTopLevel(text, ','))
            {
                var param = ParseParameter(piece.Trim());
                if (param is not null)
                {
                    result.Add(param);
                }
            }

            return result;
        }

        private static StubParameter? ParseParameter(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var scanner = new Scanner(text);
            string? ignored = null;
            scanner.SkipTrivia(ref ignored);
            while (scanner.Peek() == '#')
            {
                scanner.SkipAttribute();
                scanner.SkipTrivia(ref ignored);
            }

            var byRef = false;
            var variadic = false;
            if (scanner.TryRead('&'))
            {
                byRef = true;
                scanner.SkipTrivia(ref ignored);
            }

            var type = "";
            if (!scanner.PeekIs('$') && scanner.Peek() != '.')
            {
                type = scanner.ReadTypeExpression();
                scanner.SkipTrivia(ref ignored);
            }

            if (scanner.TryRead('&'))
            {
                byRef = true;
                scanner.SkipTrivia(ref ignored);
            }

            if (scanner.StartsWith("..."))
            {
                variadic = true;
                scanner.Advance(3);
                scanner.SkipTrivia(ref ignored);
            }

            if (!scanner.TryRead('$'))
            {
                return null;
            }

            var name = scanner.ReadIdentifier();
            if (name.Length == 0)
            {
                return null;
            }

            return new StubParameter
            {
                Name = name,
                NativeType = type,
                IsByReference = byRef,
                IsVariadic = variadic,
            };
        }

        private static string Qualify(string ns, string name)
        {
            var trimmed = name.Trim().TrimStart('\\');
            if (string.IsNullOrWhiteSpace(ns))
            {
                return "\\" + trimmed;
            }

            return "\\" + ns.Trim().TrimStart('\\').TrimEnd('\\') + "\\" + trimmed;
        }

        private static List<string> SplitTopLevel(string text, char separator)
        {
            var parts = new List<string>();
            var depthParen = 0;
            var depthAngle = 0;
            var start = 0;
            var inString = '\0';
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (inString != '\0')
                {
                    if (c == '\\' && i + 1 < text.Length)
                    {
                        i++;
                        continue;
                    }

                    if (c == inString)
                    {
                        inString = '\0';
                    }

                    continue;
                }

                if (c is '"' or '\'')
                {
                    inString = c;
                    continue;
                }

                if (c == '(')
                {
                    depthParen++;
                }
                else if (c == ')')
                {
                    depthParen--;
                }
                else if (c == '<')
                {
                    depthAngle++;
                }
                else if (c == '>')
                {
                    depthAngle--;
                }
                else if (c == separator && depthParen <= 0 && depthAngle <= 0)
                {
                    parts.Add(text[start..i]);
                    start = i + 1;
                }
            }

            if (start <= text.Length)
            {
                parts.Add(text[start..]);
            }

            return parts;
        }

        private sealed class Scanner
        {
            private readonly string _text;

            public Scanner(string text)
            {
                this._text = text;
            }

            public int Position { get; set; }

            public bool AtEnd => this.Position >= this._text.Length;

            public char Peek()
                => this.AtEnd ? '\0' : this._text[this.Position];

            public bool PeekIs(char c)
                => this.Peek() == c;

            public bool PeekIsIdentifierStart()
            {
                var c = this.Peek();
                return char.IsAsciiLetter(c) || c is '_' or '\\';
            }

            public void Advance(int n)
                => this.Position = Math.Min(this._text.Length, this.Position + n);

            public void SkipOne()
            {
                if (!this.AtEnd)
                {
                    this.Position++;
                }
            }

            public bool TryRead(char c)
            {
                if (this.Peek() != c)
                {
                    return false;
                }

                this.Position++;
                return true;
            }

            public bool StartsWith(string value)
            {
                if (this.Position + value.Length > this._text.Length)
                {
                    return false;
                }

                return string.CompareOrdinal(this._text, this.Position, value, 0, value.Length) == 0;
            }

            public bool TryReadKeyword(string keyword)
            {
                if (!this.StartsWith(keyword))
                {
                    return false;
                }

                var after = this.Position + keyword.Length;
                if (after < this._text.Length && IsIdentContinue(this._text[after]))
                {
                    return false;
                }

                this.Position = after;
                return true;
            }

            public List<string> ReadModifierSequence()
            {
                var list = new List<string>();
                while (true)
                {
                    string? doc = null;
                    this.SkipTrivia(ref doc);
                    var ident = this.PeekIdentifier();
                    if (ident is "public" or "protected" or "private" or "static"
                        or "final" or "abstract" or "readonly" or "var")
                    {
                        this.ReadIdentifier();
                        list.Add(ident);
                        continue;
                    }

                    break;
                }

                return list;
            }

            public string PeekIdentifier()
            {
                var saved = this.Position;
                var name = this.ReadIdentifier();
                this.Position = saved;
                return name;
            }

            public string ReadIdentifier()
            {
                if (this.AtEnd)
                {
                    return "";
                }

                var start = this.Position;
                if (this._text[start] == '\\')
                {
                    this.Position++;
                }

                if (this.AtEnd || !IsIdentStart(this._text[this.Position]))
                {
                    this.Position = start;
                    return "";
                }

                this.Position++;
                while (!this.AtEnd && IsIdentContinue(this._text[this.Position]))
                {
                    this.Position++;
                }

                return this._text[start..this.Position].TrimStart('\\');
            }

            public string ReadQualifiedName()
            {
                this.SkipWhitespace();
                var start = this.Position;
                while (!this.AtEnd)
                {
                    var c = this._text[this.Position];
                    if (IsIdentContinue(c) || c == '\\')
                    {
                        this.Position++;
                        continue;
                    }

                    break;
                }

                return this._text[start..this.Position];
            }

            public string ReadTypeExpression()
            {
                var start = this.Position;
                var depthAngle = 0;
                var depthParen = 0;
                while (!this.AtEnd)
                {
                    var c = this._text[this.Position];
                    if (c == '<')
                    {
                        depthAngle++;
                    }
                    else if (c == '>' && depthAngle > 0)
                    {
                        depthAngle--;
                    }
                    else if (c == '(')
                    {
                        depthParen++;
                    }
                    else if (c == ')' && depthParen > 0)
                    {
                        depthParen--;
                    }
                    else if (depthAngle == 0 && depthParen == 0
                        && (char.IsWhiteSpace(c) || c is '$' or ',' or '{' or ';' or '=' or ')'))
                    {
                        break;
                    }

                    this.Position++;
                }

                return this._text[start..this.Position].Trim();
            }

            public string ReadBalanced(char open, char close)
            {
                // Caller already consumed the opening delimiter.
                var depth = 1;
                var start = this.Position;
                var inString = '\0';
                while (!this.AtEnd && depth > 0)
                {
                    var c = this._text[this.Position];
                    if (inString != '\0')
                    {
                        if (c == '\\' && this.Position + 1 < this._text.Length)
                        {
                            this.Position += 2;
                            continue;
                        }

                        if (c == inString)
                        {
                            inString = '\0';
                        }

                        this.Position++;
                        continue;
                    }

                    if (c is '"' or '\'')
                    {
                        inString = c;
                        this.Position++;
                        continue;
                    }

                    if (c == open)
                    {
                        depth++;
                    }
                    else if (c == close)
                    {
                        depth--;
                        if (depth == 0)
                        {
                            var inner = this._text[start..this.Position];
                            this.Position++;
                            return inner;
                        }
                    }

                    this.Position++;
                }

                return this._text[start..this.Position];
            }

            public void SkipBalanced(char open, char close)
            {
                if (!this.TryRead(open))
                {
                    return;
                }

                this.ReadBalanced(open, close);
            }

            public void SkipUntil(char c)
            {
                while (!this.AtEnd && this.Peek() != c)
                {
                    this.SkipOne();
                }
            }

            public string ReadStringLiteral()
            {
                var quote = this.Peek();
                if (quote is not ('"' or '\''))
                {
                    return "";
                }

                this.Position++;
                var start = this.Position;
                while (!this.AtEnd)
                {
                    var c = this._text[this.Position];
                    if (c == '\\' && this.Position + 1 < this._text.Length)
                    {
                        this.Position += 2;
                        continue;
                    }

                    if (c == quote)
                    {
                        var value = this._text[start..this.Position];
                        this.Position++;
                        return value;
                    }

                    this.Position++;
                }

                return this._text[start..this.Position];
            }

            public void SkipAttribute()
            {
                if (!this.StartsWith("#["))
                {
                    return;
                }

                this.Position += 2;
                var depth = 1;
                var inString = '\0';
                while (!this.AtEnd && depth > 0)
                {
                    var c = this._text[this.Position];
                    if (inString != '\0')
                    {
                        if (c == '\\' && this.Position + 1 < this._text.Length)
                        {
                            this.Position += 2;
                            continue;
                        }

                        if (c == inString)
                        {
                            inString = '\0';
                        }

                        this.Position++;
                        continue;
                    }

                    if (c is '"' or '\'')
                    {
                        inString = c;
                    }
                    else if (c == '[')
                    {
                        depth++;
                    }
                    else if (c == ']')
                    {
                        depth--;
                    }

                    this.Position++;
                }
            }

            public void SkipTrivia()
            {
                string? unused = null;
                this.SkipTrivia(ref unused);
            }

            public void SkipTrivia(ref string? lastDoc)
            {
                while (!this.AtEnd)
                {
                    this.SkipWhitespace();
                    if (this.StartsWith("<?php") || this.StartsWith("<?="))
                    {
                        this.Advance(this.StartsWith("<?php") ? 5 : 3);
                        continue;
                    }

                    if (this.StartsWith("<?"))
                    {
                        this.Advance(2);
                        continue;
                    }

                    if (this.StartsWith("?>"))
                    {
                        this.Advance(2);
                        continue;
                    }

                    if (this.StartsWith("/**"))
                    {
                        lastDoc = this.ReadCommentBlock();
                        continue;
                    }

                    if (this.StartsWith("/*"))
                    {
                        this.ReadCommentBlock();
                        continue;
                    }

                    if (this.StartsWith("//") || this.Peek() == '#')
                    {
                        if (this.StartsWith("#["))
                        {
                            this.SkipAttribute();
                            continue;
                        }

                        this.SkipLine();
                        continue;
                    }

                    break;
                }
            }

            public void SkipWhitespace()
            {
                while (!this.AtEnd && char.IsWhiteSpace(this._text[this.Position]))
                {
                    this.Position++;
                }
            }

            private string ReadCommentBlock()
            {
                var start = this.Position;
                var close = this._text.IndexOf("*/", this.Position, StringComparison.Ordinal);
                if (close < 0)
                {
                    this.Position = this._text.Length;
                    return this._text[start..];
                }

                this.Position = close + 2;
                return this._text[start..this.Position];
            }

            private void SkipLine()
            {
                while (!this.AtEnd && this._text[this.Position] is not ('\n' or '\r'))
                {
                    this.Position++;
                }
            }

            private static bool IsIdentStart(char c)
                => char.IsAsciiLetter(c) || c == '_';

            private static bool IsIdentContinue(char c)
                => char.IsAsciiLetterOrDigit(c) || c == '_';
        }
    }

    internal enum StubSymbolKind
    {
        Function,
        Type,
        Method,
        Property,
        Constant,
        ClassConstant,
    }

    internal sealed class StubSymbol
    {
        public StubSymbolKind Kind { get; init; }

        public string Name { get; init; } = "";

        public string Fqn { get; init; } = "";

        public string Namespace { get; init; } = "";

        public string? TypeKind { get; init; }

        public string? DocComment { get; init; }

        public string NativeReturnType { get; init; } = "";

        public string? PhpDocType { get; init; }

        public List<StubParameter> Parameters { get; init; } = [];

        public List<string> Modifiers { get; init; } = [];

        public PhpDocBlock PhpDoc { get; init; } = PhpDocBlock.Empty;

        public List<StubSymbol> Members { get; init; } = [];
    }

    internal sealed class StubParameter
    {
        public string Name { get; init; } = "";

        public string NativeType { get; init; } = "";

        public bool IsByReference { get; init; }

        public bool IsVariadic { get; init; }
    }
}
