using System.Text;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.TyhpLang.Emitter.SourceMap
{
    /// <summary>
    /// Options for writing Source Map v3 companions alongside generated PHP files.
    /// </summary>
    internal sealed class SourceMapOptions
    {
        /// <summary>Master switch. When false, <see cref="SourceMapWriter.WriteAllSourceMaps"/> is a no-op.</summary>
        public bool Enabled { get; set; }

        /// <summary>Embed original <c>.tyhp</c> text in <c>sourcesContent</c>.</summary>
        public bool IncludeSourcesContent { get; set; }

        /// <summary>
        /// Append <c>//# sourceMappingURL=…</c> to PHP content (default: true). Ignored when
        /// <see cref="InlineSourceMap"/> is true (the data URL replaces the external-file comment).
        /// </summary>
        public bool AppendSourceMappingUrl { get; set; } = true;

        /// <summary>
        /// Embed the map as a base64 data URL in the PHP file instead of writing a separate
        /// <c>.map</c> file (default: false).
        /// </summary>
        public bool InlineSourceMap { get; set; }

        /// <summary>
        /// Callback invoked with each original registered source path when
        /// <see cref="IncludeSourcesContent"/> is true.
        /// </summary>
        public Func<string, string?>? SourceContentProvider { get; set; }

        /// <summary>
        /// Run <see cref="SourceMapValidator"/> against each generated map. Failures are
        /// non-fatal warnings. Default is false; the live writer enables this when
        /// sourcemaps are on. Does not call <c>PHPOutputFile.Generate()</c> — callers must
        /// pass the PHP string from the single generate pass.
        /// </summary>
        public bool ValidateSourceMaps { get; set; }
    }

    /// <summary>
    /// Writes <c>.map</c> files, appends <c>sourceMappingURL</c> comments, and is the single
    /// sourcemap I/O surface used by the build pipeline (<c>OutputWriterService</c>).
    /// </summary>
    internal static class SourceMapWriter
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// Directory prefix of <paramref name="registeredSourcePath"/> (with a trailing <c>/</c>)
        /// suitable as <c>PHPOutputFile.SourceRoot</c>. Pairs with the registered collector path
        /// so <c>SourceMapGenerator</c> can strip it (e.g. <c>src/</c> + <c>src/App.tyhp</c> →
        /// <c>App.tyhp</c>). Returns null when the path has no directory component. Does not
        /// invent URL-style <c>../src/</c> roots relative to the output file.
        /// </summary>
        internal static string? SourceRootPrefixFor(string? registeredSourcePath)
        {
            if (string.IsNullOrWhiteSpace(registeredSourcePath))
            {
                return null;
            }

            var normalized = registeredSourcePath.Replace('\\', '/');
            var lastSlash = normalized.LastIndexOf('/');
            if (lastSlash < 0)
            {
                return null;
            }

            return normalized[..(lastSlash + 1)];
        }

        /// <summary>
        /// File-backed content provider that resolves project-relative registered paths against
        /// <paramref name="projectPath"/> so <c>File.ReadAllText</c> does not depend on CWD.
        /// </summary>
        internal static Func<string, string?> CreateFileContentProvider(string? projectPath)
        {
            return path =>
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return null;
                }

                try
                {
                    var fullPath = Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(projectPath)
                        ? path
                        : Path.GetFullPath(Path.Combine(projectPath, path));
                    return File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    return null;
                }
            };
        }

        /// <summary>
        /// Write <paramref name="sourceMapJson"/> to <c>{phpFilePath}.map</c>. IO failures add
        /// <see cref="MessageCode.EmitterSourceMapWriteFailed"/> (warning) rather than throwing.
        /// </summary>
        public static void WriteSourceMapFile(string phpFilePath, string sourceMapJson, DiagnosticBag diagnostics)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(phpFilePath);
            ArgumentNullException.ThrowIfNull(sourceMapJson);
            ArgumentNullException.ThrowIfNull(diagnostics);

            var mapPath = phpFilePath + ".map";
            try
            {
                var directory = Path.GetDirectoryName(mapPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(mapPath, sourceMapJson, Utf8NoBom);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                diagnostics.AddWarning(
                    MessageCode.EmitterSourceMapWriteFailed,
                    phpFilePath,
                    0,
                    0,
                    mapPath,
                    ex.Message);
            }
        }

        /// <summary>
        /// Append <c>//# sourceMappingURL={mapFileName}</c> on its own line. If
        /// <paramref name="phpContent"/> already ends with a newline, the comment is appended
        /// directly; otherwise a newline is inserted first. The comment line itself ends with
        /// <c>\n</c>. Already-present <c>sourceMappingURL</c> comments are left unchanged.
        /// </summary>
        public static string AppendSourceMappingUrl(string phpContent, string mapFileName)
        {
            ArgumentNullException.ThrowIfNull(phpContent);
            ArgumentException.ThrowIfNullOrWhiteSpace(mapFileName);

            if (phpContent.Contains("sourceMappingURL=", StringComparison.Ordinal))
            {
                return phpContent;
            }

            var comment = "//# sourceMappingURL=" + mapFileName + "\n";
            if (phpContent.Length == 0 || phpContent.EndsWith('\n'))
            {
                return phpContent + comment;
            }

            return phpContent + "\n" + comment;
        }

        /// <summary>
        /// Convert JSON to a base64 data-URL comment:
        /// <c>//# sourceMappingURL=data:application/json;charset=utf-8;base64,…</c>.
        /// </summary>
        public static string CreateInlineSourceMap(string sourceMapJson)
        {
            ArgumentNullException.ThrowIfNull(sourceMapJson);

            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(sourceMapJson));
            return "//# sourceMappingURL=data:application/json;charset=utf-8;base64," + base64;
        }

        /// <summary>
        /// Build Source Map v3 JSON for <paramref name="outputFile"/>, reporting
        /// <see cref="MessageCode.EmitterSourceMapInvalidMapping"/> for out-of-range segments and
        /// <see cref="MessageCode.EmitterSourceMapGenerationFailed"/> if JSON generation throws.
        /// Returns null when tracking was not enabled (empty <c>SourceMap()</c>) or generation failed.
        /// </summary>
        public static string? TryGenerateSourceMapJson(
            PHPOutputFile outputFile,
            SourceMapOptions options,
            DiagnosticBag diagnostics)
        {
            ArgumentNullException.ThrowIfNull(outputFile);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(diagnostics);

            if (outputFile.SourceMapCollector == null)
            {
                return null;
            }

            ReportInvalidMappings(outputFile, diagnostics);

            try
            {
                var json = outputFile.SourceMap(
                    options.IncludeSourcesContent,
                    options.SourceContentProvider);
                return string.IsNullOrEmpty(json) ? null : json;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                diagnostics.AddWarning(
                    MessageCode.EmitterSourceMapGenerationFailed,
                    outputFile.SourceFileAst?.Identifier ?? "",
                    0,
                    0,
                    outputFile.OutputFilePath,
                    ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Append an external-file or inline <c>sourceMappingURL</c> comment to
        /// <paramref name="phpContent"/> according to <paramref name="options"/>.
        /// </summary>
        public static string ApplySourceMappingComment(
            string phpContent,
            string phpFilePath,
            string sourceMapJson,
            SourceMapOptions options)
        {
            ArgumentNullException.ThrowIfNull(phpContent);
            ArgumentException.ThrowIfNullOrWhiteSpace(phpFilePath);
            ArgumentNullException.ThrowIfNull(sourceMapJson);
            ArgumentNullException.ThrowIfNull(options);

            if (options.InlineSourceMap)
            {
                return AppendInlineSourceMap(phpContent, sourceMapJson);
            }

            if (!options.AppendSourceMappingUrl)
            {
                return phpContent;
            }

            var mapFileName = Path.GetFileName(phpFilePath) + ".map";
            return AppendSourceMappingUrl(phpContent, mapFileName);
        }

        /// <summary>
        /// Generate maps for every output file that has tracking data, mutate
        /// <see cref="PHPOutputFile.GeneratedContent"/> with the URL comment when requested, and
        /// write companion <c>.map</c> files (unless inlining). PHP path is
        /// <c>outputDirectory</c> + <c>OutputFilePath</c> when the output path is relative.
        /// </summary>
        public static void WriteAllSourceMaps(
            IEnumerable<PHPOutputFile> outputFiles,
            string outputDirectory,
            SourceMapOptions options,
            DiagnosticBag diagnostics)
        {
            ArgumentNullException.ThrowIfNull(outputFiles);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(diagnostics);

            if (!options.Enabled)
            {
                return;
            }

            foreach (var outputFile in outputFiles)
            {
                var json = TryGenerateSourceMapJson(outputFile, options, diagnostics);
                if (json == null)
                {
                    continue;
                }

                if (options.ValidateSourceMaps && outputFile.GeneratedContent != null)
                {
                    // Reuse the PHP from the single Generate() call. Do not re-emit —
                    // a second Generate() would Reset and re-feed the collector.
                    SourceMapValidator.Validate(
                        json,
                        outputFile.GeneratedContent,
                        diagnostics,
                        sourceContentProvider: options.SourceContentProvider);
                }

                var phpPath = ResolvePhpPath(outputDirectory, outputFile);
                if (outputFile.GeneratedContent != null)
                {
                    outputFile.GeneratedContent = ApplySourceMappingComment(
                        outputFile.GeneratedContent,
                        phpPath,
                        json,
                        options);
                }

                if (!options.InlineSourceMap)
                {
                    WriteSourceMapFile(phpPath, json, diagnostics);
                }
            }
        }

        private static string AppendInlineSourceMap(string phpContent, string sourceMapJson)
        {
            if (phpContent.Contains("sourceMappingURL=", StringComparison.Ordinal))
            {
                return phpContent;
            }

            var comment = CreateInlineSourceMap(sourceMapJson) + "\n";
            if (phpContent.Length == 0 || phpContent.EndsWith('\n'))
            {
                return phpContent + comment;
            }

            return phpContent + "\n" + comment;
        }

        private static string ResolvePhpPath(string outputDirectory, PHPOutputFile outputFile)
        {
            var relativePath = outputFile.OutputFilePath.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relativePath))
            {
                return Path.GetFullPath(relativePath);
            }

            return Path.GetFullPath(Path.Combine(outputDirectory, relativePath));
        }

        private static void ReportInvalidMappings(PHPOutputFile outputFile, DiagnosticBag diagnostics)
        {
            var collector = outputFile.SourceMapCollector;
            if (collector == null)
            {
                return;
            }

            var sources = collector.GetSourceFiles();
            var names = collector.GetNames();
            var fileName = outputFile.SourceFileAst?.Identifier ?? outputFile.OutputFilePath;

            foreach (var mapping in collector.GetMappings())
            {
                var sourceOutOfRange = mapping.SourceIndex < 0 || mapping.SourceIndex >= sources.Count;
                var nameOutOfRange = mapping.NameIndex is int nameIndex
                    && (nameIndex < 0 || nameIndex >= names.Count);
                var positionInvalid = mapping.GeneratedLine < 0
                    || mapping.GeneratedColumn < 0
                    || mapping.OriginalLine < 0
                    || mapping.OriginalColumn < 0;

                if (!sourceOutOfRange && !nameOutOfRange && !positionInvalid)
                {
                    continue;
                }

                diagnostics.AddWarning(
                    MessageCode.EmitterSourceMapInvalidMapping,
                    fileName,
                    0,
                    0,
                    outputFile.OutputFilePath,
                    mapping.GeneratedLine,
                    mapping.GeneratedColumn,
                    mapping.OriginalLine,
                    mapping.OriginalColumn);
            }
        }
    }
}
