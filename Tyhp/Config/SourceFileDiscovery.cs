namespace Tyhp.Config
{
    using Tyhp.Domain.Diagnostics;
    using Tyhp.Domain.Exceptions;

    /// <summary>
    /// Discovers source files from explicit CLI path arguments for lint and related actions.
    /// </summary>
    public static class SourceFileDiscovery
    {
        private static readonly string[] DirectoryLintExtensions = [".tyhp", ".tyhpdef"];

        /// <summary>
        /// Resolves explicit file or directory paths into a sorted, de-duplicated file list.
        /// Directory paths recursively include <c>.tyhp</c> and <c>.tyhpdef</c> files only.
        /// File paths are included regardless of extension.
        /// </summary>
        /// <param name="paths">Explicit paths from the command line.</param>
        /// <param name="diagnostics">Optional diagnostics bag for missing paths.</param>
        /// <returns>Absolute paths to lint.</returns>
        public static IEnumerable<string> FromExplicitPaths(
            IEnumerable<string> paths,
            DiagnosticBag? diagnostics = null)
        {
            var results = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in paths)
            {
                if (String.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                DiscoverPath(path.Trim(), results, diagnostics);
            }

            return results;
        }

        private static void DiscoverPath(
            string path,
            SortedSet<string> results,
            DiagnosticBag? diagnostics)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                diagnostics?.AddError(
                    MessageCode.LintInvalidPath,
                    path,
                    0,
                    0,
                    path,
                    ex.Message);
                return;
            }

            if (File.Exists(fullPath))
            {
                results.Add(fullPath);
                return;
            }

            if (Directory.Exists(fullPath))
            {
                DiscoverDirectory(fullPath, results);
                return;
            }

            diagnostics?.AddError(
                MessageCode.LintPathNotFound,
                fullPath,
                0,
                0,
                fullPath);
        }

        private static void DiscoverDirectory(string directory, SortedSet<string> results)
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (IsDirectoryLintSourceFile(file))
                {
                    results.Add(Path.GetFullPath(file));
                }
            }
        }

        private static bool IsDirectoryLintSourceFile(string filePath)
        {
            foreach (var extension in DirectoryLintExtensions)
            {
                if (filePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
