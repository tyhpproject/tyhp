using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Binder.BuiltIn
{
    public static class Types
    {
        public static void PopulateGlobal(GlobalScope globalScope)
        {
            // PHP built in types
            globalScope.AddChildSymbol(new BuiltInTypeSymbol("void"));
            globalScope.AddChildSymbol(new BuiltInTypeSymbol("null"));
            globalScope.AddChildSymbol(new BuiltInTypeSymbol("array", backingTypeName: null, genericParameterRequirements: GenericParameterRequirements.ArrayLike()));
            globalScope.AddChildSymbol(new BuiltInTypeSymbol("bool"));
            globalScope.AddChildSymbol(new BuiltInTypeSymbol("callable", backingTypeName: null, genericParameterRequirements: GenericParameterRequirements.Callable()));
            globalScope.AddChildSymbol(new BuiltInTypeSymbol("false"));
            globalScope.AddChildSymbol(new BuiltInTypeSymbol("true"));
            globalScope.AddChildSymbol(new BuiltInTypeSymbol("float"));
            globalScope.AddChildSymbol(new BuiltInTypeSymbol("int"));
            globalScope.AddChildSymbol(new BuiltInTypeSymbol("iterable", backingTypeName: null, genericParameterRequirements: GenericParameterRequirements.ArrayLike()));
            globalScope.AddChildSymbol(new BuiltInTypeSymbol("mixed"));
            globalScope.AddChildSymbol(new BuiltInTypeSymbol("never"));
            globalScope.AddChildSymbol(new BuiltInTypeSymbol("object"));
            globalScope.AddChildSymbol(new BuiltInTypeSymbol("resource"));
            globalScope.AddChildSymbol(new BuiltInTypeSymbol("string"));

            // Tyhp built in types
            globalScope.AddChildSymbol(new BuiltInTypeSymbol("Decimal", "float", null));
            // globalScope.AddChildSymbol(new BuiltInTypeSymbol("inferred"));
            globalScope.AddChildSymbol(new BuiltInTypeSymbol("struct", "array", null));
        }

        public static void PopulateObject(ObjectDeclarationScope objectScope)
        {
            objectScope.AddChildSymbol(new BuiltInTypeSymbol("parent"));
            objectScope.AddChildSymbol(new BuiltInTypeSymbol("self"));
            objectScope.AddChildSymbol(new BuiltInTypeSymbol("static"));
        }
    }
}
