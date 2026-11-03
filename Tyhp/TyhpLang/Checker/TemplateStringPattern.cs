namespace Tyhp.TyhpLang.Checker
{
    public abstract class TemplateStringSegment
    {
        private TemplateStringSegment()
        {
        }

        internal sealed class LiteralSegment : TemplateStringSegment
        {
            public LiteralSegment(string text) => Text = text;

            public string Text { get; }
        }

        internal sealed class HoleSegment : TemplateStringSegment
        {
            public HoleSegment(ICheckedType holeType, TemplateStringQuantifier quantifier, string? display = null)
            {
                HoleType = holeType;
                Quantifier = quantifier;
                Display = display ?? holeType.DisplayName;
            }

            public ICheckedType HoleType { get; }

            public TemplateStringQuantifier Quantifier { get; }

            /// <summary>Hole text between <c>${</c> and <c>}</c> in <see cref="TemplateStringPattern.DisplayName"/>.</summary>
            public string Display { get; }
        }
    }

    /// <summary>Decoded template-string pattern used by the checker.</summary>
    public sealed class TemplateStringPattern
    {
        public TemplateStringPattern(
            IReadOnlyList<TemplateStringSegment> segments,
            string displayName)
        {
            Segments = segments;
            DisplayName = displayName;
        }

        public IReadOnlyList<TemplateStringSegment> Segments { get; }

        public string DisplayName { get; }

        public int Complexity =>
            Segments.Count + Segments.OfType<TemplateStringSegment.HoleSegment>().Sum(h =>
                h.Quantifier.Max == int.MaxValue ? 8 : h.Quantifier.Max);

        /// <summary>
        /// Inlines the segments of every template-typed hole that occurs exactly once, so
        /// <c>"${ApiMethod} ${ApiPath}"</c> matches like the pattern it spells out. The display
        /// is unchanged.
        /// </summary>
        internal TemplateStringPattern InlineTemplateHoles()
        {
            if (!Segments.Any(IsInlinableHole))
            {
                return this;
            }

            var segments = new List<TemplateStringSegment>(Segments.Count);
            Append(Segments, 0);
            return new TemplateStringPattern(segments, DisplayName);

            void Append(IReadOnlyList<TemplateStringSegment> source, int depth)
            {
                foreach (var segment in source)
                {
                    if (depth < MaxInlineDepth
                        && IsInlinableHole(segment)
                        && segment is TemplateStringSegment.HoleSegment { HoleType: TemplateStringCheckedType nested })
                    {
                        Append(nested.Pattern.Segments, depth + 1);
                    }
                    else if (segment is TemplateStringSegment.LiteralSegment literal
                             && segments.Count > 0
                             && segments[^1] is TemplateStringSegment.LiteralSegment previous)
                    {
                        segments[^1] = new TemplateStringSegment.LiteralSegment(previous.Text + literal.Text);
                    }
                    else
                    {
                        segments.Add(segment);
                    }
                }
            }
        }

        private const int MaxInlineDepth = 8;

        private static bool IsInlinableHole(TemplateStringSegment segment) =>
            segment is TemplateStringSegment.HoleSegment
            {
                HoleType: TemplateStringCheckedType,
                Quantifier: { Min: 1, Max: 1 },
            };

        /// <summary>
        /// Applies <paramref name="map"/> to every hole type. Returns this pattern when no hole
        /// changes; otherwise a new pattern whose display spells the changed holes.
        /// </summary>
        internal TemplateStringPattern MapHoleTypes(Func<ICheckedType, ICheckedType> map)
        {
            var changed = false;
            var segments = new List<TemplateStringSegment>(Segments.Count);
            foreach (var segment in Segments)
            {
                if (segment is TemplateStringSegment.HoleSegment hole)
                {
                    var mapped = map(hole.HoleType);
                    if (!ReferenceEquals(mapped, hole.HoleType))
                    {
                        changed = true;
                        segments.Add(new TemplateStringSegment.HoleSegment(mapped, hole.Quantifier));
                        continue;
                    }
                }

                segments.Add(segment);
            }

            if (!changed)
            {
                return this;
            }

            var display = new System.Text.StringBuilder("\"");
            foreach (var segment in segments)
            {
                switch (segment)
                {
                    case TemplateStringSegment.LiteralSegment literal:
                        display.Append(literal.Text);
                        break;
                    case TemplateStringSegment.HoleSegment hole:
                        display.Append("${").Append(hole.Display).Append('}').Append(hole.Quantifier.ToString());
                        break;
                }
            }

            display.Append('"');
            return new TemplateStringPattern(segments, display.ToString()).InlineTemplateHoles();
        }
    }
}
