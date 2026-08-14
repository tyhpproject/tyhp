using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class ObjectPropertySymbol :
        BaseSymbol,
        IObjectDeclarationScopeSymbol
    {
        public ITypeExpression? DeclaredType { get; internal set; }

        public IExpression? DefaultValue { get; internal set; }

        public bool HasAccessor { get; internal set; }

        public AccessorType? AccessorKind { get; internal set; }

        /// <summary>
        /// Checker-computed (Prop-init #7): a typed instance property that is not guaranteed
        /// initialized after construction (no initializer, not promoted, and not definitely
        /// assigned on all constructor paths). Reads of <c>$this->prop</c> may throw PHP's
        /// "must not be accessed before initialization" error.
        /// </summary>
        public bool MayBeUninitializedAfterConstruction { get; set; }

        /// <summary>
        /// True when the declaration carries <c>#[\Tyhp\AllowUnset]</c> (Prop-init #8). Without
        /// this attribute, <c>unset($this->prop)</c> is rejected; with it, <c>unset</c> clears
        /// initialization state and instance-method reads start possibly-uninitialized.
        /// </summary>
        public bool AllowsUnset { get; set; }

        public ObjectPropertySymbol(
            string name,
            string? sourceFile = null,
            IBase2Ast? declaringNode = null,
            SymbolType symbolType = SymbolType.InstanceObjectProperty,
            MemberModifier visibility = MemberModifier.None
        )
            : base(name, symbolType, declaringNode, sourceFile ?? string.Empty, visibility)
        {
        }
    }
}