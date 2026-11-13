namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Per-property definite-initialization and control-flow type narrowing for
    /// <c>$this->prop</c> and enclosing-class <c>self::$prop</c> (Prop-init #7 +
    /// property null/instanceof narrowing).
    /// Seeded from a property initializer, a promoted constructor parameter, or a direct
    /// <c>$this->prop = …</c> / <c>self::$prop = …</c> assignment in the constructor / method body under analysis.
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
            PropertyInitializationState right)
        {
            ICheckedType? narrowed = null;
            if (left.NarrowedType is { } leftNarrowed && right.NarrowedType is { } rightNarrowed)
            {
                // Both paths refined the slot (null-check, assignment, instanceof, …).
                // Union them the way <see cref="VariableState"/> merge unions EffectiveType:
                // `if ($p === null) { $p = new T(); }` is non-null on the join (then assigned;
                // else already non-null). Divergent refinements (T vs null) union to a nullable.
                narrowed = CheckedTypes.UnionTypes(leftNarrowed, rightNarrowed);
            }

            return new PropertyInitializationState
            {
                IsDefinitelyInitialized =
                    left.IsDefinitelyInitialized && right.IsDefinitelyInitialized,
                NarrowedType = narrowed,
            };
        }
    }

    /// <summary>
    /// What a <c>return</c> inside a constructor leaves initialized. A <c>return</c> ends
    /// construction there, so the properties and the parent constructor call that matter are the
    /// ones on that path, not the ones at the end of the body.
    /// </summary>
    internal sealed class ConstructorExit
    {
        public ConstructorExit(HashSet<string> initializedProperties, bool parentConstructorInvoked)
        {
            InitializedProperties = initializedProperties;
            ParentConstructorInvoked = parentConstructorInvoked;
        }

        public HashSet<string> InitializedProperties { get; }

        public bool ParentConstructorInvoked { get; }
    }

    /// <summary>
    /// The <c>return</c> exits of one constructor body. <see cref="Owner"/> is that constructor's
    /// symbol; a <c>return</c> that belongs to a nested callable is not recorded.
    /// </summary>
    internal sealed class ConstructorExitCollector
    {
        public ConstructorExitCollector(Binder.Symbols.Interfaces.IBaseSymbol owner)
        {
            Owner = owner;
        }

        public Binder.Symbols.Interfaces.IBaseSymbol Owner { get; }

        public List<ConstructorExit> Exits { get; } = [];
    }
}
