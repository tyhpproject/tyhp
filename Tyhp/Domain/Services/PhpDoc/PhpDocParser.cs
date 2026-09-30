using System.Text;

namespace Tyhp.Domain.Services.PhpDoc
{
    /// <summary>
    /// Parses PHP <c>/** … */</c> doc comments into <see cref="PhpDocBlock"/>.
    /// </summary>
    public static class PhpDocParser
    {
        /// <summary>Parses a raw doc comment (including <c>/**</c> / <c>*/</c> delimiters if present).</summary>
        public static PhpDocBlock Parse(string? docComment)
        {
            if (string.IsNullOrWhiteSpace(docComment))
            {
                return CreateEmpty();
            }

            var lines = Unwrap(docComment);
            if (lines.Count == 0)
            {
                return CreateEmpty();
            }

            var tagStart = lines.FindIndex(IsTagLine);
            var prologue = tagStart < 0 ? lines : lines.GetRange(0, tagStart);
            SplitPrologue(prologue, out var summary, out var description);

            var tags = tagStart < 0
                ? []
                : ParseTags(lines.GetRange(tagStart, lines.Count - tagStart));

            return new PhpDocBlock(
                summary,
                description,
                tags,
                paramTags: SelectPreferredByName(
                    tags,
                    ["phpstan-param", "psalm-param", "param"]),
                returnTag: SelectPreferredSingle(
                    tags,
                    ["phpstan-return", "psalm-return", "return"]),
                throwsTags: [.. tags.Where(static t => t.TagName is "throws" or "phpstan-throws" or "psalm-throws")],
                templateTags: SelectPreferredByName(
                    tags,
                    [
                        "phpstan-template",
                        "psalm-template",
                        "template",
                        "template-covariant",
                        "template-contravariant",
                    ]),
                varTag: SelectPreferredSingle(
                    tags,
                    ["phpstan-var", "psalm-var", "var"]),
                deprecatedTag: tags.FirstOrDefault(static t => t.TagName == "deprecated"),
                isInternal: tags.Any(static t => t.TagName == "internal"),
                isDeprecated: tags.Any(static t => t.TagName == "deprecated"),
                methodTags: SelectPreferredByName(
                    tags,
                    ["phpstan-method", "psalm-method", "method"]),
                propertyTags: SelectPreferredByName(
                    tags,
                    [
                        "phpstan-property",
                        "phpstan-property-read",
                        "phpstan-property-write",
                        "psalm-property",
                        "psalm-property-read",
                        "psalm-property-write",
                        "property",
                        "property-read",
                        "property-write",
                    ]),
                typeAliasTags: SelectPreferredByName(
                    tags,
                    ["phpstan-type", "psalm-type", "phan-type"]),
                importTypeTags: SelectPreferredByName(
                    tags,
                    ["phpstan-import-type", "psalm-import-type"]));
        }

        private static PhpDocBlock CreateEmpty() => new(
            summary: "",
            description: "",
            tags: [],
            paramTags: [],
            returnTag: null,
            throwsTags: [],
            templateTags: [],
            varTag: null,
            deprecatedTag: null,
            isInternal: false,
            isDeprecated: false,
            methodTags: [],
            propertyTags: [],
            typeAliasTags: [],
            importTypeTags: []);

        private static List<string> Unwrap(string docComment)
        {
            var text = docComment.Trim();
            if (text.StartsWith("/**", StringComparison.Ordinal))
            {
                text = text[3..];
            }
            else if (text.StartsWith("/*", StringComparison.Ordinal))
            {
                text = text[2..];
            }

            if (text.EndsWith("*/", StringComparison.Ordinal))
            {
                text = text[..^2];
            }

            var result = new List<string>();
            foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
            {
                var line = raw;
                var i = 0;
                while (i < line.Length && char.IsWhiteSpace(line[i]))
                {
                    i++;
                }

                if (i < line.Length && line[i] == '*')
                {
                    i++;
                    if (i < line.Length && line[i] == ' ')
                    {
                        i++;
                    }

                    line = line[i..];
                }
                else
                {
                    line = line.TrimStart();
                }

                result.Add(line);
            }

            return result;
        }

        private static bool IsTagLine(string line)
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length < 2 || trimmed[0] != '@')
            {
                return false;
            }

