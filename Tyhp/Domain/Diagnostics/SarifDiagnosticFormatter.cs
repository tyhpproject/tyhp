using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Tyhp.Domain.Services;

namespace Tyhp.Domain.Diagnostics
{
    /// <summary>
    /// Diagnostic formatter that emits a single SARIF v2.1.0 document for CI integration
    /// when <c>--format=sarif</c> is selected (GitHub Code Scanning, Azure DevOps, etc.).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Output is one complete SARIF log object. Schema reference:
    /// <c>https://raw.githubusercontent.com/oasis-tcs/sarif-spec/main/sarif-2.1/schema/sarif-schema-2.1.0.json</c>
    /// </para>
    /// <code>
    /// {
    ///   "$schema": "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/main/sarif-2.1/schema/sarif-schema-2.1.0.json",
    ///   "version": "2.1.0",
    ///   "runs": [
    ///     {
    ///       "tool": {
    ///         "driver": {
    ///           "name": "tyhp",
    ///           "version": "805.0.0",
    ///           "informationUri": "https://tyhp.dev",
    ///           "rules": [
    ///             {
    ///               "id": "TYHP4002",
    ///               "shortDescription": { "text": "… cannot have multiple visibility modifiers" },
    ///               "defaultConfiguration": { "level": "error" }
    ///             }
    ///           ]
    ///         }
    ///       },
    ///       "results": [
    ///         {
    ///           "ruleId": "TYHP4002",
    ///           "level": "error",
    ///           "message": { "text": "…" },
    ///           "locations": [
    ///             {
    ///               "physicalLocation": {
    ///                 "artifactLocation": { "uri": "src/Models/User.tyhp" },
    ///                 "region": {
    ///                   "startLine": 42,
    ///                   "startColumn": 9,
    ///                   "endLine": 42,
    ///                   "endColumn": 26
    ///                 }
    ///               }
    ///             }
    ///           ]
    ///         }
    ///       ]
    ///     }
    ///   ]
    /// }
    /// </code>
    /// <para>
    /// <b>Coordinates:</b> Internal diagnostics use 1-based line / 0-based column.
    /// SARIF requires <b>1-based line</b> and <b>1-based column</b>, so columns are emitted as
    /// <c>column + 1</c>. When <see cref="IDiagnostic.EndLine"/> and
    /// <see cref="IDiagnostic.EndColumn"/> are both null, <c>end*</c> equals <c>start*</c>.
    /// </para>
    /// <para>
    /// Diagnostics are buffered in <see cref="Format"/> and the complete document is written
    /// only from <see cref="FormatSummary"/> so stdout stays clean SARIF with no interleaved text.
    /// File URIs are project-root-relative (forward slashes).
    /// </para>
    /// </remarks>
    public class SarifDiagnosticFormatter : IDiagnosticFormatter
    {
        private const string SarifVersion = "2.1.0";
        private const string SchemaUri =
            "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/main/sarif-2.1/schema/sarif-schema-2.1.0.json";
        private const string ToolName = "tyhp";
        private const string ToolInformationUri = "https://tyhp.dev";

