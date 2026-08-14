namespace Tyhp.TyhpLang.Checker
{
    /// <summary>Repetition bounds for a template-string interpolation hole.</summary>
    internal readonly struct TemplateStringQuantifier : IEquatable<TemplateStringQuantifier>
    {
        public int Min { get; }
        public int Max { get; }

        /// <summary><c>int.MaxValue</c> denotes unbounded repetition.</summary>
        public TemplateStringQuantifier(int min, int max)
        {
            Min = min;
            Max = max;
        }

        public static TemplateStringQuantifier ExactlyOnce { get; } = new(1, 1);
        public static TemplateStringQuantifier Optional { get; } = new(0, 1);
        public static TemplateStringQuantifier OneOrMore { get; } = new(1, int.MaxValue);
        public static TemplateStringQuantifier ZeroOrMore { get; } = new(0, int.MaxValue);

        public static TemplateStringQuantifier Exactly(int count) => new(count, count);

        public static TemplateStringQuantifier AtLeast(int min) => new(min, int.MaxValue);

        public static TemplateStringQuantifier AtMost(int max) => new(0, max);

        public static TemplateStringQuantifier Between(int min, int max) => new(min, max);

        public bool IsUnbounded => Max == int.MaxValue;

        public bool Equals(TemplateStringQuantifier other) => Min == other.Min && Max == other.Max;

        public override bool Equals(object? obj) => obj is TemplateStringQuantifier other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Min, Max);

        public override string ToString() => Min == Max
            ? (Min == 1 ? string.Empty : $"{{{Min}}}")
            : Max == int.MaxValue
                ? (Min == 0 ? "*" : Min == 1 ? "+" : $"{{{Min},}}")
                : Min == 0
                    ? $"{{,{Max}}}"
                    : $"{{{Min},{Max}}}";
    }
}
