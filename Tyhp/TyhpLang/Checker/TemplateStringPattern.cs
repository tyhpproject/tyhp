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
            public HoleSegment(ICheckedType holeType, TemplateStringQuantifier quantifier)
            {
                HoleType = holeType;
                Quantifier = quantifier;
            }

            public ICheckedType HoleType { get; }

            public TemplateStringQuantifier Quantifier { get; }
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
    }
}
