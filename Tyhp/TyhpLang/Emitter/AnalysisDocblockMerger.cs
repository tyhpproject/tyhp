namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// One generated analysis tag. <see cref="Key"/> decides whether an author tag already
    /// covers it. <see cref="Text"/> is the tag without a leading <c>*</c>, for example
    /// <c>@param array&lt;int, \App\User&gt; $ids</c>.
    /// </summary>
    internal readonly record struct GeneratedDocTag(string Key, string Text);

    /// <summary>
    /// Merges an author docblock with generated analysis tags.
    /// Emitter directives and signed blocks stay in their original order and are never dropped.
    /// An author analysis tag wins per key. Summary prose is kept when comments are included.
    /// </summary>
    internal static class AnalysisDocblockMerger
    {
        public static string? Merge(
            string? authorDoc,
            IReadOnlyList<GeneratedDocTag> generated,
            bool includeProse)
        {
            var author = Parse(authorDoc);
            var consumed = new HashSet<string>(StringComparer.Ordinal);
            var tags = new List<string>();

            foreach (var generatedTag in generated)
            {
                if (!consumed.Add(generatedTag.Key))
                {
                    continue;
                }

                if (author.AnalysisByKey.TryGetValue(generatedTag.Key, out var authorTags))
                {
                    // An author may repeat a tag (`@throws`, `@property`); keep every one.
                    tags.AddRange(authorTags);
                }
                else if (!string.IsNullOrWhiteSpace(generatedTag.Text))
                {
                    tags.Add(generatedTag.Text.Trim());
                }
            }

            foreach (var extra in author.Analysis)
            {
                if (consumed.Contains(extra.Key))
                {
                    continue;
                }

                tags.Add(extra.Text);
            }

            var prose = includeProse ? author.Prose : [];
            var directives = author.Directives;
            if (prose.Count == 0 && directives.Count == 0 && tags.Count == 0)
            {
                return null;
            }

            var lines = new List<string> { "/**" };
            foreach (var line in prose)
            {
                lines.Add(Prefix(line));
            }

            if (prose.Count > 0 && prose[^1].Length > 0 && (directives.Count > 0 || tags.Count > 0))
            {
                lines.Add(" *");
            }

            foreach (var line in directives)
            {
                lines.Add(Prefix(line));
            }

            if (prose.Count == 0 && directives.Count > 0 && tags.Count > 0)
            {
                lines.Add(" *");
            }

            foreach (var tag in tags)
            {
                // A tag may span lines (`@param array{` … `}`, or a wrapped description).
                foreach (var line in tag.Split('\n'))
                {
                    lines.Add(Prefix(line));
                }
            }

            lines.Add(" */");
            return string.Join("\n", lines);
        }

        private static string Prefix(string text)
        {
            if (text.Length == 0)
            {
                return " *";
            }

            return " * " + text;
        }

        private readonly struct AuthorTag
        {
            public AuthorTag(string key, string text)
            {
                this.Key = key;
                this.Text = text;
            }

            public string Key { get; }

            public string Text { get; }
        }

        private readonly struct ParsedDoc
        {
            public ParsedDoc(
                List<string> prose,
                List<string> directives,
                List<AuthorTag> analysis,
                Dictionary<string, List<string>> analysisByKey)
            {
                this.Prose = prose;
                this.Directives = directives;
                this.Analysis = analysis;
                this.AnalysisByKey = analysisByKey;
            }

            public List<string> Prose { get; }

            public List<string> Directives { get; }

            public List<AuthorTag> Analysis { get; }

            public Dictionary<string, List<string>> AnalysisByKey { get; }
        }

        private static void AddAnalysis(
            List<AuthorTag> analysis,
            Dictionary<string, List<string>> analysisByKey,
            string key,
            string text)
        {
            analysis.Add(new AuthorTag(key, text));
            if (!analysisByKey.TryGetValue(key, out var texts))
            {
                texts = [];
                analysisByKey[key] = texts;
            }

            texts.Add(text);
        }

        /// <summary>
        /// Appends a continuation line to the last author analysis tag, keeping the tag a single
        /// unit so it is not split across prose and tag sections.
        /// </summary>
        private static void AppendToLastAnalysis(
            List<AuthorTag> analysis,
            Dictionary<string, List<string>> analysisByKey,
            string line)
        {
            var last = analysis[^1];
            var extended = last.Text + "\n" + line;
            analysis[^1] = new AuthorTag(last.Key, extended);
            var texts = analysisByKey[last.Key];
            texts[^1] = extended;
        }

        private static ParsedDoc Parse(string? authorDoc)
        {
            var prose = new List<string>();
            var directives = new List<string>();
            var analysis = new List<AuthorTag>();
            var analysisByKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var inAnalysisTag = false;
            if (string.IsNullOrWhiteSpace(authorDoc))
            {
                return new ParsedDoc(prose, directives, analysis, analysisByKey);
            }

            var rawLines = authorDoc.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            var inSignedBlock = false;
            foreach (var raw in rawLines)
            {
                var trimmed = raw.Trim();
                if (trimmed is "/**" or "*/")
                {
                    continue;
                }

                var text = StripStar(raw);
                if (text is "/**" or "*/" || text.StartsWith("/**", StringComparison.Ordinal) && text.EndsWith("*/", StringComparison.Ordinal))
                {
                    if (text is "/**" or "*/")
                    {
                        continue;
                    }

                    text = text["/**".Length..].Trim();
                    if (text.EndsWith("*/", StringComparison.Ordinal))
                    {
                        text = text[..^2].Trim();
                    }

                    if (text.Length == 0)
                    {
                        continue;
                    }
                }

                if (text == "/**" || text == "*/")
                {
                    continue;
                }

                var directive = inSignedBlock || IsDirective(text);
                if (text.Contains("/@@", StringComparison.Ordinal) && !text.Contains("@@/", StringComparison.Ordinal))
                {
                    inSignedBlock = true;
                }

                if (inSignedBlock && text.Contains("@@/", StringComparison.Ordinal))
                {
                    directive = true;
                    inSignedBlock = false;
                }

                if (directive)
                {
                    directives.Add(text);
                    inAnalysisTag = false;
                    continue;
                }

                if (TryParseAnalysis(text, out var key))
                {
                    AddAnalysis(analysis, analysisByKey, key, text);
                    inAnalysisTag = true;
                    continue;
                }

                // Text under an analysis tag with no blank line or new tag between belongs to it.
                if (inAnalysisTag && text.Length > 0 && text[0] != '@')
                {
                    AppendToLastAnalysis(analysis, analysisByKey, text);
                    continue;
                }

                inAnalysisTag = false;
                prose.Add(text);
            }

            return new ParsedDoc(prose, directives, analysis, analysisByKey);
        }

        private static string StripStar(string raw)
        {
            var text = raw.Trim();
            if (text.StartsWith("/**", StringComparison.Ordinal))
            {
                return text;
            }

            if (text.StartsWith('*'))
            {
                text = text[1..].TrimStart();
            }

            // A signed block ends in `@@/`, which also ends in `*/`. Leave that line intact.
            if (text.EndsWith("*/", StringComparison.Ordinal)
                && text != "*/"
                && !text.Contains("@@/", StringComparison.Ordinal))
            {
                text = text[..^2].TrimEnd();
            }

            return text;
        }

        private static bool IsDirective(string text) =>
            text.Contains("@tyhp", StringComparison.OrdinalIgnoreCase)
            || text.Contains("/@@", StringComparison.Ordinal)
            || text.Contains("@@/", StringComparison.Ordinal);

        internal static bool TryParseAnalysis(string text, out string key)
        {
            key = "";
            if (text.Length == 0 || text[0] != '@' || IsDirective(text))
            {
                return false;
            }

            var space = text.IndexOf(' ');
            var tag = space < 0 ? text : text[..space];
            var rest = space < 0 ? "" : text[(space + 1)..].Trim();
            var bare = tag.TrimStart('@');
            if (bare.StartsWith("phpstan-", StringComparison.OrdinalIgnoreCase))
            {
                bare = bare["phpstan-".Length..];
            }
            else if (bare.StartsWith("psalm-", StringComparison.OrdinalIgnoreCase))
            {
                bare = bare["psalm-".Length..];
            }

            bare = bare.ToLowerInvariant();
            switch (bare)
            {
                case "param":
                case "param-out":
                    key = bare + ":" + ExtractVariable(rest);
                    return key.Length > bare.Length + 1;
                case "template":
                case "template-covariant":
                case "template-contravariant":
                    key = "template:" + FirstToken(rest);
                    return key.Length > "template:".Length;
                case "extends":
                case "implements":
                case "use":
                    key = bare + ":" + HeritageShortName(rest);
                    return key.Length > bare.Length + 1;
                case "return":
                case "var":
                case "property":
                case "property-read":
                case "property-write":
                case "method":
                case "throws":
                case "internal":
                case "deprecated":
                case "readonly":
                case "immutable":
                case "final":
                case "api":
                    key = bare;
                    return true;
                default:
                    return false;
            }
        }

        internal static string ExtractVariable(string rest)
        {
            // The variable follows the type, which can itself contain `$` (`$this`) and nested
            // brackets. The description after it may mention other variables, so take the first
            // `$` outside brackets rather than the last one on the line.
            var depth = 0;
            var candidates = new List<int>();
            for (var i = 0; i < rest.Length; i++)
            {
                var ch = rest[i];
                if (ch is '<' or '{' or '(' or '[')
                {
                    depth++;
                }
                else if (ch is '>' or '}' or ')' or ']')
                {
                    depth = Math.Max(0, depth - 1);
                }
                else if (ch == '$' && depth == 0)
                {
                    candidates.Add(i);
                }
            }

            if (candidates.Count == 0)
            {
                return "";
            }

            var dollar = candidates[0];
            if (candidates.Count > 1 && VariableNameAt(rest, dollar) == "$this")
            {
                // `@param $this $x`: the first token is the type.
                dollar = candidates[1];
            }

            return VariableNameAt(rest, dollar);
        }

        private static string VariableNameAt(string text, int dollar)
        {
            var end = dollar + 1;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
            {
                end++;
            }

            return text[dollar..end];
        }

        private static string FirstToken(string rest)
        {
            var end = 0;
            while (end < rest.Length && (char.IsLetterOrDigit(rest[end]) || rest[end] == '_'))
            {
                end++;
            }

            return end == 0 ? "" : rest[..end];
        }

        internal static string HeritageShortName(string typeSpelling)
        {
            var head = typeSpelling.Split('<', 2)[0].Trim().TrimStart('\\');
            if (head.Length == 0)
            {
                return "";
            }

            var slash = head.LastIndexOf('\\');
            return slash < 0 ? head : head[(slash + 1)..];
        }
    }
}
