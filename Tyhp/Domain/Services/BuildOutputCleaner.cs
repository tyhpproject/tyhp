using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Safety-checked cleaning of the build output directory before compilation.
    /// </summary>
    public static class BuildOutputCleaner
    {
        /// <summary>
        /// Deletes generated <c>.php</c> and <c>.php.map</c> files under the configured output path
        /// when <see cref="BuildConfig.CleanBeforeBuild"/> is enabled.
        /// </summary>
        public static bool TryClean(Project project, DiagnosticBag diagnostics)
        {
            if (!project.Build.CleanBeforeBuild)
            {
                return true;
            }

            var projectPath = PathCanonicalizer.GetCanonicalFullPath(project.GetProjectPath());
            var outputPath = ResolveOutputDirectory(projectPath, project.Output.Path);

            if (!IsSafeToClean(outputPath, projectPath, project, out string? reason))
            {
                diagnostics.AddError(
                    MessageCode.BuildCleanFailed,
                    "",
                    0,
                    0,
                    outputPath,
                    reason ?? "refusing to clean an unsafe output path");
                return false;
            }

            if (!Directory.Exists(outputPath))
            {
                return true;
            }

            try
            {
                foreach (var phpFile in Directory.EnumerateFiles(outputPath, "*.php", SearchOption.AllDirectories))
                {
                    File.Delete(phpFile);
                }

                foreach (var mapFile in Directory.EnumerateFiles(outputPath, "*.php.map", SearchOption.AllDirectories))
                {
                    File.Delete(mapFile);
                }

                IncrementalBuildService.DeleteBuildState(
                    Path.Combine(outputPath, IncrementalBuildService.BuildStateFileName));

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.AddError(
                    MessageCode.BuildCleanFailed,
                    "",
                    0,
                    0,
                    outputPath,
                    ex.Message);
                return false;
            }
        }

        internal static string ResolveOutputDirectory(string projectPath, string configuredOutputPath)
        {
            if (Path.IsPathRooted(configuredOutputPath))
            {
                return PathCanonicalizer.GetCanonicalFullPath(configuredOutputPath);
            }

            return PathCanonicalizer.GetCanonicalFullPath(Path.Combine(projectPath, configuredOutputPath));
        }

        private static bool IsSafeToClean(
            string outputPath,
            string projectPath,
            Project project,
            out string? reason)
        {
            reason = null;
            // Both sides are already canonical when called from TryClean / ResolveOutputDirectory;
            // re-canonicalize so absolute configured outputs that cross a symlink still compare.
            var normalizedOutput = AppendDirectorySeparatorChar(
                PathCanonicalizer.GetCanonicalFullPath(outputPath));
            var normalizedProject = AppendDirectorySeparatorChar(
                PathCanonicalizer.GetCanonicalFullPath(projectPath));

            if (String.Equals(normalizedOutput, normalizedProject, StringComparison.OrdinalIgnoreCase))
            {
                reason = "output path is the project root";
                return false;
            }

            if (IsSystemDirectory(outputPath))
            {
                reason = "output path is a system directory";
                return false;
            }

            foreach (var includePath in project.IncludePaths)
            {
                if (String.IsNullOrWhiteSpace(includePath))
                {
                    continue;
                }

                var sourceDirectory = PathCanonicalizer.GetCanonicalFullPath(
                    Path.Combine(projectPath, includePath));
                var normalizedSource = AppendDirectorySeparatorChar(sourceDirectory);

                if (normalizedOutput.StartsWith(normalizedSource, StringComparison.OrdinalIgnoreCase)
                    || normalizedSource.StartsWith(normalizedOutput, StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"output path overlaps source include path '{includePath}'";
                    return false;
                }
            }

            return true;
        }

        private static bool IsSystemDirectory(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (root == null)
            {
                return false;
            }

            var normalizedRoot = AppendDirectorySeparatorChar(root);
            if (String.Equals(fullPath, root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                || String.Equals(AppendDirectorySeparatorChar(fullPath), normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!String.IsNullOrWhiteSpace(home)
                && String.Equals(
                    AppendDirectorySeparatorChar(fullPath),
                    AppendDirectorySeparatorChar(home),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            if (!path.EndsWith(Path.DirectorySeparatorChar) && !path.EndsWith(Path.AltDirectorySeparatorChar))
            {
                return path + Path.DirectorySeparatorChar;
            }

            return path;
        }
    }
}
