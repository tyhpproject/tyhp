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

                    if (!TypeComparer.IsSubtypeOf(
                            childHole.HoleType,
                            parentHole.HoleType,
                            symbolTree,
                            globalScope))
                    {
                        return false;
                    }

                    continue;
                }

                return false;
            }

            return true;
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

            var literalType = new LiteralCheckedType(
                slice,
                new SimpleCheckedType(new Binder.Symbols.BuiltInTypeSymbol("string")));

            if (holeType is TemplateStringCheckedType template)
            {
                return Matches(slice, template.Pattern, symbolTree, globalScope, budget);
            }

            return TypeComparer.IsAssignableTo(literalType, holeType, symbolTree, globalScope);
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

                    break;
            }
        }
    }
}
