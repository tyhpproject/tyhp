namespace Tyhp.Config
{
    /// <summary>
    /// Resolves filesystem paths to a comparable absolute form by expanding intermediate
    /// directory (and leaf) symlinks. <see cref="Path.GetFullPath"/> alone does not do this,
    /// while <see cref="Directory.GetCurrentDirectory"/> on some platforms already returns the
    /// physical path — so string prefix checks between the two spellings disagree.
    /// </summary>
    internal static class PathCanonicalizer
    {
        private const int MaxSymlinkDepth = 64;

        /// <summary>
        /// Returns an absolute path with symlink components resolved when they exist on disk.
        /// Non-existent trailing segments keep their names after any resolved parents.
        /// </summary>
        public static string GetCanonicalFullPath(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return path;
            }

            try
            {
                return ResolveExistingComponents(fullPath, depth: 0);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return fullPath;
            }
        }

        /// <summary>
        /// True when <paramref name="path"/> resolves to <paramref name="root"/> or a path under
        /// it, after expanding intermediate directory symlinks on both sides.
        /// </summary>
        public static bool IsUnderRoot(string path, string root)
        {
            var canonicalPath = GetCanonicalFullPath(path);
            var canonicalRoot = GetCanonicalFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var prefix = canonicalRoot + Path.DirectorySeparatorChar;

            return canonicalPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || String.Equals(canonicalPath, canonicalRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveExistingComponents(string fullPath, int depth)
        {
            if (depth > MaxSymlinkDepth)
            {
                return fullPath;
            }

            var root = Path.GetPathRoot(fullPath);
            if (String.IsNullOrEmpty(root))
            {
                return fullPath;
            }

            var relative = fullPath.Length > root.Length ? fullPath[root.Length..] : String.Empty;
            var parts = relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

            // Keep the OS root spelling from GetPathRoot (e.g. "/" or "C:\").
            var current = root;

            for (var i = 0; i < parts.Length; i++)
            {
                var next = Path.Combine(current, parts[i]);
                var isLast = i == parts.Length - 1;

                if (!isLast || Directory.Exists(next))
                {
                    var directory = new DirectoryInfo(next);
                    if (directory.Exists)
                    {
                        var linkTarget = ResolveInfo(directory);
                        if (linkTarget != null)
                        {
                            // CreateSymbolicLink may record a target that still contains unresolved
                            // prefixes (e.g. /var/... while /var → /private/var). Restart the walk
                            // on target + remaining segments so every prefix is expanded.
                            return ResolveExistingComponents(
                                JoinWithRemaining(linkTarget, parts, i + 1),
                                depth + 1);
                        }

                        current = Path.GetFullPath(directory.FullName);
                        continue;
                    }

                    current = next;
                    continue;
                }

                var file = new FileInfo(next);
                if (file.Exists)
                {
                    var linkTarget = ResolveInfo(file);
                    if (linkTarget != null)
                    {
                        return ResolveExistingComponents(linkTarget, depth + 1);
                    }

                    return Path.GetFullPath(file.FullName);
                }

                // Parents resolved; trailing segment does not exist yet.
                return Path.GetFullPath(next);
            }

            return Path.GetFullPath(current);
        }

        private static string JoinWithRemaining(string head, string[] parts, int startIndex)
        {
            var combined = head;
            for (var i = startIndex; i < parts.Length; i++)
            {
                combined = Path.Combine(combined, parts[i]);
            }

            return Path.GetFullPath(combined);
        }

        private static string? ResolveInfo(FileSystemInfo info)
        {
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            return target == null ? null : Path.GetFullPath(target.FullName);
        }
    }
}
