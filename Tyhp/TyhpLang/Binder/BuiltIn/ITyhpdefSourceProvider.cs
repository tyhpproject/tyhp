using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;

namespace Tyhp.TyhpLang.Binder.BuiltIn
{
    /// <summary>
    /// Supplies the tyhpdef sources a bind loads before user files. When
    /// <see cref="CompilationOptions.TyhpdefSourceProvider"/> is null the binder calls
    /// <see cref="Tyhpdef.GetSourceFiles"/> directly.
    /// </summary>
    public interface ITyhpdefSourceProvider
    {
        /// <summary>
        /// Returns the tyhpdef sources for <paramref name="options"/> and adds their load
        /// diagnostics to <paramref name="diagnostics"/>. The returned ASTs must carry no binder
        /// state (<c>BoundSymbol</c>, <c>SelectedOverload</c>, <c>OwningFile</c>) from an earlier bind.
        /// </summary>
        IReadOnlyList<TyhpdefSourceFile> GetSourceFiles(DiagnosticBag diagnostics, CompilationOptions? options);
    }
}
