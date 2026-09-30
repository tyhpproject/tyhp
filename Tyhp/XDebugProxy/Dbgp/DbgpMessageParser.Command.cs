using System.Text;

namespace Tyhp.XDebugProxy.Dbgp
{
    public static partial class DbgpMessageParser
    {
        private static string ReadArgumentValue(string text, ref int index)
        {
            if (index >= text.Length)
            {
                return string.Empty;
            }

            if (text[index] == '"')
            {
                return ReadQuotedValue(text, ref index);
            }

            int start = index;
            int valueEnd = text.Length;
            for (int i = index; i < text.Length; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    continue;
                }

                int next = i;
                SkipWhitespace(text, ref next);
                if (next >= text.Length || IsDataSeparator(text, next) || IsFlagStart(text, next))
                {
                    valueEnd = i;
                    break;
                }
            }

            index = valueEnd;
            return text[start..valueEnd];
        }

        private static string ReadQuotedValue(string text, ref int index)
        {
            index++;
            var builder = new StringBuilder();
            while (index < text.Length)
            {
                char current = text[index];
                if (current == '"')
                {
                    index++;
                    return builder.ToString();
                }

                if (current == '\\' && index + 1 < text.Length)
                {
                    builder.Append(text[index + 1]);
                    index += 2;
                    continue;
                }

                builder.Append(current);
                index++;
            }

            throw new DbgpProtocolException("DBGp command has an unterminated quoted argument.");
        }

        private static string EncodeArgumentValue(string value)
        {
            bool needsQuotes = value.Length == 0
                || value.Contains('"')
                || value.Contains('\\')
                || value.StartsWith('-')
                || ContainsWhitespace(value);

            if (!needsQuotes)
            {
                return value;
            }

            return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }

        private static bool ContainsWhitespace(string value)
        {
            foreach (char c in value)
            {
                if (char.IsWhiteSpace(c))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ReadUntilWhitespace(string text, ref int index)
        {
            int start = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            return text[start..index];
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }

        private static bool IsDataSeparator(string text, int index)
        {
            if (index + 1 >= text.Length || text[index] != '-' || text[index + 1] != '-')
            {
                return false;
            }

            int after = index + 2;
            return after >= text.Length || char.IsWhiteSpace(text[after]);
        }

        private static bool IsFlagStart(string text, int index)
        {
            return index + 1 < text.Length
                && text[index] == '-'
                && IsFlagNameChar(text[index + 1]);
        }

        private static bool IsFlagNameChar(char c) => char.IsLetterOrDigit(c);

        private static byte[] DecodeBase64Payload(string base64)
        {
            if (base64.Length == 0)
            {
                return [];
            }

            try
            {
                return Convert.FromBase64String(base64);
            }
            catch (FormatException ex)
            {
                throw new DbgpProtocolException("DBGp command data after '--' is not valid base64.", ex);
            }
        }

        private static byte[] StripTrailingNull(byte[] rawBytes)
        {
            int length = rawBytes.Length;
            while (length > 0 && rawBytes[length - 1] == DbgpConstants.NullByte)
            {
                length--;
            }

            if (length == rawBytes.Length)
            {
                return rawBytes;
            }

            var stripped = new byte[length];
            Buffer.BlockCopy(rawBytes, 0, stripped, 0, length);
            return stripped;
        }
    }
}
