using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Decodes literal chunks in template-string patterns (Story 08.5 Phase 6 escaping rules).
    /// </summary>
    internal static class TemplateStringEscapeDecoder
    {
        public static bool TryDecodeLiteralChunk(
            string? rawTokenText,
            IBase2Ast reportNode,
            string fileName,
            DiagnosticBag diagnostics,
            out string decoded)
        {
            decoded = string.Empty;
            if (string.IsNullOrEmpty(rawTokenText))
            {
                return true;
            }

            if (!PhpStringLiteralHelper.TryDecodeQuotedTokenText(rawTokenText, out var inner))
            {
                inner = rawTokenText;
            }

            var builder = new System.Text.StringBuilder(inner.Length);
            for (var i = 0; i < inner.Length; i++)
            {
                var ch = inner[i];
                if (ch != '\\' || i + 1 >= inner.Length)
                {
                    builder.Append(ch);
                    continue;
                }

                var next = inner[++i];
                switch (next)
                {
                    case '$':
                    case '\\':
                    case '+':
                    case '*':
                    case '?':
                    case '{':
                    case '}':
                    case ',':
                        builder.Append(next);
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'e':
                        builder.Append('\u001B');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'v':
                        builder.Append('\v');
                        break;
                    case '"':
                        builder.Append('"');
                        break;
                    case '0':
                        builder.Append('\0');
                        break;
                    case 'x' when i + 2 < inner.Length && IsHex(inner[i + 1]) && IsHex(inner[i + 2]):
                        builder.Append((char)Convert.ToInt32(inner.Substring(i + 1, 2), 16));
                        i += 2;
                        break;
                    case 'u' when i + 1 < inner.Length && inner[i + 1] == '{':
                    {
                        var close = inner.IndexOf('}', i + 2);
                        if (close > i + 2 &&
                            int.TryParse(inner[(i + 2)..close], System.Globalization.NumberStyles.HexNumber, null, out var codePoint))
                        {
                            builder.Append(char.ConvertFromUtf32(codePoint));
                            i = close;
                            break;
                        }

                        ReportUnknown(reportNode, fileName, diagnostics, $"\\{next}");
                        return false;
                    }
                    default:
                        ReportUnknown(reportNode, fileName, diagnostics, $"\\{next}");
                        return false;
                }
            }

            decoded = builder.ToString();
            return true;
        }

        private static void ReportUnknown(IBase2Ast node, string fileName, DiagnosticBag diagnostics, string escape)
        {
            diagnostics.AddErrorFromAst(
                MessageCode.CheckerTemplateStringUnknownEscape,
                node,
                fileName,
                escape);
        }

        private static bool IsHex(char ch) =>
            ch is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }
}
