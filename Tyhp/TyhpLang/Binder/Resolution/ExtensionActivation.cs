using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Binder.Resolution
{
    /// <summary>
    /// Which extensions apply at a call site. One predicate for the checker and the emitter, so a
    /// call the checker accepts through an extension is the call the emitter rewrites.
    /// </summary>
    /// <remarks>
    /// A project-source extension applies in the file that declares it, in a file that imports it
    /// with <c>use extension</c>, and everywhere once any file declares it with
    /// <c>global use extension</c>. Compiler-generated extensions (tyhpdef class-body
    /// <c>extension operator</c> mappings) belong to their class and always apply.
    /// </remarks>
    public static class ExtensionActivation
    {
        /// <summary>True when <paramref name="owner"/> applies in <paramref name="file"/>.</summary>
        public static bool IsActive(
            ObjectDeclarationSymbol? owner,
            FileScope? file,
            GlobalScope globalScope)
        {
            if (owner is null || !owner.IsExtension || owner.IsCompilerGenerated)
            {
                return true;
            }

            if (file is not null
                && (file.DeclaredExtensions.Contains(owner) || file.ImportedExtensions.Contains(owner)))
            {
                return true;
            }

            return globalScope.GloballyActivatedExtensions.Contains(owner);
        }

        /// <summary>
        /// True when <paramref name="convert"/> applies at a call site in <paramref name="site"/>.
        /// A class-owned convert always applies. When the site file is unknown (no file is being
        /// checked or emitted, or it is not bound) every convert applies.
        /// </summary>
        public static bool IsConvertVisible(
            ObjectOperatorOverloadMethodSymbol convert,
            SrcFileAst? site,
            GlobalScope globalScope)
        {
            if (!convert.IsExtensionOperator || site is null)
            {
                return true;
            }

            var owner = convert.DeclaringExtensionSymbol;
            if (owner is null || !owner.IsExtension || owner.IsCompilerGenerated)
            {
                return true;
            }

            var fileScope = FindFileScope(globalScope, site);
            return fileScope is null || IsActive(owner, fileScope, globalScope);
        }

        /// <summary>
        /// True when an extension operator (binary, unary, or convert) applies at a call site
        /// in <paramref name="site"/>. Same rule as <see cref="IsActive"/>.
        /// </summary>
        public static bool IsOperatorVisible(
            ObjectOperatorOverloadMethodSymbol method,
            SrcFileAst? site,
            GlobalScope globalScope) =>
            IsConvertVisible(method, site, globalScope);

        /// <summary>
        /// True when an extension operator applies in the file named <paramref name="fileName"/>.
        /// An unknown file applies every operator, matching <see cref="IsConvertVisible"/> when
        /// the site file is not bound.
        /// </summary>
        public static bool IsOperatorVisible(
            ObjectOperatorOverloadMethodSymbol method,
            string? fileName,
            GlobalScope globalScope)
        {
            if (!method.IsExtensionOperator || string.IsNullOrEmpty(fileName))
            {
                return true;
            }

            var owner = method.DeclaringExtensionSymbol;
            if (owner is null || !owner.IsExtension || owner.IsCompilerGenerated)
            {
                return true;
            }

            FileScope? fileScope = null;
            foreach (var child in ((IBaseScope)globalScope).GetAllChildScopes())
            {
                if (child is FileScope candidate
                    && (string.Equals(candidate.FileName, fileName, StringComparison.Ordinal)
                        || string.Equals(candidate.SourceFile, fileName, StringComparison.Ordinal)))
                {
                    fileScope = candidate;
                    break;
                }
            }

            return fileScope is null || IsActive(owner, fileScope, globalScope);
        }

        /// <summary>The <see cref="FileScope"/> of <paramref name="file"/>, or null when it is not bound.</summary>
        public static FileScope? FindFileScope(GlobalScope globalScope, SrcFileAst? file)
        {
            if (file is null)
            {
                return null;
            }

            var fileName = file.FileName;
            var absolutePath = file.Identifier;
            foreach (var child in ((IBaseScope)globalScope).GetAllChildScopes())
            {
                if (child is not FileScope fileScope)
                {
                    continue;
                }

                if ((!string.IsNullOrEmpty(fileName)
                        && (string.Equals(fileScope.FileName, fileName, StringComparison.Ordinal)
                            || string.Equals(fileScope.SourceFile, fileName, StringComparison.Ordinal)))
                    || (!string.IsNullOrEmpty(absolutePath)
                        && (string.Equals(fileScope.SourceFile, absolutePath, StringComparison.Ordinal)
                            || string.Equals(fileScope.FileName, absolutePath, StringComparison.Ordinal))))
                {
                    return fileScope;
                }
            }

            return null;
        }
    }
}
