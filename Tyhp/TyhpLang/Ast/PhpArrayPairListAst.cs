using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpArrayPairListAst : NodeListAst<PhpArrayPairAst, PhpArrayPairListAst>, IExpression, IForeachVariable, IDereferenceableBase, IScalar
    {
        /// <summary>
        /// Real pairs only — <c>[, $b]</c> skip slots (Story 21.7, including trailing-comma
        /// artifacts from an empty final <c>possibleArrayPair</c>) are dropped. Callers that
        /// treat this node as an ordinary array/tuple/bag <em>value</em> (array-literal type
        /// inference, struct/callable bag matching, array-callable literal detection, <c>with
        /// [...]</c> property lists, constant-expression checks) want this view — skip slots are
        /// only meaningful on the LHS of a destructuring assignment or a foreach variable
        /// pattern, which reads <see cref="NodeListAst{TChild, TSelf}.GetAllNotNull"/> directly
        /// via <c>ArrayAccessDestructureSupport</c> instead.
        /// </summary>
        public IEnumerable<PhpArrayPairAst> GetAllExcludingSkippedSlots() =>
            GetAllNotNull().Where(pair => !pair.IsSkippedSlot);

        /// <summary>
        /// Full pair list (interior skip slots kept) with only a <em>trailing</em> run of skip
        /// slots removed. A trailing skip slot is never a real destructuring skip — there is no
        /// following element for it to shift — it is always the parser's artifact for a trailing
        /// comma (<c>[$a, $b,]</c> or <c>[1, 2,]</c>). Interior skips (<c>[, $b]</c>) are real and
        /// must round-trip for destructuring assignment/foreach targets (Story 21.7); this is the
        /// view the emitter's array/list rendering needs for both that case and array-as-value
        /// literals, unlike <see cref="GetAllExcludingSkippedSlots"/> which callers that never
        /// support skip slots at all (type inference, struct/callable bag matching, etc.) use.
        /// </summary>
        public IEnumerable<PhpArrayPairAst> GetAllTrimmingTrailingSkippedSlots()
        {
            var pairs = GetAllNotNull().ToList();
            while (pairs.Count > 0 && pairs[^1].IsSkippedSlot)
            {
                pairs.RemoveAt(pairs.Count - 1);
            }

            return pairs;
        }
    }
} 