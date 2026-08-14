namespace Tyhp.TyhpLang.Binder.Scopes.Interfaces {
    public interface INoScopeChild<TBaseScope> :
        IBaseScope<TBaseScope>
        where TBaseScope : IBaseScope
    {}
}
