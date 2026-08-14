using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.BuiltIn
{
    /// <summary>
    /// Registers compile-time-only built-in functions available in all compilation units.
    /// </summary>
    public static class Functions
    {
        public static void PopulateGlobal(GlobalScope globalScope)
        {
            globalScope.AddChildSymbol(new BuiltInFunctionSymbol(
                "nameof",
                parameters: new[]
                {
                    new ParameterInfo("$symbolReference", null, null, false, false, MemberModifier.None),
                },
                returnType: null));

            globalScope.AddChildSymbol(new BuiltInFunctionSymbol(
                "typeof",
                parameters: new[]
                {
                    new ParameterInfo("$typeReference", null, null, false, false, MemberModifier.None),
                },
                returnType: null));

            globalScope.AddChildSymbol(new BuiltInFunctionSymbol(
                "default",
                parameters: new[]
                {
                    new ParameterInfo("$typeName", null, null, false, false, MemberModifier.None),
                },
                returnType: null));

            globalScope.AddChildSymbol(new BuiltInFunctionSymbol(
                "variable_exists",
                parameters: new[]
                {
                    new ParameterInfo("$varName", null, null, false, false, MemberModifier.None),
                },
                returnType: null));
        }
    }
}
