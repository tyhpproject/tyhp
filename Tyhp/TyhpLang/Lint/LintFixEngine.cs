using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Lint.Fixes;

namespace Tyhp.TyhpLang.Lint
{
    /// <summary>
    /// Orchestrates applying registered <see cref="ILintFix"/> implementations to
    /// diagnostics in a <see cref="CompilationResult"/>. Used by <c>tyhp lint --fix</c>.
    /// </summary>
    /// <remarks>
    /// One instance spans a whole lint run (all fix iterations) so each modified file is backed up
    /// exactly once, preserving the pre-fix original. Not thread-safe.
    /// </remarks>
    public sealed class LintFixEngine
    {
        private readonly IReadOnlyDictionary<MessageCode, ILintFix> _fixesByCode;
        private readonly Dictionary<string, string> _backupsByFile = new(StringComparer.Ordinal);

        /// <summary>
        /// Creates an engine with the given registered fixes.
        /// </summary>
        /// <exception cref="ArgumentException">Two fixes target the same <see cref="MessageCode"/>.</exception>
        public LintFixEngine(IEnumerable<ILintFix> registeredFixes)
        {
            ArgumentNullException.ThrowIfNull(registeredFixes);

            var map = new Dictionary<MessageCode, ILintFix>();
            foreach (var fix in registeredFixes)
            {
                ArgumentNullException.ThrowIfNull(fix);
                if (!map.TryAdd(fix.TargetCode, fix))
                {
                    throw new ArgumentException(
                        $"Two lint fixes target {fix.TargetCode}: "
                            + $"'{map[fix.TargetCode].Description}' and '{fix.Description}'.",
                        nameof(registeredFixes));
                }
            }

            this._fixesByCode = map;
        }

        /// <summary>
        /// Creates an engine with the default placeholder fix implementations.
        /// </summary>
        public static LintFixEngine CreateDefault()
            => new(
            [
                new AddMissingTypeAnnotationFix(),
                new AddMissingImportFix(),
                new RemoveUnusedImportFix(),
                new SortImportsFix(),
            ]);

        /// <summary>
        /// Message codes for which a fix is registered.
        /// </summary>
        public IReadOnlyCollection<MessageCode> RegisteredCodes => this._fixesByCode.Keys.ToArray();

        /// <summary>
        /// Apply all applicable fixes for diagnostics in <paramref name="result"/>.
        /// Successful fixes write the modified source (after creating a timestamped backup).
        /// </summary>
        /// <param name="result">Compilation result whose diagnostics are considered.</param>
        /// <param name="previouslyApplied">
        /// Locations successfully fixed in earlier iterations. Encountering the same location
        /// again sets <see cref="LintFixPassResult.LoopDetected"/>.
        /// </param>
        public LintFixPassResult ApplyFixes(
            CompilationResult result,
            IReadOnlySet<LintFixLocationKey>? previouslyApplied = null)
        {
            ArgumentNullException.ThrowIfNull(result);

            previouslyApplied ??= new HashSet<LintFixLocationKey>();
            var applications = new List<LintFixApplication>();
            // Cache source text per file so multiple diagnostics in one file share one read;
            // successful writes update the cache so subsequent fixes in the same pass see edits.
            // Ordinal (not OrdinalIgnoreCase): on case-sensitive filesystems paths differing only
            // in case are distinct files and must not share cached text.
            var sourceCache = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var diagnostic in result.Diagnostics.All)
            {
                if (!this._fixesByCode.TryGetValue(diagnostic.Code, out var fix))
                {
                    continue;
                }

                var location = new LintFixLocationKey(
                    diagnostic.FileName ?? string.Empty,
                    diagnostic.Code,
                    diagnostic.Line,
                    diagnostic.Column);

                if (previouslyApplied.Contains(location))
                {
                    return new LintFixPassResult
                    {
                        Applications = applications,
                        LoopDetected = true,
                        LoopLocation = location,
                    };
                }

                if (String.IsNullOrWhiteSpace(diagnostic.FileName))
                {
                    applications.Add(new LintFixApplication
                    {
                        Diagnostic = diagnostic,
                        Fix = fix,
                        Result = LintFixResult.Failed("Diagnostic has no file path"),
                    });
                    continue;
                }

                if (!TryGetSourceText(diagnostic.FileName, sourceCache, out var sourceText, out var readError))
                {
                    applications.Add(new LintFixApplication
                    {
                        Diagnostic = diagnostic,
                        Fix = fix,
                        Result = LintFixResult.Failed(readError ?? "Could not read source file"),
                    });
                    continue;
                }

                var fixResult = fix.Apply(sourceText, diagnostic);
                string? backupPath = null;

                if (fixResult.Success
                    && !String.IsNullOrEmpty(fixResult.ModifiedSourceText)
                    && !String.Equals(fixResult.ModifiedSourceText, sourceText, StringComparison.Ordinal))
                {
                    try
                    {
                        backupPath = this.EnsureBackup(diagnostic.FileName);
                        File.WriteAllText(diagnostic.FileName, fixResult.ModifiedSourceText);
                        sourceCache[diagnostic.FileName] = fixResult.ModifiedSourceText;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                    {
                        fixResult = LintFixResult.Failed(ex.Message);
                        backupPath = null;
                    }
                }

                applications.Add(new LintFixApplication
                {
                    Diagnostic = diagnostic,
                    Fix = fix,
                    Result = fixResult,
                    BackupPath = backupPath,
                });
            }

            return new LintFixPassResult
            {
                Applications = applications,
                LoopDetected = false,
            };
        }

        /// <summary>
        /// Creates a timestamped backup: <c>{file}.bak.{yyyyMMdd_HHmmss}</c>. Existing backups are
        /// never overwritten — a counter is appended when the path is taken (timestamps have
        /// one-second resolution, so several fixes can land in the same second).
        /// </summary>
        public static string CreateBackup(string filePath)
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = $"{filePath}.bak.{stamp}";
            for (var suffix = 2; File.Exists(backupPath); suffix++)
            {
                backupPath = $"{filePath}.bak.{stamp}_{suffix}";
            }

            File.Copy(filePath, backupPath, overwrite: false);
            return backupPath;
        }

        /// <summary>
        /// Returns the backup for <paramref name="filePath"/>, creating it on first use so the
        /// backup holds the original content rather than a partially fixed version.
        /// </summary>
        private string EnsureBackup(string filePath)
        {
            if (this._backupsByFile.TryGetValue(filePath, out var existing))
            {
                return existing;
            }

            var backupPath = CreateBackup(filePath);
            this._backupsByFile[filePath] = backupPath;
            return backupPath;
        }

        private static bool TryGetSourceText(
            string filePath,
            Dictionary<string, string> cache,
            out string sourceText,
            out string? error)
        {
            if (cache.TryGetValue(filePath, out var cached))
            {
                sourceText = cached;
                error = null;
                return true;
            }

            try
            {
                if (!File.Exists(filePath))
                {
                    sourceText = string.Empty;
                    error = $"File not found: '{filePath}'";
                    return false;
                }

                sourceText = File.ReadAllText(filePath);
                cache[filePath] = sourceText;
                error = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                sourceText = string.Empty;
                error = ex.Message;
                return false;
            }
        }
    }
}
