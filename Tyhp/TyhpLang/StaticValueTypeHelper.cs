using System.Globalization;
using System.Text;

namespace Tyhp.TyhpLang
{
    /// <summary>
    /// Recognizes Tyhp static-value (literal) type spellings such as <c>'red'</c>, <c>42</c>,
    /// <c>3.14</c>, <c>0xFF</c>. Does not cover <c>true</c>/<c>false</c>/<c>null</c>, which are
    /// registered as builtin type symbols and preserved as PHP 8.0+ type hints.
    /// </summary>
    public static class StaticValueTypeHelper
    {
        /// <summary>
        /// Returns the PHP scalar type name a literal spelling should widen to
        /// (<c>string</c>, <c>int</c>, or <c>float</c>).
        /// </summary>
        public static bool TryGetUnderlyingBuiltinName(string? spelling, out string underlyingName)
        {
            if (TryParse(spelling, out _, out underlyingName))
            {
                return true;
            }

            underlyingName = string.Empty;
            return false;
        }

        /// <summary>
        /// Parses a builtin-type identifier that is actually a static-value literal spelling.
        /// <paramref name="value"/> is the decoded literal (string contents, long, or decimal).
        /// </summary>
        public static bool TryParse(string? spelling, out object? value, out string underlyingName)
        {
            value = null;
            underlyingName = string.Empty;
            if (string.IsNullOrEmpty(spelling))
            {
                return false;
            }

            if (TryDecodeQuotedString(spelling, out var stringValue))
            {
                value = stringValue;
                underlyingName = "string";
                return true;
            }

            if (TryParseFloat(spelling, out var floatValue))
            {
                value = floatValue;
                underlyingName = "float";
                return true;
            }

            if (TryParseInteger(spelling, out var intValue))
            {
                value = intValue;
                underlyingName = "int";
                return true;
            }

            return false;
        }

        private static bool TryDecodeQuotedString(string spelling, out string value)
        {
            value = string.Empty;

            if (spelling.StartsWith("b'", StringComparison.Ordinal) && spelling.EndsWith('\''))
            {
                value = UnescapeSingleQuoted(spelling[2..^1]);
                return true;
            }

            if (spelling.Length >= 2 && spelling[0] == '\'' && spelling[^1] == '\'')
            {
                value = UnescapeSingleQuoted(spelling[1..^1]);
                return true;
            }

            if (spelling.Length >= 2 && spelling[0] == '"' && spelling[^1] == '"')
            {
                value = UnescapeDoubleQuoted(spelling[1..^1]);
                return true;
            }

            return false;
        }

        private static bool TryParseFloat(string spelling, out decimal value)
        {
            value = 0;
            // Integer-looking spellings (including 0x/0b/0o) must not be claimed as float.
            if (spelling.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                || spelling.StartsWith("0b", StringComparison.OrdinalIgnoreCase)
                || spelling.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var looksLikeFloat = spelling.Contains('.')
                || spelling.Contains('e', StringComparison.OrdinalIgnoreCase);
            if (!looksLikeFloat)
            {
                return false;
            }

            return decimal.TryParse(
                RemoveDigitSeparators(spelling),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static bool TryParseInteger(string spelling, out long value)
        {
            value = 0;
            try
            {
                var unseparated = RemoveDigitSeparators(spelling);

                if (unseparated.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    value = Convert.ToInt64(unseparated[2..], 16);
                    return true;
                }

                if (unseparated.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
                {
                    value = Convert.ToInt64(unseparated[2..], 2);
                    return true;
                }

                if (unseparated.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
                {
                    value = Convert.ToInt64(unseparated[2..], 8);
                    return true;
                }

                // Note: unlike modern `0o17`, a legacy leading-zero spelling like `017` is
                // deliberately treated as plain decimal 17, matching how the rest of the
                // compiler lexes/evaluates such literals (T_LNUMBER -> PhpScalarType.Integer;
                // see PhpScalarAst.Create). Do not special-case it to octal here, or literal
                // type spellings would disagree with literal expression values.
                return long.TryParse(
                    unseparated,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value);
            }
            catch (FormatException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        /// <summary>
        /// PHP/Tyhp numeric literals allow <c>_</c> as a digit separator (e.g. <c>1_000_000</c>).
        /// The separator is not part of any numeric grammar this helper parses.
        /// </summary>
        private static string RemoveDigitSeparators(string spelling)
            => spelling.IndexOf('_') < 0 ? spelling : spelling.Replace("_", string.Empty);

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
