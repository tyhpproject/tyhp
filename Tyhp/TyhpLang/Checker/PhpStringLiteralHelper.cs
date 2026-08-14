using System.Text;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Decodes PHP string literal token text into runtime string values for compile-time checking.
    /// </summary>
    internal static class PhpStringLiteralHelper
    {
        public static bool TryGetStaticLiteral(PhpEncapsListAst encapsList, out string value)
        {
            value = string.Empty;
            var parts = encapsList.GetAllNotNull().ToList();
            if (parts.Count == 0)
            {
                return true;
            }

            if (!parts.All(static part => part is PhpEncapsStringAst))
            {
                return false;
            }

            var builder = new StringBuilder();
            foreach (PhpEncapsStringAst part in parts.Cast<PhpEncapsStringAst>())
            {
                if (!TryDecodeQuotedTokenText(part.ValueString, out var decoded))
                {
                    return false;
                }

                builder.Append(decoded);
            }

            value = builder.ToString();
            return true;
        }

        public static bool TryDecodeQuotedTokenText(string? tokenText, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrEmpty(tokenText))
            {
                return true;
            }

            if (tokenText.StartsWith("b'", StringComparison.Ordinal) && tokenText.EndsWith('\''))
            {
                value = UnescapeSingleQuoted(tokenText[2..^1]);
                return true;
            }

            if (tokenText.Length >= 2 && tokenText[0] == '\'' && tokenText[^1] == '\'')
            {
                value = UnescapeSingleQuoted(tokenText[1..^1]);
                return true;
            }

            if (tokenText.Length >= 2 && tokenText[0] == '"' && tokenText[^1] == '"')
            {
                value = UnescapeDoubleQuoted(tokenText[1..^1]);
                return true;
            }

            return false;
        }

        private static string UnescapeSingleQuoted(string inner)
        {
            var result = new StringBuilder(inner.Length);
            for (var i = 0; i < inner.Length; i++)
            {
                var ch = inner[i];
                if (ch == '\\' && i + 1 < inner.Length)
                {
                    var next = inner[++i];
                    result.Append(next is '\'' or '\\' ? next : $"\\{next}");
                }
                else
                {
                    result.Append(ch);
                }
            }

            return result.ToString();
        }

        private static string UnescapeDoubleQuoted(string inner)
        {
            var result = new StringBuilder(inner.Length);
            for (var i = 0; i < inner.Length; i++)
            {
                var ch = inner[i];
                if (ch != '\\' || i + 1 >= inner.Length)
                {
                    result.Append(ch);
                    continue;
                }

                var next = inner[++i];
                switch (next)
                {
                    case '"':
                    case '\\':
                    case '$':
                        result.Append(next);
                        break;
                    case 'n':
                        result.Append('\n');
                        break;
                    case 'r':
                        result.Append('\r');
                        break;
                    case 't':
                        result.Append('\t');
                        break;
                    default:
                        result.Append('\\').Append(next);
                        break;
                }
            }

            return result.ToString();
        }
    }
}
