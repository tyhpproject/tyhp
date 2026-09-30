namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Tyhpdef signatures require a type on every parameter and return.
    /// Reflection / PHP source may omit them; Layer 1 fills the gap.
    /// </summary>
    internal static class TyhpdefRequiredTypes
    {
        public static string ReturnType(string? memberName, string? mapped)
        {
            var type = (mapped ?? "").Trim();
            if (type.Length > 0)
            {
                return type;
            }

            return IsConstructorOrDestructor(memberName) ? "void" : "mixed";
        }

        public static string ParameterType(string? mapped)
        {
            var type = (mapped ?? "").Trim();
            return type.Length > 0 ? type : "mixed";
        }

        public static bool IsConstructorOrDestructor(string? name)
            => string.Equals(name, "__construct", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "__destruct", StringComparison.OrdinalIgnoreCase);
    }
}
