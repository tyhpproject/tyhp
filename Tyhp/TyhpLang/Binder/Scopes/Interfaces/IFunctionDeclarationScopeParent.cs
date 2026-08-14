namespace Tyhp.TyhpLang.Binder.Scopes.Interfaces {
    public interface IFunctionDeclarationScopeParent : IBaseScope {
        void AddFunctionDeclarationChildScope(FunctionDeclarationScope child);
    }
}