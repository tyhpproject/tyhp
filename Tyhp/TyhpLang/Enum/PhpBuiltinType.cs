namespace Tyhp.TyhpLang.Enum
{
    public enum PhpBuiltinType
    {
        Array,
        Callable,
        String,
        Int,
        Float,
        Bool,
        Object,
        Mixed,
        Void,
        Never,
        Null,
        True,
        False,
        Iterable
    }
    
    public static class PhpBuiltinTypeExtensions
    {
        public static PhpBuiltinType? FromString(string? scalarType)
        {
            return scalarType switch
            {
                "array" => PhpBuiltinType.Array,
                "callable" => PhpBuiltinType.Callable,
                "string" => PhpBuiltinType.String,
                "int" => PhpBuiltinType.Int,
                "float" => PhpBuiltinType.Float,
                "bool" => PhpBuiltinType.Bool,
                "object" => PhpBuiltinType.Object,
                "mixed" => PhpBuiltinType.Mixed,
                "void" => PhpBuiltinType.Void,
                "never" => PhpBuiltinType.Never,
                "null" => PhpBuiltinType.Null,
                "true" => PhpBuiltinType.True,
                "false" => PhpBuiltinType.False,
                "iterable" => PhpBuiltinType.Iterable,
                _ => null
            };
        }
    }
} 