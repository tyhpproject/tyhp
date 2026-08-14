namespace Tyhp.TyhpLang.Binder.Scopes.Interfaces {
    /// <summary>
    /// Marker interface for scopes that can contain code block children
    /// (CodeBlockScope, AnonymousFunctionScope, DeclareBlockScope).
    /// </summary>
    public interface ICodeBlockScopeParent : IBaseScope {
        void AddCodeBlockChildScope(ICodeBlockScopeChild child);
    }
}