        // A backtick span holding a placeholder is dropped whole (`{0}`, `${0}`, `tyhp/php-{0}`);
        // removing just the placeholder would strand the surrounding backticks in the description.
        private static readonly Regex BacktickedPlaceholderRegex = new(
            @"`[^`\r\n]*\{[0-9]+(?::[^}]*)?\}[^`\r\n]*`",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex FormatPlaceholderRegex = new(
            @"['""]?\{[0-9]+(?::[^}]*)?\}['""]?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly TextWriter _writer;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly List<BufferedDiagnostic> _diagnostics = new();

        /// <summary>
        /// Initializes a new instance that writes compact SARIF to console standard output (UTF-8).
        /// </summary>
        public SarifDiagnosticFormatter()
            : this(CreateUtf8Writer(), prettyPrint: false)
        {
        }

        /// <summary>
        /// Initializes a new instance that writes to the specified <see cref="TextWriter"/>.
        /// </summary>
        /// <param name="writer">Destination writer (UTF-8 recommended for stdout).</param>
        /// <param name="prettyPrint">When true, emits indented JSON; default is compact.</param>
        public SarifDiagnosticFormatter(TextWriter writer, bool prettyPrint = false)
        {
            this._writer = writer ?? throw new ArgumentNullException(nameof(writer));
            this._jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = prettyPrint,
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
                this._diagnostics.Add(new BufferedDiagnostic(diagnostic));
            }
            catch (Exception ex)
            {
                // stderr only — never write formatter failures to stdout (would corrupt SARIF)
                Console.Error.WriteLine(
                    CLI.Message.Localize("CLI_FormatDiagnosticFailed", diagnostic.Code, ex.Message));
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Serializes the complete SARIF v2.1.0 document (rules + results) to the output stream.
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
                var rulesById = new Dictionary<string, SarifRule>(StringComparer.Ordinal);
                var results = new List<SarifResult>(this._diagnostics.Count);

                foreach (var buffered in this._diagnostics)
                {
                    var diagnostic = buffered.Diagnostic;
                    try
                    {
                        var ruleId = FormatRuleId(diagnostic.Code);
                        var level = ToSarifLevel(diagnostic.Severity);

                        if (!rulesById.TryGetValue(ruleId, out var existingRule))
                        {
                            rulesById[ruleId] = new SarifRule
                            {
                                Id = ruleId,
                                ShortDescription = new SarifMessage
                                {
                                    Text = BuildShortDescription(diagnostic),
                                },
                                DefaultConfiguration = new SarifReportingConfiguration
                                {
                                    Level = level,
                                },
                            };
                        }
                        else
                        {
                            // Same MessageCode can surface at different severities; keep the highest.
                            existingRule.DefaultConfiguration.Level = MoreSevereLevel(
                                existingRule.DefaultConfiguration.Level,
                                level);
                        }

                        results.Add(ToSarifResult(diagnostic, ruleId, level));
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            CLI.Message.Localize("CLI_FormatDiagnosticFailed", diagnostic.Code, ex.Message));
                    }
                }

                var output = new SarifLog
                {
                    Schema = SchemaUri,
                    Version = SarifVersion,
                    Runs =
                    [
                        new SarifRun
                        {
                            Tool = new SarifTool
                            {
                                Driver = new SarifToolDriver
                                {
                                    Name = ToolName,
                                    Version = GetToolVersion(),
                                    InformationUri = ToolInformationUri,
                                    Rules = rulesById.Values.ToList(),
                                },
                            },
                            Results = results,
                        },
                    ],
                };

                var json = JsonSerializer.Serialize(output, this._jsonOptions);
                this._writer.WriteLine(json);
                this._writer.Flush();
                this._diagnostics.Clear();
            }
            catch (Exception ex)
            {
                // stderr only — never write formatter failures to stdout (would corrupt SARIF)
                Console.Error.WriteLine(CLI.Message.Localize("CLI_FormatSummaryFailed", ex.Message));
            }
        }

        private static SarifResult ToSarifResult(IDiagnostic diagnostic, string ruleId, string level)
        {
            // SARIF: 1-based line, 1-based column. Internal: 1-based line, 0-based column.
            var result = new SarifResult
            {
                RuleId = ruleId,
                Level = level,
                Message = new SarifMessage
                {
                    Text = diagnostic.Message ?? string.Empty,
                },
            };

            // GitHub Code Scanning rejects empty artifactLocation.uri values. Config/CLI diagnostics
            // often have no file — omit locations entirely rather than emitting uri:"".
            var uri = ToArtifactUri(diagnostic.FileName);
            if (!string.IsNullOrEmpty(uri))
            {
                result.Locations =
                [
                    new SarifLocation
                    {
                        PhysicalLocation = new SarifPhysicalLocation
                        {
                            ArtifactLocation = new SarifArtifactLocation { Uri = uri },
                            Region = ToSarifRegion(
                                diagnostic.Line,
                                diagnostic.Column,
                                diagnostic.EndLine,
                                diagnostic.EndColumn),
                        },
                    },
                ];
            }

            if (diagnostic.Labels is { Count: > 0 })
            {
                var related = new List<SarifLocation>();
                var nextId = 1;
                foreach (var label in diagnostic.Labels)
                {
                    var labelUri = ToArtifactUri(label.Span.FileName);
                    if (string.IsNullOrEmpty(labelUri))
                    {
                        continue;
                    }

                    related.Add(new SarifLocation
                    {
                        Id = nextId++,
                        Message = new SarifMessage { Text = label.Message ?? string.Empty },
                        PhysicalLocation = new SarifPhysicalLocation
                        {
                            ArtifactLocation = new SarifArtifactLocation { Uri = labelUri },
                            Region = ToSarifRegion(
                                label.Span.Line,
                                label.Span.Column,
                                label.Span.EndLine,
                                label.Span.EndColumn),
                        },
                    });
                }

                if (related.Count > 0)
                {
                    result.RelatedLocations = related;
                }
            }

            if (diagnostic.Suggestion is { } suggestion)
            {
                var suggestionUri = ToArtifactUri(suggestion.Span.FileName);
                if (!string.IsNullOrEmpty(suggestionUri))
                {
                    result.Fixes =
                    [
                        new SarifFix
                        {
                            Description = string.IsNullOrWhiteSpace(suggestion.Description)
                                ? null
                                : new SarifMessage { Text = suggestion.Description },
                            ArtifactChanges =
                            [
                                new SarifArtifactChange
                                {
                                    ArtifactLocation = new SarifArtifactLocation { Uri = suggestionUri },
                                    Replacements =
                                    [
                                        new SarifReplacement
                                        {
                                            DeletedRegion = ToSarifRegion(
                                                suggestion.Span.Line,
                                                suggestion.Span.Column,
                                                suggestion.Span.EndLine,
                                                suggestion.Span.EndColumn),
                                            InsertedContent = new SarifMessage
                                            {
                                                Text = suggestion.Replacement ?? string.Empty,
                                            },
                                        },
                                    ],
                                },
                            ],
                        },
                    ];
                }
            }

            if (!string.IsNullOrWhiteSpace(diagnostic.Help) || !string.IsNullOrWhiteSpace(diagnostic.Note))
            {
                result.Properties = new SarifResultProperties
                {
                    Help = string.IsNullOrWhiteSpace(diagnostic.Help) ? null : diagnostic.Help,
                    Note = string.IsNullOrWhiteSpace(diagnostic.Note) ? null : diagnostic.Note,
                };
            }

            return result;
        }

