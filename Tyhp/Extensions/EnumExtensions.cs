using System.Reflection;

namespace Tyhp.Extensions
{
    public static class EnumExtensions
{
    public static T? GetAttribute<T>(this Enum enumVal) where T:System.Attribute
    {
        MemberInfo[] memInfo = enumVal.GetType().GetMember(enumVal.ToString());
        IEnumerable<T> attributes = memInfo.FirstOrDefault()?.GetCustomAttributes<T>() ?? Array.Empty<T>();
        return attributes.FirstOrDefault();
    }
}
}