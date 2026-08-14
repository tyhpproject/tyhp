namespace Tyhp.TyhpLang.Binder.Scopes.Interfaces {
    public interface ILabelScopeParent : IBaseScope {
        void AddLabelChildScope(LabelScope child);
    }
}