namespace Tyhp.TyhpLang.Binder.Scopes.Interfaces {
    public interface IObjectDeclarationScopeParent : IBaseScope {
        void AddObjectDeclarationChildScope(ObjectDeclarationScope child);
    }
}