using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Binder.BuiltIn
{
    public static class Constants
    {
        public static void PopulateGlobal(GlobalScope globalScope)
        {
            // PHP built in constants
            globalScope.AddChildSymbol(new MagicConstantSymbol("__LINE__"));
            globalScope.AddChildSymbol(new MagicConstantSymbol("__FUNCTION__"));
            globalScope.AddChildSymbol(new MagicConstantSymbol("__FILE__"));
            globalScope.AddChildSymbol(new MagicConstantSymbol("__METHOD__"));
            globalScope.AddChildSymbol(new MagicConstantSymbol("__CLASS__"));
            globalScope.AddChildSymbol(new MagicConstantSymbol("__DIR__"));
            globalScope.AddChildSymbol(new MagicConstantSymbol("__TRAIT__"));
            globalScope.AddChildSymbol(new MagicConstantSymbol("__NAMESPACE__"));

            // Tyhp built in constants
            globalScope.AddChildSymbol(new MagicConstantSymbol("__TYHP_LINE__"));
            globalScope.AddChildSymbol(new MagicConstantSymbol("__TYHP_FUNCTION__"));
            globalScope.AddChildSymbol(new MagicConstantSymbol("__TYHP_FILE__"));
            globalScope.AddChildSymbol(new MagicConstantSymbol("__TYHP_METHOD__"));
            globalScope.AddChildSymbol(new MagicConstantSymbol("__TYHP_CLASS__"));
            globalScope.AddChildSymbol(new MagicConstantSymbol("__TYHP_DIR__"));
            globalScope.AddChildSymbol(new MagicConstantSymbol("__TYHP_TRAIT__"));
            globalScope.AddChildSymbol(new MagicConstantSymbol("__TYHP_NAMESPACE__"));
        }
    }
}