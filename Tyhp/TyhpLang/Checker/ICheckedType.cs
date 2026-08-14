namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Semantic type representation used by the checker for compatibility checking and inference.
    /// Unlike AST <c>ITypeExpression</c> nodes, checked types are resolved and expanded.
    /// </summary>
    public interface ICheckedType
    {
        CheckedTypeKind Kind { get; }

        /// <summary>Human-readable name for diagnostics (e.g. "int", "?string", "MyClass&lt;int&gt;").</summary>
        string DisplayName { get; }

        bool IsNullable { get; }
        bool IsNever { get; }
        bool IsVoid { get; }
        bool IsMixed { get; }
    }
}
