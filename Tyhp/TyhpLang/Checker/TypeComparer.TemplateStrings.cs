using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;

namespace Tyhp.TyhpLang.Checker
{
    public static partial class TypeComparer
    {
        private static bool TryCheckTemplateStringAssignability(
            ICheckedType source,
            ICheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited,
            out bool result)
        {
            result = false;

            if (TemplateStringTypeHelper.IsNonMatchingStringType(target) &&
                !TemplateStringTypeHelper.IsNonMatchingStringType(source))
            {
                return false;
            }

            if (TemplateStringTypeHelper.IsNonMatchingStringType(source))
            {
                result = TemplateStringTypeHelper.IsNonMatchingStringType(target);
                return true;
            }

            if (source is TemplateStringCheckedType sourceTemplate)
            {
                if (Rules.CheckerHelpers.IsBuiltInName(target, "string"))
                {
                    result = true;
                    return true;
                }

                if (target is TemplateStringCheckedType targetTemplate)
                {
                    var budget = CreateTemplateStringBudget();
                    result = TemplateStringMatcher.IsSubtypeOf(
                        sourceTemplate.Pattern,
                        targetTemplate.Pattern,
                        symbolTree,
                        globalScope,
                        budget,
                        out var exceededLimit);
                    if (exceededLimit || budget.ExceededLimit)
                    {
                        MarkTemplateStringBudgetExceeded();
                        result = false;
                    }

                    return true;
                }

                if (target is UnionCheckedType unionTarget)
                {
                    result = unionTarget.Members.Any(member =>
                        IsAssignableToCore(source, member, symbolTree, globalScope, visited));
                    return true;
                }

                return false;
            }

            if (source is LiteralCheckedType { Value: string literal } &&
                target is TemplateStringCheckedType targetPattern)
            {
                var budget = CreateTemplateStringBudget();
                result = TemplateStringMatcher.Matches(
                    literal,
                    targetPattern.Pattern,
                    symbolTree,
                    globalScope,
                    budget);
                if (budget.ExceededLimit)
                {
                    MarkTemplateStringBudgetExceeded();
                    result = false;
                }

                return true;
            }

            if (Rules.CheckerHelpers.IsBuiltInName(source, "string") &&
                target is TemplateStringCheckedType &&
                source is not LiteralCheckedType)
            {
                result = false;
                return true;
            }

            return false;
        }

        private static bool TryCheckTemplateStringSubtyping(
            ICheckedType child,
            ICheckedType parent,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited,
            out bool result)
        {
            result = false;

            if (child is TemplateStringCheckedType childTemplate)
            {
                if (Rules.CheckerHelpers.IsBuiltInName(parent, "string"))
                {
                    result = true;
                    return true;
                }

                if (parent is TemplateStringCheckedType parentTemplate)
                {
                    var budget = CreateTemplateStringBudget();
                    result = TemplateStringMatcher.IsSubtypeOf(
                        childTemplate.Pattern,
                        parentTemplate.Pattern,
                        symbolTree,
                        globalScope,
                        budget,
                        out var exceededLimit);
                    if (exceededLimit || budget.ExceededLimit)
                    {
                        MarkTemplateStringBudgetExceeded();
                        result = false;
                    }

                    return true;
                }
            }

            if (child is LiteralCheckedType { Value: string literal } &&
                parent is TemplateStringCheckedType parentPattern)
            {
                var budget = CreateTemplateStringBudget();
                result = TemplateStringMatcher.Matches(
                    literal,
                    parentPattern.Pattern,
                    symbolTree,
                    globalScope,
                    budget);
                if (budget.ExceededLimit)
                {
                    MarkTemplateStringBudgetExceeded();
                    result = false;
                }

                return true;
            }

            return false;
        }
    }
}
