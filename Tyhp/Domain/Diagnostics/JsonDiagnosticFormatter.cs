using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tyhp.Domain.Diagnostics
{
    /// <summary>
    /// Diagnostic formatter that emits a single JSON document for lint/CI consumption
    /// when <c>--format=json</c> is selected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Output is one complete JSON object (not NDJSON). Schema version <c>1.0</c>:
    /// </para>
    /// <code>
    /// {
    ///   "version": "1.0",
    ///   "tool": { "name": "tyhp", "version": "805.0.0" },
    ///   "diagnostics": [
    ///     {
    ///       "severity": "error",
    ///       "code": "TYHP4002",
    ///       "message": "...",
    ///       "file": "src/Models/User.tyhp",
    ///       "range": {
    ///         "start": { "line": 41, "column": 8 },
    ///         "end": { "line": 41, "column": 25 }
    ///       },
    ///       "labels": [
    ///         {
    ///           "message": "defined here",
    ///           "file": "src/Models/User.tyhp",
    ///           "range": {
    ///             "start": { "line": 10, "column": 4 },
    ///             "end": { "line": 10, "column": 12 }
    ///           }
    ///         }
    ///       ],
    ///       "help": "consider converting the value",
    ///       "note": "expected `int`",
    ///       "suggestion": {
    ///         "file": "src/Models/User.tyhp",
    ///         "range": {
    ///           "start": { "line": 41, "column": 8 },
    ///           "end": { "line": 41, "column": 25 }
    ///         },
    ///         "replacement": "42",
    ///         "description": "replace with integer literal"
    ///       }
    ///     }
    ///   ],
    ///   "summary": {
    ///     "filesChecked": 42,
    ///     "file": "src/User.tyhp",
    ///     "errorCount": 3,
    ///     "warningCount": 7,
    ///     "infoCount": 12,
    ///     "durations": {
    ///       "parseMs": 1200,
    ///       "bindMs": 800,
    ///       "checkMs": 500,
    ///       "totalMs": 2500
    ///     }
    ///   }
    /// }
    /// </code>
    /// <para>
    /// Optional fields <c>labels</c>, <c>help</c>, <c>note</c>, and <c>suggestion</c> are omitted
    /// when absent (Story 14 Phase 2 schema extension).
    /// The optional <c>summary.file</c> property is present only in single-file mode
    /// (<c>tyhp lint --file=...</c>).
    /// </para>
    /// <para>
    /// <b>Coordinates:</b> Internal diagnostics use 1-based line / 0-based column.
    /// This formatter emits <b>0-based line</b> (<c>line − 1</c>, clamped at 0) and
    /// <b>0-based column</b> (passed through unchanged). When <see cref="IDiagnostic.EndLine"/>
    /// and <see cref="IDiagnostic.EndColumn"/> are both null, <c>end</c> equals <c>start</c>.
    /// </para>
    /// <para>
    /// Diagnostics are buffered in <see cref="Format"/> and the complete document is written
    /// only from <see cref="FormatSummary"/> so stdout stays clean JSON with no interleaved text.
    /// Call <see cref="SetContext"/> before <see cref="DiagnosticBag.DisplayAll"/> so summary
    /// timings and file counts are populated.
    /// </para>
    /// </remarks>
    public class JsonDiagnosticFormatter : IDiagnosticFormatter
    {
        private const string SchemaVersion = "1.0";
        private const string ToolName = "tyhp";

        private readonly TextWriter _writer;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly List<LintJsonDiagnostic> _diagnostics = new();
        private CompilationResult? _context;

        /// <summary>
        /// Initializes a new instance that writes compact JSON to console standard output (UTF-8).
        /// </summary>
        public JsonDiagnosticFormatter()
            : this(CreateUtf8Writer(), prettyPrint: false)
        {
        }

        /// <summary>
        /// Initializes a new instance that writes to the specified <see cref="TextWriter"/>.
        /// </summary>
        /// <param name="writer">Destination writer (UTF-8 recommended for stdout).</param>
        /// <param name="prettyPrint">When true, emits indented JSON; default is compact.</param>
        public JsonDiagnosticFormatter(TextWriter writer, bool prettyPrint = false)
        {
            this._writer = writer ?? throw new ArgumentNullException(nameof(writer));
            this._jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = prettyPrint,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
        }

        /// <summary>
        /// Creates a UTF-8 <see cref="TextWriter"/> wrapping <see cref="Console.OpenStandardOutput"/>.
        /// </summary>
        /// <remarks>
        /// Uses <c>leaveOpen: true</c> so finalizing/disposing this writer never closes the process
        /// stdout handle (the default <see cref="StreamWriter"/> behavior would).
        /// </remarks>
        private static TextWriter CreateUtf8Writer()
        {
            var utf8Stream = new StreamWriter(
                Console.OpenStandardOutput(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 1024,
                leaveOpen: true);
            utf8Stream.AutoFlush = true;
            return utf8Stream;
        }

        /// <inheritdoc/>
        public void SetContext(CompilationResult result)
        {
            this._context = result ?? throw new ArgumentNullException(nameof(result));
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Buffers the diagnostic only; nothing is written until <see cref="FormatSummary"/>.
        /// </remarks>
        public void Format(IDiagnostic diagnostic)
        {
            if (diagnostic is null)
            {
                return;
            }

            try
            {
                this._diagnostics.Add(ToJsonModel(diagnostic));
            }
            catch (Exception ex)
            {
                // stderr only — never write formatter failures to stdout (would corrupt JSON)
                Console.Error.WriteLine(
                    CLI.Message.Localize("CLI_FormatDiagnosticFailed", diagnostic.Code, ex.Message));
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Serializes the complete single-document JSON payload (buffered diagnostics + summary).
        /// </remarks>
        public void FormatSummary(DiagnosticBag bag)
        {
            if (bag is null)
            {
                Console.Error.WriteLine(
                    CLI.Message.Localize("CLI_FormatSummaryFailed", "DiagnosticBag was null."));
                return;
            }

            try
            {
                var parseMs = ToWholeMilliseconds(this._context?.ParseDuration ?? TimeSpan.Zero);
                var bindMs = ToWholeMilliseconds(this._context?.BindDuration ?? TimeSpan.Zero);
                var checkMs = ToWholeMilliseconds(this._context?.CheckDuration ?? TimeSpan.Zero);

                var output = new LintJsonOutput
                {
                    Version = SchemaVersion,
                    Tool = new LintJsonTool
                    {
                        Name = ToolName,
                        Version = GetToolVersion(),
                    },
                    Diagnostics = this._diagnostics.ToList(),
                    Summary = new LintJsonSummary
                    {
                        FilesChecked = ResolveFilesChecked(this._context),
                        File = this._context?.LintTargetFile,
                        ErrorCount = bag.ErrorCount,
                        WarningCount = bag.WarningCount,
                        InfoCount = bag.InfoCount,
                        Durations = new LintJsonDurations
                        {
                            ParseMs = parseMs,
                            BindMs = bindMs,
                            CheckMs = checkMs,
                            TotalMs = parseMs + bindMs + checkMs,
                        },
                    },
                };

                var json = JsonSerializer.Serialize(output, this._jsonOptions);
                this._writer.WriteLine(json);
                this._writer.Flush();
                this._diagnostics.Clear();
            }
            catch (Exception ex)
            {
                // stderr only — never write formatter failures to stdout (would corrupt JSON)
                Console.Error.WriteLine(CLI.Message.Localize("CLI_FormatSummaryFailed", ex.Message));
            }
        }

        private static LintJsonDiagnostic ToJsonModel(IDiagnostic diagnostic)
        {
            var model = new LintJsonDiagnostic
            {
                Severity = diagnostic.Severity.ToString().ToLowerInvariant(),
                Code = $"TYHP{(int)diagnostic.Code:D4}",
                Message = diagnostic.Message ?? string.Empty,
                File = diagnostic.FileName ?? string.Empty,
                Range = ToJsonRange(
                    diagnostic.Line,
                    diagnostic.Column,
                    diagnostic.EndLine,
                    diagnostic.EndColumn),
                Help = string.IsNullOrWhiteSpace(diagnostic.Help) ? null : diagnostic.Help,
                Note = string.IsNullOrWhiteSpace(diagnostic.Note) ? null : diagnostic.Note,
            };

            if (diagnostic.Labels is { Count: > 0 })
            {
                model.Labels = diagnostic.Labels
                    .Select(label => new LintJsonLabel
                    {
                        Message = label.Message ?? string.Empty,
                        File = label.Span.FileName ?? string.Empty,
                        Range = ToJsonRange(
                            label.Span.Line,
                            label.Span.Column,
                            label.Span.EndLine,
                            label.Span.EndColumn),
                    })
                    .ToList();
            }

            if (diagnostic.Suggestion is { } suggestion)
            {
                model.Suggestion = new LintJsonSuggestion
                {
                    File = suggestion.Span.FileName ?? string.Empty,
                    Range = ToJsonRange(
                        suggestion.Span.Line,
                        suggestion.Span.Column,
                        suggestion.Span.EndLine,
                        suggestion.Span.EndColumn),
                    Replacement = suggestion.Replacement ?? string.Empty,
                    Description = string.IsNullOrWhiteSpace(suggestion.Description)
                        ? null
                        : suggestion.Description,
                };
            }

            return model;
        }

        private static LintJsonRange ToJsonRange(int line, int column, int? endLine, int? endColumn)
        {
            int startLine = Math.Max(0, line - 1);
            int startColumn = Math.Max(0, column);

            int resolvedEndLine;
            int resolvedEndColumn;
            if (endLine is null && endColumn is null)
            {
                resolvedEndLine = startLine;
                resolvedEndColumn = startColumn;
            }
            else
            {
                resolvedEndLine = Math.Max(0, (endLine ?? line) - 1);
                resolvedEndColumn = Math.Max(0, endColumn ?? column);
            }

            return new LintJsonRange
            {
                Start = new LintJsonPosition { Line = startLine, Column = startColumn },
                End = new LintJsonPosition { Line = resolvedEndLine, Column = resolvedEndColumn },
            };
        }

        private static int ResolveFilesChecked(CompilationResult? context)
        {
            if (context is null)
            {
                return 0;
            }

            if (context.SourceFileCount > 0)
            {
                return context.SourceFileCount;
            }

            return context.ParsedFiles?.Count ?? 0;
        }

        private static long ToWholeMilliseconds(TimeSpan duration)
            => (long)Math.Round(duration.TotalMilliseconds, MidpointRounding.AwayFromZero);

        private static string GetToolVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version is null)
            {
                return "unknown";
            }

            // Match the project <Version> (e.g. 805.0.0), not the 4-part assembly revision.
            return version.Build >= 0
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : $"{version.Major}.{version.Minor}";
        }

        #region JSON model types

        private sealed class LintJsonOutput
        {
            [JsonPropertyName("version")]
            public string Version { get; set; } = SchemaVersion;

            [JsonPropertyName("tool")]
            public LintJsonTool Tool { get; set; } = new();

            [JsonPropertyName("diagnostics")]
            public List<LintJsonDiagnostic> Diagnostics { get; set; } = new();

            [JsonPropertyName("summary")]
            public LintJsonSummary Summary { get; set; } = new();
        }

        private sealed class LintJsonTool
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = ToolName;

            [JsonPropertyName("version")]
            public string Version { get; set; } = string.Empty;
        }

        private sealed class LintJsonDiagnostic
        {
            [JsonPropertyName("severity")]
            public string Severity { get; set; } = string.Empty;

            [JsonPropertyName("code")]
            public string Code { get; set; } = string.Empty;

            [JsonPropertyName("message")]
            public string Message { get; set; } = string.Empty;

            [JsonPropertyName("file")]
            public string File { get; set; } = string.Empty;

            [JsonPropertyName("range")]
            public LintJsonRange Range { get; set; } = new();

            [JsonPropertyName("labels")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<LintJsonLabel>? Labels { get; set; }

            [JsonPropertyName("help")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Help { get; set; }

            [JsonPropertyName("note")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Note { get; set; }

            [JsonPropertyName("suggestion")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public LintJsonSuggestion? Suggestion { get; set; }
        }

        private sealed class LintJsonLabel
        {
            [JsonPropertyName("message")]
            public string Message { get; set; } = string.Empty;

            [JsonPropertyName("file")]
            public string File { get; set; } = string.Empty;

            [JsonPropertyName("range")]
            public LintJsonRange Range { get; set; } = new();
        }

        private sealed class LintJsonSuggestion
        {
            [JsonPropertyName("file")]
            public string File { get; set; } = string.Empty;

            [JsonPropertyName("range")]
            public LintJsonRange Range { get; set; } = new();

            [JsonPropertyName("replacement")]
            public string Replacement { get; set; } = string.Empty;

            [JsonPropertyName("description")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Description { get; set; }
        }

        private sealed class LintJsonRange
        {
            [JsonPropertyName("start")]
            public LintJsonPosition Start { get; set; } = new();

            [JsonPropertyName("end")]
            public LintJsonPosition End { get; set; } = new();
        }

        private sealed class LintJsonPosition
        {
            [JsonPropertyName("line")]
            public int Line { get; set; }

            [JsonPropertyName("column")]
            public int Column { get; set; }
        }

        private sealed class LintJsonSummary
        {
            [JsonPropertyName("filesChecked")]
            public int FilesChecked { get; set; }

            /// <summary>
            /// Single-file lint target path (present only when <c>--file</c> was used).
            /// </summary>
            [JsonPropertyName("file")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? File { get; set; }

            [JsonPropertyName("errorCount")]
            public int ErrorCount { get; set; }

            [JsonPropertyName("warningCount")]
            public int WarningCount { get; set; }

            [JsonPropertyName("infoCount")]
            public int InfoCount { get; set; }

            [JsonPropertyName("durations")]
            public LintJsonDurations Durations { get; set; } = new();
        }

        private sealed class LintJsonDurations
        {
            [JsonPropertyName("parseMs")]
            public long ParseMs { get; set; }

            [JsonPropertyName("bindMs")]
            public long BindMs { get; set; }

            [JsonPropertyName("checkMs")]
            public long CheckMs { get; set; }

            [JsonPropertyName("totalMs")]
            public long TotalMs { get; set; }
        }

        #endregion
    }
}
