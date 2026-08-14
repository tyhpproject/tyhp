using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols
{
    /// <summary>
    /// Represents a function or method parameter in the binder model.
    /// </summary>
    public record ParameterInfo(
        string Name,
        ITypeExpression? DeclaredType,
        IExpression? DefaultValue,
        bool IsVariadic,
        bool IsByReference,
        MemberModifier PromotedVisibility
    );
}
