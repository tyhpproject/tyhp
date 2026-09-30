using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Enums;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.CLI
{
    /// <summary>
    /// CLI verb <c>overlay</c>: <c>create &lt;FQN&gt;</c> and <c>stamp</c> [<c>FQN</c>].
    /// </summary>
    public class OverlayAction : ActionRunnerBase
    {
        private readonly Project _project;
        private readonly PhpRuntimeManager _runtimeManager;

        internal IReadOnlyList<string>? Args { get; init; }

        internal CompilationResult? LastResult { get; private set; }

        public OverlayAction(Project project, PhpRuntimeManager? runtimeManager = null)
        {
            this._project = project ?? throw new ArgumentNullException(nameof(project));
            this._runtimeManager = runtimeManager ?? new PhpRuntimeManager();
        }

        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            var result = this.Run(cancellationToken);
            this.LastResult = result;
            this.Report(result);
            Environment.ExitCode = result.Success
                ? (int)ExitCode.Success
                : (int)ExitCode.GenericError;
            return result;
        }

        private CompilationResult Run(CancellationToken cancellationToken)
        {
            var result = new CompilationResult(this._project.SuppressedWarnings);
            var args = (this.Args ?? ActionConfigProvider.RemainingArgs)
                .Where(static a => !a.StartsWith('-'))
                .ToList();

            if (args.Count == 0)
            {
                DisplayHelp.OverlayHelp();
                return result;
            }

            var verb = args[0];
            if (string.Equals(verb, "create", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Count < 2)
                {
                    this.Fail(result, MessageCode.OverlayInvalidArguments, "create");
                    return result;
                }

                this.CreateOverlay(result, args[1], cancellationToken);
                return result;
            }

            if (string.Equals(verb, "stamp", StringComparison.OrdinalIgnoreCase))
            {
                this.StampOverlays(result, args.Count > 1 ? args[1] : null, cancellationToken);
                return result;
            }

            this.Fail(result, MessageCode.OverlayInvalidArguments, verb);
            return result;
        }

        private void CreateOverlay(CompilationResult result, string fqn, CancellationToken cancellationToken)
        {
            var bound = this.BindProject(result, applyOverlays: true, cancellationToken);
            if (bound == null)
            {
                return;
            }

            var symbol = FindSymbolByFqn(bound, fqn);
            if (symbol == null)
            {
                this.Fail(result, MessageCode.OverlayTargetNotFound, fqn);
                return;
            }

            if (symbol is ObjectDeclarationSymbol { IsExtern: true })
            {
                this.Fail(result, MessageCode.OverlayCreateOnExtern, fqn);
                return;
            }

            if (!TryCopyDeclaration(symbol, out var declarationText, out var sourcePath))
            {
                declarationText = ReconstructDeclaration(symbol);
                sourcePath = symbol.SourceFile ?? "";
            }

            if (string.IsNullOrWhiteSpace(declarationText))
            {
                this.Fail(result, MessageCode.OverlayWriteFailed, fqn);
                return;
            }

            var (overlayDir, manifestPath) = ResolveOverlayDestination(symbol.SourceFile);
            try
            {
                Directory.CreateDirectory(overlayDir);
                var fileName = SanitizeFileName(symbol.Name) + ".tyhpdef";
                var overlayPath = Path.Combine(overlayDir, fileName);
                if (File.Exists(overlayPath))
                {
                    var existing = File.ReadAllText(overlayPath);
                    if (!existing.EndsWith('\n'))
                    {
                        existing += Environment.NewLine;
                    }

                    File.WriteAllText(overlayPath, existing + Environment.NewLine + declarationText.Trim() + Environment.NewLine);
                }
                else
                {
                    File.WriteAllText(
                        overlayPath,
                        "<?tyhpdef" + Environment.NewLine + declarationText.Trim() + Environment.NewLine);
                }

                if (!string.IsNullOrEmpty(manifestPath)
                    && !TyhpdefOverlayManifestEditor.EnsureHandWrittenOverlayGlob(manifestPath, out var manifestError))
                {
                    result.Diagnostics.AddError(
                        MessageCode.OverlayManifestUpdateFailed,
                        manifestPath,
                        0,
                        0,
                        manifestError ?? manifestPath);
                    return;
                }

                if (!this._project.BeQuiet)
                {
                    Message.Success("CLI_OverlayCreated", overlayPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Diagnostics.AddError(MessageCode.OverlayWriteFailed, sourcePath, 0, 0, ex.Message);
            }
        }

        private void StampOverlays(CompilationResult result, string? fqn, CancellationToken cancellationToken)
        {
            var overlayPaths = this.DiscoverOverlayPaths(result);
            var updated = 0;
            foreach (var minor in PhpRuntimeVersion.SupportedMinors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!this.TryGetStampRuntime(minor, result, cancellationToken, out var runtime))
                {
                    continue;
                }

                var bound = this.BindProject(result, applyOverlays: false, cancellationToken, runtime.Version);
                if (bound == null)
                {
                    continue;
                }

                var stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                CollectStamps(bound, stamps);
                foreach (var path in overlayPaths)
                {
                    updated += TyhpdefOverlayStampRewriter.Apply(path, stamps, fqn, runtime.Version);
                }
            }

            if (!this._project.BeQuiet)
            {
                Message.Success("CLI_OverlayStamped", updated);
            }
        }

        /// <summary>
        /// Managed runtime for <paramref name="minor"/>, probed the same way as
        /// <c>generate_tyhpdef</c>. A missing runtime or one that reports a different minor
        /// fails this pass; the caller does not stamp it with another PHP version.
        /// </summary>
        private bool TryGetStampRuntime(
            string minor,
            CompilationResult result,
            CancellationToken cancellationToken,
            out PhpRuntimeInfo runtime)
        {
            var probe = new DiagnosticBag();
            var info = this._runtimeManager.Ensure(
                minor,
                StampRuntimeOptions(),
                probe,
                cancellationToken);
            var reportedMinor = info == null ? null : PhpRuntimeVersion.ToMinor(info.Version);
            if (info == null || !string.Equals(reportedMinor, minor, StringComparison.Ordinal))
            {
                this.Fail(result, MessageCode.OverlayPhpRuntimeUnavailable, minor);
                runtime = null!;
                return false;
            }

            runtime = info;
            return true;
        }

        private static TyhpdefGenerationOptions StampRuntimeOptions()
            => new()
            {
                Mode = TyhpdefGenerationMode.PhpExtension,
                PreferPhpRuntime = true,
            };

        private List<string> DiscoverOverlayPaths(CompilationResult result)
        {
            var overlayOptions = CompilationOptions.FromProject(this._project, o =>
            {
                o.EnableAstCache = false;
                o.SkipChecking = true;
                o.ApplyTyhpdefOverlays = true;
            });
            var overlaySources = Tyhpdef.GetSourceFiles(result.Diagnostics, overlayOptions);
            var paths = new List<string>();
            foreach (var source in overlaySources.Where(static s => s.IsOverlay))
            {
                var path = ResolveExistingOverlayPath(source.Ast);
                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        private GlobalScope? BindProject(
            CompilationResult result,
            bool applyOverlays,
            CancellationToken cancellationToken,
            string? phpVersion = null)
        {
            var files = this._project.GetProjectSourceFiles().ToList();
            string? tempFile = null;
            if (files.Count == 0)
            {
                tempFile = Path.Combine(this._project.GetProjectPath(), ".tyhp-overlay-bind.tyhp");
                File.WriteAllText(tempFile, "<?tyhp" + Environment.NewLine);
                files.Add(tempFile);
            }

            try
            {
                using var compilation = new CompilationService();
                var options = CompilationOptions.FromProject(this._project, o =>
                {
                    o.EnableAstCache = false;
                    o.SkipChecking = true;
                    o.ApplyTyhpdefOverlays = applyOverlays;
                    if (!string.IsNullOrWhiteSpace(phpVersion))
                    {
                        o.PhpVersion = phpVersion;
                        o.PhpVersionWasDefaulted = false;
                        o.Checker.PhpVersionWasDefaulted = false;
                    }
                });
                var compiled = compilation.ParseFiles(files, options, cancellationToken);
                foreach (var diagnostic in compiled.Diagnostics)
                {
                    result.Diagnostics.Add(diagnostic);
                }

                if (compiled.GlobalScope == null)
                {
                    return null;
                }

                return compiled.GlobalScope;
            }
            finally
            {
                if (tempFile != null)
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        private void Fail(CompilationResult result, MessageCode code, params object[] args)
        {
            result.Diagnostics.AddError(code, "overlay", 0, 0, args);
        }

        private void Report(CompilationResult result)
        {
            var formatter = new ConsoleDiagnosticFormatter(this._project.BeQuiet);
            foreach (var diagnostic in result.Diagnostics.All)
            {
                formatter.Format(diagnostic);
            }
        }

        private (string OverlayDir, string? ManifestPath) ResolveOverlayDestination(string sourceFile)
        {
            var projectRoot = this._project.GetProjectPath();
            if (!string.IsNullOrEmpty(sourceFile) && File.Exists(sourceFile))
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(sourceFile));
                while (!string.IsNullOrEmpty(dir))
                {
                    var packageManifest = Path.Combine(dir, TyhpdefOverlayApplier.PackageManifestFileName);
                    if (ComposerExtraTyhpPackageManifest.HasPackageObject(packageManifest))
                    {
                        return (Path.Combine(dir, "_tyhpdef", "overlays"), packageManifest);
                    }

                    if (string.Equals(dir, projectRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    dir = Path.GetDirectoryName(dir);
                }
            }

            foreach (var includePattern in this._project.TyhpdefIncludePaths)
            {
                var manifestPath = ResolvePackageManifestInclude(projectRoot, includePattern);
                if (manifestPath != null)
                {
                    var packageRoot = Path.GetDirectoryName(manifestPath);
                    if (!string.IsNullOrEmpty(packageRoot))
                    {
                        return (Path.Combine(packageRoot, "_tyhpdef", "overlays"), manifestPath);
                    }
                }
            }

            var tyhpJson = Path.Combine(projectRoot, "tyhp.json");
            return (
                Path.Combine(projectRoot, "_tyhpdef", "overlays"),
                File.Exists(tyhpJson) ? tyhpJson : null);
        }

        private static string? ResolvePackageManifestInclude(string projectRoot, string includePattern)
        {
            if (string.IsNullOrWhiteSpace(includePattern)
                || !ComposerExtraTyhpPackageManifest.PatternMayMatchComposerJson(includePattern)
                || !includePattern.Replace('\\', '/').EndsWith(
                    ComposerExtraTyhpPackageManifest.ComposerJsonFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var combined = Path.IsPathRooted(includePattern)
                ? includePattern
                : Path.Combine(projectRoot, includePattern);
            var full = Path.GetFullPath(combined);
            return ComposerExtraTyhpPackageManifest.HasPackageObject(full) ? full : null;
        }

        private static string? ResolveExistingOverlayPath(SrcFileAst ast)
        {
            if (!string.IsNullOrWhiteSpace(ast.Identifier)
                && ast.Identifier != "_"
                && File.Exists(ast.Identifier))
            {
                return ast.Identifier;
            }

            if (!string.IsNullOrWhiteSpace(ast.FileName) && File.Exists(ast.FileName))
            {
                return ast.FileName;
            }

            return null;
        }

        private static bool TryCopyDeclaration(IBaseSymbol symbol, out string text, out string sourcePath)
        {
            text = "";
            sourcePath = symbol.SourceFile ?? "";
            if (symbol is not BaseSymbol baseSymbol
                || baseSymbol.DeclaringAstNode == null
                || string.IsNullOrEmpty(sourcePath)
                || !File.Exists(sourcePath))
            {
                return false;
            }

            var node = baseSymbol.DeclaringAstNode;
            if (node.StartIndex < 0 || node.EndIndex < node.StartIndex)
            {
                return false;
            }

            var fileText = File.ReadAllText(sourcePath);
            if (node.StartIndex >= fileText.Length)
            {
                return false;
            }

            var end = Math.Min(node.EndIndex, fileText.Length - 1);
            if (end < node.StartIndex)
            {
                return false;
            }

            text = fileText[node.StartIndex..(end + 1)];
            return !string.IsNullOrWhiteSpace(text);
        }

        private static string ReconstructDeclaration(IBaseSymbol symbol)
        {
            if (symbol is ObjectDeclarationSymbol type)
            {
                var builder = new System.Text.StringBuilder();
                builder.Append(TyhpdefOverlayStamp.Spell(type));
                builder.Append(" {");
                builder.Append(Environment.NewLine);
                foreach (var member in type.EnumerateMembersAndConstants())
                {
                    builder.Append("    ");
                    if (member is ObjectMethodSymbol)
                    {
                        builder.Append("public ");
                    }

                    builder.Append(TyhpdefOverlayStamp.Spell(member));
                    builder.Append(';');
                    builder.Append(Environment.NewLine);
                }

                builder.Append('}');
                return builder.ToString();
            }

            return TyhpdefOverlayStamp.Spell(symbol) + ";";
        }

        private static IBaseSymbol? FindSymbolByFqn(IBaseScope scope, string fqn)
        {
            var wanted = TyhpdefOverlayStamp.NormalizeFqn(fqn);
            IBaseSymbol? found = null;
            Walk(scope, symbol =>
            {
                if (found != null || symbol is not BaseSymbol baseSymbol)
                {
                    return;
                }

                if (string.Equals(
                    TyhpdefOverlayStamp.NormalizeFqn(baseSymbol.FullyQualifiedName),
                    wanted,
                    StringComparison.OrdinalIgnoreCase)
                    || string.Equals(baseSymbol.Name, fqn.TrimStart('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    if (found == null || IsPreferredOverlaySource(symbol, found))
                    {
                        found = symbol;
                    }
                }
            });
            return found;
        }

        private static bool IsPreferredOverlaySource(IBaseSymbol candidate, IBaseSymbol current)
        {
            var candidateFile = candidate.SourceFile ?? "";
            var currentFile = current.SourceFile ?? "";
            var candidateOnDisk = !string.IsNullOrEmpty(candidateFile) && File.Exists(candidateFile);
            var currentOnDisk = !string.IsNullOrEmpty(currentFile) && File.Exists(currentFile);
            if (candidateOnDisk != currentOnDisk)
            {
                return candidateOnDisk;
            }

            var candidateEmbedded = candidateFile.Contains("<tyhpdef:embedded", StringComparison.OrdinalIgnoreCase);
            var currentEmbedded = currentFile.Contains("<tyhpdef:embedded", StringComparison.OrdinalIgnoreCase);
            return currentEmbedded && !candidateEmbedded;
        }

        private static void CollectStamps(IBaseScope scope, Dictionary<string, string> stamps)
        {
            Walk(scope, symbol => TyhpdefOverlayStamp.Record(symbol, stamps));
        }

        private static void Walk(IBaseScope scope, Action<IBaseSymbol> visit)
        {
            foreach (var symbol in scope.GetAllChildSymbols())
            {
                visit(symbol);
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                Walk(child, visit);
            }
        }

        private static string SanitizeFileName(string name)
        {
            var sanitized = name.Trim().TrimStart('\\');
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(c, '_');
            }

            return string.IsNullOrEmpty(sanitized) ? "overlay" : sanitized;
        }
    }
}
