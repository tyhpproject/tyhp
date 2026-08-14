namespace Tyhp.TyhpLang.Binder.Scopes.Interfaces {
    /// <summary>
    /// Marker interface for scopes that may be attached directly under a <see cref="FileScope"/>.
    /// </summary>
    public interface IFileScopeChild : IBaseScope<FileScope> {}
}
