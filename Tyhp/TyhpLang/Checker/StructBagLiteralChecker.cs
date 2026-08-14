using System.Globalization;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Checks an array literal against a <see cref="StructCheckedType"/> (excess keys, missing
    /// required keys, and per-key assignability). Named <c>['key' => value]</c> bags serve
    /// <c>__CallableParametersStruct</c>; positional / int-keyed lists serve
    /// <c>__CallableParametersTuple</c> (<c>0 as $_1</c> aliases). Optional fields (defaulted
    /// callable parameters) may be omitted; required fields must be present.
    /// </summary>
    internal static class StructBagLiteralChecker
    {
        /// <summary>
        /// Shape of an array literal used to pick <c>__CallableParametersStruct</c> vs
        /// <c>__CallableParametersTuple</c> overloads without emitting diagnostics.
        /// </summary>
        internal enum LiteralShape
        {
            NotALiteral,
            Empty,
            Positional,
            Named,
            Other,
        }

        /// <summary>
        /// Classifies an array literal as a named bag, a positional/int-keyed list, empty, or
        /// neither (spreads / dynamic keys). Used for tyhpdef overload selection.
        /// </summary>
        public static LiteralShape Classify(IBase2Ast? expression)
        {
            if (!TryGetNamedArrayPairs(expression, out var pairs))
            {
                return LiteralShape.NotALiteral;
            }

            if (pairs.Count == 0)
            {
                return LiteralShape.Empty;
            }

            if (TryReadPositionalKeys(pairs, out _))
            {
                return LiteralShape.Positional;
            }

            if (TryReadNamedKeys(pairs, out _))
            {
                return LiteralShape.Named;
            }

            return LiteralShape.Other;
        }

        /// <summary>
        /// Returns <see langword="true"/> when <paramref name="sourceExpression"/> is an array
        /// literal that was checked against <paramref name="target"/> (diagnostics may have been
        /// reported). Returns <see langword="false"/> when the expression is not a matching bag
        /// literal so the caller should use ordinary assignability.
        /// </summary>
        public static bool TryCheck(
            IBase2Ast? sourceExpression,
            ICheckedType target,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            // Only synthetic <see cref="StructCheckedType"/> shapes (callable bags, inline
            // struct types). Named struct *classes* stay on ordinary assignability so
            // <c>Point $p = ['x' => 1]</c> is not silently accepted as a property bag.
            if (!TryGetNamedArrayPairs(sourceExpression, out var pairs)
                || target is not StructCheckedType structType)
            {
                return false;
            }

            // Eligibility is decided for the whole literal before anything is reported. Bailing
            // out mid-loop would leave the earlier pairs' diagnostics standing while the caller
            // also falls back to ordinary assignability, reporting the same literal twice.
            if (structType.HasIntegerKeyAliases && TryReadPositionalKeys(pairs, out var positional))
            {
                CheckPositionalKeys(
                    positional, structType, sourceExpression!, state, context, diagnostics);
                return true;
            }

            if (!TryReadNamedKeys(pairs, out var keys))
            {
                return false;
            }

            CheckNamedKeys(keys, structType, sourceExpression!, state, context, diagnostics);
            return true;
        }

        private static void CheckNamedKeys(
            List<(PhpArrayPairAst Pair, string Key, bool WrittenAsQuoted)> keys,
            StructCheckedType structType,
            IBase2Ast reportNode,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var provided = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (pair, key, writtenAsQuoted) in keys)
            {
                var propertyKey = key.StartsWith('$') ? key : "$" + key;
                provided.Add(propertyKey);
                if (!structType.Properties.TryGetValue(propertyKey, out var property))
                {
                    // A quoted key occupies more source than the bare name it decodes to, so an
                    // edit span measured from the name would cut into the quotes. Those keys get
                    // the plain error; only bare keys carry a "did you mean" fix.
                    var candidates = writtenAsQuoted
                        ? []
                        : structType.Properties.Keys.Select(k => k.TrimStart('$')).ToArray();

                    CheckerHelpers.ReportErrorWithDidYouMean(
                        diagnostics,
                        state,
                        pair,
                        MessageCode.CheckerWithKeywordInvalidProperty,
                        key,
                        candidates,
                        key,
                        structType.DisplayName);
                    continue;
                }

                CheckPairValue(pair, property, state, context, diagnostics);
            }

            ReportMissingRequiredKeys(
                reportNode,
                structType,
                name => provided.Contains(name),
                state,
                diagnostics);
        }

        private static void CheckPositionalKeys(
            List<(PhpArrayPairAst Pair, int Index)> keys,
            StructCheckedType structType,
            IBase2Ast reportNode,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var providedIndexes = new HashSet<int>();
            var intCandidates = structType.Properties.Values
                .Where(p => p.IntegerKeyAlias is not null)
                .Select(p => p.IntegerKeyAlias!.Value.ToString(CultureInfo.InvariantCulture))
                .ToArray();

            foreach (var (pair, index) in keys)
            {
                providedIndexes.Add(index);
                if (!structType.TryGetPropertyByIntegerKey(index, out var property) || property is null)
                {
                    var keyText = index.ToString(CultureInfo.InvariantCulture);
                    CheckerHelpers.ReportErrorWithDidYouMean(
                        diagnostics,
                        state,
                        pair,
                        MessageCode.CheckerWithKeywordInvalidProperty,
                        keyText,
                        intCandidates,
                        keyText,
                        structType.DisplayName);
                    continue;
                }

                CheckPairValue(pair, property, state, context, diagnostics);
            }

            ReportMissingRequiredKeys(
                reportNode,
                structType,
                name => structType.Properties.TryGetValue(name, out var property)
                    && property.IntegerKeyAlias is int alias
                    && providedIndexes.Contains(alias),
                state,
                diagnostics);
        }

        /// <summary>
        /// Required (non-optional) struct fields must appear in the bag. Optional fields — defaulted
        /// callable parameters — may be omitted. One diagnostic per missing key; no subset-struct
        /// intersection is required.
        /// </summary>
        private static void ReportMissingRequiredKeys(
            IBase2Ast reportNode,
            StructCheckedType structType,
            Func<string, bool> isProvided,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            foreach (var (name, property) in structType.Properties)
            {
                if (property.IsOptional || isProvided(name))
                {
                    continue;
                }

                var displayKey = property.IntegerKeyAlias is int index
                    ? index.ToString(CultureInfo.InvariantCulture)
                    : name.TrimStart('$');
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    reportNode,
                    MessageCode.CheckerStructRequiredKeyMissing,
                    displayKey,
                    structType.DisplayName);
            }
        }

        private static void CheckPairValue(
            PhpArrayPairAst pair,
            StructPropertyInfo property,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (pair.ValueExpr is null)
            {
                return;
            }

            var valueType = context.ResolveExpressionType(pair.ValueExpr, state);
            if (!context.IsAssignable(valueType, property.Type, state))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    pair.ValueExpr,
                    MessageCode.CheckerTypeMismatch,
                    valueType.DisplayName,
                    property.Type.DisplayName);
            }
        }

        /// <summary>
        /// Reads every pair as a static string key. Returns <see langword="false"/> when any pair
        /// is a spread, positional, or dynamically keyed entry — such a literal is not a named bag
        /// and belongs to ordinary assignability as a whole.
        /// </summary>
        private static bool TryReadNamedKeys(
            IReadOnlyList<PhpArrayPairAst> pairs,
            out List<(PhpArrayPairAst Pair, string Key, bool WrittenAsQuoted)> keys)
        {
            keys = new List<(PhpArrayPairAst, string, bool)>(pairs.Count);
            foreach (var pair in pairs)
            {
                if (pair.IsExpansion)
                {
                    return false;
                }

                var key = ExtractNamedKey(pair.KeyExpr, out var writtenAsQuoted);
                if (key is null)
                {
                    return false;
                }

                keys.Add((pair, key, writtenAsQuoted));
            }

            return true;
        }

        /// <summary>
        /// Reads every pair as a PHP list / int-keyed entry (implicit index or integer key,
        /// including numeric strings). Spreads, named string keys, and dynamic keys fail.
        /// </summary>
        private static bool TryReadPositionalKeys(
            IReadOnlyList<PhpArrayPairAst> pairs,
            out List<(PhpArrayPairAst Pair, int Index)> keys)
        {
            keys = new List<(PhpArrayPairAst, int)>(pairs.Count);
            var nextIndex = 0;
            foreach (var pair in pairs)
            {
                if (pair.IsExpansion)
                {
                    return false;
                }

                int index;
                if (pair.KeyExpr is null)
                {
                    index = nextIndex;
                }
                else if (!TryGetIntegerKey(pair.KeyExpr, out index))
                {
                    return false;
                }

                keys.Add((pair, index));
                if (index >= nextIndex)
                {
                    nextIndex = index + 1;
                }
            }

            return true;
        }

        private static bool TryGetIntegerKey(IExpression keyExpr, out int key)
        {
            key = 0;
            if (keyExpr is PhpScalarAst scalar)
            {
                if ((scalar.ScalarType is PhpScalarType.Integer
                        or PhpScalarType.OctalNumber
                        or PhpScalarType.HexNumber
                        or PhpScalarType.BinaryNumber)
                    && scalar.ValueInt64 is long numeric
                    && numeric is >= int.MinValue and <= int.MaxValue)
                {
                    key = (int)numeric;
                    return true;
                }

                if (scalar.ScalarType is PhpScalarType.String)
                {
                    return TryGetCanonicalIntegerString(
                        Unquote(FirstNonEmpty(scalar.ValueString, scalar.Identifier)),
                        out key);
                }
            }

            if (keyExpr is PhpEncapsStringAst encaps)
            {
                return TryGetCanonicalIntegerString(
                    Unquote(FirstNonEmpty(encaps.ValueString, encaps.TokenValue?.ValueString)),
                    out key);
            }

            if (keyExpr is PhpEncapsListAst encapsList
                && PhpStringLiteralHelper.TryGetStaticLiteral(encapsList, out var literal))
            {
                return TryGetCanonicalIntegerString(FirstNonEmpty(literal), out key);
            }

            return false;
        }

        /// <summary>
        /// PHP folds a string array key to an int only when it is the canonical decimal spelling
        /// of that int: no whitespace, no <c>+</c>, no leading zeros, and <c>-0</c> excluded. So
        /// <c>'0'</c> is key <c>0</c> while <c>'00'</c> and <c>' 0'</c> stay string keys.
        /// </summary>
        private static bool TryGetCanonicalIntegerString(string? text, out int key)
        {
            key = 0;
            if (text is null
                || !int.TryParse(text, NumberStyles.None | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
            {
                return false;
            }

            if (!string.Equals(parsed.ToString(CultureInfo.InvariantCulture), text, StringComparison.Ordinal))
            {
                return false;
            }

            key = parsed;
            return true;
        }

        private static bool TryGetNamedArrayPairs(
            IBase2Ast? expression,
            out IReadOnlyList<PhpArrayPairAst> pairs)
        {
            pairs = [];
            var source = UnwrapReturnExpression(expression);
            List<PhpArrayPairAst>? list = source switch
            {
                PhpArrayAst array => array.ArrayPairs?.GetAllNotNull().ToList(),
                PhpArrayPairListAst pairList => pairList.GetAllNotNull().ToList(),
                _ => null,
            };

            if (list is null)
            {
                return false;
            }

            pairs = list;
            return true;
        }

        private static IBase2Ast? UnwrapReturnExpression(IBase2Ast? node) =>
            node switch
            {
                PhpJumpStatementAst { JumpType: PhpJumpType.Return, Expression: { } expr } => expr,
                _ => node,
            };

        /// <summary>
        /// String-named keys only. Integer keys belong to positional bags and are left for
        /// <see cref="TryReadPositionalKeys"/>.
        /// </summary>
        /// <param name="writtenAsQuoted">
        /// True when the key came from a string literal, meaning the name is shorter than the
        /// source it was written as.
        /// </param>
        private static string? ExtractNamedKey(IExpression? keyExpr, out bool writtenAsQuoted)
        {
            writtenAsQuoted = keyExpr is PhpScalarAst or PhpEncapsStringAst or PhpEncapsListAst;

            if (keyExpr is PhpScalarAst scalarType
                && scalarType.ScalarType is PhpScalarType.Integer
                    or PhpScalarType.Float
                    or PhpScalarType.OctalNumber
                    or PhpScalarType.HexNumber
                    or PhpScalarType.BinaryNumber)
            {
                return null;
            }

            return keyExpr switch
            {
                PhpNameAst name => FirstNonEmpty(name.ValueString, name.Identifier),
                TokenValueAst token => FirstNonEmpty(token.ValueString, token.Identifier),
                PhpScalarAst scalar => Unquote(FirstNonEmpty(scalar.ValueString, scalar.Identifier)),
                PhpEncapsStringAst encaps => Unquote(
                    FirstNonEmpty(encaps.ValueString, encaps.TokenValue?.ValueString)),
                PhpEncapsListAst encapsList =>
                    PhpStringLiteralHelper.TryGetStaticLiteral(encapsList, out var literal)
                        ? FirstNonEmpty(literal)
                        : null,
                PhpBuiltinTypeAst builtin => FirstNonEmpty(builtin.Identifier, builtin.ValueString),
                _ => null,
            };
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static string? Unquote(string? text)
        {
            if (text is null)
            {
                return null;
            }

            if (PhpStringLiteralHelper.TryDecodeQuotedTokenText(text, out var decoded))
            {
                return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
            }

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
    }
}
