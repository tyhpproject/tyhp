using System.Reflection;

namespace Tyhp.Extensions
{
    public static class StringExtensions
{
    public static bool ParseBool(this string? strValue)
    {
        if (!Boolean.TryParse(strValue ?? "False", out bool boolValue)) {
            if (Int32.TryParse(strValue ?? "0", out int boolValueInt)) {
                boolValue = boolValueInt > 0;
            } else if (
                !boolValue &&
                (strValue?.ToLower() ?? "n") != "n" &&
                (
                    (strValue ?? "n").Trim().ToLower() == "y" ||
                    (strValue ?? "n").Trim().ToLower() == "t" ||
                    (strValue ?? "n").Trim().ToLower() == "yes"
                )
            ) {
                boolValue = true;
            }
        }
        
        return boolValue;
    }
}
}