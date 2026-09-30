namespace Tyhp.TyhpLang.Binder.BuiltIn
{
    /// <summary>
    /// Built-in checker utilities are global <c>__Name</c> symbols, not types under
    /// <c>\Tyhp</c>. Registration lives in <see cref="StructUtilityTypes"/> (and the
    /// symbol-name / type-name-algebra registrars). There is no
    /// <c>\Tyhp\Nullable</c> / <c>\Tyhp\ReturnType</c> / <c>\Tyhp\Parameters</c>
    /// compatibility alias.
    /// </summary>
    public static class UtilityTypes
    {
    }
}
