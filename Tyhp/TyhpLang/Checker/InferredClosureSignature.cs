using Tyhp.TyhpLang.Ast;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Call-site / annotation-driven types for a closure that omitted authored parameter or return
    /// annotations. The checker records these so the emitter can spell PHP typehints that were
    /// never written in Tyhp source.
    /// </summary>
    public sealed class InferredClosureSignature
    {
        public InferredClosureSignature(
            ICheckedType? returnType,
            IReadOnlyList<ICheckedType?> parameterTypes)
        {
            ReturnType = returnType;
            ParameterTypes = parameterTypes;
        }

        /// <summary>
        /// Expected return type when <see cref="PhpInlineFunctionAst.ReturnType"/> was absent.
        /// </summary>
        public ICheckedType? ReturnType { get; }

        /// <summary>
        /// Per-parameter expected types (same length as the closure's parameter list). Entries are
        /// null when the parameter already had an authored type annotation or no contextual type
        /// was available for that slot.
        /// </summary>
        public IReadOnlyList<ICheckedType?> ParameterTypes { get; }
    }
}