        private static SarifRegion ToSarifRegion(int line, int column, int? endLine, int? endColumn)
        {
            int startLine = Math.Max(1, line);
            int startColumn = Math.Max(1, column + 1);

            int resolvedEndLine;
            int resolvedEndColumn;
            if (endLine is null && endColumn is null)
            {
                resolvedEndLine = startLine;
                resolvedEndColumn = startColumn;
            }
            else
            {
                resolvedEndLine = Math.Max(1, endLine ?? line);
                resolvedEndColumn = Math.Max(1, (endColumn ?? column) + 1);
            }

            return new SarifRegion
            {
                StartLine = startLine,
                StartColumn = startColumn,
                EndLine = resolvedEndLine,
                EndColumn = resolvedEndColumn,
            };
        }

        private static string ToArtifactUri(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName == "_")
            {
                return string.Empty;
            }

            // Compiler diagnostics usually already store project-relative paths. Re-running
            // GetRelativePath on those would Path.GetFullPath against CWD and can invent wrong
            // uris when the process cwd differs from the project root — only relativize rooted paths.
            string path;
            if (Path.IsPathRooted(fileName))
            {
                try
                {
                    path = AstCacheService.GetRelativePath(fileName);
                }
                catch
                {
                    path = fileName;
                }
            }
            else
            {
                path = fileName;
            }

            return path.Replace('\\', '/');
        }

        private static string BuildShortDescription(IDiagnostic diagnostic)
        {
            var template = GetLocalizedTemplate(diagnostic.Severity, (int)diagnostic.Code);
            return StripFormatPlaceholders(template);
        }

        private static string GetLocalizedTemplate(DiagnosticSeverity severity, int code)
        {
            // Prefer the severity-specific resource, then fall back so a missing WARNING_/INFO_
            // key still yields a useful rule shortDescription from ERROR_ (or vice versa).
            string[] prefixes = severity switch
            {
                DiagnosticSeverity.Error => ["ERROR_TYHP", "WARNING_TYHP", "INFO_TYHP"],
                DiagnosticSeverity.Warning => ["WARNING_TYHP", "ERROR_TYHP", "INFO_TYHP"],
                _ => ["INFO_TYHP", "WARNING_TYHP", "ERROR_TYHP"],
            };

            foreach (var prefix in prefixes)
            {
                var key = prefix + code.ToString();
                var value = CLI.Message.LocalizeRaw(key);
                if (!string.Equals(value, key, StringComparison.Ordinal))
                {
                    return value;
                }
            }

            return $"TYHP{code:D4}";
        }

        private static string MoreSevereLevel(string existing, string candidate)
        {
            return SeverityRank(candidate) > SeverityRank(existing) ? candidate : existing;
        }

        private static int SeverityRank(string level)
            => level switch
            {
                "error" => 3,
                "warning" => 2,
                "note" => 1,
                _ => 0,
            };

        private static string StripFormatPlaceholders(string template)
        {
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            var stripped = BacktickedPlaceholderRegex.Replace(template, string.Empty);
            stripped = FormatPlaceholderRegex.Replace(stripped, string.Empty);
            stripped = Regex.Replace(stripped, @"\s{2,}", " ");
            stripped = Regex.Replace(stripped, @"\s+([,;:])", "$1");
            stripped = stripped.Replace("''", string.Empty, StringComparison.Ordinal)
                .Replace("\"\"", string.Empty, StringComparison.Ordinal);
            return stripped.Trim().Trim(':', '-', '—', ' ');
        }

        private static string FormatRuleId(Exceptions.MessageCode code)
            => $"TYHP{(int)code:D4}";

