using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>Membership and subtyping checks for template-string patterns.</summary>
    internal static class TemplateStringMatcher
    {
        public static bool Matches(
            string literal,
            TemplateStringPattern pattern,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            TemplateStringMatchBudget budget)
        {
            if (pattern.Complexity > budget.MaxSteps)
            {
                budget.MarkExceeded();
                return false;
            }

            return MatchSegments(literal, 0, pattern.Segments, 0, symbolTree, globalScope, budget);
        }

        public static bool IsSubtypeOf(
            TemplateStringPattern child,
            TemplateStringPattern parent,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            TemplateStringMatchBudget budget,
            out bool exceededLimit)
        {
            exceededLimit = false;
            var complexity = child.Complexity + parent.Complexity;
            if (complexity > budget.MaxSteps)
            {
                exceededLimit = true;
                budget.MarkExceeded();
                return false;
            }

            if (!budget.TryConsumeStep())
            {
                exceededLimit = true;
                return false;
            }

            if (child.Segments.Count != parent.Segments.Count)
            {
                return TrySampleInclusion(child, parent, symbolTree, globalScope, budget, ref exceededLimit);
            }

            for (var i = 0; i < child.Segments.Count; i++)
            {
                if (child.Segments[i] is TemplateStringSegment.LiteralSegment childLit &&
                    parent.Segments[i] is TemplateStringSegment.LiteralSegment parentLit)
                {
                    if (childLit.Text != parentLit.Text)
                    {
                        return false;
                    }

                    continue;
                }

                if (child.Segments[i] is TemplateStringSegment.HoleSegment childHole &&
                    parent.Segments[i] is TemplateStringSegment.HoleSegment parentHole)
                {
                    if (!QuantifierWithin(childHole.Quantifier, parentHole.Quantifier))
                    {
                        return false;
                    }

                    if (!HoleTypeWithin(childHole.HoleType, parentHole.HoleType, symbolTree, globalScope, budget))
                    {
                        return false;
                    }

                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// True when every string the interpolation <paramref name="source"/> can produce is a member
        /// of <paramref name="target"/>. Source literal text must match target text exactly. A target
        /// hole that accepts any <c>string</c> absorbs any non-empty run of source text and holes,
        /// except a run made only of holes that include <c>''</c>; a narrower target hole takes one
        /// source hole of an assignable type or literal text it matches.
        /// </summary>
        public static bool InterpolationMatches(
            TemplateStringPattern source,
            TemplateStringPattern target,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            TemplateStringMatchBudget budget)
        {
            if (target.Complexity > budget.MaxSteps)
            {
                budget.MarkExceeded();
                return false;
            }

            var items = new List<InterpolationItem>();
            FlattenInterpolation(source.Segments, items, depth: 0);
            return new InterpolationMatch(items, target.Segments, symbolTree, globalScope, budget).Match(0, 0);
        }

        private readonly record struct InterpolationItem(char Char, ICheckedType? Hole);

        // A hole with a single string literal type is that text. A nested template whose segments
        // all occur exactly once is spliced in, so `"{$path}?q"` with `$path: "api/${string}"`
        // is matched as `api/` followed by a `string` hole.
        private static void FlattenInterpolation(
            IReadOnlyList<TemplateStringSegment> segments,
            List<InterpolationItem> items,
            int depth)
        {
            foreach (var segment in segments)
            {
                switch (segment)
                {
                    case TemplateStringSegment.LiteralSegment literal:
                        foreach (var ch in literal.Text)
                        {
                            items.Add(new InterpolationItem(ch, null));
                        }

                        break;
                    case TemplateStringSegment.HoleSegment { HoleType: LiteralCheckedType { Value: string text } } hole
                        when hole.Quantifier.Min == 1 && hole.Quantifier.Max == 1:
                        foreach (var ch in text)
                        {
                            items.Add(new InterpolationItem(ch, null));
                        }

                        break;
                    case TemplateStringSegment.HoleSegment { HoleType: TemplateStringCheckedType nested } hole
                        when depth < 8
                             && hole.Quantifier.Min == 1
                             && hole.Quantifier.Max == 1
                             && nested.Pattern.Segments.All(IsSingleOccurrence):
                        FlattenInterpolation(nested.Pattern.Segments, items, depth + 1);
                        break;
                    case TemplateStringSegment.HoleSegment hole:
                        items.Add(new InterpolationItem('\0', hole.HoleType));
                        break;
                }
            }
        }

        private static bool IsSingleOccurrence(TemplateStringSegment segment) =>
            segment is TemplateStringSegment.LiteralSegment
            || segment is TemplateStringSegment.HoleSegment { Quantifier: { Min: 1, Max: 1 } };

        private sealed class InterpolationMatch(
            List<InterpolationItem> items,
            IReadOnlyList<TemplateStringSegment> segments,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            TemplateStringMatchBudget budget)
        {
            public bool Match(int pos, int segmentIndex)
            {
                if (!budget.TryConsumeStep())
                {
                    return false;
                }

                if (segmentIndex >= segments.Count)
                {
                    return pos == items.Count;
                }

                switch (segments[segmentIndex])
                {
                    case TemplateStringSegment.LiteralSegment literal:
                        if (pos + literal.Text.Length > items.Count)
                        {
                            return false;
                        }

                        for (var i = 0; i < literal.Text.Length; i++)
                        {
                            if (items[pos + i].Hole is not null || items[pos + i].Char != literal.Text[i])
                            {
                                return false;
                            }
                        }

                        return Match(pos + literal.Text.Length, segmentIndex + 1);
                    case TemplateStringSegment.HoleSegment hole:
                        return MatchHole(pos, segmentIndex, hole, 0, AcceptsAnyString(hole.HoleType));
                    default:
                        return false;
                }
            }

            private bool MatchHole(
                int pos,
                int segmentIndex,
                TemplateStringSegment.HoleSegment hole,
                int consumed,
                bool acceptsAnyString)
            {
                if (!budget.TryConsumeStep())
                {
                    return false;
                }

                if (consumed >= hole.Quantifier.Min && Match(pos, segmentIndex + 1))
                {
                    return true;
                }

                if (hole.Quantifier.Max != int.MaxValue && consumed >= hole.Quantifier.Max)
                {
                    return false;
                }

                for (var end = items.Count; end > pos; end--)
                {
                    if (!budget.TryConsumeStep())
                    {
                        return false;
                    }

                    if (CanFill(pos, end, hole.HoleType, acceptsAnyString)
                        && MatchHole(end, segmentIndex, hole, consumed + 1, acceptsAnyString))
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool CanFill(int pos, int end, ICheckedType holeType, bool acceptsAnyString)
            {
                if (acceptsAnyString)
                {
                    return HasTextOrNonEmptyHole(pos, end);
                }

                if (end - pos == 1
                    && items[pos].Hole is { } sourceHole
                    && (TypeComparer.IsAssignableTo(sourceHole, holeType, symbolTree, globalScope)
                        || HoleTypeWithin(sourceHole, holeType, symbolTree, globalScope, budget)))
                {
                    return true;
                }

                if (holeType is TemplateStringCheckedType nestedTarget)
                {
                    return new InterpolationMatch(
                            items.GetRange(pos, end - pos),
                            nestedTarget.Pattern.Segments,
                            symbolTree,
                            globalScope,
                            budget)
                        .Match(0, 0);
                }

                if (end - pos == 1 && items[pos].Hole is not null)
                {
                    return false;
                }

                var text = new System.Text.StringBuilder(end - pos);
                for (var i = pos; i < end; i++)
                {
                    if (items[i].Hole is not null)
                    {
                        return false;
                    }

                    text.Append(items[i].Char);
                }

                return SubstringMatchesHoleType(text.ToString(), holeType, symbolTree, globalScope, budget);
            }

            private bool AcceptsAnyString(ICheckedType holeType) =>
                holeType is not UnresolvedCheckedType
                && TypeComparer.IsAssignableTo(CheckedTypes.String, holeType, symbolTree, globalScope);

            // A target hole covers at least one character. A source hole whose type includes `''`
            // (a possibly-null value) may contribute nothing, so a run of only such holes cannot fill it.
            private bool HasTextOrNonEmptyHole(int pos, int end)
            {
                for (var i = pos; i < end; i++)
                {
                    if (items[i].Hole is not { } hole || !IncludesEmptyText(hole))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool IncludesEmptyText(ICheckedType hole) =>
                hole is LiteralCheckedType { Value: "" }
                || (hole is UnionCheckedType union && union.Members.Any(IncludesEmptyText));
        }

        private static bool MatchSegments(
            string input,
            int pos,
            IReadOnlyList<TemplateStringSegment> segments,
            int segmentIndex,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            TemplateStringMatchBudget budget)
        {
            if (!budget.TryConsumeStep())
            {
                return false;
            }

            if (segmentIndex >= segments.Count)
            {
                return pos == input.Length;
            }

            return segments[segmentIndex] switch
            {
                TemplateStringSegment.LiteralSegment literal =>
                    input.AsSpan(pos).StartsWith(literal.Text, StringComparison.Ordinal) &&
                    MatchSegments(input, pos + literal.Text.Length, segments, segmentIndex + 1, symbolTree, globalScope, budget),
                TemplateStringSegment.HoleSegment hole =>
                    MatchHole(input, pos, segments, segmentIndex, hole, 0, symbolTree, globalScope, budget),
                _ => false,
            };
        }

        // Recursive backtracking match for a quantified hole. <paramref name="consumed"/> is the number of
        // repetitions of the hole already matched at <paramref name="pos"/>.
        private static bool MatchHole(
            string input,
            int pos,
            IReadOnlyList<TemplateStringSegment> segments,
            int segmentIndex,
            TemplateStringSegment.HoleSegment hole,
            int consumed,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            TemplateStringMatchBudget budget)
        {
            if (!budget.TryConsumeStep())
            {
                return false;
            }

            // Once the minimum repetition count is met we may stop here and match the remaining segments.
            if (consumed >= hole.Quantifier.Min &&
                MatchSegments(input, pos, segments, segmentIndex + 1, symbolTree, globalScope, budget))
            {
                return true;
            }

            // Cannot consume more than the maximum repetition count.
            if (hole.Quantifier.Max != int.MaxValue && consumed >= hole.Quantifier.Max)
            {
                return false;
            }

            // Try consuming one more (non-empty) instance of the hole type at every possible split length.
            for (var end = input.Length; end > pos; end--)
            {
                if (!budget.TryConsumeStep())
                {
                    return false;
                }

                if (!SubstringMatchesHoleType(input[pos..end], hole.HoleType, symbolTree, globalScope, budget))
                {
                    continue;
                }

                if (MatchHole(input, end, segments, segmentIndex, hole, consumed + 1, symbolTree, globalScope, budget))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SubstringMatchesHoleType(
            string slice,
            ICheckedType holeType,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            TemplateStringMatchBudget budget)
        {
            if (slice.Length == 0)
            {
                return holeType is UnionCheckedType union &&
                       union.Members.Any(m => m is LiteralCheckedType { Value: "" });
            }

            switch (holeType)
            {
                case TemplateStringCheckedType template:
                    return Matches(slice, template.Pattern, symbolTree, globalScope, budget);
                case UnionCheckedType union:
                    return union.Members.Any(
                        member => SubstringMatchesHoleType(slice, member, symbolTree, globalScope, budget));
                case LiteralCheckedType literal when TryFormatNumericLiteral(literal, out var numericText):
                    return slice == numericText;
            }

            if (Rules.CheckerHelpers.IsBuiltInName(holeType, "int"))
            {
                return TemplateStringNumericText.IsIntText(slice);
            }

            if (Rules.CheckerHelpers.IsBuiltInName(holeType, "float"))
            {
                return TemplateStringNumericText.IsFloatText(slice);
            }

            var literalType = new LiteralCheckedType(
                slice,
                new SimpleCheckedType(new Binder.Symbols.BuiltInTypeSymbol("string")));
            return TypeComparer.IsAssignableTo(literalType, holeType, symbolTree, globalScope);
        }

        /// <summary>The <c>(string)</c> text of an <c>int</c> or <c>float</c> literal type.</summary>
        internal static bool TryFormatNumericLiteral(LiteralCheckedType literal, out string text)
        {
            switch (literal.Value)
            {
                case long l:
                    text = TemplateStringNumericText.FormatInt(l);
                    return true;
                case int i:
                    text = TemplateStringNumericText.FormatInt(i);
                    return true;
                case decimal m:
                    text = TemplateStringNumericText.FormatFloat((double)m);
                    return true;
                case double d:
                    text = TemplateStringNumericText.FormatFloat(d);
                    return true;
                default:
                    text = "";
                    return false;
            }
        }

        /// <summary>
        /// True when every string a <paramref name="child"/> hole produces fits a
        /// <paramref name="parent"/> hole: by subtyping, or because the parent accepts the text of
        /// each child literal, or the decimal text of an <c>int</c> / <c>float</c>.
        /// </summary>
        private static bool HoleTypeWithin(
            ICheckedType child,
            ICheckedType parent,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            TemplateStringMatchBudget budget)
        {
            if (TypeComparer.IsSubtypeOf(child, parent, symbolTree, globalScope))
            {
                return true;
            }

            switch (child)
            {
                case UnionCheckedType union:
                    return union.Members.Count > 0
                        && union.Members.All(member => HoleTypeWithin(member, parent, symbolTree, globalScope, budget));
                case LiteralCheckedType { Value: string text }:
                    return SubstringMatchesHoleType(text, parent, symbolTree, globalScope, budget);
                case LiteralCheckedType literal when TryFormatNumericLiteral(literal, out var numericText):
                    return SubstringMatchesHoleType(numericText, parent, symbolTree, globalScope, budget);
            }

            var isInt = Rules.CheckerHelpers.IsBuiltInName(child, "int");
            if (!isInt && !Rules.CheckerHelpers.IsBuiltInName(child, "float"))
            {
                return false;
            }

            return (parent is not UnresolvedCheckedType
                    && TypeComparer.IsAssignableTo(CheckedTypes.String, parent, symbolTree, globalScope))
                || (isInt && Rules.CheckerHelpers.IsBuiltInName(parent, "float"));
        }

        private static bool QuantifierWithin(TemplateStringQuantifier child, TemplateStringQuantifier parent) =>
            child.Min >= parent.Min &&
            child.Max <= parent.Max;

        private static bool TrySampleInclusion(
            TemplateStringPattern child,
            TemplateStringPattern parent,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            TemplateStringMatchBudget budget,
            ref bool exceededLimit)
        {
            foreach (var sample in EnumerateSamples(child, symbolTree, globalScope, maxSamples: 12))
            {
                if (!Matches(sample, parent, symbolTree, globalScope, budget))
                {
                    if (budget.ExceededLimit)
                    {
                        exceededLimit = true;
                    }

                    return false;
                }

                if (budget.ExceededLimit)
                {
                    exceededLimit = true;
                    return false;
                }
            }

            return child.Segments.Count <= parent.Segments.Count;
        }

        private static IEnumerable<string> EnumerateSamples(
            TemplateStringPattern pattern,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            int maxSamples)
        {
            var results = new List<string>();
            BuildSamples(pattern.Segments, 0, new System.Text.StringBuilder(), results, symbolTree, globalScope, maxSamples);
            return results;
        }

        private static void BuildSamples(
            IReadOnlyList<TemplateStringSegment> segments,
            int index,
            System.Text.StringBuilder current,
            List<string> results,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            int maxSamples)
        {
            if (results.Count >= maxSamples)
            {
                return;
            }

            if (index >= segments.Count)
            {
                results.Add(current.ToString());
                return;
            }

            switch (segments[index])
            {
                case TemplateStringSegment.LiteralSegment literal:
                    current.Append(literal.Text);
                    BuildSamples(segments, index + 1, current, results, symbolTree, globalScope, maxSamples);
                    current.Length -= literal.Text.Length;
                    break;
                case TemplateStringSegment.HoleSegment hole:
                    foreach (var sample in SampleHole(hole.HoleType, symbolTree, globalScope).Take(3))
                    {
                        var reps = Math.Max(hole.Quantifier.Min, Math.Min(1, hole.Quantifier.Max));
                        for (var r = 0; r < reps; r++)
                        {
                            current.Append(sample);
                        }

                        BuildSamples(segments, index + 1, current, results, symbolTree, globalScope, maxSamples);
                        current.Length -= sample.Length * reps;
                    }

                    break;
            }
        }

        private static IEnumerable<string> SampleHole(
            ICheckedType holeType,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            switch (holeType)
            {
                case LiteralCheckedType { Value: string s }:
                    yield return s;
                    break;
                case LiteralCheckedType literal when TryFormatNumericLiteral(literal, out var numericText):
                    yield return numericText;
                    break;
                case UnionCheckedType union:
                    foreach (var member in union.Members)
                    {
                        foreach (var sample in SampleHole(member, symbolTree, globalScope))
                        {
                            yield return sample;
                        }
                    }

                    break;
                case TemplateStringCheckedType template:
                    foreach (var sample in EnumerateSamples(template.Pattern, symbolTree, globalScope, 2))
                    {
                        yield return sample;
                    }

                    break;
                default:
                    if (Rules.CheckerHelpers.IsBuiltInName(holeType, "string"))
                    {
                        yield return "x";
                    }
                    else if (Rules.CheckerHelpers.IsBuiltInName(holeType, "int"))
                    {
                        yield return "0";
                        yield return "-7";
                    }
                    else if (Rules.CheckerHelpers.IsBuiltInName(holeType, "float"))
                    {
                        yield return "1.5";
                    }

                    break;
            }
        }
    }
}
