using System.Text;
using Tyhp.XDebugProxy.Dbgp;
using Tyhp.XDebugProxy.SourceMap;

namespace Tyhp.XDebugProxy.Translation
{
    /// <summary>
    /// Translates <c>eval</c> commands. Eval is never intercepted — XDebug must execute it.
    /// Identifiers in the <c>--</c> payload may be rewritten best-effort from sourcemap
    /// <c>names</c>. The payload is always preserved (null stays null; empty stays empty).
    /// </summary>
    /// <remarks>
    /// PLACEHOLDER_STORY_19: Add Tyhp expression compilation for eval
    /// </remarks>
    public sealed class EvalTranslator
    {
        private readonly SourceMapStore _store;
        private readonly PathMapper _pathMapper;
        private readonly Action<string>? _onWarning;

        internal EvalTranslator(
            SourceMapStore store,
            PathMapper pathMapper,
            Action<string>? onWarning)
        {
            this._store = store;
            this._pathMapper = pathMapper;
            this._onWarning = onWarning;
        }

        /// <summary>Eval is never answered by the proxy; always forward to XDebug.</summary>
        public DbgpResponse? TryIntercept(DbgpCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);
            return null;
        }

        /// <summary>
        /// Optionally map <c>-f</c>/<c>-n</c> Tyhp → PHP, then best-effort rewrite identifiers
        /// in <see cref="DbgpCommand.Data"/>. Never drops a present <c>--</c> payload.
        /// </summary>
        public void TranslateCommand(DbgpCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);

