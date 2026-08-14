using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Domain.Diagnostics
{
    /// <summary>
    /// Story 14 Phase 5 consistency gate: MessageCode ↔ .resx bijection and mechanical
    /// short-message style rules from <c>CONVENTIONS.md</c> §2. Run from tests / CI so drift
    /// fails the build the same way the diagnostic-code single-source-of-truth rule does.
    /// </summary>
    public static class MessageConsistencyGate
    {
        /// <summary>
        /// Codes that are intentionally emitted at more than one severity and therefore may
        /// carry more than one of <c>ERROR_</c> / <c>WARNING_</c> / <c>INFO_TYHP####</c>.
        /// </summary>
        public static readonly IReadOnlySet<MessageCode> MultiSeverityAllowlist =
            new HashSet<MessageCode>
            {
                // AddError + one AddWarning path.
                MessageCode.BinderUnknownError,
                // Explicit-path miss → warning; empty project → info.
                MessageCode.LintNoSourceFiles,
            };

        private static readonly Regex DiagnosticKeyRegex = new(
            @"^(ERROR|WARNING|INFO)_TYHP(\d+)$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ExplainKeyRegex = new(
            @"^EXPLAIN_TYHP(\d+)$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex PascalCaseTokenRegex = new(
            @"[A-Z]+(?=[A-Z][a-z])|[A-Z]?[a-z]+|[A-Z]+|\d+",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ProseWordRegex = new(
            @"[A-Za-z][A-Za-z0-9]+",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Leading <see cref="MessageCode"/> name segments that name the compiler phase, not the
        /// diagnostic topic (stripped before EXPLAIN overlap is checked).
        /// </summary>
        private static readonly string[] EnumNamePhasePrefixes =
        [
            "Checker",
            "Binder",
            "Emitter",
            "Parser",
            "Visitor",
            "Lexer",
            "Config",
            "Build",
            "Lint",
            "Tyhpdef",
            "IntegrityCheck",
            "Integrity",
        ];

        /// <summary>
        /// Tokens too generic to prove an EXPLAIN body is about the right diagnostic.
        /// </summary>
        private static readonly HashSet<string> TopicStopwords = new(StringComparer.OrdinalIgnoreCase)
        {
            "Error", "Unknown", "Warning", "Info", "Invalid", "Allowed", "Here",
            "Used", "Usage", "Found", "With", "From", "That", "This", "Must",
            "Cannot", "Type", "Types", "Code", "File", "Path", "Name", "Value",
            "Missing", "Failed", "Generic",
        };

        private static readonly Regex PlaceholderRegex = new(
            @"\{(\d+)(?::[^}]*)?\}",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Prefixes that introduce a count or free-form detail placeholder (not an identifier),
        /// matching <c>CONVENTIONS.md</c> §2 ("Counts and free-form detail strings … stay unquoted").
        /// </summary>
        /// <remarks>
        /// Keep the alternatives disjoint. A trailing <c>:</c> or <c>—</c> already covers every
        /// "&lt;summary&gt;: &lt;raw detail&gt;" message (exception text, parser detail, signature
        /// digests), so a prefix that ends in one must not be listed again on its own.
        /// </remarks>
        private static readonly Regex FreeFormPlaceholderPrefix = new(
            @"(?:"
            // "<summary>: <raw detail>" and "<summary> — <raw detail>".
            + @":\s*$|"
            + @"—\s*$|"
            // Counts and positions.
            + @"at position\s*$|"
            + @"expects\s*$|"
            + @"found\s*$|"
            + @"at most\s*$|"
            + @"at least\s*$|"
            + @"complexity limit\s*\($|"
            + @"Unexpected error\s*\($|"
            // Literal words the message itself supplies ("always true" / "always false").
            + @"Condition is always\s*$|"
            // Attribute target descriptions ("a property", "an enum case").
            + @"cannot be applied to\s*$"
            + @")",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Runs every mechanical check against the on-disk culture-neutral and en-US resource files.
        /// </summary>
        /// <param name="neutralResxPath">Path to <c>CLI.TyhpHostedService.resx</c>.</param>
        /// <param name="enUsResxPath">Path to <c>CLI.TyhpHostedService.en-US.resx</c>.</param>
        /// <returns>Zero or more human-readable violation lines (empty = gate green).</returns>
        public static IReadOnlyList<string> CollectViolations(string neutralResxPath, string enUsResxPath)
        {
            var violations = new List<string>();

            if (!File.Exists(neutralResxPath))
            {
                violations.Add($"Missing culture-neutral resource file: {neutralResxPath}");
                return violations;
            }

            if (!File.Exists(enUsResxPath))
            {
                violations.Add($"Missing en-US resource file: {enUsResxPath}");
                return violations;
            }

            var neutral = LoadResxEntries(neutralResxPath);
            var enUs = LoadResxEntries(enUsResxPath);

            CollectCultureSyncViolations(neutral, enUs, violations);
            CollectCatalogBijectionViolations(neutral, violations);
            CollectMultiSeverityViolations(neutral, violations);
            CollectStyleViolations(neutral, violations);
            CollectExplainHelpViolations(neutral, violations);

            return violations;
        }

        private static void CollectCultureSyncViolations(
            IReadOnlyDictionary<string, string> neutral,
            IReadOnlyDictionary<string, string> enUs,
            List<string> violations)
        {
            foreach (var key in neutral.Keys.Except(enUs.Keys).Order(StringComparer.Ordinal))
            {
                violations.Add(
                    $"Key `{key}` exists in culture-neutral .resx but not in en-US. "
                    + "Add the same entry to both files.");
            }

            foreach (var key in enUs.Keys.Except(neutral.Keys).Order(StringComparer.Ordinal))
            {
                violations.Add(
                    $"Key `{key}` exists in en-US .resx but not in culture-neutral. "
                    + "Add the same entry to both files.");
            }

            foreach (var key in neutral.Keys.Intersect(enUs.Keys).Order(StringComparer.Ordinal))
            {
                if (!string.Equals(neutral[key], enUs[key], StringComparison.Ordinal))
                {
                    violations.Add(
                        $"Key `{key}` text differs between culture-neutral and en-US .resx. "
                        + "Keep English strings identical until additional locales exist.");
                }
            }
        }

        private static void CollectCatalogBijectionViolations(
            IReadOnlyDictionary<string, string> resx,
            List<string> violations)
        {
            var allocated = new Dictionary<int, MessageCode>();
            foreach (var code in Enum.GetValues<MessageCode>().Where(c => c != MessageCode.NoError))
            {
                // Two enum members sharing a number would silently collapse into one diagnostic;
                // report it rather than throwing out of ToDictionary.
                if (!allocated.TryAdd((int)code, code))
                {
                    violations.Add(
                        $"MessageCode.{code} reuses number {(int)code}, already taken by "
                        + $"MessageCode.{allocated[(int)code]}. Diagnostic codes are unique and are "
                        + "never renumbered (CONVENTIONS.md §1).");
                }
            }

            var codesWithText = new HashSet<int>();
            foreach (var key in resx.Keys)
            {
                var match = DiagnosticKeyRegex.Match(key);
                if (!match.Success)
                {
                    continue;
                }

                var numeric = int.Parse(match.Groups[2].Value, System.Globalization.NumberStyles.Integer);
                if (!allocated.ContainsKey(numeric))
                {
                    violations.Add(
                        $"`{key}` references MessageCode {numeric}, which is not defined in "
                        + "MessageCode.cs. Remove the orphan .resx entry or add the enum member "
                        + "(do not renumber existing codes).");
                    continue;
                }

                codesWithText.Add(numeric);
            }

            foreach (var (numeric, code) in allocated.OrderBy(kv => kv.Key))
            {
                if (!codesWithText.Contains(numeric))
                {
                    violations.Add(
                        $"MessageCode.{code} ({MessageCodeCatalog.FormatCode(code)}) has no "
                        + "ERROR_TYHP / WARNING_TYHP / INFO_TYHP entry in the .resx catalog. "
                        + "Add a short message for the severity producers actually emit.");
                }
            }
        }

        private static void CollectMultiSeverityViolations(
            IReadOnlyDictionary<string, string> resx,
            List<string> violations)
        {
            var byCode = new Dictionary<int, List<string>>();
            foreach (var key in resx.Keys)
            {
                var match = DiagnosticKeyRegex.Match(key);
                if (!match.Success)
                {
                    continue;
                }

                var numeric = int.Parse(match.Groups[2].Value, System.Globalization.NumberStyles.Integer);
                if (!byCode.TryGetValue(numeric, out var list))
                {
                    list = [];
                    byCode[numeric] = list;
                }

                list.Add(key);
            }

            foreach (var (numeric, keys) in byCode.OrderBy(kv => kv.Key))
            {
                if (keys.Count <= 1)
                {
                    continue;
                }

                if (!Enum.IsDefined(typeof(MessageCode), numeric))
                {
                    continue;
                }

                var code = (MessageCode)numeric;
                if (MultiSeverityAllowlist.Contains(code))
                {
                    continue;
                }

                violations.Add(
                    $"{MessageCodeCatalog.FormatCode(code)} ({code}) carries multiple severity "
                    + $"catalog entries ({string.Join(", ", keys.Order(StringComparer.Ordinal))}), "
                    + "but it is not on the multi-severity allowlist. Keep only the severity "
                    + "producers emit (allowlist today: BinderUnknownError / LintNoSourceFiles), "
                    + "or extend MessageConsistencyGate.MultiSeverityAllowlist if both are real.");
            }
        }

        private static void CollectStyleViolations(
            IReadOnlyDictionary<string, string> resx,
            List<string> violations)
        {
            foreach (var (key, raw) in resx.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (!DiagnosticKeyRegex.IsMatch(key))
                {
                    continue;
                }

                var message = DecodeXmlText(raw);
                if (string.IsNullOrWhiteSpace(message))
                {
                    violations.Add(
                        $"`{key}` has an empty short message. Every diagnostic needs a non-empty "
                        + "short message (CONVENTIONS.md §2).");
                    continue;
                }

                if (HasForbiddenTrailingPeriod(message))
                {
                    violations.Add(
                        $"`{key}` ends with a period. Short messages must not have a trailing "
                        + "period (ellipsis `...` is fine). Message: \"" + message + "\"");
                }

                if (UsesQuotedPlaceholder(message))
                {
                    violations.Add(
                        $"`{key}` wraps an interpolated placeholder in '…' or \"…\". Use backticks "
                        + "around identifiers/types (`{0}`), not quotes. Message: \"" + message + "\"");
                }

                foreach (Match placeholder in PlaceholderRegex.Matches(message))
                {
                    if (IsInsideBackticks(message, placeholder.Index))
                    {
                        continue;
                    }

                    var before = message[..placeholder.Index];
                    var prefixWindow = before.Length > 80 ? before[^80..] : before;
                    if (FreeFormPlaceholderPrefix.IsMatch(prefixWindow))
                    {
                        continue;
                    }

                    violations.Add(
                        $"`{key}` interpolates `{placeholder.Value}` without backticks. Wrap "
                        + "offending symbols/types/paths/keywords in backticks (`{n}` or "
                        + "`${n}` for variables). Counts and free-form detail strings may stay "
                        + "unquoted (CONVENTIONS.md §2). Message: \"" + message + "\"");
                }
            }
        }

        private static void CollectExplainHelpViolations(
            IReadOnlyDictionary<string, string> resx,
            List<string> violations)
        {
            foreach (var (key, raw) in resx.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var match = ExplainKeyRegex.Match(key);
                if (!match.Success)
                {
                    continue;
                }

                var body = DecodeXmlText(raw);
                if (string.IsNullOrWhiteSpace(body))
                {
                    violations.Add(
                        $"`{key}` is present but empty. Authored long-form help "
                        + "(EXPLAIN_TYHP####) must be non-empty prose, or remove the key so "
                        + "`tyhp --explain` uses the stub.");
                    continue;
                }

                var numeric = int.Parse(match.Groups[1].Value, System.Globalization.NumberStyles.Integer);
                if (!Enum.IsDefined(typeof(MessageCode), numeric) || numeric == 0)
                {
                    violations.Add(
                        $"`{key}` references MessageCode {numeric}, which is not defined in "
                        + "MessageCode.cs. Remove the orphan EXPLAIN entry.");
                    continue;
                }

                CollectExplainTopicViolation(key, body, (MessageCode)numeric, resx, violations);
            }
        }

        /// <summary>
        /// Ensures an authored <c>EXPLAIN_TYHP####</c> body is about that code — not leftover prose
        /// recovered under a different number (e.g. internal-member examples under composite-type
        /// codes after a numbering shift).
        /// </summary>
        /// <remarks>
        /// Compares distinctive tokens from the <see cref="MessageCode"/> name (phase prefix
        /// stripped) against non-fenced EXPLAIN prose. Catch-all <c>*UnknownError</c> codes pass
        /// when the body says "catch-all" or "unexpected". Codes whose names yield no distinctive
        /// tokens fall back to the short-message catalog.
        /// </remarks>
        private static void CollectExplainTopicViolation(
            string key,
            string body,
            MessageCode code,
            IReadOnlyDictionary<string, string> resx,
            List<string> violations)
        {
            var enumName = code.ToString();
            var prose = ExtractExplainProse(body);

            if (enumName.EndsWith("UnknownError", StringComparison.Ordinal)
                && (prose.Contains("catch-all", StringComparison.OrdinalIgnoreCase)
                    || prose.Contains("unexpected", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var topics = TopicTokensFromEnumName(enumName);
            if (topics.Count == 0)
            {
                topics = TopicTokensFromShortMessage(resx, (int)code);
            }

            if (topics.Count == 0)
            {
                return;
            }

            var proseWords = ProseWordRegex.Matches(prose)
                .Select(m => m.Value)
                .ToList();

            if (topics.Any(token => TopicTokenAppearsInProse(token, proseWords)))
            {
                return;
            }

            violations.Add(
                $"`{key}` does not mention its diagnostic topic (MessageCode.{enumName}: "
                + string.Join(", ", topics)
                + "). Long-form help must describe this code, not leftover prose recovered under "
                + "a different number. Rewrite the body to match the short message / enum name, "
                + "or remove the EXPLAIN key so `tyhp --explain` uses the stub.");
        }

        /// <summary>
        /// EXPLAIN prose with fenced examples stripped so a `: void` in an unrelated snippet
        /// cannot satisfy a <c>void</c> topic token.
        /// </summary>
        private static string ExtractExplainProse(string body)
        {
            var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            var lines = normalized.Split('\n');
            var prose = new StringBuilder();
            var inFence = false;
            foreach (var line in lines)
            {
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }

                if (inFence)
                {
                    continue;
                }

                if (prose.Length > 0)
                {
                    prose.Append('\n');
                }

                prose.Append(line);
            }

            return prose.ToString();
        }

        private static List<string> TopicTokensFromEnumName(string enumName)
        {
            var rest = enumName;
            var stripped = true;
            while (stripped)
            {
                stripped = false;
                foreach (var prefix in EnumNamePhasePrefixes)
                {
                    if (rest.StartsWith(prefix, StringComparison.Ordinal) && rest.Length > prefix.Length)
                    {
                        rest = rest[prefix.Length..];
                        stripped = true;
                    }
                }
            }

            var tokens = new List<string>();
            foreach (Match match in PascalCaseTokenRegex.Matches(rest))
            {
                var token = match.Value;
                if (token.Length >= 4 && !TopicStopwords.Contains(token) && !token.All(char.IsDigit))
                {
                    tokens.Add(token);
                }
            }

            return tokens;
        }

        private static List<string> TopicTokensFromShortMessage(
            IReadOnlyDictionary<string, string> resx,
            int numeric)
        {
            string? shortMessage = null;
            foreach (var prefix in new[] { "ERROR", "WARNING", "INFO" })
            {
                if (resx.TryGetValue($"{prefix}_TYHP{numeric}", out var value)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    shortMessage = DecodeXmlText(value);
                    break;
                }
            }

            if (shortMessage is null)
            {
                return [];
            }

            var withoutPlaceholders = PlaceholderRegex.Replace(shortMessage, " ");
            var tokens = new List<string>();
            foreach (Match match in ProseWordRegex.Matches(withoutPlaceholders))
            {
                var token = match.Value;
                if (token.Length >= 4 && !TopicStopwords.Contains(token))
                {
                    tokens.Add(token);
                }
            }

            return tokens;
        }

        private static bool TopicTokenAppearsInProse(string token, IReadOnlyList<string> proseWords)
        {
            var normalizedToken = NormalizeTopicToken(token);
            foreach (var word in proseWords)
            {
                var normalizedWord = NormalizeTopicToken(word);
                if (string.Equals(normalizedToken, normalizedWord, StringComparison.Ordinal))
                {
                    return true;
                }

                if (normalizedToken.Length >= 4 && normalizedWord.Length >= 4
                    && (normalizedWord.StartsWith(normalizedToken, StringComparison.Ordinal)
                        || normalizedToken.StartsWith(normalizedWord, StringComparison.Ordinal)))
                {
                    return true;
                }

                // `write` in `written`, `expect` in `unexpected`, `pipe` in `pipes`.
                if (token.Length >= 4 && word.Length >= 4
                    && (word.Contains(token, StringComparison.OrdinalIgnoreCase)
                        || token.Contains(word, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Light stem so <c>write</c>/<c>written</c>, <c>maps</c>/<c>mapping</c>, and
        /// <c>assign</c>/<c>assignable</c> count as the same topic token.
        /// </summary>
        private static string NormalizeTopicToken(string word)
        {
            var w = word.ToLowerInvariant();
            if (w.Length >= 7 && w.EndsWith("ies", StringComparison.Ordinal))
            {
                return w[..^3] + "y";
            }

            if (w.Length >= 7 && w.EndsWith("ing", StringComparison.Ordinal))
            {
                var stem = w[..^3];
                if (stem.Length >= 2 && stem[^1] == stem[^2])
                {
                    stem = stem[..^1];
                }

                return stem;
            }

            foreach (var suffix in new[] { "able", "ions", "ion", "ers", "er", "ed" })
            {
                if (w.EndsWith(suffix, StringComparison.Ordinal) && w.Length - suffix.Length >= 4)
                {
                    return w[..^suffix.Length];
                }
            }

            // Prefer stripping a single `s` when `es` would leave a stem shorter than 4
            // (`pipes` → `pipe`, not `pip`).
            if (w.EndsWith("es", StringComparison.Ordinal) && w.Length - 2 >= 4)
            {
                return w[..^2];
            }

            if (w.EndsWith('s') && w.Length - 1 >= 3)
            {
                return w[..^1];
            }

            return w;
        }

        /// <summary>
        /// Loads every <c>&lt;data name&gt;</c> / <c>&lt;value&gt;</c> pair from a .resx file.
        /// </summary>
        public static IReadOnlyDictionary<string, string> LoadResxEntries(string path)
        {
            var document = XDocument.Load(path);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var data in document.Root!.Elements("data"))
            {
                var name = (string?)data.Attribute("name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var value = data.Element("value")?.Value ?? string.Empty;
                result[name] = value;
            }

            return result;
        }

        private static bool HasForbiddenTrailingPeriod(string message)
        {
            var trimmed = message.TrimEnd();
            if (trimmed.Length == 0 || trimmed[^1] != '.')
            {
                return false;
            }

            // Ellipsis used for truncation is explicitly allowed by the style guide.
            return !trimmed.EndsWith("...", StringComparison.Ordinal);
        }

        private static bool UsesQuotedPlaceholder(string message)
            => Regex.IsMatch(
                message,
                @"'[^']*\{\d+(?::[^}]*)?\}[^']*'|""[^""]*\{\d+(?::[^}]*)?\}[^""]*""",
                RegexOptions.CultureInvariant);

        private static bool IsInsideBackticks(string message, int index)
        {
            var inside = false;
            for (var i = 0; i < message.Length; i++)
            {
                if (i == index)
                {
                    return inside;
                }

                if (message[i] == '`')
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static string DecodeXmlText(string value)
            => value
                .Replace("&lt;", "<", StringComparison.Ordinal)
                .Replace("&gt;", ">", StringComparison.Ordinal)
                .Replace("&amp;", "&", StringComparison.Ordinal)
                .Replace("&quot;", "\"", StringComparison.Ordinal)
                .Replace("&apos;", "'", StringComparison.Ordinal);
    }
}
