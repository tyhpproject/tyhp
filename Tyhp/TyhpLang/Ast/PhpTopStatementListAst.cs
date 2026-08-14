using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpTopStatementListAst : NodeListAst<ITopStatement, PhpTopStatementListAst>, ISrcElement, IStatementList<ITopStatement>, ITopStatement
    {
        public void Add(ITopStatement? child)
            => Children.Add(child);

        public void AddRange(IEnumerable<ITopStatement?> children)
            => Children.AddRange(children.Where(x => !this.FilterNullChildren || x != null));

        public ITopStatement? ElementAt(int index)
            => Children.OfType<ITopStatement?>().ElementAtOrDefault(index);

        public void InsertAt(int index, ITopStatement? child)
            => Children.Insert(index, child);

        public void RemoveAt(int index)
            => Children.RemoveAt(index);
    }
} 