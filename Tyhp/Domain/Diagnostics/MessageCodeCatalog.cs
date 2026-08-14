using System.Collections.ObjectModel;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Domain.Diagnostics
{
    /// <summary>
    /// Dynamic catalog of every <see cref="MessageCode"/> value (except <see cref="MessageCode.NoError"/>),
    /// resolved against the localized short-message resources. Never hardcodes the code list.
    /// </summary>
    public static class MessageCodeCatalog
    {
        private static readonly Lock CacheLock = new();
        private static IReadOnlyList<MessageCodeEntry>? _entries;
        private static IReadOnlyDictionary<int, MessageCodeEntry>? _byNumeric;
        private static int _cachedLocalizerVersion = -1;

        /// <summary>
        /// Every allocated diagnostic code, ordered by numeric value, read from
        /// <see cref="MessageCode"/> via reflection.
        /// </summary>
        public static IReadOnlyList<MessageCodeEntry> All => GetEntries();

        /// <summary>
        /// Builds the catalog on first use and rebuilds it whenever the localizer changes, so the
        /// short messages can never be frozen at whatever text happened to resolve first.
        /// </summary>
        private static IReadOnlyList<MessageCodeEntry> GetEntries()
        {
            lock (CacheLock)
            {
                if (_entries == null || _cachedLocalizerVersion != CLI.Message.LocalizerVersion)
                {
                    _cachedLocalizerVersion = CLI.Message.LocalizerVersion;
                    _entries = BuildEntries();
                    _byNumeric = new ReadOnlyDictionary<int, MessageCodeEntry>(
                        _entries.ToDictionary(e => e.NumericCode));
                }

                return _entries;
            }
        }

        private static IReadOnlyDictionary<int, MessageCodeEntry> GetByNumeric()
        {
            GetEntries();
            lock (CacheLock)
            {
                return _byNumeric!;
            }
        }

        /// <summary>
        /// Looks up a catalog entry by enum value.
        /// </summary>
        public static bool TryGet(MessageCode code, out MessageCodeEntry entry)
        {
            if (code == MessageCode.NoError)
            {
                entry = default!;
                return false;
            }

            return GetByNumeric().TryGetValue((int)code, out entry!);
        }

        /// <summary>
        /// Parses a user-facing code token such as <c>TYHP4008</c>, <c>tyhp4008</c>, or <c>4008</c>.
        /// </summary>
        public static bool TryParseToken(string? token, out MessageCode code)
        {
            code = MessageCode.NoError;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            var text = token.Trim();
            if (text.StartsWith("TYHP", StringComparison.OrdinalIgnoreCase))
            {
                text = text[4..];
            }

            if (!int.TryParse(text, System.Globalization.NumberStyles.Integer, null, out var numeric)
                || numeric <= 0
                || !Enum.IsDefined(typeof(MessageCode), numeric))
            {
                return false;
            }

            code = (MessageCode)numeric;
            return code != MessageCode.NoError;
        }

        /// <summary>
        /// Formats a code as the stable <c>TYHP####</c> identifier (no zero-padding beyond the
        /// natural decimal spelling — matches compiler diagnostic output).
        /// </summary>
        public static string FormatCode(MessageCode code)
            => "TYHP" + ((int)code).ToString();

        /// <summary>
        /// Returns the component category for a code based on the numbering scheme in
        /// <see cref="MessageCode"/>.
        /// </summary>
        public static MessageCodeCategory GetCategory(MessageCode code)
        {
            var n = (int)code;
            return n switch
            {
                >= 1000 and <= 1999 => MessageCodeCategory.Parser,
                >= 2000 and <= 2999 => MessageCodeCategory.Visitor,
                >= 3000 and <= 3999 => MessageCodeCategory.Binder,
                >= 4000 and <= 4999 => MessageCodeCategory.Checker,
                >= 5000 and <= 5999 => MessageCodeCategory.Emitter,
                >= 6000 and <= 6999 => MessageCodeCategory.Configuration,
                >= 7000 and <= 7999 => MessageCodeCategory.Cli,
                >= 8000 and <= 8999 => MessageCodeCategory.Tyhpdef,
                >= 9000 and <= 9999 => MessageCodeCategory.Internal,
                _ => MessageCodeCategory.Other,
            };
        }

        /// <summary>
        /// Resolves the long-form explanation for a catalog entry: prefer an optional
        /// <c>EXPLAIN_TYHP{code}</c> resource, otherwise a localized stub template.
        /// </summary>
        public static string ResolveLongForm(MessageCodeEntry entry)
            => TryGetAuthoredLongForm(entry, out var authored)
                ? authored
                : CLI.Message.Localize(
                    "CLI_ExplainStubBody",
                    LocalizeCategory(entry.Category),
                    FormatCode(entry.Code));

        /// <summary>
        /// Returns the hand-authored <c>EXPLAIN_TYHP{code}</c> body when one exists.
        /// </summary>
        /// <remarks>
        /// The docs index only prints a long-form body for codes that have one, so it needs to tell
        /// authored prose from the generated stub that <see cref="ResolveLongForm"/> falls back to.
        /// </remarks>
        public static bool TryGetAuthoredLongForm(MessageCodeEntry entry, out string body)
        {
            var explainKey = "EXPLAIN_TYHP" + entry.NumericCode.ToString();
            body = CLI.Message.LocalizeRaw(explainKey);
            if (string.Equals(body, explainKey, StringComparison.Ordinal))
            {
                body = string.Empty;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Splits an authored long-form body into prose paragraphs and fenced code blocks.
        /// </summary>
        /// <remarks>
        /// Prose paragraphs collapse soft line wraps to a single line. Fenced <c>```</c> blocks keep
        /// their internal newlines so docs and <c>tyhp --explain</c> can render examples faithfully.
        /// </remarks>
        public static IEnumerable<LongFormBlock> SplitLongFormBlocks(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            var lines = normalized.Split('\n');
            var blocks = new List<LongFormBlock>();
            var prose = new List<string>();

            void FlushProse()
            {
                if (prose.Count == 0)
                {
                    return;
                }

                var paragraph = string.Join(' ', prose.Select(l => l.Trim()).Where(l => l.Length > 0));
                prose.Clear();
                if (paragraph.Length > 0)
                {
                    blocks.Add(new LongFormBlock(paragraph, isCodeFence: false));
                }
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    FlushProse();
                    var fence = new List<string> { line };
                    i++;
                    while (i < lines.Length)
                    {
                        fence.Add(lines[i]);
                        if (lines[i].StartsWith("```", StringComparison.Ordinal))
                        {
                            break;
                        }

                        i++;
                    }

                    blocks.Add(new LongFormBlock(string.Join('\n', fence), isCodeFence: true));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushProse();
                    continue;
                }

                prose.Add(line);
            }

            FlushProse();
            return blocks;
        }

        /// <summary>
        /// Localized display name for a category (via <c>CLI_ExplainCategory*</c> keys).
        /// </summary>
        public static string LocalizeCategory(MessageCodeCategory category)
            => CLI.Message.Localize(CategoryResourceKey(category));

        public static string CategoryResourceKey(MessageCodeCategory category)
            => category switch
            {
                MessageCodeCategory.Parser => "CLI_ExplainCategoryParser",
                MessageCodeCategory.Visitor => "CLI_ExplainCategoryVisitor",
                MessageCodeCategory.Binder => "CLI_ExplainCategoryBinder",
                MessageCodeCategory.Checker => "CLI_ExplainCategoryChecker",
                MessageCodeCategory.Emitter => "CLI_ExplainCategoryEmitter",
                MessageCodeCategory.Configuration => "CLI_ExplainCategoryConfiguration",
                MessageCodeCategory.Cli => "CLI_ExplainCategoryCli",
                MessageCodeCategory.Tyhpdef => "CLI_ExplainCategoryTyhpdef",
                MessageCodeCategory.Internal => "CLI_ExplainCategoryInternal",
                _ => "CLI_ExplainCategoryOther",
            };

        private static IReadOnlyList<MessageCodeEntry> BuildEntries()
        {
            var list = new List<MessageCodeEntry>();
            foreach (MessageCode code in Enum.GetValues<MessageCode>())
            {
                if (code == MessageCode.NoError)
                {
                    continue;
                }

                var category = GetCategory(code);
                list.Add(new MessageCodeEntry(
                    code,
                    code.ToString(),
                    (int)code,
                    ResolveVariants(code),
                    category,
                    CategoryResourceKey(category)));
            }

            list.Sort((a, b) => a.NumericCode.CompareTo(b.NumericCode));
            return list.AsReadOnly();
        }

        /// <summary>
        /// Collects every severity the message catalog carries text for. A code is emitted at more
        /// than one severity when the phase that raises it decides between them at runtime
        /// (<c>LintNoSourceFiles</c> is a warning for explicit paths and info for an empty project),
        /// so reporting only the first match would mislabel the code in the docs index and in
        /// <c>--explain</c>.
        /// </summary>
        private static IReadOnlyList<MessageCodeVariant> ResolveVariants(MessageCode code)
        {
            // Order matches how diagnostics are authored (errors dominate; warnings/info are explicit).
            (DiagnosticSeverity Severity, string Prefix)[] candidates =
            [
                (DiagnosticSeverity.Error, "ERROR_TYHP"),
                (DiagnosticSeverity.Warning, "WARNING_TYHP"),
                (DiagnosticSeverity.Info, "INFO_TYHP"),
            ];

            var variants = new List<MessageCodeVariant>(candidates.Length);
            foreach (var (severity, prefix) in candidates)
            {
                var key = prefix + ((int)code).ToString();
                var value = CLI.Message.LocalizeRaw(key);
                if (!string.Equals(value, key, StringComparison.Ordinal))
                {
                    variants.Add(new MessageCodeVariant(severity, key, value));
                }
            }

            if (variants.Count == 0)
            {
                // Missing .resx entry — still list the code (Phase 5 gate will fail the build).
                var fallbackKey = "ERROR_TYHP" + ((int)code).ToString();
                variants.Add(new MessageCodeVariant(DiagnosticSeverity.Error, fallbackKey, fallbackKey));
            }

            return variants.AsReadOnly();
        }
    }

    /// <summary>
    /// One segment of an authored <c>EXPLAIN_TYHP####</c> body: either prose or a fenced example.
    /// </summary>
    public readonly struct LongFormBlock
    {
        public LongFormBlock(string text, bool isCodeFence)
        {
            this.Text = text;
            this.IsCodeFence = isCodeFence;
        }

        public string Text { get; }
        public bool IsCodeFence { get; }
    }

    /// <summary>
    /// One allocated diagnostic code with its localized short message(s) and metadata.
    /// </summary>
    public sealed class MessageCodeEntry
    {
        public MessageCodeEntry(
            MessageCode code,
            string name,
            int numericCode,
            IReadOnlyList<MessageCodeVariant> variants,
            MessageCodeCategory category,
            string categoryResourceKey)
        {
            this.Code = code;
            this.Name = name;
            this.NumericCode = numericCode;
            this.Variants = variants;
            this.Category = category;
            this.CategoryResourceKey = categoryResourceKey;
        }

        public MessageCode Code { get; }
        public string Name { get; }
        public int NumericCode { get; }

        /// <summary>
        /// Every severity the code carries catalog text for, ordered error → warning → info.
        /// Always holds at least one element.
        /// </summary>
        public IReadOnlyList<MessageCodeVariant> Variants { get; }

        /// <summary>Highest severity the code can be reported at.</summary>
        public DiagnosticSeverity Severity => this.Variants[0].Severity;

        public string ResourceKey => this.Variants[0].ResourceKey;

        public string ShortMessage => this.Variants[0].ShortMessage;

        public MessageCodeCategory Category { get; }
        public string CategoryResourceKey { get; }
    }

    /// <summary>
    /// The catalog text a code carries for one severity.
    /// </summary>
    public sealed class MessageCodeVariant
    {
        public MessageCodeVariant(DiagnosticSeverity severity, string resourceKey, string shortMessage)
        {
            this.Severity = severity;
            this.ResourceKey = resourceKey;
            this.ShortMessage = shortMessage;
        }

        public DiagnosticSeverity Severity { get; }
        public string ResourceKey { get; }
        public string ShortMessage { get; }
    }

    /// <summary>
    /// Component band for a <see cref="MessageCode"/> (matches the numbering scheme).
    /// </summary>
    public enum MessageCodeCategory
    {
        Parser,
        Visitor,
        Binder,
        Checker,
        Emitter,
        Configuration,
        Cli,
        Tyhpdef,
        Internal,
        Other,
    }
}
