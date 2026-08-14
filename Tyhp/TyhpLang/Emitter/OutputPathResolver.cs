namespace Tyhp.TyhpLang.Emitter
{
    public static class OutputPathResolver
    {
        public static string ResolveObjectPath(string fullyQualifiedName, EmitConfig config)
        {
            var normalizedName = NormalizeFullyQualifiedName(fullyQualifiedName, config.NamespacePrefix);
            var segments = normalizedName.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return CombineOutputPath(config.OutputPath, "Unknown.php");
            }

            var className = segments[^1];
            var namespaceSegments = segments.Length > 1 ? segments[..^1] : Array.Empty<string>();
            return CombineOutputPath(config.OutputPath, Path.Combine([.. namespaceSegments, $"{className}.php"]));
        }

        public static string ResolveNamespaceFunctionsPath(string? namespaceName, EmitConfig config)
        {
            var normalizedNamespace = ApplyNamespacePrefix(namespaceName, config.NamespacePrefix);
            var segments = string.IsNullOrWhiteSpace(normalizedNamespace)
                ? Array.Empty<string>()
                : normalizedNamespace.Split('\\', StringSplitOptions.RemoveEmptyEntries);

            return CombineOutputPath(config.OutputPath, Path.Combine([.. segments, "_functions.php"]));
        }

        public static string ResolveEntryPointPath(string sourceFilePath, EmitConfig config)
        {
            var relativePath = GetRelativeSourcePath(sourceFilePath, config.SourceRoot);
            var phpRelativePath = ReplaceSourceExtension(relativePath);
            return CombineOutputPath(config.OutputPath, phpRelativePath);
        }

        public static string ResolveOutputFilePath(string declaredPath, EmitConfig config)
        {
            if (Path.IsPathRooted(declaredPath))
            {
                return declaredPath.Replace('\\', '/');
            }

            return CombineOutputPath(config.OutputPath, declaredPath.Replace('\\', '/'));
        }

        private static string NormalizeFullyQualifiedName(string fullyQualifiedName, string? namespacePrefix)
        {
            var trimmed = fullyQualifiedName.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return "\\Unknown";
            }

            if (!trimmed.StartsWith('\\'))
            {
                trimmed = "\\" + trimmed;
            }

            if (string.IsNullOrWhiteSpace(namespacePrefix))
            {
                return trimmed;
            }

            var prefix = namespacePrefix.Trim().TrimStart('\\');
            var nameWithoutLeadingSlash = trimmed.TrimStart('\\');
            return string.IsNullOrWhiteSpace(nameWithoutLeadingSlash)
                ? "\\" + prefix
                : "\\" + prefix + "\\" + nameWithoutLeadingSlash;
        }

        private static string ApplyNamespacePrefix(string? namespaceName, string? namespacePrefix)
        {
            var normalizedNamespace = namespaceName?.Trim().TrimStart('\\') ?? "";
            if (string.IsNullOrWhiteSpace(namespacePrefix))
            {
                return normalizedNamespace;
            }

            var prefix = namespacePrefix.Trim().TrimStart('\\');
            return string.IsNullOrWhiteSpace(normalizedNamespace)
                ? prefix
                : prefix + "\\" + normalizedNamespace;
        }

        private static string GetRelativeSourcePath(string sourceFilePath, string? sourceRoot)
        {
            var fullPath = Path.GetFullPath(sourceFilePath);
            if (!string.IsNullOrWhiteSpace(sourceRoot))
            {
                var root = Path.GetFullPath(sourceRoot);
                if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return fullPath[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
            }

            return Path.GetFileName(fullPath);
        }

        private static string ReplaceSourceExtension(string relativePath)
        {
            if (relativePath.EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
            {
                return relativePath[..^5] + ".php";
            }

            if (relativePath.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            {
                return relativePath[..^8] + ".php";
            }

            return relativePath + ".php";
        }

        private static string CombineOutputPath(string outputPath, string relativePath)
        {
            var normalizedOutput = outputPath.Replace('\\', '/').TrimEnd('/');
            var normalizedRelative = relativePath.Replace('\\', '/').TrimStart('/');
            return string.IsNullOrWhiteSpace(normalizedOutput)
                ? normalizedRelative
                : normalizedOutput + "/" + normalizedRelative;
        }
    }
}
