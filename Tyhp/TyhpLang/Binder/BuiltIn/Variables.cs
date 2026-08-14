using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Binder.BuiltIn
{
    public static class Variables
    {
        public static void PopulateGlobal(GlobalScope globalScope)
        {
            // PHP built in variables
            globalScope.AddChildSymbol(new SuperGlobalSymbol("$GLOBALS"));
            globalScope.AddChildSymbol(new SuperGlobalSymbol("$_SERVER"));
            globalScope.AddChildSymbol(new SuperGlobalSymbol("$_GET"));
            globalScope.AddChildSymbol(new SuperGlobalSymbol("$_POST"));
            globalScope.AddChildSymbol(new SuperGlobalSymbol("$_FILES"));
            globalScope.AddChildSymbol(new SuperGlobalSymbol("$_COOKIE"));
            globalScope.AddChildSymbol(new SuperGlobalSymbol("$_SESSION"));
            globalScope.AddChildSymbol(new SuperGlobalSymbol("$_REQUEST"));
            globalScope.AddChildSymbol(new SuperGlobalSymbol("$_ENV"));

            // Tyhp built in variables
            // TODO: Add Tyhp-specific built-in variables when the language specification defines them.
        }
    }
}