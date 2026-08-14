namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Per-property definite-initialization and control-flow type narrowing for
    /// <c>$this->prop</c> (Prop-init #7 + property null/instanceof narrowing).
    /// Seeded from a property initializer, a promoted constructor parameter, or a direct
    /// <c>$this->prop = …</c> assignment in the constructor / method body under analysis.
    /// </summary>
    public sealed class PropertyInitializationState
    {
        public bool IsDefinitelyInitialized { get; set; }

        /// <summary>
        /// Current control-flow narrowed type for <c>$this->prop</c>; null means use the
        /// property's declared type from the enclosing object symbol.
        /// </summary>
        public ICheckedType? NarrowedType { get; set; }

        public PropertyInitializationState Clone() =>
            new()
            {
                IsDefinitelyInitialized = IsDefinitelyInitialized,
                NarrowedType = NarrowedType,
            };

        public static PropertyInitializationState Merge(
            PropertyInitializationState left,
            PropertyInitializationState right) =>
            new()
            {
                IsDefinitelyInitialized =
                    left.IsDefinitelyInitialized && right.IsDefinitelyInitialized,
                // Divergent branches drop property narrowing (same as <see cref="VariableState"/>
                // merge): subsequent guards must re-narrow.
                NarrowedType = null,
            };
    }
}
