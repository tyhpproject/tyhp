using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Walks a double-quoted <c>encapsList</c> in type position and builds a <see cref="TemplateStringPattern"/>.
    /// </summary>
    internal static class TemplateStringPatternReader
    {
        public static TemplateStringPattern? TryRead(
            PhpEncapsListAst encapsList,
            Func<IExpression, ICheckedType> resolveHoleType,
            IBase2Ast reportNode,
            string fileName,
            DiagnosticBag diagnostics)
        {
            var parts = encapsList.GetAllNotNull().ToList();
            var segments = new List<TemplateStringSegment>();
            var pendingLiteral = new System.Text.StringBuilder();
            var display = new System.Text.StringBuilder("\"");

            void FlushLiteral()
            {
                if (pendingLiteral.Length == 0)
                {
                    return;
                }

                segments.Add(new TemplateStringSegment.LiteralSegment(pendingLiteral.ToString()));
                display.Append(pendingLiteral);
                pendingLiteral.Clear();
            }

            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (IsLiteralPart(part))
                {
                    var raw = GetLiteralTokenText(part);
                    if (!TemplateStringEscapeDecoder.TryDecodeLiteralChunk(
                            raw, reportNode, fileName, diagnostics, out var decoded))
                    {
                        return null;
                    }

                    pendingLiteral.Append(decoded);
                    continue;
                }

                if (part is not IExpression holeExpr)
                {
                    continue;
                }

                FlushLiteral();
                var holeType = resolveHoleType(holeExpr);
                display.Append("${").Append(holeType.DisplayName).Append('}');

                var quantifier = TemplateStringQuantifier.ExactlyOnce;
                if (i + 1 < parts.Count && IsLiteralPart(parts[i + 1]))
                {
                    var nextRaw = GetLiteralTokenText(parts[i + 1]);
                    if (TryParseLeadingQuantifier(
                            nextRaw,
                            reportNode,
                            fileName,
                            diagnostics,
                            out quantifier,
                            out var consumed,
                            out _))
                    {
                        i++;
                        if (consumed < nextRaw!.Length)
                        {
                            var remainder = nextRaw[consumed..];
                            if (!TemplateStringEscapeDecoder.TryDecodeLiteralChunk(
                                    WrapAsEncapsToken(remainder),
                                    reportNode,
                                    fileName,
                                    diagnostics,
                                    out var remainderDecoded))
                            {
                                return null;
                            }

                            pendingLiteral.Append(remainderDecoded);
                        }

                        display.Append(quantifier.ToString());
                        segments.Add(new TemplateStringSegment.HoleSegment(holeType, quantifier));
                        continue;
                    }
                }

                segments.Add(new TemplateStringSegment.HoleSegment(holeType, quantifier));
            }

            FlushLiteral();
            display.Append('"');
            return new TemplateStringPattern(segments, display.ToString());
        }

        public static TemplateStringPattern CreateFromSegments(
            IReadOnlyList<TemplateStringSegment> segments,
            string displayName) =>
            new(segments, displayName);

        // A literal text chunk is produced by the visitor as a PhpEncapsStringAst (for both single-quoted
        // strings and T_ENCAPSED_AND_WHITESPACE runs inside a double-quoted string). A bare identifier hole
        // (e.g. ${Name}) is a PhpNameAst which — despite deriving from TokenValueAst — is an IExpression and
        // must be treated as a hole, not literal text.
        private static bool IsLiteralPart(IEncapsVarOrString part) =>
            part is PhpEncapsStringAst or (TokenValueAst and not PhpNameAst);

        private static string? GetLiteralTokenText(IEncapsVarOrString part) =>
            part switch
            {
                PhpEncapsStringAst encap => encap.ValueString,
                PhpNameAst => null,
                TokenValueAst token => token.ValueString,
                _ => null,
            };

        private static string WrapAsEncapsToken(string text) => $"\"{text}\"";

        private static bool TryParseLeadingQuantifier(
            string? rawTokenText,
            IBase2Ast reportNode,
            string fileName,
            DiagnosticBag diagnostics,
            out TemplateStringQuantifier quantifier,
            out int consumed,
            out bool escaped)
        {
            quantifier = TemplateStringQuantifier.ExactlyOnce;
            consumed = 0;
            escaped = false;

            if (string.IsNullOrEmpty(rawTokenText))
            {
                return false;
            }

            if (!PhpStringLiteralHelper.TryDecodeQuotedTokenText(rawTokenText, out var inner))
            {
                inner = rawTokenText;
            }

            if (inner.Length == 0)
            {
                return false;
            }

            if (inner[0] == '\\' && inner.Length >= 2)
            {
                escaped = true;
                return false;
            }

            switch (inner[0])
            {
                case '+':
                    quantifier = TemplateStringQuantifier.OneOrMore;
                    consumed = CountRawPrefix(rawTokenText, 1);
                    return true;
                case '*':
                    quantifier = TemplateStringQuantifier.ZeroOrMore;
                    consumed = CountRawPrefix(rawTokenText, 1);
                    return true;
                case '?':
                    quantifier = TemplateStringQuantifier.Optional;
                    consumed = CountRawPrefix(rawTokenText, 1);
                    return true;
                case '{':
                {
                    var close = inner.IndexOf('}');
                    if (close <= 1)
                    {
                        return false;
                    }

                    var body = inner[1..close];
                    if (!TryParseBraceQuantifier(body, reportNode, fileName, diagnostics, out quantifier))
                    {
                        return false;
                    }

                    consumed = CountRawPrefix(rawTokenText, close + 1);
                    return true;
                }
                default:
                    return false;
            }
        }

        private static int CountRawPrefix(string rawTokenText, int decodedPrefixLength)
        {
            if (!PhpStringLiteralHelper.TryDecodeQuotedTokenText(rawTokenText, out var inner))
            {
                return Math.Min(decodedPrefixLength, rawTokenText.Length);
            }

            if (decodedPrefixLength >= inner.Length)
            {
                return rawTokenText.Length;
            }

            return rawTokenText.Length - (inner.Length - decodedPrefixLength);
        }

        private static bool TryParseBraceQuantifier(
            string body,
            IBase2Ast reportNode,
            string fileName,
            DiagnosticBag diagnostics,
            out TemplateStringQuantifier quantifier)
        {
            quantifier = TemplateStringQuantifier.ExactlyOnce;
            if (body.Contains(','))
            {
                var parts = body.Split(',', 2);
                var hasMin = parts[0].Length > 0;
                var hasMax = parts[1].Length > 0;
                if (!hasMin && !hasMax)
                {
                    return false;
                }

                var min = hasMin && int.TryParse(parts[0], out var parsedMin) ? parsedMin : 0;
                var max = hasMax && int.TryParse(parts[1], out var parsedMax) ? parsedMax : int.MaxValue;
                if (min < 0 || (hasMax && max < 0) || (hasMin && hasMax && min > max))
                {
                    diagnostics.AddErrorFromAst(
                        MessageCode.CheckerTemplateStringInvalidQuantifierRange,
                        reportNode,
                        fileName,
                        body);
                    return false;
                }

                quantifier = hasMax && max != int.MaxValue
                    ? TemplateStringQuantifier.Between(min, max)
                    : TemplateStringQuantifier.AtLeast(min);
                return true;
            }

            if (!int.TryParse(body, out var exact) || exact < 0)
            {
                return false;
            }

            quantifier = TemplateStringQuantifier.Exactly(exact);
            return true;
        }
    }
}
