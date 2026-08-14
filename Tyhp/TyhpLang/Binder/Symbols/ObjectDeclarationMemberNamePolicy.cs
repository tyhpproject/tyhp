using System;

namespace Tyhp.TyhpLang.Binder.Symbols
{
    /// <summary>
    /// Case policies for object declaration member maps.
    /// PHP keeps methods/properties and class constants in separate namespaces: method (and
    /// property-key) lookup is case-insensitive; class-constant / enum-case names are case-sensitive.
    /// </summary>
    internal static class ObjectDeclarationMemberNamePolicy
    {
        internal static readonly StringComparer MemberNameComparer = StringComparer.OrdinalIgnoreCase;

        internal static readonly StringComparer ConstantNameComparer = StringComparer.Ordinal;
    }
}
