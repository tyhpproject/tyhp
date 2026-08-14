using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols
{
    /// <summary>
    /// A built-in checker utility type in the <c>\Tyhp</c> namespace (e.g. <c>\Tyhp\Partial&lt;T&gt;</c>).
    /// </summary>
    public class BuiltInUtilityTypeSymbol :
        BaseSymbol,
        INamespaceBlockScopeSymbol,
        IGlobalScopeSymbol
    {
        public UtilityBehavior Behavior { get; }

        public GenericParameterRequirements GenericParameterRequirements { get; }

        public BuiltInUtilityTypeSymbol(
            string name,
            UtilityBehavior behavior,
            GenericParameterRequirements genericParameterRequirements
        )
            : base(name, SymbolType.BuiltInUtilityType)
        {
            this.Behavior = behavior;
            this.GenericParameterRequirements = genericParameterRequirements;
        }
    }
}