            return IsTagNameChar(trimmed[1]);
        }

        private static bool IsTagNameChar(char c)
            => char.IsAsciiLetter(c) || c == '_';

        private static void SplitPrologue(List<string> prologue, out string summary, out string description)
        {
            var start = 0;
            while (start < prologue.Count && string.IsNullOrWhiteSpace(prologue[start]))
            {
                start++;
            }

            if (start >= prologue.Count)
            {
                summary = "";
                description = "";
                return;
            }

            summary = prologue[start].Trim();
            var descLines = prologue.Skip(start + 1).ToList();
            while (descLines.Count > 0 && string.IsNullOrWhiteSpace(descLines[0]))
            {
                descLines.RemoveAt(0);
            }

            while (descLines.Count > 0 && string.IsNullOrWhiteSpace(descLines[^1]))
            {
                descLines.RemoveAt(descLines.Count - 1);
            }

            description = descLines.Count == 0 ? "" : string.Join("\n", descLines);
        }

        private static List<PhpDocTag> ParseTags(List<string> tagLines)
        {
            var tags = new List<PhpDocTag>();
            string? currentName = null;
            var currentBody = new StringBuilder();

            void Flush()
            {
                if (currentName is null)
                {
                    return;
                }

                tags.Add(ParseTag(currentName, currentBody.ToString().TrimEnd()));
                currentName = null;
                currentBody.Clear();
            }

            foreach (var line in tagLines)
            {
                if (IsTagLine(line))
                {
                    Flush();
                    var trimmed = line.TrimStart();
                    var nameEnd = 1;
                    while (nameEnd < trimmed.Length && IsTagNameContinue(trimmed[nameEnd]))
                    {
                        nameEnd++;
                    }

                    currentName = trimmed[1..nameEnd].ToLowerInvariant();
                    var rest = nameEnd < trimmed.Length ? trimmed[nameEnd..] : "";
                    if (rest.StartsWith(' ') || rest.StartsWith('\t'))
                    {
                        rest = rest.TrimStart();
                    }

                    currentBody.Append(rest);
                }
                else if (currentName is not null)
                {
                    currentBody.Append('\n');
                    currentBody.Append(line);
                }
            }

            Flush();
            return tags;
        }

        private static bool IsTagNameContinue(char c)
            => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or ':';

        private static PhpDocTag ParseTag(string tagName, string rawContent)
        {
            return tagName switch
            {
                "param" or "phpstan-param" or "psalm-param"
                    or "param-out" or "phpstan-param-out" or "psalm-param-out"
                    or "assert" or "phpstan-assert" or "psalm-assert"
                    or "phpstan-assert-if-true" or "psalm-assert-if-true"
                    or "phpstan-assert-if-false" or "psalm-assert-if-false"
                    => ParseTypedNameTag(tagName, rawContent),

                "return" or "phpstan-return" or "psalm-return"
                    or "throws" or "phpstan-throws" or "psalm-throws"
                    => ParseTypedDescriptionTag(tagName, rawContent),

                "var" or "phpstan-var" or "psalm-var"
                    or "property" or "property-read" or "property-write"
                    or "phpstan-property" or "phpstan-property-read" or "phpstan-property-write"
                    or "psalm-property" or "psalm-property-read" or "psalm-property-write"
                    => ParseTypedNameTag(tagName, rawContent, nameOptional: true),

                "template" or "phpstan-template" or "psalm-template"
                    or "phan-template"
                    or "template-covariant" or "template-contravariant"
                    => ParseTemplateTag(tagName, rawContent),

                "implements" or "template-implements"
                    or "phpstan-implements" or "psalm-implements" or "phan-implements"
                    or "extends" or "template-extends"
                    or "phpstan-extends" or "psalm-extends" or "phan-extends"
                    => ParseTypedDescriptionTag(tagName, rawContent),

                "deprecated" or "internal"
                    => new PhpDocTag(tagName, null, null, EmptyToNull(rawContent.Trim()), rawContent),

                "method" or "phpstan-method" or "psalm-method"
                    => ParseMethodTag(tagName, rawContent),

                "phpstan-type" or "psalm-type" or "phan-type"
                    => ParseTypeAliasTag(tagName, rawContent),

                "phpstan-import-type" or "psalm-import-type"
                    => ParseImportTypeTag(tagName, rawContent),

                _ => new PhpDocTag(tagName, null, null, EmptyToNull(rawContent.Trim()), rawContent),
            };
        }

        private static PhpDocTag ParseTypedNameTag(string tagName, string rawContent, bool nameOptional = false)
        {
            SplitTypeNameDescription(rawContent, nameOptional, out var type, out var name, out var description);
            return new PhpDocTag(tagName, type, name, description, rawContent);
        }

        private static PhpDocTag ParseTypedDescriptionTag(string tagName, string rawContent)
        {
            if (PhpDocTypeParser.TryConsume(rawContent, out _, out var remainder))
            {
                var type = SliceType(rawContent, remainder);
                return new PhpDocTag(tagName, type, null, EmptyToNull(remainder.Trim()), rawContent);
            }

            return new PhpDocTag(tagName, null, null, EmptyToNull(rawContent.Trim()), rawContent);
        }

        private static PhpDocTag ParseTemplateTag(string tagName, string rawContent)
        {
            var rest = rawContent.TrimStart();
            var nameLength = 0;
            while (nameLength < rest.Length && IsTemplateNameChar(rest[nameLength]))
            {
                nameLength++;
            }

            if (nameLength == 0)
            {
                return new PhpDocTag(tagName, null, null, EmptyToNull(rawContent.Trim()), rawContent);
            }

            var name = rest[..nameLength];
            rest = rest[nameLength..].TrimStart();
            string? constraint = null;
            if (StartsWithWord(rest, "of") || StartsWithWord(rest, "as"))
            {
                rest = rest[2..].TrimStart();
                if (PhpDocTypeParser.TryConsume(rest, out _, out var remainder))
                {
                    constraint = SliceType(rest, remainder);
                    rest = remainder;
                }
            }

            return new PhpDocTag(tagName, constraint, name, EmptyToNull(rest.Trim()), rawContent);
        }

        private static PhpDocTag ParseTypeAliasTag(string tagName, string rawContent)
        {
            var rest = rawContent.TrimStart();
            var nameLength = 0;
            while (nameLength < rest.Length && IsTemplateNameChar(rest[nameLength]))
            {
                nameLength++;
            }

            if (nameLength == 0)
            {
                return new PhpDocTag(tagName, null, null, EmptyToNull(rawContent.Trim()), rawContent);
            }

            var name = rest[..nameLength];
            rest = rest[nameLength..].TrimStart();
            if (rest.StartsWith('='))
            {
                rest = rest[1..].TrimStart();
            }

            return new PhpDocTag(tagName, EmptyToNull(rest), name, null, rawContent);
        }

        private static PhpDocTag ParseImportTypeTag(string tagName, string rawContent)
        {
            var rest = rawContent.TrimStart();
            var nameLength = 0;
            while (nameLength < rest.Length && IsTemplateNameChar(rest[nameLength]))
            {
                nameLength++;
            }

            if (nameLength == 0)
            {
                return new PhpDocTag(tagName, null, null, EmptyToNull(rawContent.Trim()), rawContent);
            }

            var importedName = rest[..nameLength];
            rest = rest[nameLength..].TrimStart();
            if (!StartsWithWord(rest, "from"))
            {
                return new PhpDocTag(tagName, null, importedName, EmptyToNull(rest), rawContent);
            }

            rest = rest["from".Length..].TrimStart();
            var classLength = 0;
            if (classLength < rest.Length && rest[classLength] == '\\')
            {
                classLength++;
            }

            while (classLength < rest.Length
                   && (IsTemplateNameChar(rest[classLength]) || rest[classLength] == '\\'))
            {
                classLength++;
            }

            if (classLength == 0)
            {
                return new PhpDocTag(tagName, null, importedName, EmptyToNull(rest), rawContent);
            }

            var fromClass = rest[..classLength];
            rest = rest[classLength..].TrimStart();
            var localName = importedName;
            if (StartsWithWord(rest, "as"))
            {
                rest = rest[2..].TrimStart();
                var aliasLength = 0;
                while (aliasLength < rest.Length && IsTemplateNameChar(rest[aliasLength]))
                {
                    aliasLength++;
                }

                if (aliasLength > 0)
                {
                    localName = rest[..aliasLength];
                }
            }

            return new PhpDocTag(tagName, fromClass, localName, importedName, rawContent);
        }

        private static PhpDocTag ParseMethodTag(string tagName, string rawContent)
        {
            var work = rawContent.TrimStart();
            if (StartsWithWord(work, "static"))
            {
                work = work["static".Length..].TrimStart();
            }

            string? returnType = null;
            string? methodName = null;
            string remainder = work;

            if (PhpDocTypeParser.TryConsume(work, out _, out var afterType))
            {
                var typeSlice = SliceType(work, afterType);
                var after = afterType.TrimStart();
                if (TryReadMethodNameAndParams(after, out methodName, out remainder))
                {
                    returnType = typeSlice;
                }
            }

            if (methodName is null && !TryReadMethodNameAndParams(work, out methodName, out remainder))
            {
                return new PhpDocTag(tagName, null, null, EmptyToNull(rawContent.Trim()), rawContent);
            }

            return new PhpDocTag(tagName, returnType, methodName, EmptyToNull(remainder.Trim()), rawContent);
        }

        private static bool TryReadMethodNameAndParams(string text, out string? methodName, out string remainder)
        {
            methodName = null;
            remainder = text;
            var i = 0;
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            var start = i;
            if (i < text.Length && text[i] == '\\')
            {
                return false;
            }

            while (i < text.Length && IsTemplateNameChar(text[i]))
            {
                i++;
            }

            if (i == start)
            {
                return false;
            }

            var name = text[start..i];
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            if (i >= text.Length || text[i] != '(')
            {
                return false;
            }

            if (!TrySkipBalanced(text, ref i, '(', ')'))
            {
                return false;
            }

            methodName = name;
            remainder = i < text.Length ? text[i..] : "";
            return true;
        }

        private static void SplitTypeNameDescription(
            string rawContent,
            bool nameOptional,
            out string? type,
            out string? name,
            out string? description)
        {
            type = null;
            name = null;
            description = null;

            var nameIndex = FindDollarNameIndex(rawContent);
            if (PhpDocTypeParser.TryConsume(rawContent, out _, out var remainder))
            {
                var consumedType = SliceType(rawContent, remainder);
                var afterTypeNameIndex = FindDollarNameIndex(remainder);

                // Only treat the "$name" as the tag's parameter name when it sits right
                // after the type (whitespace only in between). A "$" further into the text
                // (e.g. "int Price in $USD") is prose, not a name — otherwise everything
                // before it would be silently dropped from the description.
                if (afterTypeNameIndex >= 0 && IsWhitespaceOnly(remainder, 0, afterTypeNameIndex))
                {
                    type = consumedType;
                    ReadDollarName(remainder, afterTypeNameIndex, out name, out description);
                    return;
                }

                if (nameIndex < 0 || nameOptional || afterTypeNameIndex >= 0)
                {
                    type = consumedType;
                    description = EmptyToNull(remainder.Trim());
                    return;
                }
            }

            if (nameIndex >= 0)
            {
                var before = rawContent[..nameIndex].Trim();
                type = before.Length == 0 ? null : before;
                ReadDollarName(rawContent, nameIndex, out name, out description);
                return;
            }

            description = EmptyToNull(rawContent.Trim());
        }

        private static void ReadDollarName(string text, int dollarIndex, out string? name, out string? description)
        {
            var i = dollarIndex + 1;
            var start = i;
            while (i < text.Length && IsTemplateNameChar(text[i]))
            {
                i++;
            }

            name = i > start ? text[start..i] : null;
            description = EmptyToNull(text[i..].Trim());
        }

        private static bool IsWhitespaceOnly(string text, int start, int end)
        {
            for (var i = start; i < end; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static int FindDollarNameIndex(string text)
        {
            var depthAngle = 0;
            var depthParen = 0;
            var depthBrace = 0;
            var inSingle = false;
            var inDouble = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (inSingle)
                {
                    if (c == '\\' && i + 1 < text.Length)
                    {
                        i++;
                        continue;
                    }

                    if (c == '\'')
                    {
                        inSingle = false;
                    }

                    continue;
                }

                if (inDouble)
                {
                    if (c == '\\' && i + 1 < text.Length)
                    {
                        i++;
                        continue;
                    }

                    if (c == '"')
                    {
                        inDouble = false;
                    }

                    continue;
                }

                switch (c)
                {
                    case '\'':
                        inSingle = true;
                        break;
                    case '"':
                        inDouble = true;
                        break;
                    case '<':
                        depthAngle++;
                        break;
                    case '>':
                        if (depthAngle > 0)
                        {
                            depthAngle--;
                        }

                        break;
                    case '(':
                        depthParen++;
                        break;
                    case ')':
                        if (depthParen > 0)
                        {
                            depthParen--;
                        }

                        break;
                    case '{':
                        depthBrace++;
                        break;
                    case '}':
                        if (depthBrace > 0)
                        {
                            depthBrace--;
                        }

                        break;
                    case '$' when depthAngle == 0 && depthParen == 0 && depthBrace == 0:
                        if (IsThisAt(text, i))
                        {
                            i += 4;
                            break;
                        }

                        return i;
                }
            }

            return -1;
        }

        private static bool IsThisAt(string text, int dollarIndex)
        {
            if (dollarIndex + 5 > text.Length)
            {
                return false;
            }

            if (string.Compare(text, dollarIndex, "$this", 0, 5, StringComparison.Ordinal) != 0)
            {
                return false;
            }

            return dollarIndex + 5 >= text.Length || !IsTemplateNameChar(text[dollarIndex + 5]);
        }

        private static string SliceType(string original, string remainder)
        {
            var consumed = original.Length - remainder.Length;
            if (consumed <= 0)
            {
                return original.Trim();
            }

            return original[..consumed].Trim();
        }

        private static bool StartsWithWord(string text, string word)
        {
            if (text.Length < word.Length
                || string.Compare(text, 0, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                return false;
            }

            return text.Length == word.Length || !IsTemplateNameChar(text[word.Length]);
        }

        private static bool IsTemplateNameChar(char c)
            => char.IsAsciiLetterOrDigit(c) || c == '_';

        private static bool TrySkipBalanced(string text, ref int i, char open, char close)
        {
            if (i >= text.Length || text[i] != open)
            {
                return false;
            }

            var depth = 0;
            var inSingle = false;
            var inDouble = false;
            while (i < text.Length)
            {
                var c = text[i];
                if (inSingle)
                {
                    if (c == '\\' && i + 1 < text.Length)
                    {
                        i += 2;
                        continue;
                    }

                    if (c == '\'')
                    {
                        inSingle = false;
                    }

                    i++;
                    continue;
                }

                if (inDouble)
                {
                    if (c == '\\' && i + 1 < text.Length)
                    {
                        i += 2;
                        continue;
                    }

                    if (c == '"')
                    {
                        inDouble = false;
                    }

                    i++;
                    continue;
                }

                if (c == '\'')
                {
                    inSingle = true;
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inDouble = true;
                    i++;
                    continue;
                }

                if (c == open)
                {
                    depth++;
                }
                else if (c == close)
                {
                    depth--;
                    i++;
                    if (depth == 0)
                    {
                        return true;
                    }

                    continue;
                }

                i++;
            }

            return false;
        }

        private static string? EmptyToNull(string value)
            => value.Length == 0 ? null : value;

        private static List<PhpDocTag> SelectPreferredByName(List<PhpDocTag> tags, string[] namesInPriorityOrder)
        {
            var allowed = new HashSet<string>(namesInPriorityOrder, StringComparer.Ordinal);
            var rank = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < namesInPriorityOrder.Length; i++)
            {
                rank[namesInPriorityOrder[i]] = i;
            }

            var winners = new Dictionary<string, PhpDocTag>(StringComparer.Ordinal);
            var unnamed = new List<PhpDocTag>();
            foreach (var tag in tags)
            {
                if (!allowed.Contains(tag.TagName))
                {
                    continue;
                }

                if (tag.ParameterName is null)
                {
                    unnamed.Add(tag);
                    continue;
                }

                if (!winners.TryGetValue(tag.ParameterName, out var existing)
                    || rank[tag.TagName] < rank[existing.TagName])
                {
                    winners[tag.ParameterName] = tag;
                }
            }

            var result = new List<PhpDocTag>();
            var seen = new HashSet<PhpDocTag>();
            foreach (var tag in tags)
            {
                if (tag.ParameterName is not null
                    && winners.TryGetValue(tag.ParameterName, out var winner)
                    && ReferenceEquals(winner, tag)
                    && seen.Add(tag))
                {
                    result.Add(tag);
                }
            }

            foreach (var tag in unnamed)
            {
                result.Add(tag);
            }

            return result;
        }

        private static PhpDocTag? SelectPreferredSingle(List<PhpDocTag> tags, string[] namesInPriorityOrder)
        {
            PhpDocTag? best = null;
            var bestRank = int.MaxValue;
            var rank = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < namesInPriorityOrder.Length; i++)
            {
                rank[namesInPriorityOrder[i]] = i;
            }

            foreach (var tag in tags)
            {
                if (!rank.TryGetValue(tag.TagName, out var tagRank))
                {
                    continue;
                }

                if (tagRank < bestRank)
                {
                    best = tag;
                    bestRank = tagRank;
                }
            }

            return best;
        }
    }
}
