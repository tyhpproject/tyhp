namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Path checks for engine and runtime package sources. In-tree
    /// <c>runtime/packages/</c> stays recognized while that tree is still present.
    /// A sibling <c>tyhp-runtime-src/packages</c> checkout, <c>TYHP_RUNTIME_SRC</c>,
    /// and <c>/packages/php/</c> are the same sources once packages live beside the compiler.
    /// </summary>
    internal static class RuntimePackagePaths
    {
        internal static bool IsRuntimePackageSource(string? sourceFile)
            => IsRuntimePackageSource(sourceFile, ComposerJsonService.TryResolveRuntimePackagesRoot());

        internal static bool IsRuntimePackageSource(string? sourceFile, string? runtimePackagesRoot)
        {
            if (string.IsNullOrWhiteSpace(sourceFile))
            {
                return false;
            }

            var normalized = sourceFile.Replace('\\', '/');
            if (HasPathSuffix(normalized, "runtime/packages/")
                || HasPathSuffix(normalized, "tyhp-runtime-src/packages/"))
            {
                return true;
            }

            return ContainsDirectory(normalized, runtimePackagesRoot);
        }

        /// <summary>
        /// PHP engine / extension stubs. A user <c>.tyhp</c> class that only reuses a Core
        /// name is not an engine declaration.
        /// </summary>
        internal static bool IsEnginePhpPackageSource(string? sourceFile)
            => IsEnginePhpPackageSource(sourceFile, ComposerJsonService.TryResolveRuntimePackagesRoot());

        internal static bool IsEnginePhpPackageSource(string? sourceFile, string? runtimePackagesRoot)
        {
            if (string.IsNullOrWhiteSpace(sourceFile))
            {
                return false;
            }

            var normalized = sourceFile.Replace('\\', '/');
            if (HasPathSuffix(normalized, "runtime/packages/php/")
                || HasPathSuffix(normalized, "packages/php/"))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(runtimePackagesRoot))
            {
                return false;
            }

            var phpRoot = runtimePackagesRoot.Replace('\\', '/').TrimEnd('/') + "/php";
            return ContainsDirectory(normalized, phpRoot);
        }

        private static bool HasPathSuffix(string normalized, string segment)
            => normalized.StartsWith(segment, StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/" + segment, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when <paramref name="normalizedPath"/> is <paramref name="directory"/> or a file
        /// under it. A longer prefix such as <c>/opt/pkgs-other</c> does not match <c>/opt/pkgs</c>.
        /// </summary>
        private static bool ContainsDirectory(string normalizedPath, string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            var root = directory.Replace('\\', '/').TrimEnd('/');
            if (root.Length == 0)
            {
                return false;
            }

            var index = 0;
            while (index < normalizedPath.Length)
            {
                var found = normalizedPath.IndexOf(root, index, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    return false;
                }

                var beforeOk = found == 0 || normalizedPath[found - 1] == '/';
                var after = found + root.Length;
                var afterOk = after == normalizedPath.Length || normalizedPath[after] == '/';
                if (beforeOk && afterOk)
                {
                    return true;
                }

                index = found + 1;
            }

            return false;
        }
    }
}