            try
            {
                this.TranslateFileContext(command);
                this.RewriteDataIdentifiers(command);
            }
            catch (Exception ex) when (ex is not ArgumentNullException and not ObjectDisposedException)
            {
                this.Warn($"Eval translation failed: {ex.Message}");
            }
        }

        private void TranslateFileContext(DbgpCommand command)
        {
            string? filename = command.Filename;
            if (string.IsNullOrWhiteSpace(filename)
                || this._pathMapper.IsDbgpUri(filename)
                || this._pathMapper.IsPhpFile(filename))
            {
                return;
            }

            string tyhpFs = this._pathMapper.ToFileSystemPath(filename);
            bool isKnownTyhp = this._pathMapper.IsTyhpFile(filename)
                || this._store.GetMapForTyhpFile(tyhpFs).Count > 0;
            if (!isKnownTyhp)
            {
                return;
            }

            int dbgpLine = 1;
            if (!string.IsNullOrWhiteSpace(command.LineNumber)
                && int.TryParse(command.LineNumber, out int parsed)
                && parsed >= 1)
            {
                dbgpLine = parsed;
            }

            if (this._pathMapper.TryMapTyhpToPhp(
                    this._store,
                    filename,
                    dbgpLine,
                    out string phpPathOrUri,
                    out int phpDbgpLine))
            {
                command.Filename = phpPathOrUri;
                if (!string.IsNullOrWhiteSpace(command.LineNumber))
                {
                    command.LineNumber = phpDbgpLine.ToString();
                }
            }
        }

        private void RewriteDataIdentifiers(DbgpCommand command)
        {
            byte[]? data = command.Data;
            if (data is null)
            {
                return;
            }

            if (data.Length == 0)
            {
                return;
            }

            string expression;
            try
            {
                expression = DbgpMessageParser.Utf8.GetString(data);
            }
            catch (DecoderFallbackException)
            {
                return;
            }

            IReadOnlyList<string> names = this.CollectNames(command);
            string rewritten = ExpressionIdentifierRewriter.Rewrite(expression, names);
            if (string.Equals(rewritten, expression, StringComparison.Ordinal))
            {
                return;
            }

            command.Data = DbgpMessageParser.Utf8.GetBytes(rewritten);
        }

        private IReadOnlyList<string> CollectNames(DbgpCommand command)
        {
            string? filename = command.Filename;
            if (string.IsNullOrWhiteSpace(filename) || this._pathMapper.IsDbgpUri(filename))
            {
                return [];
            }

            string fsPath = this._pathMapper.ToFileSystemPath(filename);
            SourceMapFile? phpMap = this._pathMapper.GetMapForPhp(this._store, filename);
            if (phpMap is not null)
            {
                return phpMap.Names;
            }

            IReadOnlyList<SourceMapFile> tyhpMaps = this._store.GetMapForTyhpFile(fsPath);
            if (tyhpMaps.Count == 1)
            {
                return tyhpMaps[0].Names;
            }

            if (tyhpMaps.Count > 1)
            {
                var names = new List<string>();
                foreach (SourceMapFile map in tyhpMaps)
                {
                    names.AddRange(map.Names);
                }

                return names;
            }

            return [];
        }

        private void Warn(string message)
        {
            try
            {
                this._onWarning?.Invoke(message);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Best-effort PHP-ish identifier rewrite using sourcemap <c>names</c>.
    /// Unknown compiled PHP names are left unchanged. Never throws.
    /// </summary>
    internal static class ExpressionIdentifierRewriter
    {
        public static string Rewrite(string expression, IReadOnlyList<string> names)
        {
            if (string.IsNullOrEmpty(expression) || names is null || names.Count == 0)
            {
                return expression;
            }

            try
            {
                Dictionary<string, string> lookup = BuildLookup(names);
                if (lookup.Count == 0)
                {
                    return expression;
                }

                var builder = new StringBuilder(expression.Length);
                int i = 0;
                while (i < expression.Length)
                {
                    char c = expression[i];

                    // Quoted string literals are copied verbatim (honoring backslash escapes)
                    // so identifier-looking substrings inside string content — e.g. a bare word
                    // that happens to match a mapped struct field name — are never rewritten.
                    if (c is '\'' or '"')
                    {
                        int stringStart = i;
                        i = SkipStringLiteral(expression, i);
                        builder.Append(expression, stringStart, i - stringStart);
                        continue;
                    }

                    // PHP comments are copied verbatim for the same reason.
                    if (c == '/' && i + 1 < expression.Length && expression[i + 1] == '/')
                    {
                        int commentStart = i;
                        i = SkipLineComment(expression, i);
                        builder.Append(expression, commentStart, i - commentStart);
                        continue;
                    }

                    if (c == '#' && (i + 1 >= expression.Length || expression[i + 1] != '['))
                    {
                        int commentStart = i;
                        i = SkipLineComment(expression, i);
                        builder.Append(expression, commentStart, i - commentStart);
                        continue;
                    }

                    if (c == '/' && i + 1 < expression.Length && expression[i + 1] == '*')
                    {
                        int commentStart = i;
                        i = SkipBlockComment(expression, i);
                        builder.Append(expression, commentStart, i - commentStart);
                        continue;
                    }

                    if (c == '$' && i + 1 < expression.Length && IsIdentStart(expression[i + 1]))
                    {
                        int start = i;
                        i += 2;
                        while (i < expression.Length && IsIdentPart(expression[i]))
                        {
                            i++;
                        }

                        string token = expression[start..i];
                        builder.Append(lookup.TryGetValue(token, out string? mapped) ? mapped : token);
                        continue;
                    }

                    if (IsIdentStart(c) && (i == 0 || !IsIdentPart(expression[i - 1])))
                    {
                        int start = i;
                        i++;
                        while (i < expression.Length && IsIdentPart(expression[i]))
                        {
                            i++;
                        }

                        string token = expression[start..i];
                        builder.Append(lookup.TryGetValue(token, out string? mapped) ? mapped : token);
                        continue;
                    }

                    builder.Append(c);
                    i++;
                }

                return builder.ToString();
            }
            catch
            {
                return expression;
            }
        }

        /// <summary>
        /// Map Tyhp identifiers from <c>names</c> onto themselves when the compiled PHP name is
        /// unknown (identity). If <c>names</c> contains a pair <c>tyhpName=phpName</c>, use that
        /// as an explicit Tyhp→PHP rewrite.
        /// </summary>
        private static Dictionary<string, string> BuildLookup(IReadOnlyList<string> names)
        {
            var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in names)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                int eq = raw.IndexOf('=');
                if (eq > 0 && eq < raw.Length - 1 && raw.IndexOf('=', eq + 1) < 0)
                {
                    string left = raw[..eq].Trim();
                    string right = raw[(eq + 1)..].Trim();
                    AddPair(lookup, left, right);
                    continue;
                }

                AddPair(lookup, raw, raw);
            }

            return lookup;
        }

        private static void AddPair(Dictionary<string, string> lookup, string from, string to)
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            {
                return;
            }

            lookup.TryAdd(from, to);
            if (from.StartsWith('$') != to.StartsWith('$'))
            {
                string fromBare = from.TrimStart('$');
                string toBare = to.TrimStart('$');
                lookup.TryAdd(fromBare, toBare);
                lookup.TryAdd("$" + fromBare, to.StartsWith('$') ? to : "$" + toBare);
            }
            else if (from.StartsWith('$'))
            {
                lookup.TryAdd(from[1..], to.StartsWith('$') ? to[1..] : to);
            }
            else
            {
                lookup.TryAdd("$" + from, to.StartsWith('$') ? to : "$" + to);
            }
        }

        private static bool IsIdentStart(char c) => char.IsAsciiLetter(c) || c == '_';

        private static bool IsIdentPart(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

        /// <summary>Advance past a <c>'...'</c> / <c>"..."</c> literal starting at <paramref name="start"/>, honoring <c>\</c> escapes.</summary>
        private static int SkipStringLiteral(string expression, int start)
        {
            char quote = expression[start];
            int i = start + 1;
            while (i < expression.Length && expression[i] != quote)
            {
                i += expression[i] == '\\' && i + 1 < expression.Length ? 2 : 1;
            }

            return i < expression.Length ? i + 1 : i;
        }

        /// <summary>Advance to (not past) the next newline, or end of string, for <c>//</c> / <c>#</c> line comments.</summary>
        private static int SkipLineComment(string expression, int start)
        {
            int i = start;
            while (i < expression.Length && expression[i] != '\n')
            {
                i++;
            }

            return i;
        }

        /// <summary>Advance past a <c>/* ... */</c> block comment starting at <paramref name="start"/>.</summary>
        private static int SkipBlockComment(string expression, int start)
        {
            int i = start + 2;
            while (i + 1 < expression.Length && !(expression[i] == '*' && expression[i + 1] == '/'))
            {
                i++;
            }

            return Math.Min(i + 2, expression.Length);
        }
    }
}