        private static string ToSarifLevel(DiagnosticSeverity severity)
            => severity switch
            {
                DiagnosticSeverity.Error => "error",
                DiagnosticSeverity.Warning => "warning",
                DiagnosticSeverity.Info => "note",
                DiagnosticSeverity.Hint => "note",
                _ => "none",
            };

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

        private readonly record struct BufferedDiagnostic(IDiagnostic Diagnostic);

        #region SARIF model types

        private sealed class SarifLog
        {
            [JsonPropertyName("$schema")]
            public string Schema { get; set; } = SchemaUri;

            [JsonPropertyName("version")]
            public string Version { get; set; } = SarifVersion;

            [JsonPropertyName("runs")]
            public List<SarifRun> Runs { get; set; } = new();
        }

        private sealed class SarifRun
        {
            [JsonPropertyName("tool")]
            public SarifTool Tool { get; set; } = new();

            [JsonPropertyName("results")]
            public List<SarifResult> Results { get; set; } = new();
        }

        private sealed class SarifTool
        {
            [JsonPropertyName("driver")]
            public SarifToolDriver Driver { get; set; } = new();
        }

        private sealed class SarifToolDriver
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = ToolName;

            [JsonPropertyName("version")]
            public string Version { get; set; } = string.Empty;

            [JsonPropertyName("informationUri")]
            public string InformationUri { get; set; } = ToolInformationUri;

            [JsonPropertyName("rules")]
            public List<SarifRule> Rules { get; set; } = new();
        }

        private sealed class SarifRule
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("shortDescription")]
            public SarifMessage ShortDescription { get; set; } = new();

            [JsonPropertyName("defaultConfiguration")]
            public SarifReportingConfiguration DefaultConfiguration { get; set; } = new();
        }

        private sealed class SarifReportingConfiguration
        {
            [JsonPropertyName("level")]
            public string Level { get; set; } = "warning";
        }

        private sealed class SarifResult
        {
            [JsonPropertyName("ruleId")]
            public string RuleId { get; set; } = string.Empty;

            [JsonPropertyName("level")]
            public string Level { get; set; } = string.Empty;

            [JsonPropertyName("message")]
            public SarifMessage Message { get; set; } = new();

            [JsonPropertyName("locations")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<SarifLocation>? Locations { get; set; }

            [JsonPropertyName("relatedLocations")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<SarifLocation>? RelatedLocations { get; set; }

            [JsonPropertyName("fixes")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<SarifFix>? Fixes { get; set; }

            [JsonPropertyName("properties")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public SarifResultProperties? Properties { get; set; }
        }

        private sealed class SarifResultProperties
        {
            [JsonPropertyName("help")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Help { get; set; }

            [JsonPropertyName("note")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Note { get; set; }
        }

        private sealed class SarifFix
        {
            [JsonPropertyName("description")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public SarifMessage? Description { get; set; }

            [JsonPropertyName("artifactChanges")]
            public List<SarifArtifactChange> ArtifactChanges { get; set; } = new();
        }

        private sealed class SarifArtifactChange
        {
            [JsonPropertyName("artifactLocation")]
            public SarifArtifactLocation ArtifactLocation { get; set; } = new();

            [JsonPropertyName("replacements")]
            public List<SarifReplacement> Replacements { get; set; } = new();
        }

        private sealed class SarifReplacement
        {
            [JsonPropertyName("deletedRegion")]
            public SarifRegion DeletedRegion { get; set; } = new();

            [JsonPropertyName("insertedContent")]
            public SarifMessage InsertedContent { get; set; } = new();
        }

        private sealed class SarifLocation
        {
            [JsonPropertyName("id")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public int Id { get; set; }

            [JsonPropertyName("message")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public SarifMessage? Message { get; set; }

            [JsonPropertyName("physicalLocation")]
            public SarifPhysicalLocation PhysicalLocation { get; set; } = new();
        }

        private sealed class SarifPhysicalLocation
        {
            [JsonPropertyName("artifactLocation")]
            public SarifArtifactLocation ArtifactLocation { get; set; } = new();

            [JsonPropertyName("region")]
            public SarifRegion Region { get; set; } = new();
        }

        private sealed class SarifArtifactLocation
        {
            [JsonPropertyName("uri")]
            public string Uri { get; set; } = string.Empty;
        }

        private sealed class SarifRegion
        {
            [JsonPropertyName("startLine")]
            public int StartLine { get; set; }

            [JsonPropertyName("startColumn")]
            public int StartColumn { get; set; }

            [JsonPropertyName("endLine")]
            public int EndLine { get; set; }

            [JsonPropertyName("endColumn")]
            public int EndColumn { get; set; }
        }

        private sealed class SarifMessage
        {
            [JsonPropertyName("text")]
            public string Text { get; set; } = string.Empty;
        }

        #endregion
    }
}
