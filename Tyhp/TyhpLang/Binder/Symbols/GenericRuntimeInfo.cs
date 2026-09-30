namespace Tyhp.TyhpLang.Binder.Symbols
{
    /// <summary>
    /// Bound <c>#[\Tyhp\GenericRuntime]</c> on a declaration. Presence of the attribute on a
    /// tyhpdef symbol means foreign consumer emit must use <c>\Tyhp\Generic::bind</c> rather
    /// than baking <see cref="Factory"/> / <see cref="Binder"/> names into PHP.
    /// <see cref="Layouts"/> is the dispatch-key list; an empty intersection with this
    /// compiler's supported layouts is an emit error.
    /// </summary>
    public sealed class GenericRuntimeInfo
    {
        /// <summary>True when no factory / binder helper was emitted.</summary>
        public bool Erased { get; init; }

        /// <summary>Mechanism D companion, e.g. <c>decode__tyhpGeneric</c>.</summary>
        public string? Binder { get; init; }

        /// <summary>Mechanism C class factory, e.g. <c>new_Ns_Box__tyhpGeneric</c>.</summary>
        public string? Factory { get; init; }

        /// <summary>
        /// Source type-alias <c>\Tyhp\Type</c> factory, e.g. <c>DecimalCoercible</c> or
        /// <c>UserService::NameType</c>.
        /// </summary>
        public string? AliasFactory { get; init; }

        /// <summary>
        /// Dispatch keys the declaration supports. Stamps write <c>layouts: [1]</c>.
        /// A written <c>layout: 1</c> is sugar for <c>[1]</c> when reading.
        /// </summary>
        public IReadOnlyList<int> Layouts { get; init; } = [GenericRuntimeAttributeSupport.CurrentLayout];

        /// <summary>Optional compiler version for diagnostics only (not dispatch).</summary>
        public string? Compiler { get; init; }

        public bool HasBinder => !string.IsNullOrWhiteSpace(this.Binder);

        public bool HasFactory => !string.IsNullOrWhiteSpace(this.Factory);

        public bool HasAliasFactory => !string.IsNullOrWhiteSpace(this.AliasFactory);

        /// <summary>
        /// First listed layout, or <see cref="GenericRuntimeAttributeSupport.CurrentLayout"/> when
        /// the list is empty. Prefer <see cref="Layouts"/> / <see cref="HasSupportedLayout"/>.
        /// </summary>
        public int Layout => this.Layouts.Count > 0
            ? this.Layouts[0]
            : GenericRuntimeAttributeSupport.CurrentLayout;

        public bool HasSupportedLayout()
            => this.Layouts.Any(static layout => layout == GenericRuntimeAttributeSupport.CurrentLayout);

        public string FormatLayoutsForDiagnostic()
            => this.Layouts.Count == 0
                ? "(none)"
                : string.Join(", ", this.Layouts);
    }
}
