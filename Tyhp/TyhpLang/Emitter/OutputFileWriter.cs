using System.Text;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.TyhpLang.Emitter
{
    public sealed class WriteResult
    {
        public int FilesWritten { get; set; }
        public int FilesSkipped { get; set; }
        public int DirectoriesCreated { get; set; }
        public List<string> WrittenPaths { get; } = [];
        public List<(string Path, string Reason)> SkippedPaths { get; } = [];
        public List<(string Path, string Reason)> Conflicts { get; } = [];
    }

    /// <summary>
    /// Minimal disk writer for generated PHP output files.
    /// Superseded by Story 10's <c>OutputWriterService</c>.
    /// </summary>
    public sealed class OutputFileWriter
    {
        private readonly EmitContext _context;

        public OutputFileWriter(EmitContext context)
        {
            this._context = context;
        }

        public WriteResult WriteAll(IReadOnlyList<PHPOutputFile> outputFiles)
        {
            // PLACEHOLDER_STORY_10: Clean build mode
            // PLACEHOLDER_STORY_10: Dry run mode

            var result = new WriteResult();
            if (outputFiles.Count == 0)
            {
                return result;
            }

            var filesToWrite = this.ResolveOutputFiles(outputFiles, result);
            var directoriesCreated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            foreach (var outputFile in filesToWrite)
            {
                if (!this.TryGetOutputPath(outputFile, out var fullPath))
                {
                    result.FilesSkipped += 1;
                    continue;
                }

                if (!HasWritableContent(outputFile))
                {
                    this._context.Diagnostics.AddWarning(
                        MessageCode.EmitterEmptyOutputFile,
                        outputFile.SourceFileAst?.Identifier ?? "",
                        0,
                        0,
                        outputFile.OutputFilePath);
                    result.FilesSkipped += 1;
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

                    File.WriteAllText(fullPath, outputFile.GeneratedContent!, utf8NoBom);
                    result.WrittenPaths.Add(fullPath);
                    result.FilesWritten += 1;

                    // Sourcemap companions are written by OutputWriterService via SourceMapWriter
                    // (Story 17). This class is the legacy Story 09 writer and is not on the
                    // build pipeline.
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    this._context.Diagnostics.AddError(
                        MessageCode.EmitterWriteError,
                        outputFile.SourceFileAst?.Identifier ?? "",
                        0,
                        0,
                        outputFile.OutputFilePath,
                        ex.Message);
                    result.FilesSkipped += 1;
                }
            }

            return result;
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

                if (group.Any(f => f.IsPSR4ObjectDeclaration))
                {
                    this.ReportPathConflict(group, outputPath, result, resolved);
                    continue;
                }

                var merged = group[0];
                for (var i = 1; i < group.Count; i++)
                {
                    merged.Merge(group[i], this._context);
                }

                merged.Generate(this._context);

                this._context.Diagnostics.AddWarning(
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
                this._context.Diagnostics.AddError(
                    MessageCode.EmitterOutputPathConflict,
                    duplicate.SourceFileAst?.Identifier ?? winner.SourceFileAst?.Identifier ?? "",
                    0,
                    0,
                    outputPath);
                result.Conflicts.Add((outputPath, "duplicate PSR-4 output path"));
                result.FilesSkipped += 1;
            }
        }

        private bool TryGetOutputPath(PHPOutputFile outputFile, out string fullPath)
        {
            fullPath = "";

            try
            {
                var relativePath = outputFile.OutputFilePath.Replace('/', Path.DirectorySeparatorChar);
                fullPath = Path.GetFullPath(relativePath);

                if (fullPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    throw new ArgumentException("Path contains invalid characters.");
                }

                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            {
                this._context.Diagnostics.AddError(
                    MessageCode.EmitterInvalidOutputPath,
                    outputFile.SourceFileAst?.Identifier ?? "",
                    0,
                    0,
                    outputFile.OutputFilePath,
                    ex.Message);
                return false;
            }
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
