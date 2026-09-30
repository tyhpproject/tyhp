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
        /// Overlay files use 10_000+ so they bind after every include, in glob-array order.
        /// </summary>
        public int LoadOrder { get; init; }

        /// <summary>
        /// True when this file came from a package/project <c>overlay</c> glob (last-wins / omit).
        /// </summary>
        public bool IsOverlay { get; init; }

        /// <summary>
        /// Overlay apply order. Unique and increasing so later globs/files win. Zero for includes.
        /// </summary>
        public int OverlaySequence { get; init; }
    }
}
