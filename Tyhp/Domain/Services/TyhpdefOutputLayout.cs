namespace Tyhp.Domain.Services
{
    /// <summary>
    /// CLI tyhpdef output paths (plan §2.5). Default is <c>{cwd}/tyhpdef/</c>, not the live <c>runtime/packages/php/_tyhpdef/</c> tree.
    /// </summary>
    public static class TyhpdefOutputLayout
    {
        public static string ExtensionFileName(string extensionName)
        {
            var name = (extensionName ?? "").Trim();
            if (name.Length == 0)
            {
                return "ExtUnknown.tyhpdef";
            }

            var chars = name.ToCharArray();
            chars[0] = char.ToUpperInvariant(chars[0]);
            return "Ext" + new string(chars) + ".tyhpdef";
        }

        /// <summary>
        /// Layer 2 stub overlay basename. Matches <c>--output-file</c> when set so
        /// <c>_tyhpdef/Ext.Xsl.tyhpdef</c> and <c>_tyhpdef/overlays/stubs/Ext.Xsl.tyhpdef</c>
        /// share a name and the folder indicates the layer.
        /// </summary>
        public static string StubOverlayFileName(TyhpdefGenerationOptions options, string extensionName)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (!string.IsNullOrWhiteSpace(options.OutputFileName))
            {
                return Path.GetFileName(options.OutputFileName);
            }

            return ExtensionFileName(extensionName);
        }

        /// <summary>
        /// Generator-owned PHP-source harvest <c>extern</c> placeholders. Regen overwrites
        /// this basename only; hand <c>extern</c> lives in a sibling file.
        /// </summary>
        public const string ExternsFileName = "externs.tyhpdef";

        /// <summary>Default CLI filename for <c>--source</c> (plan §2.5).</summary>
        public static string SourceFileName() => "source.tyhpdef";

        /// <summary>
        /// Default CLI filename for <c>--package-path</c> from Composer <c>name</c>
        /// (<c>guzzlehttp/guzzle</c> → <c>guzzlehttp.guzzle.tyhpdef</c>).
        /// </summary>
        public static string PackageFileName(string? composerPackageName)
        {
            var name = (composerPackageName ?? "").Trim().Trim('/');
            if (name.Length == 0)
            {
                return "package.tyhpdef";
            }

            return name.Replace('/', '.') + ".tyhpdef";
        }

        public static IReadOnlyList<(string Path, TyhpdefFile File)> Split(
            TyhpdefFile file,
            TyhpdefGenerationOptions options,
            string extensionName)
            => SplitWithPrimaryFile(file, options, ExtensionFileName(extensionName));

        /// <summary>
        /// Same split rules as <see cref="Split"/>, but the default <c>file</c> layout uses
        /// <paramref name="primaryFileName"/> instead of <c>Ext{Name}.tyhpdef</c> (PHP-source harvest).
        /// </summary>
        public static IReadOnlyList<(string Path, TyhpdefFile File)> SplitWithPrimaryFile(
            TyhpdefFile file,
            TyhpdefGenerationOptions options,
            string primaryFileName)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(options);

            var outputDir = options.OutputDirectory;
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                outputDir = Path.Combine(Directory.GetCurrentDirectory(), "tyhpdef");
            }

            if (!string.IsNullOrWhiteSpace(options.OutputFileName))
            {
                return [(options.OutputFileName, file)];
            }

            var split = (options.Split ?? "file").Trim().ToLowerInvariant();
            var primary = string.IsNullOrWhiteSpace(primaryFileName)
                ? SourceFileName()
                : primaryFileName.Trim();
            if (split == "namespace")
            {
                return SplitByNamespace(file, outputDir, primary);
            }

            if (split == "type")
            {
                return SplitByType(file, outputDir);
            }

            return [(Path.Combine(outputDir, primary), file)];
        }

        private static List<(string Path, TyhpdefFile File)> SplitByNamespace(
            TyhpdefFile file,
            string outputDir,
            string primaryFileName)
        {
            var result = new List<(string Path, TyhpdefFile File)>();
            var hasGlobals = (file.GlobalConstants?.Count ?? 0) > 0
                || (file.GlobalFunctions?.Count ?? 0) > 0
                || (file.TypeAliases?.Count ?? 0) > 0
                || (file.GlobalTypes?.Count ?? 0) > 0;
            var namespaces = file.Namespaces ?? [];

            if (!hasGlobals && namespaces.Count == 1)
            {
                var ns = namespaces[0];
                var single = CloneWithNamespace(file, ns);
                if ((file.DeclareBlocks?.Count ?? 0) > 0)
                {
                    single = single with { DeclareBlocks = file.DeclareBlocks ?? [] };
                }

                result.Add((Path.Combine(outputDir, NamespaceFileName(ns.Name)), single));
                return result;
            }

            if (hasGlobals || namespaces.Count == 0)
            {
                var primaryName = hasGlobals && namespaces.Count == 0
                    ? primaryFileName
                    : "_global.tyhpdef";
                result.Add((
                    Path.Combine(outputDir, primaryName),
                    new TyhpdefFile
                    {
                        Header = file.Header,
                        FileAttributes = file.FileAttributes,
                        DeclareBlocks = file.DeclareBlocks ?? [],
                        GlobalConstants = file.GlobalConstants ?? [],
                        GlobalFunctions = file.GlobalFunctions ?? [],
                        TypeAliases = file.TypeAliases ?? [],
                        GlobalTypes = file.GlobalTypes ?? [],
                    }));
            }

            foreach (var ns in namespaces)
            {
                result.Add((Path.Combine(outputDir, NamespaceFileName(ns.Name)), CloneWithNamespace(file, ns)));
            }

            return result;
        }

        private static List<(string Path, TyhpdefFile File)> SplitByType(TyhpdefFile file, string outputDir)
        {
            var result = new List<(string Path, TyhpdefFile File)>();
            var hasGlobals = (file.GlobalConstants?.Count ?? 0) > 0
                || (file.GlobalFunctions?.Count ?? 0) > 0
                || (file.TypeAliases?.Count ?? 0) > 0;

            if (hasGlobals)
            {
                result.Add((
                    Path.Combine(outputDir, "_global.tyhpdef"),
                    new TyhpdefFile
                    {
                        Header = file.Header,
                        FileAttributes = file.FileAttributes,
                        DeclareBlocks = file.DeclareBlocks ?? [],
                        GlobalConstants = file.GlobalConstants ?? [],
                        GlobalFunctions = file.GlobalFunctions ?? [],
                        TypeAliases = file.TypeAliases ?? [],
                    }));
            }

            foreach (var type in file.GlobalTypes ?? [])
            {
                result.Add((
                    Path.Combine(outputDir, TypeFileName("", type.Name)),
                    new TyhpdefFile { Header = file.Header, GlobalTypes = [type] }));
            }

            foreach (var ns in file.Namespaces ?? [])
            {
                if ((ns.Constants?.Count ?? 0) > 0
                    || (ns.Functions?.Count ?? 0) > 0
                    || (ns.TypeAliases?.Count ?? 0) > 0)
                {
                    result.Add((
                        Path.Combine(outputDir, NamespaceFileName(ns.Name)),
                        new TyhpdefFile
                        {
                            Header = file.Header,
                            Namespaces =
                            [
                                new TyhpdefNamespace
                                {
                                    Name = ns.Name,
                                    Constants = ns.Constants ?? [],
                                    Functions = ns.Functions ?? [],
                                    TypeAliases = ns.TypeAliases ?? [],
                                },
                            ],
                        }));
                }

                foreach (var type in ns.Classes ?? [])
                {
                    result.Add((
                        Path.Combine(outputDir, TypeFileName(ns.Name, type.Name)),
                        new TyhpdefFile
                        {
                            Header = file.Header,
                            Namespaces =
                            [
                                new TyhpdefNamespace { Name = ns.Name, Classes = [type] },
                            ],
                        }));
                }
            }

            return result;
        }

        private static TyhpdefFile CloneWithNamespace(TyhpdefFile headerSource, TyhpdefNamespace ns)
            => new()
            {
                Header = headerSource.Header,
                FileAttributes = headerSource.FileAttributes,
                Namespaces = [ns],
            };

        private static string NamespaceFileName(string ns)
            => (ns.Trim().TrimStart('\\').Replace('\\', '.') + ".tyhpdef");

        private static string TypeFileName(string ns, string typeName)
        {
            var prefix = ns.Trim().TrimStart('\\').Replace('\\', '.');
            var name = typeName.Trim().TrimStart('\\');
            return string.IsNullOrWhiteSpace(prefix)
                ? name + ".tyhpdef"
                : prefix + "." + name + ".tyhpdef";
        }
    }
}
