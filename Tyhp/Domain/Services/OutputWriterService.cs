using System.Text;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Emitter.SourceMap;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Authoritative disk writer for compiled PHP output and companion sourcemap files.
    /// </summary>
    public sealed class OutputWriterService
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        private readonly Project _project;
        private readonly DiagnosticBag _diagnostics;
        private readonly EmitContext? _emitContext;

        public OutputWriterService(Project project, DiagnosticBag diagnostics, EmitContext? emitContext = null)
        {
            this._project = project;
            this._diagnostics = diagnostics;
            this._emitContext = emitContext;
        }

        /// <summary>
        /// Writes all output files to disk, detecting path conflicts and optionally performing a dry run.
        /// </summary>
        public WriteResult WriteAll(IReadOnlyList<PHPOutputFile> outputFiles, bool dryRun = false)
        {
            var result = new WriteResult();
            if (outputFiles.Count == 0)
            {
                return result;
            }

            var filesToWrite = this.ResolveOutputFiles(outputFiles, result);
            var directoriesCreated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sourceMapOptions = this.CreateSourceMapOptions();

            foreach (var outputFile in filesToWrite)
            {
                if (!this.TryGetOutputPath(outputFile, out var fullPath))
                {
                    result.FilesSkipped += 1;
                    result.SkippedPaths.Add((outputFile.OutputFilePath, "invalid output path"));
                    continue;
                }

                if (!HasWritableContent(outputFile))
                {
                    this._diagnostics.AddWarning(
                        MessageCode.EmitterEmptyOutputFile,
                        outputFile.SourceFileAst?.Identifier ?? "",
                        0,
                        0,
                        outputFile.OutputFilePath);
                    result.FilesSkipped += 1;
                    result.SkippedPaths.Add((fullPath, "empty output"));
                    continue;
                }

                var content = outputFile.GeneratedContent!;
                var sourcemapJson = sourceMapOptions.Enabled
                    ? SourceMapWriter.TryGenerateSourceMapJson(
                        outputFile,
                        sourceMapOptions,
                        this._diagnostics)
                    : null;
                if (sourcemapJson != null)
                {
                    if (sourceMapOptions.ValidateSourceMaps)
                    {
                        // Validate against the Generate() PHP, before the write-time
                        // sourceMappingURL comment is appended (that extra line is not
                        // represented in mappings).
                        SourceMapValidator.Validate(
                            sourcemapJson,
                            content,
                            this._diagnostics,
                            sourceContentProvider: sourceMapOptions.SourceContentProvider);
                    }

                    content = SourceMapWriter.ApplySourceMappingComment(
                        content,
                        fullPath,
                        sourcemapJson,
                        sourceMapOptions);
                }

                if (dryRun)
                {
                    result.FilesWritten += 1;
                    result.WrittenPaths.Add(fullPath);
                    if (this._project.Build.Verbose)
                    {
                        Message.Display("CLI_DryRunWouldWrite", fullPath, content.Length);
                        if (sourcemapJson != null)
                        {
                            Message.Display("CLI_DryRunWouldWrite", fullPath + ".map", sourcemapJson.Length);
                        }
                    }

                    continue;
                }

                try
                {
                    var directory = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        if (!Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                            if (directoriesCreated.Add(directory))
                            {
                                result.DirectoriesCreated += 1;
                            }
                        }
                    }

                    File.WriteAllText(fullPath, content, Utf8NoBom);
                    result.WrittenPaths.Add(fullPath);
                    result.FilesWritten += 1;

                    if (sourcemapJson != null && !sourceMapOptions.InlineSourceMap)
                    {
                        SourceMapWriter.WriteSourceMapFile(fullPath, sourcemapJson, this._diagnostics);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    this._diagnostics.AddError(
                        MessageCode.BuildFileWriteError,
                        outputFile.SourceFileAst?.Identifier ?? "",
                        0,
                        0,
                        fullPath,
                        ex.Message);
                    result.FilesSkipped += 1;
                    result.SkippedPaths.Add((fullPath, ex.Message));
                }
            }

            return result;
        }

        /// <summary>
        /// Computes the absolute on-disk path for an output file.
        /// </summary>
        public string ComputeOutputPath(PHPOutputFile outputFile, Project project)
        {
            var projectPath = Path.GetFullPath(project.GetProjectPath());
            var relativePath = outputFile.OutputFilePath.Replace('/', Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(relativePath))
            {
                return Path.GetFullPath(relativePath);
            }

            return Path.GetFullPath(Path.Combine(projectPath, relativePath));
        }

        private bool TryGetOutputPath(PHPOutputFile outputFile, out string fullPath)
        {
            fullPath = "";

            try
            {
                fullPath = this.ComputeOutputPath(outputFile, this._project);

                if (fullPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    throw new ArgumentException("Path contains invalid characters.");
                }

                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            {
                this._diagnostics.AddError(
                    MessageCode.EmitterInvalidOutputPath,
                    outputFile.SourceFileAst?.Identifier ?? "",
                    0,
                    0,
                    outputFile.OutputFilePath,
                    ex.Message);
                return false;
            }
        }

        private List<PHPOutputFile> ResolveOutputFiles(
            IReadOnlyList<PHPOutputFile> outputFiles,
            WriteResult result)
        {
            var ordered = outputFiles
                .OrderBy(f => f.SourceFileAst?.Identifier ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.OutputFilePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var resolved = new List<PHPOutputFile>();
            var byPath = new Dictionary<string, List<PHPOutputFile>>(StringComparer.OrdinalIgnoreCase);

            foreach (var outputFile in ordered)
            {
                if (!byPath.TryGetValue(outputFile.OutputFilePath, out var group))
                {
                    group = [];
                    byPath[outputFile.OutputFilePath] = group;
                }

                group.Add(outputFile);
            }

            foreach (var (outputPath, group) in byPath.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Count == 1)
                {
                    resolved.Add(group[0]);
                    continue;
                }

                if (group.Any(f => f.IsPSR4ObjectDeclaration) || this._emitContext == null)
                {
                    this.ReportPathConflict(group, outputPath, result, resolved);
                    continue;
                }

                var merged = group[0];
                for (var i = 1; i < group.Count; i++)
                {
                    merged.Merge(group[i], this._emitContext);
                }

                // Merge() replaces any populated collector with a fresh one. Generate() also
                // Resets at start, so this re-emit cannot double-track from the pre-merge cursor.
                merged.Generate(this._emitContext);

                this._diagnostics.AddWarning(
                    MessageCode.EmitterMergeConflict,
                    merged.SourceFileAst?.Identifier ?? "",
                    0,
                    0,
                    outputPath,
                    "merged duplicate output paths during write");
                resolved.Add(merged);
            }

            return resolved;
        }

        private void ReportPathConflict(
            List<PHPOutputFile> group,
            string outputPath,
            WriteResult result,
            List<PHPOutputFile> resolved)
        {
            var winner = group[0];
            resolved.Add(winner);

            foreach (var duplicate in group.Skip(1))
            {
                this._diagnostics.AddError(
                    MessageCode.BuildOutputPathConflict,
                    duplicate.SourceFileAst?.Identifier ?? winner.SourceFileAst?.Identifier ?? "",
                    0,
                    0,
                    outputPath);
                result.Conflicts.Add((outputPath, "duplicate output path"));
                result.FilesSkipped += 1;
                result.SkippedPaths.Add((outputPath, "path conflict"));
            }
        }

        private SourceMapOptions CreateSourceMapOptions()
        {
            var projectPath = Path.GetFullPath(this._project.GetProjectPath());
            return new SourceMapOptions
            {
                Enabled = this._project.Build.GenerateSourcemap,
                IncludeSourcesContent = this._project.Build.SourceMapIncludeContent,
                AppendSourceMappingUrl = true,
                InlineSourceMap = false,
                ValidateSourceMaps = this._project.Build.GenerateSourcemap,
                SourceContentProvider = SourceMapWriter.CreateFileContentProvider(projectPath),
            };
        }

        private static bool HasWritableContent(PHPOutputFile outputFile)
        {
            if (string.IsNullOrWhiteSpace(outputFile.GeneratedContent))
            {
                return false;
            }

            if (outputFile.Statements.Count > 0 || outputFile.IsPSR4ObjectDeclaration || outputFile.IsEntryPoint)
            {
                return true;
            }

            return false;
        }
    }
}
