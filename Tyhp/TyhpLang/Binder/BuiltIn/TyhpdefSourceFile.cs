using Tyhp.TyhpLang.Ast;

namespace Tyhp.TyhpLang.Binder.BuiltIn
{
    /// <summary>
    /// A parsed tyhpdef or tyhp overlay file together with its package source identity.
    /// </summary>
    public sealed class TyhpdefSourceFile
    {
        public required SrcFileAst Ast { get; init; }

        /// <summary>
        /// Identifies the package that contributed this file (embedded key, package root path, etc.).
        /// </summary>
        public required string PackageSource { get; init; }

        /// <summary>
        /// Lower values load earlier. Built-in embedded sources use 0; Composer packages use 100+.
        /// </summary>
        public int LoadOrder { get; init; }
    }
}
