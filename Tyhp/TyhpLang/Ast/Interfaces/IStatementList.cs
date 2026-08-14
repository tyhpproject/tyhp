namespace Tyhp.TyhpLang.Ast.Interfaces {
    public interface IStatementList<TChild> : IBase2Ast where TChild : IBase2Ast
    {
        IEnumerable<TChild?> GetAll();
        void Add(TChild? child);
        void AddRange(IEnumerable<TChild?> children);
        TChild? ElementAt(int index);
        void InsertAt(int index, TChild? child);
        void RemoveAt(int index);
    }
}