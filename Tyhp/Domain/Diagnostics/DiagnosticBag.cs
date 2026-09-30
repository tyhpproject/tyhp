using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Domain.Diagnostics
{
    /// <summary>
    /// Thread-safe collection for accumulating diagnostics across all compiler phases.
    /// Supports concurrent addition from multiple threads during parallel parsing.
    /// Identical diagnostics (same severity, code, location, and format params) are
    /// de-duplicated so ANTLR recovery / visitor double-visits do not inflate the count.
    /// Warning-severity diagnostics whose codes are listed in
    /// <c>tyhp.json</c> <c>suppressWarnings</c> are dropped at add time.
    /// </summary>
    public class DiagnosticBag : IEnumerable<IDiagnostic>
    {
        private readonly ConcurrentBag<IDiagnostic> _diagnostics = new();
        private readonly ConcurrentDictionary<DiagnosticIdentity, byte> _seen = new();
        private readonly ConcurrentDictionary<DiagnosticIdentity, byte> _retracted = new();
        private readonly IReadOnlySet<MessageCode> _suppressedWarnings;
        private IReadOnlyList<IDiagnostic>? _cachedErrors;
        private IReadOnlyList<IDiagnostic>? _cachedWarnings;
        private IReadOnlyList<IDiagnostic>? _cachedInfos;
        private IReadOnlyList<IDiagnostic>? _cachedAll;

        /// <summary>
        /// Warning codes dropped by bags constructed without an explicit suppress list.
        /// Updated when a <c>tyhp.json</c> project is loaded.
        /// </summary>
        public static IReadOnlySet<MessageCode> DefaultSuppressedWarnings { get; set; } =
            FrozenSet<MessageCode>.Empty;

        /// <summary>
        /// Initializes an empty bag. When <paramref name="suppressedWarnings"/> is omitted,
        /// <see cref="DefaultSuppressedWarnings"/> is used. Errors and info are never dropped.
        /// </summary>
        public DiagnosticBag(IReadOnlySet<MessageCode>? suppressedWarnings = null)
        {
            this._suppressedWarnings = suppressedWarnings ?? DefaultSuppressedWarnings;
        }

        /// <summary>
        /// Adds a single diagnostic to the bag. A diagnostic that is identical to one already
        /// present (same severity, code, file, span, and format params) is ignored.
        /// Warning-severity diagnostics whose code is in the suppressed-warning list are dropped.
        /// </summary>
        /// <param name="diagnostic">The diagnostic to add.</param>
        public void Add(IDiagnostic diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);

            if (diagnostic.Severity == DiagnosticSeverity.Warning
                && this._suppressedWarnings.Count > 0
                && this._suppressedWarnings.Contains(diagnostic.Code))
            {
                return;
            }

            var identity = DiagnosticIdentity.From(diagnostic);
            if (!this._seen.TryAdd(identity, 0))
            {
                return;
            }

            this.InvalidateCache();
            this._diagnostics.Add(diagnostic);
        }

        /// <summary>
        /// Drops diagnostics that match <paramref name="predicate"/> from later reads.
        /// An overlay <c>omit</c> uses this to retract an include-time
        /// <see cref="MessageCode.TyhpdefDuplicateFqnAcrossPackages"/> for the omitted name.
        /// </summary>
        public void RetractWhere(Func<IDiagnostic, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);

            var changed = false;
            foreach (var diagnostic in this._diagnostics)
            {
                if (!predicate(diagnostic))
                {
                    continue;
                }

                var identity = DiagnosticIdentity.From(diagnostic);
                if (!this._retracted.TryAdd(identity, 0))
                {
                    continue;
                }

                this._seen.TryRemove(identity, out _);
                changed = true;
            }

            if (changed)
            {
                this.InvalidateCache();
            }
        }

        /// <summary>
        /// Adds multiple diagnostics to the bag.
        /// </summary>
        /// <param name="diagnostics">The diagnostics to add.</param>
        public void AddRange(IEnumerable<IDiagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                this.Add(diagnostic);
            }
        }

        /// <summary>
        /// Adds an error diagnostic to the bag.
        /// </summary>
        /// <param name="code">The message code identifying the error type.</param>
        /// <param name="fileName">The source file where the error occurred.</param>
        /// <param name="line">The line number where the error occurred (1-indexed).</param>
        /// <param name="column">The column position where the error occurred (0-indexed).</param>
        /// <param name="formatParams">Parameters for formatting the localized error message.</param>
        public void AddError(
            MessageCode code,
            string fileName,
            int line,
            int column,
            params object[] formatParams)
        {
            this.Add(Diagnostic.Error(code, fileName, line, column, formatParams));
        }

        /// <summary>
        /// Adds a warning diagnostic to the bag.
        /// </summary>
        /// <param name="code">The message code identifying the warning type.</param>
        /// <param name="fileName">The source file where the warning occurred.</param>
        /// <param name="line">The line number where the warning occurred (1-indexed).</param>
        /// <param name="column">The column position where the warning occurred (0-indexed).</param>
        /// <param name="formatParams">Parameters for formatting the localized warning message.</param>
        public void AddWarning(
            MessageCode code,
            string fileName,
            int line,
            int column,
            params object[] formatParams)
        {
            this.Add(Diagnostic.Warning(code, fileName, line, column, formatParams));
        }

        /// <summary>
        /// Adds an info diagnostic to the bag.
        /// </summary>
        /// <param name="code">The message code identifying the info message type.</param>
        /// <param name="fileName">The source file where the info message applies.</param>
        /// <param name="line">The line number where the info message applies (1-indexed).</param>
        /// <param name="column">The column position where the info message applies (0-indexed).</param>
        /// <param name="formatParams">Parameters for formatting the localized info message.</param>
        public void AddInfo(
            MessageCode code,
            string fileName,
            int line,
            int column,
            params object[] formatParams)
        {
            this.Add(Diagnostic.Info(code, fileName, line, column, formatParams));
        }

        /// <summary>
        /// Gets whether the bag contains any error-severity diagnostics.
        /// </summary>
        public bool HasErrors => this.Errors.Count > 0;

        /// <summary>
        /// Gets whether the bag contains any warning-severity diagnostics.
        /// </summary>
        public bool HasWarnings => this.Warnings.Count > 0;

        /// <summary>
        /// Gets the number of error diagnostics.
        /// </summary>
        public int ErrorCount => this.Errors.Count;

        /// <summary>
        /// Counts error-severity diagnostics for a single source file without building the sorted
        /// <see cref="Errors"/> view. Used by parse-time AST-cache gating (compare before/after a
        /// parse) so a recoverable syntax error is never stored without its diagnostics.
        /// </summary>
        /// <param name="fileName">The source file path as recorded on diagnostics for that parse.</param>
        public int CountErrorsForFile(string fileName)
        {
            var count = 0;
            foreach (var diagnostic in this._diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error
                    && this.IsActive(diagnostic)
                    && string.Equals(diagnostic.FileName, fileName, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Gets the number of warning diagnostics.
        /// </summary>
        public int WarningCount => this.Warnings.Count;

        /// <summary>
        /// Gets the number of info diagnostics (includes hints).
        /// </summary>
        public int InfoCount => this.Infos.Count;

        /// <summary>
        /// Gets a read-only list of error diagnostics, sorted by file name, line, and column.
        /// Results are cached until new diagnostics are added.
        /// </summary>
        public IReadOnlyList<IDiagnostic> Errors
        {
            get
            {
                if (this._cachedErrors == null)
                {
                    this._cachedErrors = this._diagnostics
                        .Where(this.IsActive)
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .OrderBy(d => d.FileName, StringComparer.Ordinal)
                        .ThenBy(d => d.Line)
                        .ThenBy(d => d.Column)
                        .ToList();
                }
                return this._cachedErrors;
            }
        }

        /// <summary>
        /// Gets a read-only list of warning diagnostics, sorted by file name, line, and column.
        /// Results are cached until new diagnostics are added.
        /// </summary>
        public IReadOnlyList<IDiagnostic> Warnings
        {
            get
            {
                if (this._cachedWarnings == null)
                {
                    this._cachedWarnings = this._diagnostics
                        .Where(this.IsActive)
                        .Where(d => d.Severity == DiagnosticSeverity.Warning)
                        .OrderBy(d => d.FileName, StringComparer.Ordinal)
                        .ThenBy(d => d.Line)
                        .ThenBy(d => d.Column)
                        .ToList();
                }
                return this._cachedWarnings;
            }
        }

        /// <summary>
        /// Gets a read-only list of info and hint diagnostics, sorted by file name, line, and column.
        /// Results are cached until new diagnostics are added.
        /// </summary>
        private IReadOnlyList<IDiagnostic> Infos
        {
            get
            {
                if (this._cachedInfos == null)
                {
                    this._cachedInfos = this._diagnostics
                        .Where(this.IsActive)
                        .Where(d => d.Severity == DiagnosticSeverity.Info || d.Severity == DiagnosticSeverity.Hint)
                        .OrderBy(d => d.FileName, StringComparer.Ordinal)
                        .ThenBy(d => d.Line)
                        .ThenBy(d => d.Column)
                        .ToList();
                }
                return this._cachedInfos;
            }
        }

        /// <summary>
        /// Gets all diagnostics ordered by file name, then line number, then column.
        /// Results are cached until new diagnostics are added.
        /// </summary>
        public IReadOnlyList<IDiagnostic> All
        {
            get
            {
                if (this._cachedAll == null)
                {
                    this._cachedAll = this._diagnostics
                        .Where(this.IsActive)
                        .OrderBy(d => d.FileName, StringComparer.Ordinal)
                        .ThenBy(d => d.Line)
                        .ThenBy(d => d.Column)
                        .ToList();
                }
                return this._cachedAll;
            }
        }

        private static readonly ConsoleDiagnosticFormatter _defaultFormatter = new();

        /// <summary>
        /// Displays all diagnostics using the specified formatter.
        /// </summary>
        /// <param name="formatter">The formatter to use, or null for the default console formatter.</param>
        public void DisplayAll(IDiagnosticFormatter? formatter = null)
        {
            formatter ??= _defaultFormatter;

            foreach (var diagnostic in this.All)
            {
                formatter.Format(diagnostic);
            }

            formatter.FormatSummary(this);
        }

        /// <summary>
        /// Invalidates all cached sorted lists.
        /// </summary>
        private void InvalidateCache()
        {
            this._cachedErrors = null;
            this._cachedWarnings = null;
            this._cachedInfos = null;
            this._cachedAll = null;
        }

        private bool IsActive(IDiagnostic diagnostic)
        {
            return this._retracted.IsEmpty
                || !this._retracted.ContainsKey(DiagnosticIdentity.From(diagnostic));
        }

        /// <inheritdoc/>
        public IEnumerator<IDiagnostic> GetEnumerator()
        {
            if (this._retracted.IsEmpty)
            {
                return this._diagnostics.GetEnumerator();
            }

            return this._diagnostics.Where(this.IsActive).GetEnumerator();
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        /// <summary>
        /// Identity used to de-duplicate diagnostics that report the same finding more than once.
        /// </summary>
        private readonly record struct DiagnosticIdentity(
            DiagnosticSeverity Severity,
            MessageCode Code,
            string FileName,
            int Line,
            int Column,
            int? EndLine,
            int? EndColumn,
            string FormatParamsKey)
        {
            public static DiagnosticIdentity From(IDiagnostic diagnostic)
            {
                return new DiagnosticIdentity(
                    diagnostic.Severity,
                    diagnostic.Code,
                    diagnostic.FileName ?? string.Empty,
                    diagnostic.Line,
                    diagnostic.Column,
                    diagnostic.EndLine,
                    diagnostic.EndColumn,
                    FormatKey(diagnostic.FormatParams));
            }

            private static string FormatKey(object[]? formatParams)
            {
                if (formatParams == null || formatParams.Length == 0)
                {
                    return string.Empty;
                }

                var builder = new StringBuilder();
                for (var i = 0; i < formatParams.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append('\u001f');
                    }

                    builder.Append(formatParams[i]?.ToString() ?? string.Empty);
                }

                return builder.ToString();
            }
        }
    }
}
