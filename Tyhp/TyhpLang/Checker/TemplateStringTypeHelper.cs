using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    internal static class TemplateStringTypeHelper
    {
        public static bool IsTemplateStringType(ICheckedType type) =>
            type is TemplateStringCheckedType;

        public static bool IsNonMatchingStringType(ICheckedType type) =>
            TryGetUtilityBehavior(type, out var behavior) &&
            behavior == UtilityBehavior.NonMatchingStringType;

        public static bool TryGetUtilityBehavior(ICheckedType type, out UtilityBehavior behavior)
        {
            behavior = default;
            if (!SymbolNameTypeHelper.TryGetUtilitySymbol(type, out var utility))
            {
                return false;
            }

            behavior = utility.Behavior;
            return true;
        }

        public static ICheckedType GetErasure() => CheckedTypes.String;
    }
}
