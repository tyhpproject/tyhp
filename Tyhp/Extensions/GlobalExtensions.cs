using System.Reflection;

namespace Tyhp.Extensions
{
    public static class GlobalExtensions
    {
        public static bool In<T>(this T value, params T[] values)
        {
            return values.Any(x => Object.Equals(value, x));
        }
    }
}